using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks of realms and war: wars happen and end, every war has a casus belli, the map neither freezes nor collapses.</summary>
public class RealmHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size, bool checkInvariants) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    [Fact]
    public void ACenturyAndAHalf_OfWarAndPeace()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        var world = sim.World;
        sim.AdvanceDays(150 * 360);

        Assert.InRange(world.Wars.Count, 5, 120);
        Assert.Contains(world.Wars.Values, w => w.Ended);
        Assert.All(world.Wars.Values, w => Assert.True(sim.Log.TryGet(w.CasusBelliEventId, out _), $"{w.Name} has no casus belli"));
        Assert.All(world.Wars.Values.Where(w => w.Ended), w => Assert.Equal("PeaceSigned", sim.Log.Get(w.EndedEventId).Type));
        Assert.InRange(Realms.Independent(world).Count(), 2, 20);
        Assert.All(world.Regions.Values, r => Assert.NotNull(Realms.SovereignOf(world, r)));
        Assert.Contains(sim.Log.All, e => e.Type == "BattleFought");
        Assert.Contains(sim.Log.All, e => e.Type == "RegionCeded");
        Assert.Contains(sim.Log.All, e => e.Type == "EraBegan");
        Assert.Empty(Invariants.Check(world, sim.Log));
    }

    [Fact]
    public void Wars_TraceBackThroughTheirCasusBelli()
    {
        var sim = NewSim(7, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(100 * 360);
        var query = new HistoryQuery(sim.Log);
        var declared = sim.Log.All.Where(e => e.Type == "WarDeclared").ToList();
        Assert.NotEmpty(declared);
        Assert.All(declared, d => Assert.NotEmpty(query.WhyChain(d.Id).Causes));
    }

    [Fact]
    public void RealmsAndWars_AreChunkInvariant_AndSurviveSaveLoad()
    {
        using var file = new TempFile();
        var oneShot = NewSim(5, WorldSize.Tiny, checkInvariants: true);
        oneShot.AdvanceDays(80 * 360);

        var chunked = NewSim(5, WorldSize.Tiny, checkInvariants: true);
        chunked.AdvanceDays(40 * 360 + 17);
        using (var store = WorldStore.Open(file.Path)) store.Save(chunked.World, chunked.Log, chunked.Queue);
        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);
        resumed.AdvanceDays(40 * 360 - 17);

        Assert.Equal(WorldHasher.Hash(oneShot.World, oneShot.Log, oneShot.Queue), WorldHasher.Hash(resumed.World, resumed.Log, resumed.Queue));
    }
}
