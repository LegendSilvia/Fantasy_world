using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// What the chronicle's audience may know of beasts (the owner, 2026-10-29): nothing, until someone faces one and lives
/// to tell (<see cref="Monster.Revealed"/>). An unseen beast has no marker, no card and no name; its rising, waking,
/// sleeping and wandering are not told at all, and what it does to people is told as the work of "something".
/// </summary>
public static class Bestiary
{
    /// <summary>The beast's own doings, told only once it has been seen.</summary>
    public static readonly IReadOnlySet<string> Its = new HashSet<string>(StringComparer.Ordinal)
    {
        "MonsterRose", "MonsterLegendary", "MonsterSlumbered", "MonsterWoke", "MonstersMigrated", "LabyrinthOutbreak",
    };

    public static bool Unseen(World world, EntityRef r) => r.Kind == Monster.Kind && world.Monsters.TryGetValue(r.Id, out var m) && !m.Revealed;

    /// <summary>Whether the audience is kept from this event: an unseen beast's own doing.</summary>
    public static bool Hidden(World world, WorldEvent ev) => Its.Contains(ev.Type) && ev.Actors.Values.Any(r => Unseen(world, r));
}
