using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Llm;

/// <summary>
/// Any model behind OpenRouter's chat-completions HTTP API (spec §8.5), with JSON-schema structured output.
/// The key comes from <c>OPENROUTER_API_KEY</c>. Cost is OpenRouter's reported cost when given, else the price table.
/// </summary>
public sealed class OpenRouterProvider(string apiKey, HttpClient? http = null) : ILlmProvider
{
    public const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";

    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

    public string Name => "openrouter";

    /// <summary>OpenRouter names models by vendor; a bare Claude model name means Anthropic's.</summary>
    public static string Route(string model) => model.Contains('/', StringComparison.Ordinal) ? model : "anthropic/" + model;

    public static JsonObject BuildBody(LlmRequest request, string model) => new()
    {
        ["model"] = Route(model),
        ["max_tokens"] = request.MaxTokens + AnthropicProvider.ThinkingHeadroom,
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.System },
            new JsonObject { ["role"] = "user", ["content"] = request.Prompt },
        },
        ["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = request.Tier.ToString().ToLowerInvariant(), ["strict"] = true, ["schema"] = JsonNode.Parse(request.Schema) },
        },
        ["usage"] = new JsonObject { ["include"] = true },
    };

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(BuildBody(request, model).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new LlmUnavailableException($"OpenRouter answered {(int)response.StatusCode}.");
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var json = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            long Tokens(string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var v) ? v.GetInt64() : 0;
            var (input, output) = (Tokens("prompt_tokens"), Tokens("completion_tokens"));
            var cost = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDecimal()
                : Price.For(model).Cost(input, output, 0, 0);
            var result = new LlmResponse(json, Name, Route(model), input, output, 0, 0, cost);
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.GetString() is "content_filter")
                throw new LlmRefusedException("The model declined this request.", result);
            return result;
        }
        catch (LlmUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new LlmUnavailableException($"OpenRouter is unavailable: {e.Message}", e);
        }
    }
}
