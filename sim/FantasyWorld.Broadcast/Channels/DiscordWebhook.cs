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
/// as an attachment in the same request). When the day's video is already on YouTube (the run uploads before it posts)
/// and anyone may watch it, its link goes in the message, where Discord plays it, in place of the narration file. The
/// webhook URL is a secret (it carries its token), so errors name the status code only.
/// </summary>
public sealed class DiscordWebhook(HttpClient http, string webhookUrl, string username) : IEpisodeChannel, IStatefulChannel
{
    private Storage.IObjectStore? _store;

    public void Attach(Storage.IObjectStore store, TextWriter log, string today) => _store = store;

    /// <summary>Discord's limits on an embed.</summary>
    public const int MaxTitle = 256, MaxDescription = 4096;

    public string Name => "discord";

    public const string MapFile = "map.png", AudioFile = "narration.mp3";

    /// <summary>
    /// Discord's upload limit for a webhook (8 MiB, the smallest across server tiers), for everything sent together; when
    /// the narration would pass it, the narration is left off.
    /// </summary>
    public const int MaxAttachmentBytes = 8 * 1024 * 1024;

    public static JsonObject Payload(Episode episode, string username, bool withMap = false, string? videoUrl = null)
    {
        var payload = Embed(episode, username);
        // In the message itself, not the embed, so Discord shows the video's player.
        if (videoUrl is not null) payload["content"] = $"▶ Watch the episode: {videoUrl}";
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

    public static HttpContent Content(Episode episode, string username, byte[]? map, byte[]? audio = null, string? videoUrl = null)
    {
        // The video carries the narration, so the file is not sent twice.
        if (videoUrl is not null) audio = null;
        var json = Payload(episode, username, map is not null, videoUrl).ToJsonString();
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
        var video = _store is null ? null : await YouTube.WatchUrlAsync(_store, episode.Date, ct).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            using (var content = Content(episode, username, map, audio, video))
                response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
            // Too large for this server after all: the post goes out without the narration.
            if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge && audio is not null)
            {
                response.Dispose();
                using var smaller = Content(episode, username, map, null, video);
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
