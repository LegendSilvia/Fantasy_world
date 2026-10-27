namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Per-world LLM settings (spec §8.4–8.5), stored in the world file's meta table. Provider "none" keeps the
/// world fully offline. Story beats (decide, arbitrate, invent) and narrations have separate rolling 24-hour
/// budgets; of the story beats, <see cref="FightBeatsReserved"/> are kept for fights (the arbitrate tier), so the many
/// small choices can never starve them (sub-project 8 §3.2). The monthly spend cap switches everything to the offline
/// path when reached.
/// </summary>
public sealed record LlmSettings
{
    public const string DefaultModel = "claude-opus-5-5";

    public string Provider { get; init; } = "none";
    public SortedDictionary<string, string> Models { get; init; } = new(StringComparer.Ordinal);
    public int StoryBeatsPerDay { get; init; } = 60;

    /// <summary>Story beats only fights may use: choices and inventions stop at the rest.</summary>
    public int FightBeatsReserved { get; init; }
    public int NarrationsPerDay { get; init; } = 200;
    public decimal MonthlySpendCapUsd { get; init; } = 10m;

    public bool Enabled => Provider != "none";

    public string ModelFor(ModelTier tier) =>
        Models.TryGetValue(tier.ToString().ToLowerInvariant(), out var model) ? model : DefaultModel;
}
