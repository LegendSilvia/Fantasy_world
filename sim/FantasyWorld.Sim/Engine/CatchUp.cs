using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

public sealed record CatchUpResult(long DaysAdvanced, long DaysRemaining, WorldDate From, WorldDate To);

/// <summary>Fast-forwards the world by the real time that passed since it last ran.</summary>
public static class CatchUp
{
    public static CatchUpResult Run(
        Simulator sim, DateTimeOffset nowUtc, IProgress<WorldDate>? progress = null, CancellationToken ct = default)
    {
        var world = sim.World;
        var from = world.Date;
        var owed = TimeScale.WholeWorldDays(nowUtc - world.LastRealUtc);
        if (owed <= 0) return new CatchUpResult(0, 0, from, from);

        var days = Math.Min(owed, TimeScale.MaxCatchUpDays);
        try
        {
            sim.AdvanceDays(days, progress, ct);
        }
        catch (OperationCanceledException)
        {
            // Stopped part-way (the game was closed): the clock keeps pace with the days actually simulated, so saving
            // now never makes the world run ahead of real time.
            world.LastRealUtc += TimeSpan.FromTicks(TimeScale.RealTimePerWorldDay.Ticks * (world.Date.Day - from.Day));
            throw;
        }
        world.LastRealUtc += TimeSpan.FromTicks(TimeScale.RealTimePerWorldDay.Ticks * days);
        return new CatchUpResult(days, owed - days, from, world.Date);
    }
}
