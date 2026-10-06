using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class EconomySystemTests
{
    private static Simulator Make(World world) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new EconomySystem() }, checkInvariants: true);

    [Fact]
    public void HealthyRegion_StaysFed_ThroughAYear()
    {
        var sim = Make(TestWorlds.TwoRegions());
        sim.AdvanceDays(360);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "FoodShortageBegan");
        Assert.True(sim.World.Regions[1].Food > 0);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(4, 1.0)]
    [InlineData(7, 2.0)]
    [InlineData(10, 0.0)]
    public void Production_FollowsTheSeasons(int month, double weight)
    {
        var world = TestWorlds.TwoRegions();
        world.Date = WorldDate.FromYmd(1, month, 1);
        var region = world.Regions[1];
        var settlements = world.LivingSettlements(region).ToList();
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        var expected = settlements.Sum(Demography.Farmers) * EconomySystem.FoodPerFarmerMonth * region.Fertility / 50 * weight;
        Assert.Equal(expected, EconomySystem.Production(ctx, region, settlements), 6);
    }

    [Fact]
    public void EmptyStores_BeginAShortage_CitingTheDrought()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var drought = log.Append(world.Date, new EventDraft("Drought"));
        var region = world.Regions[1];
        region.Food = 0;
        region.Conditions[Conditions.Drought] = drought.Id;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new EconomySystem() }, checkInvariants: true);

        sim.AdvanceDays(30);

        var shortage = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageBegan");
        Assert.Equal(region.Ref, shortage.Place);
        Assert.Equal(shortage.Id, region.Conditions[Conditions.Shortage]);
        Assert.Equal("Drought", Assert.Single(new HistoryQuery(sim.Log).RootCauses(shortage.Id)).Type);
    }

    [Fact]
    public void Shortage_EndsOnceStoresRecover()
    {
        var world = TestWorlds.TwoRegions();
        world.Regions[1].Food = 0;
        var sim = Make(world);
        sim.AdvanceDays(30);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageBegan");

        world.Regions[1].Food = 100_000;
        sim.AdvanceDays(30);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageEnded");
        Assert.Equal(shortage.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(world.Regions[1].Conditions.ContainsKey(Conditions.Shortage));
    }

    [Fact]
    public void Starvation_KillsWhenNothingIsProduced()
    {
        var world = TestWorlds.TwoRegions();
        var region = world.Regions[1];
        region.Food = 0;
        region.Fertility = 0;
        var before = world.Settlements[1].Population;
        Make(world).AdvanceDays(90);
        Assert.InRange(world.Settlements[1].Population, before * 0.8, before * 0.9);
    }

    [Fact]
    public void FarmersBeyondFarmland_ProduceNothingExtra()
    {
        var world = TestWorlds.TwoRegions();
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        world.Date = WorldDate.FromYmd(1, 7, 1);
        var region = world.Regions[1];
        var settlements = world.LivingSettlements(region).ToList();
        var normal = EconomySystem.Production(ctx, region, settlements);
        settlements[0].Adults *= 10;
        var crowded = EconomySystem.Production(ctx, region, settlements);
        Assert.Equal(Demography.FarmCapacity(region) * EconomySystem.FoodPerFarmerMonth * 60 / 50 * 2, crowded, 3);
        Assert.True(crowded < normal * 10);
    }

    [Fact]
    public void Population_GrowsSlowlyWhenFed()
    {
        var world = TestWorlds.TwoRegions();
        var before = world.Settlements[1].Population;
        Make(world).AdvanceDays(3600);
        Assert.InRange(world.Settlements[1].Population, before * 1.03, before * 1.25);
    }

    [Fact]
    public void EmptyRegion_StoresNothing()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Abandoned = true;
        world.Regions[1].Resources[ResourceKinds.Fish] = new ResourceDeposit { Capacity = 50, Yield = 50 };
        var before = world.Regions[1].Food;
        Make(world).AdvanceDays(360);
        Assert.Equal(before, world.Regions[1].Food);
    }

    [Fact]
    public void Ageing_NeverCreatesPeople()
    {
        var world = TestWorlds.TwoRegions();
        var s = world.Settlements[1];
        var sim = Make(world);
        for (var year = 0; year < 200; year++)
        {
            (s.Children, s.Adults, s.Elders) = (1, 0, 0);
            sim.AdvanceDays(360);
            Assert.InRange(s.Population, 0, 1);
        }
    }

    [Fact]
    public void Settlements_EarnWealth()
    {
        var world = TestWorlds.TwoRegions();
        var before = world.Settlements[1].Wealth;
        Make(world).AdvanceDays(360);
        Assert.True(world.Settlements[1].Wealth > before);
    }
}
