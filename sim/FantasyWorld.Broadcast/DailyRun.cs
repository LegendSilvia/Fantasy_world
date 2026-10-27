using System.Globalization;
using System.Text;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Cli;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Publishing;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Broadcast;

/// <param name="UploadPrivate">On a dry run, upload the staging video to YouTube as private (to try the upload by hand).</param>
/// <param name="PreparePlan">
/// Prepare only (sub-project 7 F2): build and narrate the day's episode, plan its diorama film, write the plan to this
/// file for the workflow's film jobs, and post nothing; a later run posts it.
/// </param>
public sealed record RunOptions(bool DryRun = false, bool Create = false, bool UploadPrivate = false, string? PreparePlan = null);

/// <summary>
/// One morning's broadcast (spec §3): pull the world, back it up, catch it up, build the episode, push the episode and
/// then the world, then publish to each channel every episode of the last week not yet posted there. The world goes back
/// to the store only after the catch-up and the episode both succeeded, and each finished step is written to the
/// manifest at once, so a rerun repeats nothing that already happened.
/// </summary>
public sealed class DailyRun(BroadcastConfig config, IObjectStore root, IReadOnlyList<IEpisodeChannel> channels,
    Func<DateTimeOffset> clock, TextWriter log, RunOptions? options = null, Func<LlmSettings, ILlmProvider?>? providers = null,
    ISpeech? speech = null)
{
    /// <summary>Exit codes: published; nothing published (the world is untouched); the episode is out but a channel failed.</summary>
    public const int Ok = 0, Failed = 1, Partial = 2;

    /// <summary>How far back a failed post is still made up for.</summary>
    public const int MissedDays = 7;

    private readonly RunOptions _options = options ?? new RunOptions();
    private bool _worldSaved;

    public static string EpisodeKey(string date) => $"episodes/{date}/episode.json";

    public static string ScriptKey(string date) => $"episodes/{date}/script.txt";

    public static string MapKey(string date) => $"episodes/{date}/map.png";

    public static string NarrationKey(string date) => $"episodes/{date}/narration.mp3";

    private byte[]? _todaysMap;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var store = new PrefixedStore(root, _options.DryRun ? config.StagingPrefix : config.LivePrefix);
        var date = config.DateAt(clock());
        log.WriteLine($"Broadcast for {date}{(_options.DryRun ? " (dry run)" : "")}, under {store.Prefix}.");

        Manifest manifest;
        Episode episode;
        _worldSaved = false;
        _todaysMap = null;
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
        var result = await SpeakAsync(store, manifest, dates, date, episode, ct).ConfigureAwait(false);
        if (_options.PreparePlan is { } planPath)
        {
            var plan = await PlanFilmAsync(store, date, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(planPath, DioramaFilm.ToBytes(plan), ct).ConfigureAwait(false);
            log.WriteLine(plan.Chunks.Count > 0
                ? $"The diorama film of {date} is planned in {plan.Chunks.Count} stretches; nothing is posted until it is filmed."
                : $"No diorama film for {date} (no narration with chapters, or the film is off); nothing is posted yet.");
            return result;
        }
        foreach (var stateful in channels.OfType<IStatefulChannel>()) stateful.Attach(store, log, date);
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
                // Episodes from before the map (or whose map failed) are posted without one.
                var map = day == date && _todaysMap is not null ? _todaysMap : await store.GetAsync(MapKey(day), ct).ConfigureAwait(false);
                // The narration, when it was made in time; a post never waits for speech that failed.
                var audio = manifest.Day(day).Done(Manifest.AudioStep) ? await store.GetAsync(NarrationKey(day), ct).ConfigureAwait(false) : null;
                await channel.PublishAsync(told, map, audio, ct).ConfigureAwait(false);
            }
            catch (ChannelWaitingException e)
            {
                log.WriteLine($"{channel.Name}: the episode of {day} is not posted yet: {e.Message}");
                continue;
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

        await PublishFeedAsync(store, manifest, ct).ConfigureAwait(false);
        await PruneBackupsAsync(store, date, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// The podcast feed, the archive page and the cover (the latest map), rewritten each morning when the bucket is public.
    /// A failure here never fails the run: the next morning writes them again.
    /// </summary>
    private async Task PublishFeedAsync(PrefixedStore store, Manifest manifest, CancellationToken ct)
    {
        if (config.PublicBaseUrl is not { } baseUrl || manifest.WorldName is not { } name) return;
        var items = Feed.Items(manifest, config);
        if (items.Count == 0) return;
        try
        {
            var root = $"{baseUrl.TrimEnd('/')}/{store.Prefix}";
            await store.PutAsync(Feed.FeedKey, Encoding.UTF8.GetBytes(Feed.Rss(name, root, items, config.PodcastEmail)), "application/rss+xml; charset=utf-8", ct).ConfigureAwait(false);
            await store.PutAsync(Feed.ArchiveKey, Encoding.UTF8.GetBytes(Feed.Archive(name, items)), "text/html; charset=utf-8", ct).ConfigureAwait(false);
            var cover = _todaysMap ?? await store.GetAsync(MapKey(items[0].Date), ct).ConfigureAwait(false);
            if (cover is not null) await store.PutAsync(Feed.CoverKey, cover, "image/png", ct).ConfigureAwait(false);
            log.WriteLine($"Published the feed ({items.Count} episode(s)) at {root}{Feed.FeedKey}.");
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.WriteLine($"warning: the feed could not be published ({e.Message}); the next run tries again.");
        }
    }

    /// <summary>
    /// The narration (M3): every episode of the window still without audio is spoken, oldest first, within the monthly
    /// character budget (the shared ledger), after learning how its new names are said. Each is stored and marked at once;
    /// one found already stored is adopted, never spoken again; one that fails is left unmarked for the next run, and the
    /// posts go out as text and map meanwhile.
    /// </summary>
    private async Task<int> SpeakAsync(PrefixedStore store, Manifest manifest, IReadOnlyList<string> dates, string date, Episode today,
        CancellationToken ct)
    {
        var voice = config.Voice;
        var waiting = dates.Where(d => !manifest.Day(d).Done(Manifest.AudioStep)).ToList();
        if (!voice.Speak || waiting.Count == 0) return Ok;
        if (speech is null)
        {
            log.WriteLine("warning: ELEVENLABS_API_KEY is not set, so the episode goes out without its narration.");
            return Ok;
        }
        var month = date[..7];
        SpeechLedger ledger;
        try
        {
            ledger = SpeechLedger.FromBytes(await root.GetAsync(SpeechLedger.Key, ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.WriteLine($"warning: the speech ledger could not be read ({e.Message}); no narration today.");
            return Partial;
        }
        var respeller = voice.Respell && config.Llm.Narrate
            ? providers?.Invoke(new LlmSettings { Provider = "anthropic", Models = new(StringComparer.Ordinal) { ["narrate"] = config.Llm.NarrateModel } })
            : null;
        foreach (var day in waiting)
        {
            try
            {
                var episode = day == date ? today : await StoredEpisodeAsync(store, day, ct).ConfigureAwait(false);
                // Stored by a run that stopped before marking it: adopt it rather than pay for it again.
                if (await store.GetAsync(NarrationKey(day), ct).ConfigureAwait(false) is { } stored)
                {
                    await MarkNarratedAsync(store, manifest, day, stored, ct).ConfigureAwait(false);
                    log.WriteLine($"The narration of {day} was already stored; it is used as it is.");
                    continue;
                }
                var spent = ledger.Characters.GetValueOrDefault(month);
                if (spent + episode.Script.Length > voice.MonthlyCharacterBudget)
                {
                    log.WriteLine($"warning: this month's speech budget ({voice.MonthlyCharacterBudget:N0} characters, {spent:N0} used) "
                                  + $"would not cover the episode of {day}; it goes out without narration.");
                    return Ok;
                }
                await Pronouncer.LearnAsync(respeller, config.Llm.NarrateModel, manifest, episode.Script, log, ct).ConfigureAwait(false);
                IReadOnlyList<DictionaryRef> dictionaries;
                try
                {
                    dictionaries = await Pronouncer.DictionaryAsync(speech, manifest, episode.WorldName, ct).ConfigureAwait(false);
                }
                catch (SpeechException e)
                {
                    // Respellings help, but an episode is never left unspoken for want of them; tried again next run.
                    log.WriteLine($"warning: the respellings could not be given to ElevenLabs ({e.Message}); the episode is spoken without them. "
                                  + "An ElevenLabs key needs the Pronunciation Dictionaries permission (write) for them.");
                    dictionaries = [];
                }
                await SaveAsync(store, manifest, ct).ConfigureAwait(false); // the names and the dictionary, kept whatever happens next

                ledger.Characters[month] = spent + episode.Script.Length;
                await root.PutAsync(SpeechLedger.Key, ledger.ToBytes(), "application/json", ct).ConfigureAwait(false);
                var parts = episode.Parts;
                var (audio, sectionBytes) = await Voiceover.SpeakSectionsAsync(speech, parts.Select(p => p.Text).ToList(), voice, dictionaries, ct)
                    .ConfigureAwait(false);
                // The chapters first: a narration stored without them would be adopted as it is by a later run.
                await Chapters.SaveAsync(store, day, Chapters.Timed(parts.Select(p => p.Title).ToList(), sectionBytes, Voiceover.Kbps(voice.OutputFormat)), ct)
                    .ConfigureAwait(false);
                await store.PutAsync(NarrationKey(day), audio, "audio/mpeg", ct).ConfigureAwait(false);
                await MarkNarratedAsync(store, manifest, day, audio, ct).ConfigureAwait(false);
                var seconds = manifest.Day(day).AudioSeconds;
                log.WriteLine($"Narrated the episode of {day}: {seconds / 60}:{seconds % 60:00}, {episode.Script.Length:N0} characters.");
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.WriteLine($"warning: the episode of {day} could not be narrated ({e.Message}); it goes out as text and map, "
                              + "and the narration is tried again on the next run.");
                return Partial;
            }
        }
        return Ok;
    }

    /// <summary>
    /// The day's diorama film (sub-project 7 F2): its timed shots stored for the film jobs, and its stretches. None when
    /// YouTube or the diorama film is off, or the episode has no narration with chapters and shots.
    /// </summary>
    private async Task<FilmPlan> PlanFilmAsync(PrefixedStore store, string date, CancellationToken ct)
    {
        var none = new FilmPlan(date, store.Prefix, []);
        if (!config.YouTube.Upload || !config.YouTube.Diorama) return none;
        var timeline = await Chapters.LoadAsync(store, date, ct).ConfigureAwait(false);
        var shots = DioramaFilm.Timed(await Shots.LoadAsync(store, date, ct).ConfigureAwait(false), timeline);
        if (shots.Count == 0) return none;
        await store.PutAsync(DioramaFilm.ShotsKey(date), DioramaFilm.ToBytes(shots), "application/json", ct).ConfigureAwait(false);
        return new FilmPlan(date, store.Prefix, DioramaFilm.Chunks(timeline));
    }

    private Task MarkNarratedAsync(IObjectStore store, Manifest manifest, string day, byte[] audio, CancellationToken ct)
    {
        var entry = manifest.Day(day);
        (entry.AudioBytes, entry.AudioSeconds) = (audio.Length, Voiceover.Seconds(audio.Length, Voiceover.Kbps(config.Voice.OutputFormat)));
        entry.Steps[Manifest.AudioStep] = clock();
        return SaveAsync(store, manifest, ct);
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

            // The world's LLM settings follow broadcast.json each morning: the chronicler on the narrate tier, story beats
            // (the characters' choices and the fights, on the decide and arbitrate tiers' default model) off unless budgeted. Without ANTHROPIC_API_KEY the world runs offline and the template script ships.
            var llm = config.Llm;
            Fw("llm", worldPath, "--provider", llm.Narrate ? "anthropic" : "none", "--model", $"narrate={llm.NarrateModel}",
                "--story-beats", llm.StoryBeatsPerDay.ToString(CultureInfo.InvariantCulture),
                "--fight-beats", llm.FightBeatsPerDay.ToString(CultureInfo.InvariantCulture),
                "--narrations", llm.NarrationsPerDay.ToString(CultureInfo.InvariantCulture),
                "--cap", llm.MonthlyCapUsd.ToString(CultureInfo.InvariantCulture));
            Fw("catchup", worldPath, "--quiet");
            var dir = Path.Combine(work.FullName, "episode");
            Fw(["episode", worldPath, "--out", dir, "--date", date,
                "--stories", config.Stories.ToString(CultureInfo.InvariantCulture),
                "--max-characters", config.MaxCharacters.ToString(CultureInfo.InvariantCulture), .. llm.Narrate ? ["--narrate"] : Array.Empty<string>()]);
            var episode = Episode.FromJson(await File.ReadAllTextAsync(Path.Combine(dir, "episode.json"), ct).ConfigureAwait(false));
            if (llm.Narrate && episode.Writer != Episode.ChroniclerWriter)
                log.WriteLine("warning: the chronicler could not write today's script (see the LLM line above); the template script goes out.");
            var snapshot = Path.Combine(work.FullName, "upload.fw.db");
            Fw("snapshot", worldPath, snapshot);

            (_todaysMap, var shots) = PaintMap(snapshot, episode);
            await StoreMapAsync(store, date, ct).ConfigureAwait(false);
            try
            {
                await Shots.SaveAsync(store, date, shots, ct).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                log.WriteLine($"warning: the shot list could not be stored ({e.Message}); the video holds the whole map.");
            }

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

    /// <summary>
    /// Stores today's map, or removes one an earlier run of the day left for a different episode; a map that cannot be
    /// stored never stops the episode (it is still posted from memory).
    /// </summary>
    private async Task StoreMapAsync(IObjectStore store, string date, CancellationToken ct)
    {
        try
        {
            if (_todaysMap is not null) await store.PutAsync(MapKey(date), _todaysMap, "image/png", ct).ConfigureAwait(false);
            else await store.DeleteAsync(MapKey(date), ct).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            log.WriteLine($"warning: the map could not be stored ({e.Message}).");
        }
    }

    /// <summary>
    /// The political map of the saved world and the episode's shot list over it; a map that cannot be drawn never stops
    /// the episode (and without it, there is nothing to look at: no shots).
    /// </summary>
    private (byte[]? Map, IReadOnlyList<Shot> Shots) PaintMap(string worldPath, Episode episode)
    {
        try
        {
            using var world = WorldStore.Open(worldPath);
            var (w, l, _) = world.Load();
            return (MapPainter.Png(w, l), Shots.For(w, l, episode));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.WriteLine($"warning: the map could not be drawn ({e.Message}); the episode goes out without it.");
            return (null, []);
        }
    }

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
        var code = CliApp.Run(args, output, clock, providers);
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
