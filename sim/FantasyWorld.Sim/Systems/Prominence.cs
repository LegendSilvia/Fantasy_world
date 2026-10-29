using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Who the chronicle follows (the owner, 2026-10-29: "something to focus on the important characters"): a score from what
/// the world itself makes of a character, 0–100. A ruler and the heir to a seat, an awakened one, the renowned, a sworn
/// avenger and one with a companion at their side rank above the crowd; at <see cref="Notable"/> or more, a character is
/// one of the story's own.
/// </summary>
public static class Prominence
{
    public const int Notable = 40;

    public static int Of(World world, Character c)
    {
        var score = Math.Min(40, (int)(c.Renown / 2));
        if (world.Realms.Values.Any(r => !r.Destroyed && Realms.RulerOf(world, r)?.Id == c.Id)) score += 40;
        else if (c.Role == Role.Lord) score += 15;
        if (Fates.IsHeirApparent(world, c, world.Date)) score += 20;
        if (Classes.IsAwakened(c.Class)) score += 25;
        if (c.Ambition == Ambitions.Vengeance) score += 10;
        if (c.Companion is not null) score += 5;
        return Math.Min(100, score);
    }

    public static bool IsNotable(World world, Character c) => Of(world, c) >= Notable;
}
