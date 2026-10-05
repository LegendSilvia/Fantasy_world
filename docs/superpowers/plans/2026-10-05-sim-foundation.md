# Sim Foundation Implementation Plan (Plan 1 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deterministic simulation skeleton — calendar, seeded RNG, causal event log with "why" queries, day-stepping simulator with monthly ticks and a scheduled-event queue, real-time catch-up, SQLite persistence and a `fw` CLI — proven end-to-end by two small demo systems (harvest → shortage → revolt).

**Architecture:** A .NET 8 class library (`FantasyWorld.Sim`) with no engine dependency. Systems never call each other; they read the world and emit events into an append-only `EventLog` whose events cite earlier events as causes. The `Simulator` advances one world day at a time, runs systems on the first of each month in a fixed order, and drains a priority queue of scheduled actions. Every random draw comes from a stream derived from `(seed, system, phase, day)`, so the world is a pure function of seed and elapsed days regardless of how time is chunked.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite 8.0.10, System.Text.Json, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` (§2, §7, §9, §10).

## Global Constraints

- Target framework `net8.0`; `Nullable` enabled; `TreatWarningsAsErrors` true; `InvariantGlobalization` true.
- `FantasyWorld.Sim` must never reference Godot.
- **Determinism:** no `System.Random`, no `DateTime.Now`/`DateTimeOffset.UtcNow`, no `Guid.NewGuid` inside `FantasyWorld.Sim`. Real time is always passed in. Any dictionary that is iterated or serialized is a `SortedDictionary<string, …>` with `StringComparer.Ordinal`.
- Calendar: 360-day year = 12 months × 30 days; four seasons of 3 months. Day 0 = Year 1, Month 1, Day 1.
- Time scale: **1 real day = 1 world year** → 1 world day = 240 real seconds.
- Catch-up cap: **30 world years (10,800 days) per launch**; remainder carries over.
- Events: append-only, ids strictly increasing, dates non-decreasing, **max 3 causes** (strongest by weight kept), causes must be existing earlier events.
- Number formatting in payloads and persistence uses `CultureInfo.InvariantCulture`.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`

## Roadmap (later plans, written after this one ships)

