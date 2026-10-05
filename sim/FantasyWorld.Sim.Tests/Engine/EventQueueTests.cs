using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Tests.Engine;

public class EventQueueTests
{
    private static List<string> DrainUntil(EventQueue q, long day)
    {
        var kinds = new List<string>();
        while (q.TryDequeueDue(new WorldDate(day), out var a)) kinds.Add(a.Kind);
        return kinds;
    }

    [Fact]
    public void Dequeues_ByDateThenPriorityThenSeq()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(5), "late");
        q.Schedule(new WorldDate(3), "b", priority: 1);
        q.Schedule(new WorldDate(3), "a", priority: 0);
        q.Schedule(new WorldDate(3), "c", priority: 1);
        Assert.Equal(new[] { "a", "b", "c", "late" }, DrainUntil(q, 10));
    }

    [Fact]
    public void FutureActions_AreNotDue()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(5), "x");
        Assert.Empty(DrainUntil(q, 4));
        Assert.Equal(new[] { "x" }, DrainUntil(q, 5));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Schedule_CopiesDataSorted()
    {
        var q = new EventQueue();
        var a = q.Schedule(new WorldDate(1), "x", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        Assert.Equal(new[] { "a", "b" }, a.Data.Keys);
        Assert.Equal(0, a.Seq);
        Assert.Equal(1, q.NextSeq);
    }

    [Fact]
    public void SnapshotAndRestore_PreserveOrderAndSeq()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(9), "z");
        q.Schedule(new WorldDate(2), "y");
        q.Schedule(new WorldDate(2), "x", priority: -1);

        var snap = q.Snapshot();
        Assert.Equal(new[] { "x", "y", "z" }, snap.Select(a => a.Kind));

        var copy = new EventQueue();
        copy.Restore(snap, q.NextSeq);
        Assert.Equal(3, copy.NextSeq);
        Assert.Equal(new[] { "x", "y", "z" }, DrainUntil(copy, 100));
    }
}
