using System.Globalization;
using System.Text;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Broadcast;

public sealed record RunOptions(bool DryRun = false, bool Create = false);

/// <summary>
/// One morning's broadcast (spec §3): pull the world, back it up, catch it up, build the episode, push the episode and
/// then the world, then publish to each channel every episode of the last week not yet posted there. The world goes back
/// to the store only after the catch-up and the episode both succeeded, and each finished step is written to the
/// manifest at once, so a rerun repeats nothing that already happened.
/// </summary>
public sealed class DailyRun(BroadcastConfig config, IObjectStore root, IReadOnlyList<IEpisodeChannel> channels,
    Func<DateTimeOffset> clock, TextWriter log, RunOptions? options = null)
{
    /// <summary>Exit codes: published; nothing published (the world is untouched); the episode is out but a channel failed.</summary>
    public const int Ok = 0, Failed = 1, Partial = 2;

    /// <summary>How far back a failed post is still made up for.</summary>
    public const int MissedDays = 7;

    private readonly RunOptions _options = options ?? new RunOptions();
    private bool _worldSaved;

    public static string EpisodeKey(string date) => $"episodes/{date}/episode.json";

    public static string ScriptKey(string date) => $"episodes/{date}/script.txt";

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var store = new PrefixedStore(root, _options.DryRun ? config.StagingPrefix : config.LivePrefix);
        var date = config.DateAt(clock());
        log.WriteLine($"Broadcast for {date}{(_options.DryRun ? " (dry run)" : "")}, under {store.Prefix}.");

        Manifest manifest;
        Episode episode;
        _worldSaved = false;
        try
        {
            manifest = Manifest.FromBytes(await store.GetAsync(Manifest.Key, ct).ConfigureAwait(false));
            if (manifest.Day(date).Done(Manifest.EpisodeStep))
            {
                log.WriteLine("Today's episode is already built; the world is not touched again today.");
                episode = await StoredEpisodeAsync(store, date, ct).ConfigureAwait(false);
            }
            else
                episode = await MakeEpisodeAsync(store, manifest, date, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.WriteLine($"error: {e.Message}");
            log.WriteLine(_worldSaved
                ? "The world and the episode were saved, but not the manifest; the next run picks them up and posts them."
                : "Nothing was published, and the stored world is as it was.");
            return Failed;
        }

        // Days whose post failed are posted first, oldest first, so a missed morning is late but never lost.
        var earliest = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1 - MissedDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dates = manifest.Episodes
            .Where(d => d.Value.Done(Manifest.EpisodeStep) && string.CompareOrdinal(d.Key, earliest) >= 0 && string.CompareOrdinal(d.Key, date) <= 0)
            .Select(d => d.Key).ToList();
        var result = Ok;
        if (channels.Count == 0) log.WriteLine("No channel is configured: the episode is stored but posted nowhere.");
        foreach (var channel in channels)
        foreach (var day in dates)
        {
            if (manifest.Day(day).Done(channel.Name))
            {
                if (day == date) log.WriteLine($"{channel.Name}: already posted today, not posting again.");
                continue;
            }
            try
            {
                var told = day == date ? episode : await StoredEpisodeAsync(store, day, ct).ConfigureAwait(false);
                await channel.PublishAsync(told, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is ChannelException or IOException or InvalidOperationException && !ct.IsCancellationRequested)
            {
                log.WriteLine($"{channel.Name}: the episode of {day} was not posted: {e.Message} It will be tried again on the next run.");
                result = Partial;
                continue;
            }
            manifest.Day(day).Steps[channel.Name] = clock();
            try
            {
                await SaveAsync(store, manifest, ct).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                log.WriteLine($"error: {channel.Name} was posted, but the manifest could not be saved ({e.Message}); a rerun today would post again.");
                return Failed;
            }
            log.WriteLine(day == date ? $"{channel.Name}: posted." : $"{channel.Name}: posted the missed episode of {day}.");
        }

        await PruneBackupsAsync(store, date, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<Episode> MakeEpisodeAsync(PrefixedStore store, Manifest manifest, string date, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("fw-broadcast-");
        try
        {
            var worldPath = Path.Combine(work.FullName, "world.fw.db");
            var pulled = await store.GetAsync(config.WorldKey, ct).ConfigureAwait(false);
            var copiedFromLive = false;
            if (pulled is null && _options.DryRun)
            {
                pulled = await root.GetAsync(config.LivePrefix + config.WorldKey, ct).ConfigureAwait(false);
                copiedFromLive = pulled is not null;
                if (copiedFromLive) log.WriteLine("The staging world starts as a copy of the live one (the live world is only read).");
            }

            if (pulled is null)
            {
                if (!_options.Create)
                    throw new InvalidOperationException($"There is no world at {store.Prefix}{config.WorldKey}. " +
                                                        "Start the workflow by hand with 'create' to begin one; a scheduled run never does.");
                Fw("new", worldPath, "--seed", config.Seed.ToString(CultureInfo.InvariantCulture), "--size", config.Size);
            }
            else
            {
                await File.WriteAllBytesAsync(worldPath, pulled, ct).ConfigureAwait(false);
                var backup = BackupRetention.KeyFor(date);
                if (!(await store.ListAsync(backup, ct).ConfigureAwait(false)).Contains(backup))
                {
                    await store.PutAsync(backup, pulled, "application/octet-stream", ct).ConfigureAwait(false);
                    log.WriteLine($"Backed up the world to {store.Prefix}{backup}.");
                }
            }

            // The stored world says which day's episode it holds. A run that saved the episode and the world, then
            // stopped before the manifest, left that day unmarked: mark it now (its post follows), and if the day is
            // today, the world must not move again. (A copy of the live world holds a live episode, not a staging one.)
            if (pulled is not null && !copiedFromLive && BroadcastDateOf(worldPath) is { } held
                && !(manifest.Episodes.TryGetValue(held, out var heldDay) && heldDay.Done(Manifest.EpisodeStep)))
            {
                var stored = await StoredEpisodeAsync(store, held, ct).ConfigureAwait(false);
                manifest.Record(held, stored);
                manifest.Day(held).Steps[Manifest.EpisodeStep] = clock();
                log.WriteLine($"The episode of {held} was saved with the world by a run that stopped early; it is marked now.");
                if (held == date)
                {
                    await SaveAsync(store, manifest, ct).ConfigureAwait(false);
                    return stored;
                }
            }

            Fw("catchup", worldPath, "--quiet");
            var dir = Path.Combine(work.FullName, "episode");
            Fw("episode", worldPath, "--out", dir, "--date", date,
                "--stories", config.Stories.ToString(CultureInfo.InvariantCulture),
                "--max-characters", config.MaxCharacters.ToString(CultureInfo.InvariantCulture));
            var episode = Episode.FromJson(await File.ReadAllTextAsync(Path.Combine(dir, "episode.json"), ct).ConfigureAwait(false));
            var snapshot = Path.Combine(work.FullName, "upload.fw.db");
            Fw("snapshot", worldPath, snapshot);

            // The episode first, then the world that holds its marker: a world in the store always has its episode.
            await store.PutAsync(EpisodeKey(date), Encoding.UTF8.GetBytes(episode.ToJson()), "application/json", ct).ConfigureAwait(false);
            await store.PutAsync(ScriptKey(date), Encoding.UTF8.GetBytes(episode.Script), "text/plain; charset=utf-8", ct).ConfigureAwait(false);
            await store.PutAsync(config.WorldKey, await File.ReadAllBytesAsync(snapshot, ct).ConfigureAwait(false), "application/octet-stream", ct).ConfigureAwait(false);
            _worldSaved = true;
            log.WriteLine($"Saved the world and the episode to {store.Prefix}.");

            manifest.Record(date, episode);
            manifest.Day(date).Steps[Manifest.EpisodeStep] = clock();
            await SaveAsync(store, manifest, ct).ConfigureAwait(false);
            return episode;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            work.Delete(recursive: true);
        }
    }

    private static async Task<Episode> StoredEpisodeAsync(PrefixedStore store, string date, CancellationToken ct) =>
        await store.GetAsync(EpisodeKey(date), ct).ConfigureAwait(false) is { } bytes
            ? Episode.FromJson(Encoding.UTF8.GetString(bytes))
            : throw new InvalidOperationException($"The manifest says today's episode was built, but {store.Prefix}{EpisodeKey(date)} is missing.");

    private static string? BroadcastDateOf(string worldPath)
    {
        using var world = WorldStore.Open(worldPath);
        return world.Load().World.LastBroadcastDate;
    }

    private static Task SaveAsync(IObjectStore store, Manifest manifest, CancellationToken ct) =>
        store.PutAsync(Manifest.Key, manifest.ToBytes(), "application/json", ct);

    /// <summary>Runs one <c>fw</c> command in-process, its output indented in the log; a failure stops the run.</summary>
    private void Fw(params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, clock);
        foreach (var line in output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            log.WriteLine($"  fw {args[0]}: {line}");
        if (code != 0) throw new InvalidOperationException($"fw {args[0]} failed (exit code {code}).");
    }

    private async Task PruneBackupsAsync(IObjectStore store, string date, CancellationToken ct)
    {
        try
        {
            var keys = await store.ListAsync(BackupRetention.Prefix, ct).ConfigureAwait(false);
            foreach (var key in BackupRetention.ToDelete(keys, date, config.BackupDays))
                await store.DeleteAsync(key, ct).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            log.WriteLine($"warning: old backups could not be pruned ({e.Message}); the next run will try again.");
        }
    }
}
