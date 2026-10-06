using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

public sealed class EventQueue
{
    private readonly PriorityQueue<ScheduledAction, (long Day, int Priority, long Seq)> _queue = new();

    public long NextSeq { get; private set; }
    public int Count => _queue.Count;

    public ScheduledAction Schedule(
        WorldDate date, string kind, IReadOnlyDictionary<string, string>? data = null, int priority = 0)
    {
        var action = new ScheduledAction(date, priority, NextSeq++, kind, Sorted.From(data));
        _queue.Enqueue(action, Key(action));
        return action;
    }

    public bool TryDequeueDue(WorldDate today, out ScheduledAction action)
    {
        if (_queue.TryPeek(out var next, out _) && next.Date <= today)
        {
            action = _queue.Dequeue();
            return true;
        }
        action = null!;
        return false;
    }

    public IReadOnlyList<ScheduledAction> Snapshot() =>
        _queue.UnorderedItems.Select(i => i.Element).OrderBy(Key).ToList();

    public void Restore(IEnumerable<ScheduledAction> actions, long nextSeq)
    {
        _queue.Clear();
        foreach (var action in actions) _queue.Enqueue(action, Key(action));
        NextSeq = nextSeq;
    }

    private static (long Day, int Priority, long Seq) Key(ScheduledAction a) => (a.Date.Day, a.Priority, a.Seq);
}
