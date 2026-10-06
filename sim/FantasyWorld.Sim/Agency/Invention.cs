namespace FantasyWorld.Sim.Agency;

/// <summary>
/// Something new needs a name (spec §8.1 "Invent"): a forged artifact, a war, an era. Facts describe it; Fallback
/// is the engine's own name, used offline. Subject identifies it (e.g. <c>artifact#12</c>).
/// </summary>
public sealed record InventionBrief(string Kind, string Subject, string Facts, string Fallback);

/// <summary>A name, and lore (a few sentences of legend) when an LLM invented it.</summary>
public sealed record Invention(string Name, string? Lore);

public interface IInventor
{
    Invention Invent(InventionBrief brief);
}

/// <summary>The engine's own names, no lore: the offline path and the test baseline.</summary>
public sealed class RulesInventor : IInventor
{
    public Invention Invent(InventionBrief brief) => new(brief.Fallback, null);
}
