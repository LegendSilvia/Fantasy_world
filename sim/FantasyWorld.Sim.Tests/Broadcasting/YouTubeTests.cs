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

        public Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, CancellationToken ct = default) =>
            Task.FromResult(chunks.SelectMany(c => c).Concat(narrationMp3).ToArray());

        public Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, IReadOnlyList<TimedShot>? shots = null, CancellationToken ct = default)
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
    public async Task TheDioramaFilm_IsJoined_WhenEveryStretchWasFilmed_ElseTheMapPans()
    {
        var timeline = new FantasyWorld.Broadcast.Speech.Timeline(30, [new("Opening", 0), new("Headline", 8), new("Until tomorrow", 25)]);
        await FantasyWorld.Broadcast.Speech.Chapters.SaveAsync(_store, "2026-10-27", timeline);
        var plan = DioramaFilm.Chunks(timeline);
        Assert.Equal(3, plan.Count);
        // One stretch missing: the camera pans over the map instead.
        await _store.PutAsync(DioramaFilm.ChunkKey("2026-10-27", 0), [7], "video/mp4");
        await _store.PutAsync(DioramaFilm.ChunkKey("2026-10-27", 1), [8], "video/mp4");
        var video = new FakeVideo();
        await Channel(new FakeGoogle(Session, Created), video: video).PublishAsync(Sample(), [1], [2]);
        Assert.Equal(1, video.Made);
        Assert.Contains("not filmed in full", _log.ToString());

        // Every stretch there: the film is joined from them, with the narration.
        await _store.DeleteAsync(YouTube.StateKey("2026-10-27"));
        await _store.PutAsync(DioramaFilm.ChunkKey("2026-10-27", 2), [9], "video/mp4");
        var joined = new FakeVideo();
        await Channel(new FakeGoogle(Session, Created), video: joined).PublishAsync(Sample(), [1], [2]);
        Assert.Equal(0, joined.Made);
        Assert.Equal(new byte[] { 7, 8, 9, 2 }, await _store.GetAsync(YouTube.VideoKey("2026-10-27")));
        Assert.Contains("joined from 3 filmed stretches", _log.ToString());
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
