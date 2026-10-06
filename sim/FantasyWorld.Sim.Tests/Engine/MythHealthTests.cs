using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Sub-project 3 §10: two centuries of a medium world with every myth system running, invariants checked each tick.</summary>
public class MythHealthTests
{
    [Fact]
    public void TwoCenturies_GrowMyths_AndHoldTheirInvariants()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(200 * 360);
        var seen = sim.Log.All.Select(e => e.Type).ToHashSet();

        foreach (var type in new[] { "MonsterRose", "MonsterRaid", "MonsterSlain", "RouteOpened", "RouteSevered", "RegionConverted", "SaintDeclared",
                     "LeySurge", "LandmarkCompleted", "LabyrinthOpened", "LabyrinthDelved", "WarDeclared" })
            Assert.Contains(type, seen);
        Assert.All(world.Monsters.Values.Where(m => m.Alive), m => Assert.True(world.Regions.ContainsKey(m.RegionId)));
        Assert.All(world.Routes.Values.Where(r => r.State != RouteState.Closed),
            r => Assert.True(TradeSystem.IsMarket(world.Settlements[r.FromId]) && TradeSystem.IsMarket(world.Settlements[r.ToId])));
        var holy = sim.Log.All.Where(e => e.Type == "HolyWarProclaimed").ToList();
        Assert.NotEmpty(holy);
        Assert.All(holy, h => Assert.NotEmpty(h.Causes));
        Assert.All(world.Regions.Values, r => Assert.True(r.FaithId == 0 || world.Faiths.ContainsKey(r.FaithId)));
        Assert.All(world.Structures.Values.Where(s => s.GuardianId != 0), s => Assert.InRange(s.Depth, 0, s.Levels));
    }
}
