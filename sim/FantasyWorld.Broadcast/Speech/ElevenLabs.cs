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

    /// <summary>The audio with when each character was said (for the subtitles); a service that cannot time it gives none.</summary>
    async Task<SpokenPart> SpeakTimedAsync(SpeechRequest request, CancellationToken ct = default) =>
        new(await SpeakAsync(request, ct).ConfigureAwait(false), null);

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

    private static JsonObject Body(SpeechRequest request)
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
        return body;
    }

    public async Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default)
    {
        var body = Body(request);
        var url = $"{Api}/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}?output_format={Uri.EscapeDataString(request.OutputFormat)}";
        using var response = await SendAsync(url, body, "speak", ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary><c>POST /v1/text-to-speech/{voice}/with-timestamps</c>: the same audio, with each character's start and end.</summary>
    public async Task<SpokenPart> SpeakTimedAsync(SpeechRequest request, CancellationToken ct = default)
    {
        var body = Body(request);
        var url = $"{Api}/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}/with-timestamps?output_format={Uri.EscapeDataString(request.OutputFormat)}";
        using var response = await SendAsync(url, body, "speak", ct).ConfigureAwait(false);
        JsonObject? answer;
        try
        {
            answer = await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new SpeechException("ElevenLabs answered with something other than timed speech.");
        }
        if (answer?["audio_base64"] is not JsonValue audio || !audio.TryGetValue<string>(out var base64) || base64.Length == 0)
            throw new SpeechException("ElevenLabs answered without audio.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new SpeechException("ElevenLabs answered with audio that could not be read.");
        }
        return new SpokenPart(bytes, Read(answer["alignment"]));
    }

    /// <summary>The alignment, if it is whole (as many times as characters); else none, and the part goes without subtitles.</summary>
    public static Alignment? Read(JsonNode? node)
    {
        try
        {
            if (node is not JsonObject a || a["characters"] is not JsonArray chars || a["character_start_times_seconds"] is not JsonArray starts
                || a["character_end_times_seconds"] is not JsonArray ends || chars.Count != starts.Count || chars.Count != ends.Count) return null;
            return new Alignment(chars.Select(c => c!.GetValue<string>()).ToList(), starts.Select(t => t!.GetValue<double>()).ToList(),
                ends.Select(t => t!.GetValue<double>()).ToList());
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or NullReferenceException)
        {
            return null;
        }
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
