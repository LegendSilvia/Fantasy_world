using System.Net;
using System.Text;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class DailyRunTests : IDisposable
{
    /// <summary>05:30 in Bangkok on 2026-10-27.</summary>
    private static readonly DateTimeOffset Morning = new(2026, 10, 26, 22, 30, 0, TimeSpan.Zero);

    private static readonly BroadcastConfig Config = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny" }""");

    private sealed class CountingChannel(bool fails = false) : IEpisodeChannel
    {
        public List<Episode> Posts { get; } = [];
        public List<byte[]?> Maps { get; } = [];
        public bool Fails { get; set; } = fails;
        public string Name => "discord";

        public List<byte[]?> Audio { get; } = [];

        public Task PublishAsync(Episode episode, byte[]? map, byte[]? audio, CancellationToken ct = default)
        {
            if (Fails) throw new ChannelException("Discord answered HTTP 500.");
            Posts.Add(episode);
            Maps.Add(map);
            Audio.Add(audio);
            return Task.CompletedTask;
        }
    }

    private readonly TempDir _dir = new();
    private readonly LocalObjectStore _root;
    private readonly CountingChannel _discord = new();
    private readonly StringWriter _log = new();

    public DailyRunTests() => _root = new LocalObjectStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private Task<int> Run(DateTimeOffset now, RunOptions? options = null, IEpisodeChannel? channel = null,
        Func<LlmSettings, ILlmProvider?>? providers = null, ISpeech? speech = null, BroadcastConfig? config = null, IEpisodeChannel? also = null) =>
        new DailyRun(config ?? Config, _root, also is null ? [channel ?? _discord] : [channel ?? _discord, also], () => now, _log, options,
            providers ?? (_ => null), speech).RunAsync();

    /// <summary>A video channel: nothing to show until the episode has its narration.</summary>
    private sealed class VideoChannel : IEpisodeChannel
    {
        public List<string> Uploaded { get; } = [];
        public string Name => "youtube";

        public Task PublishAsync(Episode episode, byte[]? map, byte[]? audio, CancellationToken ct = default)
        {
            if (audio is null) throw new ChannelWaitingException("the video waits for the episode's narration and map.");
            Uploaded.Add(episode.Date);
            return Task.CompletedTask;
        }
    }

    private async Task<byte[]?> Get(string key) => await _root.GetAsync(key);

    private async Task<Episode> StoredEpisode(string prefix, string date) =>
        Episode.FromJson(Encoding.UTF8.GetString((await Get(prefix + DailyRun.EpisodeKey(date)))!));

    [Fact]
    public async Task TheChroniclerWritesTheScript_AndTheMapGoesOutWithIt()
    {
        await Run(Morning, new RunOptions(Create: true)); // offline: the template ships
        Assert.Equal(Episode.TemplateWriter, _discord.Posts[0].Writer);
        Assert.Contains("warning: the chronicler could not write today's script", _log.ToString());
        var map = await Get("live/" + DailyRun.MapKey("2026-10-27"));
        Assert.Equal(map, _discord.Maps[0]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, map![..4]);

        FakeLlmProvider? provider = null;
        provider = new FakeLlmProvider(request =>
        {
            // The brief's numbered sections, each retold in place.
            var brief = request.Prompt;
            var script = brief[(brief.IndexOf('\n') + 1)..brief.LastIndexOf("</brief>", StringComparison.Ordinal)].Trim();
            var sections = System.Text.RegularExpressions.Regex.Split(script, @"(?:^|\n\n)\[\d+\] ").Where(t => t.Length > 0)
                .Select(t => System.Text.RegularExpressions.Regex.Replace(t, @"\n\((?:A scene to paint|Face-off figures|The war's moves)[\s\S]*$", ""))
                .Select(t => t.Replace("Elsewhere in the chronicle:", "And elsewhere, in brief:")).ToList();
            return System.Text.Json.JsonSerializer.Serialize(new { sections });
        });
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), providers: settings => settings.Provider == "anthropic" ? provider : null));
        var request = provider.Requests[0]; // the script, then its editor: story beats are off
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(ModelTier.Narrate, request.Tier);
        Assert.Equal(Episode.ChroniclerWriter, _discord.Posts[1].Writer);
        Assert.Contains("And elsewhere, in brief:", _discord.Posts[1].Script);
        Assert.Equal(_discord.Posts[1].Script, Encoding.UTF8.GetString((await Get("live/" + DailyRun.ScriptKey("2026-10-28")))!));
        Assert.NotNull(_discord.Maps[1]);
        Assert.Contains("by the chronicler", _log.ToString());

        // The answer was saved in the world: a rerun after a lost manifest finds it there.
        using var copy = new TempFile();
        await File.WriteAllBytesAsync(copy.Path, (await Get("live/world.fw.db"))!);
        using (var store = WorldStore.Open(copy.Path))
        {
            Assert.Equal(2, store.LoadJournal().Count); // the chronicler and the editor
            Assert.Equal("claude-sonnet-5-5", store.LoadLlmSettings().ModelFor(ModelTier.Narrate));
            Assert.Equal(0, store.LoadLlmSettings().StoryBeatsPerDay);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task APrepareRun_NarratesAndPlansTheFilm_ButPostsNothing_AndTheNextRunPosts()
    {
        var plan = Path.Combine(_dir.Path, "plan.json");
        var speech = new PronouncerTests.FakeSpeech { SecondsPerPart = 12 };
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true, PreparePlan: plan), speech: speech));
        Assert.Empty(_discord.Posts);
        var written = DioramaFilm.FromBytes<FilmPlan>(await File.ReadAllBytesAsync(plan))!;
        Assert.Equal(("2026-10-27", "live/"), (written.Date, written.Prefix));
        Assert.NotEmpty(written.Chunks);
        Assert.NotNull(await Get("live/" + DioramaFilm.ShotsKey("2026-10-27")));
        // The browser's copy of the day's world, loadable on its own.
        var (web, _) = FantasyWorld.Sim.Persist.WebBundle.FromGzip((await Get("live/" + DailyRun.WebBundleKey))!).Load();
        Assert.NotEmpty(web.Realms);
        Assert.Contains("nothing is posted until it is filmed", _log.ToString());

        // The publishing run that follows: the episode is built and spoken, so it only posts.
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddMinutes(20), speech: speech));
        Assert.Single(_discord.Posts);
        Assert.Single(speech.Spoken, Opens);
    }

    /// <summary>An episode is spoken section by section; its first request is the only one with nothing before it.</summary>
    private static bool Opens(SpeechRequest request) => request.PreviousText is null;

    [Fact]
    public async Task TheNarrator_SpeaksEachEpisodeOnce_WithItsNamesLearned()
    {
        var speech = new PronouncerTests.FakeSpeech();
        var respeller = PronouncerTests.Respeller();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), providers: _ => respeller, speech: speech));
        var spoken = Assert.Single(speech.Spoken, Opens);
        Assert.Equal(("uju3wxzG5OhpWcoi3SMy", "eleven_v4_turbo"), (spoken.VoiceId, spoken.Model));
        var audio = await Get("live/" + DailyRun.NarrationKey("2026-10-27"));
        Assert.Equal(audio, _discord.Audio.Single());
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        Assert.True(manifest.Day("2026-10-27").Done(Manifest.AudioStep));
        Assert.Equal(_discord.Posts[0].Script.Length, SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"]);
        Assert.NotEmpty(manifest.PronunciationTries.Keys.Concat(manifest.Pronunciations.Keys)); // the names were asked about
        Assert.Contains("Narrated the episode of 2026-10-27", _log.ToString());
        // One chapter per section, timed from the narration, starting at 0:00.
        var timeline = await Chapters.LoadAsync(new PrefixedStore(_root, "live/"), "2026-10-27");
        Assert.Equal(_discord.Posts[0].Parts.Select(p => p.Title), timeline.Chapters.Select(c => c.Title));
        Assert.Equal(0, timeline.Chapters[0].Start);

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech, providers: _ => respeller));
        Assert.Single(speech.Spoken, Opens);
        Assert.Single(respeller.Requests, r => r.Key.StartsWith("pronounce|", StringComparison.Ordinal)); // names already known
    }

    [Fact]
    public async Task APublicBucket_GetsTheFeedTheArchiveAndTheCover()
    {
        var speech = new PronouncerTests.FakeSpeech();
        await Run(Morning, new RunOptions(Create: true), speech: speech);
        Assert.Null(await Get("live/feed.xml")); // no public address: nothing published

        var open = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "publicBaseUrl": "https://pub-1.r2.dev" }""");
        await Run(Morning.AddDays(1), speech: speech, config: open);
        var feed = Encoding.UTF8.GetString((await Get("live/feed.xml"))!);
        Assert.Contains("https://pub-1.r2.dev/live/episodes/2026-10-28/narration.mp3", feed);
        Assert.Contains("https://pub-1.r2.dev/live/episodes/2026-10-27/narration.mp3", feed);
        Assert.NotNull(await Get("live/index.html"));
        Assert.Equal(await Get("live/" + DailyRun.MapKey("2026-10-28")), await Get("live/cover.png"));
    }

    [Fact]
    public async Task RespellingsThatCannotBeStored_StillLeaveTheEpisodeSpoken()
    {
        var speech = new PronouncerTests.FakeSpeech { RefusesDictionaries = true };
        var respeller = new FakeLlmProvider(request => System.Text.Json.JsonSerializer.Serialize(new
        {
            respellings = Pronouncer.Words(request.Prompt).Select(w => new { word = w, say = $"{w[0]}-{w[1..].ToLowerInvariant()}" }),
        }));
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), providers: _ => respeller, speech: speech));
        var spoken = Assert.Single(speech.Spoken, Opens);
        Assert.Empty(spoken.Dictionaries);
        Assert.Contains("spoken without them", _log.ToString());
        Assert.NotNull(_discord.Audio.Single());
    }

    [Fact]
    public async Task SpeechThatFails_LeavesTheTextAndMapPost_AndIsMadeUpNextRun()
    {
        var speech = new PronouncerTests.FakeSpeech { Fails = true };
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true), speech: speech));
        Assert.Null(_discord.Audio.Single());
        Assert.NotNull(_discord.Maps.Single());
        Assert.Contains("could not be narrated", _log.ToString());

        speech.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), speech: speech));
        Assert.Equal(2, speech.Spoken.Count(Opens)); // yesterday's narration, then today's
        Assert.NotNull(await Get("live/" + DailyRun.NarrationKey("2026-10-27")));
        Assert.NotNull(_discord.Audio[1]);
    }

    [Fact]
    public async Task ANarrationStoredBeforeACrash_IsUsed_NotPaidForAgain()
    {
        var speech = new PronouncerTests.FakeSpeech();
        var flaky = new FlakyStore(_root) { Failing = Manifest.Key };
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true), _ => null, speech).RunAsync();
        Assert.Empty(speech.Spoken); // the episode itself never got its manifest

        await Run(Morning, new RunOptions(Create: true), speech: speech);
        Assert.Single(speech.Spoken, Opens);
        // Stopped after storing the narration but before marking it: the next run adopts it.
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        manifest.Day("2026-10-27").Steps.Remove(Manifest.AudioStep);
        await _root.PutAsync("live/" + Manifest.Key, manifest.ToBytes(), "application/json");
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech));
        Assert.Single(speech.Spoken, Opens);
        Assert.Contains("already stored", _log.ToString());
    }

    [Fact]
    public async Task ANewWorldBegunOnADayTheOldOneAired_TakesTheDay_AndIsPostedAgain()
    {
        var speech = new PronouncerTests.FakeSpeech();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), speech: speech));
        Assert.Single(_discord.Posts);
        await _root.PutAsync("live/episodes/2026-10-27/film/chunk-00.mp4", [1], "video/mp4"); // the old world's film

        // A rerun of the same world never redoes the day, even with 'create'.
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), new RunOptions(Create: true), speech: speech));
        Assert.Single(_discord.Posts);

        var fresh = BroadcastConfig.Parse(""" { "seed": 4, "size": "tiny", "worldKey": "fresh.fw.db" } """);
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(2), new RunOptions(Create: true), speech: speech, config: fresh));
        Assert.Contains("A new world begins today", _log.ToString());
        Assert.Null(await Get("live/episodes/2026-10-27/film/chunk-00.mp4"));
        Assert.NotNull(await Get("live/fresh.fw.db"));
        Assert.Equal(2, _discord.Posts.Count);
        Assert.Equal(2, speech.Spoken.Count(Opens));
    }

    [Fact]
    public async Task ARepaint_PaintsAndFilmsTodayAgain_AndUploadsAgain_ButTellsDiscordOnce()
    {
        var speech = new PronouncerTests.FakeSpeech();
        var youtube = new VideoChannel();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), speech: speech, also: youtube));
        Assert.Equal((1, 1), (_discord.Posts.Count, youtube.Uploaded.Count));
        var world = await Get("live/" + Config.WorldKey);
        var spoken = speech.Spoken.Count;
        await _root.PutAsync("live/episodes/2026-10-27/film/chunk-00.mp4", [1], "video/mp4");
        await _root.PutAsync("live/" + YouTube.VideoKey("2026-10-27"), [1], "video/mp4");

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), new RunOptions(Repaint: true), speech: speech, also: youtube));
        Assert.Contains("Repainted the episode of 2026-10-27", _log.ToString());
        Assert.Null(await Get("live/episodes/2026-10-27/film/chunk-00.mp4"));
        Assert.Null(await Get("live/" + YouTube.VideoKey("2026-10-27")));
        Assert.Equal((1, 2), (_discord.Posts.Count, youtube.Uploaded.Count));
        Assert.Equal(spoken, speech.Spoken.Count); // the narration is kept
        Assert.Equal(world, await Get("live/" + Config.WorldKey)); // and the world is not lived again
    }

    [Fact]
    public async Task DryRuns_ShareTheLiveBudget()
    {
        var tight = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "voice": { "monthlyCharacterBudget": 5000 } }""");
        var speech = new PronouncerTests.FakeSpeech();
        await Run(Morning, new RunOptions(Create: true), speech: speech, config: tight);
        var used = SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"];
        await Run(Morning, new RunOptions(DryRun: true), speech: speech, config: tight, channel: new CountingChannel());
        Assert.Equal(2, speech.Spoken.Count(Opens));
        // The staging world is the live one after its episode aired, so its episode is the next (the legend in brief), but
        // what it speaks is counted on the same ledger.
        Assert.InRange(SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"], used + 300, 2 * used);
    }

    [Fact]
    public async Task ANewWorld_IsNamedThenLivesItsHistory_BeforeItsFirstEpisode()
    {
        var config = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "prehistoryYears": 2 }""");
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), config: config));
        var log = _log.ToString();
        Assert.True(log.IndexOf("fw invent", StringComparison.Ordinal) < log.IndexOf("fw advance", StringComparison.Ordinal));
        Assert.Contains("2 years of history lived", log);
        Assert.Single(_discord.Posts);
    }

    [Fact]
    public async Task AnUnreadableTally_NeverStopsTheDay()
    {
        var run = new DailyRun(Config, _root, [_discord], () => Morning, _log, new RunOptions(Create: true), _ => null,
            tally: _ => Task.FromResult<string?>("<!doctype html><title>The Chronicle</title>"));
        Assert.Equal(DailyRun.Ok, await run.RunAsync());
        Assert.Contains("the players' answers could not be applied", _log.ToString());
        Assert.Single(_discord.Posts);
    }

    [Fact]
    public async Task ASpeechServiceThatAnswersNonsense_NeverStopsThePost()
    {
        var garbled = new GarbledSpeech();
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true), speech: garbled));
        Assert.Single(_discord.Posts);
        Assert.Contains("could not be narrated", _log.ToString());
    }

    private sealed class GarbledSpeech : ISpeech
    {
        public Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default) => throw new System.Text.Json.JsonException("not json");
        public Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task TheVideo_WaitsForItsNarration_WithoutHoldingUpDiscordOrFailingTheRun()
    {
        var youtube = new VideoChannel();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), also: youtube)); // no speech service
        Assert.Single(_discord.Posts);
        Assert.Empty(youtube.Uploaded);
        Assert.Contains("youtube: the episode of 2026-10-27 is not posted yet", _log.ToString());
        Assert.False(Manifest.FromBytes(await Get("live/" + Manifest.Key)).Day("2026-10-27").Done("youtube"));

        var speech = new PronouncerTests.FakeSpeech();
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech, also: youtube));
        Assert.Equal(new[] { "2026-10-27" }, youtube.Uploaded);
        Assert.Single(_discord.Posts); // Discord is not posted again
        Assert.True(Manifest.FromBytes(await Get("live/" + Manifest.Key)).Day("2026-10-27").Done("youtube"));
    }

    [Fact]
    public async Task TheMonthlyBudget_StopsSpeech_AndNoKeyMeansNoNarration()
    {
        var tight = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "voice": { "monthlyCharacterBudget": 100 } }""");
        var speech = new PronouncerTests.FakeSpeech();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), speech: speech, config: tight));
        Assert.Empty(speech.Spoken);
        Assert.Contains("speech budget", _log.ToString());

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1)));
        Assert.Contains("ELEVENLABS_API_KEY is not set", _log.ToString());
    }

    [Fact]
    public async Task AScheduledRun_NeverBeginsAWorld()
    {
        Assert.Equal(DailyRun.Failed, await Run(Morning));
        Assert.Contains("There is no world at live/world.fw.db", _log.ToString());
        Assert.Empty(await _root.ListAsync(""));
        Assert.Empty(_discord.Posts);
    }

    [Fact]
    public async Task ACreateRun_BeginsTheWorld_AndARerunTheSameDayPostsNothing()
    {
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true)));
        Assert.Single(_discord.Posts);
        Assert.Equal("2026-10-27", _discord.Posts[0].Date);
        Assert.Equal(_discord.Posts[0].Script, Encoding.UTF8.GetString((await Get("live/" + DailyRun.ScriptKey("2026-10-27")))!));
        var world = await Get("live/world.fw.db");

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(3)));
        Assert.Single(_discord.Posts);
        Assert.Equal(world, await Get("live/world.fw.db"));
        Assert.Contains("already posted today", _log.ToString());
    }

    [Fact]
    public async Task TheNextMorning_BacksUpFirst_LivesAYear_AndTellsOnlyWhatIsNew()
    {
        await Run(Morning, new RunOptions(Create: true));
        var before = await Get("live/world.fw.db");

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1)));
        Assert.Equal(before, await Get("live/backups/2026-10-28.fw.db"));
        Assert.Equal(2, _discord.Posts.Count);
        var (first, second) = (_discord.Posts[0], _discord.Posts[1]);
        Assert.Equal("2026-10-28", second.Date);
        Assert.Equal(first.ToYear + 1, second.ToYear);
        Assert.True(second.FirstEventId > first.LastEventId);
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        Assert.True(manifest.Day("2026-10-28").Done(Manifest.DiscordStep));
        Assert.Equal(second.EventCount, manifest.Day("2026-10-28").Events);
    }

    [Fact]
    public async Task AFailedPost_KeepsTheEpisode_AndIsRetriedOnceByTheNextRun()
    {
        _discord.Fails = true;
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true)));
        var world = await Get("live/world.fw.db");
        Assert.NotNull(world);

        _discord.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1)));
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(2)));
        Assert.Single(_discord.Posts);
        Assert.Equal(world, await Get("live/world.fw.db"));
    }

    [Fact]
    public async Task ABrokenWorld_PublishesNothing_AndIsLeftAsItWas()
    {
        var garbage = Encoding.UTF8.GetBytes("not a world");
        await _root.PutAsync("live/world.fw.db", garbage, "application/octet-stream");
        Assert.Equal(DailyRun.Failed, await Run(Morning));
        Assert.Equal(garbage, await Get("live/world.fw.db"));
        Assert.Null(await Get("live/" + DailyRun.EpisodeKey("2026-10-27")));
        Assert.Empty(_discord.Posts);
        Assert.Contains("Nothing was published", _log.ToString());
    }

    [Fact]
    public async Task AMissedPost_IsMadeTheNextMorning_BeforeThatMorningsOwn()
    {
        _discord.Fails = true;
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true)));
        _discord.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
        Assert.Contains("posted the missed episode of 2026-10-27", _log.ToString());
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(2, _discord.Posts.Count);
    }

    [Fact]
    public async Task ARunStoppedAfterSavingTheWorld_IsPickedUp_AndNothingIsToldTwiceOrLost()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        flaky.Failing = Manifest.Key;
        Assert.Equal(DailyRun.Failed, await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync());
        Assert.Contains("The world and the episode were saved, but not the manifest", _log.ToString());
        var world = await Get("live/world.fw.db");

        // The same morning: the stored world already holds the day's episode, so it is posted and the world stays.
        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(world, await Get("live/world.fw.db"));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
    }

    [Fact]
    public async Task ARunStoppedAfterSavingTheWorld_IsPostedTheNextMorning()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        flaky.Failing = Manifest.Key;
        await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync();
        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(2)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28", "2026-10-29" }, _discord.Posts.Select(p => p.Date));
        Assert.True(_discord.Posts[2].FirstEventId > _discord.Posts[1].LastEventId);
    }

    [Fact]
    public async Task ARunStoppedBeforeSavingTheWorld_LeavesItAsItWas_AndTheDayIsBuiltAgain()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        var world = await Get("live/world.fw.db");
        flaky.Failing = "world.fw.db";
        Assert.Equal(DailyRun.Failed, await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync());
        Assert.Contains("the stored world is as it was", _log.ToString());
        Assert.Equal(world, await Get("live/world.fw.db"));

        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
        Assert.True(_discord.Posts[1].EventCount > 0);
    }

    [Fact]
    public async Task AMapThatCannotBeStored_StillGoesOutWithThePost()
    {
        var flaky = new FlakyStore(_root) { Failing = "map.png" };
        Assert.Equal(DailyRun.Ok, await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true), _ => null).RunAsync());
        Assert.Contains("warning: the map could not be stored", _log.ToString());
        Assert.NotNull(_discord.Maps.Single());
        Assert.Null(await Get("live/" + DailyRun.MapKey("2026-10-27")));
    }

    [Fact]
    public void TheLlmKey_IsReadOnlyFromTheEnvironmentGiven()
    {
        var anthropic = new LlmSettings { Provider = "anthropic" };
        Assert.Null(FantasyWorld.Llm.LlmProviders.Create(anthropic, _ => null, BroadcastApp.ScriptTimeout));
        Assert.IsType<FantasyWorld.Llm.AnthropicProvider>(
            FantasyWorld.Llm.LlmProviders.Create(anthropic, name => name == "ANTHROPIC_API_KEY" ? "test-key" : null, BroadcastApp.ScriptTimeout));
    }

    /// <summary>A store whose writes to one key fail, as if the run stopped there.</summary>
    private sealed class FlakyStore(IObjectStore inner) : IObjectStore
    {
        public string? Failing { get; set; }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);

        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default) =>
            Failing is not null && key.EndsWith("/" + Failing, StringComparison.Ordinal)
                ? throw new IOException("R2 did not answer in time.")
                : inner.PutAsync(key, content, contentType, ct);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);

        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
    }

    [Fact]
    public async Task ADryRun_WorksOnACopyUnderStaging_AndLeavesTheLiveBroadcastAlone()
    {
        await Run(Morning, new RunOptions(Create: true));
        var liveWorld = await Get("live/world.fw.db");
        var liveManifest = await Get("live/" + Manifest.Key);
        var test = new CountingChannel();

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), new RunOptions(DryRun: true), test));
        Assert.Single(test.Posts);
        Assert.Single(_discord.Posts);
        Assert.Equal(liveWorld, await Get("live/world.fw.db"));
        Assert.Equal(liveManifest, await Get("live/" + Manifest.Key));
        Assert.Null(await Get("live/backups/2026-10-28.fw.db"));
        Assert.NotNull(await Get("staging/world.fw.db"));
        var stored = await StoredEpisode("staging/", "2026-10-28");
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), new RunOptions(DryRun: true, Create: true), test));
        Assert.Equal((test.Posts[0].LastEventId, test.Posts[0].Script), (stored.LastEventId, stored.Script));
    }

    [Fact]
    public async Task ADryRunThatCreates_WritesNothingUnderLive()
    {
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(DryRun: true, Create: true)));
        Assert.Empty(await _root.ListAsync("live/"));
        Assert.NotEmpty(await _root.ListAsync("staging/"));
    }

    [Fact]
    public async Task TheApp_TakesSecretsOnlyFromTheEnvironment_AndNeverPrintsThem()
    {
        using var config = new TempFile();
        await File.WriteAllTextAsync(config.Path, """{ "seed": 3, "size": "tiny", "youTube": { "upload": false } }""");
        var env = new Dictionary<string, string>
        {
            ["DISCORD_TEST_WEBHOOK_URL"] = "https://discord.test/api/webhooks/9/SECRET-TOKEN",
            ["YOUTUBE_CLIENT_ID"] = "id", ["YOUTUBE_CLIENT_SECRET"] = "s", ["YOUTUBE_REFRESH_TOKEN"] = "r", // a dry run uploads only when asked (--upload-unlisted)
        };
        var output = new StringWriter();

        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["run", "--config", config.Path], output, () => Morning, env.GetValueOrDefault));
        Assert.Contains("R2 is not configured", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["run", "--config", config.Path, "--local", _dir.Path], output, () => Morning, env.GetValueOrDefault));
        Assert.Contains("DISCORD_WEBHOOK_URL is not set", output.ToString());

        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var code = await BroadcastApp.RunAsync(["run", "--dry-run", "--create", "--config", config.Path, "--local", _dir.Path],
            output, () => Morning, env.GetValueOrDefault, http);
        Assert.Equal(DailyRun.Ok, code);
        Assert.Single(http.Seen);
        Assert.Equal("discord.test", http.Seen[0].Request.RequestUri!.Host);
        Assert.False(BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(DryRun: true), env.GetValueOrDefault, new HttpClient(), TextWriter.Null)!.Uploads);
        Assert.True(BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(), env.GetValueOrDefault, new HttpClient(), TextWriter.Null)!.Uploads);
        // A dry run may be asked to try the upload; the video is then unlisted (a link to share), whatever broadcast.json says.
        var tried = BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(DryRun: true, UploadUnlisted: true), env.GetValueOrDefault, new HttpClient(), TextWriter.Null)!;
        Assert.True(tried.Uploads);
        Assert.Equal("unlisted", tried.Privacy);
        Assert.Equal("unlisted", BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(), env.GetValueOrDefault, new HttpClient(), TextWriter.Null)!.Privacy);
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["run", "--upload-unlisted", "--local", _dir.Path], TextWriter.Null, () => Morning, env.GetValueOrDefault));
        var quiet = new StringWriter();
        // Without the secrets the live video is still made and stored, never uploaded.
        Assert.False(BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(), _ => null, new HttpClient(), quiet)!.Uploads);
        Assert.Contains("YouTube secrets are not set, so the video is made and stored", quiet.ToString());
        Assert.DoesNotContain("SECRET-TOKEN", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["publish"], output, () => Morning, env.GetValueOrDefault));
    }
}
