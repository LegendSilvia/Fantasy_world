# World & Regions Implementation Plan (Plan 2 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Plan 1's single-number demo with a real map. Generate a deterministic world from a seed: a 256×256 terrain grid with rivers, 100–120 regions joined by a travel graph, and 1–4 settlements per region. Then make it live: a regional food economy, natural region events (drought, flood, blight, plague, ley surge, earthquake, monsters, discoveries) and settlement unrest, revolts and migration. Every notable event cites the conditions that caused it.

**Architecture:** World generation is a pipeline of pure functions in `FantasyWorld.Sim.WorldGen`: terrain → hydrology → region partition → region graph → region profiles → settlements. Each step draws from its own seeded stream. Its output is a static `Geography` (tiles, region map, graph; written to the world file once) plus mutable `Region`/`Settlement` entities in the snapshot. Three systems replace the demo pair, in fixed order: `EconomySystem` → `RegionSystem` → `SettlementSystem`. Systems still never call each other. They share state through entity fields and `Conditions`, which map a condition name (`drought`, `shortage`, `plague`, …) to the event that began it. That mapping is how a later event cites the condition behind it, so `fw why` on a revolt walks back through the shortage to the drought.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite 8.0.10, System.Text.Json (unchanged from Plan 1).

**Spec:** `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` (§5, §7, §9, §10). **Builds on:** Plan 1 (`docs/superpowers/plans/2026-10-05-sim-foundation.md`), all 12 tasks merged.

## Global Constraints

Everything in Plan 1's Global Constraints still applies. In addition:

- **Platform-stable math:** simulation and world-generation code uses only `+ − × ÷`, `Math.Sqrt`, `Math.Floor`, `Math.Round`, `Math.Min/Max/Clamp/Abs`. **No `Math.Pow`, `Exp`, `Log`, `Sin`, `Cos`**: they are not guaranteed bit-identical across CPUs and runtimes, and would break `World = f(seed)`.
- **Ordering:** iterate entities via `SortedDictionary<long, …>` (`World.Regions`, `World.Settlements`) or other id-ordered lists. Never iterate a `Dictionary`/`HashSet` in a way that changes results.
- **Ordinal dictionaries survive JSON:** any `SortedDictionary<string, …>` property on an entity re-wraps with `Sorted.From` in its `init` accessor, so deserialized copies keep `StringComparer.Ordinal`.
- **Causes through conditions:** a system that begins a lasting condition stores `Conditions[name] = eventId`. Systems that react cite it via `Conditions.Cite(...)`. Condition names live in `Systems/Conditions.cs`.
- **Odds are injectable** where events are random (`RegionOdds`, `SettlementSystem(revoltChancePerMonth)`), so tests force or silence events instead of searching for lucky seeds.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

The spec leaves these open. Each choice below is the drafted default; change it before executing if you disagree.

| Topic | Drafted choice | Why |
|---|---|---|
| Map size | 256×256 for medium and large; large differs only in regions (120 vs 100). `tiny` = 64×64, 12 regions, for tests/demos | Spec says ~256×256; keeps one rendering size for Godot |
| World shape | One main continent with a falloff to sea at the edges (fits the floating diorama block); small islands sunk | Spec §1 camera; islands below a region's worth of land are noise |
| Lakes | None: inland water below sea level is `Ocean`; depressions are filled | Simplest drainage that always reaches the sea; lakes can come with Plan 3 structures |
| Sea travel | Sea lanes only where needed to connect every region | Plan 4 (armies, trade pacts) can add more |
| Food | Pooled per **region**, not per settlement | Cities can't feed themselves; this avoids needing trade in this plan |
| Trade between regions | Deferred (Plan 4, trade pacts) | Not needed for a stable world; see soak results below |
| Cultures & faith | Deferred to Plan 3 (characters) / Plan 4 (realms) | No consumer yet |
| Founding new settlements | Deferred: settlements only grow, decline or die out | Needs characters/realms to decide who founds them |
| Names | Rule-based syllable names (`NameGen`), unique per world | LLM invention (Plan 5) may rename; this is the offline fallback |
| Starting population | Cities only where region farmland ≥ 270; every region starts at ≤ 80% of farm capacity | Without this, cities famine-crash in year one (seen while prototyping) |
| Plague | 4%/month per land link to spread, 90–180 days long, 10 years' immunity after | Higher spread made plague endemic and emptied the world (186 of 192 settlements abandoned in 50 years) |

**Prototype soak (medium world, seed 7, 100 years, Release):** population 166k → 180k; 6 cities stable; about 16 events per year across 20+ types (ley surges, buildings, plague waves, droughts, blights, floods, discoveries, revolts, shortages, earthquakes, exoduses, monster migrations); 1 settlement abandoned; about 15 ms per world year. The code in this plan was run task by task in this order: each task's tests fail first and pass after the implementation, with the counts given.

## Roadmap (unchanged)

| Plan | Delivers |
|---|---|
| 3 — Characters & artifacts | notables, populace promotion, traits, memories/opinions, relationships, aging, death & inheritance, artifacts; cultures & faiths |
| 4 — Realms & armies | realm hierarchy, government, succession, legitimacy, diplomacy, casus belli, armies, battles, sieges, war score, eras; trade between regions; sea lanes for fleets |
| 5 — LLM layer | `ILlmProvider` (Anthropic C# SDK, OpenRouter, Fake), decisions, arbitration, invention (incl. renaming places), narration, budgets, cost ledger |
| 6 — Chronicle & hardening | importance + digest, prehistory run, live-mode host loop, event compaction, snapshot compression, rolling backups, history-health soak, BenchmarkDotNet |

## File Structure

```
sim/
  FantasyWorld.Sim/
    Core/IdGen.cs                         per-kind entity ids                         (Task 1)
    Core/StableHash.cs                    + Mix, Derive                               (Task 2, modify)
    Core/Rng.cs                           reuse StableHash.Mix                        (Task 2, modify)
    Engine/ISimSystem.cs, Simulator.cs    + yearly tick                               (Task 1, modify)
    Engine/Invariants.cs                  + region/settlement invariants              (Task 7, modify)
    Geo/TileCoord.cs, Biome.cs, TerrainGrid.cs, Noise2D.cs                            (Task 2)
    WorldGen/TerrainGenerator.cs                                                      (Task 3)
    WorldGen/Hydrology.cs                                                             (Task 4)
    WorldGen/RegionPartitioner.cs         RegionLayout + partition                    (Task 5)
    WorldGen/RegionGraphBuilder.cs                                                    (Task 6)
    WorldGen/NameGen.cs, RegionProfiler.cs                                            (Task 8)
    WorldGen/SettlementPlacer.cs                                                      (Task 9)
    WorldGen/WorldGenerator.cs            WorldSize, WorldGenOptions                  (Task 10)
    Model/RegionGraph.cs                                                              (Task 6)
    Model/Region.cs, Settlement.cs, Demography.cs, Geography.cs, World.cs             (Task 7; Demography also Task 12)
    Model/ResourceKinds.cs                                                            (Task 8)
    Persist/WorldSnapshot.cs, WorldHasher.cs, WorldStore.cs                           (Task 11, modify)
    Systems/Conditions.cs, EconomySystem.cs                                           (Task 12)
    Systems/RegionSystem.cs                                                           (Task 13)
    Systems/SettlementSystem.cs                                                       (Task 14)
    Systems/DefaultSystems.cs             economy → regions → settlements             (Task 15, modify)
    Systems/Demo/*                        deleted                                     (Task 15)
    Query/EventText.cs                    + entity names                              (Task 15, modify)
  FantasyWorld.Cli/
    CliApp.cs                             + --size, map, regions, region              (Task 15, modify)
    MapRenderer.cs                                                                    (Task 15)
  FantasyWorld.Sim.Tests/
    Core/IdGenTests.cs, Geo/*, WorldGen/*, Model/*, Systems/*, Engine/WorldHealthTests.cs
    Support/TestWorlds.cs                 hand-built two-region world                 (Tasks 7, 13)
```

All commands run from the repository root unless stated. Requires the .NET 8 SDK (on Linux: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0`, then add `~/.dotnet` to `PATH`).

---

### Task 1: Yearly tick and entity ids

Population growth, tier changes and migration happen once a year (spec §7 "Annual tick"), and every new entity needs a stable id that survives save/load.

**Files:**
- Create: `sim/FantasyWorld.Sim/Core/IdGen.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/ISimSystem.cs`, `sim/FantasyWorld.Sim/Engine/Simulator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Core/IdGenTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Engine/SimulatorTests.cs`

**Interfaces:**
- Consumes: `ISimSystem`, `Simulator` (Plan 1); `Sorted` (Plan 1).
- Produces: `ISimSystem.OnYearlyTick(SimContext ctx)` (default no-op), called on the first day of each year after every system's monthly tick, with RNG phase `"year"`; `sealed class IdGen { SortedDictionary<string,long> Next; long Take(string kind); long Peek(string kind) }` (ids start at 1 per kind; `Next` is re-sorted ordinally on deserialization).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Core/IdGenTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class IdGenTests
{
    [Fact]
    public void Take_CountsPerKindFromOne()
    {
        var ids = new IdGen();
        Assert.Equal(1, ids.Take("region"));
        Assert.Equal(2, ids.Take("region"));
        Assert.Equal(1, ids.Take("settlement"));
        Assert.Equal(3, ids.Peek("region"));
    }

    [Fact]
    public void JsonRoundTrip_ContinuesTheSequence()
    {
        var ids = new IdGen();
        ids.Take("region");
        ids.Take("region");
        var copy = JsonSerializer.Deserialize<IdGen>(JsonSerializer.Serialize(ids))!;
        Assert.Equal(3, copy.Take("region"));
        Assert.Same(StringComparer.Ordinal, copy.Next.Comparer);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Engine/SimulatorTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Engine;

public class SimulatorTests
{
    private sealed class RecordingSystem : ISimSystem
    {
        private readonly List<string> _trace;
        public RecordingSystem(string name, List<string> trace, params string[] kinds)
        {
            Name = name;
            _trace = trace;
            HandledKinds = kinds;
        }
        public string Name { get; }
        public IEnumerable<string> HandledKinds { get; }
        public void OnMonthlyTick(SimContext ctx) => _trace.Add($"{Name}@{ctx.Date.Day}");
        public void OnScheduled(SimContext ctx, ScheduledAction action) => _trace.Add($"{Name}:{action.Kind}@{ctx.Date.Day}");
    }

    private sealed class YearlySystem : ISimSystem
    {
        private readonly List<string> _trace;
        public YearlySystem(List<string> trace) => _trace = trace;
        public string Name => "yearly";
        public void OnMonthlyTick(SimContext ctx) => _trace.Add($"month@{ctx.Date.Day}");
        public void OnYearlyTick(SimContext ctx) => _trace.Add($"year@{ctx.Date.Day}");
    }

    private static Simulator Make(params ISimSystem[] systems) =>
        new(WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(), systems, checkInvariants: true);

    [Fact]
    public void MonthlyTick_FiresOnFirstOfEachMonth_InRegistrationOrder()
    {
        var trace = new List<string>();
        var sim = Make(new RecordingSystem("a", trace), new RecordingSystem("b", trace));
        sim.AdvanceDays(90);
        Assert.Equal(new[] { "a@30", "b@30", "a@60", "b@60", "a@90", "b@90" }, trace);
        Assert.Equal(90, sim.World.Date.Day);
    }

    [Fact]
    public void ScheduledActions_RunOnTheirDate_AfterMonthlyTick()
    {
        var trace = new List<string>();
        var sim = Make(new RecordingSystem("a", trace, "ping"));
        sim.Queue.Schedule(new WorldDate(30), "ping");
        sim.Queue.Schedule(new WorldDate(5), "ping");
        sim.AdvanceDays(30);
        Assert.Equal(new[] { "a:ping@5", "a@30", "a:ping@30" }, trace);
    }

    [Fact]
    public void YearlyTick_FiresOnFirstOfYear_AfterMonthlyTicks()
    {
        var trace = new List<string>();
        var sim = Make(new YearlySystem(trace));
        sim.AdvanceDays(720);
        Assert.Equal(new[] { "month@360", "year@360" }, trace.Where(t => t.EndsWith("@360")));
        Assert.Equal(2, trace.Count(t => t.StartsWith("year@")));
        Assert.Equal(24, trace.Count(t => t.StartsWith("month@")));
    }

    [Fact]
    public void UnknownScheduledKind_Throws()
    {
        var sim = Make();
        sim.Queue.Schedule(new WorldDate(1), "nobody");
        var ex = Assert.Throws<InvalidOperationException>(() => sim.AdvanceDays(1));
        Assert.Contains("nobody", ex.Message);
    }

    [Fact]
    public void DuplicateKindHandlers_AreRejected()
    {
        var trace = new List<string>();
        Assert.Throws<ArgumentException>(() =>
            Make(new RecordingSystem("a", trace, "x"), new RecordingSystem("b", trace, "x")));
    }

    [Fact]
    public void Progress_ReportsEachNewMonth()
    {
        var sim = Make();
        var reports = new List<WorldDate>();
        sim.AdvanceDays(65, new Support.ListProgress<WorldDate>(reports.Add));
        Assert.Equal(new long[] { 30, 60 }, reports.Select(r => r.Day));
    }

    [Fact]
    public void Cancellation_StopsAdvancing()
    {
        var sim = Make();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => sim.AdvanceDays(10, ct: cts.Token));
        Assert.Equal(0, sim.World.Date.Day);
    }

    [Fact]
    public void Invariants_DetectMissingCause()
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        world.Date = new WorldDate(10);
        var log = new EventLog();
        log.Restore(new WorldEvent(1, new WorldDate(5), "Broken",
            new SortedDictionary<string, EntityRef>(StringComparer.Ordinal), null,
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new[] { new Cause(99, CauseKind.Trigger, 1) }, 10, Array.Empty<string>()));
        var errors = Invariants.Check(world, log);
        Assert.Contains(errors, e => e.Contains("#99"));
    }

    [Fact]
    public void Invariants_DetectFutureEvent()
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        var log = new EventLog();
        log.Append(new WorldDate(50), new EventDraft("TooEarly"));
        Assert.Contains(Invariants.Check(world, log), e => e.Contains("future"));
    }

    [Fact]
    public void SystemsGetDistinctRandomStreams()
    {
        var draws = new Dictionary<string, ulong>();
        var a = new DrawSystem("a", draws);
        var b = new DrawSystem("b", draws);
        Make(a, b).AdvanceDays(30);
        Assert.NotEqual(draws["a"], draws["b"]);
    }

    private sealed class DrawSystem : ISimSystem
    {
        private readonly Dictionary<string, ulong> _draws;
        public DrawSystem(string name, Dictionary<string, ulong> draws) { Name = name; _draws = draws; }
        public string Name { get; }
        public void OnMonthlyTick(SimContext ctx) => _draws[Name] = ctx.Rng.NextULong();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'IdGen' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Core/IdGen.cs`:
```csharp
namespace FantasyWorld.Sim.Core;

/// <summary>Per-kind entity ids ("region", "settlement", …), each starting at 1. Persisted with the world.</summary>
public sealed class IdGen
{
    private SortedDictionary<string, long> _next = new(StringComparer.Ordinal);

    /// <summary>The next id each kind will receive. Re-sorted ordinally when deserialized.</summary>
    public SortedDictionary<string, long> Next
    {
        get => _next;
        init => _next = Sorted.From(value);
    }

    public long Take(string kind)
    {
        var id = Peek(kind);
        _next[kind] = id + 1;
        return id;
    }

    public long Peek(string kind) => _next.GetValueOrDefault(kind, 1);
}
```

Replace `sim/FantasyWorld.Sim/Engine/ISimSystem.cs` entirely with:
```csharp
namespace FantasyWorld.Sim.Engine;

/// <summary>A simulation system. Systems never call each other; they communicate through events.</summary>
public interface ISimSystem
{
    string Name { get; }

    void OnMonthlyTick(SimContext ctx);

    /// <summary>Runs on the first day of each year, after every system's monthly tick.</summary>
    void OnYearlyTick(SimContext ctx) { }

    /// <summary>Scheduled-action kinds this system handles. Each kind has exactly one owner.</summary>
    IEnumerable<string> HandledKinds => Array.Empty<string>();

    void OnScheduled(SimContext ctx, ScheduledAction action) =>
        throw new NotSupportedException($"System '{Name}' does not handle scheduled actions.");
}
```

Replace `sim/FantasyWorld.Sim/Engine/Simulator.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>
/// Advances the world one day at a time. Each day: monthly ticks (first of month), yearly ticks
/// (first of year), then due scheduled actions. Catch-up and live mode both use AdvanceDays.
/// </summary>
public sealed class Simulator
{
    private readonly IReadOnlyList<ISimSystem> _systems;
    private readonly Dictionary<string, ISimSystem> _handlers = new(StringComparer.Ordinal);
    private readonly bool _checkInvariants;
    private int _checkedCount;

    public Simulator(World world, EventLog log, EventQueue queue, IReadOnlyList<ISimSystem> systems, bool checkInvariants = false)
    {
        World = world;
        Log = log;
        Queue = queue;
        _systems = systems;
        _checkInvariants = checkInvariants;
        _checkedCount = log.Count;
        foreach (var system in systems)
            foreach (var kind in system.HandledKinds)
                if (!_handlers.TryAdd(kind, system))
                    throw new ArgumentException($"Scheduled kind '{kind}' is handled by more than one system.", nameof(systems));
    }

    public World World { get; }
    public EventLog Log { get; }
    public EventQueue Queue { get; }

    public void AdvanceDays(long days, IProgress<WorldDate>? progress = null, CancellationToken ct = default)
    {
        for (long i = 0; i < days; i++)
        {
            ct.ThrowIfCancellationRequested();
            StepDay();
            if (World.Date.IsFirstOfMonth) progress?.Report(World.Date);
        }
    }

    private void StepDay()
    {
        World.Date = World.Date.AddDays(1);

        if (World.Date.IsFirstOfMonth)
            foreach (var system in _systems)
                system.OnMonthlyTick(ContextFor(system.Name, "month"));

        if (World.Date.IsFirstOfYear)
            foreach (var system in _systems)
                system.OnYearlyTick(ContextFor(system.Name, "year"));

        while (Queue.TryDequeueDue(World.Date, out var action))
        {
            if (!_handlers.TryGetValue(action.Kind, out var handler))
                throw new InvalidOperationException($"No system handles scheduled kind '{action.Kind}'.");
            handler.OnScheduled(ContextFor(handler.Name, "scheduled/" + action.Seq), action);
        }

        if (_checkInvariants)
        {
            var errors = Invariants.Check(World, Log, _checkedCount);
            if (errors.Count > 0) throw new InvariantViolationException(errors);
            _checkedCount = Log.Count;
        }
    }

    private SimContext ContextFor(string system, string phase) =>
        new(World, Log, Queue, Rng.ForStream(World.Seed, system + "/" + phase, World.Date.Day));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~IdGenTests|FullyQualifiedName~SimulatorTests"`
Expected: `Passed!  - Failed: 0, Passed: 12`. Then `dotnet test sim` → `Passed: 96` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add yearly tick and per-kind entity ids

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Terrain grid, biomes and deterministic noise

The tile layer (spec §5 "Two map layers") that world generation fills and the Godot client renders. Noise must be bit-identical on every platform, so it uses only + − × ÷.

**Files:**
- Create: `sim/FantasyWorld.Sim/Geo/Biome.cs`, `sim/FantasyWorld.Sim/Geo/Noise2D.cs`, `sim/FantasyWorld.Sim/Geo/TerrainGrid.cs`, `sim/FantasyWorld.Sim/Geo/TileCoord.cs`
- Modify: `sim/FantasyWorld.Sim/Core/Rng.cs`, `sim/FantasyWorld.Sim/Core/StableHash.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Geo/NoiseTests.cs`, `sim/FantasyWorld.Sim.Tests/Geo/TerrainGridTests.cs`

**Interfaces:**
- Consumes: `StableHash` (Plan 1).
- Produces: `StableHash.Mix(ulong)`, `StableHash.Derive(ulong seed, string purpose)` (Rng now reuses `Mix`); namespace `FantasyWorld.Sim.Geo`: `readonly record struct TileCoord(int X, int Y) { int DistanceSquared(TileCoord) }`; `enum Biome : byte { Ocean, Tundra, Taiga, Grassland, Forest, Swamp, Desert, Savanna, Jungle, Hills, Mountain }`; `enum ClimateBand { Cold, Temperate, Hot }`; `[Flags] enum TileFlags : byte { None, River, Coast }`; `static class BiomeInfo { MoveCost, Farmland, BaseFertility, BaseDanger }`; `sealed class TerrainGrid` (byte channels `Elevation, Temperature, Moisture, Biomes, Flags`, `SeaLevel`, `Index/Coord/InBounds/BiomeAt/SetBiome/IsLand/Has/Set/Neighbours4/Neighbours8`, JSON round-trips as base64); `sealed class Noise2D(ulong seed) { double Fractal(x, y, octaves = 5, persistence = 0.5); double Value(x, y, octave = 0) }` in [0, 1).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Geo/NoiseTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Tests.Geo;

public class NoiseTests
{
    [Fact]
    public void SameSeed_SameValues()
    {
        var a = new Noise2D(5);
        var b = new Noise2D(5);
        for (var i = 0; i < 100; i++)
            Assert.Equal(a.Fractal(i * 0.37, i * 0.11), b.Fractal(i * 0.37, i * 0.11));
    }

    [Fact]
    public void DifferentSeeds_Differ() =>
        Assert.NotEqual(new Noise2D(1).Fractal(3.3, 4.4), new Noise2D(2).Fractal(3.3, 4.4));

    [Fact]
    public void Values_AreInUnitInterval_AndVaried()
    {
        var noise = new Noise2D(9);
        var values = new List<double>();
        for (var y = 0; y < 50; y++)
            for (var x = 0; x < 50; x++)
                values.Add(noise.Fractal(x / 7.0, y / 7.0));
        Assert.All(values, v => Assert.InRange(v, 0.0, 0.9999999999999999));
        Assert.True(values.Max() - values.Min() > 0.4, "noise should vary across the field");
    }

    [Fact]
    public void Noise_IsContinuous()
    {
        var noise = new Noise2D(9);
        Assert.InRange(Math.Abs(noise.Value(10.5, 3.5) - noise.Value(10.501, 3.5)), 0, 0.01);
    }

    [Fact]
    public void Derive_GivesDistinctStableSeeds()
    {
        Assert.Equal(StableHash.Derive(7, "a"), StableHash.Derive(7, "a"));
        Assert.NotEqual(StableHash.Derive(7, "a"), StableHash.Derive(7, "b"));
    }
}
```

`sim/FantasyWorld.Sim.Tests/Geo/TerrainGridTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Tests.Geo;

public class TerrainGridTests
{
    [Fact]
    public void IndexAndCoord_RoundTrip()
    {
        var grid = new TerrainGrid(8, 4);
        Assert.Equal(13, grid.Index(5, 1));
        Assert.Equal(new TileCoord(5, 1), grid.Coord(13));
        Assert.Equal(32, grid.Count);
    }

    [Fact]
    public void Neighbours_StayInBounds()
    {
        var grid = new TerrainGrid(4, 4);
        Assert.Equal(new[] { 1, 4 }, grid.Neighbours4(0));
        Assert.Equal(new[] { 1, 4, 5 }, grid.Neighbours8(0));
        Assert.Equal(8, grid.Neighbours8(grid.Index(1, 1)).Count());
    }

    [Fact]
    public void FlagsAndBiomes_AreStoredPerTile()
    {
        var grid = new TerrainGrid(4, 4);
        Assert.False(grid.IsLand(3));
        grid.SetBiome(3, Biome.Forest);
        grid.Set(3, TileFlags.River);
        Assert.True(grid.IsLand(3));
        Assert.True(grid.Has(3, TileFlags.River));
        Assert.False(grid.Has(3, TileFlags.Coast));
    }

    [Fact]
    public void JsonRoundTrip_PreservesChannels()
    {
        var grid = new TerrainGrid(3, 2) { SeaLevel = 100 };
        grid.Elevation[4] = 200;
        grid.SetBiome(4, Biome.Mountain);
        var copy = JsonSerializer.Deserialize<TerrainGrid>(JsonSerializer.Serialize(grid))!;
        Assert.Equal(100, copy.SeaLevel);
        Assert.Equal(grid.Elevation, copy.Elevation);
        Assert.Equal(Biome.Mountain, copy.BiomeAt(4));
    }

    [Fact]
    public void Constructor_RejectsWrongChannelLength() =>
        Assert.Throws<ArgumentException>(() =>
            new TerrainGrid(2, 2, 0, new byte[4], new byte[4], new byte[4], new byte[3], new byte[4]));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Geo' does not exist in the namespace 'FantasyWorld.Sim' (are you missing an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Core/Rng.cs` entirely with:
```csharp
namespace FantasyWorld.Sim.Core;

/// <summary>SplitMix64 PRNG. Deterministic across platforms and .NET versions.</summary>
public sealed class Rng
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    /// <summary>An independent stream for one system and phase on one world day.</summary>
    public static Rng ForStream(ulong worldSeed, string stream, long tick) =>
        new(Mix(worldSeed ^ Mix(StableHash.Fnv1a64(stream)) ^ Mix((ulong)tick * Golden)));

    public ulong NextULong() => Mix(_state += Golden);

    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range must not be empty.");
        var span = (ulong)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)(NextULong() % span));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    private static ulong Mix(ulong z) => StableHash.Mix(z);
}
```

Replace `sim/FantasyWorld.Sim/Core/StableHash.cs` entirely with:
```csharp
namespace FantasyWorld.Sim.Core;

/// <summary>FNV-1a 64-bit over UTF-16 code units. Stable across processes, unlike string.GetHashCode.</summary>
public static class StableHash
{
    public static ulong Fnv1a64(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>SplitMix64 finalizer: a fast, well-distributed 64-bit mix.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Derives an independent seed for a named purpose, e.g. ("terrain/height").</summary>
    public static ulong Derive(ulong seed, string purpose) => Mix(seed ^ Mix(Fnv1a64(purpose)));
}
```

`sim/FantasyWorld.Sim/Geo/Biome.cs`:
```csharp
namespace FantasyWorld.Sim.Geo;

/// <summary>Ocean covers all water, including inland seas.</summary>
public enum Biome : byte { Ocean, Tundra, Taiga, Grassland, Forest, Swamp, Desert, Savanna, Jungle, Hills, Mountain }

public enum ClimateBand { Cold, Temperate, Hot }

[Flags]
public enum TileFlags : byte { None = 0, River = 1, Coast = 2 }

/// <summary>Static per-biome numbers used by world generation and the economy.</summary>
public static class BiomeInfo
{
    /// <summary>Cost of crossing one tile, relative to open grassland.</summary>
    public static double MoveCost(Biome b) => b switch
    {
        Biome.Mountain => 5.0,
        Biome.Swamp => 2.5,
        Biome.Hills or Biome.Jungle => 2.0,
        Biome.Forest or Biome.Taiga or Biome.Desert or Biome.Tundra => 1.5,
        _ => 1.0,
    };

    /// <summary>Fraction of a tile that can be farmed.</summary>
    public static double Farmland(Biome b) => b switch
    {
        Biome.Grassland => 1.0,
        Biome.Savanna => 0.8,
        Biome.Forest or Biome.Hills => 0.5,
        Biome.Swamp or Biome.Jungle => 0.4,
        Biome.Taiga => 0.3,
        Biome.Tundra => 0.1,
        Biome.Desert => 0.05,
        _ => 0.0,
    };

    public static double BaseFertility(Biome b) => b switch
    {
        Biome.Grassland => 70,
        Biome.Forest => 55,
        Biome.Savanna => 50,
        Biome.Jungle => 45,
        Biome.Hills => 40,
        Biome.Swamp => 35,
        Biome.Taiga => 30,
        Biome.Tundra or Biome.Mountain => 15,
        Biome.Desert => 10,
        _ => 0,
    };

    public static double BaseDanger(Biome b) => b switch
    {
        Biome.Mountain => 50,
        Biome.Swamp or Biome.Jungle => 45,
        Biome.Tundra => 40,
        Biome.Taiga or Biome.Desert => 35,
        Biome.Forest or Biome.Hills => 30,
        Biome.Savanna => 25,
        _ => 15,
    };
}
```

`sim/FantasyWorld.Sim/Geo/Noise2D.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Geo;

/// <summary>
/// Deterministic value noise with fractal octaves, in [0, 1). Uses only +, -, *, / so results are
/// bit-identical on every platform (Math.Pow/Sin/Exp are not guaranteed to be).
/// </summary>
public sealed class Noise2D
{
    private readonly ulong _seed;

    public Noise2D(ulong seed) => _seed = seed;

    public double Fractal(double x, double y, int octaves = 5, double persistence = 0.5)
    {
        double sum = 0, amplitude = 1, norm = 0, frequency = 1;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * Value(x * frequency, y * frequency, octave);
            norm += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }
        return sum / norm;
    }

    public double Value(double x, double y, int octave = 0)
    {
        var x0 = (long)Math.Floor(x);
        var y0 = (long)Math.Floor(y);
        var sx = Smooth(x - x0);
        var sy = Smooth(y - y0);
        var top = Lerp(Lattice(x0, y0, octave), Lattice(x0 + 1, y0, octave), sx);
        var bottom = Lerp(Lattice(x0, y0 + 1, octave), Lattice(x0 + 1, y0 + 1, octave), sx);
        return Lerp(top, bottom, sy);
    }

    private double Lattice(long x, long y, int octave)
    {
        var h = StableHash.Mix(_seed
                               ^ StableHash.Mix((ulong)x * 0x9E3779B97F4A7C15UL)
                               ^ StableHash.Mix((ulong)y * 0xC2B2AE3D27D4EB4FUL + (ulong)octave));
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
```

`sim/FantasyWorld.Sim/Geo/TerrainGrid.cs`:
```csharp
using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Geo;

/// <summary>
/// The tile layer: one byte per tile per channel, row-major (index = y * Width + x).
/// Static after world generation; rendered by the Godot client.
/// </summary>
public sealed class TerrainGrid
{
    private static readonly (int Dx, int Dy)[] Offsets4 = { (0, -1), (-1, 0), (1, 0), (0, 1) };
    private static readonly (int Dx, int Dy)[] Offsets8 =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    public TerrainGrid(int width, int height)
        : this(width, height, 0, new byte[width * height], new byte[width * height],
            new byte[width * height], new byte[width * height], new byte[width * height])
    {
    }

    [JsonConstructor]
    public TerrainGrid(int width, int height, byte seaLevel, byte[] elevation, byte[] temperature,
        byte[] moisture, byte[] biomes, byte[] flags)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Grid must not be empty.");
        var n = width * height;
        foreach (var channel in new[] { elevation, temperature, moisture, biomes, flags })
            if (channel.Length != n) throw new ArgumentException($"Every channel must have {n} tiles.");
        Width = width;
        Height = height;
        SeaLevel = seaLevel;
        Elevation = elevation;
        Temperature = temperature;
        Moisture = moisture;
        Biomes = biomes;
        Flags = flags;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Highest elevation that is water at generation time.</summary>
    public byte SeaLevel { get; set; }

    public byte[] Elevation { get; }
    public byte[] Temperature { get; }
    public byte[] Moisture { get; }
    public byte[] Biomes { get; }
    public byte[] Flags { get; }

    [JsonIgnore] public int Count => Width * Height;

    public int Index(int x, int y) => y * Width + x;
    public TileCoord Coord(int index) => new(index % Width, index / Width);
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Biome BiomeAt(int index) => (Biome)Biomes[index];
    public void SetBiome(int index, Biome biome) => Biomes[index] = (byte)biome;
    public bool IsLand(int index) => Biomes[index] != (byte)Biome.Ocean;

    public bool Has(int index, TileFlags flag) => ((TileFlags)Flags[index] & flag) != 0;
    public void Set(int index, TileFlags flag) => Flags[index] |= (byte)flag;

    public IEnumerable<int> Neighbours4(int index) => Neighbours(index, Offsets4);
    public IEnumerable<int> Neighbours8(int index) => Neighbours(index, Offsets8);

    private IEnumerable<int> Neighbours(int index, (int Dx, int Dy)[] offsets)
    {
        var x = index % Width;
        var y = index / Width;
        foreach (var (dx, dy) in offsets)
            if (InBounds(x + dx, y + dy))
                yield return Index(x + dx, y + dy);
    }
}
```

`sim/FantasyWorld.Sim/Geo/TileCoord.cs`:
```csharp
namespace FantasyWorld.Sim.Geo;

public readonly record struct TileCoord(int X, int Y)
{
    public int DistanceSquared(TileCoord other) =>
        (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);

    public override string ToString() => $"({X},{Y})";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~NoiseTests|FullyQualifiedName~TerrainGridTests"`
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 106` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add terrain grid, biomes and deterministic noise

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Terrain generator

Seed → a floating world block ringed by sea (spec §5 "World generation": heightmap → climate/biomes). Sea level, hills and mountains are set by percentile, so land share is stable across seeds.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/TerrainGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/TerrainGeneratorTests.cs`

**Interfaces:**
- Consumes: `TerrainGrid`, `Noise2D`, `Biome`, `StableHash.Derive` (Task 2).
- Produces: `sealed record TerrainOptions(int Width = 256, int Height = 256, double LandFraction = 0.55, double MountainFraction = 0.08, double HillFraction = 0.14, int MinIslandTiles = 150)`; `static class TerrainGenerator { const byte SeaLevel = 100; TerrainGrid Generate(ulong seed, TerrainOptions); Biome Classify(elevation, mountainLevel, hillLevel, temperature, moisture); List<List<int>> LandComponents(TerrainGrid); int[] DistanceToSea(TerrainGrid) }`. Islands smaller than `MinIslandTiles` are sunk; a 2-tile border is always sea.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/TerrainGeneratorTests.cs`:
```csharp
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class TerrainGeneratorTests
{
    private static readonly TerrainOptions Small = new(Width: 96, Height: 96, MinIslandTiles: 40);

    [Fact]
    public void SameSeed_SameTerrain()
    {
        var a = TerrainGenerator.Generate(3, Small);
        var b = TerrainGenerator.Generate(3, Small);
        Assert.Equal(a.Elevation, b.Elevation);
        Assert.Equal(a.Biomes, b.Biomes);
        Assert.Equal(a.Moisture, b.Moisture);
    }

    [Fact]
    public void DifferentSeeds_DifferentTerrain() =>
        Assert.NotEqual(TerrainGenerator.Generate(3, Small).Biomes, TerrainGenerator.Generate(4, Small).Biomes);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void LandFraction_IsNearTarget(ulong seed)
    {
        var grid = TerrainGenerator.Generate(seed, Small);
        var land = Enumerable.Range(0, grid.Count).Count(grid.IsLand) / (double)grid.Count;
        Assert.InRange(land, Small.LandFraction - 0.08, Small.LandFraction + 0.01);
    }

    [Fact]
    public void Border_IsOcean()
    {
        var grid = TerrainGenerator.Generate(5, Small);
        for (var x = 0; x < grid.Width; x++)
        {
            Assert.False(grid.IsLand(grid.Index(x, 0)));
            Assert.False(grid.IsLand(grid.Index(x, grid.Height - 1)));
        }
    }

    [Fact]
    public void LandAndWater_AgreeWithSeaLevel()
    {
        var grid = TerrainGenerator.Generate(5, Small);
        for (var i = 0; i < grid.Count; i++)
            Assert.Equal(grid.Elevation[i] > grid.SeaLevel, grid.IsLand(i));
    }

    [Fact]
    public void Generates_MountainsHillsAndVariedLowlands()
    {
        var grid = TerrainGenerator.Generate(5, new TerrainOptions(Width: 160, Height: 160, MinIslandTiles: 60));
        var biomes = Enumerable.Range(0, grid.Count).Where(grid.IsLand).Select(grid.BiomeAt).ToHashSet();
        Assert.Contains(Biome.Mountain, biomes);
        Assert.Contains(Biome.Hills, biomes);
        Assert.True(biomes.Count >= 7, $"expected varied biomes, got {string.Join(",", biomes)}");
    }

    [Fact]
    public void NoIsland_IsSmallerThanTheMinimum()
    {
        var grid = TerrainGenerator.Generate(8, Small);
        Assert.All(TerrainGenerator.LandComponents(grid), c => Assert.True(c.Count >= Small.MinIslandTiles));
    }

    [Theory]
    [InlineData(0.8, 0.9, Biome.Jungle)]
    [InlineData(0.8, 0.4, Biome.Savanna)]
    [InlineData(0.8, 0.1, Biome.Desert)]
    [InlineData(0.5, 0.2, Biome.Grassland)]
    [InlineData(0.5, 0.5, Biome.Forest)]
    [InlineData(0.5, 0.8, Biome.Swamp)]
    [InlineData(0.1, 0.2, Biome.Tundra)]
    [InlineData(0.1, 0.6, Biome.Taiga)]
    public void Classify_LowlandsByTemperatureAndMoisture(double temperature, double moisture, Biome expected) =>
        Assert.Equal(expected, TerrainGenerator.Classify(120, mountainLevel: 240, hillLevel: 220, temperature, moisture));

    [Fact]
    public void Classify_HighGroundByElevation()
    {
        Assert.Equal(Biome.Mountain, TerrainGenerator.Classify(240, 240, 220, 0.5, 0.5));
        Assert.Equal(Biome.Hills, TerrainGenerator.Classify(225, 240, 220, 0.5, 0.5));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'WorldGen' does not exist in the namespace 'FantasyWorld.Sim' (are you missing an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/TerrainGenerator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

public sealed record TerrainOptions(
    int Width = 256,
    int Height = 256,
    double LandFraction = 0.55,
    double MountainFraction = 0.08,
    double HillFraction = 0.14,
    int MinIslandTiles = 150);

/// <summary>Seed → elevation, temperature, moisture and biome for every tile. A floating block ringed by sea.</summary>
public static class TerrainGenerator
{
    public const byte SeaLevel = 100;
    private const int BorderWidth = 2;

    public static TerrainGrid Generate(ulong seed, TerrainOptions options)
    {
        var grid = new TerrainGrid(options.Width, options.Height) { SeaLevel = SeaLevel };
        var raw = RawElevation(grid, new Noise2D(StableHash.Derive(seed, "terrain/height")));
        var landLevel = Percentile(raw, 1 - options.LandFraction);
        WriteElevation(grid, raw, landLevel);
        SinkSmallIslands(grid, options.MinIslandTiles);

        var land = Enumerable.Range(0, grid.Count).Where(i => grid.Elevation[i] > SeaLevel).ToArray();
        var landElevations = land.Select(i => (double)grid.Elevation[i]).ToArray();
        var mountainLevel = Percentile(landElevations, 1 - options.MountainFraction);
        var hillLevel = Percentile(landElevations, 1 - options.MountainFraction - options.HillFraction);

        WriteTemperature(grid, new Noise2D(StableHash.Derive(seed, "terrain/temperature")));
        WriteMoisture(grid, new Noise2D(StableHash.Derive(seed, "terrain/moisture")));
        foreach (var i in land)
            grid.SetBiome(i, Classify(grid.Elevation[i], mountainLevel, hillLevel,
                grid.Temperature[i] / 255.0, grid.Moisture[i] / 255.0));
        return grid;
    }

    public static Biome Classify(double elevation, double mountainLevel, double hillLevel, double temperature, double moisture)
    {
        if (elevation >= mountainLevel) return Biome.Mountain;
        if (elevation >= hillLevel) return Biome.Hills;
        if (temperature < 0.3) return moisture < 0.45 ? Biome.Tundra : Biome.Taiga;
        if (temperature < 0.65) return moisture < 0.35 ? Biome.Grassland : moisture < 0.62 ? Biome.Forest : Biome.Swamp;
        return moisture < 0.32 ? Biome.Desert : moisture < 0.55 ? Biome.Savanna : Biome.Jungle;
    }

    private static double[] RawElevation(TerrainGrid grid, Noise2D noise)
    {
        var raw = new double[grid.Count];
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var dx = (x + 0.5) / grid.Width * 2 - 1;
                var dy = (y + 0.5) / grid.Height * 2 - 1;
                var falloff = 0.5 * (dx * dx + dy * dy);
                var edge = x < BorderWidth || y < BorderWidth || x >= grid.Width - BorderWidth || y >= grid.Height - BorderWidth;
                raw[grid.Index(x, y)] = edge
                    ? -1
                    : noise.Fractal(x * 4.0 / grid.Width, y * 4.0 / grid.Height) - falloff;
            }
        return raw;
    }

    private static void WriteElevation(TerrainGrid grid, double[] raw, double landLevel)
    {
        var min = raw.Min();
        var max = raw.Max();
        for (var i = 0; i < raw.Length; i++)
        {
            grid.Elevation[i] = raw[i] >= landLevel && raw[i] > -1
                ? (byte)(SeaLevel + 1 + (int)((raw[i] - landLevel) / Math.Max(1e-9, max - landLevel) * (254 - SeaLevel)))
                : (byte)((raw[i] - min) / Math.Max(1e-9, landLevel - min) * SeaLevel);
            grid.SetBiome(i, grid.Elevation[i] > SeaLevel ? Biome.Grassland : Biome.Ocean);
        }
    }

    /// <summary>Land masses smaller than minTiles (4-connected) become sea.</summary>
    private static void SinkSmallIslands(TerrainGrid grid, int minTiles)
    {
        foreach (var component in LandComponents(grid))
            if (component.Count < minTiles)
                foreach (var i in component)
                {
                    grid.Elevation[i] = SeaLevel;
                    grid.SetBiome(i, Biome.Ocean);
                }
    }

    /// <summary>4-connected land components, ordered by their lowest tile index.</summary>
    public static List<List<int>> LandComponents(TerrainGrid grid)
    {
        var seen = new bool[grid.Count];
        var components = new List<List<int>>();
        for (var start = 0; start < grid.Count; start++)
        {
            if (seen[start] || !grid.IsLand(start)) continue;
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                component.Add(i);
                foreach (var n in grid.Neighbours4(i))
                    if (!seen[n] && grid.IsLand(n))
                    {
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
            }
            components.Add(component);
        }
        return components;
    }

    private static void WriteTemperature(TerrainGrid grid, Noise2D noise)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var i = grid.Index(x, y);
                var latitude = (double)y / (grid.Height - 1);
                var height = Math.Max(0, grid.Elevation[i] - SeaLevel) / (255.0 - SeaLevel);
                var t = 0.2 + 0.75 * latitude - 0.4 * height + (noise.Fractal(x * 3.0 / grid.Width, y * 3.0 / grid.Height, 3) - 0.5) * 0.4;
                grid.Temperature[i] = ToByte(t);
            }
    }

    private static void WriteMoisture(TerrainGrid grid, Noise2D noise)
    {
        var distance = DistanceToSea(grid);
        var coastReach = grid.Width / 10.0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var i = grid.Index(x, y);
                var coastal = Math.Max(0, 1 - distance[i] / coastReach);
                var m = (noise.Fractal(x * 4.0 / grid.Width, y * 4.0 / grid.Height, 4) - 0.5) * 2.0 + 0.4 + 0.2 * coastal;
                grid.Moisture[i] = ToByte(m);
            }
    }

    /// <summary>4-neighbour steps from each tile to the nearest ocean tile (0 for ocean).</summary>
    public static int[] DistanceToSea(TerrainGrid grid)
    {
        var distance = new int[grid.Count];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        for (var i = 0; i < grid.Count; i++)
            if (!grid.IsLand(i))
            {
                distance[i] = 0;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            foreach (var n in grid.Neighbours4(i))
                if (distance[n] == int.MaxValue)
                {
                    distance[n] = distance[i] + 1;
                    queue.Enqueue(n);
                }
        }
        return distance;
    }

    private static double Percentile(double[] values, double fraction)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)(fraction * sorted.Length), 0, sorted.Length - 1)];
    }

    private static byte ToByte(double unit) => (byte)(Math.Clamp(unit, 0, 1) * 255);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~TerrainGeneratorTests"`
