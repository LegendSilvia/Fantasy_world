# Daily Broadcast — Milestone 3 Implementation Plan (sub-project 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Milestone 3 of the daily broadcast, the narrator. Each episode is read aloud by ElevenLabs (v4 Turbo, the owner's chosen voice), its invented names said the same way every day, and attached to the Discord post; with a public bucket, a podcast feed and an archive page are kept as well.

**Architecture:** All in `sim/FantasyWorld.Broadcast`: `Speech/` (the ElevenLabs client, splitting and joining, respellings and pronunciation dictionaries), `Publishing/Feed.cs` (RSS and the archive page), and a narration step in `DailyRun` between building the episode and posting it. The manifest records what was spoken, the characters used each month and every respelling.

**Tech Stack:** unchanged (.NET 8 / C# 12, xUnit, GitHub Actions). No new packages: ElevenLabs is called over `HttpClient`.

**Spec:** `docs/superpowers/specs/2026-10-26-daily-broadcast-design.md` (§3.4, §4 M3). **Builds on:** M1 and M2 (`docs/superpowers/plans/2026-10-26-daily-broadcast-m1.md`, `…-m2.md`).

## Global Constraints

Everything in M1's and M2's Global Constraints still applies. In addition:

- **Never pay for speech twice:** a narration is stored and its `audio` step marked the moment it is made; a rerun skips it.
- **Speech never blocks a post:** if it fails, the post goes out as text and map and the narration is made by the next run; past the monthly character budget, or without a key, the episode goes out without narration.
- **Names sound the same every day:** a respelling, once learned, is never changed.
- **No real network calls in tests:** a fake `ISpeech` and fake HTTP handlers stand in for ElevenLabs.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Model and voice | `eleven_v4_turbo`; Michael C. Vincent – Confident, Expressive (`uju3wxzG5OhpWcoi3SMy`) | The owner's choices |
| Request limit | 5,000 characters a request, from config; a day's script (at most 3,600) is one request | ElevenLabs documents 10,000 for v4 but no figure for v4 Turbo; the limit is a setting, not an assumption |
| Joining parts | MP3 parts joined frame to frame (later parts' ID3 tags dropped); no ffmpeg | MP3 frames concatenate cleanly; nothing to install in the workflow |
| Budget | 150,000 characters a calendar month (about 40 days of episodes), in a ledger at the bucket's root shared by live and dry runs, counted before each request | The brief's TTS cap holds the whole account; a run that stops mid-request has still counted what it may have been billed (a failed request is counted too, erring on the safe side) |
| Never paying twice | A narration found already stored is adopted, not spoken again | Covers a run that stopped between storing the audio and marking it |
| Pronunciation | The chronicler's model respells each new name once (words written in lower case in the script, possessives and plain titles are not asked about); a respelling must look like its name (hyphenated letters, the same first letter, a similar length), so it can change how a name sounds but never what the script says; kept for good; a word without a usable answer is asked again, up to three times | Alias rules work with every ElevenLabs model; asking once and keeping the answer makes names consistent |
| Dictionaries | One ElevenLabs dictionary per bucket prefix, made once and extended (`add-rules`, a new version each time) | No pile of dictionaries in the account |
| Discord | The narration attached beside the map while everything sent stays under 8 MiB; a server that still refuses the size gets the post again without it | No public bucket needed to listen; the text post never waits on the audio |
| Feed and archive | Published only with `publicBaseUrl` (the owner turns on public access) | Podcast apps need public URLs; making a bucket public is the owner's call |
| Cover art | The latest map, now painted at 1,536 px | Podcast apps ask for at least 1,400 px |
| Respelling spend | Asked directly of the provider (one small call a day at most), not through the world's journal | It is the broadcast's, not the world's; it stays tiny (only new words) |

**Verification:** each task's tests fail first and pass after its implementation (the code was replayed task by task in this order). An independent review of the prototype found that speech could be paid for twice (and uncounted) when a run stopped between speaking and saving, that dry runs had a budget of their own, that a non-JSON answer from ElevenLabs or an odd character in a headline could stop the post, that a respelling that failed the checks was kept as "said as written" for good, that a respelling could replace a name with other words, that possessives became separate names, that every new name made a new dictionary, that the feed lacked an owner and a self link, and that the Discord size check ignored the map. All are fixed below and covered by tests.

## File Structure

```
sim/FantasyWorld.Broadcast/  Speech/ElevenLabs.cs, Speech/Voiceover.cs, BroadcastConfig.cs, Manifest.cs (1); Speech/Pronouncer.cs (2);
                             Speech/SpeechLedger.cs, DailyRun.cs, BroadcastApp.cs, Channels/DiscordWebhook.cs (3); Publishing/Feed.cs, Media/MapPainter.cs (4)
sim/FantasyWorld.Sim.Tests/Broadcasting/  SpeechTests.cs (1), PronouncerTests.cs (2), DailyRunTests.cs, BroadcastPartsTests.cs (3), FeedTests.cs (4)
broadcast.json, .github/workflows/broadcast.yml, docs/broadcast-setup.md, README.md (3, 4)
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: ElevenLabs speech, split and joined

Spec §4, M3. An `ISpeech` over ElevenLabs' HTTP API: text to speech (`POST /v1/text-to-speech/{voice}`, the key in `xi-api-key`, the model, the neighbouring text for a continuous read, and up to three pronunciation dictionaries) and alias dictionaries (`POST /v1/pronunciation-dictionaries/add-from-rules`, extended with `…/{id}/add-rules`); an answer that is not what was asked for is a speech error, never a crash. A script is split on paragraphs, then sentences, under the per-request limit from config (not assumed from the model), each part spoken with its neighbours as context, and the MP3 parts joined frame to frame. The narrator's settings (`voice` in `broadcast.json`) default to the owner's choices: Michael C. Vincent and `eleven_v4_turbo`.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Speech/ElevenLabs.cs`, `sim/FantasyWorld.Broadcast/Speech/Voiceover.cs`
- Modify: `sim/FantasyWorld.Broadcast/BroadcastConfig.cs`, `sim/FantasyWorld.Broadcast/Manifest.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/SpeechTests.cs`

**Interfaces:**
- Consumes: `BroadcastConfig` (M1).
- Produces: `record DictionaryRef(Id, VersionId)`, `record SpeechRequest`, `interface ISpeech { SpeakAsync; CreateDictionaryAsync; AddRulesAsync }`, `SpeechException`, `ElevenLabs { Api; FromEnvironment(http, env) }`; `static class Voiceover { Chunks; Join; Seconds; Kbps; SpeakAsync }`; `record BroadcastVoice`, `BroadcastConfig.Voice`; manifest fields for speech (`AudioBytes`, `AudioSeconds`, `Pronunciations`, `PronunciationTries`, the dictionary and its words).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Broadcasting/SpeechTests.cs`:
```csharp
using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Speech;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class SpeechTests
{
    private static readonly BroadcastVoice Voice = new();

    [Fact]
    public async Task ElevenLabs_SpeaksWithTheVoiceModelAndDictionaries_KeyInTheHeaderOnly()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "ID3audio"));
        var speech = new ElevenLabs(new HttpClient(http), "SECRET-KEY");
        var audio = await speech.SpeakAsync(new SpeechRequest("voice/1", "eleven_v4_turbo", "Hear this.", "Before.", null,
            [new DictionaryRef("dict1", "v7")], "mp3_44100_128"));
        Assert.Equal("ID3audio"u8.ToArray(), audio);
        var (request, body) = http.Seen.Single();
        Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/voice%2F1?output_format=mp3_44100_128", request.RequestUri!.ToString());
        Assert.Equal("SECRET-KEY", request.Headers.GetValues("xi-api-key").Single());
        var json = JsonNode.Parse(body)!;
        Assert.Equal(("Hear this.", "eleven_v4_turbo", "Before."), ((string)json["text"]!, (string)json["model_id"]!, (string)json["previous_text"]!));
        Assert.Equal("dict1", (string)json["pronunciation_dictionary_locators"]![0]!["pronunciation_dictionary_id"]!);
        Assert.Equal("v7", (string)json["pronunciation_dictionary_locators"]![0]!["version_id"]!);
        Assert.DoesNotContain("SECRET", JsonNode.Parse(body)!.ToJsonString());
    }

    [Fact]
    public async Task ElevenLabs_CreatesAliasDictionaries_AndFailsWithoutLeakingTheKey()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, """{"id":"d1","version_id":"v1","name":"x"}"""), (HttpStatusCode.Unauthorized, ""));
        var speech = new ElevenLabs(new HttpClient(http), "SECRET-KEY");
        var made = await speech.CreateDictionaryAsync("Aerenmoor", new Dictionary<string, string> { ["Deithkialwold"] = "Deeth-kyal-wold" });
        Assert.Equal(new DictionaryRef("d1", "v1"), made);
        var rule = JsonNode.Parse(http.Seen[0].Body)!["rules"]![0]!;
        Assert.Equal(("alias", "Deithkialwold", "Deeth-kyal-wold"), ((string)rule["type"]!, (string)rule["string_to_replace"]!, (string)rule["alias"]!));
        var error = await Assert.ThrowsAsync<SpeechException>(() => speech.SpeakAsync(new SpeechRequest("v", "m", "t", null, null, [], "mp3_44100_128")));
        Assert.Equal("ElevenLabs could not speak: HTTP 401.", error.Message);
    }

    [Fact]
    public async Task ElevenLabs_ExtendsADictionary_AndTurnsAGarbledAnswerIntoASpeechError()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, """{"id":"d1","version_id":"v2","version_rules_num":3}"""),
            (HttpStatusCode.OK, "<html>proxy</html>"), (HttpStatusCode.OK, """{"id":"d1"}"""));
        var speech = new ElevenLabs(new HttpClient(http), "K");
        Assert.Equal(new DictionaryRef("d1", "v2"), await speech.AddRulesAsync("d1", new Dictionary<string, string> { ["Fiar"] = "Fee-ar" }));
        Assert.Equal("https://api.elevenlabs.io/v1/pronunciation-dictionaries/d1/add-rules", http.Seen[0].Request.RequestUri!.ToString());
        Assert.Equal("Fee-ar", (string)JsonNode.Parse(http.Seen[0].Body)!["rules"]![0]!["alias"]!);
        await Assert.ThrowsAsync<SpeechException>(() => speech.AddRulesAsync("d1", new Dictionary<string, string> { ["A"] = "B" }));
        await Assert.ThrowsAsync<SpeechException>(() => speech.CreateDictionaryAsync("n", new Dictionary<string, string> { ["A"] = "B" }));
    }

    [Fact]
    public void Scripts_AreSplitOnParagraphs_UnderTheLimit_AndNothingIsLost()
    {
        var paragraphs = Enumerable.Range(1, 9).Select(i => string.Join(" ", Enumerable.Repeat($"Sentence {i} is told.", 8))).ToList();
        var script = string.Join("\n\n", paragraphs);
        var chunks = Voiceover.Chunks(script, 400);
        Assert.All(chunks, c => Assert.True(c.Length <= 400));
        Assert.Equal(script.Replace("\n\n", " "), string.Join(" ", chunks).Replace("\n\n", " "));
        Assert.Single(Voiceover.Chunks(script, 10_000));

        var long1 = string.Join(" ", Enumerable.Repeat("A very long sentence goes on.", 40));
        Assert.All(Voiceover.Chunks(long1, 200), c => Assert.True(c.Length <= 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => Voiceover.Chunks(script, 50));
    }

    [Fact]
    public void Parts_AreJoinedFrameToFrame_AndTimedByBitRate()
    {
        byte[] tagged = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 2, 0xAA, 0xBB, 0xFF, 0xFB];
        var joined = Voiceover.Join([[0xFF, 0xFB, 1], tagged]);
        Assert.Equal(new byte[] { 0xFF, 0xFB, 1, 0xFF, 0xFB }, joined);
        Assert.Equal(128, Voiceover.Kbps("mp3_44100_128"));
        Assert.Equal(60, Voiceover.Seconds(960_000, 128));
        Assert.Throws<FormatException>(() => Voiceover.Kbps("pcm_16000"));
    }

    [Fact]
    public void VoiceSettings_HaveTheOwnersDefaults_AndRefuseNonsense()
    {
        var config = BroadcastConfig.Parse("{}");
        Assert.Equal(("eleven_v4_turbo", "uju3wxzG5OhpWcoi3SMy", true), (config.Voice.Model, config.Voice.VoiceId, config.Voice.Speak));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "voice": { "maxCharactersPerRequest": 10 } }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "voice": { "outputFormat": "pcm_16000" } }"""));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Speech' does not exist in the namespace 'FantasyWorld.Broadcast' (are you missing an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Broadcast/BroadcastConfig.cs` entirely with:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The broadcast's LLM (spec §3.2, M2): the chronicler writes the script on the narrate tier. Story beats (the sim's
/// own LLM decisions while it catches up) stay off unless given a budget, so the daily cost is one script.
/// </summary>
public sealed record BroadcastLlm
{
    /// <summary>When off, the template script ships and nothing is asked.</summary>
    public bool Narrate { get; init; } = true;

    public string NarrateModel { get; init; } = "claude-sonnet-5-5";

    public int StoryBeatsPerDay { get; init; }

    public int NarrationsPerDay { get; init; } = 10;

    public decimal MonthlyCapUsd { get; init; } = 5m;
}

/// <summary>
/// The narrator's voice (M3): ElevenLabs speaks each script. The per-request limit and the monthly character budget
/// are settings, not assumptions about the model; past the budget the episode goes out as text.
/// </summary>
public sealed record BroadcastVoice
{
    public bool Speak { get; init; } = true;

    /// <summary>"Michael C. Vincent – Confident, Expressive", from the ElevenLabs library: the owner's choice.</summary>
    public string VoiceId { get; init; } = "uju3wxzG5OhpWcoi3SMy";

    public string Model { get; init; } = "eleven_v4_turbo";

    public string OutputFormat { get; init; } = "mp3_44100_128";

    public int MaxCharactersPerRequest { get; init; } = 5000;

    public int MonthlyCharacterBudget { get; init; } = 150_000;

    /// <summary>Ask the chronicler's model, once per invented name, how to say it; the answers are kept for good.</summary>
    public bool Respell { get; init; } = true;
}

/// <summary>
/// The broadcast's checked-in settings (<c>broadcast.json</c>). Nothing secret lives here: keys and webhooks come from
/// the environment (GitHub Actions secrets).
/// </summary>
public sealed record BroadcastConfig
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The world's object key, under the live or staging prefix.</summary>
    public string WorldKey { get; init; } = "world.fw.db";

    /// <summary>The seed and size a <c>create</c> run starts the world with.</summary>
    public ulong Seed { get; init; } = 20261027;

    public string Size { get; init; } = "medium";

    /// <summary>Episodes are keyed by the broadcast date in this time zone.</summary>
    public string TimeZone { get; init; } = "Asia/Bangkok";

    public string Language { get; init; } = "en";

    public string LivePrefix { get; init; } = "live/";

    public string StagingPrefix { get; init; } = "staging/";

    /// <summary>Daily backups kept; the first backup of each month is kept as well, for good.</summary>
    public int BackupDays { get; init; } = 30;

    public int Stories { get; init; } = Episode.DefaultStories;

    public int MaxCharacters { get; init; } = Episode.DefaultMaxCharacters;

    /// <summary>The name the Discord post appears under.</summary>
    public string DiscordName { get; init; } = "The Chronicle";

    public BroadcastLlm Llm { get; init; } = new();

    public BroadcastVoice Voice { get; init; } = new();

    /// <summary>When on, the workflow commits a heartbeat monthly so GitHub keeps the schedule of a public repository alive.</summary>
    public bool KeepAlive { get; init; } = true;

    public static BroadcastConfig Load(string path) => Parse(File.ReadAllText(path));

    public static BroadcastConfig Parse(string json) =>
        (JsonSerializer.Deserialize<BroadcastConfig>(json, Json) ?? throw new FormatException("broadcast.json is empty.")).Validated();

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>The broadcast date (yyyy-MM-dd) at <paramref name="now"/>, in the broadcast's time zone.</summary>
    public string DateAt(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private BroadcastConfig Validated()
    {
        if (Language != "en") throw new FormatException($"Only English broadcasts exist so far (language \"{Language}\").");
        if (Size is not ("tiny" or "medium" or "large")) throw new FormatException($"Unknown size \"{Size}\".");
        if (BackupDays < 1) throw new FormatException("backupDays must be at least 1.");
        if (Stories is < 0 or > 10) throw new FormatException("stories must be between 0 and 10.");
        if (MaxCharacters is < 300 or > 4000) throw new FormatException("maxCharacters must be between 300 and 4000 (a Discord post holds 4096).");
        if (LivePrefix == StagingPrefix || !LivePrefix.EndsWith('/') || !StagingPrefix.EndsWith('/'))
            throw new FormatException("livePrefix and stagingPrefix must differ and end with '/'.");
        if (Llm.StoryBeatsPerDay < 0 || Llm.NarrationsPerDay < 0 || Llm.MonthlyCapUsd < 0 || Llm.NarrateModel.Length == 0)
            throw new FormatException("llm budgets and the cap cannot be negative, and narrateModel must be named.");
        if (Voice.MaxCharactersPerRequest < 100 || Voice.MonthlyCharacterBudget < 0 || Voice.VoiceId.Length == 0 || Voice.Model.Length == 0)
            throw new FormatException("voice needs a voiceId and a model, at least 100 characters a request and a budget that is not negative.");
        _ = Speech.Voiceover.Kbps(Voice.OutputFormat);
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
    }
}
```

Replace `sim/FantasyWorld.Broadcast/Manifest.cs` entirely with:
```csharp
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>What one broadcast date has done: each step is marked the moment it finishes, so a rerun skips it.</summary>
public sealed class ManifestDay
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
    public int Events { get; set; }
    public string? Headline { get; set; }
    public SortedDictionary<string, DateTimeOffset> Steps { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The narration's size and length, for the feed (M3).</summary>
    public long AudioBytes { get; set; }
    public int AudioSeconds { get; set; }

    public bool Done(string step) => Steps.ContainsKey(step);
}

/// <summary>
/// <c>manifest.json</c>: per broadcast date, which steps finished and when. The rule that keeps reruns safe: a channel
/// is posted to only when its step is not marked, and the mark is written back straight after.
/// </summary>
public sealed class Manifest
{
    public const string Key = "manifest.json";
    public const string EpisodeStep = "episode";
    public const string DiscordStep = "discord";
    public const string AudioStep = "audio";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, ManifestDay> Episodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How each invented name is said (word → respelling; a word said as written maps to itself). Never changed once set.</summary>
    public SortedDictionary<string, string> Pronunciations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Words asked about without a usable answer, and how often; after a few tries one is said as written.</summary>
    public SortedDictionary<string, int> PronunciationTries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The ElevenLabs dictionary holding the respellings, its current version, and the words already in it.</summary>
    public string? DictionaryId { get; set; }
    public string? DictionaryVersionId { get; set; }
    public SortedSet<string> DictionaryWords { get; set; } = new(StringComparer.Ordinal);

    public ManifestDay Day(string date) => Episodes.TryGetValue(date, out var day) ? day : Episodes[date] = new ManifestDay();

    public void Record(string date, Episode episode)
    {
        var day = Day(date);
        (day.FromYear, day.ToYear, day.Events, day.Headline) = (episode.FromYear, episode.ToYear, episode.EventCount, episode.Headline?.Text);
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static Manifest FromBytes(byte[]? bytes) =>
        bytes is null ? new Manifest() : JsonSerializer.Deserialize<Manifest>(bytes, Json) ?? new Manifest();
}
```

`sim/FantasyWorld.Broadcast/Speech/ElevenLabs.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>A pronunciation dictionary as ElevenLabs knows it: its id and the version to read.</summary>
public sealed record DictionaryRef(string Id, string VersionId);

/// <summary>One text-to-speech request.</summary>
public sealed record SpeechRequest(string VoiceId, string Model, string Text, string? PreviousText, string? NextText,
    IReadOnlyList<DictionaryRef> Dictionaries, string OutputFormat);

/// <summary>Speech and pronunciation dictionaries. Throws <see cref="SpeechException"/> when the service could not answer.</summary>
public interface ISpeech
{
    Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default);

    /// <summary>A new dictionary holding exactly these alias rules (word → how to say it).</summary>
    Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default);

    /// <summary>Adds alias rules to a dictionary (a rule for the same word is replaced); returns its new version.</summary>
    Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default);
}

public sealed class SpeechException(string message) : Exception(message);

/// <summary>
/// ElevenLabs over HTTP (M3): <c>POST /v1/text-to-speech/{voice}</c> for audio and
/// <c>POST /v1/pronunciation-dictionaries/add-from-rules</c> and <c>…/{id}/add-rules</c> for respellings (one dictionary,
/// extended as names are learned). The key goes in the <c>xi-api-key</c> header; errors name the status code only.
/// </summary>
public sealed class ElevenLabs(HttpClient http, string apiKey) : ISpeech
{
    public const string Api = "https://api.elevenlabs.io";

    public static ElevenLabs? FromEnvironment(HttpClient http, Func<string, string?> env) =>
        env("ELEVENLABS_API_KEY") is { Length: > 0 } key ? new ElevenLabs(http, key) : null;

    public async Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["text"] = request.Text,
            ["model_id"] = request.Model,
            ["previous_text"] = request.PreviousText,
            ["next_text"] = request.NextText,
        };
        if (request.Dictionaries.Count > 0)
            body["pronunciation_dictionary_locators"] = new JsonArray(request.Dictionaries
                .Select(d => (JsonNode)new JsonObject { ["pronunciation_dictionary_id"] = d.Id, ["version_id"] = d.VersionId }).ToArray());
        var url = $"{Api}/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}?output_format={Uri.EscapeDataString(request.OutputFormat)}";
        using var response = await SendAsync(url, body, "speak", ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default)
    {
        var body = new JsonObject { ["name"] = name, ["rules"] = Rules(aliases) };
        return await DictionaryAsync($"{Api}/v1/pronunciation-dictionaries/add-from-rules", body, ct).ConfigureAwait(false);
    }

    public async Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default) =>
        await DictionaryAsync($"{Api}/v1/pronunciation-dictionaries/{Uri.EscapeDataString(dictionaryId)}/add-rules",
            new JsonObject { ["rules"] = Rules(aliases) }, ct).ConfigureAwait(false);

    private static JsonArray Rules(IReadOnlyDictionary<string, string> aliases) =>
        new(aliases.Select(a => (JsonNode)new JsonObject
        {
            ["type"] = "alias", ["string_to_replace"] = a.Key, ["alias"] = a.Value, ["case_sensitive"] = true, ["word_boundaries"] = true,
        }).ToArray());

    private async Task<DictionaryRef> DictionaryAsync(string url, JsonObject body, CancellationToken ct)
    {
        using var response = await SendAsync(url, body, "store the pronunciations", ct).ConfigureAwait(false);
        JsonObject? answer;
        try
        {
            answer = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new SpeechException("ElevenLabs answered with something other than a dictionary.");
        }
        return answer?["id"] is JsonValue id && id.TryGetValue<string>(out var dictionary)
               && answer["version_id"] is JsonValue version && version.TryGetValue<string>(out var versionId)
            ? new DictionaryRef(dictionary, versionId)
            : throw new SpeechException("ElevenLabs answered without a dictionary id.");
    }

    private async Task<HttpResponseMessage> SendAsync(string url, JsonObject body, string action, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("xi-api-key", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new SpeechException($"ElevenLabs could not be reached to {action} ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SpeechException($"ElevenLabs did not answer in time to {action}.");
        }
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new SpeechException($"ElevenLabs could not {action}: HTTP {status}.");
    }
}
```

`sim/FantasyWorld.Broadcast/Speech/Voiceover.cs`:
```csharp
using System.Text.RegularExpressions;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// Turning a script into one MP3 (M3): split on paragraphs (then sentences) under the model's per-request limit, each
/// request told the text around it for a continuous read, the parts joined frame to frame.
/// </summary>
public static partial class Voiceover
{
    /// <summary>The script in parts of at most <paramref name="limit"/> characters, breaking between paragraphs where it can.</summary>
    public static IReadOnlyList<string> Chunks(string script, int limit)
    {
        if (limit < 100) throw new ArgumentOutOfRangeException(nameof(limit), "A part must hold at least 100 characters.");
        var pieces = script.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(p => p.Length <= limit ? [p] : Sentences(p, limit)).ToList();
        var chunks = new List<string>();
        var current = "";
        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 2 + piece.Length > limit)
            {
                chunks.Add(current);
                current = "";
            }
            current = current.Length == 0 ? piece : current + "\n\n" + piece;
        }
        if (current.Length > 0) chunks.Add(current);
        return chunks;
    }

    private static IEnumerable<string> Sentences(string paragraph, int limit)
    {
        var current = "";
        foreach (var sentence in SentenceEnd().Split(paragraph).Where(s => s.Length > 0))
        {
            // A sentence longer than a whole part (none are, in practice) is cut at a space.
            var rest = sentence;
            while (rest.Length > limit)
            {
                var cut = rest.LastIndexOf(' ', limit - 1) is var space and > 0 ? space : limit;
                if (current.Length > 0) { yield return current; current = ""; }
                yield return rest[..cut];
                rest = rest[cut..].TrimStart();
            }
            if (current.Length > 0 && current.Length + 1 + rest.Length > limit) { yield return current; current = ""; }
            current = current.Length == 0 ? rest : current + " " + rest;
        }
        if (current.Length > 0) yield return current;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    /// <summary>Joins MP3 parts into one stream, dropping an ID3 tag from the start of every part but the first.</summary>
    public static byte[] Join(IReadOnlyList<byte[]> parts)
    {
        using var joined = new MemoryStream();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var skip = i > 0 && part.Length >= 10 && part[0] == 'I' && part[1] == 'D' && part[2] == '3'
                ? 10 + (part[6] << 21 | part[7] << 14 | part[8] << 7 | part[9])
                : 0;
            joined.Write(part, Math.Min(skip, part.Length), part.Length - Math.Min(skip, part.Length));
        }
        return joined.ToArray();
    }

    /// <summary>Seconds of audio in a constant-bit-rate MP3 of <paramref name="bytes"/> at <paramref name="kbps"/>.</summary>
    public static int Seconds(long bytes, int kbps) => (int)Math.Round(bytes * 8.0 / (kbps * 1000));

    /// <summary>The bit rate an output format names (<c>mp3_44100_128</c> → 128).</summary>
    public static int Kbps(string outputFormat) =>
        outputFormat.Split('_') is ["mp3", _, var rate] && int.TryParse(rate, out var kbps) ? kbps
            : throw new FormatException($"Only MP3 output formats are used (got '{outputFormat}').");

    /// <summary>The audio of a whole script: each part spoken with its neighbours as context.</summary>
    public static async Task<byte[]> SpeakAsync(ISpeech speech, string script, BroadcastVoice voice, IReadOnlyList<DictionaryRef> dictionaries,
        CancellationToken ct = default)
    {
        var chunks = Chunks(script, voice.MaxCharactersPerRequest);
        var parts = new List<byte[]>();
        for (var i = 0; i < chunks.Count; i++)
            parts.Add(await speech.SpeakAsync(new SpeechRequest(voice.VoiceId, voice.Model, chunks[i], i > 0 ? chunks[i - 1] : null,
                i + 1 < chunks.Count ? chunks[i + 1] : null, dictionaries, voice.OutputFormat), ct).ConfigureAwait(false));
        return Join(parts);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SpeechTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 635` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(broadcast): speak scripts with ElevenLabs, split under the request limit and joined

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Respellings for invented names

Spec §4, M3 (pronunciation hints). The first time a name appears in a script (a capitalised word never written in lower case there, its possessive folded in, not a plain title), the chronicler's model is asked, with the other new names of the day, for a plain English respelling, or the word itself if it is ordinary. A respelling is used only if it looks like its name (hyphenated letters, the same first letter, a similar length), so it can change how a name sounds but never what the script says; answers are kept in the manifest for good, and a word without a usable answer is asked again, up to three times, then said as written. The respelled names become one ElevenLabs alias dictionary, made once and extended as names are learned.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Speech/Pronouncer.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/PronouncerTests.cs`

**Interfaces:**
- Consumes: Task 1; `ILlmProvider`, `LlmRequest`, `PromptText` (Plan 5).
- Produces: `static class Pronouncer { MaxNewWords; MaxTries; System; Words(script); LearnAsync(provider, model, manifest, script, log); Parse(json, asked); LooksLike(say, word); Rules(manifest); DictionaryAsync(speech, manifest, worldName) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Broadcasting/PronouncerTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class PronouncerTests
{
    private const string Script = "The chronicle of Deithkialwold begins. The Duchy of Manreach declared war at Gewick. And so it was.";

    internal sealed class FakeSpeech : ISpeech
    {
        public List<IReadOnlyDictionary<string, string>> Dictionaries { get; } = [];
        public List<SpeechRequest> Spoken { get; } = [];
        public bool Fails { get; set; }

        public Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default)
        {
            if (Fails) throw new SpeechException("ElevenLabs could not speak: HTTP 500.");
            Spoken.Add(request);
            return Task.FromResult(new byte[] { 0xFF, 0xFB, (byte)Spoken.Count });
        }

        public List<IReadOnlyDictionary<string, string>> Added { get; } = [];

        public Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default)
        {
            Dictionaries.Add(aliases);
            return Task.FromResult(new DictionaryRef($"d{Dictionaries.Count}", "v1"));
        }

        public Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default)
        {
            Added.Add(aliases);
            return Task.FromResult(new DictionaryRef(dictionaryId, $"v{Added.Count + 1}"));
        }
    }

    internal static FakeLlmProvider Respeller() => new(request => JsonSerializer.Serialize(new
    {
        respellings = new object[]
        {
            new { word = "Deithkialwold", say = "Deeth-kyal-wold" }, new { word = "Manreach", say = "Man-reech" },
            new { word = "Gewick", say = "<script>" }, new { word = "Unasked", say = "Un-asked" },
        },
    }));

    [Fact]
    public void Words_AreTheScriptsNames_NotItsOrdinaryWords()
    {
        Assert.Equal(new[] { "Deithkialwold", "Manreach", "Gewick" }, Pronouncer.Words(Script));
        Assert.Equal(new[] { "Manreach" }, Pronouncer.Words("Manreach's host. Manreach’s banner. The Banner fell; a banner rose."));
    }

    [Fact]
    public async Task NewNames_AreAskedOnce_CheckedAndKeptForGood()
    {
        var manifest = new Manifest();
        var provider = Respeller();
        var log = new StringWriter();
        Assert.Equal(2, await Pronouncer.LearnAsync(provider, "claude-sonnet-5-5", manifest, Script, log));
        Assert.Equal("Deeth-kyal-wold", manifest.Pronunciations["Deithkialwold"]);
        Assert.False(manifest.Pronunciations.ContainsKey("Gewick")); // its answer was unusable: asked again
        Assert.Equal(1, manifest.PronunciationTries["Gewick"]);
        Assert.False(manifest.Pronunciations.ContainsKey("Unasked"));
        Assert.Equal(ModelTier.Narrate, provider.Requests.Single().Tier);

        Assert.Equal(0, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log));
        Assert.Equal(1, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log)); // third try: said as written
        Assert.Equal("Gewick", manifest.Pronunciations["Gewick"]);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal(0, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log));
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal(new[] { "Deithkialwold", "Manreach" }, Pronouncer.Rules(manifest).Keys);
    }

    [Fact]
    public void ARespelling_MustLookLikeItsName()
    {
        var asked = new[] { "Morwen", "Manreach", "Fiar", "Gewick" };
        var said = Pronouncer.Parse(JsonSerializer.Serialize(new
        {
            respellings = new object[]
            {
                new { word = "Morwen", say = "the traitor king" }, new { word = "Manreach", say = "Man-reech’s." },
                new { word = "Fiar", say = "Fiar" }, new { word = "Gewick", say = "Zzz-wick" },
            },
        }), asked)!;
        Assert.Equal(new[] { "Fiar", "Manreach" }, said.Keys.Order());
        Assert.Equal("Man-reech's", said["Manreach"]);
        Assert.False(Pronouncer.LooksLike("Deeth-kyal-wold-and-more-and-more-and-more", "Deithkialwold"));
        Assert.Null(Pronouncer.Parse("not json", asked));
    }

    [Fact]
    public async Task WithoutAModel_OrWithAGarbledAnswer_NothingIsLearned()
    {
        var manifest = new Manifest();
        Assert.Equal(0, await Pronouncer.LearnAsync(null, "m", manifest, Script, TextWriter.Null));
        Assert.Equal(0, await Pronouncer.LearnAsync(new FakeLlmProvider(_ => throw new LlmUnavailableException("down")), "m", manifest, Script, TextWriter.Null));
        Assert.Empty(manifest.PronunciationTries);
        Assert.Equal(0, await Pronouncer.LearnAsync(new FakeLlmProvider(_ => "{\"nope\":1}"), "m", manifest, Script, TextWriter.Null));
        Assert.Empty(manifest.Pronunciations);
    }

    [Fact]
    public async Task TheDictionary_IsMadeOnce_ThenExtended()
    {
        var manifest = new Manifest();
        var speech = new FakeSpeech();
        Assert.Empty(await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"));
        await Pronouncer.LearnAsync(Respeller(), "m", manifest, Script, TextWriter.Null);
        var first = await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor");
        Assert.Equal(first, await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"));
        Assert.Single(speech.Dictionaries);
        Assert.Empty(speech.Added);
        manifest.Pronunciations["Fiasmere"] = "Fee-as-meer";
        Assert.Equal(new DictionaryRef("d1", "v2"), (await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"))[0]);
        Assert.Equal(new[] { "Fiasmere" }, speech.Added.Single().Keys);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Pronouncer' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Broadcast/Speech/Pronouncer.cs`:
```csharp
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// How the world's invented names are said (M3). The first time a name appears in a script, the chronicler's model is
/// asked, with the other new names of the day, for a plain English respelling ("Deithkialwold" → "Deeth-kyal-wold"), or the
/// word itself for an ordinary word. An answer is used only if it looks like the name (hyphenated syllables, the same first
/// letter, a similar length), so a respelling can only change how a name sounds, never what the script says. Answers are
/// kept in the manifest for good, so a name sounds the same every morning; a word without a usable answer is asked again,
/// up to <see cref="MaxTries"/> times, then said as written.
/// </summary>
public static partial class Pronouncer
{
    /// <summary>New words asked about a morning; the rest wait for the next.</summary>
    public const int MaxNewWords = 60;

    public const int MaxTries = 3;

    public const string System = "You help a text-to-speech narrator say invented fantasy names. Answer every word. If it is an invented "
                                 + "name, give a respelling in plain English letters with hyphens between syllables (no spaces), the way an "
                                 + "English reader would say the name as it looks. If it is an ordinary English word or title, give the word "
                                 + "itself, unchanged.";

    /// <summary>Capitalised words that are plainly English, never asked about.</summary>
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "The", "And", "But", "This", "That", "Then", "There", "Their", "They", "When", "While", "After", "Before", "With", "From", "Elsewhere",
        "How", "Thus", "Until", "Remember", "Year", "Years", "Spring", "Summer", "Autumn", "Winter", "Kingdom", "Duchy", "County", "Barony",
        "Empire", "House", "Host", "War", "Peace", "Age", "Great", "Hall", "Holy", "Long", "Lord", "Lady", "King", "Queen", "Saint", "Order",
        "Sir", "Dame", "Duke", "Count", "Baron", "Emperor", "Empress", "Prince", "Princess", "Mage", "Knight", "High", "Priest", "Old", "New",
    };

    /// <summary>
    /// The names of a script worth asking about, in order of first use: capitalised words (possessives folded into their
    /// name), longer than three letters, never written in lower case in the script, and not plainly English.
    /// </summary>
    public static IReadOnlyList<string> Words(string script)
    {
        var lower = LowerWords().Matches(script).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        return Capitalised().Matches(script).Select(m => Possessive().Replace(m.Value, ""))
            .Where(w => w.Length > 3 && !Ordinary.Contains(w) && !lower.Contains(w.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Asks about the names of <paramref name="script"/> not yet known; returns how many were settled.</summary>
    public static async Task<int> LearnAsync(ILlmProvider? provider, string model, Manifest manifest, string script, TextWriter log,
        CancellationToken ct = default)
    {
        var fresh = Words(script).Where(w => !manifest.Pronunciations.ContainsKey(w)).Take(MaxNewWords).ToList();
        if (fresh.Count == 0 || provider is null) return 0;
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                respellings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new { word = new { type = "string" }, say = new { type = "string" } },
                        required = new[] { "word", "say" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "respellings" },
            additionalProperties = false,
        });
        var prompt = PromptText.Section("words", string.Join("\n", fresh.Select(w => PromptText.Data(w, 40))));
        var request = new LlmRequest(ModelTier.Narrate, "pronounce|" + string.Join(",", fresh), System, prompt, schema, MaxTokens: 2000);
        LlmResponse answer;
        try
        {
            answer = await provider.CompleteJsonAsync(request, model, ct).ConfigureAwait(false);
        }
        catch (LlmUnavailableException e)
        {
            log.WriteLine($"warning: no respellings today ({e.Message}); the names are asked about again tomorrow.");
            return 0;
        }

        var said = Parse(answer.Json, fresh) ?? new Dictionary<string, string>();
        var settled = 0;
        foreach (var word in fresh)
        {
            if (said.TryGetValue(word, out var say))
            {
                manifest.Pronunciations[word] = say;
                manifest.PronunciationTries.Remove(word);
                settled++;
            }
            else if ((manifest.PronunciationTries[word] = manifest.PronunciationTries.GetValueOrDefault(word) + 1) >= MaxTries)
            {
                manifest.Pronunciations[word] = word; // no usable answer after a few tries: said as written
                manifest.PronunciationTries.Remove(word);
                settled++;
            }
        }
        log.WriteLine($"Asked how to say {fresh.Count} new name(s): {said.Count(s => s.Key != s.Value)} respelled, "
                      + $"{said.Count(s => s.Key == s.Value)} said as written (${answer.CostUsd:0.0000}).");
        return settled;
    }

    /// <summary>
    /// The usable answers (a respelling that looks like its name, or the word itself), or null when the answer is not the
    /// expected shape at all. Curly apostrophes and trailing punctuation are tidied first.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Parse(string json, IReadOnlyCollection<string> asked)
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("respellings", out var list)
                || list.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("word", out var w) || !item.TryGetProperty("say", out var s)
                    || w.ValueKind != JsonValueKind.String || s.ValueKind != JsonValueKind.String) continue;
                var word = w.GetString()!;
                var say = s.GetString()!.Replace('’', '\'').Trim().TrimEnd('.', ',', ';', ':', '!', '?').Trim();
                if (asked.Contains(word) && (say == word || LooksLike(say, word))) said[word] = say;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return said;
    }

    /// <summary>A respelling of a name: hyphenated letters, the same first letter, about the same length.</summary>
    public static bool LooksLike(string say, string word)
    {
        if (!Respelling().IsMatch(say) || char.ToUpperInvariant(say[0]) != char.ToUpperInvariant(word[0])) return false;
        var (a, b) = (say.Count(char.IsLetter), word.Count(char.IsLetter));
        return a >= b * 0.6 && a <= b * 2.0;
    }

    /// <summary>The rules worth a dictionary: words said differently from how they are written.</summary>
    public static SortedDictionary<string, string> Rules(Manifest manifest) =>
        new(manifest.Pronunciations.Where(p => p.Key != p.Value).ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);

    /// <summary>
    /// The dictionary for today's speech: made once, then extended with the rules learned since (each extension is a new
    /// version); none while there are no rules.
    /// </summary>
    public static async Task<IReadOnlyList<DictionaryRef>> DictionaryAsync(ISpeech speech, Manifest manifest, string worldName, CancellationToken ct = default)
    {
        var rules = Rules(manifest);
        if (rules.Count == 0) return [];
        if (manifest.DictionaryId is null || manifest.DictionaryVersionId is null)
        {
            var made = await speech.CreateDictionaryAsync($"{worldName} names", rules, ct).ConfigureAwait(false);
            (manifest.DictionaryId, manifest.DictionaryVersionId) = (made.Id, made.VersionId);
            manifest.DictionaryWords = new SortedSet<string>(rules.Keys, StringComparer.Ordinal);
        }
        else if (rules.Where(r => !manifest.DictionaryWords.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value) is { Count: > 0 } added)
        {
            var extended = await speech.AddRulesAsync(manifest.DictionaryId, added, ct).ConfigureAwait(false);
            manifest.DictionaryVersionId = extended.VersionId;
            manifest.DictionaryWords.UnionWith(added.Keys);
        }
        return [new DictionaryRef(manifest.DictionaryId, manifest.DictionaryVersionId)];
    }

    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Lu}\p{L}+(?:['’-]\p{L}+)*")]
    private static partial Regex Capitalised();

    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Ll}+")]
    private static partial Regex LowerWords();

    [GeneratedRegex(@"['’]s$")]
    private static partial Regex Possessive();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z'-]*[A-Za-z]$")]
    private static partial Regex Respelling();
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PronouncerTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 640` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(broadcast): learn once how each invented name is said

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The narrator reads each episode

Spec §3–3.4, M3. After the episode, every day of the posting window still without narration is spoken, oldest first: within the monthly character budget (a ledger at the bucket's root that live and dry runs share, counted before each request), its new names learned, its dictionary current; the MP3 is stored (`episodes/<date>/narration.mp3`) and the `audio` step marked at once, and a narration found already stored is adopted, so speech is never paid for twice. Speech that fails leaves the step unmarked for the next run and the post goes out as text and map meanwhile; past the budget, or without `ELEVENLABS_API_KEY`, the episode goes out without narration and the log says why. Any speech error is a warning, never a stopped post. Discord posts attach the narration beside the map while everything sent stays under 8 MiB, and are sent again without it if a server refuses the size.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Speech/SpeechLedger.cs`
- Modify: `.github/workflows/broadcast.yml`, `README.md`, `broadcast.json`, `docs/broadcast-setup.md`, `sim/FantasyWorld.Broadcast/BroadcastApp.cs`, `sim/FantasyWorld.Broadcast/Channels/DiscordWebhook.cs`, `sim/FantasyWorld.Broadcast/DailyRun.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Broadcasting/BroadcastPartsTests.cs`, `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `DailyRun`, `DiscordWebhook` (M1–M2).
- Produces: `class SpeechLedger { Key; Characters }`; `DailyRun.NarrationKey(date)` and an optional `speech` argument; `IEpisodeChannel.PublishAsync(episode, map, audio, ct)`; `DiscordWebhook { AudioFile; MaxAttachmentBytes; Content(episode, username, map, audio) }`; workflow and docs.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Broadcasting/BroadcastPartsTests.cs` entirely with:
```csharp
using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class BroadcastPartsTests
{
    internal static Episode SampleEpisode(string script = "The chronicle of Aerenmoor, Year 12.") =>
        new("2026-10-27", "Aerenmoor", "Ilsa the Grey", 11, 12, 40, 100, 140, "The chronicle of Aerenmoor, Year 12.",
            null, "", [], null, "So it is told.", script);

    [Fact]
    public void Retention_KeepsTheLastDays_AndTheFirstOfEachMonth()
    {
        var keys = Enumerable.Range(0, 75).Select(d => BackupRetention.KeyFor(new DateOnly(2026, 8, 20).AddDays(d).ToString("yyyy-MM-dd")))
            .Append("backups/notes.txt").Append("world.fw.db").ToList();
        var deleted = BackupRetention.ToDelete(keys, "2026-11-02", keepDays: 30);
        var kept = keys.Except(deleted).ToList();
        Assert.Contains("backups/2026-08-20.fw.db", kept); // the first August backup
        Assert.Contains("backups/2026-09-01.fw.db", kept);
        Assert.Contains("backups/2026-10-01.fw.db", kept);
        Assert.Contains("backups/2026-10-04.fw.db", kept); // 30 days ending 2026-11-02
        Assert.Contains("backups/2026-10-03.fw.db", deleted);
        Assert.Contains("backups/notes.txt", kept);
        Assert.Equal(75 - 30 - 3, deleted.Count);
    }

    [Fact]
    public void Manifest_RoundTrips_AndMarksStepsPerDate()
    {
        var manifest = new Manifest();
        manifest.Record("2026-10-27", SampleEpisode());
        manifest.Day("2026-10-27").Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch;
        var back = Manifest.FromBytes(manifest.ToBytes());
        Assert.True(back.Day("2026-10-27").Done(Manifest.EpisodeStep));
        Assert.False(back.Day("2026-10-27").Done(Manifest.DiscordStep));
        Assert.Equal(12, back.Day("2026-10-27").ToYear);
        Assert.False(back.Day("2026-10-28").Done(Manifest.EpisodeStep));
        Assert.Empty(Manifest.FromBytes(null).Episodes);
    }

    [Fact]
    public void Config_HasSafeDefaults_AndRefusesWhatItCannotDo()
    {
        var config = BroadcastConfig.Parse("{}");
        Assert.Equal(("claude-sonnet-5-5", 0, true), (config.Llm.NarrateModel, config.Llm.StoryBeatsPerDay, config.Llm.Narrate));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "llm": { "monthlyCapUsd": -1 } }"""));
        Assert.Equal("2026-10-27", config.DateAt(new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-10-26", config.DateAt(new DateTimeOffset(2026, 10, 26, 16, 59, 0, TimeSpan.Zero)));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "language": "th" }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "maxCharacters": 5000 }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "livePrefix": "x/", "stagingPrefix": "x/" }"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => BroadcastConfig.Parse("""{ "voiceIdd": "x" }"""));
    }

    [Fact]
    public void TheCheckedInConfig_IsValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "broadcast.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var config = BroadcastConfig.Load(Path.Combine(dir!.FullName, "broadcast.json"));
        Assert.Equal("Asia/Bangkok", config.TimeZone);
    }

    [Fact]
    public async Task Discord_PostsOneEmbed_ThatCanPingNobody()
    {
        var payload = DiscordWebhook.Payload(SampleEpisode(new string('x', 5000)), "The Chronicle");
        var embed = payload["embeds"]![0]!;
        Assert.Equal("Aerenmoor, Years 11–12", (string)embed["title"]!);
        Assert.Equal(DiscordWebhook.MaxDescription, ((string)embed["description"]!).Length);
        Assert.Empty(payload["allowed_mentions"]!["parse"]!.AsArray());

        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"), (HttpStatusCode.TooManyRequests, ""));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "The Chronicle");
        await discord.PublishAsync(SampleEpisode(), null, null);
        Assert.Null(JsonNode.Parse(http.Seen[0].Body)!["embeds"]![0]!["image"]);
        Assert.Equal("https://discord.test/api/webhooks/1/SECRET-TOKEN?wait=true", http.Seen[0].Request.RequestUri!.ToString());
        Assert.Equal("The Chronicle", (string)JsonNode.Parse(http.Seen[0].Body)!["username"]!);
        var error = await Assert.ThrowsAsync<ChannelException>(() => discord.PublishAsync(SampleEpisode(), null, null));
        Assert.Equal("Discord answered HTTP 429.", error.Message);

        var slow = new DiscordWebhook(new HttpClient(new ThrowingHttp()), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "x");
        var late = await Assert.ThrowsAsync<ChannelException>(() => slow.PublishAsync(SampleEpisode(), null, null));
        Assert.DoesNotContain("SECRET", late.Message);
    }

    [Fact]
    public async Task Discord_SendsTheMapAsTheEmbedsImage_InOneRequest()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/T", "The Chronicle");
        byte[] map = [0x89, 0x50, 0x4e, 0x47, 1, 2, 3];
        await discord.PublishAsync(SampleEpisode(), map, null);
        var (request, body) = http.Seen.Single();
        Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
        var text = System.Text.Encoding.Latin1.GetString(body!);
        Assert.Contains("name=payload_json", text);
        Assert.Contains("\"url\":\"attachment://map.png\"", text);
        Assert.Contains("filename=map.png", text);
        Assert.Contains(System.Text.Encoding.Latin1.GetString(map), text);
        Assert.DoesNotContain("narration.mp3", text);
    }

    [Fact]
    public async Task Discord_AttachesTheNarration_UnlessItIsTooBig()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/T", "The Chronicle");
        await discord.PublishAsync(SampleEpisode(), [0x89, 0x50], [0xFF, 0xFB, 0x90]);
        var text = System.Text.Encoding.Latin1.GetString(http.Seen[0].Body!);
        Assert.Contains("name=\"files[0]\"; filename=map.png", text);
        Assert.Contains("name=\"files[1]\"; filename=narration.mp3", text);
        Assert.Contains("audio/mpeg", text);

        using var big = DiscordWebhook.Content(SampleEpisode(), "x", null, new byte[DiscordWebhook.MaxAttachmentBytes + 1]);
        Assert.Equal("application/json", big.Headers.ContentType!.MediaType);
        using var together = DiscordWebhook.Content(SampleEpisode(), "x", new byte[DiscordWebhook.MaxAttachmentBytes / 2], new byte[DiscordWebhook.MaxAttachmentBytes / 2]);
        Assert.DoesNotContain("narration.mp3", await together.ReadAsStringAsync());

        // A server that refuses the size after all: the post goes again without the narration.
        var refusing = new StorageTests.FakeHttp((HttpStatusCode.RequestEntityTooLarge, ""), (HttpStatusCode.OK, "{}"));
        await new DiscordWebhook(new HttpClient(refusing), "https://discord.test/api/webhooks/1/T", "x").PublishAsync(SampleEpisode(), [1], [2]);
        Assert.Equal(2, refusing.Seen.Count);
        Assert.DoesNotContain("narration.mp3", System.Text.Encoding.Latin1.GetString(refusing.Seen[1].Body!));
    }

    private sealed class ThrowingHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new TaskCanceledException();
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs` entirely with:
```csharp
using System.Net;
using System.Text;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
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
        Func<LlmSettings, ILlmProvider?>? providers = null, ISpeech? speech = null, BroadcastConfig? config = null) =>
        new DailyRun(config ?? Config, _root, [channel ?? _discord], () => now, _log, options, providers ?? (_ => null), speech).RunAsync();

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
            var brief = request.Prompt;
            var script = brief[(brief.IndexOf('\n') + 1)..brief.LastIndexOf("</brief>", StringComparison.Ordinal)].Trim();
            return System.Text.Json.JsonSerializer.Serialize(new { text = script.Replace("Elsewhere in the chronicle:", "And elsewhere, in brief:") });
        });
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), providers: settings => settings.Provider == "anthropic" ? provider : null));
        var request = Assert.Single(provider.Requests); // the script only: story beats are off
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
            Assert.Equal(1, store.LoadJournal().Count);
            Assert.Equal("claude-sonnet-5-5", store.LoadLlmSettings().ModelFor(ModelTier.Narrate));
            Assert.Equal(0, store.LoadLlmSettings().StoryBeatsPerDay);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task TheNarrator_SpeaksEachEpisodeOnce_WithItsNamesLearned()
    {
        var speech = new PronouncerTests.FakeSpeech();
        var respeller = PronouncerTests.Respeller();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), providers: _ => respeller, speech: speech));
        var spoken = Assert.Single(speech.Spoken);
        Assert.Equal(("uju3wxzG5OhpWcoi3SMy", "eleven_v4_turbo"), (spoken.VoiceId, spoken.Model));
        var audio = await Get("live/" + DailyRun.NarrationKey("2026-10-27"));
        Assert.Equal(audio, _discord.Audio.Single());
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        Assert.True(manifest.Day("2026-10-27").Done(Manifest.AudioStep));
        Assert.Equal(_discord.Posts[0].Script.Length, SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"]);
        Assert.NotEmpty(manifest.PronunciationTries.Keys.Concat(manifest.Pronunciations.Keys)); // the names were asked about
        Assert.Contains("Narrated the episode of 2026-10-27", _log.ToString());

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech, providers: _ => respeller));
        Assert.Single(speech.Spoken);
        Assert.Single(respeller.Requests, r => r.Key.StartsWith("pronounce|", StringComparison.Ordinal)); // names already known
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
        Assert.Equal(2, speech.Spoken.Count); // yesterday's narration, then today's
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
        Assert.Single(speech.Spoken);
        // Stopped after storing the narration but before marking it: the next run adopts it.
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        manifest.Day("2026-10-27").Steps.Remove(Manifest.AudioStep);
        await _root.PutAsync("live/" + Manifest.Key, manifest.ToBytes(), "application/json");
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech));
        Assert.Single(speech.Spoken);
        Assert.Contains("already stored", _log.ToString());
    }

    [Fact]
    public async Task DryRuns_ShareTheLiveBudget()
    {
        var tight = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "voice": { "monthlyCharacterBudget": 5000 } }""");
        var speech = new PronouncerTests.FakeSpeech();
        await Run(Morning, new RunOptions(Create: true), speech: speech, config: tight);
        var used = SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"];
        await Run(Morning, new RunOptions(DryRun: true), speech: speech, config: tight, channel: new CountingChannel());
        Assert.Equal(2, speech.Spoken.Count);
        Assert.Equal(2 * used, SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"]);
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
        await File.WriteAllTextAsync(config.Path, """{ "seed": 3, "size": "tiny" }""");
        var env = new Dictionary<string, string> { ["DISCORD_TEST_WEBHOOK_URL"] = "https://discord.test/api/webhooks/9/SECRET-TOKEN" };
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
        Assert.DoesNotContain("SECRET-TOKEN", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["publish"], output, () => Morning, env.GetValueOrDefault));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0535: 'DailyRunTests.CountingChannel' does not implement interface member 'IEpisodeChannel.PublishAsync(Episode, byte`.

- [x] **Step 3: Implement**

Replace `.github/workflows/broadcast.yml` entirely with:
```yaml
name: broadcast

# The daily broadcast (sub-project 6): the world lives one more year and the episode is published, at 05:30 in
# Bangkok every morning. GitHub runs scheduled workflows only from the default branch, and may start them late.
on:
  schedule:
    - cron: '30 22 * * *'
  workflow_dispatch:
    inputs:
      dry-run:
        description: 'Work under staging/ and post to the test Discord channel'
        type: boolean
        default: true
      create:
        description: 'Begin a new world if none is stored (a scheduled run never does)'
        type: boolean
        default: false

# One run at a time, and a run in progress is never cancelled: two runs must not touch the world together.
concurrency:
  group: broadcast
  cancel-in-progress: false

permissions:
  contents: read

jobs:
  broadcast:
    runs-on: ubuntu-latest
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Build
        run: dotnet build sim/FantasyWorld.Broadcast --configuration Release
      - name: Broadcast
        env:
          R2_ACCOUNT_ID: ${{ secrets.R2_ACCOUNT_ID }}
          R2_BUCKET: ${{ secrets.R2_BUCKET }}
          R2_ACCESS_KEY_ID: ${{ secrets.R2_ACCESS_KEY_ID }}
          R2_SECRET_ACCESS_KEY: ${{ secrets.R2_SECRET_ACCESS_KEY }}
          DISCORD_WEBHOOK_URL: ${{ secrets.DISCORD_WEBHOOK_URL }}
          DISCORD_TEST_WEBHOOK_URL: ${{ secrets.DISCORD_TEST_WEBHOOK_URL }}
          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
          ELEVENLABS_API_KEY: ${{ secrets.ELEVENLABS_API_KEY }}
          DRY_RUN: ${{ inputs.dry-run }}
          CREATE: ${{ inputs.create }}
          REF: ${{ github.ref }}
          DEFAULT_REF: refs/heads/${{ github.event.repository.default_branch }}
        run: |
          if [ "$DRY_RUN" != "true" ] && [ "$REF" != "$DEFAULT_REF" ]; then
            echo "error: the live broadcast runs only from the default branch; tick dry-run to try a branch."
            exit 1
          fi
          args=(run --config broadcast.json)
          if [ "$DRY_RUN" = "true" ]; then args+=(--dry-run); fi
          if [ "$CREATE" = "true" ]; then args+=(--create); fi
          dotnet run --no-build --configuration Release --project sim/FantasyWorld.Broadcast -- "${args[@]}"

  # GitHub turns off the schedule of a public repository after 60 days without activity: the first scheduled run of
  # each month (Bangkok time) commits a one-line heartbeat, unless broadcast.json turns keepAlive off. A month whose
  # heartbeat failed is caught up by the next morning's run.
  keep-alive:
    needs: broadcast
    if: always() && github.event_name == 'schedule'
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
      - name: Monthly heartbeat
        run: |
          month="$(TZ=Asia/Bangkok date +%Y-%m)"
          if [ "$(jq -r .keepAlive broadcast.json)" != "true" ] || [ "$(cat .github/broadcast-heartbeat 2>/dev/null)" = "$month" ]; then
            echo "No heartbeat needed."
            exit 0
          fi
          echo "$month" > .github/broadcast-heartbeat
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add .github/broadcast-heartbeat
          git commit -m "chore: broadcast keep-alive [skip ci]" || exit 0
          git push || (git pull --rebase -q && git push)
```

Replace `README.md` entirely with:
````markdown
# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `sim/FantasyWorld.Client/` — the game's engine-free logic (session, terrain, layout), tested with the rest
- `sim/FantasyWorld.Broadcast/` — the daily broadcast: R2 storage, the morning run and Discord (`broadcast.json`)
- `game/` — the Godot 4.3 diorama client
- `docs/superpowers/specs/` — design specs
- `docs/superpowers/plans/` — implementation plans

## The game

![The morning digest over the diorama](docs/images/digest.png)

The world is a floating diorama: drag to orbit, scroll to descend from the whole realm to a single town. On launch
the world catches up on the time you were away and opens on the *morning digest*: the year, what changed, the
headline and *why did this happen?* Every name is a link: people open their life and ties, places fly the camera
there, realms and artifacts open their pages, and any event opens the chain of causes that led to it. The *map*
adds layers, a history slider back to Year 1, and a card for every settlement. While the game is open the world
lives on (one world day every four real minutes) and announces what matters.

![A region seen from above, with realm borders and settlements](docs/images/region.png)

War is drawn like a strategy map: each host's banner (in its realm's colours) marches from region to region along
a broad campaign arrow toward its target, attackers in red and defenders in blue; fronts carry teeth facing the
attacker, occupied land is hatched, and crossed swords mark this year's battles. Click a banner for its commander,
a battle for why it was fought. Knights ride with their realm's host, and battles can wound, kill or make them famous.

![A host on the march: campaign arrow, banner, front and occupied land](docs/images/war.png)
![Why a war began: the chain of causes](docs/images/why.png)

Run it with [Godot 4.3 .NET](https://godotengine.org/download/archive/4.3-stable/) and the .NET 8 SDK:

```bash
dotnet build game/FantasyWorld.Game.csproj
godot --path game                                   # first launch creates a world with a century of prehistory
godot --path game -- --world worlds/aerenmoor.fw.db # or open a world made with the CLI
```

Keys: **Esc** back · **H** home (the digest) · **M** the map. Worlds live in Godot's user folder unless given.

Checks: `godot --headless --path game res://tests/HeadlessChecks.tscn` drives every screen on a fresh world and
exits non-zero on failure (CI runs it). `godot --path game res://tests/Showcase.tscn -- --shots <dir>` renders a
full-size world from the angles above, and `godot --path game --write-movie war.avi --fixed-fps 30 res://tests/WarMovie.tscn`
films a host on the march (both need a display).

## Myths, magic and structures

The world grows its own legends, each with a cause you can follow:

- **Beasts** rise where the land is dangerous, more often after a ley surge: a wyrm in the mountains, a hydra in the
  swamp. They raid nearby towns until a hero hunts them by name; some become legends, very old ones slumber.
- **Trade roads** join markets at peace with each other; wars, occupations and danger sever them, and peace reopens
  them (the map's *Trade routes* layer).
- **Faiths** spread from realms and neighbours, held back by temples and saints' relics; a realm whose lands have
  turned takes up the new faith. A faith that loses its holy land grows zealous, may wage a holy war to take it
  back, and may split when a priest founds a heresy. Devout notables of renown become saints.
- **Magic** ebbs and flows in slow ley tides. Mage towers train mages where the law allows; a surge at a tower may go
  wild (a storm, a blight, a portal that lets danger in), and realms tighten their law after it. Where magic is
  outlawed, mages flee or burn.
- **Great works** take years: an architect, a patron, delays from war, plague and famine, collapses when rushed;
  towns that knew siege raise fortresses, and fallen works are restored.
- **Labyrinths** open in wild, ley-charged land with three to seven levels and a guardian on the deepest. Delvers go
  down level by level; slaying the guardian and taking the last relic clears it, leaving a ruin or a shrine.
- Rivals quarrel over their borders, and rulers who crave renown seek glory against weaker neighbours.

![A town with its temple and mage tower, a beast in the hills and a labyrinth's walls](docs/images/town.png)

## Run

```bash
dotnet test sim
dotnet run --project sim/FantasyWorld.Cli -- new worlds/aerenmoor.fw.db            # medium world (100 regions)
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db            # ASCII map (--layer regions)
dotnet run --project sim/FantasyWorld.Cli -- catchup worlds/aerenmoor.fw.db        # simulate real elapsed time
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 50y    # debug fast-forward
dotnet run --project sim/FantasyWorld.Cli -- regions worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- region worlds/aerenmoor.fw.db 12
dotnet run --project sim/FantasyWorld.Cli -- why worlds/aerenmoor.fw.db <event-id>
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/aerenmoor.fw.db       # living notables
dotnet run --project sim/FantasyWorld.Cli -- life worlds/aerenmoor.fw.db <char-id>    # a biography
dotnet run --project sim/FantasyWorld.Cli -- artifacts worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- artifact worlds/aerenmoor.fw.db <id>     # powers and provenance
dotnet run --project sim/FantasyWorld.Cli -- realms worlds/aerenmoor.fw.db           # kingdoms and their vassals
dotnet run --project sim/FantasyWorld.Cli -- realm worlds/aerenmoor.fw.db <id>        # laws, council, treaties, eras
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- war worlds/aerenmoor.fw.db <id>          # casus belli and the course of the war
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db --layer realms
dotnet run --project sim/FantasyWorld.Cli -- tell worlds/aerenmoor.fw.db <event-id>    # the why-chain as prose
dotnet run --project sim/FantasyWorld.Cli -- stats worlds/aerenmoor.fw.db             # world summary and LLM spend
```

## Story beats from Claude

The world runs fully offline by default: rules make every choice and templates tell the story. To let Claude
decide notables' choices, arbitrate duels, hunts and delves, name forged artifacts, wars and eras, and narrate
with `fw tell`:

```bash
export ANTHROPIC_API_KEY=...        # never stored in the world file
dotnet run --project sim/FantasyWorld.Cli -- llm worlds/aerenmoor.fw.db --provider anthropic --cap 5
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 2y
```

Defaults: `claude-opus-5-5` for every job (`--model narrate=<model>` changes one), 60 story beats and 200
narrations per rolling day, and a $10 cap per 30 days; past it, or without credentials, the rules take over.
`--provider openrouter` uses `OPENROUTER_API_KEY` instead. Every answer is journaled in the world file, so
reloading or replaying a world never asks again, and `fw why` shows the motives and accounts it gave.

Each world has a chronicler (a name, a temperament and a diction, the same every time the world opens) whose voice
and house style every narration follows. Narration may use only the facts it is given: one that invents a name or a
number is rejected and journaled, and the templates speak instead. The template voice is a product in its own right
(articles, numbers in words, several phrasings per event chosen by its id, headlines with a body that adds what the
title leaves out, and the story of why as one paragraph); its structure is checked on every test run against a real
history and a golden narration.

Live quality evals grade the LLM voice with a judge model on fixed seeds, within a cost cap:

```bash
dotnet run --project sim/FantasyWorld.Cli -- eval narration --cap 2      # events and chains: faithful, clear, voice, varied
dotnet run --project sim/FantasyWorld.Cli -- eval decisions --draws 40   # choices in character; arbitrations against their odds
```

Each run prints the mean scores and saves a report under `evals/`; no call is made once a run has spent its cap (default $2).

`--size tiny` makes a 64×64 world with 12 regions for quick experiments (and no prehistory; medium and large
worlds start with 100 years of it, `--prehistory <years>` to change).

## The chronicle

```bash
dotnet run --project sim/FantasyWorld.Cli -- digest worlds/aerenmoor.fw.db              # headlines since you last looked
dotnet run --project sim/FantasyWorld.Cli -- follow worlds/aerenmoor.fw.db char#12      # their deeds lead the digest
dotnet run --project sim/FantasyWorld.Cli -- live worlds/aerenmoor.fw.db --minutes 30   # watch it unfold in real time
dotnet run --project sim/FantasyWorld.Cli -- structures worlds/aerenmoor.fw.db          # landmarks, labyrinths and ruins
```

Every screen of the game also exists as data, for clients (the Godot diorama) and for checking without one:

```bash
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db digest            # the morning digest, as JSON
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db map --as-of 150   # the political map in Year 150
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db why <event-id>     # a why-chain as a layered graph
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db character <id>     # also: family, chronicle, realm, artifact
```

`catchup` ends with the digest. Minor events older than 50 years are condensed into one summary per decade as
the world runs. Every command that changes a world first keeps a
backup of the last three sessions next to it (`.bak1`–`.bak3`). Benchmarks:
`dotnet run -c Release --project sim/FantasyWorld.Bench -- --filter '*'`.

## The daily broadcast

Every morning at 05:30 Bangkok time, GitHub Actions pulls the world from Cloudflare R2, lets it live the year that passed,
builds the episode (the script written by the world's chronicler, Claude, held to the facts; the template voice when it
cannot), has it read aloud by ElevenLabs, and posts it to Discord with the narration and the political map. A rerun the same day posts nothing new. Setup:
[docs/broadcast-setup.md](docs/broadcast-setup.md).

```bash
dotnet run --project sim/FantasyWorld.Cli -- episode worlds/aerenmoor.fw.db --out ep/ --narrate   # episode.json and script.txt since the last one
dotnet run --project sim/FantasyWorld.Broadcast -- run --dry-run --create --local bucket/   # the whole morning, with a local folder for R2
dotnet run --project sim/FantasyWorld.Broadcast -- map worlds/aerenmoor.fw.db map.png     # the map as the broadcast posts it
```
````

Replace `broadcast.json` entirely with:
```json
{
  "worldKey": "world.fw.db",
  "seed": 20261027,
  "size": "medium",
  "timeZone": "Asia/Bangkok",
  "language": "en",
  "livePrefix": "live/",
  "stagingPrefix": "staging/",
  "backupDays": 30,
  "stories": 5,
  "maxCharacters": 3600,
  "discordName": "The Chronicle",
  "llm": {
    "narrate": true,
    "narrateModel": "claude-sonnet-5-5",
    "storyBeatsPerDay": 0,
    "narrationsPerDay": 10,
    "monthlyCapUsd": 5
  },
  "voice": {
    "speak": true,
    "voiceId": "uju3wxzG5OhpWcoi3SMy",
    "model": "eleven_v4_turbo",
    "outputFormat": "mp3_44100_128",
    "maxCharactersPerRequest": 5000,
    "monthlyCharacterBudget": 150000,
    "respell": true
  },
  "keepAlive": true
}
```

Replace `docs/broadcast-setup.md` entirely with:
````markdown
# Daily broadcast: owner setup

The broadcast runs in GitHub Actions (`.github/workflows/broadcast.yml`) every morning at 05:30 Bangkok time. The world
lives in Cloudflare R2, and each episode is posted to Discord. Nothing runs on your computer. You set it up once.

## 1. Cloudflare R2

1. In the Cloudflare dashboard, open **R2** and create a bucket (for example `fantasy-world`). Keep it private: the
   world file and its backups are not for the public.
2. Under **R2 → Manage API tokens**, create a token with **Object Read & Write** on that bucket only. Note the
   **Access Key ID** and **Secret Access Key** (shown once), and your **Account ID** (on the R2 overview page).

## 2. Discord

In your server: **Channel settings → Integrations → Webhooks → New webhook**, then **Copy webhook URL**. Make one for the
real channel, and (recommended) one for a private test channel used by dry runs.

## 3. GitHub secrets

In the repository: **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|---|---|
| `R2_ACCOUNT_ID` | Cloudflare account id |
| `R2_BUCKET` | the bucket's name |
| `R2_ACCESS_KEY_ID` | the token's access key id |
| `R2_SECRET_ACCESS_KEY` | the token's secret access key |
| `DISCORD_WEBHOOK_URL` | the real channel's webhook |
| `DISCORD_TEST_WEBHOOK_URL` | the test channel's webhook (optional; without it a dry run posts nowhere) |
| `ELEVENLABS_API_KEY` | from elevenlabs.io → your profile → API Keys; the narrator reads each episode with it (without it, the post goes out as text and map) |
| `ANTHROPIC_API_KEY` | from console.anthropic.com → API Keys; the chronicler writes each script with it (without it, the template script ships) |

Secrets are only ever read from the environment. They are never written to R2, the world file, the logs or the repository.

## 4. Start it

1. Merge to the default branch. GitHub runs scheduled workflows only from there, and a run that is not a dry run is
   refused on any other branch.
2. **Actions → broadcast → Run workflow** with `dry-run` ticked and `create` ticked. This makes a staging world under
   `staging/` and posts to the test channel. Read the episode there.
3. Run it again with `dry-run` unticked and `create` ticked. This begins the real world under `live/` and posts the first
   episode. From then on the schedule takes over; never tick `create` again unless you want to start over after
   deleting `live/world.fw.db`.
4. Watch three mornings in a row. Each should post one episode. Re-running the workflow the same day posts nothing new.

## Settings (`broadcast.json`)

| Field | Default | Meaning |
|---|---|---|
| `worldKey` | `world.fw.db` | the world's name in the bucket, under the prefix |
| `seed`, `size` | `20261027`, `medium` | the world a `create` run begins |
| `timeZone` | `Asia/Bangkok` | episodes are dated in this zone |
| `language` | `en` | only English exists so far |
| `livePrefix`, `stagingPrefix` | `live/`, `staging/` | where real and dry runs keep their files |
| `backupDays` | `30` | daily backups kept; the first backup of each month is kept for good |
| `stories`, `maxCharacters` | `5`, `3600` | episode length (about 2–4 minutes read aloud) |
| `discordName` | `The Chronicle` | the name posts appear under |
| `llm.narrate` | `true` | the chronicler (Claude) writes the script; off, the template script ships |
| `llm.narrateModel` | `claude-sonnet-5-5` | the model that writes it |
| `llm.storyBeatsPerDay` | `0` | the sim's own LLM decisions while it catches up; off by default, so the daily cost is one script (a few cents) |
| `voice.speak` | `true` | ElevenLabs reads each episode; the MP3 is attached to the post and stored |
| `voice.voiceId`, `voice.model` | Michael C. Vincent (`uju3wxzG5OhpWcoi3SMy`), `eleven_v4_turbo` | the narrator's voice and model (the owner's choices) |
| `voice.maxCharactersPerRequest` | `5000` | a script longer than this is spoken in parts and joined (a day's script is at most 3,600) |
| `voice.monthlyCharacterBudget` | `150000` | characters spoken per calendar month (about 40 days of episodes); past it, episodes go out without narration |
| `voice.respell` | `true` | Claude respells each new invented name once, so the narrator says it the same way every day |
| `llm.narrationsPerDay`, `llm.monthlyCapUsd` | `10`, `5` | budget and spending cap over 30 days; past them, the template script ships |
| `keepAlive` | `true` | a monthly heartbeat commit, so GitHub keeps the schedule of a public repository; turn it off for a private one |

## What is in the bucket

```
live/world.fw.db                       the world (a consistent single-file copy)
live/manifest.json                     per date, which steps finished: a rerun repeats none of them
live/episodes/2026-10-27/episode.json  the episode as data (years, headline, stories, why)
live/episodes/2026-10-27/script.txt    the script as posted
live/episodes/2026-10-27/map.png       the political map that morning, posted as the embed's image
live/episodes/2026-10-27/narration.mp3 the episode read aloud, attached to the post
live/backups/2026-10-27.fw.db          the world as it was before that morning's run
```

## When something goes wrong

- **A red run with "There is no world"**: the bucket is empty or the R2 secrets point somewhere else. A scheduled run never
  begins a new world, so check the secrets.
- **"Discord answered HTTP …"**: the episode was saved and the world moved on. The post is made by the next run, before
  that morning's own (re-run the workflow to post it now); posts missed for up to a week are made up, oldest first. A
  post is never made twice, except when Discord took it but timed out before answering.
- **"could not be narrated"**: the post went out as text and map; the narration is made by the next run (and kept for the
  feed). Check the ElevenLabs key, credits and the voice id. **"speech budget"**: raise `voice.monthlyCharacterBudget`.
- **A run stopped part-way** (a timeout, an R2 hiccup): re-run it. Each step it finished is recorded, and the stored world
  says which day's episode it holds, so nothing is told twice or skipped.
- **The world misbehaves**: copy a file from `live/backups/` over `live/world.fw.db` and leave `live/manifest.json` alone.
  The next run carries on from the restored world. Never delete the manifest: it is what stops a day being posted twice.
````

Replace `sim/FantasyWorld.Broadcast/BroadcastApp.cs` entirely with:
```csharp
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast;

/// <summary>
/// <c>fw-broadcast run [--dry-run] [--create] [--config broadcast.json] [--local &lt;dir&gt;]</c>. Secrets come only
/// from the environment: R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, DISCORD_WEBHOOK_URL and, for
/// dry runs, DISCORD_TEST_WEBHOOK_URL. <c>--local</c> uses a directory instead of R2.
/// </summary>
public static class BroadcastApp
{
    public const string Usage = """
        usage: fw-broadcast run [--dry-run] [--create] [--config <broadcast.json>] [--local <dir>]
               fw-broadcast map <world-file> <out.png>   the political map as the broadcast posts it
          --dry-run   every step, under the staging prefix, posting to DISCORD_TEST_WEBHOOK_URL (if set)
          --create    begin a new world when none is stored (never done by a scheduled run)
          --local     keep the world in a local directory instead of R2
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, Func<DateTimeOffset> clock,
        Func<string, string?> env, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (args is ["map", var worldPath, var pngPath])
            return Map(worldPath, pngPath, output);
        if (args is not ["run", ..] || args.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                                             && a is not ("--dry-run" or "--create" or "--config" or "--local")))
        {
            output.WriteLine(Usage);
            return DailyRun.Failed;
        }
        var options = new RunOptions(args.Contains("--dry-run"), args.Contains("--create"));

        BroadcastConfig config;
        try
        {
            config = BroadcastConfig.Load(Option(args, "--config") ?? "broadcast.json");
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or TimeZoneNotFoundException)
        {
            output.WriteLine($"error: broadcast.json: {e.Message}");
            return DailyRun.Failed;
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);

        IObjectStore? store = Option(args, "--local") is { } dir ? new LocalObjectStore(dir) : R2ObjectStore.FromEnvironment(http, env);
        if (store is null)
        {
            output.WriteLine("error: R2 is not configured. Set R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY.");
            return DailyRun.Failed;
        }

        var webhookName = options.DryRun ? "DISCORD_TEST_WEBHOOK_URL" : "DISCORD_WEBHOOK_URL";
        var channels = new List<IEpisodeChannel>();
        if (env(webhookName) is { Length: > 0 } webhook)
            channels.Add(new DiscordWebhook(http, webhook, config.DiscordName));
        else if (!options.DryRun)
        {
            output.WriteLine($"error: {webhookName} is not set, so the episode could not be posted.");
            return DailyRun.Failed;
        }

        // The LLM key, like every secret, comes from the environment given (never the process's own, so tests cannot spend).
        return await new DailyRun(config, store, channels, clock, output, options,
            settings => Llm.LlmProviders.Create(settings, env, ScriptTimeout), Speech.ElevenLabs.FromEnvironment(http, env)).RunAsync(ct).ConfigureAwait(false);
    }

    private static int Map(string worldPath, string pngPath, TextWriter output)
    {
        if (!File.Exists(worldPath))
        {
            output.WriteLine($"error: {worldPath} does not exist.");
            return DailyRun.Failed;
        }
        using var store = Sim.Persist.WorldStore.Open(worldPath);
        var (world, log, _) = store.Load();
        File.WriteAllBytes(pngPath, Media.MapPainter.Png(world, log));
        output.WriteLine($"Map written to {pngPath}.");
        return DailyRun.Ok;
    }

    /// <summary>A whole script with adaptive thinking can take minutes; the run itself has 45.</summary>
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
```

Replace `sim/FantasyWorld.Broadcast/Channels/DiscordWebhook.cs` entirely with:
```csharp
using System.Text;
using System.Text.Json.Nodes;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast.Channels;

