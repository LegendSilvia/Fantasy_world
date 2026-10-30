using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class LabyrinthTests
{
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    /// <summary>A three-level labyrinth in region 2, its guardian, and a relic lost on its deepest level.</summary>
    private static (Structure Maze, Monster Guardian, Artifact Relic) Stage(World world, EventLog log, Character hero, bool aired = true)
    {
        // A labyrinth's bottom is reached only once the chronicle has aired (its secret waits for the audience).
        if (aired) world.LastBroadcastDate = "2026-10-29";
        var opened = log.Append(world.Date, new EventDraft("LabyrinthOpened", Place: world.Regions[2].Ref));
        var maze = new Structure
        {
            Id = world.Ids.Take(Structure.Kind),
            Name = "The Deeps of Eastmoor",
            Type = StructureKind.Labyrinth,
            RegionId = 2,
            StartedEventId = opened.Id,
            CompletedEventId = opened.Id,
            Progress = 100,
            State = StructureState.Standing,
            Levels = 3,
        };
        world.Structures[maze.Id] = maze;
        var guardian = new Monster
        {
            Id = world.Ids.Take(Monster.Kind),
            Name = "The Minotaur",
            Breed = "minotaur",
            RegionId = 2,
            Might = 50,
            State = MonsterState.Lairing,
            RoseEventId = opened.Id,
            LairStructureId = maze.Id,
        };
        world.Monsters[guardian.Id] = guardian;
        maze.GuardianId = guardian.Id;
        var relic = TestPeople.GiveArtifact(world, hero); // once theirs, lost below
        Fates.Lose(relic, 2, opened.Id, hero.Id);
        world.Regions[2].Danger = 60;
        return (maze, guardian, relic);
    }

    private static Character Delver(World world)
    {
        var hero = TestPeople.Add(world, 1, 25, role: Role.Adventurer, traits: new[] { Traits.Devout });
        hero.Ambition = Ambitions.Artifact;
        return hero;
    }

    [Fact]
    public void ALabyrinth_OpensWithLevels_AndAGuardianThatKeepsToIt()
    {
        var world = TestPeople.World();
        var wild = world.Regions[2];
        (wild.LeyPower, wild.Danger) = (90, 50);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem(), new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 150);
        var opened = sim.Log.All.Single(e => e.Type == "LabyrinthOpened");
        var maze = world.Structures[opened.Actors["structure"].Id];
        Assert.InRange(maze.Levels, 3, 7);
        var guardian = world.Monsters[maze.GuardianId];
        Assert.Equal(guardian.Ref, opened.Actors["guardian"]);
        Assert.Equal((MonsterState.Lairing, maze.Id, wild.Id), (guardian.State, guardian.LairStructureId, guardian.RegionId));
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "MonsterRaid" && e.Actors["monster"] == guardian.Ref);
        Assert.True(wild.Danger >= guardian.Might / 2);
    }

    [Fact]
    public void BeforeTheChronicleAirs_NoDelverReachesTheBottom()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hero = Delver(world);
        Stage(world, log, hero, aired: false);
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 8);

        Assert.Equal(new[] { "1", "2" }, sim.Log.All.Where(e => e.Type == "LabyrinthDelved").Select(e => e.Payload["level"]));
        Assert.DoesNotContain(sim.Log.All, e => e.Type is "LabyrinthCleared" or "SecretRevealed");
        Assert.Empty(world.Secrets);
    }

    [Fact]
    public void Delvers_GoDownLevelByLevel_SlayTheGuardian_AndClearIt()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hero = Delver(world);
        var (maze, guardian, relic) = Stage(world, log, hero);
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 8);

        Assert.Equal(new[] { "1", "2", "3" }, sim.Log.All.Where(e => e.Type == "LabyrinthDelved").Select(e => e.Payload["level"]));
        var slain = sim.Log.All.Single(e => e.Type == "MonsterSlain");
        Assert.Equal(guardian.Ref, slain.Actors["monster"]);
        Assert.Equal(MonsterState.Slain, guardian.State);
        Assert.Equal((ArtifactPlace.Held, hero.Id), (relic.Place, relic.HolderId));
        var cleared = sim.Log.All.Single(e => e.Type == "LabyrinthCleared");
        Assert.Equal(cleared.Id, maze.ClearedEventId);
        Assert.Equal((StructureKind.Shrine, StructureState.Standing), (maze.Type, maze.State));
        Assert.True(world.Regions[2].Danger < 60);
    }

    [Fact]
    public void AFailedDelve_GoesNoDeeper()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var (maze, _, _) = Stage(world, log, Delver(world));
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("failure_wounded")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 6);
        Assert.Contains(sim.Log.All, e => e.Type == "LabyrinthDelveFailed" && e.Payload["level"] == "1");
        Assert.Equal(0, maze.Depth);
        Assert.Equal(0, maze.ClearedEventId);
    }

    [Fact]
    public void ADelver_TakesTheTreasureOfEachLevelReached()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hero = Delver(world);
        var (maze, _, _) = Stage(world, log, hero);
        var second = TestPeople.GiveArtifact(world, hero);
        Fates.Lose(second, 2, maze.StartedEventId, 0);
        second.Level = 2;
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 8);

        var found = sim.Log.All.First(e => e.Type == "ArtifactFound");
        Assert.Equal((second.Ref, "2"), (found.Actors["artifact"], found.Payload["level"]));
        Assert.Equal((ArtifactPlace.Held, hero.Id, 0), (second.Place, second.HolderId, second.Level));
        Assert.True(found.Id < sim.Log.All.Single(e => e.Type == "MonsterSlain").Id);
    }
}
