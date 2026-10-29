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
            // A wider pool (the owner, 2026-10-29): more of what a character is shows in how they fight.
            ("inspiring", "rallies all who fight beside them to fight harder", a.Diplomacy - 13),
            ("methodical", "fights by plan, each move set up by the last", a.Stewardship - 13),
            ("devout", "fights as if their god guides the blade", c.Has(Traits.Devout) ? 3 : -9),
            ("cold-eyed", "fights without feeling, as a craftsman works", c.Has(Traits.Cynical) ? 2 : -9),
            ("relentless", "never lets a fleeing foe go", c.Has(Traits.Ambitious) ? 2 : -9),
            ("wary", "never turns their back, and sees every trap coming", c.Has(Traits.Paranoid) ? 3 : -9),
            ("merciful", "spares a beaten foe, and is feared the less for it", c.Has(Traits.Kind) ? 2 : -9),
            ("honourable", "fights fair, and gives the foe an open challenge", c.Has(Traits.Honest) ? 2 : -9),
            ("proud", "fights hardest when their name is at stake", c.Has(Traits.Renowned) ? 2 : -9),
            ("vengeful", "strikes hardest when the fight is personal", c.Has(Traits.Bereaved) || c.Has(Traits.Humiliated) ? 3 : -9),
            ("one-handed", "fights on maimed, and has made the loss a weapon", c.Has(Traits.Maimed) ? 2 : -9),
            ("grasping", "fights with an eye on the spoils", c.Has(Traits.Greedy) ? 1 : -9),
            ("unhurried", "fights only as hard as they must, and wastes nothing", c.Has(Traits.Lazy) ? 1 : -9),
            ("reckless", "throws themselves at the foe without a thought for their guard", c.Has(Traits.Reckless) ? 3 : -9),
            ("prudent", "never overreaches, and never leaves an opening", c.Has(Traits.Prudent) ? 2 : -9),
            ("scornful", "thinks little of any foe, and is sometimes punished for it", c.Has(Traits.Arrogant) ? 2 : -9),
            ("jealous", "fights hardest against those whose fame outshines theirs", c.Has(Traits.Envious) ? 2 : -9),
            ("unyielding", "will not stop, will not yield, will not stay down", c.Has(Traits.Unyielding) ? 4 : -9),
        };
        return all.Where(t => t.Weight > 0).OrderByDescending(t => t.Weight).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Take(MostShown).Select(t => (t.Name, t.Shows)).ToList();
    }

    public static string Brief(Character c) => Of(c) is { Count: > 0 } t ? string.Join("; ", t.Select(x => $"{x.Name} ({x.Shows})")) : "no gift above the common";
}
