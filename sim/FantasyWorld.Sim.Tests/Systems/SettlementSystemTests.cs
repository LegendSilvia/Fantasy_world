using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SettlementSystemTests
{
    private static Simulator Make(World world, EventLog? log = null, double revoltChance = 1.0) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new SettlementSystem(revoltChance) },
            checkInvariants: true);

    private static (World World, EventLog Log, WorldEvent Shortage) Starving()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var shortage = log.Append(world.Date, new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.Shortage] = shortage.Id;
        return (world, log, shortage);
    }

    [Fact]
    public void Shortage_RaisesUnrest_IntoARevoltCitingIt()
    {
        var (world, log, shortage) = Starving();
        world.Settlements[1].Unrest = 50;
        var sim = Make(world, log);

        sim.AdvanceDays(180);

        var revolt = sim.Log.All.First(e => e.Type == "RevoltBegan");
        Assert.Equal(world.Settlements[1].Ref, revolt.Place);
        Assert.Equal(shortage.Id, Assert.Single(revolt.Causes).EventId);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RevoltBegan" && e.Place == world.Settlements[2].Ref);
    }

    [Fact]
    public void Revolt_EndsWithin150Days_CalmingTheSettlement()
    {
        var (world, log, _) = Starving();
        world.Settlements[1].Unrest = 70;
        var sim = Make(world, log);
        sim.AdvanceDays(30);
        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        world.Regions[1].Conditions.Remove(Conditions.Shortage);

        sim.AdvanceDays(150);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "RevoltEnded");
        Assert.InRange(ended.Date.Day - revolt.Date.Day, 60, 150);
        Assert.Equal(revolt.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(world.Settlements[1].Conditions.ContainsKey(Conditions.Revolt));
    }

    [Fact]
    public void CalmSettlements_DoNotRevolt()
    {
        var sim = Make(TestWorlds.TwoRegions());
        sim.AdvanceDays(3600);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RevoltBegan");
        Assert.All(sim.World.Settlements.Values, s => Assert.InRange(s.Unrest, 0, 30));
    }

    [Fact]
    public void Settlement_GrowsIntoATown()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Adults += 1000;
        Make(world).AdvanceDays(360);
        Assert.Equal(SettlementTier.Town, world.Settlements[1].Tier);
    }

    [Fact]
    public void Town_DeclinesBelowThreeQuartersOfItsThreshold()
    {
        var world = TestWorlds.TwoRegions();
        var sim = Make(world);
        world.Settlements[1].Tier = SettlementTier.Town;
        sim.AdvanceDays(360);
        var declined = Assert.Single(sim.Log.All, e => e.Type == "SettlementDeclined");
        Assert.Equal("Village", declined.Payload["tier"]);
    }

    [Fact]
    public void TinySettlement_IsAbandoned()
    {
        var world = TestWorlds.TwoRegions();
        var hamlet = TestWorlds.AddSettlement(world, 1, SettlementTier.Hamlet, 10);
        var sim = Make(world);
        sim.AdvanceDays(360);
        Assert.True(hamlet.Abandoned);
        Assert.Equal(0, hamlet.Population);
        Assert.Single(sim.Log.All, e => e.Type == "SettlementAbandoned" && e.Place == hamlet.Ref);
    }

    [Fact]
    public void HungryFamilies_MoveToACalmNeighbour()
    {
        var (world, log, shortage) = Starving();
        var big = world.Settlements[1];
        big.Adults = 3000;
        big.Children = 2000;
        var before = world.Settlements[2].Population;
        var sim = Make(world, log, revoltChance: 0);

        sim.AdvanceDays(360);

        var exodus = Assert.Single(sim.Log.All, e => e.Type == "Exodus");
        Assert.Equal(world.Settlements[2].Ref, exodus.Actors["to"]);
        Assert.Contains(exodus.Causes, c => c.EventId == shortage.Id);
        Assert.True(world.Settlements[2].Population > before + 100);
    }

    [Fact]
    public void Town_RebuildsWallsLostToAQuake()
    {
        var world = TestWorlds.TwoRegions();
        var town = world.Settlements[1];
        town.Tier = SettlementTier.Town;
        town.Adults += 1000;
        town.Walls = 0;
        town.Wealth = SettlementSystem.WallCost * 2 + 100;
        var sim = Make(world);
        sim.AdvanceDays(60);
        Assert.Equal(2, town.Walls);
        Assert.Equal(2, sim.Log.All.Count(e => e.Type == "WallsRaised"));
    }

    [Fact]
    public void WealthySettlement_Builds()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Wealth = 1000;
        var sim = Make(world);
        sim.AdvanceDays(30);
        var built = Assert.Single(sim.Log.All, e => e.Type == "BuildingCompleted");
        Assert.Equal("granary", built.Payload["building"]);
        Assert.Contains("granary", world.Settlements[1].Buildings);
        Assert.Equal(700, world.Settlements[1].Wealth);
    }
}
