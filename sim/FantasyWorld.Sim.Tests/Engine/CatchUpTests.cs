using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class CatchUpTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static Simulator NewSim() =>
        new(WorldFactory.CreateNew(1, T0), new EventLog(), new EventQueue(), Array.Empty<ISimSystem>());

    [Fact]
    public void EightHoursAway_AdvancesFourMonths()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddHours(8));
        Assert.Equal(120, result.DaysAdvanced);
        Assert.Equal(0, result.DaysRemaining);
        Assert.Equal(new WorldDate(0), result.From);
        Assert.Equal(new WorldDate(120), result.To);
        Assert.Equal(T0.AddHours(8), sim.World.LastRealUtc);
    }

    [Fact]
    public void SubDayRemainder_CarriesToNextRun()
    {
        var sim = NewSim();
        CatchUp.Run(sim, T0.AddHours(8).AddSeconds(100));
        Assert.Equal(T0.AddHours(8), sim.World.LastRealUtc);

        var second = CatchUp.Run(sim, T0.AddHours(8).AddSeconds(240));
        Assert.Equal(1, second.DaysAdvanced);
        Assert.Equal(121, sim.World.Date.Day);
    }

    [Fact]
    public void ClockMovedBackwards_DoesNothing()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddHours(-5));
        Assert.Equal(0, result.DaysAdvanced);
        Assert.Equal(0, sim.World.Date.Day);
        Assert.Equal(T0, sim.World.LastRealUtc);
    }

    [Fact]
    public void LongAbsence_IsCappedAndRemainderReported()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddDays(60));
        Assert.Equal(10_800, result.DaysAdvanced);
        Assert.Equal(10_800, result.DaysRemaining);
        Assert.Equal(T0.AddDays(30), sim.World.LastRealUtc);

        var next = CatchUp.Run(sim, T0.AddDays(60));
        Assert.Equal(10_800, next.DaysAdvanced);
        Assert.Equal(0, next.DaysRemaining);
    }

    [Fact]
    public void ReportsProgressPerMonth()
    {
        var sim = NewSim();
        var reports = new List<WorldDate>();
        CatchUp.Run(sim, T0.AddHours(8), new ListProgress<WorldDate>(reports.Add));
        Assert.Equal(new long[] { 30, 60, 90, 120 }, reports.Select(r => r.Day));
    }
}