| Plan | Delivers |
|---|---|
| 2 — World & regions | terrain grid, region graph, settlements, economy, region events, world generation (without prehistory) |
| 3 — Characters & artifacts | notables, populace promotion, traits, memories/opinions, relationships, aging, death & inheritance, artifacts |
| 4 — Realms & armies | realm hierarchy, government, succession, legitimacy, diplomacy, casus belli, armies, battles, sieges, war score, eras |
| 5 — LLM layer | `ILlmProvider` (Anthropic C# SDK, OpenRouter, Fake), decisions, arbitration, invention, narration, budgets, cost ledger |
| 6 — Chronicle & hardening | importance + digest, prehistory run, live-mode host loop, event compaction, snapshot compression, rolling backups, history-health soak, BenchmarkDotNet (≤ 2 s/year) |

The demo systems in this plan (`HarvestSystem`, `UnrestSystem`) are replaced by real systems in Plans 2–4.

## File Structure

```
global.json                                   SDK pin (8.0, roll forward)
.gitignore
.github/workflows/ci.yml                      dotnet test on push / PR
sim/
  Directory.Build.props                       shared compiler settings
  FantasyWorld.sln
  FantasyWorld.Sim/
    Core/WorldDate.cs                         calendar value type + Season
    Core/TimeScale.cs                         real time → world days, catch-up cap
    Core/StableHash.cs                        FNV-1a 64 (stable across runs/platforms)
    Core/Rng.cs                               SplitMix64 PRNG + derived streams
    Core/Sorted.cs                            helper: ordinal SortedDictionary copies
    Events/EntityRef.cs                       (Kind, Id) reference to any entity
    Events/Cause.cs                           Cause + CauseKind
    Events/EventDraft.cs                      what systems emit
    Events/WorldEvent.cs                      what the log stores
    Events/EventLog.cs                        append-only log + indexes
    Query/HistoryQuery.cs                     WhyChain, RootCauses, Consequences
    Query/EventText.cs                        one-line event description
    Model/World.cs                            world state (grows in Plans 2–4)
    Engine/ScheduledAction.cs
    Engine/EventQueue.cs                      (date, priority, seq) priority queue
    Engine/ISimSystem.cs
    Engine/SimContext.cs
    Engine/Invariants.cs                      log invariants + exception
    Engine/Simulator.cs                       day stepping, monthly tick, queue drain
    Engine/WorldFactory.cs
    Engine/CatchUp.cs                         real-time catch-up with cap + carry
    Persist/SimJson.cs                        shared JsonSerializerOptions
    Persist/WorldSnapshot.cs                  serializable world + queue state
    Persist/WorldHasher.cs                    SHA-256 state hash for determinism tests
    Persist/WorldStore.cs                     SQLite save/load
    Systems/DefaultSystems.cs                 system registration order
    Systems/Demo/HarvestSystem.cs
    Systems/Demo/UnrestSystem.cs
  FantasyWorld.Cli/
    FantasyWorld.Cli.csproj                   AssemblyName = fw
    Program.cs
    CliApp.cs                                 command dispatch (testable)
    DurationParser.cs
  FantasyWorld.Sim.Tests/
    Support/CoinSystem.cs                     random test system for determinism
    Support/ListProgress.cs                   synchronous IProgress<T>
    Support/TempFile.cs                       temp world-file path + cleanup
    Core/WorldDateTests.cs
    Core/TimeScaleTests.cs
    Core/RngTests.cs
    Events/EventLogTests.cs
    Query/HistoryQueryTests.cs
    Engine/EventQueueTests.cs
    Engine/SimulatorTests.cs
    Engine/DeterminismTests.cs
    Engine/CatchUpTests.cs
    Persist/WorldStoreTests.cs
    Systems/DemoSystemsTests.cs
    Cli/DurationParserTests.cs
    Cli/CliAppTests.cs
```

All commands below run from the repository root `C:\Development\Fantasy_world` in Git Bash unless stated.

---

### Task 1: Toolchain, solution scaffold, calendar and time scale

**Files:**
- Create: `global.json`, `.gitignore`, `sim/Directory.Build.props`, `sim/FantasyWorld.sln` (+ three projects via `dotnet new`)
- Create: `sim/FantasyWorld.Sim/Core/WorldDate.cs`, `sim/FantasyWorld.Sim/Core/TimeScale.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Core/WorldDateTests.cs`, `sim/FantasyWorld.Sim.Tests/Core/TimeScaleTests.cs`

**Interfaces:**
- Produces: `readonly record struct WorldDate(long Day)` with `Year`, `Month`, `DayOfMonth`, `Season`, `IsFirstOfMonth`, `IsFirstOfYear`, `FromYmd(int,int,int)`, `AddDays(long)`, comparison operators, consts `DaysPerMonth=30`, `MonthsPerYear=12`, `DaysPerYear=360`; `enum Season { Spring, Summer, Autumn, Winter }`; `static class TimeScale { TimeSpan RealTimePerWorldDay; const long MaxCatchUpDays = 10800; long WholeWorldDays(TimeSpan) }`.

- [x] **Step 1: Install the .NET 8 SDK**

Run (PowerShell):
```powershell
winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
```
Then open a new shell and run `dotnet --list-sdks`.
Expected: a line starting with `8.0.`

- [x] **Step 2: Create repo-level files**

`global.json`:
```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestMajor"
  }
}
```

`.gitignore`:
```
bin/
obj/
.vs/
*.user
TestResults/
*.fw.db
*.fw.db-wal
*.fw.db-shm
worlds/
```

`sim/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [x] **Step 3: Scaffold the solution**

```bash
cd sim
dotnet new sln -n FantasyWorld
dotnet new classlib -n FantasyWorld.Sim -o FantasyWorld.Sim -f net8.0
dotnet new xunit -n FantasyWorld.Sim.Tests -o FantasyWorld.Sim.Tests -f net8.0
dotnet new console -n FantasyWorld.Cli -o FantasyWorld.Cli -f net8.0
dotnet sln add FantasyWorld.Sim FantasyWorld.Sim.Tests FantasyWorld.Cli
dotnet add FantasyWorld.Cli reference FantasyWorld.Sim
dotnet add FantasyWorld.Sim.Tests reference FantasyWorld.Sim FantasyWorld.Cli
dotnet add FantasyWorld.Sim package Microsoft.Data.Sqlite --version 8.0.10
rm FantasyWorld.Sim/Class1.cs FantasyWorld.Sim.Tests/UnitTest1.cs
cd ..
```

Edit `sim/FantasyWorld.Cli/FantasyWorld.Cli.csproj` so its first `PropertyGroup` contains (keep the template's other properties):
```xml
    <AssemblyName>fw</AssemblyName>
    <RootNamespace>FantasyWorld.Cli</RootNamespace>
```

Run: `dotnet build sim`
Expected: `Build succeeded.` with 0 warnings.

- [x] **Step 4: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Core/WorldDateTests.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class WorldDateTests
{
    [Fact]
    public void DayZero_IsFirstDayOfYearOne()
    {
        var d = new WorldDate(0);
        Assert.Equal(1, d.Year);
        Assert.Equal(1, d.Month);
        Assert.Equal(1, d.DayOfMonth);
        Assert.Equal(Season.Spring, d.Season);
        Assert.True(d.IsFirstOfMonth);
        Assert.True(d.IsFirstOfYear);
    }

    [Fact]
    public void FromYmd_RoundTrips()
    {
        var d = WorldDate.FromYmd(312, 5, 16);
        Assert.Equal(312, d.Year);
        Assert.Equal(5, d.Month);
        Assert.Equal(16, d.DayOfMonth);
        Assert.False(d.IsFirstOfMonth);
    }

    [Fact]
    public void YearHas360Days()
    {
        Assert.Equal(WorldDate.FromYmd(2, 1, 1), WorldDate.FromYmd(1, 1, 1).AddDays(360));
    }

    [Theory]
    [InlineData(1, Season.Spring)]
    [InlineData(3, Season.Spring)]
    [InlineData(4, Season.Summer)]
    [InlineData(7, Season.Autumn)]
    [InlineData(12, Season.Winter)]
    public void MonthMapsToSeason(int month, Season expected)
    {
        Assert.Equal(expected, WorldDate.FromYmd(1, month, 1).Season);
    }

    [Fact]
    public void ComparisonOperators()
    {
        Assert.True(new WorldDate(1) < new WorldDate(2));
        Assert.True(new WorldDate(2) > new WorldDate(1));
        Assert.True(new WorldDate(2) <= new WorldDate(2));
        Assert.True(new WorldDate(2) >= new WorldDate(2));
    }

    [Fact]
    public void ToString_IsReadable()
    {
        Assert.Equal("Y312 M5 D16", WorldDate.FromYmd(312, 5, 16).ToString());
    }
}
```

`sim/FantasyWorld.Sim.Tests/Core/TimeScaleTests.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class TimeScaleTests
{
    [Fact]
    public void OneRealDay_IsOneWorldYear() =>
        Assert.Equal(360, TimeScale.WholeWorldDays(TimeSpan.FromDays(1)));

    [Fact]
    public void EightHoursOfSleep_IsFourMonths() =>
        Assert.Equal(120, TimeScale.WholeWorldDays(TimeSpan.FromHours(8)));

    [Fact]
    public void PartialWorldDay_RoundsDown() =>
        Assert.Equal(0, TimeScale.WholeWorldDays(TimeSpan.FromSeconds(239)));

    [Fact]
    public void NegativeElapsed_IsZero() =>
        Assert.Equal(0, TimeScale.WholeWorldDays(TimeSpan.FromHours(-3)));

    [Fact]
    public void CatchUpCap_IsThirtyYears() =>
        Assert.Equal(10_800, TimeScale.MaxCatchUpDays);
}
```

- [x] **Step 5: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'WorldDate' could not be found`.

- [x] **Step 6: Implement**

`sim/FantasyWorld.Sim/Core/WorldDate.cs`:
```csharp
using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Core;

public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>A world calendar date: days since Year 1, Month 1, Day 1.</summary>
public readonly record struct WorldDate(long Day) : IComparable<WorldDate>
{
    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

    [JsonIgnore] public int Year => (int)(Day / DaysPerYear) + 1;
    [JsonIgnore] public int Month => (int)(Day % DaysPerYear / DaysPerMonth) + 1;
    [JsonIgnore] public int DayOfMonth => (int)(Day % DaysPerMonth) + 1;
    [JsonIgnore] public Season Season => (Season)((Month - 1) / 3);
    [JsonIgnore] public bool IsFirstOfMonth => Day % DaysPerMonth == 0;
    [JsonIgnore] public bool IsFirstOfYear => Day % DaysPerYear == 0;

    public static WorldDate FromYmd(int year, int month, int day) =>
        new((long)(year - 1) * DaysPerYear + (month - 1) * DaysPerMonth + (day - 1));

    public WorldDate AddDays(long days) => new(Day + days);

    public int CompareTo(WorldDate other) => Day.CompareTo(other.Day);

    public static bool operator <(WorldDate a, WorldDate b) => a.Day < b.Day;
    public static bool operator >(WorldDate a, WorldDate b) => a.Day > b.Day;
    public static bool operator <=(WorldDate a, WorldDate b) => a.Day <= b.Day;
    public static bool operator >=(WorldDate a, WorldDate b) => a.Day >= b.Day;

    public override string ToString() => $"Y{Year} M{Month} D{DayOfMonth}";
}
```

`sim/FantasyWorld.Sim/Core/TimeScale.cs`:
```csharp
namespace FantasyWorld.Sim.Core;

/// <summary>1 real day = 1 world year, so 1 world day = 240 real seconds.</summary>
public static class TimeScale
{
    public static readonly TimeSpan RealTimePerWorldDay =
        TimeSpan.FromSeconds(86_400.0 / WorldDate.DaysPerYear);

    public const long MaxCatchUpDays = 30L * WorldDate.DaysPerYear;

    public static long WholeWorldDays(TimeSpan real) =>
        real <= TimeSpan.Zero ? 0 : real.Ticks / RealTimePerWorldDay.Ticks;
}
```

- [x] **Step 7: Run tests to verify they pass**

Run: `dotnet test sim`
Expected: `Passed!  - Failed: 0, Passed: 15`

- [x] **Step 8: Commit**

```bash
git add global.json .gitignore sim
git commit -m "feat(sim): scaffold solution with world calendar and time scale

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Stable hash and seeded RNG streams

**Files:**
- Create: `sim/FantasyWorld.Sim/Core/StableHash.cs`, `sim/FantasyWorld.Sim/Core/Rng.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Core/RngTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class StableHash { ulong Fnv1a64(string) }`; `sealed class Rng { Rng(ulong seed); static Rng ForStream(ulong worldSeed, string stream, long tick); ulong NextULong(); double NextDouble(); int NextInt(int minInclusive, int maxExclusive); bool Chance(double p) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Core/RngTests.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class RngTests
{
    [Fact]
    public void Fnv1a64_MatchesReferenceValues()
    {
        Assert.Equal(14695981039346656037UL, StableHash.Fnv1a64(""));
        Assert.Equal(0xAF63DC4C8601EC8CUL, StableHash.Fnv1a64("a"));
    }

    [Fact]
    public void SplitMix64_MatchesReferenceValue()
    {
        Assert.Equal(0xE220A8397B1DCDAFUL, new Rng(0).NextULong());
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void SameStreamAndTick_AreEqual() =>
        Assert.Equal(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "economy", 30).NextULong());

    [Fact]
    public void DifferentStreams_Differ() =>
        Assert.NotEqual(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "politics", 30).NextULong());

    [Fact]
    public void DifferentTicks_Differ() =>
        Assert.NotEqual(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "economy", 60).NextULong());

    [Fact]
    public void DifferentSeeds_Differ() =>
        Assert.NotEqual(Rng.ForStream(1, "economy", 30).NextULong(), Rng.ForStream(2, "economy", 30).NextULong());

    [Fact]
    public void NextDouble_IsInUnitInterval()
    {
        var rng = new Rng(7);
        for (var i = 0; i < 10_000; i++)
        {
            var d = rng.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void NextInt_StaysInRangeAndHitsBothEnds()
    {
        var rng = new Rng(7);
        var seen = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.NextInt(-2, 3);
            Assert.InRange(v, -2, 2);
            seen.Add(v);
        }
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void NextInt_RejectsEmptyRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rng(1).NextInt(3, 3));

    [Fact]
    public void Chance_RespectsExtremes()
    {
        var rng = new Rng(3);
        for (var i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0.0));
            Assert.True(rng.Chance(1.0));
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~RngTests`
Expected: build FAILS with `CS0103: The name 'StableHash' does not exist`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Core/StableHash.cs`:
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
}
```

`sim/FantasyWorld.Sim/Core/Rng.cs`:
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

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~RngTests`
Expected: `Passed!  - Failed: 0, Passed: 11`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add stable hash and seeded RNG streams

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Event model and append-only event log

**Files:**
- Create: `sim/FantasyWorld.Sim/Core/Sorted.cs`, `sim/FantasyWorld.Sim/Events/EntityRef.cs`, `Events/Cause.cs`, `Events/EventDraft.cs`, `Events/WorldEvent.cs`, `Events/EventLog.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Events/EventLogTests.cs`

**Interfaces:**
- Consumes: `WorldDate` (Task 1).
- Produces:
  - `readonly record struct EntityRef(string Kind, long Id)` — `ToString()` = `"{Kind}#{Id}"`.
  - `enum CauseKind { Trigger, Motive, Enabler }`; `sealed record Cause(long EventId, CauseKind Kind, double Weight)`.
  - `sealed record EventDraft(string Type, IReadOnlyDictionary<string, EntityRef>? Actors = null, EntityRef? Place = null, IReadOnlyDictionary<string, string>? Payload = null, IReadOnlyList<Cause>? Causes = null, int Importance = 10, IReadOnlyList<string>? Tags = null)`.
  - `sealed record WorldEvent(long Id, WorldDate Date, string Type, SortedDictionary<string, EntityRef> Actors, EntityRef? Place, SortedDictionary<string, string> Payload, IReadOnlyList<Cause> Causes, int Importance, IReadOnlyList<string> Tags)`.
  - `sealed class EventLog { const int MaxCauses = 3; long NextId; int Count; IReadOnlyList<WorldEvent> All; WorldEvent Append(WorldDate, EventDraft); void Restore(WorldEvent); WorldEvent Get(long); bool TryGet(long, out WorldEvent); IReadOnlyList<long> EffectsOf(long); IReadOnlyList<WorldEvent> Involving(EntityRef); IEnumerable<WorldEvent> Since(long afterId) }`.
  - `internal static class Sorted { SortedDictionary<string, T> From<T>(IEnumerable<KeyValuePair<string, T>>?) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Events/EventLogTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Events;

public class EventLogTests
{
    private static readonly WorldDate D10 = new(10);

    [Fact]
    public void Append_AssignsIncreasingIdsAndDate()
    {
        var log = new EventLog();
        var a = log.Append(D10, new EventDraft("A"));
        var b = log.Append(D10, new EventDraft("B"));
        Assert.Equal(1, a.Id);
        Assert.Equal(2, b.Id);
        Assert.Equal(D10, b.Date);
        Assert.Equal(2, log.Count);
        Assert.Equal(3, log.NextId);
    }

    [Fact]
    public void Append_RejectsBlankType() =>
        Assert.Throws<ArgumentException>(() => new EventLog().Append(D10, new EventDraft(" ")));

    [Fact]
    public void Append_RejectsUnknownCause()
    {
        var log = new EventLog();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            log.Append(D10, new EventDraft("A", Causes: new[] { new Cause(99, CauseKind.Trigger, 1) })));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Append_RejectsDateBeforeLastEvent()
    {
        var log = new EventLog();
        log.Append(D10, new EventDraft("A"));
        Assert.Throws<InvalidOperationException>(() => log.Append(new WorldDate(9), new EventDraft("B")));
    }

    [Fact]
    public void Append_KeepsThreeStrongestCauses()
    {
        var log = new EventLog();
        for (var i = 0; i < 4; i++) log.Append(D10, new EventDraft("Seed"));
        var ev = log.Append(D10, new EventDraft("Effect", Causes: new[]
        {
            new Cause(1, CauseKind.Trigger, 0.1),
            new Cause(2, CauseKind.Motive, 0.9),
            new Cause(3, CauseKind.Enabler, 0.5),
            new Cause(4, CauseKind.Trigger, 0.7),
        }));
        Assert.Equal(new long[] { 2, 4, 3 }, ev.Causes.Select(c => c.EventId));
    }

    [Fact]
    public void EffectsOf_ReturnsReverseLinks()
    {
        var log = new EventLog();
        var cause = log.Append(D10, new EventDraft("Cause"));
        var e1 = log.Append(D10, new EventDraft("E1", Causes: new[] { new Cause(cause.Id, CauseKind.Trigger, 1) }));
        var e2 = log.Append(D10, new EventDraft("E2", Causes: new[] { new Cause(cause.Id, CauseKind.Motive, 1) }));
        Assert.Equal(new[] { e1.Id, e2.Id }, log.EffectsOf(cause.Id));
        Assert.Empty(log.EffectsOf(e1.Id));
    }

    [Fact]
    public void Involving_IndexesActorsAndPlace()
    {
        var log = new EventLog();
        var aldric = new EntityRef("char", 88);
        var ford = new EntityRef("region", 3);
        var ev = log.Append(D10, new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["ruler"] = aldric },
            Place: ford));
        log.Append(D10, new EventDraft("Unrelated"));
        Assert.Equal(ev.Id, Assert.Single(log.Involving(aldric)).Id);
        Assert.Equal(ev.Id, Assert.Single(log.Involving(ford)).Id);
        Assert.Empty(log.Involving(new EntityRef("char", 1)));
    }

    [Fact]
    public void Payload_IsStoredSortedByKey()
    {
        var log = new EventLog();
        var ev = log.Append(D10, new EventDraft("A",
            Payload: new Dictionary<string, string> { ["zeta"] = "1", ["alpha"] = "2" }));
        Assert.Equal(new[] { "alpha", "zeta" }, ev.Payload.Keys);
    }

    [Fact]
    public void Since_ReturnsOnlyNewerEvents()
    {
        var log = new EventLog();
        log.Append(D10, new EventDraft("A"));
        log.Append(D10, new EventDraft("B"));
        log.Append(D10, new EventDraft("C"));
        Assert.Equal(new[] { "B", "C" }, log.Since(1).Select(e => e.Type));
    }

    [Fact]
    public void Restore_RebuildsIndexesAndNextId()
    {
        var source = new EventLog();
        var a = source.Append(D10, new EventDraft("A"));
        var b = source.Append(D10, new EventDraft("B", Causes: new[] { new Cause(a.Id, CauseKind.Trigger, 1) }));

        var restored = new EventLog();
        restored.Restore(a);
        restored.Restore(b);

        Assert.Equal(3, restored.NextId);
        Assert.Equal(new[] { b.Id }, restored.EffectsOf(a.Id));
        Assert.Equal(3, restored.Append(D10, new EventDraft("C")).Id);
    }

    [Fact]
    public void Restore_RejectsNonIncreasingId()
    {
        var source = new EventLog();
        var a = source.Append(D10, new EventDraft("A"));
        var restored = new EventLog();
        restored.Restore(a);
        Assert.Throws<InvalidOperationException>(() => restored.Restore(a));
    }

    [Fact]
    public void Get_UnknownId_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => new EventLog().Get(5));
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~EventLogTests`
Expected: build FAILS with `CS0246: The type or namespace name 'EventLog' could not be found`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Core/Sorted.cs`:
```csharp
namespace FantasyWorld.Sim.Core;

internal static class Sorted
{
    public static SortedDictionary<string, T> From<T>(IEnumerable<KeyValuePair<string, T>>? source)
    {
        var result = new SortedDictionary<string, T>(StringComparer.Ordinal);
        if (source is null) return result;
        foreach (var (key, value) in source) result[key] = value;
        return result;
    }
}
```

