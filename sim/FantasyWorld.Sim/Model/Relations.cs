namespace FantasyWorld.Sim.Model;

/// <summary>Opinions and kinship. Opinions are derived on demand, never stored (spec §3).</summary>
public static class Relations
{
    public const int FriendAt = 40;
    public const int RivalAt = -40;

    /// <summary>
    /// What a thinks of b, −100…100: memories about b (feeling × weight / 100), trait compatibility,
    /// and kinship (+30 spouse, +20 parent, child or sibling).
    /// </summary>
    public static double Opinion(Character a, Character b)
    {
        var score = a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += Traits.Compatibility(a.Traits, b.Traits);
        if (a.SpouseId == b.Id) score += 30;
        else if (AreKin(a, b)) score += 20;
        // A master and apprentice still bound hold each other dear (the owner, 2026-10-30); once they fall out, they do not.
        else if (Bound(a, b)) score += 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Whether one is the other's master or apprentice, and they have not fallen out.</summary>
    public static bool Bound(Character a, Character b) =>
        a.MasterId == b.Id && !a.Estranged || b.MasterId == a.Id && !b.Estranged;

    public static bool AreKin(Character a, Character b) =>
        a.FatherId == b.Id || a.MotherId == b.Id || b.FatherId == a.Id || b.MotherId == a.Id || AreSiblings(a, b);

    public static bool AreSiblings(Character a, Character b) =>
        a.Id != b.Id && ((a.FatherId != 0 && a.FatherId == b.FatherId) || (a.MotherId != 0 && a.MotherId == b.MotherId));
}
