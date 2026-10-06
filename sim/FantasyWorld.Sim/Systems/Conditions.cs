using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Condition names shared by systems through Region.Conditions / Settlement.Conditions. Each maps
/// to the event that began it, so later events can cite it as a cause.
/// </summary>
public static class Conditions
{
    public const string Shortage = "shortage";
    public const string Drought = "drought";
    public const string Blight = "blight";
    public const string Flood = "flood";
    public const string Plague = "plague";
    public const string Immune = "immune";
    public const string Exhausted = "exhausted";
    public const string LeySurge = "leySurge";
    public const string Dangerous = "dangerous";
    public const string Revolt = "revolt";

    /// <summary>Causes for the listed active conditions, in the order given (strongest first).</summary>
    public static IReadOnlyList<Cause> Cite(
        SortedDictionary<string, long> active, params (string Condition, CauseKind Kind, double Weight)[] wanted) =>
        wanted.Where(w => active.ContainsKey(w.Condition))
            .Select(w => new Cause(active[w.Condition], w.Kind, w.Weight))
            .ToList();
}