`sim/FantasyWorld.Sim/Events/EntityRef.cs`:
```csharp
namespace FantasyWorld.Sim.Events;

/// <summary>Reference to any simulated entity, e.g. ("char", 88) or ("region", 3).</summary>
public readonly record struct EntityRef(string Kind, long Id)
{
    public override string ToString() => $"{Kind}#{Id}";
}
```

`sim/FantasyWorld.Sim/Events/Cause.cs`:
```csharp
namespace FantasyWorld.Sim.Events;

public enum CauseKind { Trigger, Motive, Enabler }

public sealed record Cause(long EventId, CauseKind Kind, double Weight);
```

`sim/FantasyWorld.Sim/Events/EventDraft.cs`:
```csharp
namespace FantasyWorld.Sim.Events;

/// <summary>What a system emits. The log assigns id and date.</summary>
public sealed record EventDraft(
    string Type,
    IReadOnlyDictionary<string, EntityRef>? Actors = null,
    EntityRef? Place = null,
    IReadOnlyDictionary<string, string>? Payload = null,
    IReadOnlyList<Cause>? Causes = null,
    int Importance = 10,
    IReadOnlyList<string>? Tags = null);
```

`sim/FantasyWorld.Sim/Events/WorldEvent.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Events;

public sealed record WorldEvent(
    long Id,
    WorldDate Date,
    string Type,
    SortedDictionary<string, EntityRef> Actors,
    EntityRef? Place,
    SortedDictionary<string, string> Payload,
    IReadOnlyList<Cause> Causes,
    int Importance,
    IReadOnlyList<string> Tags);
```