Expected: `Passed!  - Failed: 0, Passed: 18`. Then `dotnet test sim` → `Passed: 124` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): generate elevation, climate and biomes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Drainage, rivers and coasts

Rivers drive farming, settlement sites, river crossings and floods. A priority flood from the sea guarantees that every land tile drains to the sea, so rivers never dead-end.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/Hydrology.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/HydrologyTests.cs`

**Interfaces:**
- Consumes: `TerrainGrid`, `TileFlags` (Task 2); `TerrainGenerator` (Task 3).
- Produces: `static class Hydrology { int[] Apply(TerrainGrid grid, double riverFraction = 0.04); int[] Drainage(TerrainGrid grid, out List<int> order) }`. `Apply` sets `River` on the ~4% of land with the most accumulated flow and `Coast` on land 4-adjacent to water, and returns each tile's downstream tile (−1 for water).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/HydrologyTests.cs`:
```csharp
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class HydrologyTests
{
    private static (TerrainGrid Grid, int[] Downstream) Make(ulong seed = 4)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 96, Height: 96, MinIslandTiles: 40));
        return (grid, Hydrology.Apply(grid));
    }

    [Fact]
    public void EveryLandTile_DrainsToTheSea()
    {
        var (grid, downstream) = Make();
        for (var i = 0; i < grid.Count; i++)
        {
            if (!grid.IsLand(i)) continue;
            var steps = 0;
            var at = i;
            while (grid.IsLand(at))
            {
                at = downstream[at];
                Assert.True(at >= 0, $"tile {i} has no outlet");
                Assert.True(++steps <= grid.Count, $"tile {i} drains in a cycle");
            }
        }
    }

    [Fact]
    public void Rivers_FlowIntoRiversOrTheSea()
    {
        var (grid, downstream) = Make();
        for (var i = 0; i < grid.Count; i++)
            if (grid.Has(i, TileFlags.River))
            {
                var next = downstream[i];
                Assert.True(!grid.IsLand(next) || grid.Has(next, TileFlags.River), $"river at {grid.Coord(i)} ends inland");
            }
    }

    [Fact]
    public void RiverShare_IsNearTarget()
    {
        var (grid, _) = Make();
        var land = Enumerable.Range(0, grid.Count).Count(grid.IsLand);
        var rivers = Enumerable.Range(0, grid.Count).Count(i => grid.Has(i, TileFlags.River));
        Assert.InRange(rivers / (double)land, 0.03, 0.06);
    }

    [Fact]
    public void Coast_IsLandNextToWater()
    {
        var (grid, _) = Make();
        for (var i = 0; i < grid.Count; i++)
            Assert.Equal(grid.IsLand(i) && grid.Neighbours4(i).Any(n => !grid.IsLand(n)), grid.Has(i, TileFlags.Coast));
    }

    [Fact]
    public void Hydrology_IsDeterministic()
    {
        var (a, _) = Make(9);
        var (b, _) = Make(9);
        Assert.Equal(a.Flags, b.Flags);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Hydrology' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/Hydrology.cs`:
```csharp
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Drainage and rivers. A priority flood from the sea gives every land tile a downstream neighbour,
/// so all water reaches the sea (depressions are filled, never trapped). Flow accumulates
/// downstream; the wettest channels become rivers.
/// </summary>
public static class Hydrology
{
    /// <summary>Marks River and Coast flags. Returns each tile's downstream tile (-1 for water).</summary>
    public static int[] Apply(TerrainGrid grid, double riverFraction = 0.04)
    {
        var downstream = Drainage(grid, out var order);
        MarkRivers(grid, downstream, order, riverFraction);
        MarkCoasts(grid);
        return downstream;
    }

    /// <summary>Priority flood. order lists land tiles from the sea inland (upstream last).</summary>
    public static int[] Drainage(TerrainGrid grid, out List<int> order)
    {
        var downstream = new int[grid.Count];
        Array.Fill(downstream, -1);
        var filled = new int[grid.Count];
        var visited = new bool[grid.Count];
        var queue = new PriorityQueue<int, (int Level, long Seq)>();
        long seq = 0;
        order = new List<int>();

        for (var i = 0; i < grid.Count; i++)
            if (!grid.IsLand(i))
            {
                visited[i] = true;
                filled[i] = grid.Elevation[i];
                queue.Enqueue(i, (filled[i], seq++));
            }

        while (queue.TryDequeue(out var current, out _))
            foreach (var next in grid.Neighbours8(current))
            {
                if (visited[next]) continue;
                visited[next] = true;
                downstream[next] = current;
                filled[next] = Math.Max(grid.Elevation[next], filled[current]);
                order.Add(next);
                queue.Enqueue(next, (filled[next], seq++));
            }
        return downstream;
    }

    private static void MarkRivers(TerrainGrid grid, int[] downstream, List<int> order, double riverFraction)
    {
        if (order.Count == 0) return;
        var flow = new double[grid.Count];
        foreach (var i in order) flow[i] = 0.1 + grid.Moisture[i] / 255.0;
        for (var k = order.Count - 1; k >= 0; k--)
        {
            var i = order[k];
            if (grid.IsLand(downstream[i])) flow[downstream[i]] += flow[i];
        }

        var sorted = order.Select(i => flow[i]).OrderBy(f => f).ToArray();
        var threshold = sorted[Math.Clamp((int)((1 - riverFraction) * sorted.Length), 0, sorted.Length - 1)];
        foreach (var i in order)
            if (flow[i] >= threshold) grid.Set(i, TileFlags.River);
    }

    private static void MarkCoasts(TerrainGrid grid)
    {
        for (var i = 0; i < grid.Count; i++)
            if (grid.IsLand(i) && grid.Neighbours4(i).Any(n => !grid.IsLand(n)))
                grid.Set(i, TileFlags.Coast);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~HydrologyTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 129` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): add drainage, rivers and coasts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Region partition

