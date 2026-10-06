using System.Diagnostics;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks with the real systems: determinism, invariants, history health, speed.</summary>
public class WorldHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size = WorldSize.Tiny, bool checkInvariants = true) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    private static long Population(World world) => world.Settlements.Values.Sum(s => (long)s.Population);

    [Fact]
    public void RealSystems_AreChunkInvariant()
    {
        var oneShot = NewSim(21);
        oneShot.AdvanceDays(3600);

        var chunked = NewSim(21);
        foreach (var chunk in new long[] { 1, 59, 400, 1140, 2000 }) chunked.AdvanceDays(chunk);

        Assert.True(oneShot.Log.Count > 10, "a decade should produce history");
        Assert.Equal(Hash(oneShot), Hash(chunked));
    }

    [Fact]
    public void SavedAndReloadedWorld_ContinuesIdentically()
    {
        using var file = new TempFile();
        var original = NewSim(22);
        original.AdvanceDays(1800);
        using (var store = WorldStore.Open(file.Path)) store.Save(original.World, original.Log, original.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);

        original.AdvanceDays(1800);
        resumed.AdvanceDays(1800);
        Assert.Equal(Hash(original), Hash(resumed));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoCenturies_StayHealthy(ulong seed)
    {
        var sim = NewSim(seed);
        var startPopulation = Population(sim.World);

        sim.AdvanceDays(200 * 360);

        var population = Population(sim.World);
        Assert.InRange(population, startPopulation / 2, startPopulation * 4);
        Assert.True(sim.World.Settlements.Values.Count(s => s.Abandoned) * 4 <= sim.World.Settlements.Count,
            "no more than a quarter of settlements should die out");
        var types = sim.Log.All.Select(e => e.Type).Distinct().ToList();
        Assert.True(types.Count >= 12, $"history should be varied, saw: {string.Join(", ", types)}");
        var query = new HistoryQuery(sim.Log);
        Assert.Contains(sim.Log.All, e => e.Causes.Count > 0 && query.WhyChain(e.Id).Causes.Any(c => c.Causes.Count > 0));
    }

    [Fact]
    public void MediumWorld_SimulatesAYearWellUnderTwoSeconds()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(360);
        var clock = Stopwatch.StartNew();
        sim.AdvanceDays(5 * 360);
        Assert.True(clock.Elapsed.TotalSeconds / 5 < 2.0, $"took {clock.Elapsed.TotalSeconds / 5:0.00} s per world year");
    }
}
