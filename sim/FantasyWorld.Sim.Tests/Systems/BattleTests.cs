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

    /// <summary>Plans every battle as Claude might: the defenders win over 70 days, told on days 1, 35 and 70.</summary>
    private sealed class Planned(Func<BattleStage, IReadOnlyDictionary<long, Fate>> fates) : IArbiter
    {
        public List<BattleStage> Stages { get; } = new();

        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes[0];

        public BattlePlan PlanBattle(BattleStage stage)
        {
            Stages.Add(stage);
            return new BattlePlan(false, 70, [new(1, "The lines met."), new(35, "The ford was lost and won."), new(70, "The attackers broke.")],
                0.10, 0.30, fates(stage));
        }
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
    public void ALongBattle_HoldsBothHosts_GoesOn_AndEndsOnItsLastDay()
    {
        var (world, log, war, host, guard) = Armies();
        host.Levies = guard.Levies = 1000;
        var commander = world.Characters[guard.CommanderId];
        var arbiter = new Planned(stage => new Dictionary<long, Fate> { [stage.DefenderNotables.Last()] = Fate.Wounded });
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(arbiter) }, checkInvariants: true);
        sim.AdvanceDays(30);
        var joined = sim.Log.All.Single(e => e.Type == "BattleJoined");
        Assert.Equal(("70", "The lines met."), (joined.Payload["days"], joined.Payload["account"]));
        Assert.Equal(new[] { world.Realms[1].Ref, world.Realms[2].Ref }, new[] { joined.Actors["attacker"], joined.Actors["defender"] });
        Assert.True(host.Engaged(world.Date) && guard.Engaged(world.Date));
        Assert.Equal(commander.Id, arbiter.Stages.Single().DefenderNotables.Last());

        while (!sim.Log.All.Any(e => e.Type == "BattleFought")) sim.AdvanceDays(1);
        // Asked once: while it lasted no host marched off and no second battle was joined.
        Assert.Single(arbiter.Stages);
        Assert.Equal(joined.Place!.Value.Id, host.RegionId);
        var turned = sim.Log.All.Single(e => e.Type == "BattleTurned");
        Assert.Equal((joined.Date.AddDays(34), "The ford was lost and won."), (turned.Date, turned.Payload["account"]));
        var battle = sim.Log.All.Single(e => e.Type == "BattleFought");
        Assert.Equal(joined.Date.AddDays(69), battle.Date);
        Assert.Equal((world.Realms[2].Ref, "70", "The attackers broke."), (battle.Actors["victor"], battle.Payload["days"], battle.Payload["account"]));
        Assert.Equal(joined.Id, battle.Causes.Single().EventId);
        // The shares are taken from the troops still standing on the last day (attrition thins them while the hosts hold the field).
        Assert.InRange(int.Parse(battle.Payload["vanquished losses"]), 1, 400);
        Assert.Equal(0, host.EngagedUntil);
        Assert.Contains(sim.Log.All, e => e.Type == "WoundedInBattle" && e.Actors["character"] == commander.Ref && e.Causes.Single().EventId == battle.Id);
        Assert.True(war.BattleScore < 0);
    }

    [Fact]
    public void ALongBattle_IsBrokenOff_WhenAHostIsGoneBeforeItsEnd()
    {
        var (world, log, _, host, guard) = Armies();
        host.Levies = guard.Levies = 1000;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(new Planned(_ => new Dictionary<long, Fate>())) },
            checkInvariants: true);
        sim.AdvanceDays(30);
        var joined = sim.Log.All.Single(e => e.Type == "BattleJoined");
        Statecraft.Disband(world, guard);
        sim.AdvanceDays(70);

        Assert.DoesNotContain(sim.Log.All, e => e.Type is "BattleTurned" or "BattleFought");
        var off = sim.Log.All.Single(e => e.Type == "BattleBrokenOff");
        Assert.Equal(joined.Id, off.Causes.Single().EventId);
        Assert.Equal(0, host.EngagedUntil);
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

    [Fact]
    public void TheCommanderAndTheHeroes_WeighInTheBattle()
    {
        var (world, _, _, host, guard) = Armies();
        host.Levies = guard.Levies = 1000;
        var (captain, rival) = (world.Characters[host.CommanderId], world.Characters[guard.CommanderId]);
        captain.Attributes.Martial = 20;
        rival.Attributes.Martial = 2;
        var region = world.Regions[2];
        Assert.True(MilitarySystem.Strength(world, host, region) > 2 * MilitarySystem.Strength(world, guard, region));
        Assert.InRange(MilitarySystem.Generalship(world, host), 1.49, 1.51);

        // Heroes lift a host by their fighting strength, but never past the cap.
        rival.Attributes.Prowess = 200;
        Assert.Equal(1 + MilitarySystem.MostHeroism, MilitarySystem.Heroism(world, guard), 9);
    }
}