Regions are the unit of movement, food and ownership (spec §5: 100–120 regions). Seeds are spread with a minimum spacing and grown by terrain cost, so mountain ranges tend to become borders.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/RegionPartitioner.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/RegionPartitionerTests.cs`

**Interfaces:**
- Consumes: `TerrainGrid`, `BiomeInfo.MoveCost` (Task 2); `TerrainGenerator.LandComponents` (Task 3); `Rng` (Plan 1).
- Produces: `sealed class RegionLayout { int Count; int[] RegionOf; List<int>[] Tiles; TileCoord[] Centers }` (index 0 unused/water); `static class RegionPartitioner { RegionLayout Partition(TerrainGrid, int targetRegions, Rng) }`. Every land component gets at least one region; regions are contiguous.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/RegionPartitionerTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionPartitionerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout) Make(ulong seed = 6, int target = 30)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        return (grid, RegionPartitioner.Partition(grid, target, new Rng(seed)));
    }

    [Fact]
    public void EveryLandTile_BelongsToExactlyOneRegion()
    {
        var (grid, layout) = Make();
        for (var i = 0; i < grid.Count; i++)
        {
            if (grid.IsLand(i)) Assert.InRange(layout.RegionOf[i], 1, layout.Count);
            else Assert.Equal(0, layout.RegionOf[i]);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void RegionCount_IsNearTarget(ulong seed)
    {
        var (_, layout) = Make(seed, target: 30);
        Assert.InRange(layout.Count, 27, 34);
    }

    [Fact]
    public void Regions_AreContiguous()
    {
        var (grid, layout) = Make();
        for (var r = 1; r <= layout.Count; r++)
        {
            var tiles = layout.Tiles[r];
            var seen = new HashSet<int> { tiles[0] };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
                foreach (var n in grid.Neighbours4(queue.Dequeue()))
                    if (layout.RegionOf[n] == r && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(tiles.Count, seen.Count);
        }
    }

    [Fact]
    public void Regions_AreReasonablySized()
    {
        var (_, layout) = Make();
        var sizes = Enumerable.Range(1, layout.Count).Select(r => layout.Tiles[r].Count).ToList();
        Assert.True(sizes.Min() * 6 >= sizes.Average(), $"smallest region {sizes.Min()} vs mean {sizes.Average():F0}");
    }

    [Fact]
    public void Centers_LieInsideTheirRegion()
    {
        var (grid, layout) = Make();
        for (var r = 1; r <= layout.Count; r++)
            Assert.Equal(r, layout.RegionOf[grid.Index(layout.Centers[r].X, layout.Centers[r].Y)]);
    }

    [Fact]
    public void Partition_IsDeterministic() =>
        Assert.Equal(Make(3).Layout.RegionOf, Make(3).Layout.RegionOf);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'RegionLayout' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/RegionPartitioner.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Land split into regions. RegionOf[tile] is the region id (1..Count), 0 for water.</summary>
public sealed class RegionLayout
{
    public RegionLayout(int count, int[] regionOf, TerrainGrid grid)
    {
        Count = count;
        RegionOf = regionOf;
        Tiles = new List<int>[count + 1];
        for (var r = 0; r <= count; r++) Tiles[r] = new List<int>();
        for (var i = 0; i < regionOf.Length; i++)
            if (regionOf[i] > 0) Tiles[regionOf[i]].Add(i);
        Centers = new TileCoord[count + 1];
        for (var r = 1; r <= count; r++) Centers[r] = CenterOf(grid, Tiles[r]);
    }

    public int Count { get; }
    public int[] RegionOf { get; }

    /// <summary>Tiles of each region, index 0 unused.</summary>
    public List<int>[] Tiles { get; }

    /// <summary>The region tile nearest each region's centroid (always inside the region), index 0 unused.</summary>
    public TileCoord[] Centers { get; }

    private static TileCoord CenterOf(TerrainGrid grid, List<int> tiles)
    {
        long sx = 0, sy = 0;
        foreach (var i in tiles)
        {
            sx += i % grid.Width;
            sy += i / grid.Width;
        }
        var mean = new TileCoord((int)(sx / tiles.Count), (int)(sy / tiles.Count));
        return tiles.Select(grid.Coord).OrderBy(c => c.DistanceSquared(mean)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
    }
}

/// <summary>
/// Splits land into ~targetRegions contiguous regions. Seeds are spread with minimum spacing, then
/// a multi-source Dijkstra grows them by terrain cost, so mountain ranges tend to become borders.
/// </summary>
public static class RegionPartitioner
{
    public static RegionLayout Partition(TerrainGrid grid, int targetRegions, Rng rng)
    {
        var components = TerrainGenerator.LandComponents(grid);
        var landCount = components.Sum(c => c.Count);
        if (landCount == 0) throw new InvalidOperationException("The world has no land to divide into regions.");

        var seeds = new List<int>();
        var spacing = Math.Sqrt((double)landCount / targetRegions) * 0.8;
        foreach (var component in components)
        {
            var wanted = Math.Max(1, (int)Math.Round((double)component.Count * targetRegions / landCount));
            seeds.AddRange(PickSeeds(grid, component, wanted, spacing, rng));
        }
        return new RegionLayout(seeds.Count, Grow(grid, seeds), grid);
    }

    private static List<int> PickSeeds(TerrainGrid grid, List<int> component, int wanted, double spacing, Rng rng)
    {
        var candidates = component.Where(i => grid.BiomeAt(i) != Biome.Mountain).ToList();
        if (candidates.Count == 0) candidates = component.ToList();
        for (var k = candidates.Count - 1; k > 0; k--)
        {
            var j = rng.NextInt(0, k + 1);
            (candidates[k], candidates[j]) = (candidates[j], candidates[k]);
        }

        var chosen = new List<int>();
        for (var gap = spacing; chosen.Count < wanted && gap >= 1; gap *= 0.85)
            foreach (var tile in candidates)
            {
                if (chosen.Count >= wanted) break;
                var c = grid.Coord(tile);
                if (chosen.All(s => grid.Coord(s).DistanceSquared(c) >= gap * gap)) chosen.Add(tile);
            }
        return chosen;
    }

    private static int[] Grow(TerrainGrid grid, List<int> seeds)
    {
        var regionOf = new int[grid.Count];
        var cost = new double[grid.Count];
        Array.Fill(cost, double.MaxValue);
        var queue = new PriorityQueue<int, (double Cost, long Seq)>();
        long seq = 0;
        for (var r = 0; r < seeds.Count; r++)
        {
            regionOf[seeds[r]] = r + 1;
            cost[seeds[r]] = 0;
            queue.Enqueue(seeds[r], (0, seq++));
        }

        while (queue.TryDequeue(out var tile, out var priority))
        {
            if (priority.Cost > cost[tile]) continue;
            foreach (var next in grid.Neighbours4(tile))
            {
                if (!grid.IsLand(next)) continue;
                var step = (BiomeInfo.MoveCost(grid.BiomeAt(tile)) + BiomeInfo.MoveCost(grid.BiomeAt(next))) / 2;
                var total = cost[tile] + step;
                if (total >= cost[next]) continue;
                cost[next] = total;
                regionOf[next] = regionOf[tile];
                queue.Enqueue(next, (total, seq++));
            }
        }
        return regionOf;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RegionPartitionerTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 137` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): partition land into regions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Region graph and routing

All movement — armies, trade, migration, plague, monsters — uses the adjacency graph (spec §5). Sea lanes are added only where needed to make every region reachable.

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/RegionGraph.cs`, `sim/FantasyWorld.Sim/WorldGen/RegionGraphBuilder.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Model/RegionGraphTests.cs`, `sim/FantasyWorld.Sim.Tests/WorldGen/RegionGraphBuilderTests.cs`

**Interfaces:**
- Consumes: `RegionLayout` (Task 5); `TerrainGrid`, `BiomeInfo` (Task 2).
- Produces: `enum LinkKind { Land, Sea }`; `sealed record RegionLink(long A, long B, LinkKind Kind, double TravelDays, int BorderLength, bool RiverCrossing, bool MountainPass) { long Other(long) }` (A < B); `sealed class RegionGraph { IReadOnlyList<RegionLink> Links; LinksOf(long); Neighbours(long); Between(long, long); (IReadOnlyList<long> Path, double Days)? ShortestPath(long, long); IReadOnlySet<long> Reachable(long) }`; `static class RegionGraphBuilder { const double DaysPerTile = 0.5, SeaDaysPerTile = 0.25; RegionGraph Build(TerrainGrid, RegionLayout) }`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Model/RegionGraphTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Model;

public class RegionGraphTests
{
    // 1 -2- 2 -2- 3, and a slow direct road 1 -10- 3; 4 is isolated.
    private static RegionGraph Sample() => new(new[]
    {
        new RegionLink(1, 2, LinkKind.Land, 2, 5, false, false),
        new RegionLink(2, 3, LinkKind.Land, 2, 5, true, false),
        new RegionLink(1, 3, LinkKind.Sea, 10, 0, false, false),
    });

    [Fact]
    public void Neighbours_AreSymmetric()
    {
        var g = Sample();
        Assert.Equal(new long[] { 2, 3 }, g.Neighbours(1).OrderBy(x => x));
        Assert.Equal(new long[] { 1, 2 }, g.Neighbours(3).OrderBy(x => x));
        Assert.Empty(g.Neighbours(4));
        Assert.True(g.Between(3, 2)!.RiverCrossing);
    }

    [Fact]
    public void ShortestPath_PrefersCheaperRoute()
    {
        var (path, days) = Sample().ShortestPath(1, 3)!.Value;
        Assert.Equal(new long[] { 1, 2, 3 }, path);
        Assert.Equal(4, days);
    }

    [Fact]
    public void ShortestPath_ToSelf_IsEmptyJourney()
    {
        var (path, days) = Sample().ShortestPath(2, 2)!.Value;
        Assert.Equal(new long[] { 2 }, path);
        Assert.Equal(0, days);
    }

    [Fact]
    public void ShortestPath_Unreachable_IsNull() => Assert.Null(Sample().ShortestPath(1, 4));

    [Fact]
    public void Links_MustBeOrdered() =>
        Assert.Throws<ArgumentException>(() => new RegionGraph(new[] { new RegionLink(2, 1, LinkKind.Land, 1, 1, false, false) }));

    [Fact]
    public void JsonRoundTrip_RebuildsAdjacency()
    {
        var copy = JsonSerializer.Deserialize<RegionGraph>(JsonSerializer.Serialize(Sample()))!;
        Assert.Equal(3, copy.Links.Count);
        Assert.Equal(4, copy.ShortestPath(1, 3)!.Value.Days);
    }
}
```

`sim/FantasyWorld.Sim.Tests/WorldGen/RegionGraphBuilderTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionGraphBuilderTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, RegionGraph Graph) Make(ulong seed)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        return (grid, layout, RegionGraphBuilder.Build(grid, layout));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void EveryRegion_IsReachable(ulong seed)
    {
        var (_, layout, graph) = Make(seed);
        Assert.Equal(layout.Count, graph.Reachable(1).Count);
    }

    [Fact]
    public void LandLinks_JoinRegionsThatShareABorder()
    {
        var (grid, layout, graph) = Make(2);
        foreach (var link in graph.Links.Where(l => l.Kind == LinkKind.Land))
        {
            Assert.True(link.BorderLength > 0);
            var touching = layout.Tiles[(int)link.A].Any(i =>
                grid.Neighbours4(i).Any(n => layout.RegionOf[n] == link.B));
            Assert.True(touching, $"regions {link.A} and {link.B} do not touch");
        }
    }

    [Fact]
    public void TravelDays_ArePositive()
    {
        var (_, _, graph) = Make(3);
        Assert.All(graph.Links, l => Assert.True(l.TravelDays > 0));
    }

    [Fact]
    public void Regions_HaveSeveralNeighboursOnAverage()
    {
        var (_, layout, graph) = Make(1);
        var mean = Enumerable.Range(1, layout.Count).Average(r => graph.LinksOf(r).Count);
        Assert.InRange(mean, 3.0, 8.0);
    }

    [Fact]
    public void SeparateIslands_AreJoinedBySeaLane()
    {
        var grid = new TerrainGrid(20, 10) { SeaLevel = 100 };
        var regionOf = new int[grid.Count];
        for (var y = 2; y < 8; y++)
            for (var x = 2; x < 18; x++)
            {
                if (x is >= 8 and < 12) continue;
                var i = grid.Index(x, y);
                grid.Elevation[i] = 120;
                grid.SetBiome(i, Biome.Grassland);
                regionOf[i] = x < 10 ? 1 : 2;
            }
        Hydrology.Apply(grid);

        var graph = RegionGraphBuilder.Build(grid, new RegionLayout(2, regionOf, grid));

        var lane = Assert.Single(graph.Links);
        Assert.Equal(LinkKind.Sea, lane.Kind);
        Assert.Equal((1L, 2L), (lane.A, lane.B));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'RegionGraph' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Model/RegionGraph.cs`:
```csharp
using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Model;

public enum LinkKind { Land, Sea }

/// <summary>An undirected connection between two regions (A &lt; B). TravelDays is the cost used by every mover.</summary>
public sealed record RegionLink(
    long A,
    long B,
    LinkKind Kind,
    double TravelDays,
    int BorderLength,
    bool RiverCrossing,
    bool MountainPass)
{
    public long Other(long region) => region == A ? B : A;
}

/// <summary>The adjacency graph all movement uses: armies, trade, migration, plague, monsters.</summary>
public sealed class RegionGraph
{
    private static readonly IReadOnlyList<RegionLink> NoLinks = Array.Empty<RegionLink>();
    private readonly Dictionary<long, List<RegionLink>> _byRegion = new();

    [JsonConstructor]
    public RegionGraph(IReadOnlyList<RegionLink> links)
    {
        Links = links.OrderBy(l => l.A).ThenBy(l => l.B).ToList();
        foreach (var link in Links)
        {
            if (link.A >= link.B) throw new ArgumentException($"Link {link.A}-{link.B} must have A < B.", nameof(links));
            Add(link.A, link);
            Add(link.B, link);
        }
    }

    public IReadOnlyList<RegionLink> Links { get; }

    public IReadOnlyList<RegionLink> LinksOf(long region) =>
        _byRegion.TryGetValue(region, out var list) ? list : NoLinks;

    public IEnumerable<long> Neighbours(long region) => LinksOf(region).Select(l => l.Other(region));

    public RegionLink? Between(long a, long b) => LinksOf(a).FirstOrDefault(l => l.Other(a) == b);

    /// <summary>Cheapest route by travel days (Dijkstra), or null when unreachable.</summary>
    public (IReadOnlyList<long> Path, double Days)? ShortestPath(long from, long to)
    {
        var best = new Dictionary<long, double> { [from] = 0 };
        var previous = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Region)>();
        queue.Enqueue(from, (0, from));
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (priority.Days > best[region]) continue;
            if (region == to)
            {
                var path = new List<long> { to };
                while (path[^1] != from) path.Add(previous[path[^1]]);
                path.Reverse();
                return (path, priority.Days);
            }
            foreach (var link in LinksOf(region))
            {
                var next = link.Other(region);
                var days = priority.Days + link.TravelDays;
                if (best.TryGetValue(next, out var known) && known <= days) continue;
                best[next] = days;
                previous[next] = region;
                queue.Enqueue(next, (days, next));
            }
        }
        return null;
    }

    /// <summary>Regions reachable from start, including start.</summary>
    public IReadOnlySet<long> Reachable(long start)
    {
        var seen = new HashSet<long> { start };
        var queue = new Queue<long>(seen);
        while (queue.Count > 0)
            foreach (var next in Neighbours(queue.Dequeue()))
                if (seen.Add(next)) queue.Enqueue(next);
        return seen;
    }

    private void Add(long region, RegionLink link)
    {
        if (!_byRegion.TryGetValue(region, out var list)) _byRegion[region] = list = new List<RegionLink>();
        list.Add(link);
    }
}
```

`sim/FantasyWorld.Sim/WorldGen/RegionGraphBuilder.cs`:
```csharp
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Builds land links from shared borders, then sea lanes until every region is reachable.</summary>
public static class RegionGraphBuilder
{
    public const double DaysPerTile = 0.5;
    public const double SeaDaysPerTile = 0.25;

    public static RegionGraph Build(TerrainGrid grid, RegionLayout layout)
    {
        var borders = new SortedDictionary<(int A, int B), (int Length, int River, int Mountain)>();
        for (var i = 0; i < grid.Count; i++)
        {
            var a = layout.RegionOf[i];
            if (a == 0) continue;
            var x = i % grid.Width;
            foreach (var j in new[] { x + 1 < grid.Width ? i + 1 : -1, i + grid.Width < grid.Count ? i + grid.Width : -1 })
            {
                if (j < 0) continue;
                var b = layout.RegionOf[j];
                if (b == 0 || b == a) continue;
                var key = (Math.Min(a, b), Math.Max(a, b));
                var (length, river, mountain) = borders.GetValueOrDefault(key);
                var isRiver = grid.Has(i, TileFlags.River) || grid.Has(j, TileFlags.River);
                var isMountain = grid.BiomeAt(i) == Biome.Mountain && grid.BiomeAt(j) == Biome.Mountain;
                borders[key] = (length + 1, river + (isRiver ? 1 : 0), mountain + (isMountain ? 1 : 0));
            }
        }

        var links = new List<RegionLink>();
        foreach (var ((a, b), (length, river, mountain)) in borders)
        {
            var pass = mountain * 2 >= length;
            var terrain = (BiomeInfo.MoveCost(CenterBiome(grid, layout, a)) + BiomeInfo.MoveCost(CenterBiome(grid, layout, b))) / 2;
            var days = Distance(layout, a, b) * DaysPerTile * terrain * (pass ? 1.5 : 1.0) + (river > 0 ? 1 : 0);
            links.Add(new RegionLink(a, b, LinkKind.Land, Math.Round(days, 2), length, river > 0, pass));
        }
        AddSeaLanes(grid, layout, links);
        return new RegionGraph(links);
    }

    private static void AddSeaLanes(TerrainGrid grid, RegionLayout layout, List<RegionLink> links)
    {
        var coastal = Enumerable.Range(1, layout.Count)
            .Where(r => layout.Tiles[r].Any(i => grid.Has(i, TileFlags.Coast)))
            .ToList();
        while (true)
        {
            var connected = new RegionGraph(links).Reachable(1);
            if (connected.Count == layout.Count) return;
            var outside = coastal.Where(r => !connected.Contains(r)).ToList();
            var inside = coastal.Where(r => connected.Contains(r)).ToList();
            if (outside.Count == 0 || inside.Count == 0)
                throw new InvalidOperationException("Cannot connect regions: an isolated region has no coast.");
            var (a, b) = outside.SelectMany(o => inside.Select(i => (o, i)))
                .OrderBy(p => Distance(layout, p.o, p.i)).ThenBy(p => p.o).ThenBy(p => p.i).First();
            links.Add(new RegionLink(Math.Min(a, b), Math.Max(a, b), LinkKind.Sea,
                Math.Round(Distance(layout, a, b) * SeaDaysPerTile + 2, 2), 0, false, false));
        }
    }

    private static Biome CenterBiome(TerrainGrid grid, RegionLayout layout, int region) =>
        grid.BiomeAt(grid.Index(layout.Centers[region].X, layout.Centers[region].Y));

    private static double Distance(RegionLayout layout, int a, int b) =>
        Math.Sqrt(layout.Centers[a].DistanceSquared(layout.Centers[b]));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RegionGraphBuilderTests|FullyQualifiedName~RegionGraphTests"`
Expected: `Passed!  - Failed: 0, Passed: 14`. Then `dotnet test sim` → `Passed: 151` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add region graph with land links, sea lanes and routing

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Region, settlement and geography model

The world's entities (spec §5 "Region data", "Settlements"). Living state is mutable and snapshotted; the map is static. Conditions (`drought`, `shortage`, …) map to the event that began them, which is how later events cite causes. Entity invariants are now checked every simulated day.

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/Demography.cs`, `sim/FantasyWorld.Sim/Model/Geography.cs`, `sim/FantasyWorld.Sim/Model/Region.cs`, `sim/FantasyWorld.Sim/Model/Settlement.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Model/World.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Model/EntityTests.cs`, `sim/FantasyWorld.Sim.Tests/Support/TestWorlds.cs`

**Interfaces:**
- Consumes: `IdGen` (Task 1); `TileCoord`, `Biome`, `ClimateBand`, `TerrainGrid` (Task 2); `RegionGraph` (Task 6); `Invariants` (Plan 1).
- Produces: `sealed class ResourceDeposit { Capacity; Yield }`; `sealed class Region` (kind `"region"`; geography fixed at generation; `Fertility/Danger/LeyPower` with `Base*` values; `Food` in person-months; `Settlements`; `Resources`; `Conditions`; `Memory`; `Ref`); `enum SettlementTier { Hamlet, Village, Town, City }`; `sealed class Settlement` (kind `"settlement"`; `Children/Adults/Elders`, `Wealth`, `Walls`, `Garrison`, `Unrest`, `Abandoned`, `Buildings`, `Conditions`, `Population`, `Ref`); `static class Demography { Consumption, FarmShare, Farmers, FarmersPerFarmland = 12, FarmCapacity(Region), TierFor(int) }`; `sealed class Geography(TerrainGrid Terrain, int[] RegionOf, RegionGraph Graph) { long RegionAt(TileCoord) }`; `World` gains `Geography?`, `Ids`, `Regions`, `Settlements`, `LivingSettlements(Region)`; `Invariants.Check` also validates regions and settlements. Test support: `TestWorlds.TwoRegions()`, `AddRegion`, `AddSettlement`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Model/EntityTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class EntityTests
{
    [Fact]
    public void TestWorld_SatisfiesInvariants() =>
        Assert.Empty(Invariants.Check(TestWorlds.TwoRegions(), new EventLog()));

    [Fact]
    public void Settlement_PopulationAndConsumption()
    {
        var s = new Settlement { Children = 100, Adults = 200, Elders = 50 };
        Assert.Equal(350, s.Population);
        Assert.Equal(100 * 0.6 + 200 + 50 * 0.8, Demography.Consumption(s));
    }

    [Theory]
    [InlineData(100, SettlementTier.Hamlet)]
    [InlineData(300, SettlementTier.Village)]
    [InlineData(1500, SettlementTier.Town)]
    [InlineData(9000, SettlementTier.City)]
    public void TierFor_UsesPopulationThresholds(int population, SettlementTier expected) =>
        Assert.Equal(expected, Demography.TierFor(population));

    [Fact]
    public void LivingSettlements_SkipsAbandoned()
    {
        var world = TestWorlds.TwoRegions();
        var extra = TestWorlds.AddSettlement(world, 1, SettlementTier.Hamlet, 50);
        extra.Abandoned = true;
        Assert.Equal(new long[] { 1 }, world.LivingSettlements(world.Regions[1]).Select(s => s.Id));
    }

    [Fact]
    public void Region_JsonRoundTrip_KeepsOrdinalDictionaries()
    {
        var region = TestWorlds.TwoRegions().Regions[1];
        region.Conditions["drought"] = 4;
        region.Resources["ore"] = new ResourceDeposit { Capacity = 5, Yield = 3 };
        var copy = JsonSerializer.Deserialize<Region>(JsonSerializer.Serialize(region))!;
        Assert.Equal(4, copy.Conditions["drought"]);
        Assert.Equal(3, copy.Resources["ore"].Yield);
        Assert.Same(StringComparer.Ordinal, copy.Conditions.Comparer);
        Assert.Same(StringComparer.Ordinal, copy.Resources.Comparer);
    }

    [Fact]
    public void Invariants_CatchBrokenEntities()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Adults = -5;
        world.Regions[2].Fertility = 140;
        world.Regions[2].Settlements.Add(99);
        world.Regions[1].Conditions["drought"] = 7;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("negative population"));
        Assert.Contains(errors, e => e.Contains("fertility 140"));
        Assert.Contains(errors, e => e.Contains("settlement #99"));
        Assert.Contains(errors, e => e.Contains("missing event #7"));
    }

    [Fact]
    public void EntityRefs_UseKindNames()
    {
        var world = TestWorlds.TwoRegions();
        Assert.Equal(new EntityRef("region", 2), world.Regions[2].Ref);
        Assert.Equal(new EntityRef("settlement", 1), world.Settlements[1].Ref);
    }
}
```