`sim/FantasyWorld.Sim/Events/EventLog.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Events;

/// <summary>Append-only causal history. Causes always point to earlier events.</summary>
public sealed class EventLog
{
    public const int MaxCauses = 3;

    private readonly List<WorldEvent> _events = new();
    private readonly Dictionary<long, WorldEvent> _byId = new();
    private readonly Dictionary<long, List<long>> _effects = new();
    private readonly Dictionary<EntityRef, List<WorldEvent>> _byEntity = new();

    public long NextId { get; private set; } = 1;
    public int Count => _events.Count;
    public IReadOnlyList<WorldEvent> All => _events;

    public WorldEvent Append(WorldDate date, EventDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Type))
            throw new ArgumentException("Event type is required.", nameof(draft));
        if (_events.Count > 0 && date < _events[^1].Date)
            throw new InvalidOperationException($"Event dated {date} is earlier than the last event ({_events[^1].Date}).");

        var causes = (draft.Causes ?? Array.Empty<Cause>())
            .OrderByDescending(c => c.Weight)
            .ThenBy(c => c.EventId)
            .Take(MaxCauses)
            .ToArray();
        foreach (var cause in causes)
            if (!_byId.ContainsKey(cause.EventId))
                throw new InvalidOperationException($"Cause event #{cause.EventId} does not exist.");

        var ev = new WorldEvent(
            NextId,
            date,
            draft.Type,
            Sorted.From(draft.Actors),
            draft.Place,
            Sorted.From(draft.Payload),
            causes,
            draft.Importance,
            draft.Tags?.ToArray() ?? Array.Empty<string>());
        Add(ev);
        NextId++;
        return ev;
    }

    /// <summary>Re-adds a persisted event without cause validation (see Invariants for checks).</summary>
    public void Restore(WorldEvent ev)
    {
        if (ev.Id < NextId)
            throw new InvalidOperationException($"Restored event #{ev.Id} must have an id >= {NextId}.");
        Add(ev);
        NextId = ev.Id + 1;
    }

    public WorldEvent Get(long id) =>
        _byId.TryGetValue(id, out var ev) ? ev : throw new KeyNotFoundException($"Event #{id} not found.");

    public bool TryGet(long id, [MaybeNullWhen(false)] out WorldEvent ev) => _byId.TryGetValue(id, out ev);

    public IReadOnlyList<long> EffectsOf(long id) =>
        _effects.TryGetValue(id, out var list) ? list : Array.Empty<long>();

    public IReadOnlyList<WorldEvent> Involving(EntityRef entity) =>
        _byEntity.TryGetValue(entity, out var list) ? list : Array.Empty<WorldEvent>();

    public IEnumerable<WorldEvent> Since(long afterId) => _events.Where(e => e.Id > afterId);

    private void Add(WorldEvent ev)
    {
        _events.Add(ev);
        _byId[ev.Id] = ev;
        foreach (var cause in ev.Causes)
        {
            if (!_effects.TryGetValue(cause.EventId, out var effects))
                _effects[cause.EventId] = effects = new List<long>();
            effects.Add(ev.Id);
        }
        var entities = ev.Actors.Values.ToList();
        if (ev.Place is { } place) entities.Add(place);
        foreach (var entity in entities.Distinct())
        {
            if (!_byEntity.TryGetValue(entity, out var list))
                _byEntity[entity] = list = new List<WorldEvent>();
            list.Add(ev);
        }
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~EventLogTests`
Expected: `Passed!  - Failed: 0, Passed: 12`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add causal event model and append-only event log

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: History queries (why chain, root causes, consequences)

**Files:**
- Create: `sim/FantasyWorld.Sim/Query/HistoryQuery.cs`, `sim/FantasyWorld.Sim/Query/EventText.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs`

**Interfaces:**
- Consumes: `EventLog`, `WorldEvent`, `Cause`, `CauseKind` (Task 3).
- Produces: `sealed record ChainNode(WorldEvent Event, CauseKind? Kind, IReadOnlyList<ChainNode> Causes)`; `sealed class HistoryQuery { HistoryQuery(EventLog); ChainNode WhyChain(long eventId, int depth = 4); IReadOnlyList<WorldEvent> RootCauses(long eventId); IReadOnlyList<WorldEvent> Consequences(long eventId, int depth = 1) }`; `static class EventText { string Describe(WorldEvent) }` → `"#12 Y3 M2 D1 RevoltBegan grain=35"`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs`:
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
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~HistoryQueryTests`
Expected: build FAILS with `CS0246: The type or namespace name 'HistoryQuery' could not be found`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Query/HistoryQuery.cs`:
```csharp
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

/// <summary>A node in a "why" tree. Kind is how this event relates to its parent (null at the root).</summary>
public sealed record ChainNode(WorldEvent Event, CauseKind? Kind, IReadOnlyList<ChainNode> Causes);

public sealed class HistoryQuery
{
    private readonly EventLog _log;

    public HistoryQuery(EventLog log) => _log = log;

    public ChainNode WhyChain(long eventId, int depth = 4) => Build(_log.Get(eventId), null, depth);

    public IReadOnlyList<WorldEvent> RootCauses(long eventId)
    {
        var roots = new SortedSet<long>();
        var seen = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(eventId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id)) continue;
            var ev = _log.Get(id);
            if (ev.Causes.Count == 0)
            {
                if (id != eventId) roots.Add(id);
                continue;
            }
            foreach (var cause in ev.Causes) stack.Push(cause.EventId);
        }
        return roots.Select(_log.Get).ToList();
    }

    public IReadOnlyList<WorldEvent> Consequences(long eventId, int depth = 1)
    {
        var found = new SortedSet<long>();
        var frontier = new List<long> { eventId };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = new List<long>();
            foreach (var id in frontier)
                foreach (var effect in _log.EffectsOf(id))
                    if (found.Add(effect)) next.Add(effect);
            frontier = next;
        }
        return found.Select(_log.Get).ToList();
    }

    private ChainNode Build(WorldEvent ev, CauseKind? kind, int depth)
    {
        var causes = depth <= 0
            ? Array.Empty<ChainNode>()
            : ev.Causes.Select(c => Build(_log.Get(c.EventId), c.Kind, depth - 1)).ToArray();
        return new ChainNode(ev, kind, causes);
    }
}
```

`sim/FantasyWorld.Sim/Query/EventText.cs`:
```csharp
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

public static class EventText
{
    public static string Describe(WorldEvent ev)
    {
        var text = $"#{ev.Id} {ev.Date} {ev.Type}";
        if (ev.Actors.Count > 0)
            text += " " + string.Join(", ", ev.Actors.Select(a => $"{a.Key}={a.Value}"));
        if (ev.Place is { } place)
            text += $" at {place}";
        if (ev.Payload.Count > 0)
            text += " " + string.Join(", ", ev.Payload.Select(p => $"{p.Key}={p.Value}"));
        return text;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~HistoryQueryTests`
Expected: `Passed!  - Failed: 0, Passed: 6`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add why-chain, root-cause and consequence queries

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Scheduled-action queue

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/ScheduledAction.cs`, `sim/FantasyWorld.Sim/Engine/EventQueue.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Engine/EventQueueTests.cs`

**Interfaces:**
- Consumes: `WorldDate` (Task 1), `Sorted` (Task 3).
- Produces: `sealed record ScheduledAction(WorldDate Date, int Priority, long Seq, string Kind, SortedDictionary<string, string> Data)`; `sealed class EventQueue { long NextSeq; int Count; ScheduledAction Schedule(WorldDate date, string kind, IReadOnlyDictionary<string, string>? data = null, int priority = 0); bool TryDequeueDue(WorldDate today, out ScheduledAction action); IReadOnlyList<ScheduledAction> Snapshot(); void Restore(IEnumerable<ScheduledAction> actions, long nextSeq) }`. Order: date, then priority (lower first), then seq.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/EventQueueTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Tests.Engine;

public class EventQueueTests
{
    private static List<string> DrainUntil(EventQueue q, long day)
    {
        var kinds = new List<string>();
        while (q.TryDequeueDue(new WorldDate(day), out var a)) kinds.Add(a.Kind);
        return kinds;
    }

    [Fact]
    public void Dequeues_ByDateThenPriorityThenSeq()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(5), "late");
        q.Schedule(new WorldDate(3), "b", priority: 1);
        q.Schedule(new WorldDate(3), "a", priority: 0);
        q.Schedule(new WorldDate(3), "c", priority: 1);
        Assert.Equal(new[] { "a", "b", "c", "late" }, DrainUntil(q, 10));
    }

