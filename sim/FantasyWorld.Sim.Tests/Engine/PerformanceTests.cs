using System.Diagnostics;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>
/// Spec §7: at most 2 s per world year at 120 regions with the LLM faked (here: offline rules). A coarse guard for
/// every test run; precise numbers come from the BenchmarkDotNet project (sim/FantasyWorld.Bench).
/// </summary>
public class PerformanceTests
{
    [Fact]
    public void ALargeWorldWithHistory_SimulatesAYearWellInsideTheBudget()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Large), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 20);
        var watch = Stopwatch.StartNew();
        sim.AdvanceDays(5 * 360);
        var perYear = watch.Elapsed / 5;
        Assert.True(perYear < TimeSpan.FromSeconds(2), $"{perYear.TotalMilliseconds:0} ms per world year");
    }
}