`sim/FantasyWorld.Sim.Tests/Support/TestWorlds.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Hand-built worlds for system tests: two regions, one settlement each.</summary>
public static class TestWorlds
{
    public static World TwoRegions(ulong seed = 1)
    {
        var world = WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch);
        AddRegion(world, "Westmark");
        AddRegion(world, "Eastmoor");
        AddSettlement(world, 1, SettlementTier.Village, 1000);
        AddSettlement(world, 2, SettlementTier.Village, 1000);
        return world;
    }

    public static Region AddRegion(World world, string name)
    {
        var region = new Region
        {
            Id = world.Ids.Take(Region.Kind),
            Name = name,
            TileCount = 100,
            Biome = Biome.Grassland,
            Climate = ClimateBand.Temperate,
            Farmland = 100,
            BaseFertility = 60,
            Fertility = 60,
            BaseDanger = 20,
            Danger = 20,
            BaseLey = 30,
            LeyPower = 30,
        };
        world.Regions[region.Id] = region;
        return region;
    }

    public static Settlement AddSettlement(World world, long regionId, SettlementTier tier, int population)
    {
        var settlement = new Settlement
        {
            Id = world.Ids.Take(Settlement.Kind),
            Name = $"Town{world.Settlements.Count + 1}",
            RegionId = regionId,
            Tier = tier,
            Children = population * 35 / 100,
            Adults = population * 55 / 100,
            Elders = population - population * 35 / 100 - population * 55 / 100,
            Wealth = 100,
            Unrest = 10,
        };
        world.Settlements[settlement.Id] = settlement;
        var region = world.Regions[regionId];
        region.Settlements.Add(settlement.Id);
        region.Food = Demography.Consumption(settlement) * 6 + region.Food;
        return settlement;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'SettlementTier' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Engine/Invariants.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException(IReadOnlyList<string> errors)
        : base("World invariants violated:" + Environment.NewLine + string.Join(Environment.NewLine, errors)) =>
        Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

public static class Invariants
{
    /// <summary>Checks log events from startIndex onward, plus every entity. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0)
    {
        var errors = new List<string>();
        CheckLog(world, log, startIndex, errors);
        CheckRegions(world, log, errors);
        CheckSettlements(world, log, errors);
        return errors;
    }

    private static void CheckLog(World world, EventLog log, int startIndex, List<string> errors)
    {
        var all = log.All;
        for (var i = Math.Max(0, startIndex); i < all.Count; i++)
        {
            var ev = all[i];
            if (i > 0)
            {
                var prev = all[i - 1];
                if (ev.Id <= prev.Id) errors.Add($"Event #{ev.Id} id does not increase after #{prev.Id}.");
                if (ev.Date < prev.Date) errors.Add($"Event #{ev.Id} is dated before #{prev.Id}.");
            }
            if (ev.Date > world.Date) errors.Add($"Event #{ev.Id} is dated in the future ({ev.Date} > {world.Date}).");
            if (ev.Causes.Count > EventLog.MaxCauses) errors.Add($"Event #{ev.Id} has {ev.Causes.Count} causes.");
            foreach (var cause in ev.Causes)
            {
                if (!log.TryGet(cause.EventId, out var c))
                    errors.Add($"Event #{ev.Id} cites missing cause #{cause.EventId}.");
                else if (c.Id >= ev.Id || c.Date > ev.Date)
                    errors.Add($"Event #{ev.Id} cites later event #{c.Id}.");
            }
        }
    }

    private static void CheckRegions(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, region) in world.Regions)
        {
            var name = $"Region #{id}";
            if (region.Id != id) errors.Add($"{name} is stored under the wrong key ({region.Id}).");
            Range(errors, name, "fertility", region.Fertility);
            Range(errors, name, "danger", region.Danger);
            Range(errors, name, "ley power", region.LeyPower);
            if (!(region.Food >= 0)) errors.Add($"{name} has invalid food stores {region.Food}.");
            foreach (var sid in region.Settlements)
                if (!world.Settlements.TryGetValue(sid, out var s) || s.RegionId != id)
                    errors.Add($"{name} lists settlement #{sid}, which is not in it.");
            Conditions(errors, log, name, region.Conditions);
        }
    }

    private static void CheckSettlements(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, s) in world.Settlements)
        {
            var name = $"Settlement #{id}";
            if (s.Id != id) errors.Add($"{name} is stored under the wrong key ({s.Id}).");
            if (!world.Regions.TryGetValue(s.RegionId, out var region) || !region.Settlements.Contains(id))
                errors.Add($"{name} is not listed by its region #{s.RegionId}.");
            if (s.Children < 0 || s.Adults < 0 || s.Elders < 0) errors.Add($"{name} has a negative population.");
            if (!(s.Wealth >= 0)) errors.Add($"{name} has invalid wealth {s.Wealth}.");
            Range(errors, name, "unrest", s.Unrest);
            Conditions(errors, log, name, s.Conditions);
        }
    }

    private static void Range(List<string> errors, string name, string what, double value)
    {
        if (!(value >= 0 && value <= 100)) errors.Add($"{name} has {what} {value} outside 0–100.");
    }

    private static void Conditions(List<string> errors, EventLog log, string name, SortedDictionary<string, long> conditions)
    {
        foreach (var (condition, eventId) in conditions)
            if (!log.TryGet(eventId, out _))
                errors.Add($"{name} condition '{condition}' cites missing event #{eventId}.");
    }
}
```

`sim/FantasyWorld.Sim/Model/Demography.cs`:
```csharp
namespace FantasyWorld.Sim.Model;

/// <summary>Shared population arithmetic used by world generation and the economy.</summary>
public static class Demography
{
    public const double ChildFood = 0.6;
    public const double ElderFood = 0.8;

    /// <summary>Food eaten per month, in person-months.</summary>
    public static double Consumption(Settlement s) => s.Children * ChildFood + s.Adults + s.Elders * ElderFood;

    /// <summary>Share of adults who farm; the rest are crafters, traders and soldiers.</summary>
    public static double FarmShare(SettlementTier tier) => tier switch
    {
        SettlementTier.Hamlet => 0.9,
        SettlementTier.Village => 0.85,
        SettlementTier.Town => 0.65,
        _ => 0.45,
    };

    public static double Farmers(Settlement s) => s.Adults * FarmShare(s.Tier);

    /// <summary>How many farmers one tile of farmland can usefully employ.</summary>
    public const double FarmersPerFarmland = 12;

    /// <summary>Farmers beyond this add nothing: the land is fully worked.</summary>
    public static double FarmCapacity(Region region) => region.Farmland * FarmersPerFarmland;

    /// <summary>The tier a settlement of this size grows into (no hysteresis).</summary>
    public static SettlementTier TierFor(int population) => population switch
    {
        >= 6000 => SettlementTier.City,
        >= 1500 => SettlementTier.Town,
        >= 300 => SettlementTier.Village,
        _ => SettlementTier.Hamlet,
    };
}
```

`sim/FantasyWorld.Sim/Model/Geography.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

/// <summary>The static map: terrain tiles, which region owns each tile, and the region graph.</summary>
public sealed class Geography
{
    [JsonConstructor]
    public Geography(TerrainGrid terrain, int[] regionOf, RegionGraph graph)
    {
        if (regionOf.Length != terrain.Count)
            throw new ArgumentException("RegionOf must have one entry per tile.", nameof(regionOf));
        Terrain = terrain;
        RegionOf = regionOf;
        Graph = graph;
    }

    public TerrainGrid Terrain { get; }

    /// <summary>Region id per tile; 0 is water.</summary>
    public int[] RegionOf { get; }

    public RegionGraph Graph { get; }

    public long RegionAt(TileCoord tile) => RegionOf[Terrain.Index(tile.X, tile.Y)];
}
```

`sim/FantasyWorld.Sim/Model/Region.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

/// <summary>A resource a region can yield. Yield drifts toward Capacity; extraction pulls it down.</summary>
public sealed class ResourceDeposit
{
    public double Capacity { get; set; }
    public double Yield { get; set; }
}

/// <summary>A province: the unit of movement, food, danger and (from Plan 4) ownership.</summary>
public sealed class Region
{
    public const string Kind = "region";

    private SortedDictionary<string, ResourceDeposit> _resources = new(StringComparer.Ordinal);
    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; init; } = "";

    // Geography — fixed at world generation.
    public int TileCount { get; init; }
    public TileCoord Center { get; init; }
    public Biome Biome { get; init; }
    public ClimateBand Climate { get; init; }
    public bool Coastal { get; init; }
    public bool HasRiver { get; init; }

    /// <summary>Farmable land in tile-equivalents; caps how many farmers can work.</summary>
    public double Farmland { get; init; }

    public double MountainShare { get; init; }

    // Living state, 0–100 unless noted.
    public double BaseFertility { get; init; }
    public double Fertility { get; set; }
    public double BaseDanger { get; init; }
    public double Danger { get; set; }
    public double BaseLey { get; init; }
    public double LeyPower { get; set; }

    /// <summary>Food stores shared by the region's settlements, in person-months.</summary>
    public double Food { get; set; }

    public List<long> Settlements { get; init; } = new();

    public SortedDictionary<string, ResourceDeposit> Resources
    {
        get => _resources;
        init => _resources = Sorted.From(value);
    }

    /// <summary>Active conditions ("drought", "shortage", …) mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    /// <summary>Defining events ("the burned valley"), newest last. Used for naming and narration.</summary>
    public List<long> Memory { get; init; } = new();

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

`sim/FantasyWorld.Sim/Model/Settlement.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

public enum SettlementTier { Hamlet, Village, Town, City }

/// <summary>A place where the populace lives, simulated as aggregate numbers.</summary>
public sealed class Settlement
{
    public const string Kind = "settlement";

    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long RegionId { get; init; }
    public TileCoord Tile { get; init; }
    public SettlementTier Tier { get; set; }

    public int Children { get; set; }
    public int Adults { get; set; }
    public int Elders { get; set; }

    public double Wealth { get; set; }

    /// <summary>Wall level 0–3.</summary>
    public int Walls { get; set; }

    public int Garrison { get; set; }

    /// <summary>0–100. High unrest leads to revolt and emigration.</summary>
    public double Unrest { get; set; }

    public bool Abandoned { get; set; }

    /// <summary>Building names, kept sorted.</summary>
    public List<string> Buildings { get; init; } = new();