    [Fact]
    public void FutureActions_AreNotDue()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(5), "x");
        Assert.Empty(DrainUntil(q, 4));
        Assert.Equal(new[] { "x" }, DrainUntil(q, 5));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Schedule_CopiesDataSorted()
    {
        var q = new EventQueue();
        var a = q.Schedule(new WorldDate(1), "x", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        Assert.Equal(new[] { "a", "b" }, a.Data.Keys);
        Assert.Equal(0, a.Seq);
        Assert.Equal(1, q.NextSeq);
    }

    [Fact]
    public void SnapshotAndRestore_PreserveOrderAndSeq()
    {
        var q = new EventQueue();
        q.Schedule(new WorldDate(9), "z");
        q.Schedule(new WorldDate(2), "y");
        q.Schedule(new WorldDate(2), "x", priority: -1);

        var snap = q.Snapshot();
        Assert.Equal(new[] { "x", "y", "z" }, snap.Select(a => a.Kind));

        var copy = new EventQueue();
        copy.Restore(snap, q.NextSeq);
        Assert.Equal(3, copy.NextSeq);
        Assert.Equal(new[] { "x", "y", "z" }, DrainUntil(copy, 100));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~EventQueueTests`
Expected: build FAILS with `CS0246: The type or namespace name 'EventQueue' could not be found`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Engine/ScheduledAction.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

/// <summary>Something that will happen on a future world day, handled by the system that owns Kind.</summary>
public sealed record ScheduledAction(
    WorldDate Date,
    int Priority,
    long Seq,
    string Kind,
    SortedDictionary<string, string> Data);
```

`sim/FantasyWorld.Sim/Engine/EventQueue.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

public sealed class EventQueue
{
    private readonly PriorityQueue<ScheduledAction, (long Day, int Priority, long Seq)> _queue = new();

    public long NextSeq { get; private set; }
    public int Count => _queue.Count;

    public ScheduledAction Schedule(
        WorldDate date, string kind, IReadOnlyDictionary<string, string>? data = null, int priority = 0)
    {
        var action = new ScheduledAction(date, priority, NextSeq++, kind, Sorted.From(data));
        _queue.Enqueue(action, Key(action));
        return action;
    }

    public bool TryDequeueDue(WorldDate today, out ScheduledAction action)
    {
        if (_queue.TryPeek(out var next, out _) && next.Date <= today)
        {
            action = _queue.Dequeue();
            return true;
        }
        action = null!;
        return false;
    }

    public IReadOnlyList<ScheduledAction> Snapshot() =>
        _queue.UnorderedItems.Select(i => i.Element).OrderBy(Key).ToList();

    public void Restore(IEnumerable<ScheduledAction> actions, long nextSeq)
    {
        _queue.Clear();
        foreach (var action in actions) _queue.Enqueue(action, Key(action));
        NextSeq = nextSeq;
    }

    private static (long Day, int Priority, long Seq) Key(ScheduledAction a) => (a.Date.Day, a.Priority, a.Seq);
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~EventQueueTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add deterministic scheduled-action queue

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: World, systems, simulator and invariants

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/World.cs`, `Engine/ISimSystem.cs`, `Engine/SimContext.cs`, `Engine/Invariants.cs`, `Engine/Simulator.cs`, `Engine/WorldFactory.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Engine/SimulatorTests.cs`

**Interfaces:**
- Consumes: `WorldDate`, `Rng` (Tasks 1–2); `EventLog`, `EventDraft`, `WorldEvent` (Task 3); `EventQueue`, `ScheduledAction` (Task 5).
- Produces:
  - `sealed class World { required ulong Seed; WorldDate Date; DateTimeOffset LastRealUtc; long LastSeenEventId; SortedDictionary<string, double> Stats; SortedDictionary<string, long> Markers }`.
  - `interface ISimSystem { string Name { get; } void OnMonthlyTick(SimContext ctx); IEnumerable<string> HandledKinds => []; void OnScheduled(SimContext ctx, ScheduledAction action) => throw NotSupportedException }`.
  - `sealed class SimContext { World World; EventLog Log; EventQueue Queue; Rng Rng; WorldDate Date; WorldEvent Emit(EventDraft) }`.
  - `static class Invariants { IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0) }`; `sealed class InvariantViolationException : Exception`.
  - `sealed class Simulator { Simulator(World, EventLog, EventQueue, IReadOnlyList<ISimSystem>, bool checkInvariants = false); World World; EventLog Log; EventQueue Queue; void AdvanceDays(long days, IProgress<WorldDate>? progress = null, CancellationToken ct = default) }`. Each step moves the date forward one day, runs every system's `OnMonthlyTick` (in registration order) when the new date is the first of a month, then drains due scheduled actions. Progress is reported on each first-of-month.
  - `static class WorldFactory { World CreateNew(ulong seed, DateTimeOffset nowUtc) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/SimulatorTests.cs`:
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

Also create `sim/FantasyWorld.Sim.Tests/Support/ListProgress.cs`:
```csharp
namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Synchronous IProgress (System.Progress posts asynchronously).</summary>
public sealed class ListProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;
    public ListProgress(Action<T> onReport) => _onReport = onReport;
    public void Report(T value) => _onReport(value);
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~SimulatorTests`
Expected: build FAILS with `CS0246: The type or namespace name 'ISimSystem' could not be found`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Model/World.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>All simulated state. Grows with real entities in Plans 2–4.</summary>
public sealed class World
{
    public required ulong Seed { get; init; }
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    /// <summary>Named scalar state (demo systems use "grain" and "unrest").</summary>
    public SortedDictionary<string, double> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Named references to "currently active" events, e.g. an ongoing shortage.</summary>
    public SortedDictionary<string, long> Markers { get; init; } = new(StringComparer.Ordinal);
}
```

`sim/FantasyWorld.Sim/Engine/ISimSystem.cs`:
```csharp
namespace FantasyWorld.Sim.Engine;

/// <summary>A simulation system. Systems never call each other; they communicate through events.</summary>
public interface ISimSystem
{
    string Name { get; }

    void OnMonthlyTick(SimContext ctx);

    /// <summary>Scheduled-action kinds this system handles. Each kind has exactly one owner.</summary>
    IEnumerable<string> HandledKinds => Array.Empty<string>();

    void OnScheduled(SimContext ctx, ScheduledAction action) =>
        throw new NotSupportedException($"System '{Name}' does not handle scheduled actions.");
}
```

`sim/FantasyWorld.Sim/Engine/SimContext.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>What a system sees during one tick: the world, the log, the queue and its own RNG stream.</summary>
public sealed class SimContext
{
    public SimContext(World world, EventLog log, EventQueue queue, Rng rng)
    {
        World = world;
        Log = log;
        Queue = queue;
        Rng = rng;
    }

    public World World { get; }
    public EventLog Log { get; }
    public EventQueue Queue { get; }
    public Rng Rng { get; }
    public WorldDate Date => World.Date;

    public WorldEvent Emit(EventDraft draft) => Log.Append(World.Date, draft);
}
```

`sim/FantasyWorld.Sim/Engine/Invariants.cs`:
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
    /// <summary>Checks log events from startIndex onward. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0)
    {
        var errors = new List<string>();
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
        return errors;
    }
}
```

`sim/FantasyWorld.Sim/Engine/Simulator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>Advances the world one day at a time. Catch-up and live mode both use AdvanceDays.</summary>
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

`sim/FantasyWorld.Sim/Engine/WorldFactory.cs`:
```csharp
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static class WorldFactory
{
    public static World CreateNew(ulong seed, DateTimeOffset nowUtc) => new() { Seed = seed, LastRealUtc = nowUtc };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~SimulatorTests`
Expected: `Passed!  - Failed: 0, Passed: 9`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add day-stepping simulator with monthly ticks and invariants

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Snapshots, state hashing and determinism guarantees

**Files:**
- Create: `sim/FantasyWorld.Sim/Persist/SimJson.cs`, `Persist/WorldSnapshot.cs`, `Persist/WorldHasher.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Support/CoinSystem.cs`, `sim/FantasyWorld.Sim.Tests/Engine/DeterminismTests.cs`

**Interfaces:**
- Consumes: `World`, `Simulator`, `WorldFactory`, `ISimSystem`, `EventQueue`, `ScheduledAction` (Tasks 5–6); `EventLog`, `WorldEvent` (Task 3).
- Produces: `static class SimJson { JsonSerializerOptions Options }`; `sealed record WorldSnapshot(int SchemaVersion, ulong Seed, long Day, DateTimeOffset LastRealUtc, long LastSeenEventId, SortedDictionary<string, double> Stats, SortedDictionary<string, long> Markers, List<ScheduledAction> Queue, long QueueNextSeq) { static WorldSnapshot Capture(World, EventQueue); World ToWorld(); EventQueue ToQueue() }`; `static class WorldHasher { string Hash(World, EventLog, EventQueue) }` (uppercase hex SHA-256). Test support: `CoinSystem` (name `"coin"`, handles `"echo"`).

- [x] **Step 1: Write the test support system**

`sim/FantasyWorld.Sim.Tests/Support/CoinSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Random test system: flips coins monthly, chains them causally, schedules echoes.</summary>
public sealed class CoinSystem : ISimSystem
{
    public string Name => "coin";
    public IEnumerable<string> HandledKinds => new[] { "echo" };

    public void OnMonthlyTick(SimContext ctx)
    {
        if (!ctx.Rng.Chance(0.5)) return;
        var causes = ctx.World.Markers.TryGetValue("lastCoin", out var last)
            ? new[] { new Cause(last, CauseKind.Enabler, 0.5) }
            : Array.Empty<Cause>();
        var coin = ctx.Emit(new EventDraft("Coin",
            Payload: new Dictionary<string, string> { ["v"] = ctx.Rng.NextInt(0, 1000).ToString(CultureInfo.InvariantCulture) },
            Causes: causes));
        ctx.World.Markers["lastCoin"] = coin.Id;
        ctx.World.Stats["coins"] = ctx.World.Stats.GetValueOrDefault("coins", 0) + 1;
        ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(1, 20)), "echo",
            new Dictionary<string, string> { ["of"] = coin.Id.ToString(CultureInfo.InvariantCulture) });
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action) =>
        ctx.Emit(new EventDraft("Echo", Causes: new[]
        {
            new Cause(long.Parse(action.Data["of"], CultureInfo.InvariantCulture), CauseKind.Trigger, 1.0),
        }));
}
```

- [x] **Step 2: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/DeterminismTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class DeterminismTests
{
    private static Simulator NewSim(ulong seed) =>
        new(WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    [Fact]
    public void SameSeed_ProducesIdenticalWorlds()
    {
        var a = NewSim(7);
        var b = NewSim(7);
        a.AdvanceDays(3600);
        b.AdvanceDays(3600);
        Assert.True(a.Log.Count > 20, "test system should produce events");
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentWorlds()
    {
        var a = NewSim(7);
        var b = NewSim(8);
        a.AdvanceDays(3600);
        b.AdvanceDays(3600);
        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Chunking_DoesNotChangeTheWorld()
    {
        var oneShot = NewSim(11);
        oneShot.AdvanceDays(3600);

        var chunked = NewSim(11);
        foreach (var chunk in new long[] { 1, 29, 300, 1000, 2270 }) chunked.AdvanceDays(chunk);

        var daily = NewSim(11);
        for (var i = 0; i < 3600; i++) daily.AdvanceDays(1);

        Assert.Equal(Hash(oneShot), Hash(chunked));
        Assert.Equal(Hash(oneShot), Hash(daily));
    }

    [Fact]
    public void SnapshotRoundTrip_ContinuesIdentically()
    {
        var original = NewSim(9);
        original.AdvanceDays(1000);

        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(original.World, original.Queue), SimJson.Options);
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!;
        var log = new EventLog();
        foreach (var ev in original.Log.All)
            log.Restore(JsonSerializer.Deserialize<WorldEvent>(JsonSerializer.Serialize(ev, SimJson.Options), SimJson.Options)!);
        var restored = new Simulator(snapshot.ToWorld(), log, snapshot.ToQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

        Assert.Equal(Hash(original), Hash(restored));
        original.AdvanceDays(360);
        restored.AdvanceDays(360);
        Assert.Equal(Hash(original), Hash(restored));
    }

    [Fact]
    public void Hash_IsUppercaseSha256Hex()
    {
        var hash = Hash(NewSim(1));
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }
}
```

- [x] **Step 3: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~DeterminismTests`
Expected: build FAILS with `CS0234: The type or namespace name 'Persist' does not exist`.

- [x] **Step 4: Implement**

`sim/FantasyWorld.Sim/Persist/SimJson.cs`:
```csharp
using System.Text.Json;

namespace FantasyWorld.Sim.Persist;

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
}
```

`sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>Serializable world + queue state. The event log is stored separately.</summary>
public sealed record WorldSnapshot(
    int SchemaVersion,
    ulong Seed,
    long Day,
    DateTimeOffset LastRealUtc,
    long LastSeenEventId,
    SortedDictionary<string, double> Stats,
    SortedDictionary<string, long> Markers,
    List<ScheduledAction> Queue,
    long QueueNextSeq)
{
    public const int CurrentVersion = 1;

    public static WorldSnapshot Capture(World world, EventQueue queue) => new(
        CurrentVersion,
        world.Seed,
        world.Date.Day,
        world.LastRealUtc,
        world.LastSeenEventId,
        Sorted.From(world.Stats),
        Sorted.From(world.Markers),
        queue.Snapshot().ToList(),
        queue.NextSeq);

    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
    };

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}
```

`sim/FantasyWorld.Sim/Persist/WorldHasher.cs`:
```csharp
using System.Security.Cryptography;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>SHA-256 over the serialized snapshot and event log. Used to prove determinism.</summary>
public static class WorldHasher
{
    public static string Hash(World world, EventLog log, EventQueue queue)
    {
        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(WorldSnapshot.Capture(world, queue), SimJson.Options);
        byte[] events = JsonSerializer.SerializeToUtf8Bytes(log.All, SimJson.Options);
        var all = new byte[snapshot.Length + events.Length];
        snapshot.CopyTo(all, 0);
        events.CopyTo(all, snapshot.Length);
        return Convert.ToHexString(SHA256.HashData(all));
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~DeterminismTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): add snapshots and state hashing with determinism tests

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Real-time catch-up

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/CatchUp.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Engine/CatchUpTests.cs`

**Interfaces:**
- Consumes: `Simulator` (Task 6), `TimeScale` (Task 1).
- Produces: `sealed record CatchUpResult(long DaysAdvanced, long DaysRemaining, WorldDate From, WorldDate To)`; `static class CatchUp { CatchUpResult Run(Simulator sim, DateTimeOffset nowUtc, IProgress<WorldDate>? progress = null, CancellationToken ct = default) }`. Rules: owed = whole world days in `now − world.LastRealUtc`; advance `min(owed, 10800)`; move `LastRealUtc` forward only by the real time actually consumed (so sub-day remainders and capped days carry over); negative elapsed (clock moved back) does nothing.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/CatchUpTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class CatchUpTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static Simulator NewSim() =>
        new(WorldFactory.CreateNew(1, T0), new EventLog(), new EventQueue(), Array.Empty<ISimSystem>());

    [Fact]
    public void EightHoursAway_AdvancesFourMonths()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddHours(8));
        Assert.Equal(120, result.DaysAdvanced);
        Assert.Equal(0, result.DaysRemaining);
        Assert.Equal(new WorldDate(0), result.From);
        Assert.Equal(new WorldDate(120), result.To);
        Assert.Equal(T0.AddHours(8), sim.World.LastRealUtc);
    }

    [Fact]
    public void SubDayRemainder_CarriesToNextRun()
    {
        var sim = NewSim();
        CatchUp.Run(sim, T0.AddHours(8).AddSeconds(100));
        Assert.Equal(T0.AddHours(8), sim.World.LastRealUtc);

        var second = CatchUp.Run(sim, T0.AddHours(8).AddSeconds(240));
        Assert.Equal(1, second.DaysAdvanced);
        Assert.Equal(121, sim.World.Date.Day);
    }

    [Fact]
    public void ClockMovedBackwards_DoesNothing()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddHours(-5));
        Assert.Equal(0, result.DaysAdvanced);
        Assert.Equal(0, sim.World.Date.Day);
        Assert.Equal(T0, sim.World.LastRealUtc);
    }

    [Fact]
    public void LongAbsence_IsCappedAndRemainderReported()
    {
        var sim = NewSim();
        var result = CatchUp.Run(sim, T0.AddDays(60));
        Assert.Equal(10_800, result.DaysAdvanced);
        Assert.Equal(10_800, result.DaysRemaining);
        Assert.Equal(T0.AddDays(30), sim.World.LastRealUtc);

        var next = CatchUp.Run(sim, T0.AddDays(60));
        Assert.Equal(10_800, next.DaysAdvanced);
        Assert.Equal(0, next.DaysRemaining);
    }

    [Fact]
    public void ReportsProgressPerMonth()
    {
        var sim = NewSim();
        var reports = new List<WorldDate>();
        CatchUp.Run(sim, T0.AddHours(8), new ListProgress<WorldDate>(reports.Add));
        Assert.Equal(new long[] { 30, 60, 90, 120 }, reports.Select(r => r.Day));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~CatchUpTests`
Expected: build FAILS with `CS0103: The name 'CatchUp' does not exist`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Engine/CatchUp.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

public sealed record CatchUpResult(long DaysAdvanced, long DaysRemaining, WorldDate From, WorldDate To);

/// <summary>Fast-forwards the world by the real time that passed since it last ran.</summary>
public static class CatchUp
{
    public static CatchUpResult Run(
        Simulator sim, DateTimeOffset nowUtc, IProgress<WorldDate>? progress = null, CancellationToken ct = default)
    {
        var world = sim.World;
        var from = world.Date;
        var owed = TimeScale.WholeWorldDays(nowUtc - world.LastRealUtc);
        if (owed <= 0) return new CatchUpResult(0, 0, from, from);

        var days = Math.Min(owed, TimeScale.MaxCatchUpDays);
        sim.AdvanceDays(days, progress, ct);
        world.LastRealUtc += TimeSpan.FromTicks(TimeScale.RealTimePerWorldDay.Ticks * days);
        return new CatchUpResult(days, owed - days, from, world.Date);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~CatchUpTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add real-time catch-up with 30-year cap and carry-over

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: SQLite world store

**Files:**
- Create: `sim/FantasyWorld.Sim/Persist/WorldStore.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Support/TempFile.cs`, `sim/FantasyWorld.Sim.Tests/Persist/WorldStoreTests.cs`

**Interfaces:**
- Consumes: `WorldSnapshot`, `SimJson`, `WorldHasher` (Task 7); `EventLog`, `WorldEvent` (Task 3); `World`, `EventQueue`.
- Produces: `sealed class WorldStore : IDisposable { const int CurrentSchemaVersion = 1; static WorldStore Open(string path); int SchemaVersion; bool HasWorld; long CountEvents(); void Save(World, EventLog, EventQueue); (World World, EventLog Log, EventQueue Queue) Load() }`. Tables: `meta(key, value)`, `snapshot(id=1, json)`, `events(id, day, type, json)`, `event_causes(cause_id, effect_id, kind, weight)`, `event_actors(event_id, role, kind, entity_id)`. `Save` writes everything in one transaction and only inserts events newer than the stored maximum id. Test support: `TempFile` (`Path` property, `Dispose` deletes the file and its `-wal`/`-shm` siblings).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Support/TempFile.cs`:
```csharp
namespace FantasyWorld.Sim.Tests.Support;

public sealed class TempFile : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fw-test-{Guid.NewGuid():N}.fw.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
    }
}
```

`sim/FantasyWorld.Sim.Tests/Persist/WorldStoreTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
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
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~WorldStoreTests`
Expected: build FAILS with `CS0246: The type or namespace name 'WorldStore' could not be found`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Persist/WorldStore.cs`:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot + append-only event log.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
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

