# Daily Broadcast — Milestone 4 Implementation Plan (sub-project 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Milestone 4 of the daily broadcast (optional in the brief): each morning's episode as a video, the map held under the narration, uploaded to YouTube once a day, never holding up the other channels.

**Architecture:** `Media/Video.cs` makes the MP4 with ffmpeg; `Channels/YouTube.cs` is one more `IEpisodeChannel`, with its own manifest step, added by the live broadcast when the YouTube secrets are set.

**Tech Stack:** .NET 8 / C# 12, xUnit, GitHub Actions; ffmpeg (installed by the workflow). No new packages: Google's OAuth and upload endpoints are called over `HttpClient`.

**Spec:** `docs/superpowers/specs/2026-10-26-daily-broadcast-design.md` (§4 M4). **Builds on:** M1–M3.

## Global Constraints

Everything in M1–M3's Global Constraints still applies. In addition:

- **One upload a day:** the `youtube` step is marked the moment an upload is accepted; a rerun skips it.
- **Failures don't block other channels:** each channel has its own step; a failed upload is retried by the next run.
- **Dry runs never upload.**
- **No real network calls in tests:** fake handlers stand in for Google; the ffmpeg test runs ffmpeg only where it is installed.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Built before the owner's YouTube secrets | The code is ready and off until `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET` and `YOUTUBE_REFRESH_TOKEN` exist | The owner asked for every step that can be done without them |
| Video | The day's map, still, under the narration; 1,080 × 1,080 H.264 at 6 fps, AAC 160 kb/s | Nothing moves, so a few frames a second keep the file small; a square suits the map |
| Making it | ffmpeg, installed by the workflow when missing | The standard tool; nothing to bundle in .NET |
| Privacy | `public` by default (`unlisted` and `private` in config) | YouTube keeps uploads private anyway until the owner's Google app passes its API audit; the guide says so |
| Category | 24 (Entertainment) | Fiction and storytelling |
| No narration yet | The video waits (logged, not marked, the run not failed) | A video needs its audio; the narration may follow on a later run |
| At most once | The upload session is recorded (`youtube.json`) before a byte is sent, and the video id when YouTube accepts it; an unfinished upload is asked about and finished or resumed, never begun again; one YouTube no longer knows is left alone, with a warning | "One upload a day" survives a lost answer, a timeout or a run that stops after uploading |
| Backlog | One video a run, of today's or yesterday's episode only | Turning YouTube on (or an outage) never uploads a week of videos at once, nor runs out of the daily API quota |
| Dry runs | Make and store the video under `staging/`, never upload | YouTube has no test channel; the first video is not made in production |
| ffmpeg install | Allowed to fail without stopping the run | A failed install costs only the video |
| Privacy actually given | Read from YouTube's answer; the log warns when it differs | Unaudited apps' uploads are made private |

**Verification:** each task's tests fail first and pass after its implementation (the code was replayed task by task in this order); the ffmpeg test made a real MP4 from a map and two seconds of audio. An independent review of the prototype found that a lost answer, a timeout or a run stopping after the upload could upload a day twice, that turning YouTube on would upload a week of videos in one run, that a failed ffmpeg install would stop the whole broadcast, that the setup guide's Desktop client cannot be used with the OAuth Playground, that the description limit is in bytes, that a private result went unnoticed, and smaller points about running ffmpeg. All are fixed below and covered by tests.

## File Structure

```
sim/FantasyWorld.Broadcast/  Media/Video.cs (1); Channels/YouTube.cs, Channels/DiscordWebhook.cs, BroadcastConfig.cs (2);
                             DailyRun.cs, BroadcastApp.cs (3)
sim/FantasyWorld.Sim.Tests/Broadcasting/  VideoTests.cs (1), YouTubeTests.cs (2), DailyRunTests.cs (3)
broadcast.json, .github/workflows/broadcast.yml, docs/broadcast-setup.md, README.md (3)
```

All commands run from the repository root. Requires the .NET 8 SDK (and ffmpeg to make a video locally).

---

### Task 1: The episode as a video

