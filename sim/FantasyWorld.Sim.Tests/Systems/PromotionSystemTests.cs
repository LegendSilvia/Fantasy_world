using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class PromotionSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new PromotionSystem() }, checkInvariants: true);

    [Fact]
    public void VacantSeat_IsFilled_CitingTheDeath()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var death = log.Append(world.Date, new EventDraft("CharacterDied"));
        var town = world.Settlements[1];
        town.Tier = SettlementTier.Town;
        town.Conditions[Fates.VacantSeat] = death.Id;
        var sim = Make(world, log);

        sim.AdvanceDays(30);

        var rose = Assert.Single(sim.Log.All, e => e.Type == "NewLordRose");
        Assert.Equal(death.Id, rose.Causes.Single().EventId);
        var lord = world.Characters[town.LordId];
        Assert.Equal(Role.Lord, lord.Role);
        Assert.Equal(town.Id, world.Houses[lord.HouseId].SeatSettlementId);
        Assert.False(town.Conditions.ContainsKey(Fates.VacantSeat));
    }

    [Fact]
    public void AbleResident_IsPreferredAsLord()
    {
        var world = TestPeople.World();
        world.Settlements[1].Tier = SettlementTier.Town;
        var steward = TestPeople.Add(world, 1, 40, role: Role.Merchant);
        steward.Attributes.Stewardship = 20;
        TestPeople.Add(world, 1, 40);
        Make(world).AdvanceDays(30);
        Assert.Equal(steward.Id, world.Settlements[1].LordId);
        Assert.Equal(Role.Lord, steward.Role);
    }

    [Fact]
    public void Revolt_FindsALeader()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var revolt = log.Append(world.Date, new EventDraft("RevoltBegan"));
        world.Settlements[2].Conditions[Conditions.Revolt] = revolt.Id;
        var before = world.Settlements[2].Adults;
        var sim = Make(world, log);

        sim.AdvanceDays(60);

        var rose = Assert.Single(sim.Log.All, e => e.Type == "RebelLeaderRose");
        Assert.Equal(revolt.Id, rose.Causes.Single().EventId);
        Assert.Equal(Role.Rebel, world.Characters[rose.Actors["character"].Id].Role);
        Assert.Equal(before - 1, world.Settlements[2].Adults);
    }

    [Fact]
    public void AbandonedSettlement_IsEvacuated_AndItsLordUnseated()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var friend = TestPeople.Add(world, 1, 30);
        var ruin = world.Settlements[1];
        ruin.Abandoned = true;
        var abandoned = log.Append(world.Date, new EventDraft("SettlementAbandoned", Place: ruin.Ref));
        var sim = Make(world, log);

        sim.AdvanceDays(30);

        Assert.Equal(2, lord.SettlementId);
        Assert.Equal(2, friend.SettlementId);
        Assert.Equal(Role.None, lord.Role);
        Assert.Equal(0, ruin.LordId);
        var fled = Assert.Single(sim.Log.All, e => e.Type == "NotablesFled");
        Assert.Equal(abandoned.Id, fled.Causes.Single().EventId);
    }

    [Fact]
    public void DangerousRegion_RaisesAHero()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var rose = log.Append(world.Date, new EventDraft("DangerRose"));
        world.Regions[2].Conditions[Conditions.Dangerous] = rose.Id;
        var sim = Make(world, log);

        sim.AdvanceDays(360);

        var hero = Assert.Single(sim.Log.All, e => e.Type == "HeroRose");
        Assert.Equal(rose.Id, hero.Causes.Single().EventId);
        Assert.Equal(Ambitions.Renown, world.Characters[hero.Actors["character"].Id].Ambition);
    }
}
