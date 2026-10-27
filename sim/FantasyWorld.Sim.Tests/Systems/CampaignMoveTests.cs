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
}