    /// <summary>Active conditions ("revolt") mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    [JsonIgnore] public int Population => Children + Adults + Elders;

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Model/World.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>All simulated state. Grows with characters, realms and armies in Plans 3–4.</summary>
public sealed class World
{
    public required ulong Seed { get; init; }
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    /// <summary>Named scalar state for systems without entities of their own.</summary>
    public SortedDictionary<string, double> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Named references to "currently active" events, e.g. an ongoing shortage.</summary>
    public SortedDictionary<string, long> Markers { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The static map. Null only for bare worlds built in tests.</summary>
    public Geography? Geography { get; set; }

    public IdGen Ids { get; init; } = new();
    public SortedDictionary<long, Region> Regions { get; init; } = new();
    public SortedDictionary<long, Settlement> Settlements { get; init; } = new();

    /// <summary>The region's settlements that are still inhabited, in id order.</summary>
    public IEnumerable<Settlement> LivingSettlements(Region region) =>
        region.Settlements.Select(id => Settlements[id]).Where(s => !s.Abandoned);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~EntityTests"`
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 161` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add region, settlement and geography model with invariants

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Region profiles, resources and names

Turns tiles into gameplay numbers: biome, climate, fertility, danger, ley power and resource deposits, including the rare ones (mithril, dragonbone, arcane crystal). Names are rule-based placeholders; the LLM invention layer (Plan 5) can rename places later.

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/ResourceKinds.cs`, `sim/FantasyWorld.Sim/WorldGen/NameGen.cs`, `sim/FantasyWorld.Sim/WorldGen/RegionProfiler.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/NameGenTests.cs`, `sim/FantasyWorld.Sim.Tests/WorldGen/RegionProfilerTests.cs`

**Interfaces:**
- Consumes: `RegionLayout` (Task 5); `Region`, `ResourceDeposit`, `IdGen` (Tasks 1, 7); `Noise2D`, `BiomeInfo` (Task 2).
- Produces: `static class ResourceKinds` (`grain, timber, ore, stone, horses, fish, mithril, dragonbone, arcane crystal`; `Rare`; `IsRare`); `sealed class NameGen(Rng) { string Region(); string Settlement() }` (unique per world); `static class RegionProfiler { List<Region> Profile(TerrainGrid, RegionLayout, ulong seed, Rng, IdGen, NameGen) }` (region ids follow layout order).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/NameGenTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class NameGenTests
{
    [Fact]
    public void Names_AreDeterministic()
    {
        var a = new NameGen(new Rng(3));
        var b = new NameGen(new Rng(3));
        for (var i = 0; i < 20; i++) Assert.Equal(a.Region(), b.Region());
    }

    [Fact]
    public void Names_AreUniqueAndCapitalised()
    {
        var names = new NameGen(new Rng(8));
        var all = Enumerable.Range(0, 300).Select(i => i % 2 == 0 ? names.Region() : names.Settlement()).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, n => Assert.True(char.IsUpper(n[0]) && n.Length >= 3, n));
    }
}
```

`sim/FantasyWorld.Sim.Tests/WorldGen/RegionProfilerTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionProfilerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, List<Region> Regions) Make(ulong seed = 2)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        var regions = RegionProfiler.Profile(grid, layout, seed, new Rng(seed + 1), new IdGen(), new NameGen(new Rng(seed + 2)));
        return (grid, layout, regions);
    }

    [Fact]
    public void OneRegionPerLayoutEntry_WithMatchingIds()
    {
        var (_, layout, regions) = Make();
        Assert.Equal(Enumerable.Range(1, layout.Count).Select(i => (long)i), regions.Select(r => r.Id));
        Assert.All(regions, r => Assert.Equal(layout.Tiles[(int)r.Id].Count, r.TileCount));
    }

    [Fact]
    public void Stats_AreInRange()
    {
        var (_, _, regions) = Make();
        Assert.All(regions, r =>
        {
            Assert.InRange(r.Fertility, 0, 100);
            Assert.InRange(r.Danger, 0, 100);
            Assert.InRange(r.LeyPower, 0, 100);
            Assert.True(r.Farmland >= 0);
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
        });
        Assert.True(regions.Select(r => r.LeyPower).Max() - regions.Select(r => r.LeyPower).Min() > 20, "ley should vary");
    }

    [Fact]
    public void Resources_FollowGeography()
    {
        var (_, _, regions) = Make();
        Assert.All(regions.Where(r => r.Coastal), r => Assert.Contains(ResourceKinds.Fish, r.Resources.Keys));
        Assert.All(regions.Where(r => r.MountainShare >= 0.2), r => Assert.Contains(ResourceKinds.Ore, r.Resources.Keys));
        Assert.All(regions.SelectMany(r => r.Resources.Values), d => Assert.Equal(d.Capacity, d.Yield));
    }

    [Fact]
    public void FlagsMatchTiles()
    {
        var (grid, layout, regions) = Make();
        foreach (var r in regions)
        {
            var tiles = layout.Tiles[(int)r.Id];
            Assert.Equal(tiles.Any(i => grid.Has(i, TileFlags.River)), r.HasRiver);
            Assert.Equal(tiles.Any(i => grid.Has(i, TileFlags.Coast)), r.Coastal);
        }
    }

    [Fact]
    public void Profile_IsDeterministic()
    {
        var a = Make(5).Regions;
        var b = Make(5).Regions;
        Assert.Equal(a.Select(r => (r.Name, r.Danger, r.Resources.Count)), b.Select(r => (r.Name, r.Danger, r.Resources.Count)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'RegionProfiler' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Model/ResourceKinds.cs`:
```csharp
namespace FantasyWorld.Sim.Model;

/// <summary>Resource names used as keys in Region.Resources.</summary>
public static class ResourceKinds
{
    public const string Grain = "grain";
    public const string Timber = "timber";
    public const string Ore = "ore";
    public const string Stone = "stone";
    public const string Horses = "horses";
    public const string Fish = "fish";
    public const string Mithril = "mithril";
    public const string Dragonbone = "dragonbone";
    public const string ArcaneCrystal = "arcane crystal";

    public static readonly IReadOnlyList<string> Rare = new[] { Mithril, Dragonbone, ArcaneCrystal };

    public static bool IsRare(string kind) => Rare.Contains(kind);
}
```

`sim/FantasyWorld.Sim/WorldGen/NameGen.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Rule-based placeholder names, unique per world. The LLM invention layer (Plan 5) may rename
/// places with culture-aware names; these remain the offline fallback.
/// </summary>
public sealed class NameGen
{
    private static readonly string[] Onsets =
        { "", "b", "br", "c", "d", "dr", "f", "g", "gr", "h", "k", "l", "m", "n", "r", "s", "st", "t", "th", "v", "w" };
    private static readonly string[] Vowels = { "a", "e", "i", "o", "u", "ae", "ia", "ei", "ou" };
    private static readonly string[] Codas = { "", "", "n", "r", "l", "th", "s", "nd", "rn", "ll", "m" };
    private static readonly string[] RegionSuffixes =
        { "mark", "moor", "vale", "fell", "wold", "reach", "shire", "dale", "march", "holt" };
    private static readonly string[] SettlementSuffixes =
        { "ford", "by", "ton", "wick", "stead", "burg", "haven", "bridge", "mere", "cross" };

    private readonly Rng _rng;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public NameGen(Rng rng) => _rng = rng;

    public string Region() => Unique(() => Root() + (_rng.Chance(0.7) ? Pick(RegionSuffixes) : ""));

    public string Settlement() => Unique(() => Root() + (_rng.Chance(0.6) ? Pick(SettlementSuffixes) : ""));

    private string Unique(Func<string> make)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var name = make();
            if (name.Length >= 3 && _used.Add(name)) return name;
        }
        for (var n = 2; ; n++)
        {
            var name = $"{make()} {n}";
            if (_used.Add(name)) return name;
        }
    }

    private string Root()
    {
        var syllables = _rng.Chance(0.5) ? 1 : 2;
        var text = "";
        for (var i = 0; i < syllables; i++) text += Pick(Onsets) + Pick(Vowels) + Pick(Codas);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private string Pick(string[] options) => options[_rng.NextInt(0, options.Length)];
}
```

`sim/FantasyWorld.Sim/WorldGen/RegionProfiler.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Turns each region's tiles into a Region: biome, climate, fertility, danger, ley power and resources.</summary>
public static class RegionProfiler
{
    public static List<Region> Profile(TerrainGrid grid, RegionLayout layout, ulong seed, Rng rng, IdGen ids, NameGen names)
    {
        var ley = new Noise2D(StableHash.Derive(seed, "terrain/ley"));
        var regions = new List<Region>();
        for (var r = 1; r <= layout.Count; r++)
        {
            var tiles = layout.Tiles[r];
            var count = tiles.Count;
            var biomes = tiles.Select(grid.BiomeAt).ToList();
            int CountOf(params Biome[] kinds) => biomes.Count(kinds.Contains);

            var mountains = CountOf(Biome.Mountain);
            var riverTiles = tiles.Count(i => grid.Has(i, TileFlags.River));
            var coastTiles = tiles.Count(i => grid.Has(i, TileFlags.Coast));
            var farmable = biomes.Sum(BiomeInfo.Farmland);
            var farmland = farmable + 0.3 * riverTiles;
            var fertility = farmable > 0
                ? biomes.Sum(b => BiomeInfo.BaseFertility(b) * BiomeInfo.Farmland(b)) / farmable
                : biomes.Average(BiomeInfo.BaseFertility);
            fertility = Math.Clamp(fertility + (riverTiles > 0 ? 10 : 0), 0, 100);

            var leyMean = tiles.Average(i =>
            {
                var c = grid.Coord(i);
                return ley.Fractal(c.X * 3.0 / grid.Width, c.Y * 3.0 / grid.Height, 3);
            });
            var mountainShare = (double)mountains / count;
            var leyPower = Math.Clamp((leyMean - 0.5) * 250 + 40 + mountainShare * 20, 0, 100);

            var biome = biomes.GroupBy(b => b).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            var baseDanger = Math.Clamp(BiomeInfo.BaseDanger(biome) + leyPower / 5, 0, 100);
            var temperature = tiles.Average(i => grid.Temperature[i] / 255.0);

            var region = new Region
            {
                Id = ids.Take(Region.Kind),
                Name = names.Region(),
                TileCount = count,
                Center = layout.Centers[r],
                Biome = biome,
                Climate = temperature < 0.35 ? ClimateBand.Cold : temperature < 0.65 ? ClimateBand.Temperate : ClimateBand.Hot,
                Coastal = coastTiles > 0,
                HasRiver = riverTiles > 0,
                Farmland = Math.Round(farmland, 2),
                MountainShare = Math.Round(mountainShare, 3),
                BaseFertility = Math.Round(fertility, 1),
                Fertility = Math.Round(fertility, 1),
                BaseDanger = Math.Round(baseDanger, 1),
                Danger = Math.Round(Math.Clamp(baseDanger + rng.NextInt(-5, 6), 0, 100), 1),
                BaseLey = Math.Round(leyPower, 1),
                LeyPower = Math.Round(leyPower, 1),
            };
            if (region.Id != r) throw new InvalidOperationException("Region ids must follow layout order.");

            AddDeposit(region, ResourceKinds.Grain, farmland);
            AddDeposit(region, ResourceKinds.Timber, CountOf(Biome.Forest, Biome.Jungle, Biome.Taiga));
            AddDeposit(region, ResourceKinds.Ore, CountOf(Biome.Hills) * 0.5 + mountains);
            AddDeposit(region, ResourceKinds.Stone, mountains * 0.8 + CountOf(Biome.Hills) * 0.5 + CountOf(Biome.Desert) * 0.2);
            AddDeposit(region, ResourceKinds.Horses, CountOf(Biome.Grassland) * 0.6 + CountOf(Biome.Savanna) * 0.5);
            AddDeposit(region, ResourceKinds.Fish, coastTiles + riverTiles * 0.5);
            if (mountainShare >= 0.1 && rng.Chance(0.15 + leyPower / 400))
                AddDeposit(region, ResourceKinds.Mithril, Math.Max(1, mountains * 0.2));
            if (biome is Biome.Desert or Biome.Tundra or Biome.Mountain && rng.Chance(0.08))
                AddDeposit(region, ResourceKinds.Dragonbone, 5);
            if (leyPower >= 65 && rng.Chance(0.4))
                AddDeposit(region, ResourceKinds.ArcaneCrystal, leyPower / 10);

            regions.Add(region);
        }
        return regions;
    }

    private static void AddDeposit(Region region, string kind, double capacity)
    {
        if (capacity < 2 && !ResourceKinds.IsRare(kind)) return;
        var rounded = Math.Round(capacity, 1);
        region.Resources[kind] = new ResourceDeposit { Capacity = rounded, Yield = rounded };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~NameGenTests|FullyQualifiedName~RegionProfilerTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 168` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): profile regions with resources and names

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Settlement placement

1–4 settlements per region on the best sites, ranked worldwide into cities (one per region at most, only where farmland can feed one), towns, villages and hamlets. Each region starts with farmers within 80% of what its land can employ and six months of food, so the world does not famine-crash in year one.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/SettlementPlacer.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/SettlementPlacerTests.cs`

**Interfaces:**
- Consumes: `Region`, `Settlement`, `Demography` (Task 7); `RegionLayout` (Task 5); `NameGen`, `ResourceKinds` (Task 8).
- Produces: `static class SettlementPlacer { const int MinSpacing = 4; const double StartingFoodMonths = 6, MinCityFarmland = 270, StartingFarmLoad = 0.8; const int MinPopulation = 30; List<Settlement> Place(TerrainGrid, RegionLayout, IReadOnlyList<Region>, Rng, IdGen, NameGen); double SiteScore(TerrainGrid, int tile) }`. City count = clamp(regions / 15, 2, 8). Fills `Region.Settlements` and `Region.Food`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/SettlementPlacerTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class SettlementPlacerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, List<Region> Regions, List<Settlement> Settlements) Make(ulong seed = 2)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        var ids = new IdGen();
        var names = new NameGen(new Rng(seed + 2));
        var regions = RegionProfiler.Profile(grid, layout, seed, new Rng(seed + 1), ids, names);
        var settlements = SettlementPlacer.Place(grid, layout, regions, new Rng(seed + 3), ids, names);
        return (grid, layout, regions, settlements);
    }

    [Fact]
    public void EveryRegion_HasOneToFourSettlements()
    {
        var (_, _, regions, settlements) = Make();
        Assert.All(regions, r => Assert.InRange(r.Settlements.Count, 1, 4));
        Assert.Equal(settlements.Select(s => s.Id).OrderBy(i => i), regions.SelectMany(r => r.Settlements).OrderBy(i => i));
    }

    [Fact]
    public void Settlements_SitInsideTheirRegion_AndAreSpacedApart()
    {
        var (grid, layout, _, settlements) = Make();
        foreach (var s in settlements)
            Assert.Equal(s.RegionId, layout.RegionOf[grid.Index(s.Tile.X, s.Tile.Y)]);
        foreach (var group in settlements.GroupBy(s => s.RegionId))
            foreach (var a in group)
                foreach (var b in group.Where(b => b.Id > a.Id))
                    Assert.True(a.Tile.DistanceSquared(b.Tile) >= SettlementPlacer.MinSpacing * SettlementPlacer.MinSpacing);
    }

    [Fact]
    public void Tiers_IncludeCities_AtMostOnePerRegion()
    {
        var (_, _, _, settlements) = Make();
        var cities = settlements.Where(s => s.Tier == SettlementTier.City).ToList();
        Assert.InRange(cities.Count, 1, 8);
        Assert.Equal(cities.Count, cities.Select(c => c.RegionId).Distinct().Count());
        Assert.Contains(settlements, s => s.Tier == SettlementTier.Hamlet);
        Assert.All(settlements, s => Assert.Equal(s.Tier, Demography.TierFor(s.Population)));
    }

    [Fact]
    public void Regions_StartWithFarmersTheLandCanEmploy()
    {
        var (_, _, regions, settlements) = Make();
        foreach (var r in regions)
        {
            var farmers = settlements.Where(s => s.RegionId == r.Id).Sum(Demography.Farmers);
            Assert.True(farmers <= Demography.FarmCapacity(r) * SettlementPlacer.StartingFarmLoad + 30,
                $"{r.Name}: {farmers:0} farmers for capacity {Demography.FarmCapacity(r):0}");
        }
    }

    [Fact]
    public void Regions_StartWithSixMonthsOfFood()
    {
        var (_, _, regions, settlements) = Make();
        foreach (var r in regions)
        {
            var need = settlements.Where(s => s.RegionId == r.Id).Sum(Demography.Consumption);
            Assert.InRange(r.Food, need * 6 - 1, need * 6 + 1);
        }
    }

    [Fact]
    public void SiteScore_FavoursRiversAndCoasts()
    {
        var grid = new TerrainGrid(3, 1);
        grid.SetBiome(0, Biome.Grassland);
        grid.SetBiome(1, Biome.Grassland);
        grid.Set(1, TileFlags.River);
        grid.SetBiome(2, Biome.Mountain);
        Assert.True(SettlementPlacer.SiteScore(grid, 1) > SettlementPlacer.SiteScore(grid, 0));
        Assert.True(SettlementPlacer.SiteScore(grid, 0) > SettlementPlacer.SiteScore(grid, 2));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'SettlementPlacer' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/SettlementPlacer.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Founds 1–4 settlements per region on the best sites (rivers, coasts, farmland), then ranks every
/// site worldwide: the best become cities (at most one per region, only where farmland can feed one),
/// then towns, villages, hamlets. Regions start with farmers within 80% of their farm capacity.
/// </summary>
public static class SettlementPlacer
{
    public const int MinSpacing = 4;
    public const double StartingFoodMonths = 6;
    public const double MinCityFarmland = 270;
    public const double StartingFarmLoad = 0.8;
    public const int MinPopulation = 30;

    private sealed class Draft
    {
        public required Region Region { get; init; }
        public required int Tile { get; init; }
        public required SettlementTier Tier { get; set; }
        public int Population { get; set; }
    }

    public static List<Settlement> Place(TerrainGrid grid, RegionLayout layout, IReadOnlyList<Region> regions,
        Rng rng, IdGen ids, NameGen names)
    {
        var meanFarmland = regions.Average(r => r.Farmland);
        var sites = new List<(Region Region, int Tile, double Score)>();
        foreach (var region in regions)
        {
            var wanted = Math.Clamp((int)Math.Round(region.Farmland / Math.Max(1, meanFarmland) * 2), 1, 4);
            var ranked = layout.Tiles[(int)region.Id]
                .Select(i => (Tile: i, Score: SiteScore(grid, i)))
                .OrderByDescending(s => s.Score).ThenBy(s => s.Tile)
                .ToList();
            var chosen = new List<int>();
            foreach (var (tile, score) in ranked)
            {
                if (chosen.Count >= wanted) break;
                var c = grid.Coord(tile);
                if (chosen.Any(t => grid.Coord(t).DistanceSquared(c) < MinSpacing * MinSpacing)) continue;
                chosen.Add(tile);
                sites.Add((region, tile, score + region.Fertility / 20 + rng.NextDouble()));
            }
        }

        var cityCount = Math.Clamp(regions.Count / 15, 2, 8);
        var drafts = new List<Draft>();
        var regionsWithCity = new HashSet<long>();
        var ordered = sites.OrderByDescending(s => s.Score).ThenBy(s => s.Tile).ToList();
        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var (region, tile, _) = ordered[rank];
            var share = (double)rank / ordered.Count;
            SettlementTier tier;
            if (regionsWithCity.Count < cityCount && region.Farmland >= MinCityFarmland && regionsWithCity.Add(region.Id))
                tier = SettlementTier.City;
            else tier = share < 0.2 ? SettlementTier.Town : share < 0.55 ? SettlementTier.Village : SettlementTier.Hamlet;
            drafts.Add(new Draft { Region = region, Tile = tile, Tier = tier });
        }

        foreach (var draft in drafts.OrderBy(d => d.Region.Id).ThenBy(d => d.Tile))
            draft.Population = draft.Tier switch
            {
                SettlementTier.City => rng.NextInt(6000, 10_000),
                SettlementTier.Town => rng.NextInt(1500, 3500),
                SettlementTier.Village => rng.NextInt(300, 900),
                _ => rng.NextInt(60, 250),
            };
        foreach (var group in drafts.GroupBy(d => d.Region.Id)) FitToFarmland(group.ToList());

        var settlements = new List<Settlement>();
        foreach (var draft in drafts.OrderBy(d => d.Region.Id).ThenBy(d => d.Tile))
        {
            var (region, tile, tier, population) = (draft.Region, draft.Tile, draft.Tier, draft.Population);
            var children = population * 35 / 100;
            var adults = population * 55 / 100;
            var settlement = new Settlement
            {
                Id = ids.Take(Settlement.Kind),
                Name = names.Settlement(),
                RegionId = region.Id,
                Tile = grid.Coord(tile),
                Tier = tier,
                Children = children,
                Adults = adults,
                Elders = population - children - adults,
                Wealth = population * 0.5,
                Walls = tier switch { SettlementTier.City => 2, SettlementTier.Town => 1, _ => 0 },
                Garrison = tier >= SettlementTier.Town ? population / 50 : 0,
                Unrest = 10 + rng.NextInt(0, 11),
                Buildings = StartingBuildings(tier, region, grid.Has(tile, TileFlags.Coast)),
            };
            region.Settlements.Add(settlement.Id);
            settlements.Add(settlement);
        }

        foreach (var region in regions)
            region.Food = Math.Round(settlements.Where(s => s.RegionId == region.Id).Sum(Demography.Consumption) * StartingFoodMonths);
        return settlements;
    }

    /// <summary>
    /// Shrinks a crowded region's settlements so its farmers fit the land. Tiers follow the new sizes,
    /// and smaller tiers farm more, so repeat until it fits (or every settlement is at the minimum).
    /// </summary>
    private static void FitToFarmland(IReadOnlyList<Draft> drafts)
    {
        var limit = Demography.FarmCapacity(drafts[0].Region) * StartingFarmLoad;
        for (var pass = 0; pass < 10; pass++)
        {
            var farmers = drafts.Sum(d => d.Population * 0.55 * Demography.FarmShare(d.Tier));
            if (farmers <= limit || drafts.All(d => d.Population == MinPopulation)) return;
            var scale = limit / farmers;
            foreach (var d in drafts)
            {
                d.Population = Math.Max(MinPopulation, (int)(d.Population * scale));
                d.Tier = Demography.TierFor(d.Population);
            }
        }
    }

    public static double SiteScore(TerrainGrid grid, int tile)
    {
        var biome = grid.BiomeAt(tile);
        var score = BiomeInfo.Farmland(biome) * 3;
        if (grid.Has(tile, TileFlags.River)) score += 3;
        if (grid.Has(tile, TileFlags.Coast)) score += 2;
        if (biome == Biome.Mountain) score -= 10;
        if (biome == Biome.Hills) score -= 1;
        return score;
    }

    private static List<string> StartingBuildings(SettlementTier tier, Region region, bool coastal)
    {
        var buildings = new List<string>();
        if (tier >= SettlementTier.Village) buildings.Add("shrine");
        if (tier >= SettlementTier.Town) buildings.AddRange(new[] { "market", "temple" });
        if (tier >= SettlementTier.Town && coastal) buildings.Add("harbor");
        if (tier >= SettlementTier.Village && region.Resources.ContainsKey(ResourceKinds.Ore)) buildings.Add("forge");
        if (tier == SettlementTier.City && region.LeyPower >= 60) buildings.Add("mage tower");
        buildings.Sort(StringComparer.Ordinal);
        return buildings;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SettlementPlacerTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 174` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): place settlements sized to their farmland

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: World generator

One call from seed to a complete world (no prehistory yet; that is Plan 6). `Tiny` (64×64, 12 regions) keeps tests and demos fast; `Medium`/`Large` meet the spec's 100–120 regions.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/WorldGeneratorTests.cs`

**Interfaces:**
- Consumes: Tasks 3–9; `WorldFactory` (Plan 1).
- Produces: `enum WorldSize { Tiny, Medium, Large }`; `sealed record WorldGenOptions(ulong Seed, WorldSize Size = Medium) { MapSize; TargetRegions; TerrainOptions Terrain }`; `static class WorldGenerator { World Generate(WorldGenOptions, DateTimeOffset nowUtc) }`. Each step draws from `Rng.ForStream(seed, "worldgen/<step>", 0)`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/WorldGeneratorTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class WorldGeneratorTests
{
    private static World Tiny(ulong seed = 7) =>
        WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);

    [Fact]
    public void TinyWorld_IsCompleteAndValid()
    {
        var world = Tiny();
        Assert.NotNull(world.Geography);
        Assert.InRange(world.Regions.Count, 9, 16);
        Assert.True(world.Settlements.Count >= world.Regions.Count);
        Assert.Equal(world.Regions.Count, world.Geography!.Graph.Reachable(1).Count);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void SameSeed_SameWorld()
    {
        string Json(World w) => JsonSerializer.Serialize(new { w.Regions, w.Settlements, w.Geography });
        Assert.Equal(Json(Tiny(5)), Json(Tiny(5)));
        Assert.NotEqual(Json(Tiny(5)), Json(Tiny(6)));
    }

    [Fact]
    public void RegionAt_MatchesSettlementRegions()
    {
        var world = Tiny();
        Assert.All(world.Settlements.Values, s => Assert.Equal(s.RegionId, world.Geography!.RegionAt(s.Tile)));
    }

    [Fact]
    public void IdGenerator_ContinuesAfterGeneratedEntities()
    {
        var world = Tiny();
        Assert.Equal(world.Regions.Count + 1, world.Ids.Peek(Region.Kind));
        Assert.Equal(world.Settlements.Count + 1, world.Ids.Peek(Settlement.Kind));
    }

    [Fact]
    public void MediumWorld_MatchesTheSpec()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(42), DateTimeOffset.UnixEpoch);
        Assert.Equal(256, world.Geography!.Terrain.Width);
        Assert.InRange(world.Regions.Count, 95, 120);
        Assert.InRange(world.Settlements.Values.Count(s => s.Tier == SettlementTier.City), 6, 8);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'WorldGenerator' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Tiny is for tests and quick demos; Medium and Large are real worlds (spec: 100–120 regions).</summary>
public enum WorldSize { Tiny, Medium, Large }

public sealed record WorldGenOptions(ulong Seed, WorldSize Size = WorldSize.Medium)
{
    public int MapSize => Size == WorldSize.Tiny ? 64 : 256;

    public int TargetRegions => Size switch
    {
        WorldSize.Tiny => 12,
        WorldSize.Medium => 100,
        _ => 120,
    };

    public TerrainOptions Terrain
    {
        get
        {
            var defaults = new TerrainOptions();
            var land = MapSize * MapSize * defaults.LandFraction;
            return defaults with { Width = MapSize, Height = MapSize, MinIslandTiles = (int)(land / TargetRegions / 2) };
        }
    }
}

/// <summary>Seed → a complete world: terrain, rivers, regions, region graph, settlements. No history yet (Plan 6).</summary>
public static class WorldGenerator
{
    public static World Generate(WorldGenOptions options, DateTimeOffset nowUtc)
    {
        var seed = options.Seed;
        Rng Stream(string name) => Rng.ForStream(seed, "worldgen/" + name, 0);

        var terrain = TerrainGenerator.Generate(seed, options.Terrain);
        Hydrology.Apply(terrain);
        var layout = RegionPartitioner.Partition(terrain, options.TargetRegions, Stream("regions"));
        var graph = RegionGraphBuilder.Build(terrain, layout);

        var world = WorldFactory.CreateNew(seed, nowUtc);
        var names = new NameGen(Stream("names"));
        var regions = RegionProfiler.Profile(terrain, layout, seed, Stream("profile"), world.Ids, names);
        var settlements = SettlementPlacer.Place(terrain, layout, regions, Stream("settlements"), world.Ids, names);

        world.Geography = new Geography(terrain, layout.RegionOf, graph);
        foreach (var region in regions) world.Regions[region.Id] = region;
        foreach (var settlement in settlements) world.Settlements[settlement.Id] = settlement;
        return world;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~WorldGeneratorTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 179` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): generate complete worlds from a seed

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Persistence schema v2

Regions, settlements and ids go into the monthly snapshot. The static geography (~0.6 MB of JSON for a medium world) is written once to its own table rather than on every save. Version 1 files upgrade in place.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Persist/WorldHasher.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/Persist/WorldStore.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Persist/WorldStoreTests.cs`

**Interfaces:**
- Consumes: `World`, `Region`, `Settlement`, `Geography`, `IdGen` (Tasks 1, 7); `WorldGenerator` (Task 10, tests only).
- Produces: `WorldSnapshot` version 2 with optional `Ids`, `Regions`, `Settlements` (null when reading v1 snapshots); `WorldHasher` also hashes geography; `WorldStore.CurrentSchemaVersion = 2`, table `geography(id = 1, json)`, `bool HasGeography`; `Save` writes geography only if the table is empty; `Load` restores it.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Persist/WorldStoreTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.WorldGen;
using Microsoft.Data.Sqlite;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Persist;

public class WorldStoreTests
{
    private static Simulator NewSim(ulong seed = 5) =>
        new(WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    [Fact]
    public void NewFile_HasSchemaButNoWorld()
    {
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(WorldStore.CurrentSchemaVersion, store.SchemaVersion);
        Assert.False(store.HasWorld);
        Assert.Throws<InvalidOperationException>(() => store.Load());
    }

    [Fact]
    public void SaveThenLoad_RestoresIdenticalState()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(2000);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        Assert.Equal(Hash(sim), WorldHasher.Hash(world, log, queue));
    }

    [Fact]
    public void LoadedWorld_ContinuesIdentically()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(500);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

        sim.AdvanceDays(720);
        resumed.AdvanceDays(720);
        Assert.Equal(Hash(sim), Hash(resumed));
    }

    [Fact]
    public void RepeatedSaves_AppendOnlyNewEvents()
    {
        using var file = new TempFile();
        var sim = NewSim();
        using var store = WorldStore.Open(file.Path);

        sim.AdvanceDays(1000);
        store.Save(sim.World, sim.Log, sim.Queue);
        Assert.Equal(sim.Log.Count, store.CountEvents());

        sim.AdvanceDays(1000);
        store.Save(sim.World, sim.Log, sim.Queue);
        store.Save(sim.World, sim.Log, sim.Queue);
        Assert.Equal(sim.Log.Count, store.CountEvents());
        Assert.True(store.HasWorld);
    }

    [Fact]
    public void GeneratedWorld_RoundTripsWithGeography()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), Array.Empty<ISimSystem>(), checkInvariants: true);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        Assert.True(reopened.HasGeography);
        var (loaded, log, queue) = reopened.Load();
        Assert.Equal(Hash(sim), WorldHasher.Hash(loaded, log, queue));
        Assert.Equal(world.Regions.Count, loaded.Regions.Count);
        Assert.Equal(world.Geography!.Terrain.Biomes, loaded.Geography!.Terrain.Biomes);
        Assert.Equal(world.Ids.Peek("settlement"), loaded.Ids.Peek("settlement"));
    }

    [Fact]
    public void SchemaVersion1File_IsUpgraded()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(100);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);
        using (var connection = new SqliteConnection($"Data Source={file.Path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE geography; UPDATE meta SET value = '1' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }

        using var upgraded = WorldStore.Open(file.Path);
        Assert.Equal(2, upgraded.SchemaVersion);
        Assert.False(upgraded.HasGeography);
        Assert.Equal(Hash(sim), WorldHasher.Hash(upgraded.Load().World, sim.Log, sim.Queue));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'WorldStore' does not contain a definition for 'HasGeography' and no accessible extension method 'HasGeography' accepting a first argument of type 'WorldStore' could be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Persist/WorldHasher.cs` entirely with:
```csharp
using System.Security.Cryptography;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>SHA-256 over the serialized snapshot, geography and event log. Used to prove determinism.</summary>
public static class WorldHasher
{
    public static string Hash(World world, EventLog log, EventQueue queue)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(WorldSnapshot.Capture(world, queue), SimJson.Options));
        if (world.Geography is { } geography)
            sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(geography, SimJson.Options));
        sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(log.All, SimJson.Options));
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Serializable world + queue state. The event log and the static geography are stored separately.
/// Capture references the live entities, so serialize the snapshot immediately.
/// </summary>
public sealed record WorldSnapshot(
    int SchemaVersion,
    ulong Seed,
    long Day,
    DateTimeOffset LastRealUtc,
    long LastSeenEventId,
    SortedDictionary<string, double> Stats,
    SortedDictionary<string, long> Markers,
    List<ScheduledAction> Queue,
    long QueueNextSeq,
    SortedDictionary<string, long>? Ids = null,
    SortedDictionary<long, Region>? Regions = null,
    SortedDictionary<long, Settlement>? Settlements = null)
{
    public const int CurrentVersion = 2;

    public static WorldSnapshot Capture(World world, EventQueue queue) => new(
        CurrentVersion,
        world.Seed,
        world.Date.Day,
        world.LastRealUtc,
        world.LastSeenEventId,
        Sorted.From(world.Stats),
        Sorted.From(world.Markers),
        queue.Snapshot().ToList(),
        queue.NextSeq,
        Sorted.From(world.Ids.Next),
        world.Regions,
        world.Settlements);

    /// <summary>Rebuilds the world. Version 1 snapshots have no entities and load as an empty map.</summary>
    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
        Ids = new IdGen { Next = Ids ?? new SortedDictionary<string, long>() },
        Regions = new SortedDictionary<long, Region>(Regions ?? new SortedDictionary<long, Region>()),
        Settlements = new SortedDictionary<long, Settlement>(Settlements ?? new SortedDictionary<long, Settlement>()),
    };

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldStore.cs` entirely with:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot, static geography (written once) and the append-only event log.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 2;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS geography (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, day INTEGER NOT NULL, type TEXT NOT NULL, json TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_events_day ON events(day);
        CREATE INDEX IF NOT EXISTS ix_events_type ON events(type);
        CREATE TABLE IF NOT EXISTS event_causes (
            cause_id INTEGER NOT NULL, effect_id INTEGER NOT NULL, kind TEXT NOT NULL, weight REAL NOT NULL,
            PRIMARY KEY (cause_id, effect_id));
        CREATE TABLE IF NOT EXISTS event_actors (
            event_id INTEGER NOT NULL, role TEXT NOT NULL, kind TEXT NOT NULL, entity_id INTEGER NOT NULL,
            PRIMARY KEY (event_id, role));
        CREATE INDEX IF NOT EXISTS ix_event_actors_entity ON event_actors(kind, entity_id);
        """;

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    private WorldStore(SqliteConnection connection) => _connection = connection;

    public static WorldStore Open(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        var store = new WorldStore(connection);
        store.Migrate();
        return store;
    }

    public int SchemaVersion => int.Parse(GetMeta("schema_version")!, CultureInfo.InvariantCulture);

    public bool HasWorld => ScalarLong("SELECT COUNT(*) FROM snapshot") > 0;

    public long CountEvents() => ScalarLong("SELECT COUNT(*) FROM events");

    public bool HasGeography => ScalarLong("SELECT COUNT(*) FROM geography") > 0;

    public void Save(World world, EventLog log, EventQueue queue)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            foreach (var ev in log.Since(persistedMax)) InsertEvent(ev);

            if (world.Geography is { } geography && !HasGeography)
            {
                using var cmd = Command("INSERT INTO geography(id, json) VALUES (1, $json)");
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(geography, SimJson.Options));
                cmd.ExecuteNonQuery();
            }

            using (var cmd = Command("INSERT INTO snapshot(id, json) VALUES (1, $json) " +
                                     "ON CONFLICT(id) DO UPDATE SET json = excluded.json"))
            {
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options));
                cmd.ExecuteNonQuery();
            }
            SetMeta("world_day", world.Date.Day.ToString(CultureInfo.InvariantCulture));
            SetMeta("last_real_utc", world.LastRealUtc.ToString("O", CultureInfo.InvariantCulture));

            _transaction.Commit();
        }
        catch
        {
            _transaction.Rollback();
            throw;
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public (World World, EventLog Log, EventQueue Queue) Load()
    {
        string json;
        using (var cmd = Command("SELECT json FROM snapshot WHERE id = 1"))
            json = cmd.ExecuteScalar() as string ?? throw new InvalidOperationException("No world has been saved in this file.");
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)
            ?? throw new InvalidOperationException("World snapshot is empty.");

        var log = new EventLog();
        using (var cmd = Command("SELECT json FROM events ORDER BY id"))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                log.Restore(JsonSerializer.Deserialize<WorldEvent>(reader.GetString(0), SimJson.Options)!);

        var world = snapshot.ToWorld();
        using (var cmd = Command("SELECT json FROM geography WHERE id = 1"))
        {
            if (cmd.ExecuteScalar() is string geography)
                world.Geography = JsonSerializer.Deserialize<Geography>(geography, SimJson.Options);
        }

        return (world, log, snapshot.ToQueue());
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        using (var cmd = Command("PRAGMA journal_mode=WAL;")) cmd.ExecuteNonQuery();
        using (var cmd = Command(Schema)) cmd.ExecuteNonQuery();
        var version = GetMeta("schema_version");
        var current = version is null ? 0 : int.Parse(version, CultureInfo.InvariantCulture);
        if (current > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"World file schema {version} is newer than this build supports ({CurrentSchemaVersion}).");
        // 1 → 2 only adds the geography table, which the schema script above creates.
        if (current < CurrentSchemaVersion)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private void InsertEvent(WorldEvent ev)
    {
        using (var cmd = Command("INSERT INTO events(id, day, type, json) VALUES ($id, $day, $type, $json)"))
        {
            cmd.Parameters.AddWithValue("$id", ev.Id);
            cmd.Parameters.AddWithValue("$day", ev.Date.Day);
            cmd.Parameters.AddWithValue("$type", ev.Type);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(ev, SimJson.Options));
            cmd.ExecuteNonQuery();
        }
        foreach (var cause in ev.Causes)
        {
            using var cmd = Command("INSERT INTO event_causes(cause_id, effect_id, kind, weight) VALUES ($c, $e, $k, $w)");
            cmd.Parameters.AddWithValue("$c", cause.EventId);
            cmd.Parameters.AddWithValue("$e", ev.Id);
            cmd.Parameters.AddWithValue("$k", cause.Kind.ToString());
            cmd.Parameters.AddWithValue("$w", cause.Weight);
            cmd.ExecuteNonQuery();
        }
        var actors = ev.Actors.ToList();
        if (ev.Place is { } place) actors.Add(new KeyValuePair<string, EntityRef>("@place", place));
        foreach (var (role, entity) in actors)
        {
            using var cmd = Command("INSERT INTO event_actors(event_id, role, kind, entity_id) VALUES ($e, $r, $k, $i)");
            cmd.Parameters.AddWithValue("$e", ev.Id);
            cmd.Parameters.AddWithValue("$r", role);
            cmd.Parameters.AddWithValue("$k", entity.Kind);
            cmd.Parameters.AddWithValue("$i", entity.Id);
            cmd.ExecuteNonQuery();
        }
    }

    private string? GetMeta(string key)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $key");
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var cmd = Command("INSERT INTO meta(key, value) VALUES ($key, $value) " +
                                "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private long ScalarLong(string sql)
    {
        using var cmd = Command(sql);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private SqliteCommand Command(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _transaction;
        return cmd;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~WorldStoreTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 181` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(persist): store geography and entities (schema v2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Economy system

Each region pools its settlements' food. Farmers (capped by farmland) harvest by season and fertility, coasts and rivers add fish, everyone eats; empty stores starve people. A store below one month's need begins a `FoodShortageBegan` condition event that **cites the drought, blight, flood, plague or exhausted soil behind it**, which is the causal core of this plan. Yearly births, ageing and deaths make populations grow slowly when fed.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/Conditions.cs`, `sim/FantasyWorld.Sim/Systems/EconomySystem.cs`
- Modify: `sim/FantasyWorld.Sim/Model/Demography.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/EconomySystemTests.cs`

**Interfaces:**
- Consumes: `ISimSystem.OnYearlyTick` (Task 1); `Region`, `Settlement`, `Demography`, `ResourceKinds` (Tasks 7–8); `EventDraft`, `Cause` (Plan 1).
- Produces: `Demography.RoundRandom(double, Rng)` and `Demography.Kill(Settlement, double fraction, Rng)`; `static class Conditions` (condition names `shortage, drought, blight, flood, plague, immune, exhausted, leySurge, dangerous, revolt`; `Cite(active, params (condition, kind, weight)[])`); `sealed class EconomySystem : ISimSystem` (name `"economy"`; consts `FoodPerFarmerMonth = 3.2`, `FishFoodPerYield = 4`, `ShortageBelowMonths = 1`, `ShortageEndsAtMonths = 3`, `MaxStoreMonths = 12`, `CraftWealthPerWorker = 0.02`; `static double Production(SimContext, Region, IReadOnlyList<Settlement>)`). Events: `FoodShortageBegan` (payload `months`), `FoodShortageEnded`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/EconomySystemTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class EconomySystemTests
{
    private static Simulator Make(World world) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new EconomySystem() }, checkInvariants: true);

    [Fact]
    public void HealthyRegion_StaysFed_ThroughAYear()
    {
        var sim = Make(TestWorlds.TwoRegions());
        sim.AdvanceDays(360);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "FoodShortageBegan");
        Assert.True(sim.World.Regions[1].Food > 0);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(4, 1.0)]
    [InlineData(7, 2.0)]
    [InlineData(10, 0.0)]
    public void Production_FollowsTheSeasons(int month, double weight)
    {
        var world = TestWorlds.TwoRegions();
        world.Date = WorldDate.FromYmd(1, month, 1);
        var region = world.Regions[1];
        var settlements = world.LivingSettlements(region).ToList();
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        var expected = settlements.Sum(Demography.Farmers) * EconomySystem.FoodPerFarmerMonth * region.Fertility / 50 * weight;
        Assert.Equal(expected, EconomySystem.Production(ctx, region, settlements), 6);
    }

    [Fact]
    public void EmptyStores_BeginAShortage_CitingTheDrought()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var drought = log.Append(world.Date, new EventDraft("Drought"));
        var region = world.Regions[1];
        region.Food = 0;
        region.Conditions[Conditions.Drought] = drought.Id;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new EconomySystem() }, checkInvariants: true);

        sim.AdvanceDays(30);

        var shortage = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageBegan");
        Assert.Equal(region.Ref, shortage.Place);
        Assert.Equal(shortage.Id, region.Conditions[Conditions.Shortage]);
        Assert.Equal("Drought", Assert.Single(new HistoryQuery(sim.Log).RootCauses(shortage.Id)).Type);
    }

    [Fact]
    public void Shortage_EndsOnceStoresRecover()
    {
        var world = TestWorlds.TwoRegions();
        world.Regions[1].Food = 0;
        var sim = Make(world);
        sim.AdvanceDays(30);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageBegan");

        world.Regions[1].Food = 100_000;
        sim.AdvanceDays(30);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "FoodShortageEnded");
        Assert.Equal(shortage.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(world.Regions[1].Conditions.ContainsKey(Conditions.Shortage));
    }

    [Fact]
    public void Starvation_KillsWhenNothingIsProduced()
    {
        var world = TestWorlds.TwoRegions();
        var region = world.Regions[1];
        region.Food = 0;
        region.Fertility = 0;
        var before = world.Settlements[1].Population;
        Make(world).AdvanceDays(90);
        Assert.InRange(world.Settlements[1].Population, before * 0.8, before * 0.9);
    }

    [Fact]
    public void FarmersBeyondFarmland_ProduceNothingExtra()
    {
        var world = TestWorlds.TwoRegions();
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        world.Date = WorldDate.FromYmd(1, 7, 1);
        var region = world.Regions[1];
        var settlements = world.LivingSettlements(region).ToList();
        var normal = EconomySystem.Production(ctx, region, settlements);
        settlements[0].Adults *= 10;
        var crowded = EconomySystem.Production(ctx, region, settlements);
        Assert.Equal(Demography.FarmCapacity(region) * EconomySystem.FoodPerFarmerMonth * 60 / 50 * 2, crowded, 3);
        Assert.True(crowded < normal * 10);
    }

    [Fact]
    public void Population_GrowsSlowlyWhenFed()
    {
        var world = TestWorlds.TwoRegions();
        var before = world.Settlements[1].Population;
        Make(world).AdvanceDays(3600);
        Assert.InRange(world.Settlements[1].Population, before * 1.03, before * 1.25);
    }

    [Fact]
    public void EmptyRegion_StoresNothing()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Abandoned = true;
        world.Regions[1].Resources[ResourceKinds.Fish] = new ResourceDeposit { Capacity = 50, Yield = 50 };
        var before = world.Regions[1].Food;
        Make(world).AdvanceDays(360);
        Assert.Equal(before, world.Regions[1].Food);
    }

    [Fact]
    public void Ageing_NeverCreatesPeople()
    {
        var world = TestWorlds.TwoRegions();
        var s = world.Settlements[1];
        var sim = Make(world);
        for (var year = 0; year < 200; year++)
        {
            (s.Children, s.Adults, s.Elders) = (1, 0, 0);
            sim.AdvanceDays(360);
            Assert.InRange(s.Population, 0, 1);
        }
    }

    [Fact]
    public void Settlements_EarnWealth()
    {
        var world = TestWorlds.TwoRegions();
        var before = world.Settlements[1].Wealth;
        Make(world).AdvanceDays(360);
        Assert.True(world.Settlements[1].Wealth > before);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'EconomySystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Model/Demography.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>Shared population arithmetic used by world generation and the economy.</summary>
public static class Demography
{
    public const double ChildFood = 0.6;
    public const double ElderFood = 0.8;

    /// <summary>Food eaten per month, in person-months.</summary>
    public static double Consumption(Settlement s) => s.Children * ChildFood + s.Adults + s.Elders * ElderFood;

    /// <summary>Share of adults who farm; the rest are crafters, traders and soldiers.</summary>
    public static double FarmShare(SettlementTier tier) => tier switch
    {
        SettlementTier.Hamlet => 0.9,
        SettlementTier.Village => 0.85,
        SettlementTier.Town => 0.65,
        _ => 0.45,
    };

    public static double Farmers(Settlement s) => s.Adults * FarmShare(s.Tier);

    /// <summary>How many farmers one tile of farmland can usefully employ.</summary>
    public const double FarmersPerFarmland = 12;

    /// <summary>Farmers beyond this add nothing: the land is fully worked.</summary>
    public static double FarmCapacity(Region region) => region.Farmland * FarmersPerFarmland;

    /// <summary>The tier a settlement of this size grows into (no hysteresis).</summary>
    public static SettlementTier TierFor(int population) => population switch
    {
        >= 6000 => SettlementTier.City,
        >= 1500 => SettlementTier.Town,
        >= 300 => SettlementTier.Village,
        _ => SettlementTier.Hamlet,
    };

    /// <summary>Rounds up with probability equal to the fraction, so small settlements still change.</summary>
    public static int RoundRandom(double value, Rng rng)
    {
        var floor = Math.Floor(value);
        return (int)floor + (rng.NextDouble() < value - floor ? 1 : 0);
    }

    /// <summary>Removes a share of each age band (children and elders are hit harder). Returns the dead.</summary>
    public static int Kill(Settlement s, double fraction, Rng rng)
    {
        if (fraction <= 0) return 0;
        var children = Math.Min(s.Children, RoundRandom(s.Children * Math.Min(1, fraction * 1.5), rng));
        var adults = Math.Min(s.Adults, RoundRandom(s.Adults * Math.Min(1, fraction), rng));
        var elders = Math.Min(s.Elders, RoundRandom(s.Elders * Math.Min(1, fraction * 1.5), rng));
        s.Children -= children;
        s.Adults -= adults;
        s.Elders -= elders;
        return children + adults + elders;
    }
}
```

`sim/FantasyWorld.Sim/Systems/Conditions.cs`:
```csharp
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Condition names shared by systems through Region.Conditions / Settlement.Conditions. Each maps
/// to the event that began it, so later events can cite it as a cause.
/// </summary>
public static class Conditions
{
    public const string Shortage = "shortage";
    public const string Drought = "drought";
    public const string Blight = "blight";
    public const string Flood = "flood";
    public const string Plague = "plague";
    public const string Immune = "immune";
    public const string Exhausted = "exhausted";
    public const string LeySurge = "leySurge";
    public const string Dangerous = "dangerous";
    public const string Revolt = "revolt";

    /// <summary>Causes for the listed active conditions, in the order given (strongest first).</summary>
    public static IReadOnlyList<Cause> Cite(
        SortedDictionary<string, long> active, params (string Condition, CauseKind Kind, double Weight)[] wanted) =>
        wanted.Where(w => active.ContainsKey(w.Condition))
            .Select(w => new Cause(active[w.Condition], w.Kind, w.Weight))
            .ToList();
}
```

`sim/FantasyWorld.Sim/Systems/EconomySystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Food, labour, wealth and population. Each region pools the food of its settlements: farmers
/// (capped by farmland) produce by season and fertility, coasts and rivers add fish, everyone eats.
/// Stores below a month of need begin a shortage, which cites whatever conditions caused it.
/// </summary>
public sealed class EconomySystem : ISimSystem
{
    public const double FoodPerFarmerMonth = 3.2;
    public const double FishFoodPerYield = 4;
    public const double ShortageBelowMonths = 1;
    public const double ShortageEndsAtMonths = 3;
    public const double MaxStoreMonths = 12;
    public const double CraftWealthPerWorker = 0.02;

    /// <summary>Harvest weight per season: Spring, Summer, Autumn, Winter.</summary>
    private static readonly double[] SeasonWeight = { 0.5, 1.0, 2.0, 0.0 };

    private static readonly (string Condition, double Factor)[] ConditionYield =
    {
        (Conditions.Drought, 0.4),
        (Conditions.Blight, 0.6),
        (Conditions.Plague, 0.8),
    };

    private static readonly IReadOnlyDictionary<string, double> ResourceValue = new Dictionary<string, double>
    {
        [ResourceKinds.Timber] = 0.05,
        [ResourceKinds.Ore] = 0.1,
        [ResourceKinds.Stone] = 0.05,
        [ResourceKinds.Horses] = 0.1,
        [ResourceKinds.Mithril] = 2,
        [ResourceKinds.Dragonbone] = 3,
        [ResourceKinds.ArcaneCrystal] = 2,
    };

    public string Name => "economy";

    public void OnMonthlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
        {
            var settlements = ctx.World.LivingSettlements(region).ToList();
            if (settlements.Count == 0) continue;
            var need = settlements.Sum(Demography.Consumption);
            region.Food = Math.Max(0, region.Food + Production(ctx, region, settlements) - need);
            if (need > 0)
            {
                if (region.Food == 0) Starve(ctx, region, settlements, need);
                region.Food = Math.Min(region.Food, need * MaxStoreMonths);
            }
            UpdateShortage(ctx, region, settlements, need);
            Trade(region, settlements);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
        {
            var hungry = region.Conditions.ContainsKey(Conditions.Shortage);
            foreach (var s in ctx.World.LivingSettlements(region)) AgeOneYear(s, hungry, ctx.Rng);
        }
    }

    /// <summary>Food made this month, before anyone eats.</summary>
    public static double Production(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var farmers = Math.Min(settlements.Sum(Demography.Farmers), Demography.FarmCapacity(region));
        var yield = region.Fertility / 50 * SeasonWeight[(int)ctx.Date.Season];
        foreach (var (condition, factor) in ConditionYield)
            if (region.Conditions.ContainsKey(condition)) yield *= factor;
        var fish = region.Resources.TryGetValue(ResourceKinds.Fish, out var deposit) ? deposit.Yield * FishFoodPerYield : 0;
        return farmers * FoodPerFarmerMonth * yield + fish;
    }

    /// <summary>Empty stores: up to 5% of people die per month, in proportion to how much food was missing.</summary>
    private static void Starve(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements, double need)
    {
        var produced = Production(ctx, region, settlements);
        var shortfall = Math.Clamp(1 - produced / need, 0, 1);
        foreach (var s in settlements) Demography.Kill(s, 0.05 * shortfall, ctx.Rng);
    }

    private static void UpdateShortage(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements, double need)
    {
        var months = need > 0 ? region.Food / need : double.PositiveInfinity;
        var active = region.Conditions.TryGetValue(Conditions.Shortage, out var shortageId);
        if (!active && months < ShortageBelowMonths)
        {
            var hasCity = settlements.Any(s => s.Tier == SettlementTier.City);
            var shortage = ctx.Emit(new EventDraft("FoodShortageBegan",
                Place: region.Ref,
                Payload: new Dictionary<string, string> { ["months"] = months.ToString("0.0", CultureInfo.InvariantCulture) },
                Causes: Conditions.Cite(region.Conditions,
                    (Conditions.Drought, CauseKind.Trigger, 0.9),
                    (Conditions.Blight, CauseKind.Trigger, 0.8),
                    (Conditions.Flood, CauseKind.Trigger, 0.7),
                    (Conditions.Plague, CauseKind.Enabler, 0.6),
                    (Conditions.Exhausted, CauseKind.Enabler, 0.5)),
                Importance: hasCity ? 45 : 30,
                Tags: new[] { "Economy" }));
            region.Conditions[Conditions.Shortage] = shortage.Id;
        }
        else if (active && months >= ShortageEndsAtMonths)
        {
            ctx.Emit(new EventDraft("FoodShortageEnded",
                Place: region.Ref,
                Causes: new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) },
                Importance: 15,
                Tags: new[] { "Economy" }));
            region.Conditions.Remove(Conditions.Shortage);
        }
    }

    /// <summary>Crafters and the region's non-food resources earn wealth; extraction wears deposits down.</summary>
    private static void Trade(Region region, IReadOnlyList<Settlement> settlements)
    {
        var population = settlements.Sum(s => s.Population);
        var resourceIncome = 0.0;
        foreach (var (kind, deposit) in region.Resources)
        {
            if (!ResourceValue.TryGetValue(kind, out var value)) continue;
            resourceIncome += deposit.Yield * value;
            var extraction = deposit.Yield * 0.01 * Math.Min(1, population / 5000.0);
            deposit.Yield = Math.Clamp(deposit.Yield - extraction + (deposit.Capacity - deposit.Yield) * 0.02, 0, deposit.Capacity);
        }
        foreach (var s in settlements)
        {
            var crafters = s.Adults - Demography.Farmers(s);
            var share = population > 0 ? (double)s.Population / population : 0;
            s.Wealth += crafters * CraftWealthPerWorker * (1 + (int)s.Tier * 0.5) + resourceIncome * share;
        }
    }

    private static void AgeOneYear(Settlement s, bool hungry, Rng rng)
    {
        var mortality = hungry ? 1.5 : 1.0;
        var births = Demography.RoundRandom(s.Adults * 0.05 * (hungry ? 0.5 : 1.0), rng);
        // Each person leaves a band at most once: clamp so no one is created from nothing.
        var childDeaths = Math.Min(s.Children, Demography.RoundRandom(s.Children * 0.012 * mortality, rng));
        var grownUp = Math.Min(s.Children - childDeaths, Demography.RoundRandom(s.Children / 14.0, rng));
        var adultDeaths = Math.Min(s.Adults, Demography.RoundRandom(s.Adults * 0.01 * mortality, rng));
        var aged = Math.Min(s.Adults - adultDeaths, Demography.RoundRandom(s.Adults / 35.0, rng));
        var elderDeaths = Math.Min(s.Elders, Demography.RoundRandom(s.Elders * 0.09 * mortality, rng));
        s.Children += births - grownUp - childDeaths;
        s.Adults += grownUp - aged - adultDeaths;
        s.Elders += aged - elderDeaths;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~EconomySystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 13`. Then `dotnet test sim` → `Passed: 194` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add economy system with food, shortages and population

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Region system and natural events

The land lives: fertility, danger and ley power drift (overfarming exhausts soil, cities tame the wilds); droughts, floods, blights, plagues, ley surges, earthquakes, monster migrations and discoveries happen with seasonal gating, each citing what enabled it. Plague spreads along the region graph and leaves ten years of immunity, so it burns out instead of becoming endemic. Odds are injectable, so tests can force or silence each event.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/RegionSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/RegionSystemTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Support/TestWorlds.cs`

**Interfaces:**
- Consumes: `Conditions`, `Demography.Kill` (Task 12); `RegionGraph` (Task 6); `Region`, `World.Geography` (Task 7).
- Produces: `sealed record RegionOdds(Drought = 0.06, Flood = 0.05, Blight = 0.02, Plague = 0.008, LeySurge = 0.02, Earthquake = 0.01, Monsters = 0.08, Discovery = 0.015, PlagueSpreadLand = 0.04, PlagueSpreadSea = 0.02) { static None }` (yearly odds rolled monthly as odds / 12; spread odds are per month per link); `sealed class RegionSystem(RegionOdds? odds = null) : ISimSystem` (name `"regions"`, handles `"region.condition.end"`; consts `MemoryCursor`, `MemorySize = 12`, `MemoryImportance = 35`, `ImmunityDays = 3600`). Events: `Drought/DroughtEnded`, `Flood`, `Blight/BlightEnded`, `PlagueOutbreak/PlagueSpread/PlagueEnded`, `LeySurge`, `Earthquake`, `MonstersMigrated`, `ResourceDiscovered`, `FertilityExhausted/FertilityRecovered`, `DangerRose/DangerSubsided`. Test support: `TestWorlds.TwoRegions` now links its regions and accepts a mountain share.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Support/TestWorlds.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Hand-built worlds for system tests: two linked regions, one settlement each.</summary>
public static class TestWorlds
{
    public static World TwoRegions(ulong seed = 1, double westMountainShare = 0)
    {
        var world = WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch);
        AddRegion(world, "Westmark", westMountainShare);
        AddRegion(world, "Eastmoor");
        AddSettlement(world, 1, SettlementTier.Village, 1000);
        AddSettlement(world, 2, SettlementTier.Village, 1000);
        world.Geography = new Geography(new TerrainGrid(2, 1), new[] { 1, 2 },
            new RegionGraph(new[] { new RegionLink(1, 2, LinkKind.Land, 3, 10, false, false) }));
        return world;
    }

    public static Region AddRegion(World world, string name, double mountainShare = 0)
    {
        var region = new Region
        {
            Id = world.Ids.Take(Region.Kind),
            Name = name,
            TileCount = 100,
            Biome = Biome.Grassland,
            Climate = ClimateBand.Temperate,
            Farmland = 100,
            MountainShare = mountainShare,
            BaseFertility = 60,
            Fertility = 60,
            BaseDanger = 20,
            Danger = 20,
            BaseLey = 30,
            LeyPower = 30,
        };
        world.Regions[region.Id] = region;
        return region;
    }

    public static Settlement AddSettlement(World world, long regionId, SettlementTier tier, int population)
    {
        var settlement = new Settlement
        {
            Id = world.Ids.Take(Settlement.Kind),
            Name = $"Town{world.Settlements.Count + 1}",
            RegionId = regionId,
            Tier = tier,
            Children = population * 35 / 100,
            Adults = population * 55 / 100,
            Elders = population - population * 35 / 100 - population * 55 / 100,
            Wealth = 100,
            Unrest = 10,
        };
        world.Settlements[settlement.Id] = settlement;
        var region = world.Regions[regionId];
        region.Settlements.Add(settlement.Id);
        region.Food = Demography.Consumption(settlement) * 6 + region.Food;
        return settlement;
    }
}
```

`sim/FantasyWorld.Sim.Tests/Systems/RegionSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class RegionSystemTests
{
    private const double Always = 12;

    private static Simulator Make(World world, RegionOdds odds, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new RegionSystem(odds) }, checkInvariants: true);

    [Fact]
    public void QuietOdds_ProduceNoRandomEvents()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None);
        sim.AdvanceDays(3600);
        Assert.Empty(sim.Log.All);
    }

    [Fact]
    public void Drought_StrikesInSummer_AndEndsInAutumn()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Drought = Always });
        sim.AdvanceDays(360);

        var drought = sim.Log.All.First(e => e.Type == "Drought");
        Assert.Equal(WorldDate.FromYmd(1, 4, 1), drought.Date);
        var ended = sim.Log.All.First(e => e.Type == "DroughtEnded" && e.Causes[0].EventId == drought.Id);
        Assert.Equal(WorldDate.FromYmd(1, 10, 1), ended.Date);
        Assert.False(sim.World.Regions[drought.Place!.Value.Id].Conditions.ContainsKey(Conditions.Drought));
    }

    [Fact]
    public void Plague_SpreadsAlongLinks_CitingTheOutbreak()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var outbreak = log.Append(world.Date, new EventDraft("PlagueOutbreak"));
        world.Regions[2].Conditions[Conditions.Plague] = outbreak.Id;
        var sim = Make(world, RegionOdds.None with { PlagueSpreadLand = 1 }, log);

        sim.AdvanceDays(30);

        var spread = Assert.Single(sim.Log.All, e => e.Type == "PlagueSpread");
        Assert.Equal(world.Regions[1].Ref, spread.Place);
        Assert.Equal(world.Regions[2].Ref, spread.Actors["from"]);
        Assert.Equal(outbreak.Id, Assert.Single(spread.Causes).EventId);
    }

    [Fact]
    public void RegionsThatSurvivedPlague_AreImmuneForAWhile()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Plague = Always, PlagueSpreadLand = 1 });
        sim.AdvanceDays(360 * 5);

        var outbreaks = sim.Log.All.Where(e => e.Type is "PlagueOutbreak" or "PlagueSpread").ToList();
        Assert.Equal(2, outbreaks.Count);
        var ended = sim.Log.All.First(e => e.Type == "PlagueEnded");
        Assert.Equal(ended.Id, sim.World.Regions[ended.Place!.Value.Id].Conditions[Conditions.Immune]);
    }

    [Fact]
    public void Plague_KillsEachMonth()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        world.Regions[1].Conditions[Conditions.Plague] = log.Append(world.Date, new EventDraft("PlagueOutbreak")).Id;
        var before = world.Settlements[1].Population;
        Make(world, RegionOdds.None, log).AdvanceDays(60);
        Assert.InRange(world.Settlements[1].Population, before * 0.94, before * 0.985);
    }

    [Fact]
    public void Monsters_LeaveADangerousRegion_ForItsNeighbour()
    {
        var world = TestWorlds.TwoRegions();
        world.Regions[1].Danger = 90;
        var sim = Make(world, RegionOdds.None with { Monsters = Always });

        sim.AdvanceDays(30);

        var rose = Assert.Single(sim.Log.All, e => e.Type == "DangerRose");
        var migrated = Assert.Single(sim.Log.All, e => e.Type == "MonstersMigrated");
        Assert.Equal(world.Regions[2].Ref, migrated.Place);
        Assert.Equal(rose.Id, Assert.Single(migrated.Causes).EventId);
        Assert.True(world.Regions[2].Danger > 30);
    }

    [Fact]
    public void Earthquake_IsTriggeredByALeySurge()
    {
        var world = TestWorlds.TwoRegions(westMountainShare: 0.5);
        var sim = Make(world, RegionOdds.None with { LeySurge = Always, Earthquake = Always });

        sim.AdvanceDays(30);

        var surge = sim.Log.All.First(e => e.Type == "LeySurge");
        var quake = sim.Log.All.First(e => e.Type == "Earthquake");
        Assert.Equal(surge.Id, Assert.Single(quake.Causes).EventId);
    }

    [Fact]
    public void Overfarming_ExhaustsTheSoil()
    {
        var world = TestWorlds.TwoRegions();
        var region = world.Regions[1];
        region.Fertility = 42.1;
        world.Settlements[1].Adults = 50_000;
        var sim = Make(world, RegionOdds.None);

        sim.AdvanceDays(30);

        var exhausted = Assert.Single(sim.Log.All, e => e.Type == "FertilityExhausted");
        Assert.Equal(exhausted.Id, region.Conditions[Conditions.Exhausted]);
    }

    [Fact]
    public void ImportantRegionEvents_AreRemembered()
    {
        var sim = Make(TestWorlds.TwoRegions(), RegionOdds.None with { Plague = Always });
        sim.AdvanceDays(60);
        var outbreak = sim.Log.All.First(e => e.Type == "PlagueOutbreak" && e.Place == sim.World.Regions[1].Ref);
        Assert.Contains(outbreak.Id, sim.World.Regions[1].Memory);
    }

    [Fact]
    public void Discovery_AddsAResource()
    {
        var world = TestWorlds.TwoRegions();
        var sim = Make(world, RegionOdds.None with { Discovery = Always });
        sim.AdvanceDays(30);
        var found = sim.Log.All.Where(e => e.Type == "ResourceDiscovered" && e.Place == world.Regions[1].Ref).ToList();
        Assert.Single(found);
        Assert.Contains(found[0].Payload["resource"], world.Regions[1].Resources.Keys);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'RegionOdds' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/RegionSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Yearly odds of each region event (rolled monthly as odds / 12). Plague spread odds are per month
/// per link. Tests pass custom odds to force or silence events.
/// </summary>
public sealed record RegionOdds(
    double Drought = 0.06,
    double Flood = 0.05,
    double Blight = 0.02,
    double Plague = 0.008,
    double LeySurge = 0.02,
    double Earthquake = 0.01,
    double Monsters = 0.08,
    double Discovery = 0.015,
    double PlagueSpreadLand = 0.04,
    double PlagueSpreadSea = 0.02)
{
    public static readonly RegionOdds None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The land itself: fertility, danger and ley power drift; droughts, floods, blights, plagues,
/// ley surges, earthquakes, monster migrations and discoveries happen, each citing what enabled it.
/// Also records each region's defining events in Region.Memory.
/// </summary>
public sealed class RegionSystem : ISimSystem
{
    public const string ConditionEndKind = "region.condition.end";
    public const string MemoryCursor = "regions.memoryCursor";
    public const int MemorySize = 12;
    public const int MemoryImportance = 35;
    public const int ImmunityDays = 10 * 360;

    private readonly RegionOdds _odds;

    public RegionSystem(RegionOdds? odds = null) => _odds = odds ?? new RegionOdds();

    public string Name => "regions";

    public IEnumerable<string> HandledKinds => new[] { ConditionEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var plagued = world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.Plague)).ToList();
        foreach (var region in world.Regions.Values)
        {
            var settlements = world.LivingSettlements(region).ToList();
            Drift(ctx, region, settlements);
            RollEvents(ctx, region, settlements);
            if (region.Conditions.ContainsKey(Conditions.Plague))
                foreach (var s in settlements) Demography.Kill(s, 0.015, ctx.Rng);
        }
        SpreadPlague(ctx, plagued);
        Remember(ctx);
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var region = ctx.World.Regions[long.Parse(action.Data["region"], CultureInfo.InvariantCulture)];
        var condition = action.Data["condition"];
        var began = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!region.Conditions.TryGetValue(condition, out var current) || current != began) return;
        region.Conditions.Remove(condition);
        if (action.Data["end"] is not { Length: > 0 } endType) return;
        var ended = ctx.Emit(new EventDraft(endType, Place: region.Ref,
            Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: 10, Tags: new[] { "Regions" }));
        if (condition == Conditions.Plague) Hold(ctx, region, Conditions.Immune, ended.Id, ImmunityDays);
    }

    private static void Drift(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var farmers = settlements.Sum(Demography.Farmers);
        var capacity = Demography.FarmCapacity(region);
        region.Fertility = capacity > 0 && farmers > capacity * 0.9
            ? Math.Max(0, region.Fertility - 0.2)
            : Math.Clamp(region.Fertility + (region.BaseFertility - region.Fertility) * 0.02, 0, 100);

        var population = settlements.Sum(s => s.Population);
        var dangerTarget = Math.Max(0, region.BaseDanger - Math.Min(20, population / 500.0));
        region.Danger = Math.Clamp(region.Danger + (dangerTarget - region.Danger) * 0.03, 0, 100);
        region.LeyPower = Math.Clamp(region.LeyPower + (region.BaseLey - region.LeyPower) * 0.01, 0, 100);

        Threshold(ctx, region, Conditions.Exhausted,
            begins: region.Fertility < region.BaseFertility * 0.7, ends: region.Fertility >= region.BaseFertility * 0.85,
            "FertilityExhausted", "FertilityRecovered", importance: 25, Array.Empty<Cause>());
        Threshold(ctx, region, Conditions.Dangerous,
            begins: region.Danger >= 70, ends: region.Danger < 50,
            "DangerRose", "DangerSubsided", importance: 30,
            Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.7)));
    }

    /// <summary>Condition events for a value crossing a threshold, with hysteresis.</summary>
    private static void Threshold(SimContext ctx, Region region, string condition, bool begins, bool ends,
        string beginType, string endType, int importance, IReadOnlyList<Cause> causes)
    {
        var active = region.Conditions.TryGetValue(condition, out var began);
        if (!active && begins)
            region.Conditions[condition] = ctx.Emit(new EventDraft(beginType, Place: region.Ref, Causes: causes,
                Importance: importance, Tags: new[] { "Regions" })).Id;
        else if (active && ends)
        {
            ctx.Emit(new EventDraft(endType, Place: region.Ref, Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) },
                Importance: 10, Tags: new[] { "Regions" }));
            region.Conditions.Remove(condition);
        }
    }

    private void RollEvents(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var season = ctx.Date.Season;
        var c = region.Conditions;

        var droughtOdds = _odds.Drought
                          * region.Climate switch { ClimateBand.Hot => 1.5, ClimateBand.Cold => 0.5, _ => 1.0 }
                          * (region.Biome is Biome.Swamp or Biome.Jungle ? 0.3 : 1.0);
        if (season == Season.Summer && !c.ContainsKey(Conditions.Drought) && Roll(ctx, droughtOdds))
        {
            var autumnEnds = WorldDate.FromYmd(ctx.Date.Year, 10, 1);
            Begin(ctx, region, Conditions.Drought, "Drought", 35, autumnEnds.Day - ctx.Date.Day, "DroughtEnded");
        }

        if (season == Season.Spring && region.HasRiver && !c.ContainsKey(Conditions.Flood) && Roll(ctx, _odds.Flood))
        {
            region.Food *= 0.75;
            region.Fertility = Math.Min(100, region.Fertility + 3);
            foreach (var s in settlements) Demography.Kill(s, 0.005, ctx.Rng);
            Begin(ctx, region, Conditions.Flood, "Flood", 30, 120);
        }

        if (ctx.Date.Month is >= 2 and <= 8 && region.Farmland > 0 && !c.ContainsKey(Conditions.Blight) && Roll(ctx, _odds.Blight))
            Begin(ctx, region, Conditions.Blight, "Blight", 25, 240, "BlightEnded");

        var population = settlements.Sum(s => s.Population);
        var plagueOdds = _odds.Plague * (1 + population / 10_000.0) * (c.ContainsKey(Conditions.Shortage) ? 2 : 1);
        if (!c.ContainsKey(Conditions.Plague) && !c.ContainsKey(Conditions.Immune) && Roll(ctx, plagueOdds))
            Begin(ctx, region, Conditions.Plague, "PlagueOutbreak", 50, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                Conditions.Cite(c, (Conditions.Shortage, CauseKind.Enabler, 0.6)));

        if (!c.ContainsKey(Conditions.LeySurge) && Roll(ctx, _odds.LeySurge * region.LeyPower / 50))
        {
            region.LeyPower = Math.Min(100, region.LeyPower + 20);
            region.Danger = Math.Min(100, region.Danger + 10);
            Begin(ctx, region, Conditions.LeySurge, "LeySurge", 30, 720);
        }

        if (region.MountainShare >= 0.05) Earthquake(ctx, region, settlements);
        if (region.Danger >= 65 && Roll(ctx, _odds.Monsters)) Monsters(ctx, region);
        if (Roll(ctx, _odds.Discovery)) Discovery(ctx, region);
    }

    private void Earthquake(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var surge = new[] { region }.Concat(Neighbours(ctx, region))
            .Select(r => r.Conditions.TryGetValue(Conditions.LeySurge, out var id) ? id : 0)
            .FirstOrDefault(id => id > 0);
        if (!Roll(ctx, _odds.Earthquake * (surge > 0 ? 5 : 1))) return;
        foreach (var s in settlements)
        {
            s.Walls = Math.Max(0, s.Walls - 1);
            Demography.Kill(s, 0.01, ctx.Rng);
        }
        region.Food *= 0.9;
        ctx.Emit(new EventDraft("Earthquake", Place: region.Ref,
            Causes: surge > 0 ? new[] { new Cause(surge, CauseKind.Trigger, 0.8) } : null,
            Importance: 40, Tags: new[] { "Regions" }));
    }

    private static void Monsters(SimContext ctx, Region region)
    {
        var target = Neighbours(ctx, region, LinkKind.Land).OrderBy(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (target is null) return;
        region.Danger = Math.Max(0, region.Danger - 15);
        target.Danger = Math.Min(100, target.Danger + 20);
        ctx.Emit(new EventDraft("MonstersMigrated",
            Actors: new Dictionary<string, EntityRef> { ["from"] = region.Ref },
            Place: target.Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Trigger, 0.8)),
            Importance: 35, Tags: new[] { "Regions" }));
    }

    private static void Discovery(SimContext ctx, Region region)
    {
        string kind;
        double capacity;
        if (region.LeyPower >= 60 && ctx.Rng.Chance(0.3)) (kind, capacity) = (ResourceKinds.ArcaneCrystal, region.LeyPower / 10);
        else if (region.MountainShare >= 0.1 && ctx.Rng.Chance(0.2)) (kind, capacity) = (ResourceKinds.Mithril, 3);
        else
        {
            var common = region.Resources.Keys.Where(k => !ResourceKinds.IsRare(k) && k != ResourceKinds.Grain).ToList();
            kind = common.Count > 0 ? common[ctx.Rng.NextInt(0, common.Count)] : ResourceKinds.Stone;
            capacity = region.Resources.TryGetValue(kind, out var existing) ? existing.Capacity * 0.5 : 5;
        }
        var deposit = region.Resources.TryGetValue(kind, out var d) ? d : region.Resources[kind] = new ResourceDeposit();
        deposit.Capacity = Math.Round(deposit.Capacity + capacity, 1);
        deposit.Yield = Math.Round(deposit.Yield + capacity, 1);
        ctx.Emit(new EventDraft("ResourceDiscovered", Place: region.Ref,
            Payload: new Dictionary<string, string> { ["resource"] = kind },
            Importance: ResourceKinds.IsRare(kind) ? 45 : 20, Tags: new[] { "Regions", "Economy" }));
    }

    private void SpreadPlague(SimContext ctx, IReadOnlyList<Region> plagued)
    {
        foreach (var source in plagued)
        {
            if (!source.Conditions.TryGetValue(Conditions.Plague, out var outbreak)) continue;
            foreach (var link in ctx.World.Geography?.Graph.LinksOf(source.Id) ?? Array.Empty<RegionLink>())
            {
                var target = ctx.World.Regions[link.Other(source.Id)];
                var odds = link.Kind == LinkKind.Sea ? _odds.PlagueSpreadSea : _odds.PlagueSpreadLand;
                if (target.Conditions.ContainsKey(Conditions.Plague) || target.Conditions.ContainsKey(Conditions.Immune)
                    || !ctx.Rng.Chance(odds)) continue;
                Begin(ctx, target, Conditions.Plague, "PlagueSpread", 45, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                    new[] { new Cause(outbreak, CauseKind.Trigger, 1.0) },
                    new Dictionary<string, EntityRef> { ["from"] = source.Ref });
            }
        }
    }

    /// <summary>Appends recent important events that happened in a region to its memory.</summary>
    private static void Remember(SimContext ctx)
    {
        var cursor = ctx.World.Markers.GetValueOrDefault(MemoryCursor, 0);
        var all = ctx.Log.All;
        // Ids increase along the log, so binary-search the first unscanned event instead of walking history.
        int lo = 0, hi = all.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (all[mid].Id <= cursor) lo = mid + 1; else hi = mid;
        }
        for (var i = lo; i < all.Count; i++)
        {
            var ev = all[i];
            if (ev.Importance < MemoryImportance || ev.Place is not { Kind: Region.Kind } place) continue;
            if (!ctx.World.Regions.TryGetValue(place.Id, out var region)) continue;
            region.Memory.Add(ev.Id);
            if (region.Memory.Count > MemorySize) region.Memory.RemoveAt(0);
        }
        if (ctx.Log.Count > 0) ctx.World.Markers[MemoryCursor] = ctx.Log.NextId - 1;
    }

    private static WorldEvent Begin(SimContext ctx, Region region, string condition, string type, int importance,
        long days, string endType = "", IReadOnlyList<Cause>? causes = null, IReadOnlyDictionary<string, EntityRef>? actors = null)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: actors, Place: region.Ref, Causes: causes,
            Importance: importance, Tags: new[] { "Regions" }));
        Hold(ctx, region, condition, ev.Id, days, endType);
        return ev;
    }

    /// <summary>Sets a condition caused by eventId and schedules its end (with an end event if endType is set).</summary>
    private static void Hold(SimContext ctx, Region region, string condition, long eventId, long days, string endType = "")
    {
        region.Conditions[condition] = eventId;
        ctx.Queue.Schedule(ctx.Date.AddDays(days), ConditionEndKind, new Dictionary<string, string>
        {
            ["region"] = region.Id.ToString(CultureInfo.InvariantCulture),
            ["condition"] = condition,
            ["event"] = eventId.ToString(CultureInfo.InvariantCulture),
            ["end"] = endType,
        });
    }

    private static IEnumerable<Region> Neighbours(SimContext ctx, Region region, LinkKind? kind = null) =>
        (ctx.World.Geography?.Graph.LinksOf(region.Id) ?? Array.Empty<RegionLink>())
        .Where(l => kind is null || l.Kind == kind)
        .Select(l => ctx.World.Regions[l.Other(region.Id)]);

    private static bool Roll(SimContext ctx, double perYear) => perYear > 0 && ctx.Rng.Chance(perYear / 12);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RegionSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 204` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add region system with natural events and plague spread

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Settlement system

The populace's mood: unrest follows hunger, plague and danger. High unrest starts revolts that cite the shortage behind them and drives families to calmer neighbours. Settlements also grow and decline between tiers, die out, and spend wealth on buildings.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/SettlementSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/SettlementSystemTests.cs`

**Interfaces:**
- Consumes: `Conditions`, `Demography` (Tasks 7, 12); `RegionGraph` (Task 6).
- Produces: `sealed class SettlementSystem(double revoltChancePerMonth = 0.35) : ISimSystem` (name `"settlements"`, handles `"settlement.revolt.end"`; consts `RevoltAt = 60`, `EmigrateAt = 50`, `AbandonBelow = 15`, `WallCost = 4000`; walls are a level (towns up to 2, cities up to 3) that is rebuilt after earthquakes, not a one-off building). Events: `RevoltBegan/RevoltEnded` (place = settlement, actor `region`), `Exodus` (actor `to`, payload `people`), `SettlementGrew/SettlementDeclined` (payload `tier`), `SettlementAbandoned`, `BuildingCompleted` (payload `building`), `WallsRaised` (payload `level`).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/SettlementSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SettlementSystemTests
{
    private static Simulator Make(World world, EventLog? log = null, double revoltChance = 1.0) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new SettlementSystem(revoltChance) },
            checkInvariants: true);

    private static (World World, EventLog Log, WorldEvent Shortage) Starving()
    {
        var world = TestWorlds.TwoRegions();
        var log = new EventLog();
        var shortage = log.Append(world.Date, new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.Shortage] = shortage.Id;
        return (world, log, shortage);
    }

    [Fact]
    public void Shortage_RaisesUnrest_IntoARevoltCitingIt()
    {
        var (world, log, shortage) = Starving();
        world.Settlements[1].Unrest = 50;
        var sim = Make(world, log);

        sim.AdvanceDays(180);

        var revolt = sim.Log.All.First(e => e.Type == "RevoltBegan");
        Assert.Equal(world.Settlements[1].Ref, revolt.Place);
        Assert.Equal(shortage.Id, Assert.Single(revolt.Causes).EventId);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RevoltBegan" && e.Place == world.Settlements[2].Ref);
    }

    [Fact]
    public void Revolt_EndsWithin150Days_CalmingTheSettlement()
    {
        var (world, log, _) = Starving();
        world.Settlements[1].Unrest = 70;
        var sim = Make(world, log);
        sim.AdvanceDays(30);
        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        world.Regions[1].Conditions.Remove(Conditions.Shortage);

        sim.AdvanceDays(150);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "RevoltEnded");
        Assert.InRange(ended.Date.Day - revolt.Date.Day, 60, 150);
        Assert.Equal(revolt.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(world.Settlements[1].Conditions.ContainsKey(Conditions.Revolt));
    }

    [Fact]
    public void CalmSettlements_DoNotRevolt()
    {
        var sim = Make(TestWorlds.TwoRegions());
        sim.AdvanceDays(3600);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RevoltBegan");
        Assert.All(sim.World.Settlements.Values, s => Assert.InRange(s.Unrest, 0, 30));
    }

    [Fact]
    public void Settlement_GrowsIntoATown()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Adults += 1000;
        Make(world).AdvanceDays(360);
        Assert.Equal(SettlementTier.Town, world.Settlements[1].Tier);
    }

    [Fact]
    public void Town_DeclinesBelowThreeQuartersOfItsThreshold()
    {
        var world = TestWorlds.TwoRegions();
        var sim = Make(world);
        world.Settlements[1].Tier = SettlementTier.Town;
        sim.AdvanceDays(360);
        var declined = Assert.Single(sim.Log.All, e => e.Type == "SettlementDeclined");
        Assert.Equal("Village", declined.Payload["tier"]);
    }

    [Fact]
    public void TinySettlement_IsAbandoned()
    {
        var world = TestWorlds.TwoRegions();
        var hamlet = TestWorlds.AddSettlement(world, 1, SettlementTier.Hamlet, 10);
        var sim = Make(world);
        sim.AdvanceDays(360);
        Assert.True(hamlet.Abandoned);
        Assert.Equal(0, hamlet.Population);
        Assert.Single(sim.Log.All, e => e.Type == "SettlementAbandoned" && e.Place == hamlet.Ref);
    }

    [Fact]
    public void HungryFamilies_MoveToACalmNeighbour()
    {
        var (world, log, shortage) = Starving();
        var big = world.Settlements[1];
        big.Adults = 3000;
        big.Children = 2000;
        var before = world.Settlements[2].Population;
        var sim = Make(world, log, revoltChance: 0);

        sim.AdvanceDays(360);

        var exodus = Assert.Single(sim.Log.All, e => e.Type == "Exodus");
        Assert.Equal(world.Settlements[2].Ref, exodus.Actors["to"]);
        Assert.Contains(exodus.Causes, c => c.EventId == shortage.Id);
        Assert.True(world.Settlements[2].Population > before + 100);
    }

    [Fact]
    public void Town_RebuildsWallsLostToAQuake()
    {
        var world = TestWorlds.TwoRegions();
        var town = world.Settlements[1];
        town.Tier = SettlementTier.Town;
        town.Adults += 1000;
        town.Walls = 0;
        town.Wealth = SettlementSystem.WallCost * 2 + 100;
        var sim = Make(world);
        sim.AdvanceDays(60);
        Assert.Equal(2, town.Walls);
        Assert.Equal(2, sim.Log.All.Count(e => e.Type == "WallsRaised"));
    }

    [Fact]
    public void WealthySettlement_Builds()
    {
        var world = TestWorlds.TwoRegions();
        world.Settlements[1].Wealth = 1000;
        var sim = Make(world);
        sim.AdvanceDays(30);
        var built = Assert.Single(sim.Log.All, e => e.Type == "BuildingCompleted");
        Assert.Equal("granary", built.Payload["building"]);
        Assert.Contains("granary", world.Settlements[1].Buildings);
        Assert.Equal(700, world.Settlements[1].Wealth);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'SettlementSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/SettlementSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The populace's mood and fortunes: unrest follows hunger, plague and danger; high unrest starts
/// revolts and drives people to calmer neighbours; settlements grow, decline, build and die out.
/// </summary>
public sealed class SettlementSystem : ISimSystem
{
    public const string RevoltEndKind = "settlement.revolt.end";
    public const double RevoltAt = 60;
    public const double EmigrateAt = 50;
    public const int AbandonBelow = 15;

    private static readonly (string Name, SettlementTier MinTier, double Cost)[] BuildOrder =
    {
        ("granary", SettlementTier.Village, 300),
        ("market", SettlementTier.Town, 1500),
        ("temple", SettlementTier.Town, 2500),
        ("guildhall", SettlementTier.City, 8000),
        ("mage tower", SettlementTier.City, 12_000),
    };

    /// <summary>Walls are a level, not a building: towns raise them to 2, cities to 3, and rebuild after quakes.</summary>
    public const double WallCost = 4000;

    private readonly double _revoltChance;

    public SettlementSystem(double revoltChancePerMonth = 0.35) => _revoltChance = revoltChancePerMonth;

    public string Name => "settlements";

    public IEnumerable<string> HandledKinds => new[] { RevoltEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                UpdateUnrest(ctx, region, s);
                if (s.Unrest >= RevoltAt && !s.Conditions.ContainsKey(Conditions.Revolt) && ctx.Rng.Chance(_revoltChance))
                    BeginRevolt(ctx, region, s);
                Build(ctx, region, s);
            }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                Emigrate(ctx, region, s);
                ChangeTier(ctx, region, s);
                if (s.Population < AbandonBelow) Abandon(ctx, region, s);
            }
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var s = ctx.World.Settlements[long.Parse(action.Data["settlement"], CultureInfo.InvariantCulture)];
        var revolt = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!s.Conditions.TryGetValue(Conditions.Revolt, out var current) || current != revolt) return;
        s.Conditions.Remove(Conditions.Revolt);
        if (s.Abandoned) return;
        Demography.Kill(s, 0.02, ctx.Rng);
        s.Wealth *= 0.7;
        s.Unrest = 30;
        ctx.Emit(new EventDraft("RevoltEnded", Place: s.Ref,
            Causes: new[] { new Cause(revolt, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "Politics" }));
    }

    private static void UpdateUnrest(SimContext ctx, Region region, Settlement s)
    {
        var target = 10 + region.Danger * 0.2;
        if (region.Conditions.ContainsKey(Conditions.Shortage)) target += 50;
        if (region.Conditions.ContainsKey(Conditions.Plague)) target += 15;
        if (s.Walls == 0 && region.Danger > 60) target += 5;
        s.Unrest = Math.Clamp(s.Unrest + (target - s.Unrest) * 0.25 + ctx.Rng.NextInt(-3, 4), 0, 100);
    }

    private static void BeginRevolt(SimContext ctx, Region region, Settlement s)
    {
        var revolt = ctx.Emit(new EventDraft("RevoltBegan",
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Place: s.Ref,
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.9),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.4)),
            Importance: s.Tier switch
            {
                SettlementTier.City => 60,
                SettlementTier.Town => 45,
                SettlementTier.Village => 30,
                _ => 20,
            },
            Tags: new[] { "Politics" }));
        s.Conditions[Conditions.Revolt] = revolt.Id;
        ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(60, 151)), RevoltEndKind, new Dictionary<string, string>
        {
            ["settlement"] = s.Id.ToString(CultureInfo.InvariantCulture),
            ["event"] = revolt.Id.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static void Build(SimContext ctx, Region region, Settlement s)
    {
        var maxWalls = s.Tier switch { SettlementTier.City => 3, SettlementTier.Town => 2, _ => 0 };
        if (s.Walls < maxWalls && s.Wealth >= WallCost)
        {
            s.Wealth -= WallCost;
            s.Walls++;
            ctx.Emit(new EventDraft("WallsRaised", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["level"] = s.Walls.ToString(CultureInfo.InvariantCulture) },
                Importance: 15, Tags: new[] { "Settlements" }));
            return;
        }
        foreach (var (name, minTier, cost) in BuildOrder)
        {
            if (s.Buildings.Contains(name) || s.Tier < minTier) continue;
            if (name == "mage tower" && region.LeyPower < 50) continue;
            if (s.Wealth < cost) return;
            s.Wealth -= cost;
            s.Buildings.Add(name);
            s.Buildings.Sort(StringComparer.Ordinal);
            ctx.Emit(new EventDraft("BuildingCompleted", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["building"] = name },
                Importance: name == "mage tower" ? 30 : 10, Tags: new[] { "Settlements" }));
            return;
        }
    }

    /// <summary>Unhappy or hungry settlements lose 4% of their families to the calmest settlement nearby.</summary>
    private static void Emigrate(SimContext ctx, Region region, Settlement s)
    {
        var hungry = region.Conditions.ContainsKey(Conditions.Shortage);
        if (s.Unrest < EmigrateAt && !hungry) return;

        var nearby = new[] { region.Id }.Concat(ctx.World.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>());
        var destination = nearby
            .Select(id => ctx.World.Regions[id])
            .Where(r => !r.Conditions.ContainsKey(Conditions.Shortage))
            .SelectMany(r => ctx.World.LivingSettlements(r))
            .Where(d => d.Id != s.Id && d.Unrest < s.Unrest - 10)
            .OrderBy(d => d.Unrest).ThenByDescending(d => d.Tier).ThenBy(d => d.Id)
            .FirstOrDefault();
        if (destination is null) return;

        var children = Demography.RoundRandom(s.Children * 0.04, ctx.Rng);
        var adults = Demography.RoundRandom(s.Adults * 0.04, ctx.Rng);
        s.Children -= children;
        s.Adults -= adults;
        destination.Children += children;
        destination.Adults += adults;
        if (children + adults < 100) return;

        ctx.Emit(new EventDraft("Exodus",
            Actors: new Dictionary<string, EntityRef> { ["to"] = destination.Ref },
            Place: s.Ref,
            Payload: new Dictionary<string, string> { ["people"] = (children + adults).ToString(CultureInfo.InvariantCulture) },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Shortage, CauseKind.Trigger, 0.8))
                .Concat(Conditions.Cite(s.Conditions, (Conditions.Revolt, CauseKind.Enabler, 0.6))).ToList(),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    /// <summary>Grow at the next tier's threshold; decline below 75% of the current tier's.</summary>
    private static void ChangeTier(SimContext ctx, Region region, Settlement s)
    {
        var grown = Demography.TierFor(s.Population);
        if (grown > s.Tier)
        {
            s.Tier = grown;
            ctx.Emit(new EventDraft("SettlementGrew", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["tier"] = grown.ToString() },
                Importance: grown switch { SettlementTier.City => 50, SettlementTier.Town => 30, _ => 15 },
                Tags: new[] { "Settlements" }));
            return;
        }
        if (s.Tier == SettlementTier.Hamlet || s.Population >= Threshold(s.Tier) * 0.75) return;
        s.Tier -= 1;
        ctx.Emit(new EventDraft("SettlementDeclined", Place: s.Ref,
            Payload: new Dictionary<string, string> { ["tier"] = s.Tier.ToString() },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Enabler, 0.7), (Conditions.Plague, CauseKind.Enabler, 0.6)),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    private static void Abandon(SimContext ctx, Region region, Settlement s)
    {
        s.Abandoned = true;
        s.Children = s.Adults = s.Elders = 0;
        ctx.Emit(new EventDraft("SettlementAbandoned", Place: s.Ref,
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.8),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
            Importance: 30, Tags: new[] { "Settlements" }));
    }

    private static int Threshold(SettlementTier tier) => tier switch
    {
        SettlementTier.City => 6000,
        SettlementTier.Town => 1500,
        SettlementTier.Village => 300,
        _ => 0,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SettlementSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 9`. Then `dotnet test sim` → `Passed: 213` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add settlement system with unrest, revolts and migration

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Real systems and CLI

The demo harvest/unrest systems from Plan 1 are retired: the default order is now Economy → Regions → Settlements (spec §7 order, with settlements standing in for politics until Plan 4). The CLI generates real worlds and can show them as text: an ASCII map, a region table and a region detail page. Event lines name places, e.g. `Brenmoor (region#3)`.

**Files:**
- Create: `sim/FantasyWorld.Cli/MapRenderer.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Cli/DurationParser.cs`, `sim/FantasyWorld.Sim/Query/EventText.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Delete: `sim/FantasyWorld.Sim.Tests/Systems/DemoSystemsTests.cs`, `sim/FantasyWorld.Sim/Systems/Demo/HarvestSystem.cs`, `sim/FantasyWorld.Sim/Systems/Demo/UnrestSystem.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs`, `sim/FantasyWorld.Sim.Tests/Cli/DurationParserTests.cs`, `sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `DefaultSystems.Create()` → `[EconomySystem, RegionSystem, SettlementSystem]`; `EventText.Describe(WorldEvent, Func<EntityRef, string?>? nameOf = null)`; `static class MapRenderer { string Render(World, string layer) }` (`biome` or `regions`; each character covers step × 2·step tiles, step = width / 128); CLI: `fw new <file> [--seed n] [--size tiny|medium|large]`, `fw map <file> [--layer biome|regions]`, `fw regions <file>`, `fw region <file> <id>`. `DurationParser.MaxDays` = 1000 years (overflow-safe); CLI errors for bad files, overflowing numbers and corrupt JSON print `error:` instead of a stack trace. Deletes `Systems/Demo/*` and `DemoSystemsTests`.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs` entirely with:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Cli;

public class CliAppTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(DateTimeOffset now, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => now);
        return (code, output.ToString());
    }

    private static void NewTiny(TempFile file) => Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");

    /// <summary>A tiny world whose first region starts a drought with empty, barren fields.</summary>
    private static (long RegionId, long DroughtId) StarvingWorld(TempFile file)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), T0);
        var region = world.Regions[1];
        var log = new EventLog();
        var drought = log.Append(world.Date, new EventDraft("Drought", Place: region.Ref));
        region.Conditions[Conditions.Drought] = drought.Id;
        region.Food = 0;
        region.Fertility = 0;
        region.Resources.Remove(ResourceKinds.Fish);
        using var store = WorldStore.Open(file.Path);
        store.Save(world, log, new EventQueue());
        return (region.Id, drought.Id);
    }

    [Fact]
    public void NoArguments_PrintsUsage()
    {
        var (code, output) = Run(T0);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void UnknownCommand_PrintsUsage()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "dance", file.Path);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void New_GeneratesAWorld()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--seed", "42", "--size", "tiny");
        Assert.Equal(0, code);
        Assert.Contains("seed 42, tiny:", output);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(42UL, world.Seed);
        Assert.NotEmpty(world.Regions);
        Assert.NotNull(world.Geography);
    }

    [Fact]
    public void New_RejectsUnknownSize()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--size", "enormous");
        Assert.Equal(1, code);
        Assert.Contains("error: Unknown size", output);
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public void New_RefusesToOverwrite()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "new", file.Path, "--seed", "2");
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void Advance_MovesTheWorldAndSaves()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "advance", file.Path, "2y");
        Assert.Equal(0, code);
        Assert.Contains("Y3 M1 D1", output);
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(720, store.Load().World.Date.Day);
    }

    [Fact]
    public void CatchUp_UsesTheClock()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0.AddHours(8), "catchup", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("Advanced 120 days", output);
    }

    [Fact]
    public void MissingWorldFile_IsAnError()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "log", file.Path);
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void NotAWorldFile_IsAnError()
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path, "these are not the chronicles you are looking for");
        var (code, output) = Run(T0, "log", file.Path);
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void OutOfRangeNumbers_AreErrors()
    {
        using var file = new TempFile();
        Assert.Equal(1, Run(T0, "new", file.Path, "--seed", "-1").Code);
        NewTiny(file);
        Assert.Equal(1, Run(T0, "region", file.Path, "99999999999999999999").Code);
    }

    [Fact]
    public void Why_PrintsTheCausalChain()
    {
        using var file = new TempFile();
        var (regionId, droughtId) = StarvingWorld(file);
        Run(T0, "advance", file.Path, "1mo");
        long shortageId;
        using (var store = WorldStore.Open(file.Path))
            shortageId = store.Load().Log.All.First(e => e.Type == "FoodShortageBegan" && e.Place!.Value.Id == regionId).Id;

        var (code, output) = Run(T0, "why", file.Path, shortageId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("FoodShortageBegan", lines[0]);
        Assert.StartsWith($"  because #{droughtId} ", lines[1]);
        Assert.Contains("Drought", lines[1]);
    }

    [Fact]
    public void Log_ListsEventsSinceId_WithNames()
    {
        using var file = new TempFile();
        StarvingWorld(file);
        Run(T0, "advance", file.Path, "1mo");
        string regionName;
        using (var store = WorldStore.Open(file.Path)) regionName = store.Load().World.Regions[1].Name;

        var (code, output) = Run(T0, "log", file.Path, "--since", "1");

        Assert.Equal(0, code);
        Assert.DoesNotContain("#1 ", output);
        Assert.Contains("#2 ", output);
        Assert.Contains($"{regionName} (region#1)", output);
    }

    [Fact]
    public void Map_DrawsTerrainAndSettlements()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "map", file.Path);
        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(33, lines.Length);
        Assert.All(lines.Take(32), l => Assert.Equal(64, l.Length));
        Assert.Contains('~', output);
        Assert.Contains('@', output);
    }

    [Fact]
    public void Regions_ListsEveryRegion()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "regions", file.Path);
        Assert.Equal(0, code);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(world.Regions.Count + 1, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(world.Regions[1].Name, output);
    }

    [Fact]
    public void Region_ShowsSettlementsAndNeighbours()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "region", file.Path, "1");
        Assert.Equal(0, code);
        Assert.Contains("(region#1)", output);
        Assert.Contains("Settlements:", output);
        Assert.Contains("neighbours:", output);
    }

    [Fact]
    public void Region_UnknownId_IsAnError()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "region", file.Path, "999");
        Assert.Equal(1, code);
        Assert.Contains("error: No region #999", output);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Cli/DurationParserTests.cs` entirely with:
```csharp
using FantasyWorld.Cli;

namespace FantasyWorld.Sim.Tests.Cli;

public class DurationParserTests
{
    [Theory]
    [InlineData("30d", 30)]
    [InlineData("4mo", 120)]
    [InlineData("7y", 2520)]
    [InlineData(" 1y ", 360)]
    public void ParsesUnits(string text, long expectedDays) =>
        Assert.Equal(expectedDays, DurationParser.ParseDays(text));

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("3w")]
    [InlineData("-2d")]
    [InlineData("1001y")]
    [InlineData("99999999999999999999d")]
    [InlineData("25620477880152159y")]
    public void RejectsBadInput(string text) =>
        Assert.Throws<FormatException>(() => DurationParser.ParseDays(text));
}
```

Replace `sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Tests.Query;

public class HistoryQueryTests
{
    // drought(1) -> shortage(2); insult(3); shortage + insult -> war(4) -> victory(5)
    private static (EventLog Log, HistoryQuery Query) BuildChain()
    {
        var log = new EventLog();
        var d = new WorldDate(100);
        var drought = log.Append(d, new EventDraft("Drought"));
        var shortage = log.Append(d, new EventDraft("GrainShortageBegan",
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 1.0) }));
        var insult = log.Append(d, new EventDraft("MarriageRefused"));
        var war = log.Append(d, new EventDraft("WarDeclared", Causes: new[]
        {
            new Cause(insult.Id, CauseKind.Motive, 0.9),
            new Cause(shortage.Id, CauseKind.Enabler, 0.6),
        }));
        log.Append(d, new EventDraft("RiverCrownTaken",
            Causes: new[] { new Cause(war.Id, CauseKind.Trigger, 1.0) }));
        return (log, new HistoryQuery(log));
    }

    [Fact]
    public void WhyChain_WalksCausesStrongestFirst()
    {
        var (_, q) = BuildChain();
        var root = q.WhyChain(5);

        Assert.Equal("RiverCrownTaken", root.Event.Type);
        Assert.Null(root.Kind);
        var war = Assert.Single(root.Causes);
        Assert.Equal("WarDeclared", war.Event.Type);
        Assert.Equal(CauseKind.Trigger, war.Kind);
        Assert.Equal(new[] { "MarriageRefused", "GrainShortageBegan" }, war.Causes.Select(c => c.Event.Type));
        Assert.Equal("Drought", Assert.Single(war.Causes[1].Causes).Event.Type);
    }

    [Fact]
    public void WhyChain_RespectsDepth()
    {
        var (_, q) = BuildChain();
        var root = q.WhyChain(5, depth: 1);
        Assert.Empty(Assert.Single(root.Causes).Causes);
    }

    [Fact]
    public void RootCauses_ReturnsLeavesInIdOrder()
    {
        var (_, q) = BuildChain();
        Assert.Equal(new[] { "Drought", "MarriageRefused" }, q.RootCauses(5).Select(e => e.Type));
    }

    [Fact]
    public void RootCauses_OfUncausedEvent_IsEmpty()
    {
        var (_, q) = BuildChain();
        Assert.Empty(q.RootCauses(1));
    }

    [Fact]
    public void Consequences_FollowsEffectsToDepth()
    {
        var (_, q) = BuildChain();
        Assert.Equal(new[] { "GrainShortageBegan" }, q.Consequences(1).Select(e => e.Type));
        Assert.Equal(new[] { "GrainShortageBegan", "WarDeclared", "RiverCrownTaken" },
            q.Consequences(1, depth: 3).Select(e => e.Type));
    }

    [Fact]
    public void EventText_DescribesEventOnOneLine()
    {
        var log = new EventLog();
        var ev = log.Append(WorldDate.FromYmd(3, 2, 1), new EventDraft("GrainShortageBegan",
            Payload: new Dictionary<string, string> { ["grain"] = "35" }));
        Assert.Equal("#1 Y3 M2 D1 GrainShortageBegan grain=35", EventText.Describe(ev));
    }

    [Fact]
    public void EventText_NamesKnownEntities()
    {
        var log = new EventLog();
        var ev = log.Append(new WorldDate(0), new EventDraft("Flood",
            Actors: new Dictionary<string, EntityRef> { ["from"] = new("region", 2) },
            Place: new EntityRef("region", 3)));
        var text = EventText.Describe(ev, e => e.Id == 3 ? "Brenmoor" : null);
        Assert.Equal("#1 Y1 M1 D1 Flood from=region#2 at Brenmoor (region#3)", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1501: No overload for method 'Describe' takes 2 arguments`.

- [ ] **Step 3: Implement**

Delete the retired demo systems and their tests:
```bash
git rm -r sim/FantasyWorld.Sim.Tests/Systems/DemoSystemsTests.cs sim/FantasyWorld.Sim/Systems/Demo
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large]
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions]
          fw regions <world-file>
          fw region <world-file> <region-id>
        """;

    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock)
    {
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output),
                "catchup" => CatchUpCommand(path, output, clock),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                _ => UsageError(output),
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
                                       or KeyNotFoundException or IOException or OverflowException
                                       or DbException or JsonException)
        {
            output.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int New(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"{path} already exists.");
        var now = clock();
        var seedText = Option(rest, "--seed");
        var seed = seedText is null ? (ulong)now.UtcTicks : ulong.Parse(seedText, CultureInfo.InvariantCulture);
        var sizeText = Option(rest, "--size") ?? "medium";
        if (!Enum.TryParse<WorldSize>(sizeText, ignoreCase: true, out var size) || !Enum.IsDefined(size))
            throw new FormatException($"Unknown size '{sizeText}'. Use tiny, medium or large.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, new EventLog(), new EventQueue());
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var sim = Load(store);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        store.Save(sim.World, sim.Log, sim.Queue);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock)
    {
        using var store = OpenExisting(path);
        var sim = Load(store);
        var lastId = sim.Log.NextId - 1;
        var result = CatchUp.Run(sim, clock());
        store.Save(sim.World, sim.Log, sim.Queue);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int Log(string path, string[] rest, TextWriter output)
    {
        var since = long.Parse(Option(rest, "--since") ?? "0", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var sim = Load(store);
        PrintEvents(output, sim.World, sim.Log.Since(since));
        return 0;
    }

    private static int Why(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var eventId = long.Parse(rest[0], CultureInfo.InvariantCulture);
        var depth = int.Parse(Option(rest, "--depth") ?? "4", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var sim = Load(store);
        var chain = new HistoryQuery(sim.Log).WhyChain(eventId, depth);
        PrintChain(output, sim.World, chain, 0);
        return 0;
    }

    private static int Map(string path, string[] rest, TextWriter output)
    {
        var layer = Option(rest, "--layer") ?? "biome";
        if (layer is not ("biome" or "regions")) throw new FormatException($"Unknown layer '{layer}'. Use biome or regions.");
        using var store = OpenExisting(path);
        output.WriteLine(MapRenderer.Render(store.Load().World, layer));
        return 0;
    }

    private static int Regions(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("  id name               biome      climate      people  fert danger  ley   food  conditions");
        foreach (var r in world.Regions.Values)
        {
            var people = world.LivingSettlements(r).Sum(s => s.Population);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.Id,4} {r.Name,-18} {r.Biome,-10} {r.Climate,-10} {people,8} {r.Fertility,5:0} {r.Danger,6:0} {r.LeyPower,4:0} {FoodMonths(world, r),5:0.0}mo  {string.Join(",", r.Conditions.Keys)}"));
        }
        return 0;
    }

    private static int RegionDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var r = world.Regions.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No region #{id}.");

        var inv = CultureInfo.InvariantCulture;
        output.WriteLine($"{r.Name} (region#{r.Id}) — {r.Biome}, {r.Climate}{(r.Coastal ? ", coastal" : "")}{(r.HasRiver ? ", river" : "")}");
        output.WriteLine(string.Create(inv,
            $"  tiles {r.TileCount}, farmland {r.Farmland:0}, fertility {r.Fertility:0}/{r.BaseFertility:0}, danger {r.Danger:0}, ley {r.LeyPower:0}, food {FoodMonths(world, r):0.0} months"));
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
        if (r.Memory.Count > 0)
        {
            output.WriteLine("  Remembered for:");
            foreach (var ev in r.Memory.Select(log.Get)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
        }
        return 0;
    }

    private static double FoodMonths(World world, Region region)
    {
        var need = world.LivingSettlements(region).Sum(Demography.Consumption);
        return need > 0 ? region.Food / need : 0;
    }

    private static WorldStore OpenExisting(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"No world file at {path}. Create one with: fw new {path}");
        return WorldStore.Open(path);
    }

    private static Simulator Load(WorldStore store)
    {
        var (world, log, queue) = store.Load();
        return new Simulator(world, log, queue, DefaultSystems.Create());
    }

    private static Func<EntityRef, string?> NameOf(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        _ => null,
    };

    private static void PrintEvents(TextWriter output, World world, IEnumerable<WorldEvent> events)
    {
        foreach (var ev in events) output.WriteLine(EventText.Describe(ev, NameOf(world)));
    }

    private static void PrintChain(TextWriter output, World world, ChainNode node, int indent)
    {
        var prefix = new string(' ', indent * 2) + (indent == 0 ? "" : "because ");
        output.WriteLine(prefix + EventText.Describe(node.Event, NameOf(world)));
        foreach (var cause in node.Causes) PrintChain(output, world, cause, indent + 1);
    }

    private static string? Option(string[] rest, string name)
    {
        var index = Array.IndexOf(rest, name);
        return index >= 0 && index + 1 < rest.Length ? rest[index + 1] : null;
    }

    private static int UsageError(TextWriter output)
    {
        output.WriteLine(Usage);
        return 2;
    }
}
```

Replace `sim/FantasyWorld.Cli/DurationParser.cs` entirely with:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Cli;

public static class DurationParser
{
    private static readonly Regex Pattern = new(@"^(\d+)(d|mo|y)$", RegexOptions.CultureInvariant);

    /// <summary>Debug fast-forwards beyond this are almost certainly typos (and would run for hours).</summary>
    public const long MaxDays = 1000L * WorldDate.DaysPerYear;

    public static long ParseDays(string text)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
            throw new FormatException($"Bad duration '{text}'. Use e.g. 30d, 4mo or 7y.");
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            throw new FormatException($"Duration '{text}' is too long.");
        var days = match.Groups[2].Value switch
        {
            "d" => n,
            "mo" => n > MaxDays ? MaxDays + 1 : n * WorldDate.DaysPerMonth,
            _ => n > MaxDays ? MaxDays + 1 : n * WorldDate.DaysPerYear,
        };
        if (days > MaxDays) throw new FormatException($"Duration '{text}' is longer than 1000 years.");
        return days;
    }
}
```

`sim/FantasyWorld.Cli/MapRenderer.cs`:
```csharp
using System.Text;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Cli;

/// <summary>ASCII map of the world. Each character covers a block of tiles (twice as tall as wide).</summary>
public static class MapRenderer
{
    public const string Legend =
        "~ sea  = river  . grassland  f forest  % swamp  , savanna  _ desert  J jungle  : tundra  T taiga  n hills  ^ mountains  @ city  O town  o village";

    private const string RegionGlyphs = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Render(World world, string layer)
    {
        var geography = world.Geography ?? throw new InvalidOperationException("This world has no map.");
        var grid = geography.Terrain;
        var step = Math.Max(1, grid.Width / 128);
        var places = new Dictionary<(int, int), SettlementTier>();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Village))
        {
            var cell = (s.Tile.X / step, s.Tile.Y / (step * 2));
            if (!places.TryGetValue(cell, out var tier) || s.Tier > tier) places[cell] = s.Tier;
        }

        var text = new StringBuilder();
        for (var y = 0; y < grid.Height; y += step * 2)
        {
            for (var x = 0; x < grid.Width; x += step)
            {
                var i = grid.Index(x, y);
                text.Append(places.TryGetValue((x / step, y / (step * 2)), out var tier)
                    ? tier switch { SettlementTier.City => '@', SettlementTier.Town => 'O', _ => 'o' }
                    : layer == "regions"
                        ? geography.RegionOf[i] == 0 ? '~' : RegionGlyphs[geography.RegionOf[i] % RegionGlyphs.Length]
                        : Glyph(grid, i));
            }
            text.AppendLine();
        }
        text.Append(layer == "regions" ? "Each glyph is a region (id mod 62); @ O o are settlements." : Legend);
        return text.ToString();
    }

    private static char Glyph(TerrainGrid grid, int i) =>
        grid.Has(i, TileFlags.River)
            ? '='
            : grid.BiomeAt(i) switch
            {
                Biome.Ocean => '~',
                Biome.Tundra => ':',
                Biome.Taiga => 'T',
                Biome.Grassland => '.',
                Biome.Forest => 'f',
                Biome.Swamp => '%',
                Biome.Desert => '_',
                Biome.Savanna => ',',
                Biome.Jungle => 'J',
                Biome.Hills => 'n',
                _ => '^',
            };
}
```

Replace `sim/FantasyWorld.Sim/Query/EventText.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