/// <summary>
/// Somewhere an episode is published, with its map and its narration when there are. Throws <see cref="ChannelException"/> when it
/// could not be.
/// </summary>
public interface IEpisodeChannel
{
    string Name { get; }

    Task PublishAsync(Episode episode, byte[]? map, byte[]? audio, CancellationToken ct = default);
}

public sealed class ChannelException(string message) : Exception(message);

/// <summary>
/// Posts the episode to a Discord channel through a webhook: one embed with the script, and the map as its image (sent
/// as an attachment in the same request). The webhook URL is a secret (it carries its token), so errors name the status
/// code only.
/// </summary>
public sealed class DiscordWebhook(HttpClient http, string webhookUrl, string username) : IEpisodeChannel
{
    /// <summary>Discord's limits on an embed.</summary>
    public const int MaxTitle = 256, MaxDescription = 4096;

    public string Name => "discord";

    public const string MapFile = "map.png", AudioFile = "narration.mp3";

    /// <summary>
    /// Discord's upload limit for a webhook (8 MiB, the smallest across server tiers), for everything sent together; when
    /// the narration would pass it, the narration is left off.
    /// </summary>
    public const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public static JsonObject Payload(Episode episode, string username, bool withMap = false)
    {
        var payload = Embed(episode, username);
        if (withMap) payload["embeds"]![0]!["image"] = new JsonObject { ["url"] = $"attachment://{MapFile}" };
        return payload;
    }

