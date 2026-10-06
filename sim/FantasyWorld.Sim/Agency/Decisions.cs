using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Agency;

/// <summary>One legal choice. Weight is the rules engine's prior (from traits, ambition, opinion).</summary>
public sealed record DecisionOption(string Key, double Weight, string Summary);

/// <summary>
/// A decision point (spec §8.2): a notable chooses among 2–5 legal options. The rules decider picks by
/// weight; Plan 5 adds an LLM decider behind the same interface, falling back to rules.
/// </summary>
public sealed record DecisionBrief(long CharacterId, string Situation, IReadOnlyList<DecisionOption> Options);

public interface IDecider
{
    DecisionOption Decide(DecisionBrief brief, Rng rng);
}

/// <summary>Seeded weighted choice over the options' weights.</summary>
public sealed class RulesDecider : IDecider
{
    public DecisionOption Decide(DecisionBrief brief, Rng rng)
    {
        var options = brief.Options.Where(o => o.Weight > 0).ToList();
        if (options.Count == 0) throw new ArgumentException($"Decision '{brief.Situation}' has no option with positive weight.", nameof(brief));
        var roll = rng.NextDouble() * options.Sum(o => o.Weight);
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll < 0) return option;
        }
        return options[^1];
    }
}
