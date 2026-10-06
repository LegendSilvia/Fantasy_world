using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class CampaignTests
{
    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Mobilise_RaisesLevies_KnightsAndAMarshal()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var marshal = TestPeople.Add(world, 1, 40);
        world.Realms[1].Council[CouncilSeats.Marshal] = marshal.Id;
        var knight = TestPeople.Add(world, 1, 30, role: Role.Knight);
        var adults = world.Settlements[1].Adults;

        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);

        Assert.Equal(marshal.Id, army.CommanderId);
        Assert.Contains(knight.Id, army.Knights);
        Assert.Equal((int)(adults * MilitarySystem.LevyShare), army.Levies);
        Assert.Equal(adults - army.Levies, world.Settlements[1].Adults);
        Assert.Equal(army.Levies, army.RaisedFrom[1]);
        Assert.Equal(25, army.MenAtArms);
        Assert.Single(log.All, e => e.Type == "ArmyRaised");
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Attackers_MarchOnTheTarget()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        TestRealms.DeclareWar(world, log);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);

        sim.AdvanceDays(30);

        var host = world.Armies.Values.Single(a => a.RealmId == 1);
        Assert.Equal(2, host.RegionId);
        Assert.Equal(ArmyObjective.Siege, host.Objective);
    }

    [Fact]
    public void Foraging_EatsTheEnemysFood_AndScarsTheLand()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        TestRealms.DeclareWar(world, log);
        var (food, fertility) = (world.Regions[2].Food, world.Regions[2].Fertility);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(60);
        Assert.True(world.Regions[2].Food < food);
        Assert.True(world.Regions[2].Fertility < fertility);
    }

    [Fact]
    public void BrokeRealms_LoseTheirMenAtArms()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.MenAtArms = 400;
        army.Objective = ArmyObjective.Return;
        army.Morale = 45;
        world.Realms[1].Treasury = -10;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.True(army.MenAtArms < 400);
        Assert.True(army.Morale < 45);
    }

    [Fact]
    public void BeatenArmies_RejoinTheWarOnceRecovered()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.Objective = ArmyObjective.Return;
        army.Morale = 30;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(31);
        Assert.Equal(ArmyObjective.Return, army.Objective);
        army.Morale = 80;
        sim.AdvanceDays(30);
        Assert.Equal(ArmyObjective.Siege, army.Objective);
    }

    [Fact]
    public void Occupations_LapseWithoutAWar()
    {
        var world = TestRealms.TwoKingdoms();
        world.Regions[2].OccupierRealmId = 1;
        new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true).AdvanceDays(31);
        Assert.Equal(0, world.Regions[2].OccupierRealmId);
    }

    [Fact]
    public void Disbanding_SendsSurvivorsHome()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var before = world.Settlements[1].Adults;
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.Levies /= 2;
        Statecraft.Disband(world, army);
        Assert.True(army.Disbanded);
        Assert.InRange(world.Settlements[1].Adults, before - army.RaisedFrom[1] / 2 - 1, before - army.RaisedFrom[1] / 2 + 1);
    }
}
