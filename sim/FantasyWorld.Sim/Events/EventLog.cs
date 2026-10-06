using System.Diagnostics.CodeAnalysis;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Events;

/// <summary>Append-only causal history. Causes always point to earlier events.</summary>
public sealed class EventLog
{
    public const int MaxCauses = 3;

    private readonly List<WorldEvent> _events = new();
    private readonly Dictionary<long, WorldEvent> _byId = new();
    private readonly Dictionary<long, List<long>> _effects = new();
    private readonly Dictionary<EntityRef, List<WorldEvent>> _byEntity = new();

    public long NextId { get; private set; } = 1;
    public int Count => _events.Count;
    public IReadOnlyList<WorldEvent> All => _events;

    public WorldEvent Append(WorldDate date, EventDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Type))
            throw new ArgumentException("Event type is required.", nameof(draft));
        if (_events.Count > 0 && date < _events[^1].Date)
            throw new InvalidOperationException($"Event dated {date} is earlier than the last event ({_events[^1].Date}).");

        var causes = (draft.Causes ?? Array.Empty<Cause>())
            .OrderByDescending(c => c.Weight)
            .ThenBy(c => c.EventId)
            .Take(MaxCauses)
            .ToArray();
        foreach (var cause in causes)
            if (!_byId.ContainsKey(cause.EventId))
                throw new InvalidOperationException($"Cause event #{cause.EventId} does not exist.");

        var ev = new WorldEvent(
            NextId,
            date,
            draft.Type,
            Sorted.From(draft.Actors),
            draft.Place,
            Sorted.From(draft.Payload),
            causes,
            draft.Importance,
            draft.Tags?.ToArray() ?? Array.Empty<string>());
        Add(ev);
        NextId++;
        return ev;
    }

    /// <summary>Re-adds a persisted event without cause validation (see Invariants for checks).</summary>
    public void Restore(WorldEvent ev)
    {
        if (ev.Id < NextId)
            throw new InvalidOperationException($"Restored event #{ev.Id} must have an id >= {NextId}.");
        Add(ev);
        NextId = ev.Id + 1;
    }

    public WorldEvent Get(long id) =>
        _byId.TryGetValue(id, out var ev) ? ev : throw new KeyNotFoundException($"Event #{id} not found.");

    public bool TryGet(long id, [MaybeNullWhen(false)] out WorldEvent ev) => _byId.TryGetValue(id, out ev);

    public IReadOnlyList<long> EffectsOf(long id) =>
        _effects.TryGetValue(id, out var list) ? list : Array.Empty<long>();

    public IReadOnlyList<WorldEvent> Involving(EntityRef entity) =>
        _byEntity.TryGetValue(entity, out var list) ? list : Array.Empty<WorldEvent>();

    public IEnumerable<WorldEvent> Since(long afterId) => _events.Where(e => e.Id > afterId);

    private void Add(WorldEvent ev)
    {
        _events.Add(ev);
        _byId[ev.Id] = ev;
        foreach (var cause in ev.Causes)
        {
            if (!_effects.TryGetValue(cause.EventId, out var effects))
                _effects[cause.EventId] = effects = new List<long>();
            effects.Add(ev.Id);
        }
        var entities = ev.Actors.Values.ToList();
        if (ev.Place is { } place) entities.Add(place);
        foreach (var entity in entities.Distinct())
        {
            if (!_byEntity.TryGetValue(entity, out var list))
                _byEntity[entity] = list = new List<WorldEvent>();
            list.Add(ev);
        }
    }
}
