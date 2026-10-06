using System.Security.Cryptography;
using System.Text;

namespace FantasyWorld.Sim.Llm;

/// <summary>The four LLM jobs (spec §8.1); each can use its own model.</summary>
public enum ModelTier { Decide, Arbitrate, Invent, Narrate }

/// <summary>
/// One structured-output request. Key identifies the question independent of the prompt's wording
/// (e.g. <c>decide|char#12|courtship|day 4400</c>): answers are journaled under it, so replays never
/// re-query and prompts can be improved without invalidating old worlds. System is the stable, cacheable
/// world bible; Prompt is the per-call brief; Schema is a JSON Schema for the answer.
/// </summary>
public sealed record LlmRequest(ModelTier Tier, string Key, string System, string Prompt, string Schema, int MaxTokens = 2048)
{
    public string Hash => HashOf(Tier + "|" + Key);

    public static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant();
}

/// <summary>A provider's answer: the JSON text plus usage for the cost ledger.</summary>
public sealed record LlmResponse(
    string Json, string Provider, string Model,
    long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal CostUsd);

/// <summary>A language-model backend (spec §8.5). Implementations throw on any failure; the gateway falls back.</summary>
public interface ILlmProvider
{
    string Name { get; }

    Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default);
}

/// <summary>The provider could not answer: no credentials, network, rate limit, server error.</summary>
public class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The model declined to answer (stop reason <c>refusal</c>); Response carries the billed usage when known.</summary>
public sealed class LlmRefusedException(string message, LlmResponse? response = null) : LlmUnavailableException(message)
{
    public LlmResponse? Response { get; } = response;
}

/// <summary>Scripted answers for tests and offline demos: a function from request to JSON.</summary>
public sealed class FakeLlmProvider(Func<LlmRequest, string> respond) : ILlmProvider
{
    public List<LlmRequest> Requests { get; } = new();

    public string Name => "fake";

    public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
    {
        Requests.Add(request);
        var json = respond(request);
        return Task.FromResult(new LlmResponse(json, Name, model, request.Prompt.Length / 4, json.Length / 4, 0, 0, 0m));
    }
}
