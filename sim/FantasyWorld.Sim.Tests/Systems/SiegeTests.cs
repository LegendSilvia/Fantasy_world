using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SiegeTests
{
    /// <summary>Realm 1's host stands at the target; realm 2 has no army (it fell back elsewhere).</summary>
    private static (World World, EventLog Log, War War, Army Host, Simulator Sim) AtTheGates(int walls, int garrison, string[]? commanderTraits = null)
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var town = world.Settlements[2];
        (town.Walls, town.Garrison) = (walls, garrison);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var host = MilitarySystem.Mobilise(ctx, war, world.Realms[1]);
        host.Levies = 3000;
        if (commanderTraits is not null) world.Characters[host.CommanderId].Traits.AddRange(commanderTraits);
        var guard = MilitarySystem.Mobilise(ctx, war, world.Realms[2]);
        Statecraft.Disband(world, guard);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        return (world, log, war, host, sim);
    }

    [Fact]
    public void UndefendedRegion_FallsAtOnce_AndScores()
    {
        var (world, _, war, _, sim) = AtTheGates(walls: 0, garrison: 10);
        sim.AdvanceDays(30);
        Assert.Equal(1, world.Regions[2].OccupierRealmId);
        Assert.Single(sim.Log.All, e => e.Type == "RegionOccupied");
        Assert.True(war.Score >= 10);
    }

    [Fact]
    public void WalledTown_IsBesiegedForMonths_ThenFalls()
    {
        var (world, _, _, _, sim) = AtTheGates(walls: 2, garrison: 100);
        sim.AdvanceDays(30);
        Assert.Single(sim.Log.All, e => e.Type == "SiegeBegan");
        Assert.Equal(0, world.Regions[2].OccupierRealmId);

        sim.AdvanceDays(90);

        var fell = sim.Log.All.First(e => e.Type == "SiegeEnded");
        Assert.Equal("SiegeBegan", sim.Log.Get(fell.Causes[0].EventId).Type);
        Assert.Equal(1, world.Regions[2].OccupierRealmId);
        Assert.Equal(1, world.Settlements[2].Walls);
    }

    [Fact]
    public void CruelCommanders_SackWhatFalls()
    {
        var (world, _, _, _, sim) = AtTheGates(walls: 1, garrison: 100, commanderTraits: new[] { Traits.Cruel });
        var people = world.Settlements[2].Population;
        sim.AdvanceDays(360 * 2);
        var sack = sim.Log.All.Single(e => e.Type == "SettlementSacked");
        Assert.Equal("SiegeEnded", sim.Log.Get(sack.Causes.Single().EventId).Type);
        Assert.True(world.Settlements[2].Population < people * 0.95);
    }

    [Fact]
    public void Defenders_LiberateOccupiedLand()
    {
        var (world, log, war, host, sim) = AtTheGates(walls: 0, garrison: 10);
        sim.AdvanceDays(30);
        Statecraft.Disband(world, host);
        var relief = MilitarySystem.Mobilise(new SimContext(world, log, new EventQueue(), new Rng(2)), war, world.Realms[2]);
        relief.RegionId = 2;
        sim.AdvanceDays(30);
        Assert.Equal(0, world.Regions[2].OccupierRealmId);
        Assert.Contains(sim.Log.All, e => e.Type == "RegionLiberated");
    }
}