Spec §4, M4. The map held still for the length of the narration, as an MP4 YouTube accepts: H.264 at 1,080 × 1,080 (a few frames a second, since nothing moves), AAC audio, the index at the front. A map that is not square is fitted, never stretched. Made by ffmpeg (no input read, killed if the run is cancelled), which the workflow installs; no imaging or video package in .NET.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Media/Video.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/VideoTests.cs`

**Interfaces:**
- Consumes: `MapPainter` (M2), the narration (M3).
- Produces: `interface IVideoMaker { MakeAsync(mapPng, narrationMp3) }`, `VideoException`, `FfmpegVideo(ffmpeg, size) { DefaultSize; Arguments(image, audio, output, size) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Broadcasting/VideoTests.cs`:
```csharp
using System.Diagnostics;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class VideoTests
{
    private static bool Has(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public void TheVideo_IsAStillMapUnderTheNarration_ThatYouTubeAccepts()
    {
        var args = FfmpegVideo.Arguments("map.png", "n.mp3", "out.mp4", 1080);
        Assert.Equal(new[] { "-loop", "1" }, args.SkipWhile(a => a != "-loop").Take(2));
        Assert.Contains("-shortest", args);
        Assert.Contains("libx264", args);
        Assert.Contains("aac", args);
        Assert.Contains("-nostdin", args);
        Assert.StartsWith("scale=1080:1080:force_original_aspect_ratio=decrease", args.Single(a => a.StartsWith("scale=", StringComparison.Ordinal)));
        Assert.Equal("out.mp4", args[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfmpegVideo(size: 1081)); // 4:2:0 needs even sides
    }

    [Fact]
    public async Task Ffmpeg_MakesAnMp4_OrSaysWhyNot()
    {
        await Assert.ThrowsAsync<VideoException>(() => new FfmpegVideo("no-such-ffmpeg-here").MakeAsync([1], [2]));
        if (!Has("ffmpeg")) return; // the workflow installs it; a machine without it checks only the failure above

        // Two seconds of silence as the narration, and a real map.
        var mp3 = Path.GetTempFileName() + ".mp3";
        using (var make = Process.Start("ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i anullsrc=r=44100:cl=mono -t 2 -b:a 128k {mp3}")) make.WaitForExit();
        var sim = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 5);
        var video = await new FfmpegVideo(size: 240).MakeAsync(MapPainter.Png(sim.World, sim.Log, 256), await File.ReadAllBytesAsync(mp3));
        File.Delete(mp3);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(video, 4, 4));
        await Assert.ThrowsAsync<VideoException>(() => new FfmpegVideo().MakeAsync([1, 2, 3], [4, 5, 6])); // not a picture
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'FfmpegVideo' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Broadcast/Media/Video.cs`:
```csharp
using System.Diagnostics;
using System.Globalization;

namespace FantasyWorld.Broadcast.Media;

/// <summary>Makes the episode's video from its map and narration. Throws <see cref="VideoException"/> when it cannot.</summary>
public interface IVideoMaker
{
    Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, CancellationToken ct = default);
}

public sealed class VideoException(string message) : Exception(message);

/// <summary>
/// The video (M4): the map held still for the length of the narration, as an MP4 that YouTube accepts (H.264 at
/// 1080×1080, a few frames a second since nothing moves; AAC audio; the index at the front for streaming), made by ffmpeg.
/// </summary>
public sealed class FfmpegVideo : IVideoMaker
{
    public const int DefaultSize = 1080;

    private readonly string _ffmpeg;
    private readonly int _size;

    /// <param name="size">The video's side in pixels; even, as H.264 in 4:2:0 needs.</param>
    public FfmpegVideo(string ffmpeg = "ffmpeg", int size = DefaultSize)
    {
        if (size < 16 || size % 2 != 0) throw new ArgumentOutOfRangeException(nameof(size), "The video's side must be even and at least 16 pixels.");
        (_ffmpeg, _size) = (ffmpeg, size);
    }

    /// <summary>
    /// The arguments for <c>ffmpeg</c>: a looped still image (fitted into the square, never stretched) and the audio, cut
    /// to the audio's length.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string image, string audio, string output, int size) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-loop", "1", "-framerate", "2", "-i", image,
        "-i", audio,
        "-vf", $"scale={size}:{size}:force_original_aspect_ratio=decrease:flags=lanczos,pad={size}:{size}:(ow-iw)/2:(oh-ih)/2:color=0xecebe7,format=yuv420p",
        "-c:v", "libx264", "-tune", "stillimage", "-preset", "veryfast", "-r", "6",
        "-c:a", "aac", "-b:a", "160k",
        "-shortest", "-movflags", "+faststart", output,
    ];

    public async Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, CancellationToken ct = default)
    {
        var work = Directory.CreateTempSubdirectory("fw-video-");
        try
        {
            var (image, audio, output) = (Path.Combine(work.FullName, "map.png"), Path.Combine(work.FullName, "narration.mp3"),
                Path.Combine(work.FullName, "episode.mp4"));
            await File.WriteAllBytesAsync(image, mapPng, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(audio, narrationMp3, ct).ConfigureAwait(false);
            var start = new ProcessStartInfo(_ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in Arguments(image, audio, output, _size)) start.ArgumentList.Add(argument);
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new VideoException("ffmpeg could not be started.");
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new VideoException($"ffmpeg could not be started; is it installed? ({e.Message})");
            }
            using (process)
            {
                var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
                try
                {
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                if (process.ExitCode != 0 || !File.Exists(output))
                    throw new VideoException($"ffmpeg failed (exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}): {(await errors.ConfigureAwait(false)).Trim()}");
            }
            return await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~VideoTests"`
Expected: `Passed!  - Failed: 0, Passed: 2`. Then `dotnet test sim` → `Passed: 652` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(broadcast): make each episode a video of its map and narration

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The YouTube upload

Spec §4, M4. A channel that uploads the day's video at most once: it makes and stores the MP4 (`episode.mp4`), refreshes the access token, opens a resumable upload session with the video's details (title within 100 characters, description within 5,000 UTF-8 bytes, no angle brackets, the category and privacy from config, not made for kids), records the session (`youtube.json`) before sending a byte, and records the video's id when YouTube accepts it. A lost answer, a server error or a run that stopped is followed by asking YouTube how much arrived and finishing or resuming that upload; a session YouTube no longer knows is left alone with a warning. One video a run, of today's or yesterday's episode. Without an account (dry runs) the video is made and stored, not uploaded. An episode without its narration waits. Errors name the status code only, with a hint when the refresh token is refused; the privacy YouTube actually gave is checked.

**Files:**
- Create: `sim/FantasyWorld.Broadcast/Channels/YouTube.cs`
- Modify: `sim/FantasyWorld.Broadcast/BroadcastConfig.cs`, `sim/FantasyWorld.Broadcast/Channels/DiscordWebhook.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/YouTubeTests.cs`

**Interfaces:**
- Consumes: Task 1; `IEpisodeChannel` (M1–M3).
- Produces: `record YouTubeAccount(ClientId, ClientSecret, RefreshToken) { FromEnvironment(env) }`; `record UploadState`; `YouTube(http, account, video, settings) { TokenUrl; UploadUrl; MaxTitle; MaxDescriptionBytes; Uploads; VideoKey; StateKey; Attach; Details(episode, settings) }`; `ChannelWaitingException`, `interface IStatefulChannel`; `record BroadcastYouTube { Upload; Privacy; CategoryId }`, `BroadcastConfig.YouTube`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Broadcasting/YouTubeTests.cs`:
```csharp
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class YouTubeTests : IDisposable
{
    /// <summary>Google, scripted: the token endpoint always answers; uploads answer from the queue (null: the answer is lost).</summary>
    private sealed class FakeGoogle(params Func<HttpRequestMessage, HttpResponseMessage?>[] uploads) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpRequestMessage Request, byte[]? Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct)));
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"AT-123","expires_in":3599}""") };
            return uploads[Math.Min(_next++, uploads.Length - 1)](request) ?? throw new HttpRequestException("connection reset");
        }
    }

    private static HttpResponseMessage Session(HttpRequestMessage _)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Location = new Uri("https://www.googleapis.com/upload/session/1");
        return response;
    }

    private static HttpResponseMessage Created(HttpRequestMessage _) =>
        new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"vid1","status":{"privacyStatus":"private"}}""") };

    private static Func<HttpRequestMessage, HttpResponseMessage?> Status(HttpStatusCode code, string? range = null) => _ =>
    {
        var response = new HttpResponseMessage(code);
        if (range is not null) response.Headers.TryAddWithoutValidation("Range", range);
        return response;
    };

    internal sealed class FakeVideo(bool fails = false) : IVideoMaker
    {
        public int Made { get; private set; }

        public Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, CancellationToken ct = default)
        {
            if (fails) throw new VideoException("ffmpeg could not be started; is it installed?");
            Made++;
            return Task.FromResult(new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        }
    }

    private static readonly YouTubeAccount Account = new("client-id", "CLIENT-SECRET", "REFRESH-SECRET");
    private readonly TempDir _dir = new();
    private readonly StringWriter _log = new();
    private readonly LocalObjectStore _store;

    public YouTubeTests() => _store = new LocalObjectStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private static Episode Sample(string date = "2026-10-27") => BroadcastPartsTests.SampleEpisode() with
    {
        Date = date,
        Headline = new EpisodeStory(1, 12, "War", "The War of <Ash> begins"),
    };

    private YouTube Channel(FakeGoogle google, YouTubeAccount? account = null, IVideoMaker? video = null, string today = "2026-10-27")
    {
        var youtube = new YouTube(new HttpClient(google), account ?? Account, video ?? new FakeVideo(), new BroadcastYouTube());
        youtube.Attach(_store, _log, today);
        return youtube;
    }

    private async Task<UploadState?> State(string date = "2026-10-27") =>
        await _store.GetAsync(YouTube.StateKey(date)) is { } bytes
            ? System.Text.Json.JsonSerializer.Deserialize<UploadState>(bytes, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
            : null;

    [Fact]
    public async Task AnEpisode_IsUploadedOnce_ThroughAResumableSession()
    {
        var google = new FakeGoogle(Session, Created);
        await Channel(google).PublishAsync(Sample(), [1], [2]);
        var (start, details) = google.Seen[1];
        Assert.Equal(YouTube.UploadUrl, start.RequestUri!.ToString());
        Assert.Equal("Bearer AT-123", start.Headers.Authorization!.ToString());
        Assert.Equal("Aerenmoor, Year 12: The War of ‹Ash› begins", (string)JsonNode.Parse(details)!["snippet"]!["title"]!);
        Assert.Equal((HttpMethod.Put, 8), (google.Seen[2].Request.Method, google.Seen[2].Body!.Length));
        Assert.Equal(new UploadState(UploadState.Done, null, 8, "vid1", "private"), await State());
        Assert.NotNull(await _store.GetAsync(YouTube.VideoKey("2026-10-27")));
        Assert.Contains("YouTube made the video private, not public", _log.ToString());

        await Channel(google).PublishAsync(Sample(), [1], [2]); // a rerun: already uploaded
        Assert.Equal(3, google.Seen.Count);
    }

    [Fact]
    public async Task ALostAnswer_IsAskedAbout_NotUploadedAgain()
    {
        var google = new FakeGoogle(Session, _ => null, Created);
        await Channel(google).PublishAsync(Sample(), [1], [2]);
        Assert.Equal(UploadState.Done, (await State())!.State);
        var query = google.Seen[^1].Request;
        Assert.Equal("bytes */8", query.Content!.Headers.ContentRange!.ToString());
        Assert.Equal(1, google.Seen.Count(s => s.Body is { Length: 8 }));
    }

    [Fact]
    public async Task AnUnfinishedUpload_IsResumedNextRun_FromWhereYouTubeSaysItStopped()
    {
        var failing = new FakeGoogle(Session, Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<ChannelException>(() => Channel(failing).PublishAsync(Sample(), [1], [2]));
        Assert.Equal(UploadState.Uploading, (await State())!.State);

        var resuming = new FakeGoogle(Status((HttpStatusCode)308, "bytes=0-3"), Created);
        await Channel(resuming, today: "2026-10-28").PublishAsync(Sample(), [1], [2]);
        var rest = resuming.Seen[^1];
        Assert.Equal("bytes 4-7/8", rest.Request.Content!.Headers.ContentRange!.ToString());
        Assert.Equal(4, rest.Body!.Length);
        Assert.Equal(UploadState.Done, (await State())!.State);
    }

    [Fact]
    public async Task AnUploadYouTubeForgot_IsLeftAlone_RatherThanDuplicated()
    {
        await Assert.ThrowsAsync<ChannelException>(() => Channel(new FakeGoogle(Session, Status(HttpStatusCode.ServiceUnavailable))).PublishAsync(Sample(), [1], [2]));
        var gone = new FakeGoogle(Status(HttpStatusCode.NotFound));
        await Channel(gone).PublishAsync(Sample(), [1], [2]);
        Assert.Equal(UploadState.Abandoned, (await State())!.State);
        Assert.Contains("it is not uploaded again", _log.ToString());
        await Channel(gone).PublishAsync(Sample(), [1], [2]);
        Assert.Equal(2, gone.Seen.Count); // token + the one question; nothing more
    }

    [Fact]
    public async Task OnlyTheDaysEpisode_OneARun_AndOnlyWithItsNarration()
    {
        var google = new FakeGoogle(Session, Created);
        var youtube = Channel(google, today: "2026-10-29");
        await youtube.PublishAsync(Sample("2026-10-25"), [1], [2]);
        Assert.Contains("too old to upload", _log.ToString());
        await Assert.ThrowsAsync<ChannelWaitingException>(() => youtube.PublishAsync(Sample("2026-10-29"), [1], null));
        await youtube.PublishAsync(Sample("2026-10-28"), [1], [2]);
        await Assert.ThrowsAsync<ChannelWaitingException>(() => youtube.PublishAsync(Sample("2026-10-29"), [1], [2]));
        Assert.Equal(3, google.Seen.Count);
    }

    [Fact]
    public async Task ADryRun_MakesAndStoresTheVideo_WithoutUploading()
    {
        var google = new FakeGoogle(Session, Created);
        var dry = new YouTube(new HttpClient(google), null, new FakeVideo(), new BroadcastYouTube());
        dry.Attach(_store, _log, "2026-10-27");
        Assert.False(dry.Uploads);
        await dry.PublishAsync(Sample(), [1], [2]);
        Assert.Empty(google.Seen);
        Assert.NotNull(await _store.GetAsync(YouTube.VideoKey("2026-10-27")));
        Assert.Contains("dry runs do not upload", _log.ToString());
    }

    [Fact]
    public async Task Failures_NameNoSecret_AndTheDetailsFitYouTubesLimits()
    {
        var refused = new HttpClient(new StorageTests.FakeHttp((HttpStatusCode.BadRequest, "")));
        var youtube = new YouTube(refused, Account, new FakeVideo(), new BroadcastYouTube());
        youtube.Attach(_store, _log, "2026-10-27");
        var error = await Assert.ThrowsAsync<ChannelException>(() => youtube.PublishAsync(Sample(), [1], [2]));
        Assert.Contains("refresh token may have expired", error.Message);
        Assert.DoesNotContain("SECRET", error.Message);
        var noVideo = Channel(new FakeGoogle(Session), video: new FakeVideo(fails: true));
        Assert.Contains("is it installed", (await Assert.ThrowsAsync<ChannelException>(() => noVideo.PublishAsync(Sample(), [1], [2]))).Message);

        var long1 = Sample() with { Script = new string('é', 6000), Headline = new EpisodeStory(1, 12, "War", new string('b', 200)) };
        var json = YouTube.Details(long1, new BroadcastYouTube { Privacy = "unlisted" });
        Assert.Equal(YouTube.MaxTitle, ((string)json["snippet"]!["title"]!).Length);
        Assert.InRange(Encoding.UTF8.GetByteCount((string)json["snippet"]!["description"]!), 4900, YouTube.MaxDescriptionBytes);
        Assert.Equal("unlisted", (string)json["status"]!["privacyStatus"]!);
        Assert.Null(YouTubeAccount.FromEnvironment(_ => null));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "youTube": { "privacy": "everyone" } }"""));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'YouTubeAccount' could not be found (are you missing a using directive or an assembly reference?)`.

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
/// The YouTube upload (M4): on when the three YouTube secrets are set. YouTube keeps videos uploaded through a Google
/// app that has not passed its audit private, whatever is asked here.
/// </summary>
public sealed record BroadcastYouTube
{
    public bool Upload { get; init; } = true;

    /// <summary>public, unlisted or private.</summary>
    public string Privacy { get; init; } = "public";

    /// <summary>24 is Entertainment.</summary>
    public string CategoryId { get; init; } = "24";
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

    public BroadcastYouTube YouTube { get; init; } = new();

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
        if (YouTube.Privacy is not ("public" or "unlisted" or "private"))
            throw new FormatException($"youTube.privacy must be public, unlisted or private (got \"{YouTube.Privacy}\").");
        if (PublicBaseUrl is { } url && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            throw new FormatException($"publicBaseUrl must be an https address (got \"{url}\").");
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
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

public class ChannelException(string message) : Exception(message);

/// <summary>The channel has nothing to publish yet (a video without its narration); the run is not failed, and it waits.</summary>
public sealed class ChannelWaitingException(string message) : ChannelException(message);

/// <summary>A channel that keeps its own state in the broadcast's store (YouTube's uploads); told the store, log and date each run.</summary>
public interface IStatefulChannel
{
    void Attach(Storage.IObjectStore store, TextWriter log, string today);
}

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

`sim/FantasyWorld.Broadcast/Channels/YouTube.cs`:
```csharp
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast.Channels;

/// <summary>The YouTube upload's account (the three secrets).</summary>
public sealed record YouTubeAccount(string ClientId, string ClientSecret, string RefreshToken)
{
    public static YouTubeAccount? FromEnvironment(Func<string, string?> env) =>
        (env("YOUTUBE_CLIENT_ID"), env("YOUTUBE_CLIENT_SECRET"), env("YOUTUBE_REFRESH_TOKEN")) is
            ({ Length: > 0 } id, { Length: > 0 } secret, { Length: > 0 } refresh)
            ? new YouTubeAccount(id, secret, refresh)
            : null;
}

/// <summary>Where a day's upload stands, kept beside the episode (<c>episodes/&lt;date&gt;/youtube.json</c>).</summary>
public sealed record UploadState(string State, string? Session = null, long Length = 0, string? VideoId = null, string? Privacy = null)
{
    public const string Uploading = "uploading", Done = "done", Abandoned = "abandoned";
}

/// <summary>
/// Uploads the day's episode as a video (M4), at most once. The MP4 is made and stored first; the upload session is
/// recorded before a byte is sent, and the video's id when YouTube accepts it. A run that finds a session but no id asks
/// YouTube how much arrived and finishes or resumes that upload, never starting a second; a session YouTube no longer
/// knows is left alone (with a loud warning) rather than risk a duplicate. One upload a run, of today's or yesterday's
/// episode only, so turning YouTube on never floods the channel with a week of videos. Without an account (dry runs) the
/// video is made and stored but not uploaded. An episode without narration has nothing to show, so it waits.
/// </summary>
public sealed class YouTube(HttpClient http, YouTubeAccount? account, IVideoMaker video, BroadcastYouTube settings) : IEpisodeChannel, IStatefulChannel
{
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string UploadUrl = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status";

    /// <summary>YouTube's limits: the title in characters, the description in UTF-8 bytes.</summary>
    public const int MaxTitle = 100, MaxDescriptionBytes = 5000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private IObjectStore? _store;
    private TextWriter _log = TextWriter.Null;
    private string _today = "";
    private int _uploads;

    public string Name => "youtube";

    /// <summary>Whether this channel uploads (an account is set) or only makes the video (dry runs).</summary>
    public bool Uploads => account is not null;

    public static string VideoKey(string date) => $"episodes/{date}/episode.mp4";

    public static string StateKey(string date) => $"episodes/{date}/youtube.json";

    public void Attach(IObjectStore store, TextWriter log, string today) => (_store, _log, _today, _uploads) = (store, log, today, 0);

    public static JsonObject Details(Episode episode, BroadcastYouTube settings)
    {
        var title = episode.Headline is { } headline ? $"{episode.WorldName}, Year {episode.ToYear}: {headline.Text}" : $"{episode.WorldName}, Year {episode.ToYear}";
        // YouTube refuses angle brackets in titles and descriptions.
        static string Plain(string text) => text.Replace('<', '‹').Replace('>', '›');
        var description = $"{episode.Script}\n\nTold by {episode.Chronicler}. A new episode every morning.";
        return new JsonObject
        {
            ["snippet"] = new JsonObject
            {
                ["title"] = ClipChars(Plain(title), MaxTitle),
                ["description"] = ClipBytes(Plain(description), MaxDescriptionBytes),
                ["categoryId"] = settings.CategoryId,
                ["tags"] = new JsonArray("fantasy", "chronicle", "worldbuilding", Plain(episode.WorldName)),
            },
            ["status"] = new JsonObject { ["privacyStatus"] = settings.Privacy, ["selfDeclaredMadeForKids"] = false },
        };
    }

    public async Task PublishAsync(Episode episode, byte[]? map, byte[]? audio, CancellationToken ct = default)
    {
        var store = _store ?? throw new InvalidOperationException("The YouTube channel was not attached to the broadcast's store.");
        try
        {
            var day = episode.Date;
            var state = await LoadAsync(store, day, ct).ConfigureAwait(false);
            if (state?.State == UploadState.Done)
            {
                _log.WriteLine($"youtube: the episode of {day} was already uploaded ({state.VideoId}).");
                return;
            }
            if (state?.State == UploadState.Abandoned) return;
            if (state?.State == UploadState.Uploading && account is not null)
            {
                await ResumeAsync(store, day, state, ct).ConfigureAwait(false);
                return;
            }
            if (day != _today && day != Yesterday(_today))
            {
                _log.WriteLine($"youtube: the episode of {day} is too old to upload now; only the day's episode goes to YouTube.");
                return;
            }
            if (map is null || audio is null) throw new ChannelWaitingException("the video waits for the episode's narration and map.");
            if (_uploads >= 1) throw new ChannelWaitingException("one video a run; this one goes up on the next.");

            byte[] mp4;
            try
            {
                mp4 = await video.MakeAsync(map, audio, ct).ConfigureAwait(false);
            }
            catch (VideoException e)
            {
                throw new ChannelException($"the video could not be made ({e.Message}).");
            }
            await store.PutAsync(VideoKey(day), mp4, "video/mp4", ct).ConfigureAwait(false);
            if (account is null)
            {
                _log.WriteLine($"youtube: the video of {day} was made and stored ({mp4.Length / 1024:N0} KB); dry runs do not upload.");
                return;
            }

            _uploads++;
            var token = await TokenAsync(ct).ConfigureAwait(false);
            using var start = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = JsonContent.Create(Details(episode, settings)) };
            start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            start.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "video/mp4");
            start.Headers.TryAddWithoutValidation("X-Upload-Content-Length", mp4.Length.ToString(CultureInfo.InvariantCulture));
            Uri session;
            using (var opened = await SendAsync(start, "open the upload", ct).ConfigureAwait(false))
                session = opened.Headers.Location ?? throw new ChannelException("YouTube opened no upload session.");
            // Recorded before a byte is sent: from here on, this day's video is only ever finished, never begun again.
            var uploading = new UploadState(UploadState.Uploading, session.ToString(), mp4.Length);
            await SaveAsync(store, day, uploading, ct).ConfigureAwait(false);
            await SendBytesAsync(store, day, uploading, token, mp4, 0, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not ChannelException and not OperationCanceledException)
        {
            throw new ChannelException($"YouTube failed ({e.GetType().Name}).");
        }
    }

    /// <summary>Sends the video from <paramref name="from"/>; when the answer is lost, asks how much arrived and carries on once.</summary>
    private async Task SendBytesAsync(IObjectStore store, string day, UploadState state, string token, byte[] mp4, long from, CancellationToken ct)
    {
        using var put = new HttpRequestMessage(HttpMethod.Put, state.Session) { Content = new ByteArrayContent(mp4, (int)from, (int)(mp4.Length - from)) };
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        if (from > 0) put.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, mp4.Length - 1, mp4.Length);
        HttpResponseMessage? response = null;
        try
        {
            response = await http.SendAsync(put, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            // The answer was lost; the bytes may all have arrived.
        }
        using (response)
        {
            if (response is { IsSuccessStatusCode: true })
            {
                await FinishAsync(store, day, state, response, ct).ConfigureAwait(false);
                return;
            }
            if (response is { StatusCode: < HttpStatusCode.InternalServerError } and not { StatusCode: (HttpStatusCode)308 })
                throw new ChannelException($"YouTube could not upload the video: HTTP {(int)response.StatusCode}.");
        }
        if (from > 0) throw new ChannelException("YouTube did not take the rest of the video; the upload is finished on the next run.");
        await ResumeAsync(store, day, state, ct, token, mp4).ConfigureAwait(false);
    }

    /// <summary>Asks YouTube where an upload stands, then finishes it: done, resumed from where it stopped, or given up.</summary>
    private async Task ResumeAsync(IObjectStore store, string day, UploadState state, CancellationToken ct, string? token = null, byte[]? mp4 = null)
    {
        token ??= await TokenAsync(ct).ConfigureAwait(false);
        using var query = new HttpRequestMessage(HttpMethod.Put, state.Session) { Content = new ByteArrayContent([]) };
        query.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        query.Content.Headers.ContentRange = new ContentRangeHeaderValue(state.Length);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ChannelException("YouTube could not be asked how the upload stands; it is asked again on the next run.");
        }
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                await FinishAsync(store, day, state, response, ct).ConfigureAwait(false);
                return;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                await SaveAsync(store, day, state with { State = UploadState.Abandoned, Session = null }, ct).ConfigureAwait(false);
                _log.WriteLine($"warning: YouTube no longer knows the upload of {day}; it is not uploaded again, to avoid a duplicate. "
                               + $"Check the channel, and delete {StateKey(day)} to try once more.");
                return;
            }
            if (response.StatusCode != (HttpStatusCode)308)
                throw new ChannelException($"YouTube could not say how the upload stands: HTTP {(int)response.StatusCode}.");
            var received = response.Headers.TryGetValues("Range", out var ranges) && ranges.FirstOrDefault() is { } range
                ? long.Parse(range[(range.IndexOf('-') + 1)..], CultureInfo.InvariantCulture) + 1
                : 0;
            mp4 ??= await store.GetAsync(VideoKey(day), ct).ConfigureAwait(false)
                    ?? throw new ChannelException($"the stored video of {day} is missing, so its upload cannot be finished.");
            await SendBytesAsync(store, day, state, token, mp4, received, ct).ConfigureAwait(false);
        }
    }

    private async Task FinishAsync(IObjectStore store, string day, UploadState state, HttpResponseMessage response, CancellationToken ct)
    {
        string? id = null, privacy = null;
        try
        {
            var video = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
            id = video?["id"]?.GetValue<string>();
            privacy = video?["status"]?["privacyStatus"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
        }
        await SaveAsync(store, day, new UploadState(UploadState.Done, null, state.Length, id, privacy), ct).ConfigureAwait(false);
        _log.WriteLine($"youtube: uploaded the episode of {day}{(id is null ? "" : $" (https://youtu.be/{id})")}.");
        if (privacy is not null && privacy != settings.Privacy)
            _log.WriteLine($"warning: YouTube made the video {privacy}, not {settings.Privacy}: uploads stay private until the Google app passes "
                           + "YouTube's API audit (see docs/broadcast-setup.md).");
    }

    private static async Task<UploadState?> LoadAsync(IObjectStore store, string day, CancellationToken ct) =>
        await store.GetAsync(StateKey(day), ct).ConfigureAwait(false) is { } bytes ? JsonSerializer.Deserialize<UploadState>(bytes, Json) : null;

    private static Task SaveAsync(IObjectStore store, string day, UploadState state, CancellationToken ct) =>
        store.PutAsync(StateKey(day), JsonSerializer.SerializeToUtf8Bytes(state, Json), "application/json", ct);

    private static string Yesterday(string today) =>
        DateOnly.TryParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "";

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        var account1 = account ?? throw new ChannelException("YouTube is not set up.");
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = account1.ClientId, ["client_secret"] = account1.ClientSecret,
                ["refresh_token"] = account1.RefreshToken, ["grant_type"] = "refresh_token",
            }),
        };
        using var response = await SendAsync(request, "sign in", ct).ConfigureAwait(false);
        try
        {
            var answer = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
            return answer?["access_token"] is JsonValue value && value.TryGetValue<string>(out var token) && token.Length > 0
                ? token
                : throw new ChannelException("YouTube's sign-in gave no access token.");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new ChannelException("YouTube's sign-in answered with something other than a token.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string action, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ChannelException($"YouTube could not be reached to {action} ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChannelException($"YouTube did not answer in time to {action}.");
        }
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        // A refused refresh token needs the owner (it expires after a week while the Google app is in testing).
        var hint = status is 400 or 401 && action == "sign in" ? " The refresh token may have expired; see docs/broadcast-setup.md." : "";
        throw new ChannelException($"YouTube could not {action}: HTTP {status}.{hint}");
    }

    private static string ClipChars(string text, int max) =>
        text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 2]) ? max - 2 : max - 1)] + "…";

    private static string ClipBytes(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        var budget = maxBytes - Encoding.UTF8.GetByteCount("…");
        var end = text.Length;
        while (end > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, end)) > budget) end -= Math.Max(1, (end - budget) / 4);
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "…";
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~YouTubeTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 659` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(broadcast): upload each episode to YouTube

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The daily video goes to YouTube

Spec §3–4, M4. The live broadcast adds the YouTube channel when the three YouTube secrets are set; a dry run adds it without an account, so it makes and stores the video but never uploads (YouTube has no test channel). Stateful channels are attached to the run's store each morning; the log warns when YouTube is set up but narration is off. Like every channel it has its own manifest step, so a failure never holds up Discord and is retried by the next run; a channel that is waiting is logged and left unmarked without failing the run. The workflow installs ffmpeg (a failed install costs only the video) and passes the secrets; the setup guide walks the owner through the Google side, including the seven-day refresh tokens of apps still in testing and the audit that unlocks public videos.

**Files:**
- Modify: `.github/workflows/broadcast.yml`, `README.md`, `broadcast.json`, `docs/broadcast-setup.md`, `sim/FantasyWorld.Broadcast/BroadcastApp.cs`, `sim/FantasyWorld.Broadcast/DailyRun.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `DailyRun`, `BroadcastApp` (M1–M3).
- Produces: the waiting state and channel attachment in `DailyRun`; `BroadcastApp.YouTubeChannel(config, options, env, http, output)`; workflow, `broadcast.json` and docs.

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
            ["YOUTUBE_CLIENT_ID"] = "id", ["YOUTUBE_CLIENT_SECRET"] = "s", ["YOUTUBE_REFRESH_TOKEN"] = "r", // a dry run never uploads
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
        var quiet = new StringWriter();
        Assert.Null(BroadcastApp.YouTubeChannel(BroadcastConfig.Parse("{}"), new RunOptions(), _ => null, new HttpClient(), quiet));
        Assert.Contains("YouTube secrets are not set", quiet.ToString());
        Assert.DoesNotContain("SECRET-TOKEN", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["publish"], output, () => Morning, env.GetValueOrDefault));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0117: 'BroadcastApp' does not contain a definition for 'YouTubeChannel'`.

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
      - name: Install ffmpeg (for the YouTube video)
        # A failed install costs only the video; the broadcast itself must still run.
        continue-on-error: true
        run: command -v ffmpeg || (sudo apt-get update -qq && sudo apt-get install -y -qq ffmpeg)
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
          YOUTUBE_CLIENT_ID: ${{ secrets.YOUTUBE_CLIENT_ID }}
          YOUTUBE_CLIENT_SECRET: ${{ secrets.YOUTUBE_CLIENT_SECRET }}
          YOUTUBE_REFRESH_TOKEN: ${{ secrets.YOUTUBE_REFRESH_TOKEN }}
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
cannot), has it read aloud by ElevenLabs, and posts it to Discord with the narration and the political map (and, with the YouTube secrets, as a video). A rerun the same day posts nothing new; with a public bucket it also keeps a podcast feed and an archive page. Setup:
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
  "youTube": {
    "upload": true,
    "privacy": "public",
    "categoryId": "24"
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
| `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET`, `YOUTUBE_REFRESH_TOKEN` | optional: the daily video upload (see section 5) |
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

## 5. YouTube (optional)

Each morning's map and narration can also go to YouTube as a video. It needs three more secrets:

1. In the Google Cloud console, create a project and enable the **YouTube Data API v3**.
2. Set up the **OAuth consent screen** (External), add yourself as a test user, then **Publish app** (to production).
   While the app is in testing, Google lets a refresh token live only seven days, and the upload stops after a week.
3. Under **Credentials**, create an **OAuth client ID** of type *Web application*, with the authorised redirect URI
   `https://developers.google.com/oauthplayground`. Note its client id and secret.
4. Get a refresh token for the channel's Google account in the OAuth 2.0 Playground: gear icon → *Use your own OAuth
   credentials* (paste the id and secret), select the scope `https://www.googleapis.com/auth/youtube.upload`, *Authorize
   APIs*, sign in as the channel, then *Exchange authorization code for tokens* and copy the refresh token.
5. Add the secrets `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET` and `YOUTUBE_REFRESH_TOKEN`.

YouTube keeps videos uploaded through a Google app that has not passed its API audit **private**, whatever `privacy`
says (the run log warns when that happens); request the audit (the YouTube API Services form) to publish them.

One video a run, of the day's episode (or yesterday's, if it was missed): turning YouTube on never uploads a backlog.
An upload is recorded before it starts and finished, never begun again, on a later run if it was cut short, so a video
never goes up twice. Dry runs make and store the video (`staging/episodes/<date>/episode.mp4`) but never upload. An
episode without narration waits for it; a failed upload never holds up Discord.

## 6. Start it

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
| `youTube.upload`, `youTube.privacy`, `youTube.categoryId` | `true`, `public`, `24` | the daily video (with the three YouTube secrets); 24 is Entertainment |
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
live/episodes/2026-10-27/episode.mp4   the video (with YouTube), and youtube.json, where its upload stands
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
        var youtube = YouTubeChannel(config, options, env, http, output);
        var channels = new List<IEpisodeChannel>();
        if (env(webhookName) is { Length: > 0 } webhook)
            channels.Add(new DiscordWebhook(http, webhook, config.DiscordName));
        else if (!options.DryRun)
        {
            output.WriteLine($"error: {webhookName} is not set, so the episode could not be posted.");
            return DailyRun.Failed;
        }
        if (youtube is not null) channels.Add(youtube);

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

    /// <summary>
    /// The YouTube channel: the live broadcast uploads with the three YouTube secrets; a dry run (YouTube has no test
    /// channel) only makes and stores the video, so the first real video is not made in production.
    /// </summary>
    public static Channels.YouTube? YouTubeChannel(BroadcastConfig config, RunOptions options, Func<string, string?> env, HttpClient http, TextWriter output)
    {
        if (!config.YouTube.Upload) return null;
        var account = options.DryRun ? null : Channels.YouTubeAccount.FromEnvironment(env);
        if (!options.DryRun && account is null)
        {
            output.WriteLine("YouTube: the YouTube secrets are not set, so no video is uploaded.");
            return null;
        }
        if (!config.Voice.Speak) output.WriteLine("warning: YouTube is set up but voice.speak is off, so there is no narration to make a video of.");
        return new Channels.YouTube(http, account, new Media.FfmpegVideo(), config.YouTube);
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

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DailyRunTests"`
Expected: `Passed!  - Failed: 0, Passed: 23`. Then `dotnet test sim` → `Passed: 660` in total.

- [x] **Step 5: Commit**

```bash
git add .github README.md broadcast.json docs sim
git commit -m "feat(broadcast): upload the daily video to YouTube from the live broadcast

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §4 M4 mp4 from the audio and the map | 1 |
| §4 M4 YouTube upload, one a day | 2, 3 |
| §3.4 failures don't block the other channels; retried next run | 3 |

**Done when (M4):** with the YouTube secrets set, one upload a day; a failed upload never holds up Discord.

Deferred: Godot diorama shots in the video (xvfb on the runner), chapters and thumbnails.

## Follow-up after the first dry run on GitHub Actions

The runner has no ffmpeg, and `apt-get update` there took over eight minutes. The workflow now installs ffmpeg only
when a video will be made (the YouTube secrets are set, or it is a dry run), without recommended packages, and gives the
step five minutes; a failed or slow install still costs only the video. Change in `.github/workflows/broadcast.yml`:

```yaml
    env:
      MAKES_VIDEO: ${{ secrets.YOUTUBE_REFRESH_TOKEN != '' || inputs.dry-run == true }}
    ...
      - name: Install ffmpeg (for the YouTube video)
        if: env.MAKES_VIDEO == 'true'
        continue-on-error: true
        timeout-minutes: 5
        run: command -v ffmpeg || (sudo apt-get update -qq && sudo apt-get install -y -qq --no-install-recommends ffmpeg)
```