    private static JsonObject Embed(Episode episode, string username)
    {
        var years = episode.FromYear == episode.ToYear ? $"Year {episode.ToYear}" : $"Years {episode.FromYear}–{episode.ToYear}";
        return new JsonObject
        {
            ["username"] = username,
            // Names come from the world, so nothing in a post may ping anyone.
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
            ["embeds"] = new JsonArray
            {
                new JsonObject
                {
                    ["title"] = Clip($"{episode.WorldName}, {years}", MaxTitle),
                    ["description"] = Clip(episode.Script, MaxDescription),
                    ["footer"] = new JsonObject { ["text"] = $"Told by {episode.Chronicler} · {episode.EventCount} events · {episode.Date}" },
                },
            },
        };
    }

    public static HttpContent Content(Episode episode, string username, byte[]? map, byte[]? audio = null)
    {
        var json = Payload(episode, username, map is not null).ToJsonString();
        if (audio is not null && Encoding.UTF8.GetByteCount(json) + (map?.Length ?? 0) + audio.Length > MaxAttachmentBytes) audio = null;
        if (map is null && audio is null) return new StringContent(json, Encoding.UTF8, "application/json");
        var form = new MultipartFormDataContent
        {
            { new StringContent(json, Encoding.UTF8, "application/json"), "payload_json" },
        };
        var files = 0;
        void Attach(byte[] bytes, string type, string name)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            form.Add(file, $"files[{files++}]", name);
        }
        if (map is not null) Attach(map, "image/png", MapFile);
        if (audio is not null) Attach(audio, "audio/mpeg", AudioFile);
        return form;
    }

    public async Task PublishAsync(Episode episode, byte[]? map, byte[]? audio, CancellationToken ct = default)
    {
        var url = webhookUrl + (webhookUrl.Contains('?') ? "&" : "?") + "wait=true";
        HttpResponseMessage response;
        try
        {
            using (var content = Content(episode, username, map, audio))
                response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
            // Too large for this server after all: the post goes out without the narration.
            if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge && audio is not null)
            {
                response.Dispose();
                using var smaller = Content(episode, username, map, null);
                response = await http.PostAsync(url, smaller, ct).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException e)
        {
            throw new ChannelException($"Discord could not be reached ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChannelException("Discord did not answer in time (the post may have gone out).");
        }
        using (response)
            if (!response.IsSuccessStatusCode)
                throw new ChannelException($"Discord answered HTTP {(int)response.StatusCode}.");
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
```

Replace `sim/FantasyWorld.Broadcast/DailyRun.cs` entirely with:
```csharp
using System.Globalization;
using System.Text;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Cli;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Llm;
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
                var dictionaries = await Pronouncer.DictionaryAsync(speech, manifest, episode.WorldName, ct).ConfigureAwait(false);
                await SaveAsync(store, manifest, ct).ConfigureAwait(false); // the names and the dictionary, kept whatever happens next

                ledger.Characters[month] = spent + episode.Script.Length;
                await root.PutAsync(SpeechLedger.Key, ledger.ToBytes(), "application/json", ct).ConfigureAwait(false);
                var audio = await Voiceover.SpeakAsync(speech, episode.Script, voice, dictionaries, ct).ConfigureAwait(false);
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
            // off unless budgeted. Without ANTHROPIC_API_KEY the world runs offline and the template script ships.
            var llm = config.Llm;
            Fw("llm", worldPath, "--provider", llm.Narrate ? "anthropic" : "none", "--model", $"narrate={llm.NarrateModel}",
                "--story-beats", llm.StoryBeatsPerDay.ToString(CultureInfo.InvariantCulture),
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

            _todaysMap = PaintMap(snapshot);
            await StoreMapAsync(store, date, ct).ConfigureAwait(false);

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

    /// <summary>The political map of the saved world; a map that cannot be drawn never stops the episode.</summary>
    private byte[]? PaintMap(string worldPath)
    {
        try
        {
            using var world = WorldStore.Open(worldPath);
            var (w, l, _) = world.Load();
            return MapPainter.Png(w, l);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.WriteLine($"warning: the map could not be drawn ({e.Message}); the episode goes out without it.");
            return null;
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
```

`sim/FantasyWorld.Broadcast/Speech/SpeechLedger.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// Characters sent to the speech service, by calendar month (yyyy-MM), at the bucket's root: live and dry runs share
/// it, so the monthly budget holds the whole account. Characters are counted before each request, so a run that stops
/// mid-request has still counted what it may have been billed.
/// </summary>
public sealed class SpeechLedger
{
    public const string Key = "speech-ledger.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, long> Characters { get; set; } = new(StringComparer.Ordinal);

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static SpeechLedger FromBytes(byte[]? bytes) =>
        bytes is null ? new SpeechLedger() : JsonSerializer.Deserialize<SpeechLedger>(bytes, Json) ?? new SpeechLedger();
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~BroadcastPartsTests|FullyQualifiedName~DailyRunTests"`
Expected: `Passed!  - Failed: 0, Passed: 28`. Then `dotnet test sim` → `Passed: 647` in total.

- [x] **Step 5: Commit**

```bash
git add .github README.md broadcast.json docs sim
git commit -m "feat(broadcast): narrate each episode and attach it to the post

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The podcast feed and the archive page

Spec §4, M3. When the bucket is public (`publicBaseUrl`), each morning rewrites `feed.xml` (RSS 2.0 with the iTunes tags podcast apps read: cover, category, explicit, duration; each item an MP3 enclosure with its length and a stable GUID of world and date, dated at that morning's 05:30 in Bangkok, newest first), `index.html` (every episode with its narration, script and map) and `cover.png` (the latest map, now painted at 1,536 px, above the 1,400 px podcast apps ask for). The feed carries an owner (with `podcastEmail`, for directories to confirm ownership) and a self link; characters XML cannot hold are dropped. Without a public address nothing is published; a failure here never fails the run.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Publishing/Feed.cs`
- Modify: `README.md`, `docs/broadcast-setup.md`, `sim/FantasyWorld.Broadcast/BroadcastConfig.cs`, `sim/FantasyWorld.Broadcast/DailyRun.cs`, `sim/FantasyWorld.Broadcast/Manifest.cs`, `sim/FantasyWorld.Broadcast/Media/MapPainter.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/FeedTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs`

**Interfaces:**
- Consumes: Task 3; `Manifest` (M1).
- Produces: `record FeedItem`; `static class Feed { FeedKey; ArchiveKey; CoverKey; MaxItems; Clean; Title; Items(manifest, config); MorningOf(date, config); Rss(worldName, baseUrl, items, ownerEmail); ItemTitle; Archive(worldName, items) }`; `BroadcastConfig.PublicBaseUrl`, `PodcastEmail`; `Manifest.WorldName`; `MapPainter.DefaultWidth` = 1,536.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs` entirely with:
```csharp
using System.Net;
using System.Text;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
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
        Func<LlmSettings, ILlmProvider?>? providers = null, ISpeech? speech = null, BroadcastConfig? config = null) =>
        new DailyRun(config ?? Config, _root, [channel ?? _discord], () => now, _log, options, providers ?? (_ => null), speech).RunAsync();

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
            var brief = request.Prompt;
            var script = brief[(brief.IndexOf('\n') + 1)..brief.LastIndexOf("</brief>", StringComparison.Ordinal)].Trim();
            return System.Text.Json.JsonSerializer.Serialize(new { text = script.Replace("Elsewhere in the chronicle:", "And elsewhere, in brief:") });
        });
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), providers: settings => settings.Provider == "anthropic" ? provider : null));
        var request = Assert.Single(provider.Requests); // the script only: story beats are off
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
            Assert.Equal(1, store.LoadJournal().Count);
            Assert.Equal("claude-sonnet-5-5", store.LoadLlmSettings().ModelFor(ModelTier.Narrate));
            Assert.Equal(0, store.LoadLlmSettings().StoryBeatsPerDay);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task TheNarrator_SpeaksEachEpisodeOnce_WithItsNamesLearned()
    {
        var speech = new PronouncerTests.FakeSpeech();
        var respeller = PronouncerTests.Respeller();
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true), providers: _ => respeller, speech: speech));
        var spoken = Assert.Single(speech.Spoken);
        Assert.Equal(("uju3wxzG5OhpWcoi3SMy", "eleven_v4_turbo"), (spoken.VoiceId, spoken.Model));
        var audio = await Get("live/" + DailyRun.NarrationKey("2026-10-27"));
        Assert.Equal(audio, _discord.Audio.Single());
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        Assert.True(manifest.Day("2026-10-27").Done(Manifest.AudioStep));
        Assert.Equal(_discord.Posts[0].Script.Length, SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"]);
        Assert.NotEmpty(manifest.PronunciationTries.Keys.Concat(manifest.Pronunciations.Keys)); // the names were asked about
        Assert.Contains("Narrated the episode of 2026-10-27", _log.ToString());

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech, providers: _ => respeller));
        Assert.Single(speech.Spoken);
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
    public async Task SpeechThatFails_LeavesTheTextAndMapPost_AndIsMadeUpNextRun()
    {
        var speech = new PronouncerTests.FakeSpeech { Fails = true };
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true), speech: speech));
        Assert.Null(_discord.Audio.Single());
        Assert.NotNull(_discord.Maps.Single());
        Assert.Contains("could not be narrated", _log.ToString());

        speech.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), speech: speech));
        Assert.Equal(2, speech.Spoken.Count); // yesterday's narration, then today's
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
        Assert.Single(speech.Spoken);
        // Stopped after storing the narration but before marking it: the next run adopts it.
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        manifest.Day("2026-10-27").Steps.Remove(Manifest.AudioStep);
        await _root.PutAsync("live/" + Manifest.Key, manifest.ToBytes(), "application/json");
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1), speech: speech));
        Assert.Single(speech.Spoken);
        Assert.Contains("already stored", _log.ToString());
    }

    [Fact]
    public async Task DryRuns_ShareTheLiveBudget()
    {
        var tight = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "voice": { "monthlyCharacterBudget": 5000 } }""");
        var speech = new PronouncerTests.FakeSpeech();
        await Run(Morning, new RunOptions(Create: true), speech: speech, config: tight);
        var used = SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"];
        await Run(Morning, new RunOptions(DryRun: true), speech: speech, config: tight, channel: new CountingChannel());
        Assert.Equal(2, speech.Spoken.Count);
        Assert.Equal(2 * used, SpeechLedger.FromBytes(await Get(SpeechLedger.Key)).Characters["2026-10"]);
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
        await File.WriteAllTextAsync(config.Path, """{ "seed": 3, "size": "tiny" }""");
        var env = new Dictionary<string, string> { ["DISCORD_TEST_WEBHOOK_URL"] = "https://discord.test/api/webhooks/9/SECRET-TOKEN" };
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
        Assert.DoesNotContain("SECRET-TOKEN", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["publish"], output, () => Morning, env.GetValueOrDefault));
    }
}
```

`sim/FantasyWorld.Sim.Tests/Broadcasting/FeedTests.cs`:
```csharp
using System.Xml.Linq;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Publishing;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class FeedTests
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly BroadcastConfig Config = BroadcastConfig.Parse("""{ "publicBaseUrl": "https://pub-1.r2.dev" }""");

    private static Manifest Narrated()
    {
        var manifest = new Manifest { WorldName = "Aerenmoor" };
        foreach (var (date, year, headline) in new[] { ("2026-10-27", 101, "The War of the Ash begins."), ("2026-10-28", 102, (string?)null), ("2026-10-29", 103, "Gewick <falls>\u0001") })
        {
            var day = manifest.Day(date);
            (day.ToYear, day.Headline, day.AudioBytes, day.AudioSeconds) = (year, headline, 3_000_000, 187);
            day.Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch;
            day.Steps[Manifest.AudioStep] = DateTimeOffset.UnixEpoch;
        }
        manifest.Day("2026-10-30").Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch; // not narrated: not in the feed
        return manifest;
    }

    [Fact]
    public void TheFeed_IsPodcastRss_NewestFirst_WithStableGuidsAndEnclosures()
    {
        var items = Feed.Items(Narrated(), Config);
        var rss = XDocument.Parse(Feed.Rss("Aerenmoor", "https://pub-1.r2.dev/live/", items, "chronicle@example.com"));
        var channel = rss.Root!.Element("channel")!;
        Assert.Equal("The Chronicle of Aerenmoor", (string?)channel.Element("title"));
        Assert.Equal("https://pub-1.r2.dev/live/cover.png", (string?)channel.Element(Itunes + "image")!.Attribute("href"));
        Assert.Equal("false", (string?)channel.Element(Itunes + "explicit"));
        Assert.Equal("chronicle@example.com", (string?)channel.Element(Itunes + "owner")!.Element(Itunes + "email"));
        Assert.Equal("https://pub-1.r2.dev/live/feed.xml", (string?)channel.Element((XNamespace)"http://www.w3.org/2005/Atom" + "link")!.Attribute("href"));
        Assert.Equal("The War of the Ash begins.", (string?)channel.Elements("item").Last().Element("description"));
        var entries = channel.Elements("item").ToList();
        Assert.Equal(new[] { "Aerenmoor-2026-10-29", "Aerenmoor-2026-10-28", "Aerenmoor-2026-10-27" }, entries.Select(i => (string)i.Element("guid")!));
        var newest = entries[0];
        Assert.Equal("Year 103: Gewick <falls>", (string?)newest.Element("title")); // the control character is dropped
        var enclosure = newest.Element("enclosure")!;
        Assert.Equal(("https://pub-1.r2.dev/live/episodes/2026-10-29/narration.mp3", "3000000", "audio/mpeg"),
            ((string)enclosure.Attribute("url")!, (string)enclosure.Attribute("length")!, (string)enclosure.Attribute("type")!));
        Assert.Equal("187", (string?)newest.Element(Itunes + "duration"));
        Assert.Equal("Wed, 28 Oct 2026 22:30:00 GMT", (string?)newest.Element("pubDate")); // 05:30 on the 29th in Bangkok
        Assert.Equal("Aerenmoor, Year 102", (string?)entries[1].Element("title"));
    }

    [Fact]
    public void TheArchive_ListsEveryEpisode_WithItsWordsEscaped()
    {
        var html = Feed.Archive("Aerenmoor", Feed.Items(Narrated(), Config));
        Assert.Contains("<title>The Chronicle of Aerenmoor</title>", html);
        Assert.Contains("Gewick &lt;falls&gt;", html);
        Assert.DoesNotContain("<falls>", html);
        Assert.Contains("src=\"episodes/2026-10-27/narration.mp3\"", html);
        Assert.True(html.IndexOf("2026-10-29", StringComparison.Ordinal) < html.IndexOf("2026-10-27", StringComparison.Ordinal));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "publicBaseUrl": "http://insecure.example" }"""));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Publishing' does not exist in the namespace 'FantasyWorld.Broadcast' (are you missing an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `README.md` entirely with:
