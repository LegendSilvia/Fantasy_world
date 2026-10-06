using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Two neighbouring kingdoms on TestPeople's two-region world, each ruled by its capital's lord.</summary>
public static class TestRealms
{
    public static World TwoKingdoms(ulong seed = 1)
    {
        var world = TestPeople.World(seed);
        foreach (var settlement in world.Settlements.Values)
        {
            var lord = TestPeople.Add(world, settlement.Id, 40, role: Role.Lord);
            lord.Attributes.Martial = 12;
            AddRealm(world, settlement.Id, $"Realm of {settlement.Name}");
        }
        return world;
    }

    public static Realm AddRealm(World world, long capitalId, string name, long liegeId = 0)
    {
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = name,
            CapitalId = capitalId,
            LiegeId = liegeId,
            Legitimacy = 70,
            Stability = 70,
            Treasury = 500,
            CultureId = 1,
            FaithId = 1,
        };
        world.Realms[realm.Id] = realm;
        world.Regions[world.Settlements[capitalId].RegionId].OwnerRealmId = realm.Id;
        return realm;
    }
}
