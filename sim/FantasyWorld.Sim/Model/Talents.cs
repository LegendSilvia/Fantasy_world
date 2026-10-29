namespace FantasyWorld.Sim.Model;

/// <summary>
/// How a character fights (the owner, 2026-10-29: "the attack was strong and devastating, it broke the shield"): talents
/// read from their strengths and their nature, each with how it shows in a blow, so a fight is told as theirs.
/// </summary>
public static class Talents
{
    public const int MostShown = 3;

    /// <summary>"strong (blows that break shields and bones)", most telling first.</summary>
    public static IReadOnlyList<(string Name, string Shows)> Of(Character c)
    {
        var a = c.Attributes;
        var all = new List<(string Name, string Shows, double Weight)>
        {
            ("strong", "blows that break shields and bones", a.Prowess - 12),
            ("precise", "strikes that find the gap in any guard", (a.Intrigue + a.Prowess) / 2.0 - 11),
            ("swift", "faster than the eye, never where the blow lands", a.Intrigue - 12),
            ("enduring", "keeps fighting long after others would fall", a.Martial - 12 + (c.Has(Traits.Diligent) ? 2 : 0)),
            ("canny", "reads the foe and turns their own strength against them", a.Martial + a.Lore / 2.0 - 15),
            ("arcane", "fights with spells as much as steel", a.Lore - 12),
            ("fearless", "steps into blows others would flee", c.Has(Traits.Brave) ? 4 : -9),
            ("furious", "a storm of rage that is hard to stand against, and careless", c.Has(Traits.Wrathful) ? 4 : -9),
            ("patient", "waits for the one opening and takes it", c.Has(Traits.Calm) ? 3 : -9),
            ("treacherous", "fights dirty: sand in the eyes, a hidden knife", c.Has(Traits.Deceitful) ? 3 : -9),
            ("ruthless", "shows no mercy to a fallen foe", c.Has(Traits.Cruel) ? 3 : -9),
            ("cautious", "gives ground rather than risk a wound", c.Has(Traits.Craven) ? 4 : -9),
            ("scarred", "fights around old wounds, and knows pain", c.Wounds.Count > 0 ? 1 : -9),
        };
        return all.Where(t => t.Weight > 0).OrderByDescending(t => t.Weight).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Take(MostShown).Select(t => (t.Name, t.Shows)).ToList();
    }

    public static string Brief(Character c) => Of(c) is { Count: > 0 } t ? string.Join("; ", t.Select(x => $"{x.Name} ({x.Shows})")) : "no gift above the common";
}
