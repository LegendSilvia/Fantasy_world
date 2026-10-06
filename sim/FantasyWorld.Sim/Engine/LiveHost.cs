using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Engine;

/// <summary>What one live step did: the world days it advanced and the events they produced.</summary>
public sealed record LiveTick(long Days, IReadOnlyList<WorldEvent> Events);

/// <summary>
/// Live mode (spec §7 "Modes"): while the game is open the world advances one day per 4 real minutes, through the
/// same tick function as catch-up. Each step simulates whatever real time is owed (a sleeping laptop catches up
/// on waking); if the clock moves backwards the world waits and never rewinds. The host loop is engine-agnostic:
/// the CLI drives it with a real clock, the Godot client from its frame loop, tests with a fake one.
/// </summary>
public sealed class LiveHost(Simulator sim)
{
    public Simulator Simulator { get; } = sim;

    /// <summary>When the next world day falls due.</summary>
    public DateTimeOffset NextDayAt => Simulator.World.LastRealUtc + TimeScale.RealTimePerWorldDay;

    public LiveTick Step(DateTimeOffset nowUtc)
    {
        var last = Simulator.Log.NextId - 1;
        var result = CatchUp.Run(Simulator, nowUtc);
        return new LiveTick(result.DaysAdvanced, result.DaysAdvanced > 0 ? Simulator.Log.Since(last).ToList() : Array.Empty<WorldEvent>());
    }

    /// <summary>Steps until <paramref name="until"/>, sleeping between world days; onTick sees every step that advanced.</summary>
    public void Run(Func<DateTimeOffset> clock, Action<TimeSpan> sleep, DateTimeOffset until, Action<LiveTick> onTick, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var tick = Step(clock());
            if (tick.Days > 0) onTick(tick);
            var now = clock();
            if (now >= until) return;
            var wait = NextDayAt - now;
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            if (now + wait > until) wait = until - now;
            sleep(wait);
        }
    }
}
