using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class DiplomacySystemTests
{
    private sealed class Choose(string prefix) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(prefix, StringComparison.Ordinal)) ?? brief.Options[^1];
    }

    private static Simulator Make(World world, EventLog log, string choice = "peace") =>
        new(world, log, new EventQueue(), new ISimSystem[] { new DiplomacySystem(new Choose(choice)) }, checkInvariants: true);

    private static WorldEvent Grudge(World world, EventLog log, long realm, long about, double feeling = -80)
    {
        var ev = log.Append(world.Date, new EventDraft("BorderRaid"));
        world.Realms[realm].Memories.Add(new RealmMemory(ev.Id, about, feeling, 90));
        return ev;
    }

    [Fact]
    public void Friends_Ally_WithARoyalMarriage()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var a = world.Characters[world.Settlements[1].LordId];
        var b = world.Characters[world.Settlements[2].LordId];
        var prince = TestPeople.Add(world, 1, 20, Sex.Male, fatherId: a.Id, houseId: a.HouseId);
        var princess = TestPeople.Add(world, 2, 20, Sex.Female, fatherId: b.Id, houseId: b.HouseId);
        var feast = log.Append(world.Date, new EventDraft("Feast"));
        a.Memories.Add(new Memory(feast.Id, b.Id, 100, 100));
        b.Memories.Add(new Memory(feast.Id, a.Id, 100, 100));
        foreach (var r in world.Realms.Values) r.Memories.Add(new RealmMemory(feast.Id, r.Id == 1 ? 2 : 1, 60, 60));
        var sim = Make(world, log);

        sim.AdvanceDays(360);

        Assert.True(world.Realms[1].Has(TreatyKind.Alliance, 2));
        var pact = sim.Log.All.Single(e => e.Type == "AllianceFormed");
        Assert.Equal("MarriageWed", sim.Log.Get(pact.Causes.Single().EventId).Type);
        Assert.Equal(princess.Id, prince.SpouseId);
    }

    [Fact]
    public void Enemies_DeclareRivalry()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Grudge(world, log, 2, 1);
        world.Realms[2].FaithId = 2;
        world.Faiths[2] = new Faith { Id = 2, Name = "Other", Deity = "Zed" };
        var sim = Make(world, log);
        sim.AdvanceDays(360);
        Assert.True(world.Realms[1].Has(TreatyKind.Rivalry, 2));
    }

    [Fact]
    public void Grudge_GivesACasusBelli_AndTheWarCitesIt()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var raid = Grudge(world, log, 1, 2);
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.Revenge, war.CasusBelli);
        Assert.Equal(raid.Id, war.CasusBelliEventId);
        var declared = sim.Log.Get(war.DeclaredEventId);
        Assert.Equal("WarDeclared", declared.Type);
        Assert.Equal(raid.Id, declared.Causes.Single().EventId);
        Assert.Contains(world.Realms[2].Memories, m => m.EventId == declared.Id && m.About == 1);
    }

    [Fact]
    public void ADeclaredWar_MustersForAYear_ShowingTheOdds_BeforeAnyHostIsRaised()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DiplomacySystem(new Choose("war")), new MilitarySystem() }, checkInvariants: true);

        sim.AdvanceDays(360);
        var war = world.Wars.Values.Single();
        var declared = sim.Log.Get(war.DeclaredEventId);
        Assert.Equal(war.StartDay + War.MusterDays, war.HostilitiesDay);
        Assert.InRange(int.Parse(declared.Payload["odds"]), 0, 100);
        Assert.True(double.Parse(declared.Payload["attacker strength"]) > 0);

        sim.AdvanceDays(War.MusterDays - 2);
        Assert.True(war.Mustering(world.Date));
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "ArmyRaised");

        sim.AdvanceDays(60);
        var raised = sim.Log.All.Where(e => e.Type == "ArmyRaised").ToList();
        Assert.NotEmpty(raised);
        Assert.All(raised, e => Assert.True(e.Date.Day >= war.HostilitiesDay));
    }

    [Fact]
    public void Zealots_ProclaimHolyWar_AgainstOtherFaiths()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Faiths[1].Tenets.Add("zealous");
        world.Faiths[2] = new Faith { Id = 2, Name = "The Other Way", Deity = "Zed" };
        world.Realms[2].FaithId = 2;
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.HolyWar, war.CasusBelli);
        Assert.Equal("HolyWarProclaimed", sim.Log.Get(war.CasusBelliEventId).Type);
        Assert.StartsWith("The Holy War of", war.Name);
    }

    [Fact]
    public void Peaceful_Rulers_KeepThePeace()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Make(world, log, "peace").AdvanceDays(360 * 5);
        Assert.Empty(world.Wars);
    }

    [Fact]
    public void StolenHeirloom_GivesArtifactRecovery()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.ArtifactRecovery);
        Assert.Equal(theft.Id, cb.EventId);
        Assert.Equal(crown.Id, cb.TargetArtifactId);
    }

    [Fact]
    public void AnInheritedHeirloom_IsNoLongerACasusBelli()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var inherited = log.Append(world.Date, new EventDraft("ArtifactInherited"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        crown.Provenance.Add(new ProvenanceEntry(inherited.Id, thief.Id, thief.Id, "inherited"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        Assert.DoesNotContain(DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]), o => o.Kind == CasusBelli.ArtifactRecovery);
    }

    [Fact]
    public void DeclaredRivals_HaveABorderDispute()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var rivalry = log.Append(world.Date, new EventDraft("RivalryDeclared"));
        world.Realms[1].Treaties.Add(new Treaty(TreatyKind.Rivalry, 2, rivalry.Id, 0));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.BorderDispute);
        Assert.Equal((2L, rivalry.Id), (cb.Against, cb.EventId));
    }

    [Fact]
    public void ARulerWhoCravesRenown_SeeksGlory_OnlyAgainstTheWeaker()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var ruler = world.Characters[world.Settlements[1].LordId];
        ruler.Ambition = Ambitions.Renown;
        var crowned = log.Append(world.Date, new EventDraft("SuccessionOccurred", Actors: new Dictionary<string, EntityRef> { ["heir"] = ruler.Ref }));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        Assert.DoesNotContain(DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]), o => o.Kind == CasusBelli.Glory);

        world.Realms[1].Treasury = 100_000;
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.Glory);
        Assert.Equal((2L, crowned.Id), (cb.Against, cb.EventId));
    }
}
