using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class MapScreenTests
{
    [Fact]
    public void TheMap_ShowsHoldersOccupiersAndConflicts()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var siege = log.Append(world.Date, new EventDraft("RegionOccupied", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref, ["war"] = war.Ref },
            Place: world.Settlements[2].Ref, Importance: 30, Tags: new[] { "War" }));
        world.Regions[2].OccupierRealmId = 1;

        var map = MapScreen.Build(world, log);

        var held = map.Settlements.Single(s => s.Ref == world.Settlements[2].Ref);
        Assert.Equal(PinMarker.Conflict, held.Marker);
        Assert.Equal($"Held by the {Statecraft.Title(world.Realms[2])} since the founding, occupied by {world.Realms[1].Name}", held.Card.Title);
        Assert.StartsWith("Pop. ", held.Card.Line);
        Assert.Equal(world.Realms[1].Ref, map.Regions.Single(r => r.Ref == world.Regions[2].Ref).Occupier);
        var conflict = Assert.Single(map.Conflicts);
        Assert.Equal(new[] { world.Regions[2].Ref }, conflict.Occupied);
        Assert.Contains(map.Legend, l => l.Label == world.Realms[1].Name);
    }

    [Fact]
    public void EveryIndependentRealm_HasAStandard_AtItsCapital()
    {
        var world = TestRealms.TwoKingdoms();
        var map = MapScreen.Build(world, new EventLog());

        Assert.Equal(new[] { world.Realms[1].Ref, world.Realms[2].Ref }, map.Realms!.Select(r => r.Ref));
        var first = map.Realms![0];
        Assert.Equal((world.Realms[1].Name, world.Realms[1].Rank.ToString(), world.Realms[1].Colors[0]), (first.Name, first.Rank, first.Colour));
        Assert.Equal(MapPoint.Of(world, world.Settlements[world.Realms[1].CapitalId].Ref), first.Position);
        Assert.True(first.Regions >= 1);
    }

    [Fact]
    public void TheMapAtAPastDate_ShowsThePastState()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var ceded = sim.Log.All.First(e => e.Type == "RegionCeded");
        var before = MapScreen.Build(world, sim.Log, ceded.Date.AddDays(-1));
        var after = MapScreen.Build(world, sim.Log, ceded.Date);
        Assert.Equal(ceded.Actors["from"], before.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.Equal(ceded.Actors["to"], after.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.DoesNotContain("Pop.", before.Settlements[0].Card.Line);

        var now = MapScreen.Build(world, sim.Log);
        Assert.Equal(world.Date.Year, now.Year);
        Assert.Equal(world.Settlements.Values.Count(s => !s.Abandoned), now.Settlements.Count);
        Assert.All(now.Settlements, s => Assert.True(s.Position!.X is > 0 and < 1));
        Assert.All(now.Borders, b => Assert.NotEqual(now.Regions.Single(r => r.Ref == b.A).Owner, now.Regions.Single(r => r.Ref == b.B).Owner));
        Assert.Matches(@"^[A-Z][a-z]+ realms?, [a-z0-9]+ settlements?, [a-z0-9]+ open labyrinths?\.$", now.Summary);
    }

    [Fact]
    public void AWorldWithoutAFoundingRecord_ShowsTodaysMap_AtAnyDate()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new WorldDate(3600);
        var map = MapScreen.Build(world, new EventLog(), new WorldDate(10));
        Assert.False(map.Historical);
        Assert.Equal(world.Date.Year, map.Year);
    }

    [Fact]
    public void TemplesTowers_AndLabyrinthDepths_ShowToday_AndClearedLabyrinthsReplay()
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var r in world.Regions.Values) world.FoundingOwners[r.Id] = r.OwnerRealmId;
        var log = new EventLog();
        world.Settlements[1].Buildings.AddRange(new[] { "granary", "mage tower", "temple" });
        var opened = log.Append(world.Date, new EventDraft("LabyrinthOpened", Place: world.Regions[2].Ref));
        var maze = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Deeps", Type = StructureKind.Labyrinth, RegionId = 2, StartedEventId = opened.Id,
            CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing, Levels = 5, Depth = 2,
        };
        world.Structures[maze.Id] = maze;
        var guardian = new Monster { Id = world.Ids.Take(Monster.Kind), Name = "The Lich", Breed = "lich", RegionId = 2, Might = 60,
            State = MonsterState.Lairing, RoseEventId = opened.Id, LairStructureId = maze.Id };
        world.Monsters[guardian.Id] = guardian;
        maze.GuardianId = guardian.Id;

        var map = MapScreen.Build(world, log);
        Assert.Equal(new[] { "temple", "mage tower" }, map.Settlements.Single(s => s.Ref.Id == 1).Marks);
        var deeps = map.Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal(("Labyrinth", "level 2 of 5"), (deeps.Kind, deeps.Detail));
        Assert.True(map.Monsters!.Single().Guarding);

        world.Date = world.Date.AddDays(3600);
        var cleared = log.Append(world.Date, new EventDraft("LabyrinthCleared", Place: world.Regions[2].Ref));
        (maze.ClearedEventId, maze.Type, guardian.State, guardian.SlainEventId) = (cleared.Id, StructureKind.Shrine, MonsterState.Slain, cleared.Id);
        world.Date = world.Date.AddDays(360);
        var now = MapScreen.Build(world, log).Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal(("Shrine", "cleared"), (now.Kind, now.Detail));
        var then = MapScreen.Build(world, log, new WorldDate(360)).Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal("Labyrinth", then.Kind);
        Assert.Empty(MapScreen.Build(world, log, new WorldDate(360)).Settlements.Single(s => s.Ref.Id == 1).Marks!);
    }

    [Fact]
    public void GreatWorks_ReplayTheirCollapses_AndFortressesTheirBuilding()
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var r in world.Regions.Values) world.FoundingOwners[r.Id] = r.OwnerRealmId;
        var log = new EventLog();
        Structure Work(StructureKind kind, string begun)
        {
            var started = log.Append(world.Date, new EventDraft(begun, Place: world.Settlements[1].Ref));
            var work = new Structure { Id = world.Ids.Take(Structure.Kind), Name = $"The {kind}", Type = kind, RegionId = 1, SettlementId = 1, StartedEventId = started.Id };
            world.Structures[work.Id] = work;
            return work;
        }
        var spire = Work(StructureKind.Landmark, "LandmarkBegun");
        var walls = Work(StructureKind.Fortification, "FortificationBegun");
        world.Date = new WorldDate(720);
        log.Append(world.Date, new EventDraft("WorksCollapsed", Actors: new Dictionary<string, EntityRef> { ["structure"] = spire.Ref },
            Payload: new Dictionary<string, string> { ["ruined"] = "yes" }));
        var done = log.Append(world.Date, new EventDraft("FortificationCompleted", Actors: new Dictionary<string, EntityRef> { ["structure"] = walls.Ref }));
        (spire.State, walls.State, walls.CompletedEventId) = (StructureState.Ruined, StructureState.Standing, done.Id);
        world.Date = new WorldDate(1440);

        string At(Structure st, WorldDate? when) => MapScreen.Build(world, log, when).Structures.Single(s => s.Ref == st.Ref).State;
        Assert.Equal(("Ruined", "Standing"), (At(spire, null), At(walls, null)));
        Assert.Equal(("Building", "Building"), (At(spire, new WorldDate(360)), At(walls, new WorldDate(360))));
    }

    [Fact]
    public void Fights_AreMarked_WhileTheyGoOn_AndForAYearAfter()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var log = new EventLog();
        var (a, b) = (world.Characters.Values.First(), world.Characters.Values.Skip(1).First());
        var (r1, r2) = (world.Realms.Values.First(), world.Realms.Values.Last());
        var town = world.Settlements.Values.First();
        var duel = new Dictionary<string, EntityRef> { ["challenger"] = a.Ref, ["defender"] = b.Ref };
        var begun = log.Append(world.Date, new EventDraft("DuelBegun", Actors: duel, Place: town.Ref));
        var joined = log.Append(world.Date, new EventDraft("BattleJoined",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = r1.Ref, ["defender"] = r2.Ref }, Place: world.Regions[town.RegionId].Ref));
        var going = MapScreen.Build(world, log).Battles!;
        Assert.Equal(new[] { (begun.Id, MapBattle.Duel, true), (joined.Id, MapBattle.Battle, true) },
            going.Select(f => (f.EventId, f.Kind, f.UnderWay)));

        var fought = log.Append(world.Date, new EventDraft("DuelFought", Actors: duel, Place: town.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = "defender_wounds" }, Causes: new[] { new Cause(begun.Id, CauseKind.Trigger, 1) }));
        var after = MapScreen.Build(world, log).Battles!;
        var mark = Assert.Single(after, f => f.Kind == MapBattle.Duel);
        Assert.Equal((fought.Id, false, b.Ref), (mark.EventId, mark.UnderWay, mark.Victor));
        Assert.Contains(after, f => f.EventId == joined.Id && f.UnderWay);
    }
}
