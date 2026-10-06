using BenchmarkDotNet.Attributes;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Bench;

/// <summary>
/// Spec §7 performance target: at most 2 s per world year at 120 regions, rules only. Each iteration simulates one
/// year of a large world that already has 50 years of history (so the cast and the log are at working size).
/// </summary>
[MemoryDiagnoser]
public class WorldYearBenchmark
{
    private byte[] _start = Array.Empty<byte>();
    private IReadOnlyList<WorldEvent> _history = Array.Empty<WorldEvent>();
    private Sim.Model.Geography? _geography;
    private Simulator _sim = null!;

    [Params(WorldSize.Medium, WorldSize.Large)]
    public WorldSize Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, Size), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 50);
        _start = Packed.Pack(WorldSnapshot.Capture(world, sim.Queue));
        _history = sim.Log.All.ToList();
        _geography = world.Geography;
    }

    [IterationSetup]
    public void Fresh()
    {
        var snapshot = Packed.Unpack<WorldSnapshot>(_start)!;
        var world = snapshot.ToWorld();
        world.Geography = _geography;
        var log = new EventLog();
        foreach (var ev in _history) log.Restore(ev);
        _sim = new Simulator(world, log, snapshot.ToQueue(), DefaultSystems.Create());
    }

    [Benchmark]
    public void OneWorldYear() => _sim.AdvanceDays(WorldDate.DaysPerYear);
}