public static class EventText
{
    /// <summary>One line per event. With nameOf, entities read "Brenmoor (region#3)".</summary>
    public static string Describe(WorldEvent ev, Func<EntityRef, string?>? nameOf = null)
    {
        string Name(EntityRef entity) => nameOf?.Invoke(entity) is { } name ? $"{name} ({entity})" : entity.ToString();

        var text = $"#{ev.Id} {ev.Date} {ev.Type}";
        if (ev.Actors.Count > 0)
            text += " " + string.Join(", ", ev.Actors.Select(a => $"{a.Key}={Name(a.Value)}"));
        if (ev.Place is { } place)
            text += $" at {Name(place)}";
        if (ev.Payload.Count > 0)
            text += " " + string.Join(", ", ev.Payload.Select(p => $"{p.Key}={p.Value}"));
        return text;
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Economy → Regions → Politics → …).
/// Settlements stand in for politics until realms arrive in Plan 4.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() =>
        new ISimSystem[] { new EconomySystem(), new RegionSystem(), new SettlementSystem() };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CliAppTests|FullyQualifiedName~DurationParserTests|FullyQualifiedName~HistoryQueryTests"`
Expected: `Passed!  - Failed: 0, Passed: 34`. Then `dotnet test sim` → `Passed: 219` in total.

- [ ] **Step 5: Smoke-test the real binary**

```bash
dotnet run --project sim/FantasyWorld.Cli -- new worlds/demo.fw.db --seed 7
dotnet run --project sim/FantasyWorld.Cli -- map worlds/demo.fw.db
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/demo.fw.db 50y
dotnet run --project sim/FantasyWorld.Cli -- regions worlds/demo.fw.db
```
Expected: `Created world at worlds/demo.fw.db (seed 7, medium: 100 regions, … settlements).`; a 128×64 map of a continent with a mountain spine, rivers (`=`) and settlements (`@ O o`); `Advanced 18000 days to Y51 M1 D1.` followed by several hundred named event lines (plague waves, droughts, revolts, buildings…), in a few seconds; a 100-row region table. Pick a `RevoltBegan` or `FoodShortageBegan` id and run `fw why worlds/demo.fw.db <id>`: you should see an indented `because …` chain (e.g. revolt ← shortage ← drought). Then `rm -r worlds`.

- [ ] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(cli): replace demo systems; add map, regions and region commands

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: World health checks and README

These checks hold the whole stack to the spec's guarantees: chunk invariance and save/load continuity with the real systems, two centuries without collapse or stagnation, causal chains at least two links deep, and the ≤ 2 s per world year performance target (spec §7) on a medium world.

**Files:**
- Modify: `README.md`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/WorldHealthTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `WorldHealthTests` (6 tests); README run instructions for the new commands.

- [ ] **Step 1: Write the checks**

`sim/FantasyWorld.Sim.Tests/Engine/WorldHealthTests.cs`:
```csharp
using System.Diagnostics;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks with the real systems: determinism, invariants, history health, speed.</summary>
public class WorldHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size = WorldSize.Tiny, bool checkInvariants = true) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    private static long Population(World world) => world.Settlements.Values.Sum(s => (long)s.Population);

    [Fact]
    public void RealSystems_AreChunkInvariant()
    {
        var oneShot = NewSim(21);
        oneShot.AdvanceDays(3600);

        var chunked = NewSim(21);
        foreach (var chunk in new long[] { 1, 59, 400, 1140, 2000 }) chunked.AdvanceDays(chunk);

        Assert.True(oneShot.Log.Count > 10, "a decade should produce history");
        Assert.Equal(Hash(oneShot), Hash(chunked));
    }

    [Fact]
    public void SavedAndReloadedWorld_ContinuesIdentically()
    {
        using var file = new TempFile();
        var original = NewSim(22);
        original.AdvanceDays(1800);
        using (var store = WorldStore.Open(file.Path)) store.Save(original.World, original.Log, original.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);

        original.AdvanceDays(1800);
        resumed.AdvanceDays(1800);
        Assert.Equal(Hash(original), Hash(resumed));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoCenturies_StayHealthy(ulong seed)
    {
        var sim = NewSim(seed);
        var startPopulation = Population(sim.World);

        sim.AdvanceDays(200 * 360);

        var population = Population(sim.World);
        Assert.InRange(population, startPopulation / 2, startPopulation * 4);
        Assert.True(sim.World.Settlements.Values.Count(s => s.Abandoned) * 4 <= sim.World.Settlements.Count,
            "no more than a quarter of settlements should die out");
        var types = sim.Log.All.Select(e => e.Type).Distinct().ToList();
        Assert.True(types.Count >= 12, $"history should be varied, saw: {string.Join(", ", types)}");
        var query = new HistoryQuery(sim.Log);
        Assert.Contains(sim.Log.All, e => e.Causes.Count > 0 && query.WhyChain(e.Id).Causes.Any(c => c.Causes.Count > 0));
    }

    [Fact]
    public void MediumWorld_SimulatesAYearWellUnderTwoSeconds()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(360);
        var clock = Stopwatch.StartNew();
        sim.AdvanceDays(5 * 360);
        Assert.True(clock.Elapsed.TotalSeconds / 5 < 2.0, $"took {clock.Elapsed.TotalSeconds / 5:0.00} s per world year");
    }
}
```

- [ ] **Step 2: Run tests to verify they pass against the finished stack**

Run: `dotnet test sim`
Expected: PASS. These tests pin down behaviour built in Tasks 12–15; if one fails, fix the system it names before going on.

- [ ] **Step 3: Implement**

Replace `README.md` entirely with:
````markdown
# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `docs/superpowers/specs/` — design specs
- `docs/superpowers/plans/` — implementation plans

## Run

```bash
dotnet test sim
dotnet run --project sim/FantasyWorld.Cli -- new worlds/aerenmoor.fw.db            # medium world (100 regions)
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db            # ASCII map (--layer regions)
dotnet run --project sim/FantasyWorld.Cli -- catchup worlds/aerenmoor.fw.db        # simulate real elapsed time
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 50y    # debug fast-forward
dotnet run --project sim/FantasyWorld.Cli -- regions worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- region worlds/aerenmoor.fw.db 12
dotnet run --project sim/FantasyWorld.Cli -- why worlds/aerenmoor.fw.db <event-id>
```

`--size tiny` makes a 64×64 world with 12 regions for quick experiments.
````

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~WorldHealthTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 225` in total.

- [ ] **Step 5: Run the full suite in Release**

Run: `dotnet test sim --configuration Release`
Expected: `Passed!  - Failed: 0, Passed: 225`

- [ ] **Step 6: Commit**

```bash
git add sim README.md
git commit -m "test(sim): add world health, determinism and performance checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §5 terrain grid (~256×256): elevation, biome, moisture, river flags | 2, 3, 4 |
| §5 regions (100–120) with adjacency graph, travel cost, roads/river crossings/passes | 5, 6 |
| §5 region data: geography, resources (incl. rare), fertility, danger, ley power, contents, memory | 7, 8, 13 |
| §5 settlements: tier, population by age band and occupation, food, wealth, defences, buildings, unrest | 7, 9, 12, 14 |
| §5 region events: drought, flood, plague, earthquake (← ley surge), monster migration, ley surge, resource discovery | 13 |
| §5 world generation: seed → heightmap → climate/biomes → rivers → regions → resources → settlements | 3–10 |
| §7 annual tick (population growth, harvest) | 1, 12 |
| §7 condition events as causes (`GrainShortageBegan`-style) | 12, 13, 14 |
| §7 performance ≤ 2 s per world year at 120 regions | 16 |
| §9 persistence of the new state | 11 |
| §10 invariants, determinism (chunking, save/load), history health | 7, 16 |

Deferred (see Roadmap and decisions): cultures & faith, realm ownership/control level, roads, labyrinths and other structures (sub-project 3 hooks arrive with Plan 3/4), inter-region trade, founding new settlements, lakes, prehistory.
