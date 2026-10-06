using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class LifeSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new LifeSystem() }, checkInvariants: true);

    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Mortality_RisesWithAge()
    {
        Assert.True(LifeSystem.BaseMortality(30) < LifeSystem.BaseMortality(62));
        Assert.True(LifeSystem.BaseMortality(62) < LifeSystem.BaseMortality(90));
    }

    [Fact]
    public void TheVeryOld_Die_OfOldAge()
    {
        var world = TestPeople.World();
        var elders = Enumerable.Range(0, 30).Select(_ => TestPeople.Add(world, 1, 88)).ToList();
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.True(elders.Count(e => !e.IsAlive) >= 20);
        Assert.All(sim.Log.All.Where(e => e.Type == "CharacterDied"), e => Assert.Equal("old age", e.Payload["cause"]));
    }

    [Fact]
    public void PlagueDeaths_CiteThePlague()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var plague = log.Append(world.Date, new EventDraft("PlagueOutbreak"));
        world.Regions[1].Conditions[Conditions.Plague] = plague.Id;
        for (var i = 0; i < 60; i++) TestPeople.Add(world, 1, 30);
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        var died = sim.Log.All.Where(e => e.Type == "CharacterDied" && e.Payload["cause"] == "plague").ToList();
        Assert.NotEmpty(died);
        Assert.All(died, d => Assert.Equal(plague.Id, Assert.Single(d.Causes).EventId));
    }

    [Fact]
    public void LordsDeath_PassesSeatAndArtifactsToEldestChild()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, Sex.Male, Role.Lord);
        var lady = TestPeople.Add(world, 1, 58, Sex.Female, houseId: lord.HouseId);
        TestPeople.Marry(world, lord, lady);
        var younger = TestPeople.Add(world, 2, 20, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        var elder = TestPeople.Add(world, 2, 30, Sex.Female, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        var sword = TestPeople.GiveArtifact(world, lord);

        var death = Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());

        Assert.Equal(elder.Id, world.Settlements[1].LordId);
        Assert.Equal(Role.Lord, elder.Role);
        Assert.Equal(1, elder.SettlementId);
        Assert.Equal(Role.None, lord.Role);
        Assert.Equal(0, lady.SpouseId);
        Assert.Equal(elder.Id, sword.HolderId);
        Assert.Equal("inherited", sword.Provenance.Last().How);
        var succession = Assert.Single(log.All, e => e.Type == "SuccessionOccurred");
        Assert.Equal(death.Id, succession.Causes[0].EventId);
        Assert.Contains(younger.Memories, m => m.EventId == death.Id);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void LordWithoutKin_LeavesTheSeatEmpty_AndTheTreasuryFull()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 50, role: Role.Lord);
        var crown = TestPeople.GiveArtifact(world, lord);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(0, world.Settlements[1].LordId);
        Assert.Equal(log.All[0].Id, world.Settlements[1].Conditions[Fates.VacantSeat]);
        Assert.Equal(ArtifactPlace.Treasury, crown.Place);
        Assert.Equal(1, crown.SettlementId);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void ChildlessHolder_LeavesArtifactsToTheWidow()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var knight = TestPeople.Add(world, 1, 50);
        var widow = TestPeople.Add(world, 1, 45, Sex.Female);
        TestPeople.Marry(world, knight, widow);
        var blade = TestPeople.GiveArtifact(world, knight);
        Fates.Die(Context(world, log), knight, "illness", Array.Empty<Cause>());
        Assert.Equal(widow.Id, blade.HolderId);
    }

    [Fact]
    public void DeathInTheWilds_LosesWhatTheyCarried()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hunter = TestPeople.Add(world, 1, 30);
        TestPeople.Add(world, 1, 5, fatherId: hunter.Id, houseId: hunter.HouseId);
        var spear = TestPeople.GiveArtifact(world, hunter);
        Fates.Die(Context(world, log), hunter, "monsters", Array.Empty<Cause>(), lostInRegion: 2);
        Assert.Equal(ArtifactPlace.Lost, spear.Place);
        Assert.Equal(2, spear.RegionId);
        Assert.Empty(hunter.Memories);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Killer_NeitherInherits_NorSwearsVengeanceOnThemselves()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var elder = TestPeople.Add(world, 1, 35, fatherId: lord.Id, houseId: lord.HouseId);
        var younger = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        Fates.Die(Context(world, log), lord, "a duel", Array.Empty<Cause>(), killer: elder);
        Assert.Equal(younger.Id, world.Settlements[1].LordId);
        Assert.NotEqual(elder.Id, elder.AmbitionTarget);
        Assert.Equal(elder.Id, younger.AmbitionTarget);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void FadedChildren_DoNotInherit()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var faded = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        faded.Faded = true;
        var crown = TestPeople.GiveArtifact(world, lord);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(0, world.Settlements[1].LordId);
        Assert.Equal(ArtifactPlace.Treasury, crown.Place);
    }

    [Fact]
    public void HeirMarriedToALord_PassesTheSeatOn()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var eldest = TestPeople.Add(world, 2, 35, Sex.Female, fatherId: lord.Id, houseId: lord.HouseId);
        var otherLord = TestPeople.Add(world, 2, 35, Sex.Male, Role.Lord);
        TestPeople.Marry(world, eldest, otherLord);
        var second = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(second.Id, world.Settlements[1].LordId);
        Assert.Equal(2, otherLord.SettlementId);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void ChildLord_NeverFades()
    {
        var world = TestPeople.World();
        for (var i = 0; i < 40; i++) TestPeople.Add(world, 2, 30);
        var young = TestPeople.Add(world, 1, 15, role: Role.Lord);
        new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new LifeSystem() }, checkInvariants: true)
            .AdvanceDays(360);
        Assert.False(young.Faded);
    }

    [Fact]
    public void Killing_StirsVengeanceInKin()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var victim = TestPeople.Add(world, 1, 40);
        var son = TestPeople.Add(world, 1, 20, fatherId: victim.Id, houseId: victim.HouseId);
        var killer = TestPeople.Add(world, 2, 30);
        Fates.Die(Context(world, log), victim, "duel", Array.Empty<Cause>(), killer);
        Assert.Equal(Ambitions.Vengeance, son.Ambition);
        Assert.Equal(killer.Id, son.AmbitionTarget);
        Assert.True(Relations.Opinion(son, killer) < 0);
    }

    [Fact]
    public void Children_ComeOfAge()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var heir = TestPeople.Add(world, 1, 15, fatherId: lord.Id, houseId: lord.HouseId);
        heir.Traits.AddRange(new[] { Traits.Brave });
        var sim = Make(world);
        sim.AdvanceDays(360);
        var came = Assert.Single(sim.Log.All, e => e.Type == "ComingOfAge");
        Assert.Equal(heir.Ref, came.Actors["character"]);
        Assert.False(heir.Faded);
    }

    [Fact]
    public void Crowded_World_FadesSomeYoungsters_ButNeverTheHeir()
    {
        var world = TestPeople.World();
        for (var i = 0; i < 40; i++) TestPeople.Add(world, 2, 30);
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var heir = TestPeople.Add(world, 1, 15, fatherId: lord.Id, houseId: lord.HouseId);
        var others = Enumerable.Range(0, 20).Select(_ => TestPeople.Add(world, 2, 15)).ToList();
        Make(world).AdvanceDays(360);
        Assert.False(heir.Faded);
        Assert.InRange(others.Count(o => o.Faded), 12, 20);
    }

    [Fact]
    public void Risks_IncludeFamineForTheYoung()
    {
        var world = TestPeople.World();
        world.Regions[1].Conditions[Conditions.Shortage] = 7;
        var child = TestPeople.Add(world, 1, 8);
        var risks = LifeSystem.Risks(child, world.Regions[1], world.Date);
        Assert.Contains(risks, r => r.Cause == "famine" && r.Citing == 7 && r.PerYear == 0.06);
    }
}
