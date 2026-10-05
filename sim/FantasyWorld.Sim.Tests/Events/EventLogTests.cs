using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Events;

public class EventLogTests
{
    private static readonly WorldDate D10 = new(10);

    [Fact]
    public void Append_AssignsIncreasingIdsAndDate()
    {
        var log = new EventLog();
        var a = log.Append(D10, new EventDraft("A"));
        var b = log.Append(D10, new EventDraft("B"));
        Assert.Equal(1, a.Id);
        Assert.Equal(2, b.Id);
        Assert.Equal(D10, b.Date);
        Assert.Equal(2, log.Count);
        Assert.Equal(3, log.NextId);
    }

    [Fact]
    public void Append_RejectsBlankType() =>
        Assert.Throws<ArgumentException>(() => new EventLog().Append(D10, new EventDraft(" ")));

    [Fact]
    public void Append_RejectsUnknownCause()
    {
        var log = new EventLog();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            log.Append(D10, new EventDraft("A", Causes: new[] { new Cause(99, CauseKind.Trigger, 1) })));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Append_RejectsDateBeforeLastEvent()
    {
        var log = new EventLog();
        log.Append(D10, new EventDraft("A"));
        Assert.Throws<InvalidOperationException>(() => log.Append(new WorldDate(9), new EventDraft("B")));
    }

    [Fact]
    public void Append_KeepsThreeStrongestCauses()
    {
        var log = new EventLog();
        for (var i = 0; i < 4; i++) log.Append(D10, new EventDraft("Seed"));
        var ev = log.Append(D10, new EventDraft("Effect", Causes: new[]
        {
            new Cause(1, CauseKind.Trigger, 0.1),
            new Cause(2, CauseKind.Motive, 0.9),
            new Cause(3, CauseKind.Enabler, 0.5),
            new Cause(4, CauseKind.Trigger, 0.7),
        }));
        Assert.Equal(new long[] { 2, 4, 3 }, ev.Causes.Select(c => c.EventId));
    }

    [Fact]
    public void EffectsOf_ReturnsReverseLinks()
    {
        var log = new EventLog();
        var cause = log.Append(D10, new EventDraft("Cause"));
        var e1 = log.Append(D10, new EventDraft("E1", Causes: new[] { new Cause(cause.Id, CauseKind.Trigger, 1) }));
        var e2 = log.Append(D10, new EventDraft("E2", Causes: new[] { new Cause(cause.Id, CauseKind.Motive, 1) }));
        Assert.Equal(new[] { e1.Id, e2.Id }, log.EffectsOf(cause.Id));
        Assert.Empty(log.EffectsOf(e1.Id));
    }

    [Fact]
    public void Involving_IndexesActorsAndPlace()
    {
        var log = new EventLog();
        var aldric = new EntityRef("char", 88);
        var ford = new EntityRef("region", 3);
        var ev = log.Append(D10, new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["ruler"] = aldric },
            Place: ford));
        log.Append(D10, new EventDraft("Unrelated"));
        Assert.Equal(ev.Id, Assert.Single(log.Involving(aldric)).Id);
        Assert.Equal(ev.Id, Assert.Single(log.Involving(ford)).Id);
        Assert.Empty(log.Involving(new EntityRef("char", 1)));
    }

    [Fact]
    public void Payload_IsStoredSortedByKey()
    {
        var log = new EventLog();
        var ev = log.Append(D10, new EventDraft("A",
            Payload: new Dictionary<string, string> { ["zeta"] = "1", ["alpha"] = "2" }));
        Assert.Equal(new[] { "alpha", "zeta" }, ev.Payload.Keys);
    }

    [Fact]
    public void Since_ReturnsOnlyNewerEvents()
    {
        var log = new EventLog();
        log.Append(D10, new EventDraft("A"));
        log.Append(D10, new EventDraft("B"));
        log.Append(D10, new EventDraft("C"));
        Assert.Equal(new[] { "B", "C" }, log.Since(1).Select(e => e.Type));
    }

    [Fact]
    public void Restore_RebuildsIndexesAndNextId()
    {
        var source = new EventLog();
        var a = source.Append(D10, new EventDraft("A"));
        var b = source.Append(D10, new EventDraft("B", Causes: new[] { new Cause(a.Id, CauseKind.Trigger, 1) }));

        var restored = new EventLog();
        restored.Restore(a);
        restored.Restore(b);

        Assert.Equal(3, restored.NextId);
        Assert.Equal(new[] { b.Id }, restored.EffectsOf(a.Id));
        Assert.Equal(3, restored.Append(D10, new EventDraft("C")).Id);
    }

    [Fact]
    public void Restore_RejectsNonIncreasingId()
    {
        var source = new EventLog();
        var a = source.Append(D10, new EventDraft("A"));
        var restored = new EventLog();
        restored.Restore(a);
        Assert.Throws<InvalidOperationException>(() => restored.Restore(a));
    }

    [Fact]
    public void Get_UnknownId_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => new EventLog().Get(5));
}
