using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

/// <summary>A node in a "why" tree. Kind is how this event relates to its parent (null at the root).</summary>
public sealed record ChainNode(WorldEvent Event, CauseKind? Kind, IReadOnlyList<ChainNode> Causes);

public sealed class HistoryQuery
{
    private readonly EventLog _log;

    public HistoryQuery(EventLog log) => _log = log;

    public ChainNode WhyChain(long eventId, int depth = 4) => Build(_log.Get(eventId), null, depth);

    public IReadOnlyList<WorldEvent> RootCauses(long eventId)
    {
        var roots = new SortedSet<long>();
        var seen = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(eventId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id)) continue;
            var ev = _log.Get(id);
            if (ev.Causes.Count == 0)
            {
                if (id != eventId) roots.Add(id);
                continue;
            }
            foreach (var cause in ev.Causes) stack.Push(cause.EventId);
        }
        return roots.Select(_log.Get).ToList();
    }

    public IReadOnlyList<WorldEvent> Consequences(long eventId, int depth = 1)
    {
        var found = new SortedSet<long>();
        var frontier = new List<long> { eventId };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = new List<long>();
            foreach (var id in frontier)
                foreach (var effect in _log.EffectsOf(id))
                    if (found.Add(effect)) next.Add(effect);
            frontier = next;
        }
        return found.Select(_log.Get).ToList();
    }

    private ChainNode Build(WorldEvent ev, CauseKind? kind, int depth)
    {
        var causes = depth <= 0
            ? Array.Empty<ChainNode>()
            : ev.Causes.Select(c => Build(_log.Get(c.EventId), c.Kind, depth - 1)).ToArray();
        return new ChainNode(ev, kind, causes);
    }
}
