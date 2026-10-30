using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class PeaceTests
{
    private sealed class Choose(string key) : IDecider
    {
        // The peace answer; any other decision (a commander's move) goes to the rules.
        public DecisionOption Decide(DecisionBrief brief, Rng rng) => brief.Options.SingleOrDefault(o => o.Key == key) ?? new RulesDecider().Decide(brief, rng);
    }

    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Conquest_CedesOccupiedLand_AndAFallenRealmIsGone()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        world.Regions[2].OccupierRealmId = 1;
        war.Score = 70;

        var peace = MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);

        Assert.True(war.Ended);
        Assert.Equal("attackers won", peace.Payload["outcome"]);
        Assert.Equal(1, world.Regions[2].OwnerRealmId);
        Assert.True(world.Realms[2].Destroyed);
        Assert.Contains(log.All, e => e.Type == "RegionCeded" && e.Causes[0].EventId == peace.Id);
        Assert.Contains(log.All, e => e.Type == "RealmFell");
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Losers_AcceptTerms_AtADecisiveScore()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        war.BattleScore = 60;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(decider: new Choose("accept")) }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.True(war.Ended);
        Assert.All(world.Armies.Values, a => Assert.True(a.Disbanded));
    }

    [Fact]
    public void StubbornLosers_FightOn()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        war.BattleScore = 60;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(decider: new Choose("fight")) }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.False(war.Ended);
    }

    [Fact]
    public void Stalemates_EndInWhitePeace()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        world.Date = new WorldDate(world.Date.Day + 360 * 5);
        var peace = MilitarySystem.MakePeace(Context(world, log), war, attackerWon: false, white: true);
        Assert.Equal("status quo", peace.Payload["terms"]);
        Assert.False(world.Realms[2].Destroyed);
        Assert.Equal(2, world.Regions[2].OwnerRealmId);
    }

    [Fact]
    public void FailedRebellion_RestoresVassalage()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var declared = log.Append(world.Date, new EventDraft("IndependenceDeclared"));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = "The Rebellion",
            AttackerId = 1,
            DefenderId = 2,
            Attackers = { 1 },
            Defenders = { 2 },
            CasusBelli = CasusBelli.Independence,
            CasusBelliEventId = declared.Id,
            DeclaredEventId = declared.Id,
        };
        world.Wars[war.Id] = war;
        world.Realms[2].Treaties.Add(new Treaty(TreatyKind.Rivalry, 1, declared.Id, 0));
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);
        Assert.Equal(1, world.Realms[2].LiegeId);
        Assert.Empty(world.Realms[2].Treaties);
        Assert.Contains("vassalage restored", log.All.Last().Payload["terms"]);
    }

    [Fact]
    public void SuccessfulRebellion_ConfirmsIndependence()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var declared = log.Append(world.Date, new EventDraft("IndependenceDeclared"));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = "The Rebellion",
            AttackerId = 1,
            DefenderId = 2,
            Attackers = { 1 },
            Defenders = { 2 },
            CasusBelli = CasusBelli.Independence,
            CasusBelliEventId = declared.Id,
            DeclaredEventId = declared.Id,
        };
        world.Wars[war.Id] = war;
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: false, white: false);
        Assert.Equal(0, world.Realms[1].LiegeId);
        Assert.Equal(0, world.Realms[2].LiegeId);
        Assert.Equal("independence", log.All.Last().Payload["terms"]);
    }

    [Fact]
    public void ArtifactRecovery_ReturnsTheRelic()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var holder = world.Characters[world.Settlements[2].LordId];
        var crown = TestPeople.GiveArtifact(world, holder);
        world.Wars[war.Id] = new War
        {
            Id = war.Id,
            Name = war.Name,
            AttackerId = 1,
            DefenderId = 2,
            Attackers = { 1 },
            Defenders = { 2 },
            CasusBelli = CasusBelli.ArtifactRecovery,
            CasusBelliEventId = war.CasusBelliEventId,
            DeclaredEventId = war.DeclaredEventId,
            TargetArtifactId = crown.Id,
        };
        MilitarySystem.MakePeace(Context(world, log), world.Wars[war.Id], attackerWon: true, white: false);
        Assert.Equal(world.Settlements[1].LordId, crown.HolderId);
        Assert.Equal("returned", crown.Provenance.Last().How);
    }

    [Fact]
    public void AVictorWithNothingToTake_TakesTribute()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log, target: 1);
        var (theirs, ours) = (world.Realms[2].Treasury, world.Realms[1].Treasury);
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);
        Assert.Equal("tribute", log.All.Last().Payload["terms"]);
        Assert.True(world.Realms[1].Treasury > ours);
        Assert.True(world.Realms[2].Treasury < theirs);
    }
}
