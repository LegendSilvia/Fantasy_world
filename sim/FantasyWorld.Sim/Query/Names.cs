using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Query;

/// <summary>Display names for entities, shared by the CLI, briefs and narration.</summary>
public static class Names
{
    public static string Full(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;

    public static Func<EntityRef, string?> Of(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? Full(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        Realm.Kind => world.Realms.TryGetValue(entity.Id, out var realm) ? Statecraft.Title(realm) : null,
        War.Kind => world.Wars.TryGetValue(entity.Id, out var war) ? war.Name : null,
        Army.Kind => world.Armies.TryGetValue(entity.Id, out var army) ? army.Name : null,
        Structure.Kind => world.Structures.TryGetValue(entity.Id, out var structure) ? structure.Name : null,
        Culture.Kind => world.Cultures.TryGetValue(entity.Id, out var culture) ? culture.Name : null,
        Faith.Kind => world.Faiths.TryGetValue(entity.Id, out var faith) ? faith.Name : null,
        _ => null,
    };
}
