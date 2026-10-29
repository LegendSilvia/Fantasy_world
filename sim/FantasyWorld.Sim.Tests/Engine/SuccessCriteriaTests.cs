using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>A medium world after its century of prehistory, shared by the success-criteria checks.</summary>
public sealed class PrehistoricWorld
{
    public PrehistoricWorld()
    {
        World = WorldGenerator.Generate(new WorldGenOptions(42, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        Sim = Prehistory.Run(World, Prehistory.DefaultYears);
    }

    public World World { get; }
    public Simulator Sim { get; }

    /// <summary>An independent deep copy (snapshots share entity objects, so round-trip through storage).</summary>
    public Simulator Fork()
    {
        var snapshot = Packed.Unpack<WorldSnapshot>(Packed.Pack(WorldSnapshot.Capture(World, Sim.Queue)))!;
        var world = snapshot.ToWorld();
        world.Geography = World.Geography;
        var log = new EventLog();
        foreach (var ev in Sim.Log.All) log.Restore(ev);
        return new Simulator(world, log, snapshot.ToQueue(), DefaultSystems.Create());
    }
}

/// <summary>Spec §11, success criteria for the sim core, checked on a real medium world (criteria 2 and 5 offline).</summary>
public class SuccessCriteriaTests(PrehistoricWorld fixture) : IClassFixture<PrehistoricWorld>
{
    private static int ChainLength(ChainNode node) => 1 + (node.Causes.Count == 0 ? 0 : node.Causes.Max(ChainLength));

    [Fact]
    public void Criterion1_ANewWorldHasACenturyOfHistory_AndHoldsItsInvariants()
    {
        Assert.Equal(Prehistory.DefaultYears + 1, fixture.World.Date.Year);
        Assert.Empty(Invariants.Check(fixture.World, fixture.Sim.Log));
        Assert.Contains(fixture.Sim.Log.All, e => e.Type == "WarDeclared");
        Assert.Contains(fixture.World.Structures.Values, s => s.Type == StructureKind.Landmark);
        Assert.Contains(fixture.World.Artifacts.Values, a => a.OriginEventId != 0);
    }

    [Fact]
    public void Criterion3_WarsTraceBackThroughLinkedCausesToRoots()
    {
        var query = new HistoryQuery(fixture.Sim.Log);
        var wars = fixture.Sim.Log.All.Where(e => e.Type == "WarDeclared").ToList();
        var deep = wars.Count(w => ChainLength(query.WhyChain(w.Id, 10)) >= 3);
        Assert.True(deep * 10 >= wars.Count * 9, $"{deep} of {wars.Count} wars have a why-chain of 3+ events");
        Assert.All(wars, w => Assert.NotEmpty(query.RootCauses(w.Id)));
    }

    [Fact]
    public void Criterion3_MythsTraceBackToTheirCauses()
    {
        // Sub-project 3 §10: raids lead back to the beast's rising, holy wars to what stirred the faithful.
        // A beast spilled from a labyrinth (the owner, 2026-10-29) leads back to its opening.
        var query = new HistoryQuery(fixture.Sim.Log);
        var raids = fixture.Sim.Log.All.Where(e => e.Type == "MonsterRaid").ToList();
        Assert.NotEmpty(raids);
        Assert.All(raids, r => Assert.Contains(query.RootCauses(r.Id), root => root.Type is "MonsterRose" or "LeySurge" or "DangerRose" or "LabyrinthOpened"));
        // Holy wars are checked on a longer history in MythHealthTests (a century may have none).
        Assert.All(fixture.Sim.Log.All.Where(e => e.Type == "HolyWarProclaimed"), h => Assert.NotEmpty(h.Causes));
    }

    [Fact]
    public void Criterion2_EightHoursAwayGiveADigestOfHeadlines()
    {
        var sim = fixture.Fork();
        var (copy, history) = (sim.World, sim.Log);
        var seen = history.NextId - 1;
        sim.AdvanceDays(120);

        var digest = Digest.Build(history, copy, seen, new HashSet<EntityRef>());
        Assert.InRange(digest.Entries.Count(), 3, 8);
        Assert.StartsWith("From ", new TemplateNarrator(copy).Digest(digest));
    }

    [Fact]
    public void Compaction_KeepsEveryWarExplainable()
    {
        var sim = fixture.Fork();
        var (world, log) = (sim.World, sim.Log);
        var before = log.All.Where(e => e.Type == "WarDeclared").ToDictionary(e => e.Id, e => ChainLength(new HistoryQuery(log).WhyChain(e.Id, 10)));

        Assert.NotEmpty(Compactor.Compact(world, log, sim.Queue, 1, world.Date.Year - Compactor.AgeYears - 1).Forgotten);

        Assert.Empty(Invariants.Check(world, log));
        foreach (var (id, length) in before)
            Assert.Equal(length, ChainLength(new HistoryQuery(log).WhyChain(id, 10)));
    }

    [Fact]
    public void Armies_MeetEachOther_RatherThanTradePlacesForever()
    {
        // A defender that only chased lost regions and an invader that only took the next one used to swap places
        // monthly for decades without a battle, freeing and retaking the same two regions.
        // A real campaign may take and lose a region several times, but each swing is fought for: a won battle may free
        // a whole sweep of regions at once, but no one region changes hands again and again between battles.
        var log = fixture.Sim.Log.All;
        var battles = log.Where(e => e.Type == "BattleFought" && e.Actors.ContainsKey("war")).GroupBy(e => e.Actors["war"]).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(log.Where(e => e.Type == "RegionLiberated" && e.Actors.ContainsKey("war")).GroupBy(e => (War: e.Actors["war"], e.Place)),
            region => Assert.True(region.Count() <= battles.GetValueOrDefault(region.Key.War) + 2,
                $"{region.Key.War}: {region.Key.Place} freed {region.Count()} times but {battles.GetValueOrDefault(region.Key.War)} battles"));
        Assert.Contains(log, e => e.Type == "BattleFought");
    }

    [Fact]
    public void Battles_AreFoughtByPeople_WhoCarryThemIntoTheirLives()
    {
        var log = fixture.Sim.Log;
        var battles = log.All.Where(e => e.Type == "BattleFought").ToList();
        Assert.Contains(battles, b => b.Actors.Keys.Any(k => k.Contains("knight")));
        var glory = log.All.Where(e => e.Type == "DistinguishedInBattle").ToList();
        Assert.NotEmpty(glory);
        Assert.All(glory, g => Assert.Equal("BattleFought", log.Get(g.Causes[0].EventId).Type));
        Assert.All(log.All.Where(e => e.Type == "WoundedInBattle"), w => Assert.Contains(w.Payload["wound"], new[] { "wounded", "maimed" }));
        // A knight named on a battle finds it in their own history.
        var knight = battles.SelectMany(b => b.Actors.Where(a => a.Key.Contains("knight")).Select(a => (a.Value, b.Id))).First();
        Assert.Contains(log.Involving(knight.Value), e => e.Id == knight.Id);
    }
}
