namespace FantasyWorld.Llm;

/// <summary>USD per million tokens, for the cost ledger. Unknown models are priced at the top tier so the spend cap errs safe.</summary>
public sealed record Price(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    private static readonly IReadOnlyDictionary<string, Price> Known = new Dictionary<string, Price>(StringComparer.Ordinal)
    {
        ["claude-opus-5-5"] = new(4m, 20m, 0.20m, 5m),
        ["claude-sonnet-5-5"] = new(2m, 10m, 0.20m, 2.5m),
        ["claude-haiku-4-5"] = new(1m, 5m, 0.10m, 1.25m),
        ["claude-fable-5-1"] = new(10m, 50m, 0.25m, 12.5m),
    };

    public static Price For(string model) =>
        Known.TryGetValue(model.StartsWith("anthropic/", StringComparison.Ordinal) ? model["anthropic/".Length..] : model, out var price)
            ? price
            : Known["claude-fable-5-1"];

    public decimal Cost(long input, long output, long cacheRead, long cacheWrite) =>
        (input * Input + output * Output + cacheRead * CacheRead + cacheWrite * CacheWrite) / 1_000_000m;
}
