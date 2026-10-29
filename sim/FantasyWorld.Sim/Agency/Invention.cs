namespace FantasyWorld.Sim.Agency;

/// <summary>
/// Something new needs a name (spec §8.1 "Invent"): a forged artifact, a war, an era. Facts describe it; Fallback
/// is the engine's own name, used offline. Subject identifies it (e.g. <c>artifact#12</c>).
/// </summary>
public sealed record InventionBrief(string Kind, string Subject, string Facts, string Fallback);

/// <summary>A name, and lore (a few sentences of legend) when an LLM invented it.</summary>
public sealed record Invention(string Name, string? Lore);

/// <summary>A calling awakened (the owner, 2026-10-29): who awakens, from what calling, and what befell them.</summary>
public sealed record CallingBrief(string Subject, string Facts, string Base, IReadOnlyList<string> Known);

/// <summary>An awakened calling's name and lore: where its power comes from, what it does, and its condition.</summary>
public sealed record CallingInvention(string Name, string Origin, string Does, string Condition);

public interface IInventor
{
    Invention Invent(InventionBrief brief);

    /// <summary>An awakened calling, or null for the engine's own.</summary>
    CallingInvention? InventCalling(CallingBrief brief) => null;
}

/// <summary>The engine's own names, no lore: the offline path and the test baseline.</summary>
public sealed class RulesInventor : IInventor
{
    public Invention Invent(InventionBrief brief) => new(brief.Fallback, null);
}
