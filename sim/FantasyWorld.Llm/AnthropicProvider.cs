using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Llm;

/// <summary>
/// Claude through the official Anthropic C# SDK (spec §8.5). Credentials are resolved by the SDK from the
/// <c>ANTHROPIC_API_KEY</c> environment variable. The world bible is the cached system prefix; answers use
/// structured outputs (JSON schema). Thinking is always on for Claude Opus 5.5, so effort sets the depth:
/// low for story beats, medium for narration.
/// </summary>
public sealed class AnthropicProvider(AnthropicClient? client = null) : ILlmProvider
{
    /// <summary>Thinking shares the output budget, so the answer's own budget gets generous headroom.</summary>
    public const int ThinkingHeadroom = 8000;

    private AnthropicClient? _client = client;

    public string Name => "anthropic";

    public static MessageCreateParams BuildParams(LlmRequest request, string model) => new()
    {
        Model = model,
        MaxTokens = request.MaxTokens + ThinkingHeadroom,
        System = new List<TextBlockParam> { new() { Text = request.System, CacheControl = new CacheControlEphemeral() } },
        OutputConfig = new OutputConfig
        {
            Effort = request.Tier == ModelTier.Narrate ? Effort.Medium : Effort.Low,
            Format = new JsonOutputFormat { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Schema)! },
        },
        Messages = [new() { Role = Role.User, Content = request.Prompt }],
    };

    /// <summary>A game must not stall on one story beat: one retry, a minute at most per attempt.</summary>
    public static AnthropicClient DefaultClient() => new() { Timeout = TimeSpan.FromSeconds(60), MaxRetries = 1 };

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
    {
        LlmResponse response;
        bool refused;
        try
        {
            _client ??= DefaultClient();
            var message = await _client.Messages.Create(BuildParams(request, model), ct);
            refused = message.StopReason == StopReason.Refusal;
            var json = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            var usage = message.Usage;
            var (cacheRead, cacheWrite) = (usage.CacheReadInputTokens ?? 0, usage.CacheCreationInputTokens ?? 0);
            response = new LlmResponse(json, Name, model, usage.InputTokens, usage.OutputTokens, cacheRead, cacheWrite,
                Price.For(model).Cost(usage.InputTokens, usage.OutputTokens, cacheRead, cacheWrite));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Network, auth, timeouts, rate limits, malformed responses: all mean "not now".
            throw new LlmUnavailableException($"Claude is unavailable: {e.Message}", e);
        }
        if (refused) throw new LlmRefusedException("Claude declined this request.", response);
        return response;
    }
}
