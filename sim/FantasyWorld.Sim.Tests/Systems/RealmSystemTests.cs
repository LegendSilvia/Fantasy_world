using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class RealmSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new RealmSystem() }, checkInvariants: true);

    [Fact]
    public void Taxes_FillTheTreasury_AndVassalsPayTribute()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[2].LiegeId = 1;
        var (liege, vassal) = (world.Realms[1].Treasury, world.Realms[2].Treasury);
        Make(world).AdvanceDays(30);
        var vassalIncome = world.Settlements[2].Wealth / 0.99 * 0.01;
        Assert.True(world.Realms[1].Treasury > liege + vassalIncome * 0.19);
        Assert.Equal(vassal + vassalIncome * 0.8, world.Realms[2].Treasury, 6);
    }

    [Fact]
    public void Duchies_FillTheirCouncil_WithTheAblest()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].Rank = RealmRank.Duchy;
        var general = TestPeople.Add(world, 1, 40, role: Role.Knight);
        general.Attributes.Martial = 20;
        var clerk = TestPeople.Add(world, 1, 40);
        clerk.Attributes.Stewardship = 20;
        var sim = Make(world);
        sim.AdvanceDays(30);
        Assert.Equal(general.Id, world.Realms[1].Council[CouncilSeats.Marshal]);
        Assert.Equal(clerk.Id, world.Realms[1].Council[CouncilSeats.Steward]);
        Assert.Contains(sim.Log.All, e => e.Type == "CouncilAppointed" && e.Payload["seat"] == CouncilSeats.Marshal);
    }

    [Fact]
    public void DeadCouncillors_LeaveTheirSeat()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var marshal = TestPeople.Add(world, 1, 40);
        world.Realms[1].Council[CouncilSeats.Marshal] = marshal.Id;
        Fates.Die(new SimContext(world, log, new EventQueue(), new Rng(1)), marshal, "illness", Array.Empty<Cause>());
        Assert.False(world.Realms[1].Council.ContainsKey(CouncilSeats.Marshal));
    }

    [Fact]
    public void LostCapital_Moves_OrTheRealmFalls()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[2].LiegeId = 1;
        var extra = TestWorlds.AddSettlement(world, 1, SettlementTier.Village, 400);
        world.Settlements[1].Abandoned = true;
        var exiled = world.Characters[world.Settlements[1].LordId];
        exiled.Role = Role.None;
        exiled.SettlementId = extra.Id;
        world.Settlements[1].LordId = 0;
        var sim = Make(world);

        sim.AdvanceDays(30);
        Assert.Equal(extra.Id, world.Realms[1].CapitalId);
        Assert.Single(sim.Log.All, e => e.Type == "CapitalMoved");

        world.Regions[1].OwnerRealmId = 2;
        sim.AdvanceDays(30);
        Assert.True(world.Realms[1].Destroyed);
        Assert.Equal(0, world.Realms[2].LiegeId);
        Assert.Single(sim.Log.All, e => e.Type == "RealmFell");
    }

    [Fact]
    public void ARealmWithLandLeftButNoTown_FallsOnce_AndItsLandPassesOn()
    {
        // Each cession of a falling realm's leftover land used to re-check its capital and make it fall again.
        var world = TestRealms.TwoKingdoms();
        var wastes = new[] { TestWorlds.AddRegion(world, "Ashwaste", 0.9), TestWorlds.AddRegion(world, "Saltwaste", 0.9) };
        foreach (var w in wastes) w.OwnerRealmId = 1;
        world.Geography = new Geography(new TerrainGrid(4, 1), new[] { 1, 2, 3, 4 }, new RegionGraph(new[]
        {
            new RegionLink(1, 2, LinkKind.Land, 3, 10, false, false), new RegionLink(2, 3, LinkKind.Land, 3, 10, false, false),
            new RegionLink(2, 4, LinkKind.Land, 3, 10, false, false),
        }));
        world.Regions[1].OwnerRealmId = 2;
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        Assert.False(Statecraft.SecureCapital(ctx, world.Realms[1], Array.Empty<Cause>()));
        Assert.Single(ctx.Log.All, e => e.Type == "RealmFell");
        Assert.All(wastes, w => Assert.Equal(2, w.OwnerRealmId));
    }

    [Fact]
    public void Interregnum_CollapsesLegitimacy_CitingTheEmptySeat()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var lord = world.Characters[world.Settlements[1].LordId];
        var death = Fates.Die(new SimContext(world, log, new EventQueue(), new Rng(1)), lord, "illness", Array.Empty<Cause>());
        var sim = Make(world, log);

        sim.AdvanceDays(360 * 8);

        var collapse = Assert.Single(sim.Log.All, e => e.Type == "LegitimacyCollapsed");
        Assert.Equal(death.Id, collapse.Causes.Single().EventId);
        Assert.True(world.Realms[1].Stability < 70);
    }

    [Fact]
    public void Vassal_OfACollapsingLiege_DeclaresIndependence()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Realms[2].LiegeId = 1;
        world.Realms[1].Conditions[Statecraft.StabilityCollapsed] = log.Append(world.Date, new EventDraft("StabilityCollapsed")).Id;
        world.Realms[1].Stability = 10;
        var sim = Make(world, log);

        sim.AdvanceDays(360 * 10);

        var declared = Assert.Single(sim.Log.All, e => e.Type == "IndependenceDeclared");
        Assert.Equal(0, world.Realms[2].LiegeId);
        Assert.Contains(world.Realms[1].Memories, m => m.EventId == declared.Id && m.About == 2);
    }

    [Fact]
    public void GrowingRealm_BeginsAnEra()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].RegionsAtLastReview = 1;
        world.Regions[2].OwnerRealmId = 1;
        world.Realms[2].Destroyed = true;
        var sim = Make(world);
        sim.AdvanceDays(360);
        var era = Assert.Single(sim.Log.All, e => e.Type == "EraBegan");
        Assert.Equal("Expanding", era.Payload["phase"]);
        Assert.EndsWith("Age of " + world.Realms[1].Name, era.Payload["era"]);
        Assert.Equal(RealmPhase.Expanding, world.Realms[1].Phase);
    }

    [Fact]
    public void CouncilMembers_RaisedToALordship_StepDown()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].Rank = RealmRank.Duchy;
        var general = TestPeople.Add(world, 1, 40, role: Role.Knight);
        general.Attributes.Martial = 20;
        var sim = Make(world);
        sim.AdvanceDays(30);
        Assert.Equal(general.Id, world.Realms[1].Council[CouncilSeats.Marshal]);
        var seat = world.Settlements[2];
        world.Characters[seat.LordId].Role = Role.None;
        (seat.LordId, general.SettlementId, general.Role) = (general.Id, seat.Id, Role.Lord);
        sim.AdvanceDays(31);
        Assert.NotEqual(general.Id, world.Realms[1].Council.GetValueOrDefault(CouncilSeats.Marshal));
    }

    [Fact]
    public void VillageCapitals_GetANewRuler()
    {
        var world = TestRealms.TwoKingdoms();
        var capital = world.Settlements[1];
        Assert.Equal(SettlementTier.Village, capital.Tier);
        world.Characters[capital.LordId].Role = Role.None;
        capital.LordId = 0;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new PromotionSystem() }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.NotNull(Realms.RulerOf(world, world.Realms[1]));
        Assert.Contains(sim.Log.All, e => e.Type == "NewLordRose");
    }
}
