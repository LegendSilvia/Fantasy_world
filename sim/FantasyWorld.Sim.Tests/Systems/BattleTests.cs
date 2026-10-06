using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class BattleTests
{
    private sealed class Fixed(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static (World World, EventLog Log, War War, Army Host, Army Guard) Armies()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var host = MilitarySystem.Mobilise(ctx, war, world.Realms[1]);
        var guard = MilitarySystem.Mobilise(ctx, war, world.Realms[2]);
        return (world, log, war, host, guard);
    }

    [Fact]
    public void Armies_InTheSameRegion_Fight()
    {
        var (world, log, war, host, guard) = Armies();
        host.Levies = 2000;
        guard.Levies = 20;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(new Fixed("unharmed")) }, checkInvariants: true);

        sim.AdvanceDays(30);

        var battle = sim.Log.All.Single(e => e.Type == "BattleFought");
        Assert.Equal(world.Realms[1].Ref, battle.Actors["victor"]);
        Assert.Equal(1, war.Battles);
        Assert.True(war.BattleScore > 0);
        Assert.True(guard.Disbanded);
        Assert.Contains(sim.Log.All, e => e.Type == "ArmyRouted");
    }

    [Fact]
    public void FallenCommanders_DieOfBattle_CitingIt()
    {
        var (world, log, _, host, guard) = Armies();
        host.Levies = 2000;
        guard.Levies = 1000;
        var commander = world.Characters[guard.CommanderId];
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(new Fixed("slain")) }, checkInvariants: true);

        sim.AdvanceDays(30);

        Assert.False(commander.IsAlive);
        var death = sim.Log.All.First(e => e.Type == "CharacterDied" && e.Actors["deceased"] == commander.Ref);
        Assert.Equal("battle", death.Payload["cause"]);
        Assert.Equal("BattleFought", sim.Log.Get(death.Causes.Single().EventId).Type);
    }

    [Fact]
    public void Defenders_AreFavouredAtHome()
    {
        var (world, _, _, host, guard) = Armies();
        host.Levies = guard.Levies = 1000;
        Assert.Equal(MilitarySystem.Strength(world, host, world.Regions[2]), MilitarySystem.Strength(world, guard, world.Regions[2]));
        var outcomes = MilitarySystem.BattleFate(world.Characters[host.CommanderId], won: false).Outcomes;
        Assert.Equal(1.0, outcomes.Sum(o => o.Probability), 9);
    }
}
