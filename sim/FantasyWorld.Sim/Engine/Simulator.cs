using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>
/// Advances the world one day at a time. Each day: monthly ticks (first of month), yearly ticks
/// (first of year), then due scheduled actions. Catch-up and live mode both use AdvanceDays.
/// </summary>
public sealed class Simulator
{
    private readonly IReadOnlyList<ISimSystem> _systems;
    private readonly Dictionary<string, ISimSystem> _handlers = new(StringComparer.Ordinal);
    private readonly bool _checkInvariants;
    private int _checkedCount;

    public Simulator(World world, EventLog log, EventQueue queue, IReadOnlyList<ISimSystem> systems, bool checkInvariants = false)
    {
        World = world;
        Log = log;
        Queue = queue;
        _systems = systems;
        _checkInvariants = checkInvariants;
        _checkedCount = log.Count;
        foreach (var system in systems)
            foreach (var kind in system.HandledKinds)
                if (!_handlers.TryAdd(kind, system))
                    throw new ArgumentException($"Scheduled kind '{kind}' is handled by more than one system.", nameof(systems));
    }

    public World World { get; }
    public EventLog Log { get; }
    public EventQueue Queue { get; }

    public void AdvanceDays(long days, IProgress<WorldDate>? progress = null, CancellationToken ct = default)
    {
        for (long i = 0; i < days; i++)
        {
            ct.ThrowIfCancellationRequested();
            StepDay();
            if (World.Date.IsFirstOfMonth) progress?.Report(World.Date);
        }
    }

    private void StepDay()
    {
        World.Date = World.Date.AddDays(1);
        var changed = World.Date.IsFirstOfMonth;

        if (World.Date.IsFirstOfMonth)
            foreach (var system in _systems)
                system.OnMonthlyTick(ContextFor(system.Name, "month"));

        if (World.Date.IsFirstOfYear)
            foreach (var system in _systems)
                system.OnYearlyTick(ContextFor(system.Name, "year"));

        while (Queue.TryDequeueDue(World.Date, out var action))
        {
            if (!_handlers.TryGetValue(action.Kind, out var handler))
                throw new InvalidOperationException($"No system handles scheduled kind '{action.Kind}'.");
            handler.OnScheduled(ContextFor(handler.Name, "scheduled/" + action.Seq), action);
            changed = true;
        }

        if (_checkInvariants)
        {
            // Entities only change when a tick or scheduled action ran; the log is checked every day.
            var errors = Invariants.Check(World, Log, _checkedCount, entities: changed);
            if (errors.Count > 0) throw new InvariantViolationException(errors);
            _checkedCount = Log.Count;
        }
    }

    private SimContext ContextFor(string system, string phase) =>
        new(World, Log, Queue, Rng.ForStream(World.Seed, system + "/" + phase, World.Date.Day));
}
