namespace FantasyWorld.Sim.Model;

/// <summary>
/// A nation's own festival (sub-project 13 N6, the owner: a unique festival for every nation, Claude deciding): its name,
/// the month it falls in, what is done, and what it honours. Held every year.
/// </summary>
public sealed record Festival(string Name, int Month, string Rite, string Honours, string? Lore = null);