    public void Save(World world, EventLog log, EventQueue queue)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            foreach (var ev in log.Since(persistedMax)) InsertEvent(ev);

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

        return (snapshot.ToWorld(), log, snapshot.ToQueue());
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        using (var cmd = Command("PRAGMA journal_mode=WAL;")) cmd.ExecuteNonQuery();
        using (var cmd = Command(Schema)) cmd.ExecuteNonQuery();
        var version = GetMeta("schema_version");
        if (version is null)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
        else if (int.Parse(version, CultureInfo.InvariantCulture) > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"World file schema {version} is newer than this build supports ({CurrentSchemaVersion}).");
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

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~WorldStoreTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add SQLite world store with transactional saves

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Demo systems — harvest, shortage and revolt

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/Demo/HarvestSystem.cs`, `Systems/Demo/UnrestSystem.cs`, `Systems/DefaultSystems.cs`
- Test: `sim/FantasyWorld.Sim.Tests/Systems/DemoSystemsTests.cs`

**Interfaces:**
- Consumes: `ISimSystem`, `SimContext`, `Simulator`, `WorldFactory` (Task 6); `EventDraft`, `Cause` (Task 3); `HistoryQuery` (Task 4).
- Produces:
  - `HarvestSystem(int minDelta = -25, int maxDeltaExclusive = 21)`, name `"harvest"`, const `ShortageMarker = "grainShortage"`. Monthly: `grain` (default 100) += random delta; +40 in month 9; clamped to [0, 200]. Emits `GrainShortageBegan` (payload `grain`) when grain < 40 and no shortage is active; emits `GrainShortageEnded` (cause: the shortage) when grain ≥ 60 and a shortage is active.
  - `UnrestSystem`, name `"unrest"`, consts `RevoltMarker = "revolt"`, `RevoltEndKind = "revolt.end"`. Monthly: `unrest` (default 0) += 15 during a shortage else −5, floored at 0. At ≥ 50 with no active revolt, emits `RevoltBegan` (cause: the active shortage, if any) and schedules `revolt.end` 90 days later, which emits `RevoltEnded` (cause: the revolt), sets unrest to 20 and clears the marker.
  - `static class DefaultSystems { IReadOnlyList<ISimSystem> Create() }` → `[HarvestSystem, UnrestSystem]`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/DemoSystemsTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Systems.Demo;

namespace FantasyWorld.Sim.Tests.Systems;

public class DemoSystemsTests
{
    private static Simulator Make(double grain, double unrest, int minDelta, int maxDeltaExclusive)
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        world.Stats["grain"] = grain;
        world.Stats["unrest"] = unrest;
        return new Simulator(world, new EventLog(), new EventQueue(),
            new ISimSystem[] { new HarvestSystem(minDelta, maxDeltaExclusive), new UnrestSystem() },
            checkInvariants: true);
    }

    [Fact]
    public void Shortage_BeginsWhenGrainFallsBelow40()
    {
        var sim = Make(grain: 45, unrest: 0, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(30);

        Assert.Equal(35, sim.World.Stats["grain"]);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageBegan");
        Assert.Equal("35", shortage.Payload["grain"]);
        Assert.Equal(shortage.Id, sim.World.Markers[HarvestSystem.ShortageMarker]);
    }

    [Fact]
    public void Shortage_EndsWhenGrainRecovers_CitingTheShortage()
    {
        var sim = Make(grain: 40, unrest: 0, minDelta: -1, maxDeltaExclusive: 0);
        sim.AdvanceDays(30);
        var shortage = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageBegan");

        sim.World.Stats["grain"] = 100;
        sim.AdvanceDays(30);

        var ended = Assert.Single(sim.Log.All, e => e.Type == "GrainShortageEnded");
        Assert.Equal(shortage.Id, Assert.Single(ended.Causes).EventId);
        Assert.False(sim.World.Markers.ContainsKey(HarvestSystem.ShortageMarker));
    }

    [Fact]
    public void Revolt_IsCausedByTheShortage()
    {
        var sim = Make(grain: 45, unrest: 45, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(30);

        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        var root = Assert.Single(new HistoryQuery(sim.Log).RootCauses(revolt.Id));
        Assert.Equal("GrainShortageBegan", root.Type);
    }

    [Fact]
    public void Revolt_EndsNinetyDaysLater()
    {
        var sim = Make(grain: 45, unrest: 45, minDelta: -10, maxDeltaExclusive: -9);
        sim.AdvanceDays(120);

        var revolt = Assert.Single(sim.Log.All, e => e.Type == "RevoltBegan");
        var ended = Assert.Single(sim.Log.All, e => e.Type == "RevoltEnded");
        Assert.Equal(revolt.Date.Day + 90, ended.Date.Day);
        Assert.Equal(revolt.Id, Assert.Single(ended.Causes).EventId);
        Assert.Equal(20, sim.World.Stats["unrest"]);
        Assert.False(sim.World.Markers.ContainsKey(UnrestSystem.RevoltMarker));
    }

    [Fact]
    public void DefaultSystems_RunACenturyWithoutViolatingInvariants()
    {
        var sim = new Simulator(WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(36_000);
        Assert.Equal(101, sim.World.Date.Year);
        Assert.InRange(sim.World.Stats["grain"], 0, 200);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter FullyQualifiedName~DemoSystemsTests`
Expected: build FAILS with `CS0234: The type or namespace name 'Systems' does not exist`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/Demo/HarvestSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems.Demo;

/// <summary>Demo economy: a single grain store that drifts monthly and refills at harvest.</summary>
public sealed class HarvestSystem : ISimSystem
{
    public const string ShortageMarker = "grainShortage";

