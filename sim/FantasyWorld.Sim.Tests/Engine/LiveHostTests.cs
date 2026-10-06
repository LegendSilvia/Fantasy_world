using FantasyWorld.Cli;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class LiveHostTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static LiveHost Host() =>
        new(new Simulator(WorldFactory.CreateNew(5, T0), new EventLog(), new EventQueue(), new ISimSystem[] { new CoinSystem() }));

    [Fact]
    public void LiveMode_AdvancesOneWorldDayPerFourRealMinutes()
    {
        var host = Host();
        var now = T0;
        var ticks = new List<LiveTick>();
        host.Run(() => now, wait => now += wait, T0.AddHours(1), ticks.Add);
        Assert.Equal(15, host.Simulator.World.Date.Day);
        Assert.Equal(15, ticks.Sum(t => t.Days));
        Assert.All(ticks, t => Assert.Equal(1, t.Days));
        Assert.Equal(T0.AddHours(1), host.NextDayAt.AddMinutes(-4));
    }

    [Fact]
    public void AStepCatchesUpOwedTime_AndABackwardsClockWaits()
    {
        var host = Host();
        Assert.Equal(30, host.Step(T0.AddHours(2)).Days);
        var tick = host.Step(T0.AddHours(1));
        Assert.Equal(0, tick.Days);
        Assert.Empty(tick.Events);
        Assert.Equal(30, host.Simulator.World.Date.Day);
    }

    [Fact]
    public void TheLiveCommand_CatchesUpAndSaves()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => T0);
        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "live", file.Path, "--minutes", "0" }, output, () => T0.AddHours(24)));
        Assert.Contains("Stopped at Y2 M1 D1.", output.ToString());
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(360, store.Load().World.Date.Day);
    }
}