````markdown
# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `sim/FantasyWorld.Client/` — the game's engine-free logic (session, terrain, layout), tested with the rest
- `sim/FantasyWorld.Broadcast/` — the daily broadcast: R2 storage, the morning run and Discord (`broadcast.json`)
- `game/` — the Godot 4.3 diorama client
- `docs/superpowers/specs/` — design specs
- `docs/superpowers/plans/` — implementation plans

## The game

![The morning digest over the diorama](docs/images/digest.png)

The world is a floating diorama: drag to orbit, scroll to descend from the whole realm to a single town. On launch
the world catches up on the time you were away and opens on the *morning digest*: the year, what changed, the
headline and *why did this happen?* Every name is a link: people open their life and ties, places fly the camera
there, realms and artifacts open their pages, and any event opens the chain of causes that led to it. The *map*
adds layers, a history slider back to Year 1, and a card for every settlement. While the game is open the world
lives on (one world day every four real minutes) and announces what matters.

![A region seen from above, with realm borders and settlements](docs/images/region.png)

War is drawn like a strategy map: each host's banner (in its realm's colours) marches from region to region along
a broad campaign arrow toward its target, attackers in red and defenders in blue; fronts carry teeth facing the
attacker, occupied land is hatched, and crossed swords mark this year's battles. Click a banner for its commander,
a battle for why it was fought. Knights ride with their realm's host, and battles can wound, kill or make them famous.