    private readonly int _minDelta;
    private readonly int _maxDeltaExclusive;

    public HarvestSystem(int minDelta = -25, int maxDeltaExclusive = 21)
    {
        _minDelta = minDelta;
        _maxDeltaExclusive = maxDeltaExclusive;
    }

    public string Name => "harvest";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var grain = world.Stats.GetValueOrDefault("grain", 100) + ctx.Rng.NextInt(_minDelta, _maxDeltaExclusive);
        if (ctx.Date.Month == 9) grain += 40;
        grain = Math.Clamp(grain, 0, 200);
        world.Stats["grain"] = grain;

        var active = world.Markers.TryGetValue(ShortageMarker, out var shortageId);
        if (!active && grain < 40)
        {
            var shortage = ctx.Emit(new EventDraft("GrainShortageBegan",
                Payload: new Dictionary<string, string> { ["grain"] = grain.ToString("0", CultureInfo.InvariantCulture) },
                Importance: 40,
                Tags: new[] { "Economy" }));
            world.Markers[ShortageMarker] = shortage.Id;
        }
        else if (active && grain >= 60)
        {
            ctx.Emit(new EventDraft("GrainShortageEnded",
                Causes: new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) },
                Importance: 20,
                Tags: new[] { "Economy" }));
            world.Markers.Remove(ShortageMarker);
        }
    }
}
```

`sim/FantasyWorld.Sim/Systems/Demo/UnrestSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems.Demo;

/// <summary>Demo politics: shortages raise unrest; high unrest starts a 90-day revolt.</summary>
public sealed class UnrestSystem : ISimSystem
{
    public const string RevoltMarker = "revolt";
    public const string RevoltEndKind = "revolt.end";

    public string Name => "unrest";

    public IEnumerable<string> HandledKinds => new[] { RevoltEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var inShortage = world.Markers.TryGetValue(HarvestSystem.ShortageMarker, out var shortageId);
        var unrest = Math.Max(0, world.Stats.GetValueOrDefault("unrest", 0) + (inShortage ? 15 : -5));
        world.Stats["unrest"] = unrest;

        if (unrest < 50 || world.Markers.ContainsKey(RevoltMarker)) return;

        var causes = inShortage
            ? new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) }
            : Array.Empty<Cause>();
        var revolt = ctx.Emit(new EventDraft("RevoltBegan", Causes: causes, Importance: 60, Tags: new[] { "Politics" }));
        world.Markers[RevoltMarker] = revolt.Id;
        ctx.Queue.Schedule(ctx.Date.AddDays(90), RevoltEndKind,
            new Dictionary<string, string> { ["revoltEventId"] = revolt.Id.ToString(CultureInfo.InvariantCulture) });
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var revoltId = long.Parse(action.Data["revoltEventId"], CultureInfo.InvariantCulture);
        ctx.Emit(new EventDraft("RevoltEnded",
            Causes: new[] { new Cause(revoltId, CauseKind.Trigger, 1.0) },
            Importance: 30,
            Tags: new[] { "Politics" }));
        ctx.World.Stats["unrest"] = 20;
        ctx.World.Markers.Remove(RevoltMarker);
    }
}
```

`sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Systems.Demo;

