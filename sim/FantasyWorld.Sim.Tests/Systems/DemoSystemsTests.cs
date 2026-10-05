using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Systems.Demo;

namespace FantasyWorld.Sim.Tests.Systems;

public class DemoSystemsTests
{
    private static Simulator Make(double grain, double unrest, int minDelta, int maxDeltaExclusive)
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        world.Stats["grain"] = grain;
        world.Stats["unrest"] = unrest;
        return new Simulator(world, new EventLog(), new EventQueue(),
            new ISimSystem[] { new HarvestSystem(minDelta, maxDeltaExclusive), new UnrestSystem() },
            checkInvariants: true);
    }

    [Fact]
    public void Shortage_BeginsWhenGrainFallsBelow40()
    {
        var sim = Make(grain: 45, unrest: 0, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(30);

        Assert.Equal(35, sim.World.Stats["grain"]);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageBegan");
        Assert.Equal("35", shortage.Payload["grain"]);
        Assert.Equal(shortage.Id, sim.World.Markers[HarvestSystem.ShortageMarker]);
    }

    [Fact]
    public void Shortage_EndsWhenGrainRecovers_CitingTheShortage()
    {
        var sim = Make(grain: 40, unrest: 0, minDelta: -1, maxDeltaExclusive: 0);
        sim.AdvanceDays(30);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageBegan");

        sim.World.Stats["grain"] = 100;
        sim.AdvanceDays(30);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageEnded");
        Assert.Equal(shortage.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(sim.World.Markers.ContainsKey(HarvestSystem.ShortageMarker));
    }

    [Fact]
    public void Revolt_IsCausedByTheShortage()
    {
        var sim = Make(grain: 45, unrest: 45, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(30);

        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        var root = Assert.Single(new HistoryQuery(sim.Log).RootCauses(revolt.Id));
        Assert.Equal("GrainShortageBegan", root.Type);
    }

    [Fact]
    public void Revolt_EndsNinetyDaysLater()
    {
        var sim = Make(grain: 45, unrest: 45, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(120);

        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        var ended = Assert.Single(sim.Log.All, e => e.Type == "RevoltEnded");
        Assert.Equal(revolt.Date.Day + 90, ended.Date.Day);
        Assert.Equal(revolt.Id, Assert.Single(ended.Causes).EventId);
        Assert.Equal(20, sim.World.Stats["unrest"]);
        Assert.False(sim.World.Markers.ContainsKey(UnrestSystem.RevoltMarker));
    }

    [Fact]
    public void DefaultSystems_RunACenturyWithoutViolatingInvariants()
    {
        var sim = new Simulator(WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(36_000);
        Assert.Equal(101, sim.World.Date.Year);
        Assert.InRange(sim.World.Stats["grain"], 0, 200);
    }
}