![A host on the march: campaign arrow, banner, front and occupied land](docs/images/war.png)
![Why a war began: the chain of causes](docs/images/why.png)

Run it with [Godot 4.3 .NET](https://godotengine.org/download/archive/4.3-stable/) and the .NET 8 SDK:

```bash
dotnet build game/FantasyWorld.Game.csproj
godot --path game                                   # first launch creates a world with a century of prehistory
godot --path game -- --world worlds/aerenmoor.fw.db # or open a world made with the CLI
```

Keys: **Esc** back · **H** home (the digest) · **M** the map. Worlds live in Godot's user folder unless given.

Checks: `godot --headless --path game res://tests/HeadlessChecks.tscn` drives every screen on a fresh world and
exits non-zero on failure (CI runs it). `godot --path game res://tests/Showcase.tscn -- --shots <dir>` renders a
full-size world from the angles above, and `godot --path game --write-movie war.avi --fixed-fps 30 res://tests/WarMovie.tscn`
films a host on the march (both need a display).

## Myths, magic and structures

The world grows its own legends, each with a cause you can follow:

- **Beasts** rise where the land is dangerous, more often after a ley surge: a wyrm in the mountains, a hydra in the
  swamp. They raid nearby towns until a hero hunts them by name; some become legends, very old ones slumber.
- **Trade roads** join markets at peace with each other; wars, occupations and danger sever them, and peace reopens
  them (the map's *Trade routes* layer).
- **Faiths** spread from realms and neighbours, held back by temples and saints' relics; a realm whose lands have
  turned takes up the new faith. A faith that loses its holy land grows zealous, may wage a holy war to take it
  back, and may split when a priest founds a heresy. Devout notables of renown become saints.
- **Magic** ebbs and flows in slow ley tides. Mage towers train mages where the law allows; a surge at a tower may go
  wild (a storm, a blight, a portal that lets danger in), and realms tighten their law after it. Where magic is
  outlawed, mages flee or burn.
- **Great works** take years: an architect, a patron, delays from war, plague and famine, collapses when rushed;
  towns that knew siege raise fortresses, and fallen works are restored.
- **Labyrinths** open in wild, ley-charged land with three to seven levels and a guardian on the deepest. Delvers go
  down level by level; slaying the guardian and taking the last relic clears it, leaving a ruin or a shrine.
- Rivals quarrel over their borders, and rulers who crave renown seek glory against weaker neighbours.

![A town with its temple and mage tower, a beast in the hills and a labyrinth's walls](docs/images/town.png)

## Run

```bash
dotnet test sim
dotnet run --project sim/FantasyWorld.Cli -- new worlds/aerenmoor.fw.db            # medium world (100 regions)
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db            # ASCII map (--layer regions)
dotnet run --project sim/FantasyWorld.Cli -- catchup worlds/aerenmoor.fw.db        # simulate real elapsed time
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 50y    # debug fast-forward
dotnet run --project sim/FantasyWorld.Cli -- regions worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- region worlds/aerenmoor.fw.db 12
dotnet run --project sim/FantasyWorld.Cli -- why worlds/aerenmoor.fw.db <event-id>
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/aerenmoor.fw.db       # living notables
dotnet run --project sim/FantasyWorld.Cli -- life worlds/aerenmoor.fw.db <char-id>    # a biography
dotnet run --project sim/FantasyWorld.Cli -- artifacts worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- artifact worlds/aerenmoor.fw.db <id>     # powers and provenance
dotnet run --project sim/FantasyWorld.Cli -- realms worlds/aerenmoor.fw.db           # kingdoms and their vassals
dotnet run --project sim/FantasyWorld.Cli -- realm worlds/aerenmoor.fw.db <id>        # laws, council, treaties, eras
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- war worlds/aerenmoor.fw.db <id>          # casus belli and the course of the war
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db --layer realms
dotnet run --project sim/FantasyWorld.Cli -- tell worlds/aerenmoor.fw.db <event-id>    # the why-chain as prose
dotnet run --project sim/FantasyWorld.Cli -- stats worlds/aerenmoor.fw.db             # world summary and LLM spend
```

## Story beats from Claude

The world runs fully offline by default: rules make every choice and templates tell the story. To let Claude
decide notables' choices, arbitrate duels, hunts and delves, name forged artifacts, wars and eras, and narrate
with `fw tell`:

```bash
export ANTHROPIC_API_KEY=...        # never stored in the world file
dotnet run --project sim/FantasyWorld.Cli -- llm worlds/aerenmoor.fw.db --provider anthropic --cap 5
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 2y
```

Defaults: `claude-opus-5-5` for every job (`--model narrate=<model>` changes one), 60 story beats and 200
narrations per rolling day, and a $10 cap per 30 days; past it, or without credentials, the rules take over.
`--provider openrouter` uses `OPENROUTER_API_KEY` instead. Every answer is journaled in the world file, so
reloading or replaying a world never asks again, and `fw why` shows the motives and accounts it gave.

Each world has a chronicler (a name, a temperament and a diction, the same every time the world opens) whose voice
and house style every narration follows. Narration may use only the facts it is given: one that invents a name or a
number is rejected and journaled, and the templates speak instead. The template voice is a product in its own right
(articles, numbers in words, several phrasings per event chosen by its id, headlines with a body that adds what the
title leaves out, and the story of why as one paragraph); its structure is checked on every test run against a real
history and a golden narration.

Live quality evals grade the LLM voice with a judge model on fixed seeds, within a cost cap:

```bash
dotnet run --project sim/FantasyWorld.Cli -- eval narration --cap 2      # events and chains: faithful, clear, voice, varied
dotnet run --project sim/FantasyWorld.Cli -- eval decisions --draws 40   # choices in character; arbitrations against their odds
```

Each run prints the mean scores and saves a report under `evals/`; no call is made once a run has spent its cap (default $2).

`--size tiny` makes a 64×64 world with 12 regions for quick experiments (and no prehistory; medium and large
worlds start with 100 years of it, `--prehistory <years>` to change).

## The chronicle

```bash
dotnet run --project sim/FantasyWorld.Cli -- digest worlds/aerenmoor.fw.db              # headlines since you last looked
dotnet run --project sim/FantasyWorld.Cli -- follow worlds/aerenmoor.fw.db char#12      # their deeds lead the digest
dotnet run --project sim/FantasyWorld.Cli -- live worlds/aerenmoor.fw.db --minutes 30   # watch it unfold in real time
dotnet run --project sim/FantasyWorld.Cli -- structures worlds/aerenmoor.fw.db          # landmarks, labyrinths and ruins
```

Every screen of the game also exists as data, for clients (the Godot diorama) and for checking without one:

```bash
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db digest            # the morning digest, as JSON
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db map --as-of 150   # the political map in Year 150
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db why <event-id>     # a why-chain as a layered graph
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db character <id>     # also: family, chronicle, realm, artifact
```

`catchup` ends with the digest. Minor events older than 50 years are condensed into one summary per decade as
the world runs. Every command that changes a world first keeps a
backup of the last three sessions next to it (`.bak1`–`.bak3`). Benchmarks:
`dotnet run -c Release --project sim/FantasyWorld.Bench -- --filter '*'`.

## The daily broadcast

Every morning at 05:30 Bangkok time, GitHub Actions pulls the world from Cloudflare R2, lets it live the year that passed,
builds the episode (the script written by the world's chronicler, Claude, held to the facts; the template voice when it
cannot), has it read aloud by ElevenLabs, and posts it to Discord with the narration and the political map. A rerun the same day posts nothing new; with a public bucket it also keeps a podcast feed and an archive page. Setup:
[docs/broadcast-setup.md](docs/broadcast-setup.md).

```bash
dotnet run --project sim/FantasyWorld.Cli -- episode worlds/aerenmoor.fw.db --out ep/ --narrate   # episode.json and script.txt since the last one
dotnet run --project sim/FantasyWorld.Broadcast -- run --dry-run --create --local bucket/   # the whole morning, with a local folder for R2
dotnet run --project sim/FantasyWorld.Broadcast -- map worlds/aerenmoor.fw.db map.png     # the map as the broadcast posts it
```
````

Replace `docs/broadcast-setup.md` entirely with:
````markdown
# Daily broadcast: owner setup

The broadcast runs in GitHub Actions (`.github/workflows/broadcast.yml`) every morning at 05:30 Bangkok time. The world
lives in Cloudflare R2, and each episode is posted to Discord. Nothing runs on your computer. You set it up once.

## 1. Cloudflare R2

1. In the Cloudflare dashboard, open **R2** and create a bucket (for example `fantasy-world`). Keep it private: the
   world file and its backups are not for the public.
2. Under **R2 → Manage API tokens**, create a token with **Object Read & Write** on that bucket only. Note the
   **Access Key ID** and **Secret Access Key** (shown once), and your **Account ID** (on the R2 overview page).

## 2. Discord

In your server: **Channel settings → Integrations → Webhooks → New webhook**, then **Copy webhook URL**. Make one for the
real channel, and (recommended) one for a private test channel used by dry runs.

## 3. GitHub secrets

In the repository: **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|---|---|
| `R2_ACCOUNT_ID` | Cloudflare account id |
| `R2_BUCKET` | the bucket's name |
| `R2_ACCESS_KEY_ID` | the token's access key id |
| `R2_SECRET_ACCESS_KEY` | the token's secret access key |
| `DISCORD_WEBHOOK_URL` | the real channel's webhook |
| `DISCORD_TEST_WEBHOOK_URL` | the test channel's webhook (optional; without it a dry run posts nowhere) |
| `ELEVENLABS_API_KEY` | from elevenlabs.io → your profile → API Keys; the narrator reads each episode with it (without it, the post goes out as text and map) |
| `ANTHROPIC_API_KEY` | from console.anthropic.com → API Keys; the chronicler writes each script with it (without it, the template script ships) |

Secrets are only ever read from the environment. They are never written to R2, the world file, the logs or the repository.

## 4. The podcast feed and archive page (optional)

The feed (`feed.xml`) and the archive page (`index.html`) are published only once the bucket can be read from the web:

1. Cloudflare → R2 → your bucket → **Settings → Public access → R2.dev subdomain → Allow** (or connect a custom domain).
2. Copy the public address (for example `https://pub-1234abcd.r2.dev`) into `broadcast.json` as `"publicBaseUrl"` (and your
   address as `"podcastEmail"`, if you will list the podcast in Apple Podcasts or Spotify), and merge.
3. The next morning, add `<address>/live/feed.xml` to a podcast app, or open `<address>/live/index.html`.

Everything in the bucket becomes readable by anyone with the address: the episodes, and also the world file and its
backups. Nothing secret is stored there, but if you would rather keep the world private, leave this off.

## 5. Start it

1. Merge to the default branch. GitHub runs scheduled workflows only from there, and a run that is not a dry run is
   refused on any other branch.
2. **Actions → broadcast → Run workflow** with `dry-run` ticked and `create` ticked. This makes a staging world under
   `staging/` and posts to the test channel. Read the episode there.
3. Run it again with `dry-run` unticked and `create` ticked. This begins the real world under `live/` and posts the first
   episode. From then on the schedule takes over; never tick `create` again unless you want to start over after
   deleting `live/world.fw.db`.
4. Watch three mornings in a row. Each should post one episode. Re-running the workflow the same day posts nothing new.

## Settings (`broadcast.json`)

| Field | Default | Meaning |
|---|---|---|
| `worldKey` | `world.fw.db` | the world's name in the bucket, under the prefix |
| `seed`, `size` | `20261027`, `medium` | the world a `create` run begins |
| `timeZone` | `Asia/Bangkok` | episodes are dated in this zone |
| `language` | `en` | only English exists so far |
| `livePrefix`, `stagingPrefix` | `live/`, `staging/` | where real and dry runs keep their files |
| `backupDays` | `30` | daily backups kept; the first backup of each month is kept for good |
| `stories`, `maxCharacters` | `5`, `3600` | episode length (about 2–4 minutes read aloud) |
| `discordName` | `The Chronicle` | the name posts appear under |
| `llm.narrate` | `true` | the chronicler (Claude) writes the script; off, the template script ships |
| `llm.narrateModel` | `claude-sonnet-5-5` | the model that writes it |
| `llm.storyBeatsPerDay` | `0` | the sim's own LLM decisions while it catches up; off by default, so the daily cost is one script (a few cents) |
| `voice.speak` | `true` | ElevenLabs reads each episode; the MP3 is attached to the post and stored |
| `voice.voiceId`, `voice.model` | Michael C. Vincent (`uju3wxzG5OhpWcoi3SMy`), `eleven_v4_turbo` | the narrator's voice and model (the owner's choices) |
| `voice.maxCharactersPerRequest` | `5000` | a script longer than this is spoken in parts and joined (a day's script is at most 3,600) |
| `voice.monthlyCharacterBudget` | `150000` | characters spoken per calendar month (about 40 days of episodes); past it, episodes go out without narration |
| `publicBaseUrl` | unset | the bucket's public address; when set, the podcast feed, the archive page and the cover are published |
| `podcastEmail` | unset | the feed's contact address, which Apple Podcasts and Spotify use to confirm you own the feed |
| `voice.respell` | `true` | Claude respells each new invented name once, so the narrator says it the same way every day |
| `llm.narrationsPerDay`, `llm.monthlyCapUsd` | `10`, `5` | budget and spending cap over 30 days; past them, the template script ships |
| `keepAlive` | `true` | a monthly heartbeat commit, so GitHub keeps the schedule of a public repository; turn it off for a private one |

## What is in the bucket

```
live/world.fw.db                       the world (a consistent single-file copy)
live/manifest.json                     per date, which steps finished: a rerun repeats none of them
live/episodes/2026-10-27/episode.json  the episode as data (years, headline, stories, why)
live/episodes/2026-10-27/script.txt    the script as posted
live/episodes/2026-10-27/map.png       the political map that morning, posted as the embed's image
live/episodes/2026-10-27/narration.mp3 the episode read aloud, attached to the post
live/feed.xml, live/index.html         the podcast feed and the archive page (with publicBaseUrl)
live/cover.png                         the latest map, the podcast's cover
live/backups/2026-10-27.fw.db          the world as it was before that morning's run
```

## When something goes wrong

- **A red run with "There is no world"**: the bucket is empty or the R2 secrets point somewhere else. A scheduled run never
  begins a new world, so check the secrets.
- **"Discord answered HTTP …"**: the episode was saved and the world moved on. The post is made by the next run, before
  that morning's own (re-run the workflow to post it now); posts missed for up to a week are made up, oldest first. A
  post is never made twice, except when Discord took it but timed out before answering.
- **"could not be narrated"**: the post went out as text and map; the narration is made by the next run (and kept for the
  feed). Check the ElevenLabs key, credits and the voice id. **"speech budget"**: raise `voice.monthlyCharacterBudget`.
- **A run stopped part-way** (a timeout, an R2 hiccup): re-run it. Each step it finished is recorded, and the stored world
  says which day's episode it holds, so nothing is told twice or skipped.
- **The world misbehaves**: copy a file from `live/backups/` over `live/world.fw.db` and leave `live/manifest.json` alone.
  The next run carries on from the restored world. Never delete the manifest: it is what stops a day being posted twice.
````

Replace `sim/FantasyWorld.Broadcast/BroadcastConfig.cs` entirely with:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The broadcast's LLM (spec §3.2, M2): the chronicler writes the script on the narrate tier. Story beats (the sim's
/// own LLM decisions while it catches up) stay off unless given a budget, so the daily cost is one script.
/// </summary>
public sealed record BroadcastLlm
{
    /// <summary>When off, the template script ships and nothing is asked.</summary>
    public bool Narrate { get; init; } = true;

    public string NarrateModel { get; init; } = "claude-sonnet-5-5";

    public int StoryBeatsPerDay { get; init; }

    public int NarrationsPerDay { get; init; } = 10;

    public decimal MonthlyCapUsd { get; init; } = 5m;
}

/// <summary>
/// The narrator's voice (M3): ElevenLabs speaks each script. The per-request limit and the monthly character budget
/// are settings, not assumptions about the model; past the budget the episode goes out as text.
/// </summary>
public sealed record BroadcastVoice
{
    public bool Speak { get; init; } = true;

    /// <summary>"Michael C. Vincent – Confident, Expressive", from the ElevenLabs library: the owner's choice.</summary>
    public string VoiceId { get; init; } = "uju3wxzG5OhpWcoi3SMy";

    public string Model { get; init; } = "eleven_v4_turbo";

    public string OutputFormat { get; init; } = "mp3_44100_128";

    public int MaxCharactersPerRequest { get; init; } = 5000;

    public int MonthlyCharacterBudget { get; init; } = 150_000;

    /// <summary>Ask the chronicler's model, once per invented name, how to say it; the answers are kept for good.</summary>
    public bool Respell { get; init; } = true;
}

/// <summary>
/// The broadcast's checked-in settings (<c>broadcast.json</c>). Nothing secret lives here: keys and webhooks come from
/// the environment (GitHub Actions secrets).
/// </summary>
public sealed record BroadcastConfig
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The world's object key, under the live or staging prefix.</summary>
    public string WorldKey { get; init; } = "world.fw.db";

    /// <summary>The seed and size a <c>create</c> run starts the world with.</summary>
    public ulong Seed { get; init; } = 20261027;

    public string Size { get; init; } = "medium";

    /// <summary>Episodes are keyed by the broadcast date in this time zone.</summary>
    public string TimeZone { get; init; } = "Asia/Bangkok";

    public string Language { get; init; } = "en";

    public string LivePrefix { get; init; } = "live/";

    public string StagingPrefix { get; init; } = "staging/";

    /// <summary>Daily backups kept; the first backup of each month is kept as well, for good.</summary>
    public int BackupDays { get; init; } = 30;

    public int Stories { get; init; } = Episode.DefaultStories;

    public int MaxCharacters { get; init; } = Episode.DefaultMaxCharacters;

    /// <summary>The name the Discord post appears under.</summary>
    public string DiscordName { get; init; } = "The Chronicle";

    public BroadcastLlm Llm { get; init; } = new();

    public BroadcastVoice Voice { get; init; } = new();

    /// <summary>
    /// Where the bucket is public (an r2.dev address or a custom domain), for the podcast feed and the archive page; unset,
    /// they are not published.
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>The podcast's contact address (<c>itunes:email</c>), which directories use to confirm who owns the feed.</summary>
    public string? PodcastEmail { get; init; }

    /// <summary>When on, the workflow commits a heartbeat monthly so GitHub keeps the schedule of a public repository alive.</summary>
    public bool KeepAlive { get; init; } = true;

    public static BroadcastConfig Load(string path) => Parse(File.ReadAllText(path));

    public static BroadcastConfig Parse(string json) =>
        (JsonSerializer.Deserialize<BroadcastConfig>(json, Json) ?? throw new FormatException("broadcast.json is empty.")).Validated();

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>The broadcast date (yyyy-MM-dd) at <paramref name="now"/>, in the broadcast's time zone.</summary>
    public string DateAt(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private BroadcastConfig Validated()
    {
        if (Language != "en") throw new FormatException($"Only English broadcasts exist so far (language \"{Language}\").");
        if (Size is not ("tiny" or "medium" or "large")) throw new FormatException($"Unknown size \"{Size}\".");
        if (BackupDays < 1) throw new FormatException("backupDays must be at least 1.");
        if (Stories is < 0 or > 10) throw new FormatException("stories must be between 0 and 10.");
        if (MaxCharacters is < 300 or > 4000) throw new FormatException("maxCharacters must be between 300 and 4000 (a Discord post holds 4096).");
        if (LivePrefix == StagingPrefix || !LivePrefix.EndsWith('/') || !StagingPrefix.EndsWith('/'))
            throw new FormatException("livePrefix and stagingPrefix must differ and end with '/'.");
        if (Llm.StoryBeatsPerDay < 0 || Llm.NarrationsPerDay < 0 || Llm.MonthlyCapUsd < 0 || Llm.NarrateModel.Length == 0)
            throw new FormatException("llm budgets and the cap cannot be negative, and narrateModel must be named.");
        if (Voice.MaxCharactersPerRequest < 100 || Voice.MonthlyCharacterBudget < 0 || Voice.VoiceId.Length == 0 || Voice.Model.Length == 0)
            throw new FormatException("voice needs a voiceId and a model, at least 100 characters a request and a budget that is not negative.");
        _ = Speech.Voiceover.Kbps(Voice.OutputFormat);
        if (PublicBaseUrl is { } url && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            throw new FormatException($"publicBaseUrl must be an https address (got \"{url}\").");
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
    }
}
```

Replace `sim/FantasyWorld.Broadcast/DailyRun.cs` entirely with:
```csharp
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

public sealed record RunOptions(bool DryRun = false, bool Create = false);

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
                var dictionaries = await Pronouncer.DictionaryAsync(speech, manifest, episode.WorldName, ct).ConfigureAwait(false);
                await SaveAsync(store, manifest, ct).ConfigureAwait(false); // the names and the dictionary, kept whatever happens next

                ledger.Characters[month] = spent + episode.Script.Length;
                await root.PutAsync(SpeechLedger.Key, ledger.ToBytes(), "application/json", ct).ConfigureAwait(false);
                var audio = await Voiceover.SpeakAsync(speech, episode.Script, voice, dictionaries, ct).ConfigureAwait(false);
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
            // off unless budgeted. Without ANTHROPIC_API_KEY the world runs offline and the template script ships.
            var llm = config.Llm;
            Fw("llm", worldPath, "--provider", llm.Narrate ? "anthropic" : "none", "--model", $"narrate={llm.NarrateModel}",
                "--story-beats", llm.StoryBeatsPerDay.ToString(CultureInfo.InvariantCulture),
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

            _todaysMap = PaintMap(snapshot);
            await StoreMapAsync(store, date, ct).ConfigureAwait(false);

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

    /// <summary>The political map of the saved world; a map that cannot be drawn never stops the episode.</summary>
    private byte[]? PaintMap(string worldPath)
    {
        try
        {
            using var world = WorldStore.Open(worldPath);
            var (w, l, _) = world.Load();
            return MapPainter.Png(w, l);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.WriteLine($"warning: the map could not be drawn ({e.Message}); the episode goes out without it.");
            return null;
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
```

Replace `sim/FantasyWorld.Broadcast/Manifest.cs` entirely with:
```csharp
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>What one broadcast date has done: each step is marked the moment it finishes, so a rerun skips it.</summary>
public sealed class ManifestDay
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
    public int Events { get; set; }
    public string? Headline { get; set; }
    public SortedDictionary<string, DateTimeOffset> Steps { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The narration's size and length, for the feed (M3).</summary>
    public long AudioBytes { get; set; }
    public int AudioSeconds { get; set; }

    public bool Done(string step) => Steps.ContainsKey(step);
}

/// <summary>
/// <c>manifest.json</c>: per broadcast date, which steps finished and when. The rule that keeps reruns safe: a channel
/// is posted to only when its step is not marked, and the mark is written back straight after.
/// </summary>
public sealed class Manifest
{
    public const string Key = "manifest.json";
    public const string EpisodeStep = "episode";
    public const string DiscordStep = "discord";
    public const string AudioStep = "audio";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, ManifestDay> Episodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The world's name, for the feed's title.</summary>
    public string? WorldName { get; set; }

    /// <summary>How each invented name is said (word → respelling; a word said as written maps to itself). Never changed once set.</summary>
    public SortedDictionary<string, string> Pronunciations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Words asked about without a usable answer, and how often; after a few tries one is said as written.</summary>
    public SortedDictionary<string, int> PronunciationTries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The ElevenLabs dictionary holding the respellings, its current version, and the words already in it.</summary>
    public string? DictionaryId { get; set; }
    public string? DictionaryVersionId { get; set; }
    public SortedSet<string> DictionaryWords { get; set; } = new(StringComparer.Ordinal);

    public ManifestDay Day(string date) => Episodes.TryGetValue(date, out var day) ? day : Episodes[date] = new ManifestDay();

    public void Record(string date, Episode episode)
    {
        WorldName = episode.WorldName;
        var day = Day(date);
        (day.FromYear, day.ToYear, day.Events, day.Headline) = (episode.FromYear, episode.ToYear, episode.EventCount, episode.Headline?.Text);
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static Manifest FromBytes(byte[]? bytes) =>
        bytes is null ? new Manifest() : JsonSerializer.Deserialize<Manifest>(bytes, Json) ?? new Manifest();
}
```

Replace `sim/FantasyWorld.Broadcast/Media/MapPainter.cs` entirely with:
```csharp
using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Broadcast.Media;

/// <summary>A painted image: width, height and three bytes a pixel.</summary>
public sealed record Raster(int Width, int Height, byte[] Rgb)
{
    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = (y * Width + x) * 3;
        return (Rgb[i], Rgb[i + 1], Rgb[i + 2]);
    }
}

/// <summary>
/// The political map as a picture for the broadcast (sub-project 6 §4, M2), from the same map view the game draws:
/// land in its biome's colour washed with the holding realm's heraldic colour, the sea by depth, rivers, realm borders
/// in ink and region borders faintly, occupied land striped in the colour of conflict, and towns as dots. No text: the
/// post names the realms.
/// </summary>
public static class MapPainter
{
    /// <summary>6 px a tile on a medium world: 1,536 px, big enough to serve as podcast cover art (1,400 px at least).</summary>
    public const int DefaultWidth = 1536;

    /// <summary>How much of a realm's colour washes over the land it holds.</summary>
    public const float RealmWash = 0.45f;

    public static byte[] Png(World world, EventLog log, int width = DefaultWidth)
    {
        var raster = Paint(world, log, width);
        return Media.Png.Encode(raster.Width, raster.Height, raster.Rgb);
    }

    public static Raster Paint(World world, EventLog log, int width = DefaultWidth)
    {
        var geography = world.Geography ?? throw new InvalidOperationException("The world has no geography to draw.");
        var terrain = geography.Terrain;
        var scale = Math.Max(1, width / terrain.Width);
        var (w, h) = (terrain.Width * scale, terrain.Height * scale);
        var map = MapScreen.Build(world, log);
        var regions = map.Regions.ToDictionary(r => r.Ref.Id);
        var rgb = new byte[w * h * 3];

        long OwnerOf(int tile) => geography.RegionOf[tile] is var r and > 0 && regions.TryGetValue(r, out var region) ? region.Owner?.Id ?? 0 : -1;

        for (var tile = 0; tile < terrain.Count; tile++)
        {
            var (tx, ty) = (tile % terrain.Width, tile / terrain.Width);
            var colour = TileColour(terrain, tile, geography.RegionOf[tile] is var id and > 0 ? regions.GetValueOrDefault(id) : null);
            var occupied = geography.RegionOf[tile] > 0 && regions.GetValueOrDefault(geography.RegionOf[tile])?.Occupier is not null;
            for (var py = 0; py < scale; py++)
            for (var px = 0; px < scale; px++)
            {
                var (x, y) = (tx * scale + px, ty * scale + py);
                var c = occupied && (x + y) % 8 < 2 ? Palette.Conflict : colour;
                Set(rgb, w, x, y, c);
            }

            // Borders on this tile's right and bottom edges: ink between realms (and at the coast), faint between regions.
            foreach (var (nx, ny, right) in new[] { (tx + 1, ty, true), (tx, ty + 1, false) })
            {
                if (nx >= terrain.Width || ny >= terrain.Height) continue;
                var other = terrain.Index(nx, ny);
                var (a, b) = (OwnerOf(tile), OwnerOf(other));
                Vector3? line = a != b && (a > 0 || b > 0) ? Palette.Ink
                    : geography.RegionOf[tile] != geography.RegionOf[other] && a >= 0 && b >= 0 ? Mix(colour, Palette.Ink, 0.25f)
                    : null;
                if (line is not { } ink) continue;
                var thick = a != b ? Math.Max(1, scale / 2) : 1;
                for (var t = 0; t < scale; t++)
                for (var k = 0; k < thick; k++)
                    if (right) Set(rgb, w, tx * scale + scale - 1 - k, ty * scale + t, ink);
                    else Set(rgb, w, tx * scale + t, ty * scale + scale - 1 - k, ink);
            }
        }

        foreach (var s in map.Settlements)
        {
            if (s.Position is not { } at) continue;
            var radius = s.Tier switch { nameof(SettlementTier.City) => scale * 2, nameof(SettlementTier.Town) => scale * 3 / 2, _ => Math.Max(1, scale / 2) };
            Dot(rgb, w, h, (int)(at.X * w), (int)(at.Y * h), radius + 1, Palette.Card);
            Dot(rgb, w, h, (int)(at.X * w), (int)(at.Y * h), radius, Palette.Ink);
        }
        return new Raster(w, h, rgb);
    }

    private static Vector3 TileColour(TerrainGrid terrain, int tile, MapRegion? region)
    {
        if (!terrain.IsLand(tile))
        {
            var depth = Math.Clamp((terrain.SeaLevel - terrain.Elevation[tile]) / (float)Math.Max(1, (int)terrain.SeaLevel), 0, 1);
            return Mix(BlockMesh.Shallows, BlockMesh.Deep, depth);
        }
        var land = BlockMesh.ColourOf(terrain.BiomeAt(tile));
        // Heraldic colours are deep; lightened, they tint the land without hiding it.
        land = region is { Owner: not null } ? Mix(land, Mix(Palette.Heraldic(region.Colour), Palette.Card, 0.3f), RealmWash) : Mix(land, Palette.Ground, 0.35f);
        return terrain.Has(tile, TileFlags.River) ? Mix(land, BlockMesh.River, 0.6f) : land;
    }

    private static Vector3 Mix(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    private static void Dot(byte[] rgb, int w, int h, int cx, int cy, int r, Vector3 c)
    {
        for (var y = cy - r; y <= cy + r; y++)
        for (var x = cx - r; x <= cx + r; x++)
            if (x >= 0 && y >= 0 && x < w && y < h && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Set(rgb, w, x, y, c);
    }

    private static void Set(byte[] rgb, int w, int x, int y, Vector3 c)
    {
        var i = (y * w + x) * 3;
        rgb[i] = Byte(c.X);
        rgb[i + 1] = Byte(c.Y);
        rgb[i + 2] = Byte(c.Z);
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
}
```

`sim/FantasyWorld.Broadcast/Publishing/Feed.cs`:
```csharp
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace FantasyWorld.Broadcast.Publishing;

/// <summary>One narrated episode as the feed and the archive show it.</summary>
public sealed record FeedItem(string Date, int ToYear, string? Headline, long AudioBytes, int AudioSeconds, DateTimeOffset Published);

/// <summary>
/// The podcast feed and the archive page (M3), rebuilt each morning from the manifest: RSS 2.0 with the iTunes tags
/// podcast apps read (cover, duration, explicit, category), each item an MP3 enclosure with a stable GUID (world and
/// date), newest first.
/// </summary>
public static class Feed
{
    public const string FeedKey = "feed.xml", ArchiveKey = "index.html", CoverKey = "cover.png";
    public const int MaxItems = 100;

    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>Text as XML can hold it: characters XML forbids (control characters) are dropped.</summary>
    public static string Clean(string text) => new(text.Where(System.Xml.XmlConvert.IsXmlChar).ToArray());

    public static string Title(string worldName) => $"The Chronicle of {worldName}";

    /// <summary>The items of a manifest: every day with narration, newest first, dated at its morning in the broadcast's zone.</summary>
    public static IReadOnlyList<FeedItem> Items(Manifest manifest, BroadcastConfig config) =>
        manifest.Episodes.Where(d => d.Value.Done(Manifest.AudioStep)).OrderByDescending(d => d.Key, StringComparer.Ordinal).Take(MaxItems)
            .Select(d => new FeedItem(d.Key, d.Value.ToYear, d.Value.Headline, d.Value.AudioBytes, d.Value.AudioSeconds, MorningOf(d.Key, config)))
            .ToList();

    /// <summary>05:30 in the broadcast's zone on the date (the schedule's hour).</summary>
    public static DateTimeOffset MorningOf(string date, BroadcastConfig config)
    {
        var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(5.5);
        return new DateTimeOffset(day, config.Zone.GetUtcOffset(day));
    }

    /// <param name="ownerEmail">The podcast directories' contact for proving ownership; optional.</param>
    public static string Rss(string worldName, string baseUrl, IReadOnlyList<FeedItem> items, string? ownerEmail = null)
    {
        string Url(string key) => $"{baseUrl.TrimEnd('/')}/{key}";
        worldName = Clean(worldName);
        var channel = new XElement("channel",
            new XElement("title", Title(worldName)),
            new XElement("link", Url(ArchiveKey)),
            new XElement(Atom + "link", new XAttribute("href", Url(FeedKey)), new XAttribute("rel", "self"), new XAttribute("type", "application/rss+xml")),
            new XElement("description", $"Every morning the world of {worldName} lives one more year, and its chronicler tells what happened and why."),
            new XElement("language", "en"),
            new XElement(Itunes + "author", "The Chronicle"),
            new XElement(Itunes + "owner", new XElement(Itunes + "name", "The Chronicle"), ownerEmail is null ? null : new XElement(Itunes + "email", ownerEmail)),
            new XElement(Itunes + "image", new XAttribute("href", Url(CoverKey))),
            new XElement(Itunes + "category", new XAttribute("text", "Fiction")),
            new XElement(Itunes + "explicit", "false"),
            new XElement(Itunes + "type", "episodic"),
            items.Select(item => new XElement("item",
                new XElement("title", Clean(ItemTitle(worldName, item))),
                new XElement("description", item.Headline is null ? "A quiet year in the chronicle." : $"{Clean(item.Headline).TrimEnd('.')}."),
                new XElement("guid", new XAttribute("isPermaLink", "false"), $"{worldName}-{item.Date}"),
                new XElement("pubDate", item.Published.ToString("r", CultureInfo.InvariantCulture)),
                new XElement("enclosure", new XAttribute("url", Url(DailyRun.NarrationKey(item.Date))),
                    new XAttribute("length", item.AudioBytes), new XAttribute("type", "audio/mpeg")),
                new XElement(Itunes + "duration", item.AudioSeconds),
                new XElement(Itunes + "episodeType", "full"))));
        var rss = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
                new XAttribute(XNamespace.Xmlns + "atom", Atom), channel));
        using var writer = new Utf8StringWriter();
        rss.Save(writer);
        return writer.ToString();
    }

    public static string ItemTitle(string worldName, FeedItem item) =>
        item.Headline is null ? $"{worldName}, Year {item.ToYear}" : $"Year {item.ToYear}: {item.Headline}";

    /// <summary>The archive page: every episode with its map, its narration and its script.</summary>
    public static string Archive(string worldName, IReadOnlyList<FeedItem> items)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(Title(worldName))}</title>
            <link rel="alternate" type="application/rss+xml" title="{E(Title(worldName))}" href="{FeedKey}">
            <style>
            body {"{"} margin: 0 auto; max-width: 46rem; padding: 2rem 1rem; background: #ecebe7; color: #1a1a18; font: 16px/1.5 Georgia, serif; {"}"}
            h1 {"{"} font-style: italic; font-weight: normal; font-size: 2.4rem; margin: 0 0 .25rem; {"}"}
            article {"{"} background: #f7f6f3; border: 1px solid #dcdad4; border-radius: 12px; padding: 1rem 1.25rem; margin: 1rem 0; {"}"}
            .when {"{"} color: #66645d; font: 13px system-ui, sans-serif; {"}"} img {"{"} width: 100%; border-radius: 8px; {"}"} audio {"{"} width: 100%; {"}"}
            </style></head><body>
            <h1>{E(Title(worldName))}</h1>
            <p class="when">A new episode every morning · <a href="{FeedKey}">podcast feed</a></p>

            """);
        foreach (var item in items)
            html.Append($"""
                <article>
                <p class="when">{E(item.Date)} · Year {item.ToYear} · {item.AudioSeconds / 60}:{item.AudioSeconds % 60:00}</p>
                <h2>{E(item.Headline ?? "A quiet year")}</h2>
                <audio controls preload="none" src="{DailyRun.NarrationKey(item.Date)}"></audio>
                <p><a href="{DailyRun.ScriptKey(item.Date)}">Read the script</a></p>
                <img loading="lazy" alt="The map that morning" src="{DailyRun.MapKey(item.Date)}" onerror="this.remove()">
                </article>

                """);
        html.Append("</body></html>\n");
        return html.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DailyRunTests|FullyQualifiedName~FeedTests"`
Expected: `Passed!  - Failed: 0, Passed: 24`. Then `dotnet test sim` → `Passed: 650` in total.

- [x] **Step 5: Commit**

```bash
git add README.md docs sim
git commit -m "feat(broadcast): publish a podcast feed and an archive page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §4 M3 ElevenLabs audio in chunks under the model's limit, joined | 1, 3 |
| §4 M3 pronunciation dictionary from world names | 2, 3 |
| §3.4 speech failing → text and map, retried next run; never paid twice | 3 |
| §3.4 spend caps: a monthly character budget | 3 |
| §4 M3 podcast RSS and archive page on R2 | 4 |
| §5 tests: speech chunking, RSS generation, the budget stopping calls | 1, 3, 4 |

**Done when (M3):** each morning's post carries the narration; names are said the same way every day; the budget stops calls; with a public bucket the feed validates in a podcast validator.

Deferred to M4: video (an MP4 of the map and the narration) and YouTube.
