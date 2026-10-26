using System.Text;
using System.Text.Json.Nodes;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast.Channels;

/// <summary>Somewhere an episode is published. Throws <see cref="ChannelException"/> when it could not be.</summary>
public interface IEpisodeChannel
{
    string Name { get; }

    Task PublishAsync(Episode episode, CancellationToken ct = default);
}

public sealed class ChannelException(string message) : Exception(message);

/// <summary>
/// Posts the episode to a Discord channel through a webhook: one embed with the script. The webhook URL is a secret
/// (it carries its token), so errors name the status code only.
/// </summary>
public sealed class DiscordWebhook(HttpClient http, string webhookUrl, string username) : IEpisodeChannel
{
    /// <summary>Discord's limits on an embed.</summary>
    public const int MaxTitle = 256, MaxDescription = 4096;

    public string Name => "discord";

    public static JsonObject Payload(Episode episode, string username)
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

    public async Task PublishAsync(Episode episode, CancellationToken ct = default)
    {
        var url = webhookUrl + (webhookUrl.Contains('?') ? "&" : "?") + "wait=true";
        using var content = new StringContent(Payload(episode, username).ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
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
