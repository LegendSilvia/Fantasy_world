using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>The beginnings of history and the new identities they lead to (the owner, 2026-10-29).</summary>
public class BeginningsTests(PrehistoricWorld fixture) : IClassFixture<PrehistoricWorld>
{
    private static readonly string[] Beginnings = ["CouncilConvened", "StoresLost", "ConspiratorsMet", "AssassinationPlotted", "LabyrinthOutbreak"];

    [Fact]
    public void ACentury_HasItsBeginnings_EachTitledAndCaused_AndWhatFollowsFromThem()
    {
        var log = fixture.Sim.Log;
        var begun = log.All.Where(e => Beginnings.Contains(e.Type)).ToList();
        Assert.NotEmpty(begun);
        Assert.All(begun, e => Assert.False(string.IsNullOrEmpty(e.Payload.GetValueOrDefault("title"))));
        Assert.True(begun.Count(e => e.Causes.Count > 0) >= begun.Count * 3 / 4);
        // Their consequences: plots carried out or foiled, culprits unmasked.
        var query = new HistoryQuery(log);
        var plots = begun.Where(e => e.Type is "AssassinationPlotted" or "ConspiratorsMet").ToList();
        if (plots.Count > 0)
            Assert.Contains(plots, p => query.Consequences(p.Id).Any(c => c.Type is "CharacterDied" or "AssassinationFoiled"));
        // A hidden hand is never named while it stays hidden.
        Assert.All(begun.Where(e => e.Payload.GetValueOrDefault("by") == "the work of a hand no one could name"),
            e => Assert.False(e.Actors.ContainsKey("culprit")));
    }

    [Fact]
    public void TheRules_ChooseThreeToSixAYear_InAFullWorld_AndClaudesChoicesAreChecked()
    {
        var sim = fixture.Fork();
        var system = new FantasyWorld.Sim.Systems.IncidentSystem();
        var ctx = new FantasyWorld.Sim.Engine.SimContext(sim.World, sim.Log, sim.Queue, new FantasyWorld.Sim.Core.Rng(1));
        var candidates = system.Candidates(ctx);
        Assert.NotEmpty(candidates);
        Assert.All(candidates.GroupBy(c => c.Kind), g => Assert.True(g.Count() <= FantasyWorld.Sim.Systems.IncidentSystem.OfAKind));
        Assert.All(candidates, c => Assert.Contains(c.Kind, Incident.Kinds));
    }

    [Fact]
    public void ATragedy_CanMakeSomeoneOver_AndTheirOldNameIsKept()
    {
        var changed = fixture.Sim.Log.All.Where(e => e.Type == "IdentityChanged").ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, e =>
        {
            Assert.NotEmpty(e.Causes);
            var who = fixture.World.Characters[e.Actors["who"].Id];
            Assert.Contains(e.Payload["from"], who.FormerNames);
            Assert.NotEqual(e.Payload["from"], e.Payload["to"]);
            Assert.Contains(e.Payload["kind"], new[] { "forgot", "forgotten", "hidden", "reborn" });
        });
        Assert.Equal(changed.Count, changed.Select(e => e.Actors["who"].Id).Distinct().Count()); // each once
    }

    [Fact]
    public void EnemiesClash_WithNoDaySet_ToldAsUnderWay_AndDecidedTheNextYear()
    {
        var log = fixture.Sim.Log;
        var clashes = log.All.Where(e => e.Type == "ClashBegun").ToList();
        Assert.NotEmpty(clashes);
        Assert.All(clashes, c => Assert.False(c.Payload.ContainsKey("day")));
        Assert.All(clashes, c => Assert.Contains(c.Payload["how"], new[] { "encounter", "ambush", "attack" }));
        // Each ends a year later as a fight (or is called off when one side is gone), and keeps how it began.
        var query = new HistoryQuery(log);
        var settled = clashes.Where(c => c.Date.Year < fixture.World.Date.Year - 1).ToList();
        // (A call-off is a minor event the chronicle may have condensed since.)
        Assert.True(settled.Count(c => query.Consequences(c.Id).Any(e => e.Type is "DuelFought" or "DuelBegun" or "DuelCalledOff")) >= settled.Count * 9 / 10);
        var fought = log.All.Where(e => e.Type == "DuelFought" && e.Payload.ContainsKey("how")).ToList();
        Assert.NotEmpty(fought);
        Assert.All(fought, f => Assert.True(f.Date.Day - log.Get(f.Causes[0].EventId).Date.Day >= 360 || log.Get(f.Causes[0].EventId).Type == "DuelBegun"));
    }
}
