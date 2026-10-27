using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Agency;

/// <summary>One legal choice. Weight is the rules engine's prior (from traits, ambition, opinion).</summary>
public sealed record DecisionOption(string Key, double Weight, string Summary)
{
    /// <summary>Why it was chosen, when an LLM chose it (spec §8.2): stored as the event's motive.</summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// A decision point (spec §8.2): a notable chooses among 2–5 legal options. The rules decider picks by
/// weight; Plan 5 adds an LLM decider behind the same interface, falling back to rules. A brief may name the option the
/// rules would take outright (<see cref="Preferred"/>, e.g. a commander's next move): the rules then take it without a
/// roll, so offering a choice changes nothing until an LLM answers.
/// </summary>
public sealed record DecisionBrief(long CharacterId, string Situation, IReadOnlyList<DecisionOption> Options, string? Preferred = null);

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
        if (brief.Preferred is { } preferred && options.FirstOrDefault(o => o.Key == preferred) is { } outright) return outright;
        var roll = rng.NextDouble() * options.Sum(o => o.Weight);
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll < 0) return option;
        }
        return options[^1];
    }
}
