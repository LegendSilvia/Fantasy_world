using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Cli;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Publishing;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Broadcast;

/// <param name="UploadUnlisted">On a dry run, upload the staging video to YouTube as unlisted: a link anyone can open and share, not listed on the channel.</param>
/// <param name="PreparePlan">
/// Prepare only (sub-project 7 F2): build and narrate the day's episode, plan its diorama film, write the plan to this
/// file for the workflow's film jobs, and post nothing; a later run posts it.
/// </param>
public sealed record RunOptions(bool DryRun = false, bool Create = false, bool UploadUnlisted = false, string? PreparePlan = null, bool Repaint = false);

/// <summary>
/// One morning's broadcast (spec §3): pull the world, back it up, catch it up, build the episode, push the episode and
/// then the world, then publish to each channel every episode of the last week not yet posted there. The world goes back
/// to the store only after the catch-up and the episode both succeeded, and each finished step is written to the
/// manifest at once, so a rerun repeats nothing that already happened.
/// </summary>
public sealed class DailyRun(BroadcastConfig config, IObjectStore root, IReadOnlyList<IEpisodeChannel> channels,
    Func<DateTimeOffset> clock, TextWriter log, RunOptions? options = null, Func<LlmSettings, ILlmProvider?>? providers = null,
    ISpeech? speech = null, IImageMaker? images = null, Func<CancellationToken, Task<string?>>? tally = null)
{
    /// <summary>Exit codes: published; nothing published (the world is untouched); the episode is out but a channel failed.</summary>
    public const int Ok = 0, Failed = 1, Partial = 2;

    /// <summary>How far back a failed post is still made up for.</summary>
    public const int MissedDays = 7;

    private readonly RunOptions _options = options ?? new RunOptions();
    private bool _worldSaved;

    public static string EpisodeKey(string date) => $"episodes/{date}/episode.json";

    /// <summary>The world for the browser viewer (sub-project 11): the day's world, its history and geography, no LLM journal.</summary>
    public const string WebBundleKey = "web/world.json.gz";

    /// <summary>The website's images, as JSON: {"realm#3": "images/realm-3-1a2b3c4d.png", "monster#9": …, "char#12": …}.</summary>
    public const string WebImagesKey = "web/images.json";

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
            await NewWorldTakesTodayAsync(store, manifest, date, ct).ConfigureAwait(false);
            if (manifest.Day(date).Done(Manifest.EpisodeStep))
            {
                log.WriteLine("Today's episode is already built; the world is not touched again today.");
                episode = await StoredEpisodeAsync(store, date, ct).ConfigureAwait(false);
                if (_options.Repaint) episode = await RepaintAsync(store, manifest, episode, date, ct).ConfigureAwait(false);
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
        await KeepUnderLimitAsync(store, date, ct).ConfigureAwait(false);
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
                var (audio, sectionBytes, cues) = await Voiceover.SpeakTimedAsync(speech, parts.Select(p => p.Text).ToList(), voice, dictionaries, ct)
                    .ConfigureAwait(false);
                // The subtitles, timed by the voice itself (the owner, 2026-10-29), burned into the video when it is joined.
                if (cues.Count > 0) await Subtitles.SaveAsync(store, day, cues, ct).ConfigureAwait(false);
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
        var sections = await store.GetAsync(EpisodeKey(date), ct).ConfigureAwait(false) is { } stored
            ? Episode.FromJson(Encoding.UTF8.GetString(stored)).Parts : null;
        var shots = DioramaFilm.Timed(await Shots.LoadAsync(store, date, ct).ConfigureAwait(false), timeline, sections);
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

    /// <summary>
    /// A world begun afresh on a day the old one already aired (the owner, 2026-10-29: "start a new world fresh, rerender
    /// and upload"): only on a run started with 'create' when no world is stored at the configured key yet. Today's
    /// stored episode, narration, film and upload states belong to the old world, so they are cleared and the day is made
    /// again (and posted again) from the new one; earlier days stay in the archive as they were.
    /// </summary>
    private async Task NewWorldTakesTodayAsync(PrefixedStore store, Manifest manifest, string date, CancellationToken ct)
    {
        if (!_options.Create || !manifest.Episodes.TryGetValue(date, out var day) || day.Steps.Count == 0
            || await store.GetAsync(config.WorldKey, ct).ConfigureAwait(false) is not null) return;
        var old = await store.ListAsync($"episodes/{date}/", ct).ConfigureAwait(false);
        foreach (var key in old) await store.DeleteAsync(key, ct).ConfigureAwait(false);
        manifest.Episodes.Remove(date);
        manifest.WorldName = null;
        await store.PutAsync(Manifest.Key, manifest.ToBytes(), "application/json", ct).ConfigureAwait(false);
        log.WriteLine($"A new world begins today: the old world's episode of {date} ({old.Count} stored files) is cleared and the day is made again.");
    }

    /// <summary>
    /// Today's episode painted and filmed again (the owner, 2026-10-29: "rerender today's episode with the new stills"): the
    /// world, script and narration stay as they are; the fights' stills are made afresh, and the film, the video and the
    /// YouTube uploads are cleared so they are made and uploaded again. Discord, already told, is not posted again.
    /// </summary>
    private async Task<Episode> RepaintAsync(PrefixedStore store, Manifest manifest, Episode episode, string date, CancellationToken ct)
    {
        foreach (var still in episode.Parts.Select(p => p.Still).OfType<string>().Distinct()) await store.DeleteAsync(still, ct).ConfigureAwait(false);
        episode = episode with { Sections = episode.Parts.Select(p => p with { Still = null }).ToList() };
        var work = Directory.CreateTempSubdirectory("fw-repaint-");
        try
        {
            var snapshot = Path.Combine(work.FullName, "world.fw.db");
            await File.WriteAllBytesAsync(snapshot, await store.GetAsync(config.WorldKey, ct).ConfigureAwait(false)
                                                    ?? throw new InvalidOperationException($"No world is stored at {store.Prefix}{config.WorldKey}."), ct).ConfigureAwait(false);
            episode = await FaceOffImagesAsync(store, snapshot, episode, date, ct).ConfigureAwait(false);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            work.Delete(recursive: true);
        }
        await store.PutAsync(EpisodeKey(date), Encoding.UTF8.GetBytes(episode.ToJson()), "application/json", ct).ConfigureAwait(false);
        var film = await store.ListAsync($"episodes/{date}/film/", ct).ConfigureAwait(false);
        foreach (var key in film.Concat([Channels.YouTube.VideoKey(date), Channels.YouTube.ShortVideoKey(date), Channels.YouTube.ThumbnailKey(date),
                     Channels.YouTube.StateKey(date), Channels.YouTube.ShortStateKey(date)]))
            await store.DeleteAsync(key, ct).ConfigureAwait(false);
        manifest.Day(date).Steps.Remove("youtube");
        manifest.Day(date).Steps.Remove("youtube-short");
        await SaveAsync(store, manifest, ct).ConfigureAwait(false);
        log.WriteLine($"Repainted the episode of {date}: {episode.Parts.Count(p => p.Still is not null)} stills made afresh; "
                      + "its film, video and YouTube uploads are cleared, to be made and uploaded again.");
        return episode;
    }

    private async Task<Episode> MakeEpisodeAsync(PrefixedStore store, Manifest manifest, string date, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("fw-broadcast-");
        try
        {
            var worldPath = Path.Combine(work.FullName, "world.fw.db");
            var pulled = await store.GetAsync(config.WorldKey, ct).ConfigureAwait(false);
            var copiedFromLive = false;
            var created = false;
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
                // Made without history first: Claude names its races and its nations' festivals (below), then its first
                // century is lived offline (sub-project 13 N7), so the history already speaks of them by their names.
                Fw("new", worldPath, "--seed", config.Seed.ToString(CultureInfo.InvariantCulture), "--size", config.Size, "--prehistory", "0");
                created = true;
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
            void Llm(string provider) => Fw("llm", worldPath, "--provider", provider, "--model", $"narrate={llm.NarrateModel}",
                "--story-beats", llm.StoryBeatsPerDay.ToString(CultureInfo.InvariantCulture),
                "--fight-beats", llm.FightBeatsPerDay.ToString(CultureInfo.InvariantCulture),
                "--narrations", llm.NarrationsPerDay.ToString(CultureInfo.InvariantCulture),
                "--cap", llm.MonthlyCapUsd.ToString(CultureInfo.InvariantCulture));
            Llm(llm.Narrate ? "anthropic" : "none");
            if (created)
            {
                Fw("invent", worldPath);
                var years = config.PrehistoryYears ?? Prehistory.DefaultYearsFor(Enum.Parse<WorldSize>(config.Size, ignoreCase: true));
                if (years > 0)
                {
                    Llm("none");
                    Fw("advance", worldPath, $"{years.ToString(CultureInfo.InvariantCulture)}y", "--quiet");
                    Llm(llm.Narrate ? "anthropic" : "none");
                }
                log.WriteLine($"A new world was made: its races and festivals named, {years} years of history lived.");
            }
            // The world's legend (the owner, 2026-10-29): a world made before legends gets one, told in full in its next episode.
            Fw("legend", worldPath);
            // The players' answers since yesterday (sub-project 13 N5: the players are the gods), before the day's history.
            if (tally is not null && await tally(ct).ConfigureAwait(false) is { Length: > 0 } votes)
            {
                var answers = Path.Combine(work.FullName, "tally.json");
                await File.WriteAllTextAsync(answers, votes, ct).ConfigureAwait(false);
                // The players' answers never stop the day: unreadable ones are left, and the world goes on.
                try { Fw("answer", worldPath, answers); }
                catch (InvalidOperationException) { log.WriteLine("warning: the players' answers could not be applied; the rules decide today."); }
            }
            Fw("catchup", worldPath, "--quiet");
            var dir = Path.Combine(work.FullName, "episode");
            // The recent episodes as the audience heard them, for the editor's pass (the owner, 2026-10-29).
            var recent = Path.Combine(work.FullName, "recent.txt");
            await File.WriteAllTextAsync(recent, await RecentScriptsAsync(store, manifest, date, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            Fw(["episode", worldPath, "--out", dir, "--date", date,
                "--stories", config.Stories.ToString(CultureInfo.InvariantCulture),
                "--max-characters", config.MaxCharacters.ToString(CultureInfo.InvariantCulture), .. llm.Narrate ? ["--narrate", "--recent", recent] : Array.Empty<string>()]);
            var episode = Episode.FromJson(await File.ReadAllTextAsync(Path.Combine(dir, "episode.json"), ct).ConfigureAwait(false));
            await FeedbackAsync(store, date, episode, ct).ConfigureAwait(false);
            if (llm.Narrate && episode.Writer != Episode.ChroniclerWriter)
                log.WriteLine("warning: the chronicler could not write today's script (see the LLM line above); the template script goes out.");
            var snapshot = Path.Combine(work.FullName, "upload.fw.db");
            Fw("snapshot", worldPath, snapshot);

            (_todaysMap, var shots) = PaintMap(snapshot, episode);
            episode = await FaceOffImagesAsync(store, snapshot, episode, date, ct).ConfigureAwait(false);
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
            try
            {
                // The browser's copy of the day; a failure costs only the website's update.
                var web = Path.Combine(work.FullName, "world.json.gz");
                Fw("export-web", snapshot, web);
                await store.PutAsync(WebBundleKey, await File.ReadAllBytesAsync(web, ct).ConfigureAwait(false), "application/gzip", ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or HttpRequestException)
            {
                log.WriteLine($"warning: the web bundle could not be stored ({e.Message}); the website keeps yesterday's world.");
            }
            await WebImagesAsync(store, snapshot, date, ct).ConfigureAwait(false);

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

    /// <summary>
    /// No image is painted before this year of the world (the owner, 2026-10-29: images only from the start of year 100,
    /// where the chronicle begins); the offline history before it is never painted.
    /// </summary>
    public const int FirstImageYear = 100;

    /// <summary>Whether the world has reached the year its images begin; logged when it has not.</summary>
    private bool Paints(FantasyWorld.Sim.Model.World world, string what)
    {
        if (world.Date.Year >= FirstImageYear) return true;
        log.WriteLine($"Images: none for {what} before Year {FirstImageYear} (the world is in Year {world.Date.Year}).");
        return false;
    }

    /// <summary>The face-offs' portraits and banners and the fights' stills (<see cref="Likenesses"/>); a failure only leaves them out.</summary>
    private async Task<Episode> FaceOffImagesAsync(PrefixedStore store, string snapshot, Episode episode, string date, CancellationToken ct)
    {
        if (!config.Images.Make || episode.Parts.All(p => p.FaceOff is null && p.Scene is null)) return episode;
        try
        {
            using var saved = WorldStore.Open(snapshot);
            var (world, events, _) = saved.Load();
            if (!Paints(world, "the episode")) return episode;
            var limit = StorageBudget.Bytes(config.Storage.MaxGigabytes);
            // A fight's still is painted from its fighters' portraits, laid side by side by ffmpeg (without it, from words alone).
            var sheets = new FfmpegVideo();
            return await Likenesses.AttachAsync(episode, world, store, images, config.Images.Style, date,
                async bytes => StorageBudget.Room((await root.ListSizesAsync("", ct).ConfigureAwait(false)).Sum(o => o.Bytes), bytes, limit), log, ct,
                events, sheets.SheetAsync).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or HttpRequestException && !ct.IsCancellationRequested)
        {
            log.WriteLine($"warning: the episode's images could not be had ({e.Message}); the film goes without them.");
            return episode;
        }
    }

    /// <summary>How many recent episodes the editor is shown.</summary>
    public const int RecentEpisodes = 3;

    /// <summary>The last few episodes' scripts before <paramref name="date"/>, oldest first, each under its date.</summary>
    private static async Task<string> RecentScriptsAsync(PrefixedStore store, Manifest manifest, string date, CancellationToken ct)
    {
        var text = new StringBuilder();
        foreach (var day in manifest.Episodes.Keys.Where(d => string.CompareOrdinal(d, date) < 0).OrderBy(d => d, StringComparer.Ordinal).TakeLast(RecentEpisodes))
            if (await store.GetAsync(ScriptKey(day), ct).ConfigureAwait(false) is { } script)
                text.Append($"The episode of {day}:\n{Encoding.UTF8.GetString(script)}\n\n");
        return text.ToString();
    }

    /// <summary>Where the editor's feedback on the world is kept, a file a day (the owner, 2026-10-29).</summary>
    public static string FeedbackKey(string date) => $"feedback/{date}.md";

    /// <summary>
    /// The editor's feedback on the world, for its maker: kept in the store, written to the log and, on GitHub, to the run's
    /// summary page. Never voiced or posted.
    /// </summary>
    private async Task FeedbackAsync(PrefixedStore store, string date, Episode episode, CancellationToken ct)
    {
        if (episode.Feedback is not { Length: > 0 } feedback) return;
        log.WriteLine($"The editor's feedback on the world:\n{feedback}");
        try
        {
            await store.PutAsync(FeedbackKey(date), Encoding.UTF8.GetBytes($"# Feedback on the episode of {date}\n\n{feedback}\n"), "text/markdown; charset=utf-8", ct)
                .ConfigureAwait(false);
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
                await File.AppendAllTextAsync(summary, $"## The editor's feedback on the world ({date})\n\n{feedback}\n\n", ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or HttpRequestException && !ct.IsCancellationRequested)
        {
            log.WriteLine($"warning: the feedback could not be kept ({e.Message}).");
        }
    }

    /// <summary>
    /// New images painted for the website alone: none (the owner, 2026-10-29: paint only what appears in the videos). The
    /// site shows the pictures the episodes' face-offs and stills already have.
    /// </summary>
    public const int NewWebImagesPerDay = 0;

    /// <summary>
    /// The website's images (the owner, 2026-10-29): the realms' banners for the flags, the beasts for their badges and cards,
    /// the past year's duellists for the fights' cards; made once each and listed at <see cref="WebImagesKey"/> (reference to
    /// image key). A failure leaves the website without them.
    /// </summary>
    private async Task WebImagesAsync(PrefixedStore store, string snapshot, string date, CancellationToken ct)
    {
        if (!config.Images.Make) return;
        try
        {
            using var saved = WorldStore.Open(snapshot);
            var (world, events, _) = saved.Load();
            if (!Paints(world, "the website")) return;
            var limit = StorageBudget.Bytes(config.Storage.MaxGigabytes);
            var pictures = await Likenesses.ForWebAsync(world, events, store, images, config.Images.Style, date,
                async bytes => StorageBudget.Room((await root.ListSizesAsync("", ct).ConfigureAwait(false)).Sum(o => o.Bytes), bytes, limit), log,
                NewWebImagesPerDay, ct).ConfigureAwait(false);
            await store.PutAsync(WebImagesKey, JsonSerializer.SerializeToUtf8Bytes(pictures), "application/json", ct).ConfigureAwait(false);
            log.WriteLine($"The website's images: {pictures.Count} (banners, beasts and duellists).");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or HttpRequestException && !ct.IsCancellationRequested)
        {
            log.WriteLine($"warning: the website's images could not be had ({e.Message}); it goes without them.");
        }
    }

    /// <summary>Keeps the whole bucket under its limit (<see cref="StorageBudget"/>); a failure only waits for the next run.</summary>
    private async Task KeepUnderLimitAsync(PrefixedStore store, string date, CancellationToken ct)
    {
        try
        {
            var objects = await root.ListSizesAsync("", ct).ConfigureAwait(false);
            var used = (await Likenesses.IndexAsync(store, ct).ConfigureAwait(false)).ToDictionary(e => store.Prefix + e.Key, e => e.Value.LastUsed);
            var doomed = StorageBudget.ToDelete(objects, date, StorageBudget.Bytes(config.Storage.MaxGigabytes), config.LivePrefix, used);
            if (doomed.Count == 0) return;
            foreach (var key in doomed) await root.DeleteAsync(key, ct).ConfigureAwait(false);
            await Likenesses.ForgetAsync(store, doomed.Where(k => k.StartsWith(store.Prefix, StringComparison.Ordinal)).Select(k => k[store.Prefix.Length..]), ct)
                .ConfigureAwait(false);
            var freed = objects.Where(o => doomed.Contains(o.Key)).Sum(o => o.Bytes);
            log.WriteLine($"The bucket was near its {config.Storage.MaxGigabytes:0.#} GB limit: {doomed.Count} old object(s) deleted, {freed / 1e6:0} MB freed.");
        }
        catch (IOException e)
        {
            log.WriteLine($"warning: the bucket's size could not be kept in check ({e.Message}); the next run will try again.");
        }
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