namespace FantasyWorld.Sim.Systems;

/// <summary>The fixed system order for monthly ticks. Demo systems are replaced in Plans 2–4.</summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[] { new HarvestSystem(), new UnrestSystem() };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter FullyQualifiedName~DemoSystemsTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add demo harvest and unrest systems proving causal chains

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: `fw` command-line tool

**Files:**
- Create: `sim/FantasyWorld.Cli/DurationParser.cs`, `sim/FantasyWorld.Cli/CliApp.cs`
- Modify: `sim/FantasyWorld.Cli/Program.cs` (replace template contents)
- Test: `sim/FantasyWorld.Sim.Tests/Cli/DurationParserTests.cs`, `sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs`

**Interfaces:**
- Consumes: `WorldStore` (Task 9), `Simulator`, `WorldFactory`, `CatchUp` (Tasks 6, 8), `DefaultSystems` (Task 10), `HistoryQuery`, `ChainNode`, `EventText` (Task 4).
- Produces: `static class DurationParser { long ParseDays(string) }` (`30d`, `4mo`, `7y`); `static class CliApp { int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock) }`. Exit codes: 0 ok, 1 runtime error (message prefixed `error: `), 2 usage. Commands: `new`, `advance`, `catchup`, `log`, `why`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Cli/DurationParserTests.cs`:
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
    public void RejectsBadInput(string text) =>
        Assert.Throws<FormatException>(() => DurationParser.ParseDays(text));
}
```

`sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

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
    public void New_CreatesWorldFile()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--seed", "42");
        Assert.Equal(0, code);
        Assert.Contains("seed 42", output);
        using var store = WorldStore.Open(file.Path);
        Assert.True(store.HasWorld);
        Assert.Equal(42UL, store.Load().World.Seed);
    }

    [Fact]
    public void New_RefusesToOverwrite()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "1");
        var (code, output) = Run(T0, "new", file.Path, "--seed", "2");
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void Advance_MovesTheWorldAndSaves()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3");
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
        Run(T0, "new", file.Path, "--seed", "3");
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
    public void Why_PrintsTheCausalChain()
    {
        using var file = new TempFile();
        var world = WorldFactory.CreateNew(3, T0);
        world.Stats["grain"] = 10;
        world.Stats["unrest"] = 45;
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());

        Run(T0, "advance", file.Path, "1mo");
        long revoltId;
        using (var store = WorldStore.Open(file.Path))
            revoltId = store.Load().Log.All.Single(e => e.Type == "RevoltBegan").Id;

        var (code, output) = Run(T0, "why", file.Path, revoltId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("RevoltBegan", lines[0]);
        Assert.StartsWith("  because #", lines[1]);
        Assert.Contains("GrainShortageBegan", lines[1]);
    }

    [Fact]
    public void Log_ListsEventsSinceId()
    {
        using var file = new TempFile();
        var world = WorldFactory.CreateNew(3, T0);
        world.Stats["grain"] = 10;
        world.Stats["unrest"] = 45;
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());
        Run(T0, "advance", file.Path, "1mo");

        var (code, output) = Run(T0, "log", file.Path, "--since", "1");
        Assert.Equal(0, code);
        Assert.DoesNotContain("#1 ", output);
        Assert.Contains("#2 ", output);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim --filter "FullyQualifiedName~DurationParserTests|FullyQualifiedName~CliAppTests"`
Expected: build FAILS with `CS0103: The name 'DurationParser' does not exist`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Cli/DurationParser.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Cli;

public static class DurationParser
{
    private static readonly Regex Pattern = new(@"^(\d+)(d|mo|y)$", RegexOptions.CultureInvariant);

    public static long ParseDays(string text)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
            throw new FormatException($"Bad duration '{text}'. Use e.g. 30d, 4mo or 7y.");
        var n = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "d" => n,
            "mo" => n * WorldDate.DaysPerMonth,
            _ => n * WorldDate.DaysPerYear,
        };
    }
}
```

`sim/FantasyWorld.Cli/CliApp.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Cli;

public static class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>]
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
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
                _ => UsageError(output),
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
                                       or KeyNotFoundException or IOException)
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
        var world = WorldFactory.CreateNew(seed, now);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, new EventLog(), new EventQueue());
        output.WriteLine($"Created world at {path} (seed {seed}).");
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
        PrintEvents(output, sim.Log.Since(lastId));
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
        PrintEvents(output, sim.Log.Since(lastId));
        return 0;
    }

    private static int Log(string path, string[] rest, TextWriter output)
    {
        var since = long.Parse(Option(rest, "--since") ?? "0", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        PrintEvents(output, Load(store).Log.Since(since));
        return 0;
    }

    private static int Why(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var eventId = long.Parse(rest[0], CultureInfo.InvariantCulture);
        var depth = int.Parse(Option(rest, "--depth") ?? "4", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var chain = new HistoryQuery(Load(store).Log).WhyChain(eventId, depth);
        PrintChain(output, chain, 0);
        return 0;
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

    private static void PrintEvents(TextWriter output, IEnumerable<WorldEvent> events)
    {
        foreach (var ev in events) output.WriteLine(EventText.Describe(ev));
    }

    private static void PrintChain(TextWriter output, ChainNode node, int indent)
    {
        var prefix = new string(' ', indent * 2) + (indent == 0 ? "" : "because ");
        output.WriteLine(prefix + EventText.Describe(node.Event));
        foreach (var cause in node.Causes) PrintChain(output, cause, indent + 1);
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

Replace `sim/FantasyWorld.Cli/Program.cs` entirely with:
```csharp
return FantasyWorld.Cli.CliApp.Run(args, Console.Out, () => DateTimeOffset.UtcNow);
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DurationParserTests|FullyQualifiedName~CliAppTests"`
Expected: `Passed!  - Failed: 0, Passed: 17`

- [x] **Step 5: Smoke-test the real binary**

```bash
dotnet run --project sim/FantasyWorld.Cli -- new worlds/demo.fw.db --seed 7
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/demo.fw.db 20y
```
Expected: `Created world at worlds/demo.fw.db (seed 7).`, then `Advanced 7200 days to Y21 M1 D1.` followed by event lines. Pick any `RevoltBegan` id from the output and run:
```bash
dotnet run --project sim/FantasyWorld.Cli -- why worlds/demo.fw.db <that-id>
```
Expected: the revolt line, then an indented `because #… GrainShortageBegan …` line (or no cause line if that revolt began without an active shortage). Then delete the folder: `rm -r worlds`.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(cli): add fw command-line tool for new/advance/catchup/log/why

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: Continuous integration and README

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: the whole `sim` solution.
- Produces: a CI check named `ci / test` on every push and pull request.

- [x] **Step 1: Write the workflow**

`.github/workflows/ci.yml`:
```yaml
name: ci

on:
  push:
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Test
        run: dotnet test sim --configuration Release
```

- [x] **Step 2: Update the README**

Replace `README.md` with:
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
dotnet run --project sim/FantasyWorld.Cli -- new worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- catchup worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- why worlds/aerenmoor.fw.db <event-id>
```
````

- [x] **Step 3: Run the full suite in Release**

Run: `dotnet test sim --configuration Release`
Expected: `Passed!  - Failed: 0, Passed: 93`

- [x] **Step 4: Commit and push**

```bash
git add .github README.md
git commit -m "ci: run sim tests on push; document layout and CLI

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
git push
```

Then run `gh run watch --exit-status` and expect the `ci` run to finish with `completed success`.

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §2 engine-agnostic library, CLI-first | 1, 11 |
| §7 calendar, 1 day = 1 year, catch-up, 30-year cap, carry-over, clock regression | 1, 8 |
| §7 day-granular loop, monthly tick in fixed order, scheduled queue (date, priority, seq) | 5, 6 |
| §7 determinism: per-system RNG streams, chunk invariance | 2, 6, 7 |
| §7 event model, max 3 causes, causes are earlier events, condition events | 3, 10 |
| §7 WhyChain / RootCauses / Consequences | 4 |
| §9 SQLite file per world, transactional saves, meta/snapshot/events/causes, schema version | 9 |
| §10 unit, determinism, invariants, causality tests, CI | 3–12 |

Deferred to later plans (see Roadmap): importance-ranked digest, `Life`/`History`/`Eras`, compaction, snapshot compression, rolling backups, `llm_calls`/`followed` tables, live-mode host loop, benchmarks, all domain systems and the LLM layer.
