using FantasyWorld.Sim.Events;
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
            RegionsAtLastReview = 1,
        };
        world.Realms[realm.Id] = realm;
        world.Regions[world.Settlements[capitalId].RegionId].OwnerRealmId = realm.Id;
        return realm;
    }

    /// <summary>Realm 1 declares war on realm 2 over the given region (default: region 2).</summary>
    public static War DeclareWar(World world, EventLog log, long target = 2)
    {
        var cb = log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Test War", AttackerId = 1, DefenderId = 2,
            Attackers = { 1 }, Defenders = { 2 }, CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id,
            DeclaredEventId = declared.Id, TargetRegionId = target, StartDay = world.Date.Day,
        };
        world.Wars[war.Id] = war;
        return war;
    }
}
