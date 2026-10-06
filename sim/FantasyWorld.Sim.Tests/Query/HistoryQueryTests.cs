using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Tests.Query;

public class HistoryQueryTests
{
    // drought(1) -> shortage(2); insult(3); shortage + insult -> war(4) -> victory(5)
    private static (EventLog Log, HistoryQuery Query) BuildChain()
    {
        var log = new EventLog();
        var d = new WorldDate(100);
        var drought = log.Append(d, new EventDraft("Drought"));
        var shortage = log.Append(d, new EventDraft("GrainShortageBegan",
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 1.0) }));
        var insult = log.Append(d, new EventDraft("MarriageRefused"));
        var war = log.Append(d, new EventDraft("WarDeclared", Causes: new[]
        {
            new Cause(insult.Id, CauseKind.Motive, 0.9),
            new Cause(shortage.Id, CauseKind.Enabler, 0.6),
        }));
        log.Append(d, new EventDraft("RiverCrownTaken",
            Causes: new[] { new Cause(war.Id, CauseKind.Trigger, 1.0) }));
        return (log, new HistoryQuery(log));
    }

    [Fact]
    public void WhyChain_WalksCausesStrongestFirst()
    {
        var (_, q) = BuildChain();
        var root = q.WhyChain(5);

        Assert.Equal("RiverCrownTaken", root.Event.Type);
        Assert.Null(root.Kind);
        var war = Assert.Single(root.Causes);
        Assert.Equal("WarDeclared", war.Event.Type);
        Assert.Equal(CauseKind.Trigger, war.Kind);
        Assert.Equal(new[] { "MarriageRefused", "GrainShortageBegan" }, war.Causes.Select(c => c.Event.Type));
        Assert.Equal("Drought", Assert.Single(war.Causes[1].Causes).Event.Type);
    }

    [Fact]
    public void WhyChain_RespectsDepth()
    {
        var (_, q) = BuildChain();
        var root = q.WhyChain(5, depth: 1);
        Assert.Empty(Assert.Single(root.Causes).Causes);
    }

    [Fact]
    public void RootCauses_ReturnsLeavesInIdOrder()
    {
        var (_, q) = BuildChain();
        Assert.Equal(new[] { "Drought", "MarriageRefused" }, q.RootCauses(5).Select(e => e.Type));
    }

    [Fact]
    public void RootCauses_OfUncausedEvent_IsEmpty()
    {
        var (_, q) = BuildChain();
        Assert.Empty(q.RootCauses(1));
    }

    [Fact]
    public void Consequences_FollowsEffectsToDepth()
    {
        var (_, q) = BuildChain();
        Assert.Equal(new[] { "GrainShortageBegan" }, q.Consequences(1).Select(e => e.Type));
        Assert.Equal(new[] { "GrainShortageBegan", "WarDeclared", "RiverCrownTaken" },
            q.Consequences(1, depth: 3).Select(e => e.Type));
    }

    [Fact]
    public void EventText_DescribesEventOnOneLine()
    {
        var log = new EventLog();
        var ev = log.Append(WorldDate.FromYmd(3, 2, 1), new EventDraft("GrainShortageBegan",
            Payload: new Dictionary<string, string> { ["grain"] = "35" }));
        Assert.Equal("#1 Y3 M2 D1 GrainShortageBegan grain=35", EventText.Describe(ev));
    }

    [Fact]
    public void EventText_NamesKnownEntities()
    {
        var log = new EventLog();
        var ev = log.Append(new WorldDate(0), new EventDraft("Flood",
            Actors: new Dictionary<string, EntityRef> { ["from"] = new("region", 2) },
            Place: new EntityRef("region", 3)));
        var text = EventText.Describe(ev, e => e.Id == 3 ? "Brenmoor" : null);
        Assert.Equal("#1 Y1 M1 D1 Flood from=region#2 at Brenmoor (region#3)", text);
    }
}
