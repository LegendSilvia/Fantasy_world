using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

/// <summary>Campaign moves (sub-project 10 C1): army roads that respect mountains, and one move at a time.</summary>
public class CampaignMoveTests
{
    private static World Generated(ulong seed) => WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Medium), DateTimeOffset.UnixEpoch);

    [Fact]
    public void Mountains_AreBarriers_CrossedOnlyAtPasses_AndSlowerTheMoreMountainOnTheWay()
    {
        var (closed, slowed) = (0, 0);
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var world = Generated(seed);
            foreach (var link in world.Geography!.Graph.Links.Where(l => l.Kind == LinkKind.Land))
            {
                var share = Campaigns.MountainShare(world, link.A, link.B);
                var days = Campaigns.StepDays(world, link, link.A);
                if (share >= Campaigns.PassNeeded && !Campaigns.HasPass(world.Geography, link.A, link.B))
                {
                    closed++;
                    Assert.Null(days);
                    // The way round, if any, never takes the closed crossing.
                    if (Campaigns.Road(world, link.A, link.B) is { } road)
                        Assert.True(road.Path.Count > 2, $"seed {seed}: the road from {link.A} to {link.B} crossed the range");
                }
                else
                {
                    Assert.Equal(link.TravelDays * (1 + Campaigns.MountainToll * share), days!.Value, 6);
                    if (share > 0) slowed++;
                }
                Assert.Equal(share, Campaigns.MountainShare(world, link.B, link.A));
            }
        }
        Assert.True(closed > 0, "no generated world had a range without a pass");
        Assert.True(slowed > 0, "no generated world had a mountain crossing");
    }

    /// <summary>A generated world at war: an attacker whose host must cross at least three regions to the target.</summary>
    private static (World World, EventLog Log, War War, Army Host) AWarFarAway()
    {
        foreach (var seed in new ulong[] { 3, 4, 5, 6, 7, 8 })
        {
            var world = Generated(seed);
            var log = new EventLog();
            var realms = world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == 0).OrderBy(r => r.Id).ToList();
            foreach (var attacker in realms)
                foreach (var defender in realms.Where(d => d.Id != attacker.Id))
                {
                    var (from, to) = (world.Settlements[attacker.CapitalId].RegionId, world.Settlements[defender.CapitalId].RegionId);
                    if (Campaigns.Road(world, from, to) is not { Path.Count: >= 4 }) continue;
                    var cb = log.Append(world.Date, new EventDraft("BorderRaid"));
                    var declared = log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
                    var war = new War
                    {
                        Id = world.Ids.Take(War.Kind), Name = "The Long March", AttackerId = attacker.Id, DefenderId = defender.Id,
                        Attackers = { attacker.Id }, Defenders = { defender.Id }, CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id,
                        DeclaredEventId = declared.Id, TargetRegionId = to, StartDay = world.Date.Day,
                    };
                    world.Wars[war.Id] = war;
                    var host = MilitarySystem.Mobilise(new SimContext(world, log, new EventQueue(), new Rng(1)), war, attacker);
                    return (world, log, war, host);
                }
        }
        throw new InvalidOperationException("No generated world had two realms three regions apart.");
    }

    [Fact]
    public void AHost_MakesOneMoveAMonth_ToANeighbour_AndEachMoveIsRecorded()
    {
        var (world, log, war, host) = AWarFarAway();
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        var graph = world.Geography!.Graph;
        var visited = new List<long> { host.RegionId };

        for (var month = 0; month < 4 && !host.Disbanded; month++)
        {
            sim.AdvanceDays(30);
            // Only its next move is ever planned: the map's arrow shows that step, not the whole road.
            Assert.True(host.Path.Count <= 1);
            if (host.RegionId != visited[^1])
            {
                Assert.Contains(host.RegionId, graph.Neighbours(visited[^1]));
                visited.Add(host.RegionId);
            }
        }
        Assert.True(visited.Count >= 2, "the host never moved");
        var moves = sim.Log.All.Where(e => e.Type == "ArmyMoved" && e.Actors["army"] == host.Ref).ToList();
        Assert.Equal(visited.Count - 1, moves.Count);
        for (var i = 0; i < moves.Count; i++)
        {
            Assert.Equal((world.Regions[visited[i]].Ref, world.Regions[visited[i + 1]].Ref), (moves[i].Actors["from"], moves[i].Place!.Value));
            Assert.Equal(war.Ref, moves[i].Actors["war"]);
        }
        // Crossing into the enemy's land is news; a step through one's own land is a minor line.
        Assert.All(moves.Where(m => m.Payload["into"] == "own"), m => Assert.True(m.Importance < FantasyWorld.Sim.Chronicle.Compactor.MinorBelow));
    }

    [Fact]
    public void TheEpisode_TellsTheWarsMoves_InTheWarsSection_AndGivesThemToTheChronicler()
    {
        var (world, log, war, host) = AWarFarAway();
        var since = log.All[^1].Id;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        for (var month = 0; month < 6 && !host.Disbanded; month++) sim.AdvanceDays(30);
        var made = log.Since(since).Where(e => e.Type == "ArmyMoved").Select(e => e.Id).ToList();
        Assert.NotEmpty(made);

        var episode = FantasyWorld.Sim.Chronicle.Episode.Build(world, log, since, "2026-10-27");
        // The war's moves go with one section only, the first that tells the war, in the order they were made.
        var told = Assert.Single(episode.Parts, p => p.Moves is { Count: > 0 });
        var moves = told.Moves!;
        Assert.True(moves.Count <= FantasyWorld.Sim.Chronicle.Campaign.MovesPerWar);
        Assert.All(moves, m => Assert.Contains(m.EventId, made));
        Assert.Equal(moves.Select(m => m.EventId).OrderBy(id => id), moves.Select(m => m.EventId));
        // Each arrow in its side's colour: the attacker's hosts and the defender's alike march.
        Assert.All(moves, m => Assert.Equal(log.All.Single(e => e.Id == m.EventId).Actors["realm"].Id == war.AttackerId ? "attacker" : "defender", m.Side));
        var first = moves[0];
        var from = log.All.Single(e => e.Id == first.EventId).Actors["from"];
        Assert.Equal(FantasyWorld.Sim.Views.MapPoint.Of(world, from), first.From);
        // Each host's march is told once, in brief: how long, from where to where, no town between (the owner, 2026-10-29).
        var marches = moves.Where(m => m.Line.Length > 0).ToList();
        Assert.NotEmpty(marches);
        Assert.Equal(moves[^1].EventId, marches[^1].EventId);
        Assert.All(marches, m => Assert.Matches(@" marched (for (a|an|\w+) months?|in \w+ days|in a matter of days), (from .+ (to|into) .+|out from .+ and back again)\.$", m.Line));
        Assert.All(marches, m => Assert.Contains(m.Line, told.Text));

        // The chronicler is given every move with its section.
        var fake = new FantasyWorld.Sim.Llm.FakeLlmProvider(_ => System.Text.Json.JsonSerializer.Serialize(new { sections = episode.Parts.Select(p => p.Text) }));
        var story = new FantasyWorld.Sim.Llm.StoryContext(world, log,
            new FantasyWorld.Sim.Llm.LlmGateway(fake, new FantasyWorld.Sim.Llm.LlmSettings { Provider = "fake" }, new FantasyWorld.Sim.Llm.LlmJournal()));
        var written = new FantasyWorld.Sim.Narration.LlmNarrator(story).EpisodeScript(episode, 20_000);
        var prompt = fake.Requests.Single().Prompt;
        Assert.Contains("The war's marches:", prompt);
        Assert.All(marches, m => Assert.Contains(m.Line, prompt));
        Assert.Equal(moves, written.Parts.Single(p => p.Moves is { Count: > 0 }).Moves);
    }
}
