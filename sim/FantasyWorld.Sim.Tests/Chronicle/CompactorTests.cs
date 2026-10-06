using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class CompactorTests
{
    [Fact]
    public void OldMinorEvents_AreSummarised_UnlessSomethingStillCitesThem()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var person = TestPeople.Add(world, 1, 30);
        var gossip = log.Append(new WorldDate(10), new EventDraft("Gossip", Importance: 5));
        var grudge = log.Append(new WorldDate(20), new EventDraft("Insult", Importance: 5));
        var cause = log.Append(new WorldDate(30), new EventDraft("Quarrel", Importance: 5));
        var feud = log.Append(new WorldDate(40), new EventDraft("FeudBegan", Importance: 60, Causes: new[] { new Cause(cause.Id, CauseKind.Trigger, 1) }));
        var later = log.Append(new WorldDate(20 * 360), new EventDraft("Gossip", Importance: 5));
        person.Memories.Add(new Memory(grudge.Id, person.Id, -50, 50));
        world.Date = new WorldDate(60 * 360);

        var result = Compactor.Compact(world, log, new EventQueue(), 1, 10);

        Assert.Equal(new[] { gossip.Id }, result.Forgotten);
        Assert.Equal(new[] { gossip.Id }, log.ForgottenUnsaved);
        Assert.All(new[] { grudge, cause, feud, later }, e => Assert.True(log.TryGet(e.Id, out _)));
        var summary = Assert.Single(result.Summaries);
        Assert.Equal("Year 1 to Year 10", summary.Payload["period"]);
        Assert.Equal("1 Gossip", summary.Payload["most common"]);
        Assert.Empty(Compactor.Compact(world, log, new EventQueue(), 1, 10).Forgotten);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compactor.Compact(world, log, new EventQueue(), 11, 20));
    }

    [Fact]
    public void TheChronicle_CompactsEachDecadeOnce_AsTheWorldRuns()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(360 * 80);

        var summaries = sim.Log.All.Where(e => e.Type == Compactor.SummaryType).ToList();
        Assert.Equal(new[] { "Year 1 to Year 10", "Year 11 to Year 20", "Year 21 to Year 30" }, summaries.Select(s => s.Payload["period"]));
        Assert.DoesNotContain(sim.Log.All, e => e.Date.Year <= 30 && e.Importance < Compactor.MinorBelow
                                                 && e.Type != Compactor.SummaryType && sim.Log.EffectsOf(e.Id).Count == 0
                                                 && !Compactor.References(world, sim.Queue).Contains(e.Id));
        foreach (var ev in sim.Log.All) new HistoryQuery(sim.Log).WhyChain(ev.Id, 8);
    }

    [Fact]
    public void ACompactedWorld_IsTheSameHoweverItWasSaved()
    {
        Simulator Make() => new(WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create());
        var whole = Make();
        whole.AdvanceDays(360 * 72);

        using var file = new TempFile();
        var first = Make();
        first.AdvanceDays(360 * 65);
        using (var store = WorldStore.Open(file.Path)) store.Save(first.World, first.Log, first.Queue);
        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);
        resumed.AdvanceDays(360 * 7);
        reopened.Save(world, log, queue);

        Assert.Equal(WorldHasher.Hash(whole.World, whole.Log, whole.Queue), WorldHasher.Hash(world, log, queue));
        Assert.Equal(log.Count, reopened.CountEvents());
        Assert.Empty(log.ForgottenUnsaved);
    }
}
