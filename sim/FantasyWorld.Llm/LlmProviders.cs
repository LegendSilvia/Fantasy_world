using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Llm;

/// <summary>Builds the provider a world's settings name, or null (offline) when its credentials are not set.</summary>
public static class LlmProviders
{
    public static ILlmProvider? Create(LlmSettings settings, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        return settings.Provider switch
        {
            "anthropic" when env("ANTHROPIC_API_KEY") is { Length: > 0 } => new AnthropicProvider(),
            "openrouter" when env("OPENROUTER_API_KEY") is { Length: > 0 } key => new OpenRouterProvider(key),
            _ => null,
        };
    }
}
