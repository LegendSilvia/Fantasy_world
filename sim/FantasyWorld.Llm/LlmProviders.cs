using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Llm;

/// <summary>Builds the provider a world's settings name, or null (offline) when its credentials are not set.</summary>
public static class LlmProviders
{
    /// <param name="timeout">How long one Claude request may take; the game's default (a minute) suits story beats, and
    /// the daily broadcast allows more for a whole script with thinking.</param>
    public static ILlmProvider? Create(LlmSettings settings, Func<string, string?>? env = null, TimeSpan? timeout = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        return settings.Provider switch
        {
            "anthropic" when env("ANTHROPIC_API_KEY") is { Length: > 0 } key => new AnthropicProvider(timeout is { } t
                ? new Anthropic.AnthropicClient { ApiKey = key, Timeout = t, MaxRetries = 1 }
                : null),
            "openrouter" when env("OPENROUTER_API_KEY") is { Length: > 0 } key => new OpenRouterProvider(key),
            _ => null,
        };
    }
}
