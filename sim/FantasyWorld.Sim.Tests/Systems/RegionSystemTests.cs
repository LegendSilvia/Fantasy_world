using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class RegionSystemTests
{
    private const double Always = 12;

    private static Simulator Make(World world, RegionOdds odds, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new RegionSystem(odds) }, checkInvariants: true);

    [Fact]
    public void QuietOdds_ProduceNoRandomEvents()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None);
        sim.AdvanceDays(3600);
        Assert.Empty(sim.Log.All);
    }

    [Fact]
    public void Drought_StrikesInSummer_AndEndsInAutumn()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Drought = Always });
        sim.AdvanceDays(360);

        var drought = sim.Log.All.First(e => e.Type == "Drought");
        Assert.Equal(WorldDate.FromYmd(1, 4, 1), drought.Date);
        var ended = sim.Log.All.First(e => e.Type == "DroughtEnded" && e.Causes[0].EventId == drought.Id);
        Assert.Equal(WorldDate.FromYmd(1, 10, 1), ended.Date);
        Assert.False(sim.World.Regions[drought.Place!.Value.Id].Conditions.ContainsKey(Conditions.Drought));
    }

    [Fact]
    public void Plague_SpreadsAlongLinks_CitingTheOutbreak()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var outbreak = log.Append(world.Date, new EventDraft("PlagueOutbreak"));
        world.Regions[2].Conditions[Conditions.Plague] = outbreak.Id;
        var sim = Make(world, RegionOdds.None with { PlagueSpreadLand = 1 }, log);

        sim.AdvanceDays(30);

        var spread = Assert.Single(sim.Log.All, e => e.Type == "PlagueSpread");
        Assert.Equal(world.Regions[1].Ref, spread.Place);
        Assert.Equal(world.Regions[2].Ref, spread.Actors["from"]);
        Assert.Equal(outbreak.Id, Assert.Single(spread.Causes).EventId);
    }

    [Fact]
    public void RegionsThatSurvivedPlague_AreImmuneForAWhile()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Plague = Always, PlagueSpreadLand = 1 });
        sim.AdvanceDays(360 * 5);

        var outbreaks = sim.Log.All.Where(e => e.Type is "PlagueOutbreak" or "PlagueSpread").ToList();
        Assert.Equal(2, outbreaks.Count);
        var ended = sim.Log.All.First(e => e.Type == "PlagueEnded");
        Assert.Equal(ended.Id, sim.World.Regions[ended.Place!.Value.Id].Conditions[Conditions.Immune]);
    }

    [Fact]
    public void Plague_KillsEachMonth()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        world.Regions[1].Conditions[Conditions.Plague] = log.Append(world.Date, new EventDraft("PlagueOutbreak")).Id;
        var before = world.Settlements[1].Population;
        Make(world, RegionOdds.None, log).AdvanceDays(60);
        Assert.InRange(world.Settlements[1].Population, before * 0.94, before * 0.985);
    }

    [Fact]
    public void Monsters_LeaveADangerousRegion_ForItsNeighbour()
    {
        var world = TestWorlds.TwoRegions();
        world.Regions[1].Danger = 90;
        var sim = Make(world, RegionOdds.None with { Monsters = Always });

        sim.AdvanceDays(30);

        var rose = Assert.Single(sim.Log.All, e => e.Type == "DangerRose");
        var migrated = Assert.Single(sim.Log.All, e => e.Type == "MonstersMigrated");
        Assert.Equal(world.Regions[2].Ref, migrated.Place);
        Assert.Equal(rose.Id, Assert.Single(migrated.Causes).EventId);
        Assert.True(world.Regions[2].Danger > 30);
    }

    [Fact]
    public void Earthquake_IsTriggeredByALeySurge()
    {
        var world = TestWorlds.TwoRegions(westMountainShare: 0.5);
        var sim = Make(world, RegionOdds.None with { LeySurge = Always, Earthquake = Always });

        sim.AdvanceDays(30);

        var surge = sim.Log.All.First(e => e.Type == "LeySurge");
        var quake = sim.Log.All.First(e => e.Type == "Earthquake");
        Assert.Equal(surge.Id, Assert.Single(quake.Causes).EventId);
    }

    [Fact]
    public void Overfarming_ExhaustsTheSoil()
    {
        var world = TestWorlds.TwoRegions();
        var region = world.Regions[1];
        region.Fertility = 42.1;
        world.Settlements[1].Adults = 50_000;
        var sim = Make(world, RegionOdds.None);

        sim.AdvanceDays(30);

        var exhausted = Assert.Single(sim.Log.All, e => e.Type == "FertilityExhausted");
        Assert.Equal(exhausted.Id, region.Conditions[Conditions.Exhausted]);
    }

    [Fact]
    public void ImportantRegionEvents_AreRemembered()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Plague = Always });
        sim.AdvanceDays(60);
        var outbreak = sim.Log.All.First(e => e.Type == "PlagueOutbreak" && e.Place == sim.World.Regions[1].Ref);
        Assert.Contains(outbreak.Id, sim.World.Regions[1].Memory);
    }

    [Fact]
    public void Discovery_AddsAResource()
    {
        var world = TestWorlds.TwoRegions();
        var sim = Make(world, RegionOdds.None with { Discovery = Always });
        sim.AdvanceDays(30);
        var found = sim.Log.All.Where(e => e.Type == "ResourceDiscovered" && e.Place == world.Regions[1].Ref).ToList();
        Assert.Single(found);
        Assert.Contains(found[0].Payload["resource"], world.Regions[1].Resources.Keys);
    }
}
