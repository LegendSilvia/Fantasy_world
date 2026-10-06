using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class EntityTests
{
    [Fact]
    public void TestWorld_SatisfiesInvariants() =>
        Assert.Empty(Invariants.Check(TestWorlds.TwoRegions(), new EventLog()));

    [Fact]
    public void Settlement_PopulationAndConsumption()
    {
        var s = new Settlement { Children = 100, Adults = 200, Elders = 50 };
        Assert.Equal(350, s.Population);
        Assert.Equal(100 * 0.6 + 200 + 50 * 0.8, Demography.Consumption(s));
    }

    [Theory]
    [InlineData(100, SettlementTier.Hamlet)]
    [InlineData(300, SettlementTier.Village)]
    [InlineData(1500, SettlementTier.Town)]
    [InlineData(9000, SettlementTier.City)]
    public void TierFor_UsesPopulationThresholds(int population, SettlementTier expected) =>
        Assert.Equal(expected, Demography.TierFor(population));

    [Fact]
    public void LivingSettlements_SkipsAbandoned()
    {
        var world = TestWorlds.TwoRegions();
        var extra = TestWorlds.AddSettlement(world, 1, SettlementTier.Hamlet, 50);
        extra.Abandoned = true;
        Assert.Equal(new long[] { 1 }, world.LivingSettlements(world.Regions[1]).Select(s => s.Id));
    }

    [Fact]
    public void Region_JsonRoundTrip_KeepsOrdinalDictionaries()
    {
        var region = TestWorlds.TwoRegions().Regions[1];
        region.Conditions["drought"] = 4;
        region.Resources["ore"] = new ResourceDeposit { Capacity = 5, Yield = 3 };
        var copy = JsonSerializer.Deserialize<Region>(JsonSerializer.Serialize(region))!;
        Assert.Equal(4, copy.Conditions["drought"]);
        Assert.Equal(3, copy.Resources["ore"].Yield);
        Assert.Same(StringComparer.Ordinal, copy.Conditions.Comparer);
        Assert.Same(StringComparer.Ordinal, copy.Resources.Comparer);
    }

    [Fact]
    public void Invariants_CatchBrokenEntities()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Adults = -5;
        world.Regions[2].Fertility = 140;
        world.Regions[2].Settlements.Add(99);
        world.Regions[1].Conditions["drought"] = 7;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("negative population"));
        Assert.Contains(errors, e => e.Contains("fertility 140"));
        Assert.Contains(errors, e => e.Contains("settlement #99"));
        Assert.Contains(errors, e => e.Contains("missing event #7"));
    }

    [Fact]
    public void EntityRefs_UseKindNames()
    {
        var world = TestWorlds.TwoRegions();
        Assert.Equal(new EntityRef("region", 2), world.Regions[2].Ref);
        Assert.Equal(new EntityRef("settlement", 1), world.Settlements[1].Ref);
    }
}
