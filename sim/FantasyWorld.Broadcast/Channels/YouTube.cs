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

    /// <summary>The privacy the video is uploaded with.</summary>
    public string Privacy => settings.Privacy;

    public static string VideoKey(string date) => $"episodes/{date}/episode.mp4";

    public static string StateKey(string date) => $"episodes/{date}/youtube.json";

    public void Attach(IObjectStore store, TextWriter log, string today) => (_store, _log, _today, _uploads) = (store, log, today, 0);

    public static JsonObject Details(Episode episode, BroadcastYouTube settings, Speech.Timeline? timeline = null)
    {
        var title = episode.Headline is { } headline ? $"{episode.WorldName}, Year {episode.ToYear}: {headline.Text}" : $"{episode.WorldName}, Year {episode.ToYear}";
        // YouTube refuses angle brackets in titles and descriptions.
        static string Plain(string text) => text.Replace('<', '‹').Replace('>', '›');
        // The chapter list leads, so it survives the description's limit; YouTube turns it into the video's chapters.
        var chapters = timeline?.ForYouTube() ?? [];
        var description = (chapters.Count > 0 ? Speech.Chapters.Lines(chapters) + "\n\n" : "")
                          + $"{episode.Script}\n\nTold by {episode.Chronicler}. A new episode every morning.";
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
                // The camera follows the sections when the run kept a shot list and the narration its chapters.
                var shots = Shots.Timed(await Shots.LoadAsync(store, day, ct).ConfigureAwait(false),
                    await Speech.Chapters.LoadAsync(store, day, ct).ConfigureAwait(false));
                mp4 = await video.MakeAsync(map, audio, shots, ct).ConfigureAwait(false);
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
            var timeline = await Speech.Chapters.LoadAsync(store, day, ct).ConfigureAwait(false);
            using var start = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = JsonContent.Create(Details(episode, settings, timeline)) };
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

    /// <summary>The day's video link, once it is up and anyone may watch it (public or unlisted); null otherwise.</summary>
    public static async Task<string?> WatchUrlAsync(IObjectStore store, string day, CancellationToken ct = default) =>
        await LoadAsync(store, day, ct).ConfigureAwait(false) is { State: UploadState.Done, VideoId: { Length: > 0 } id, Privacy: "public" or "unlisted" }
            ? $"https://youtu.be/{id}"
            : null;

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
