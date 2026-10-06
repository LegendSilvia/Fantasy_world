using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>What a system sees during one tick: the world, the log, the queue and its own RNG stream.</summary>
public sealed class SimContext
{
    private readonly SortedDictionary<string, string> _annotations = new(StringComparer.Ordinal);

    public SimContext(World world, EventLog log, EventQueue queue, Rng rng)
    {
        World = world;
        Log = log;
        Queue = queue;
        Rng = rng;
    }

    public World World { get; }
    public EventLog Log { get; }
    public EventQueue Queue { get; }
    public Rng Rng { get; }
    public WorldDate Date => World.Date;

    public WorldEvent Emit(EventDraft draft)
    {
        if (_annotations.Count > 0)
        {
            var payload = new Dictionary<string, string>(draft.Payload ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            foreach (var (key, text) in _annotations) payload.TryAdd(key, text);
            _annotations.Clear();
            draft = draft with { Payload = payload };
        }
        return Log.Append(World.Date, draft);
    }

    /// <summary>
    /// Attaches text (an LLM's motive for a decision, its account of an encounter) to the next event emitted
    /// within the scope, as payload <paramref name="key"/>. Null text is a no-op; leaving the scope discards
    /// anything unused, so it never leaks onto unrelated events.
    /// </summary>
    public IDisposable Annotate(string key, string? text)
    {
        if (text is null) return Scope.Empty;
        var outer = _annotations.GetValueOrDefault(key);
        _annotations[key] = text;
        // Leaving the scope discards this text; an enclosing scope's text for the same key is not reinstated once used.
        return new Scope(() =>
        {
            if (_annotations.TryGetValue(key, out var current) && ReferenceEquals(current, text))
            {
                if (outer is null) _annotations.Remove(key);
                else _annotations[key] = outer;
            }
        });
    }

    private sealed class Scope(Action? onDispose) : IDisposable
    {
        public static readonly Scope Empty = new(null);
        public void Dispose() => onDispose?.Invoke();
    }
}
