using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class GreatWorksTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem() }, checkInvariants: true);

    /// <summary>Realm 1's capital as a rich town with a learned notable who is not its lord.</summary>
    private static (World World, Settlement Seat, Character Lord, Character Architect) RichTown(params string[] lordTraits)
    {
        var world = TestRealms.TwoKingdoms();
        var seat = world.Settlements[1];
        (seat.Tier, seat.Wealth) = (SettlementTier.Town, 1_000_000);
        var lord = world.Characters[seat.LordId];
        lord.Traits.AddRange(lordTraits);
        var architect = TestPeople.Add(world, 1, 35);
        architect.Attributes.Lore = architect.Attributes.Stewardship = 18;
        return (world, seat, lord, architect);
    }

    private static Structure Staged(World world, EventLog log, Settlement seat, Character lord, bool rushed = false, double progress = 0)
    {
        var begun = log.Append(world.Date, new EventDraft("LandmarkBegun", Place: seat.Ref));
        var work = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Spire", Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building, Rushed = rushed, Progress = progress,
        };
        world.Structures[work.Id] = work;
        return work;
    }

    [Fact]
    public void AGreatWork_HasAnArchitect_AndItsCompletionSteadiesTheRealm()
    {
        var (world, _, _, architect) = RichTown();
        world.Realms[1].Stability = 50;
        var sim = Make(world);
        sim.AdvanceDays(360 * 80);
        var done = sim.Log.All.First(e => e.Type == "LandmarkCompleted");
        Assert.Equal(architect.Ref, done.Actors["architect"]);
        Assert.Equal(architect.Id, world.Structures[done.Actors["structure"].Id].ArchitectId);
        Assert.True(architect.Renown >= 8);
        Assert.True(world.Realms[1].Stability > 50);
    }

    [Fact]
    public void Plague_HoldsTheWorkUp_AndSaysSoOnce()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var work = Staged(world, log, seat, lord, progress: 40);
        var plague = log.Append(world.Date, new EventDraft("PlagueOutbreak", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.Plague] = plague.Id;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        var delayed = sim.Log.All.Single(e => e.Type == "WorksDelayed");
        Assert.Contains(delayed.Causes, c => c.EventId == plague.Id);
        Assert.Equal(40, work.Progress);
        Assert.Equal(plague.Id, work.DelayedByEventId);
    }

    [Fact]
    public void ARushedWork_MayCollapse_AndTheArchitectIsBlamed()
    {
        var (world, seat, lord, architect) = RichTown(Traits.Ambitious);
        var log = new EventLog();
        architect.Renown = 30;
        for (var i = 0; i < 40; i++) Staged(world, log, seat, lord, rushed: true).ArchitectId = architect.Id;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 2);
        var fall = sim.Log.All.First(e => e.Type == "WorksCollapsed");
        var work = world.Structures[fall.Actors["structure"].Id];
        Assert.Contains(fall.Causes, c => c.EventId == work.StartedEventId);
        Assert.Equal(architect.Ref, fall.Actors["architect"]);
        Assert.True(architect.Renown < 30);
    }

    [Fact]
    public void ATownThatKnewSiege_RaisesAFortress_ThatStrengthensItsDefence()
    {
        var (world, seat, _, _) = RichTown();
        var log = new EventLog();
        var siege = log.Append(world.Date, new EventDraft("SiegeEnded", Place: seat.Ref));
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 150);
        var begun = sim.Log.All.First(e => e.Type == "FortificationBegun");
        Assert.Contains(begun.Causes, c => c.EventId == siege.Id);
        Assert.Contains(sim.Log.All, e => e.Type == "FortificationCompleted");
        Assert.True(StructureSystem.Fortified(world, seat));
    }

    [Fact]
    public void AFallenLandmark_IsRestored_ByAWealthyLord()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var work = Staged(world, log, seat, lord);
        var fell = log.Append(world.Date, new EventDraft("WorksCollapsed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref));
        work.State = StructureState.Ruined;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 60);
        var restored = sim.Log.All.First(e => e.Type == "RestorationBegun");
        Assert.Equal(work.Ref, restored.Actors["structure"]);
        Assert.Contains(restored.Causes, c => c.EventId == fell.Id);
        Assert.NotEqual(StructureState.Ruined, work.State);
    }

    [Fact]
    public void AnUnfinishedFortress_FallsWithItsAbandonedTown()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var begun = log.Append(world.Date, new EventDraft("FortificationBegun", Place: seat.Ref));
        var walls = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Walls", Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building, Progress = 90,
        };
        world.Structures[walls.Id] = walls;
        foreach (var c in world.Characters.Values.Where(c => c.SettlementId == seat.Id)) c.SettlementId = 2;
        (seat.LordId, seat.Abandoned, lord.Role) = (0, true, Role.None);
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        Assert.Equal(StructureState.Ruined, walls.State);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "FortificationCompleted");
    }
}
