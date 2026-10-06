using BenchmarkDotNet.Attributes;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Bench;

/// <summary>Sub-project 2 §6: each screen at most 50 ms on a medium world after 300 years.</summary>
[MemoryDiagnoser]
public class ViewsBenchmark
{
    private Simulator _sim = null!;
    private long _war;
    private long _lord;

    [GlobalSetup]
    public void Setup()
    {
        _sim = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch), 300);
        _war = _sim.Log.All.Last(e => e.Type == "WarDeclared").Id;
        _lord = _sim.World.Settlements.Values.First(s => s.LordId != 0).LordId;
    }

    [Benchmark]
    public DigestScreen Digest() => DigestScreen.Build(_sim.World, _sim.Log,
        Sim.Chronicle.Digest.Build(_sim.Log, _sim.World, _sim.Log.NextId - 500, new HashSet<EntityRef>()), _sim.World.Date.AddDays(-360));

    [Benchmark]
    public MapScreen MapToday() => MapScreen.Build(_sim.World, _sim.Log);

    [Benchmark]
    public MapScreen MapFiftyYearsAgo() => MapScreen.Build(_sim.World, _sim.Log, _sim.World.Date.AddDays(-50 * 360));

    [Benchmark]
    public WhyScreen Why() => WhyScreen.Build(_sim.World, _sim.Log, _war);

    [Benchmark]
    public CharacterScreen Character() => CharacterScreen.Build(_sim.World, _sim.Log, _lord);
}
