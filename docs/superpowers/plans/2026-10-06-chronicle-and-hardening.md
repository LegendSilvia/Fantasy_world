# Chronicle & Hardening Implementation Plan (Plan 6 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the sim core as a game you come back to. A new world arrives with a century of history. Coming back after a night away gives a short, ranked digest of what happened, led by the people and realms you follow, and the world keeps moving in real time while you watch. Old minor history is compacted so the world can run for centuries. Snapshots are compressed and the last three sessions are backed up. Structures arrive: landmarks, labyrinths and ruins. Finally the spec's success criteria and performance budget are checked automatically.

**Architecture:** A new namespace, `FantasyWorld.Sim.Chronicle`, holds importance scoring, the digest, the compactor and the `ChronicleSystem` that runs it. `Engine` gains `LiveHost`, `WorldGen` gains `Prehistory`, and `Persist` gains compression and online backup. A new `StructureSystem` runs near the end of the tick order, and compaction runs last. The CLI grows `digest`, `follow`/`unfollow`, `live` and `structures`, and `catchup` ends with the digest. A BenchmarkDotNet project measures the per-year cost. Nothing here changes how the world evolves except Task 5's in-sim compaction, Task 7's structures and Task 8's extra war causes; determinism, chunk invariance and the LLM journal are untouched.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite 8.0.10, System.Text.Json, Anthropic 12.53.0 (unchanged); new: `BenchmarkDotNet` 0.14.0 in `FantasyWorld.Bench` only.

**Spec:** `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` (§5 world generation and structures, §7 modes, importance, digest, compaction and performance, §9 persistence, §10 testing, §11 success criteria). **Builds on:** Plans 1–5 (merged).

## Global Constraints

Everything in Plans 1–5's Global Constraints still applies. In addition:

- **Compaction never breaks explanation:** an event is forgotten only if it is minor, older than 50 years, cited by no retained event and referred to by nothing in the world's state. `fw why` on anything retained works exactly as before (checked in Task 8).
- **The reader's state is not the world's state:** followed entities live in the `followed` table; `LastSeenEventId` only moves when the reader is shown events.
- **Live and catch-up share one path:** live mode is repeated catch-up steps; the world never rewinds.
- **Backups before change:** any command that advances a world first rotates the backups, and a session never saves over another session's changes.
- `sim/FantasyWorld.sln` and the `.csproj` files made by `dotnet new` use CRLF line endings; keep them when replacing those files.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Importance | Engine importance + up to 20 for fame (renown, lords, independent/high-ranked realms) + 30 for anything followed | Spec §7; following is the player's only lever, so it must win |
| Digest size | 8 headlines; absences over a year are told by season (the four seasons with the most important events, 2 each) | Spec §11: 3–8 headlines after 8 hours; longer absences need structure |
| Prehistory | 100 years for medium and large worlds, none for tiny; rules only | Spec §5 "~100-year prehistory"; LLM founding myths would cost hundreds of calls per new world |
| Live pace | 1 world day per 4 real minutes; events of importance ≥ 40 are shown as they happen | Spec §7 (1 real day = 1 world year) |
| Compaction | Importance < 30 and older than 50 years; one summary per decade, made once by an in-sim system at the start of the decade that makes it fifty | Spec §7; inside the simulation it is deterministic (a CLI-side compaction would make the world depend on how often you play), and the log is append-only by date, so summaries cannot be back-dated |
| References | Found by walking the snapshot JSON (`…EventId` fields, condition/marker values, queued `event` data) | No hand-kept list to fall out of date as models grow |
| Backups | Three rolling copies beside the world file, via SQLite online backup | Spec §9 "rolling backups of the last 3 sessions" |
| Structures | Landmarks (wealthy lords, 6–15 years), labyrinths (ley ≥ 70, danger ≥ 35, one per 15 regions, each hides a relic), ruins of abandoned settlements | Spec §5 minimal hooks; depth comes with sub-project 3 |
| War causes | Casus belli as motive, plus the latest rivalry and the ruler's accession as enablers | Spec §11 criterion 3: chains of linked causes reaching roots |

**Verification:** 200-year medium soaks are covered by Plan 4's checks; here a medium world after its century of prehistory is checked against the success criteria (Task 8), compaction is shown deterministic across saves (Task 5), and the benchmark measures about 50–90 ms per world year on medium and large worlds, far inside the 2 s budget. An independent review of the prototype found four defects (compaction run from the CLI made worlds depend on how often they were caught up and repeated decade summaries; Ctrl+C in live mode waited up to four minutes; two sessions on one file silently lost events; benchmark output was committed) and smaller ones (region memories missed by the reference walk, a missing index); all are fixed in the code below and covered by tests. The code in this plan was replayed task by task in this order: each task's tests fail first and pass after the implementation, with the counts given.

## Roadmap after the sim core

| Sub-project | Delivers |
|---|---|
| 2 — Chronicle & why UI data | view models for chronicle pages, era timelines and why-chain graphs, ready for a client |
| 3 — Myth, magic & structures | monsters, religions, magic systems, landmark projects, labyrinth internals |
| 4 — Godot 3D diorama client | the four designed screens over `LiveHost`, `HistoryQuery` and the digest |
| 5 — Narration polish | prompt tuning, voice, and evals of decisions and narration with live Claude |

## File Structure

```
sim/
  FantasyWorld.sln                                    + FantasyWorld.Bench                 (Task 9, modify)
  FantasyWorld.Sim/
    Chronicle/Importance.cs                                                                 (Task 1)
    Chronicle/Digest.cs; Narration/TemplateNarrator.cs, LlmNarrator.cs                      (Task 2; Tasks 2, 7 modify)
    WorldGen/Prehistory.cs                                                                  (Task 3)
    Engine/LiveHost.cs                                                                      (Task 4)
    Chronicle/Compactor.cs, ChronicleSystem.cs; Events/EventLog.cs (Forget); Engine/Simulator.cs   (Task 5)
    Persist/Packed.cs; Persist/WorldStore.cs     v4 followed, compression, backup, forget   (Tasks 1, 5, 6)
    Model/Structure.cs, World.cs; Persist/WorldSnapshot.cs (v5); Engine/Invariants*.cs;
      Systems/StructureSystem.cs, DefaultSystems.cs; Query/Names.cs                        (Task 7)
    Systems/DiplomacySystem.cs, RealmSystem.cs, Fates.cs                                    (Task 8, modify)
  FantasyWorld.Cli/CliApp.cs, CliApp.Chronicle.cs, Backups.cs                               (Tasks 1–7)
  FantasyWorld.Bench/FantasyWorld.Bench.csproj, Program.cs, WorldYearBenchmark.cs           (Task 9)
  FantasyWorld.Sim.Tests/
    Chronicle/{Importance,Digest,Compactor}Tests.cs; WorldGen/PrehistoryTests.cs;
    Engine/{LiveHost,SuccessCriteria,Performance}Tests.cs; Persist/StorageTests.cs; Systems/StructureSystemTests.cs
.github/workflows/ci.yml                              + bench job                           (Task 9, modify)
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: Importance and following

Spec §7 "Importance and the digest": how much an event matters to the reader now is the importance the engine gave it, raised by the fame of those involved (renowned people, lords, independent and high-ranked realms) and, most of all, by entities the player follows. Following is the player's only lever, so it is stored in the world file (spec §9 `followed`), not in the world's state.

**Files:**
- Create: `sim/FantasyWorld.Cli/CliApp.Chronicle.cs`, `sim/FantasyWorld.Sim/Chronicle/Importance.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Persist/WorldStore.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Chronicle/ImportanceTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Llm/LlmPersistenceTests.cs`, `sim/FantasyWorld.Sim.Tests/Persist/WorldStoreTests.cs`

**Interfaces:**
- Consumes: `WorldEvent`, `World`, `WorldStore` (Plans 1–5).
- Produces (namespace `FantasyWorld.Sim.Chronicle`): `static class Importance { FollowedBonus = 30; Score(ev, world, followed); ParseRef("char#12") }`; `WorldStore` schema 4 with table `followed`, `LoadFollowed()`, `Follow(entity, follow = true)`; CLI `follow <entity>`, `unfollow <entity>` (`CliApp.Chronicle.cs`).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Chronicle/ImportanceTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class ImportanceTests
{
    [Fact]
    public void FameAndFollowing_RaiseAnEventsImportance()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var nobody = TestPeople.Add(world, 1, 30);
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 80;
        EventDraft Draft(Character c) => new("DuelFought", Actors: new Dictionary<string, EntityRef> { ["challenger"] = c.Ref }, Importance: 20);
        var plain = log.Append(world.Date, Draft(nobody));
        var famous = log.Append(world.Date, Draft(hero));
        var none = new HashSet<EntityRef>();

        Assert.Equal(20, Importance.Score(plain, world, none));
        Assert.Equal(36, Importance.Score(famous, world, none));
        Assert.Equal(50, Importance.Score(plain, world, new HashSet<EntityRef> { nobody.Ref }));
        var realmEvent = log.Append(world.Date, new EventDraft("RealmFell", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref }, Importance: 95));
        Assert.Equal(100, Importance.Score(realmEvent, world, none));
    }

    [Fact]
    public void EntityRefs_ParseAsPrinted()
    {
        Assert.Equal(new EntityRef("char", 12), Importance.ParseRef("char#12"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("char12"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("#3"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("char#-3"));
    }

    [Fact]
    public void FollowingIsStored_AndChecked()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, CliApp.Run(new[] { "follow", file.Path, "region#2" }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("Following ", output.ToString());
        using (var store = WorldStore.Open(file.Path)) Assert.Contains(new EntityRef("region", 2), store.LoadFollowed());

        CliApp.Run(new[] { "unfollow", file.Path, "region#2" }, output, () => DateTimeOffset.UnixEpoch);
        using (var store = WorldStore.Open(file.Path)) Assert.Empty(store.LoadFollowed());

        var error = new StringWriter();
        Assert.Equal(1, CliApp.Run(new[] { "follow", file.Path, "char#99999" }, error, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("error: No char#99999", error.ToString());
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Llm/LlmPersistenceTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Llm;

public class LlmPersistenceTests
{
    private static LlmCall Call(string hash, decimal cost = 0.0123m) =>
        new(hash, ModelTier.Arbitrate, 400, "anthropic", "claude-opus-5-5", "{\"outcome\":\"yield\"}",
            1200, 80, 1000, 0, cost, new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), "upset");

    [Fact]
    public void Journal_IsSavedWithTheMonth_AndLoadsBack()
    {
        using var file = new TempFile();
        var sim = new Simulator(WorldFactory.CreateNew(5, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() });
        var journal = new LlmJournal();
        journal.Add(Call("a"));
        journal.Add(Call("b"));
        using (var store = WorldStore.Open(file.Path))
        {
            Assert.Equal(WorldStore.CurrentSchemaVersion, store.SchemaVersion);
            store.Save(sim.World, sim.Log, sim.Queue, journal);
            store.Save(sim.World, sim.Log, sim.Queue, journal);
        }
        Assert.Empty(journal.Pending);

        using var reopened = WorldStore.Open(file.Path);
        var loaded = reopened.LoadJournal();
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded.TryGet("a", out var a));
        Assert.Equal(Call("a"), a);
        Assert.Empty(loaded.Pending);
    }

    [Fact]
    public void Settings_RoundTrip_AndDefaultToOffline()
    {
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        Assert.False(store.LoadLlmSettings().Enabled);
        var settings = new LlmSettings { Provider = "anthropic", StoryBeatsPerDay = 12, MonthlySpendCapUsd = 2.5m };
        settings.Models["narrate"] = "claude-sonnet-5-5";
        store.SaveLlmSettings(settings);
        var loaded = store.LoadLlmSettings();
        Assert.Equal("anthropic", loaded.Provider);
        Assert.Equal(12, loaded.StoryBeatsPerDay);
        Assert.Equal(2.5m, loaded.MonthlySpendCapUsd);
        Assert.Equal("claude-sonnet-5-5", loaded.ModelFor(ModelTier.Narrate));
        Assert.Equal(LlmSettings.DefaultModel, loaded.ModelFor(ModelTier.Decide));
    }
}
```

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
            cmd.CommandText = "DROP TABLE geography; DROP TABLE llm_calls; DROP TABLE followed; UPDATE meta SET value = '1' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }

        using var upgraded = WorldStore.Open(file.Path);
        Assert.Equal(WorldStore.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.False(upgraded.HasGeography);
        Assert.Equal(0, upgraded.LoadJournal().Count);
        Assert.Equal(Hash(sim), WorldHasher.Hash(upgraded.Load().World, sim.Log, sim.Queue));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Chronicle' does not exist in the namespace 'FantasyWorld.Sim' (are you missing an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Cli/CliApp.Chronicle.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large]
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/Chronicle/Importance.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// How much an event matters to the reader now (spec §7 "Importance and the digest"): the importance the
/// engine gave it, raised by the fame of those involved (renowned people, independent and high-ranked realms)
/// and above all by entities the player follows; 0–100.
/// </summary>
public static class Importance
{
    public const int FollowedBonus = 30;

    public static int Score(WorldEvent ev, World world, IReadOnlySet<EntityRef> followed)
    {
        double score = ev.Importance;
        var fame = 0.0;
        foreach (var actor in ev.Actors.Values)
        {
            if (actor.Kind == Character.Kind && world.Characters.TryGetValue(actor.Id, out var c))
                fame = Math.Max(fame, c.Renown / 5 + (c.Role == Role.Lord ? 5 : 0));
            else if (actor.Kind == Realm.Kind && world.Realms.TryGetValue(actor.Id, out var realm))
                fame = Math.Max(fame, (realm.LiegeId == 0 ? 10 : 0) + (int)realm.Rank * 3);
        }
        score += Math.Min(20, fame);
        if (followed.Count > 0 && (ev.Actors.Values.Any(followed.Contains) || ev.Place is { } place && followed.Contains(place)))
            score += FollowedBonus;
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    /// <summary>Parses "char#12", as entities are printed.</summary>
    public static EntityRef ParseRef(string text)
    {
        var parts = text.Split('#');
        if (parts.Length != 2 || parts[0].Length == 0 || !long.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
            throw new FormatException($"'{text}' is not an entity like char#12 or realm#3.");
        return new EntityRef(parts[0], id);
    }
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldStore.cs` entirely with:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot, static geography (written once), the append-only event log and the LLM journal.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 4;

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
        CREATE TABLE IF NOT EXISTS llm_calls (
            hash TEXT PRIMARY KEY, tier TEXT NOT NULL, day INTEGER NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL,
            response TEXT NOT NULL, input_tokens INTEGER NOT NULL, output_tokens INTEGER NOT NULL,
            cache_read_tokens INTEGER NOT NULL, cache_write_tokens INTEGER NOT NULL, cost_usd TEXT NOT NULL,
            real_utc TEXT NOT NULL, note TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS followed (kind TEXT NOT NULL, entity_id INTEGER NOT NULL, PRIMARY KEY (kind, entity_id));
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

    /// <summary>Saves the month in one transaction: new events, the snapshot and new LLM answers (spec §9).</summary>
    public void Save(World world, EventLog log, EventQueue queue, LlmJournal? journal = null)
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
            foreach (var call in journal?.Pending ?? Array.Empty<LlmCall>()) InsertCall(call);

            _transaction.Commit();
            journal?.MarkSaved();
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

    /// <summary>Saves new LLM answers alone (narration, which changes nothing in the world).</summary>
    public void SaveJournal(LlmJournal journal)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            foreach (var call in journal.Pending) InsertCall(call);
            _transaction.Commit();
            journal.MarkSaved();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public LlmJournal LoadJournal()
    {
        var journal = new LlmJournal();
        using var cmd = Command("SELECT hash, tier, day, provider, model, response, input_tokens, output_tokens, " +
                                "cache_read_tokens, cache_write_tokens, cost_usd, real_utc, note FROM llm_calls ORDER BY hash");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            journal.Restore(new LlmCall(reader.GetString(0), Enum.Parse<ModelTier>(reader.GetString(1)), reader.GetInt64(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), decimal.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture), reader.GetString(12)));
        return journal;
    }

    public LlmSettings LoadLlmSettings() =>
        GetMeta("llm_settings") is { } json ? JsonSerializer.Deserialize<LlmSettings>(json, SimJson.Options)! : new LlmSettings();

    public void SaveLlmSettings(LlmSettings settings) => SetMeta("llm_settings", JsonSerializer.Serialize(settings, SimJson.Options));

    /// <summary>Entities the player follows (spec §9): their events rank higher in the digest.</summary>
    public SortedSet<EntityRef> LoadFollowed()
    {
        var followed = new SortedSet<EntityRef>(Comparer<EntityRef>.Create((a, b) =>
            string.CompareOrdinal(a.Kind, b.Kind) is var k and not 0 ? k : a.Id.CompareTo(b.Id)));
        using var cmd = Command("SELECT kind, entity_id FROM followed");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) followed.Add(new EntityRef(reader.GetString(0), reader.GetInt64(1)));
        return followed;
    }

    public void Follow(EntityRef entity, bool follow = true)
    {
        using var cmd = Command(follow
            ? "INSERT OR IGNORE INTO followed(kind, entity_id) VALUES ($k, $i)"
            : "DELETE FROM followed WHERE kind = $k AND entity_id = $i");
        cmd.Parameters.AddWithValue("$k", entity.Kind);
        cmd.Parameters.AddWithValue("$i", entity.Id);
        cmd.ExecuteNonQuery();
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
        // 1 → 2 adds the geography table, 2 → 3 llm_calls and 3 → 4 followed; the schema script above creates them.
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

    private void InsertCall(LlmCall call)
    {
        using var cmd = Command("INSERT OR IGNORE INTO llm_calls VALUES ($h, $t, $d, $p, $m, $r, $i, $o, $cr, $cw, $c, $u, $n)");
        cmd.Parameters.AddWithValue("$h", call.Hash);
        cmd.Parameters.AddWithValue("$t", call.Tier.ToString());
        cmd.Parameters.AddWithValue("$d", call.Day);
        cmd.Parameters.AddWithValue("$p", call.Provider);
        cmd.Parameters.AddWithValue("$m", call.Model);
        cmd.Parameters.AddWithValue("$r", call.Json);
        cmd.Parameters.AddWithValue("$i", call.InputTokens);
        cmd.Parameters.AddWithValue("$o", call.OutputTokens);
        cmd.Parameters.AddWithValue("$cr", call.CacheReadTokens);
        cmd.Parameters.AddWithValue("$cw", call.CacheWriteTokens);
        cmd.Parameters.AddWithValue("$c", call.CostUsd.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", call.RealUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", call.Note);
        cmd.ExecuteNonQuery();
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

Run: `dotnet test sim --filter "FullyQualifiedName~ImportanceTests|FullyQualifiedName~LlmPersistenceTests|FullyQualifiedName~WorldStoreTests"`
Expected: `Passed!  - Failed: 0, Passed: 11`. Then `dotnet test sim` → `Passed: 432` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(chronicle): score event importance and let the player follow entities

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The morning digest

Spec §11 criterion 2: come back after a night away and read 3–8 headlines. The digest takes events since the reader last looked, ranks them by importance and keeps the best; an absence of more than a year is told by season, keeping the seasons with the most important events. Both narrators tell it (templates offline, the LLM's narration tier otherwise, cached in the journal). `fw catchup` now ends with the digest instead of every event, and marks what it showed as seen; `fw digest` re-reads it or any span.

**Files:**
- Create: `sim/FantasyWorld.Sim/Chronicle/Digest.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.Chronicle.cs`, `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Narration/LlmNarrator.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Chronicle/DigestTests.cs`

**Interfaces:**
- Consumes: Task 1; `INarrator`, `TemplateNarrator`, `LlmNarrator`, `World.LastSeenEventId` (Plans 1, 5).
- Produces: `record DigestEntry(Event, Score)`, `record DigestGroup(Label, Entries)`, `record DigestView(From, To, EventCount, Groups) { Entries }`; `static class Digest { DefaultTake = 8; Build(log, world, sinceEventId, followed, take); SeasonLabel(date) }`; `INarrator.Digest(DigestView)`; CLI `digest [--since <event-id>] [--take <n>]`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Chronicle/DigestTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class DigestTests
{
    private static readonly HashSet<EntityRef> None = new();

    private static EventLog Log(params (long Day, int Importance)[] events)
    {
        var log = new EventLog();
        foreach (var (day, importance) in events)
            log.Append(new WorldDate(day), new EventDraft("Rumour", Importance: importance));
        return log;
    }

    [Fact]
    public void AShortAbsence_ShowsTheMostImportantEvents_InOrder()
    {
        var world = TestPeople.World();
        var log = Log((1, 10), (2, 90), (3, 50), (4, 70), (5, 5));
        var digest = Digest.Build(log, world, 0, None, take: 3);
        Assert.Equal(5, digest.EventCount);
        var group = Assert.Single(digest.Groups);
        Assert.Equal("", group.Label);
        Assert.Equal(new long[] { 2, 3, 4 }, group.Entries.Select(e => e.Event.Id));
        Assert.Equal(new long[] { 4, 5 }, Digest.Build(log, world, 3, None, take: 3).Entries.Select(e => e.Event.Id));
    }

    [Fact]
    public void FollowedEntities_LeadTheDigest()
    {
        var world = TestPeople.World();
        var log = Log((1, 60), (2, 60));
        var followed = world.Regions[1].Ref;
        log.Append(new WorldDate(3), new EventDraft("Rumour", Place: followed, Importance: 35));
        Assert.Equal(new long[] { 3 }, Digest.Build(log, world, 0, new HashSet<EntityRef> { followed }, take: 1).Entries.Select(e => e.Event.Id));
    }

    [Fact]
    public void ALongAbsence_IsToldBySeason()
    {
        var world = TestPeople.World();
        var log = Log((10, 20), (100, 80), (200, 30), (400, 90), (700, 60));
        var digest = Digest.Build(log, world, 0, None, take: 8);
        Assert.All(digest.Groups, g => Assert.Contains(" of Year ", g.Label));
        // Five seasons saw events; the four with the most important ones are kept, in calendar order.
        Assert.Equal(new[] { "Summer of Year 1", "Autumn of Year 1", "Spring of Year 2", "Winter of Year 2" }, digest.Groups.Select(g => g.Label));
        Assert.Equal(digest.Groups.Select(g => g.Entries[0].Event.Id).OrderBy(id => id), digest.Groups.Select(g => g.Entries[0].Event.Id));
    }

    [Fact]
    public void TheDigest_IsNarrated_ByTemplateOrLlm()
    {
        var world = TestPeople.World();
        var log = Log((1, 50));
        var digest = Digest.Build(log, world, 0, None);
        Assert.StartsWith("From Y1 M1 D2 to Y1 M1 D2, 1 events were recorded. The chronicle remembers:\n- In year 1, ", new TemplateNarrator(world).Digest(digest));
        Assert.Equal("Nothing of note has happened since you last looked.", new TemplateNarrator(world).Digest(Digest.Build(log, world, 1, None)));

        var fake = new FakeLlmProvider(_ => "{\"text\":\"While you slept, rumours spread.\"}");
        var story = new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal()));
        Assert.Equal("While you slept, rumours spread.", new LlmNarrator(story).Digest(digest));
        Assert.Contains("morning digest", fake.Requests.Single().Prompt);
    }

    [Fact]
    public void Catchup_EndsWithTheDigest_AndMarksItSeen()
    {
        using var file = new TempFile();
        var t0 = new DateTimeOffset(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => t0);
        output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "catchup", file.Path }, output, () => t0.AddDays(3)));
        Assert.Contains("The chronicle remembers:", output.ToString());

        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path }, output, () => t0.AddDays(3));
        Assert.Contains("Nothing of note has happened since you last looked.", output.ToString());
        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path, "--since", "0", "--take", "2" }, output, () => t0.AddDays(3));
        Assert.Equal(2, output.ToString().Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Digest' does not exist in the current context`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Cli/CliApp.Chronicle.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int DigestCommand(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var since = Option(rest, "--since") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : (long?)null;
        var take = int.Parse(Option(rest, "--take") ?? Digest.DefaultTake.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (take < 1) throw new FormatException("--take must be at least 1.");
        output.WriteLine(TellDigest(store, sim, gateway, since, take));
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        return 0;
    }

    /// <summary>
    /// Narrates the digest since the reader last looked (or since an event) and marks everything as seen; the caller
    /// saves. Explicit --since leaves the reader's place unchanged.
    /// </summary>
    private static string TellDigest(WorldStore store, Simulator sim, LlmGateway gateway, long? sinceEventId, int take = Digest.DefaultTake)
    {
        var digest = Digest.Build(sim.Log, sim.World, sinceEventId ?? sim.World.LastSeenEventId, store.LoadFollowed(), take);
        if (sinceEventId is null) sim.World.LastSeenEventId = sim.Log.NextId - 1;
        return new LlmNarrator(new StoryContext(sim.World, sim.Log, gateway)).Digest(digest);
    }

    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large]
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed, then the digest
          fw digest <world-file> [--since <event-id>] [--take <n>]   headlines since you last looked
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        output.WriteLine(digest);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/Chronicle/Digest.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Chronicle;

public sealed record DigestEntry(WorldEvent Event, int Score);

/// <summary>A labelled stretch of the digest ("Spring of Year 12"), or the whole span when it is short.</summary>
public sealed record DigestGroup(string Label, IReadOnlyList<DigestEntry> Entries);

public sealed record DigestView(WorldDate From, WorldDate To, int EventCount, IReadOnlyList<DigestGroup> Groups)
{
    public IEnumerable<DigestEntry> Entries => Groups.SelectMany(g => g.Entries);
}

/// <summary>
/// The morning digest (spec §7, §11 criterion 2): the most important events since the reader last looked, ranked
/// by <see cref="Importance"/>, and grouped by season when the absence spans more than a year.
/// </summary>
public static class Digest
{
    public const int DefaultTake = 8;

    public static DigestView Build(EventLog log, World world, long sinceEventId, IReadOnlySet<EntityRef> followed, int take = DefaultTake)
    {
        var events = log.Since(sinceEventId).ToList();
        if (events.Count == 0) return new DigestView(world.Date, world.Date, 0, Array.Empty<DigestGroup>());
        var from = events[0].Date;
        var to = events[^1].Date;
        var spansYears = to.Day - from.Day > WorldDate.DaysPerYear;
        var perGroup = spansYears ? Math.Max(1, take / 4) : take;

        var groups = events
            .Select(e => new DigestEntry(e, Importance.Score(e, world, followed)))
            .GroupBy(e => spansYears ? SeasonLabel(e.Event.Date) : "")
            .Select(g => (Label: g.Key, First: g.Min(e => e.Event.Id), Best: g.OrderByDescending(e => e.Score).ThenBy(e => e.Event.Id).Take(perGroup).ToList()))
            .ToList();
        if (spansYears)
        {
            // Keep the seasons that hold the most important events, in calendar order.
            var keep = groups.OrderByDescending(g => g.Best[0].Score).ThenBy(g => g.First).Take(Math.Max(1, take / perGroup))
                .Select(g => g.Label).ToHashSet(StringComparer.Ordinal);
            groups = groups.Where(g => keep.Contains(g.Label)).ToList();
        }
        return new DigestView(from, to, events.Count, groups
            .OrderBy(g => g.First)
            .Select(g => new DigestGroup(g.Label, g.Best.OrderBy(e => e.Event.Id).ToList()))
            .ToList());
    }

    public static string SeasonLabel(WorldDate date) => $"{date.Season} of Year {date.Year}";
}
```

Replace `sim/FantasyWorld.Sim/Narration/LlmNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// Narration by LLM (spec §8.7), on its own budget. The prompt carries the template prose plus the facts, motives,
/// accounts and lore stored on the events; answers are cached in the journal by event, so reading the same
/// passage twice costs nothing. Without an answer, the template narration is returned.
/// </summary>
public sealed class LlmNarrator(StoryContext story) : INarrator
{
    public const int MaxLength = 2000;

    private readonly TemplateNarrator _templates = new(story.World);

    public string Narrate(WorldEvent ev) =>
        Ask($"narrate|event|{ev.Id}", ev.Date.Day, Facts(ev), "Retell this event in two or three sentences.") ?? _templates.Narrate(ev);

    public string NarrateChain(ChainNode chain)
    {
        var facts = new StringBuilder();
        var depth = 0;
        void Walk(ChainNode node, int level)
        {
            depth = Math.Max(depth, level);
            facts.AppendLine($"{new string(' ', level * 2)}{(level == 0 ? "" : "caused by: ")}{Facts(node.Event)}");
            foreach (var cause in node.Causes) Walk(cause, level + 1);
        }
        Walk(chain, 0);
        return Ask($"narrate|chain|{chain.Event.Id}|{depth}", chain.Event.Date.Day, facts.ToString(),
                   "Tell how this came to pass, from the earliest cause to the event itself, in one paragraph of at most six sentences.")
               ?? _templates.NarrateChain(chain);
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return _templates.Digest(digest);
        var facts = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events; the most important:\n");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) facts.AppendLine($"{group.Label}:");
            foreach (var entry in group.Entries) facts.AppendLine($"- {Facts(entry.Event)}");
        }
        var ids = string.Join(",", digest.Entries.Select(e => e.Event.Id));
        return Ask($"narrate|digest|{ids}", digest.To.Day, facts.ToString(),
                   "Write the morning digest for a reader returning to this world: what happened while they were away, most important first, in at most three short paragraphs.")
               ?? _templates.Digest(digest);
    }

    private string Facts(WorldEvent ev)
    {
        var line = new StringBuilder(PromptText.Data(_templates.Narrate(ev), 600));
        if (ev.Payload.TryGetValue("lore", out var lore)) line.Append($" [lore: {PromptText.Data(lore, 400)}]");
        return line.ToString();
    }

    private string? Ask(string key, long day, string facts, string task)
    {
        if (!story.Gateway.MayAnswer(ModelTier.Narrate, key)) return null;
        var prompt = PromptText.Section("brief", facts) + $"\n\n{task} Write as the world's chronicler; use only the facts given.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { text = new { type = "string" } },
            required = new[] { "text" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Narrate, key, story.Bible, prompt, schema, MaxTokens: 1500);
        var json = story.Gateway.Ask(request, day, answer => Text(answer) is not null);
        return json is null ? null : Text(json);
    }

    private static string? Text(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("text", out var t)
                || t.ValueKind != JsonValueKind.String) return null;
            var text = t.GetString()!.Trim();
            return text.Length is > 0 and <= MaxLength ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DigestTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 437` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(chronicle): add the morning digest after catch-up

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Prehistory

Spec §5 and §11 criterion 1: a new world runs about a century before the player arrives, so there are already grudges, ruins, wars and artifacts with histories. The rules engine runs it (no LLM spend); the real-time clock is untouched so catch-up does not count those years, and nothing is marked seen, so the first digest tells the story so far. Tiny worlds (tests, quick demos) default to none.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/Prehistory.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/PrehistoryTests.cs`

**Interfaces:**
- Consumes: `WorldGenerator`, `Simulator`, `DefaultSystems` (Plans 1–5).
- Produces: `static class Prehistory { DefaultYears = 100; DefaultYearsFor(size); Simulator Run(world, years, progress?, checkInvariants?) }`; CLI `new … [--prehistory <years>]`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/PrehistoryTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class PrehistoryTests
{
    [Fact]
    public void Prehistory_GivesANewWorldAPast_WithoutTouchingTheRealClock()
    {
        var now = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
        var world = WorldGenerator.Generate(new WorldGenOptions(9, WorldSize.Tiny), now);
        var sim = Prehistory.Run(world, 15, checkInvariants: true);
        Assert.Equal(16, world.Date.Year);
        Assert.Contains(sim.Log.All, e => e.Type == "CharacterBorn");
        Assert.Contains(sim.Log.All, e => e.Type == "CharacterDied");
        Assert.Equal(now, world.LastRealUtc);
        Assert.Equal(0, world.LastSeenEventId);
        Assert.Throws<ArgumentOutOfRangeException>(() => Prehistory.Run(world, -1));
    }

    [Fact]
    public void New_RunsPrehistory_AndTheFirstDigestTellsTheStorySoFar()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        var t0 = DateTimeOffset.UnixEpoch;
        Assert.Equal(0, CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny", "--prehistory", "10" }, output, () => t0));
        Assert.Contains("10 years of prehistory", output.ToString());
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, log, _) = store.Load();
            Assert.Equal(11, world.Date.Year);
            Assert.NotEqual(0, log.Count);
        }
        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path }, output, () => t0);
        Assert.Contains("The chronicle remembers:", output.ToString());
        Assert.Equal(1, CliApp.Run(new[] { "new", file.Path + ".x", "--prehistory", "-5" }, new StringWriter(), () => t0));
        Assert.Equal(0, Prehistory.DefaultYearsFor(WorldSize.Tiny));
        Assert.Equal(Prehistory.DefaultYears, Prehistory.DefaultYearsFor(WorldSize.Medium));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Prehistory' does not exist in the current context`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large] [--prehistory <years>]   (default 100; tiny 0)
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed, then the digest
          fw digest <world-file> [--since <event-id>] [--take <n>]   headlines since you last looked
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

        var years = int.Parse(Option(rest, "--prehistory") ?? Prehistory.DefaultYearsFor(size).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (years is < 0 or > 1000) throw new FormatException("--prehistory must be between 0 and 1000 years.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        var sim = Prehistory.Run(world, years);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, sim.Log, sim.Queue);
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        if (years > 0)
            output.WriteLine($"{years} years of prehistory: {sim.Log.Count} events; it is now {world.Date}. Run `fw digest` for the story so far.");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        output.WriteLine(digest);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/WorldGen/Prehistory.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Spec §5 "World generation": a new world runs about a century before the player arrives, so they find existing
/// history, grudges, ruins and artifacts. The rules engine runs it (no LLM spend); the real-time clock is left
/// alone, so catch-up does not count those years, and nothing is marked seen, so the first digest tells the story
/// so far.
/// </summary>
public static class Prehistory
{
    public const int DefaultYears = 100;

    public static int DefaultYearsFor(WorldSize size) => size == WorldSize.Tiny ? 0 : DefaultYears;

    public static Simulator Run(World world, int years, IProgress<WorldDate>? progress = null, bool checkInvariants = false)
    {
        if (years < 0) throw new ArgumentOutOfRangeException(nameof(years), "Prehistory cannot be negative.");
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);
        sim.AdvanceDays((long)years * WorldDate.DaysPerYear, progress);
        return sim;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PrehistoryTests"`
Expected: `Passed!  - Failed: 0, Passed: 2`. Then `dotnet test sim` → `Passed: 439` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): give new worlds a century of prehistory

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Live mode

Spec §7 "Modes": while the game is open the world advances one day per four real minutes, through the same tick function as catch-up. Each step simulates whatever real time is owed (a laptop waking from sleep catches up), and a clock that jumps backwards makes the world wait rather than rewind. The loop is engine-agnostic: the CLI drives it with the real clock (Ctrl+C stops it), the Godot client will drive `Step` from its frame loop, and tests use a fake clock. Live mode saves after each world day and shows events important enough to pin; Ctrl+C stops it at once (the wait between days is cancellable).

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/LiveHost.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.Chronicle.cs`, `sim/FantasyWorld.Cli/CliApp.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/LiveHostTests.cs`

**Interfaces:**
- Consumes: `CatchUp`, `TimeScale` (Plan 1); Tasks 1–2.
- Produces: `record LiveTick(Days, Events)`; `sealed class LiveHost(Simulator) { NextDayAt; Step(now); Run(clock, sleep, until, onTick, ct) }`; CLI `live [--minutes <n>]` (`CliApp.LivePinImportance = 40`).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/LiveHostTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class LiveHostTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static LiveHost Host() =>
        new(new Simulator(WorldFactory.CreateNew(5, T0), new EventLog(), new EventQueue(), new ISimSystem[] { new CoinSystem() }));

    [Fact]
    public void LiveMode_AdvancesOneWorldDayPerFourRealMinutes()
    {
        var host = Host();
        var now = T0;
        var ticks = new List<LiveTick>();
        host.Run(() => now, wait => now += wait, T0.AddHours(1), ticks.Add);
        Assert.Equal(15, host.Simulator.World.Date.Day);
        Assert.Equal(15, ticks.Sum(t => t.Days));
        Assert.All(ticks, t => Assert.Equal(1, t.Days));
        Assert.Equal(T0.AddHours(1), host.NextDayAt.AddMinutes(-4));
    }

    [Fact]
    public void AStepCatchesUpOwedTime_AndABackwardsClockWaits()
    {
        var host = Host();
        Assert.Equal(30, host.Step(T0.AddHours(2)).Days);
        var tick = host.Step(T0.AddHours(1));
        Assert.Equal(0, tick.Days);
        Assert.Empty(tick.Events);
        Assert.Equal(30, host.Simulator.World.Date.Day);
    }

    [Fact]
    public void TheLiveCommand_CatchesUpAndSaves()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => T0);
        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "live", file.Path, "--minutes", "0" }, output, () => T0.AddHours(24)));
        Assert.Contains("Stopped at Y2 M1 D1.", output.ToString());
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(360, store.Load().World.Date.Day);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'LiveHost' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Cli/CliApp.Chronicle.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int DigestCommand(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var since = Option(rest, "--since") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : (long?)null;
        var take = int.Parse(Option(rest, "--take") ?? Digest.DefaultTake.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (take < 1) throw new FormatException("--take must be at least 1.");
        output.WriteLine(TellDigest(store, sim, gateway, since, take));
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        return 0;
    }

    /// <summary>
    /// Narrates the digest since the reader last looked (or since an event) and marks everything as seen; the caller
    /// saves. Explicit --since leaves the reader's place unchanged.
    /// </summary>
    private static string TellDigest(WorldStore store, Simulator sim, LlmGateway gateway, long? sinceEventId, int take = Digest.DefaultTake)
    {
        var digest = Digest.Build(sim.Log, sim.World, sinceEventId ?? sim.World.LastSeenEventId, store.LoadFollowed(), take);
        if (sinceEventId is null) sim.World.LastSeenEventId = sim.Log.NextId - 1;
        return new LlmNarrator(new StoryContext(sim.World, sim.Log, gateway)).Digest(digest);
    }

    private static int Live(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        var minutes = int.Parse(Option(rest, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        if (minutes < 0) throw new FormatException("--minutes cannot be negative.");
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var followed = store.LoadFollowed();
        var host = new LiveHost(sim);
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            output.WriteLine($"Live from {sim.World.Date}; one world day every {TimeScale.RealTimePerWorldDay.TotalMinutes:0} real minutes.");
            host.Run(clock, wait => cts.Token.WaitHandle.WaitOne(wait), clock().AddMinutes(minutes), tick =>
            {
                store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
                output.WriteLine($"{sim.World.Date}");
                foreach (var ev in tick.Events.Where(e => Importance.Score(e, sim.World, followed) >= LivePinImportance))
                    output.WriteLine("  " + EventText.Describe(ev, NameOf(sim.World)));
            }, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Stopped at {sim.World.Date}.");
        return 0;
    }

    /// <summary>Events at least this important are shown as they happen in live mode.</summary>
    public const int LivePinImportance = 40;

    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large] [--prehistory <years>]   (default 100; tiny 0)
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed, then the digest
          fw live <world-file> [--minutes <n>]      keep the world running in real time (default 60; Ctrl+C stops)
          fw digest <world-file> [--since <event-id>] [--take <n>]   headlines since you last looked
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "live" => Live(path, rest, output, clock, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

        var years = int.Parse(Option(rest, "--prehistory") ?? Prehistory.DefaultYearsFor(size).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (years is < 0 or > 1000) throw new FormatException("--prehistory must be between 0 and 1000 years.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        var sim = Prehistory.Run(world, years);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, sim.Log, sim.Queue);
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        if (years > 0)
            output.WriteLine($"{years} years of prehistory: {sim.Log.Count} events; it is now {world.Date}. Run `fw digest` for the story so far.");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        output.WriteLine(digest);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/Engine/LiveHost.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Engine;

/// <summary>What one live step did: the world days it advanced and the events they produced.</summary>
public sealed record LiveTick(long Days, IReadOnlyList<WorldEvent> Events);

/// <summary>
/// Live mode (spec §7 "Modes"): while the game is open the world advances one day per 4 real minutes, through the
/// same tick function as catch-up. Each step simulates whatever real time is owed (a sleeping laptop catches up
/// on waking); if the clock moves backwards the world waits and never rewinds. The host loop is engine-agnostic:
/// the CLI drives it with a real clock, the Godot client from its frame loop, tests with a fake one.
/// </summary>
public sealed class LiveHost(Simulator sim)
{
    public Simulator Simulator { get; } = sim;

    /// <summary>When the next world day falls due.</summary>
    public DateTimeOffset NextDayAt => Simulator.World.LastRealUtc + TimeScale.RealTimePerWorldDay;

    public LiveTick Step(DateTimeOffset nowUtc)
    {
        var last = Simulator.Log.NextId - 1;
        var result = CatchUp.Run(Simulator, nowUtc);
        return new LiveTick(result.DaysAdvanced, result.DaysAdvanced > 0 ? Simulator.Log.Since(last).ToList() : Array.Empty<WorldEvent>());
    }

    /// <summary>Steps until <paramref name="until"/>, sleeping between world days; onTick sees every step that advanced.</summary>
    public void Run(Func<DateTimeOffset> clock, Action<TimeSpan> sleep, DateTimeOffset until, Action<LiveTick> onTick, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var tick = Step(clock());
            if (tick.Days > 0) onTick(tick);
            var now = clock();
            if (now >= until) return;
            var wait = NextDayAt - now;
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            if (now + wait > until) wait = until - now;
            sleep(wait);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~LiveHostTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 442` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(engine): add a live host loop and fw live

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Compaction

Spec §7 "Compaction": minor events (importance under 30) older than 50 years are rolled into one summary event per decade, except any event a retained event cites as a cause, or that the world's state still refers to. References are found by walking the snapshot (every `…EventId` field, condition and marker values, region memories and scheduled actions' `event` data), so new fields are covered without a list to maintain. Compaction runs *inside* the simulation, as the last system: at the start of each decade, the decade that has just turned fifty is compacted once. That keeps it deterministic, so a world is identical however often it was saved or caught up. The log remembers which ids it forgot until the next save deletes them in the same transaction, and invariant checks track events by id so they survive a shrinking log.

**Files:**
- Create: `sim/FantasyWorld.Sim/Chronicle/ChronicleSystem.cs`, `sim/FantasyWorld.Sim/Chronicle/Compactor.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/Simulator.cs`, `sim/FantasyWorld.Sim/Events/EventLog.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Persist/WorldStore.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Chronicle/CompactorTests.cs`

**Interfaces:**
- Consumes: `EventLog`, `Simulator`, `WorldSnapshot`, `WorldStore`, `DefaultSystems` (Plans 1–5).
- Produces: `EventLog.Forget(ids)`, `ForgottenUnsaved`, `MarkForgottenSaved()`, `IndexAfter(id)`; `record CompactionResult(Forgotten, Summaries)`; `static class Compactor { AgeYears = 50; MinorBelow = 30; SummaryType = "ChronicleCompacted"; Compact(world, log, queue, firstYear, lastYear); References(world, queue) }`; `sealed class ChronicleSystem : ISimSystem` (name `"chronicle"`, last in `DefaultSystems`); `WorldStore.Save` deletes forgotten rows; index `ix_event_causes_effect`; template for `ChronicleCompacted`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Chronicle/CompactorTests.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class CompactorTests
{
    [Fact]
    public void OldMinorEvents_AreSummarised_UnlessSomethingStillCitesThem()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var person = TestPeople.Add(world, 1, 30);
        var gossip = log.Append(new WorldDate(10), new EventDraft("Gossip", Importance: 5));
        var grudge = log.Append(new WorldDate(20), new EventDraft("Insult", Importance: 5));
        var cause = log.Append(new WorldDate(30), new EventDraft("Quarrel", Importance: 5));
        var feud = log.Append(new WorldDate(40), new EventDraft("FeudBegan", Importance: 60, Causes: new[] { new Cause(cause.Id, CauseKind.Trigger, 1) }));
        var later = log.Append(new WorldDate(20 * 360), new EventDraft("Gossip", Importance: 5));
        person.Memories.Add(new Memory(grudge.Id, person.Id, -50, 50));
        world.Date = new WorldDate(60 * 360);

        var result = Compactor.Compact(world, log, new EventQueue(), 1, 10);

        Assert.Equal(new[] { gossip.Id }, result.Forgotten);
        Assert.Equal(new[] { gossip.Id }, log.ForgottenUnsaved);
        Assert.All(new[] { grudge, cause, feud, later }, e => Assert.True(log.TryGet(e.Id, out _)));
        var summary = Assert.Single(result.Summaries);
        Assert.Equal("Year 1 to Year 10", summary.Payload["period"]);
        Assert.Equal("1 Gossip", summary.Payload["most common"]);
        Assert.Empty(Compactor.Compact(world, log, new EventQueue(), 1, 10).Forgotten);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compactor.Compact(world, log, new EventQueue(), 11, 20));
    }

    [Fact]
    public void TheChronicle_CompactsEachDecadeOnce_AsTheWorldRuns()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(360 * 80);

        var summaries = sim.Log.All.Where(e => e.Type == Compactor.SummaryType).ToList();
        Assert.Equal(new[] { "Year 1 to Year 10", "Year 11 to Year 20", "Year 21 to Year 30" }, summaries.Select(s => s.Payload["period"]));
        Assert.DoesNotContain(sim.Log.All, e => e.Date.Year <= 30 && e.Importance < Compactor.MinorBelow
                                                 && e.Type != Compactor.SummaryType && sim.Log.EffectsOf(e.Id).Count == 0
                                                 && !Compactor.References(world, sim.Queue).Contains(e.Id));
        foreach (var ev in sim.Log.All) new HistoryQuery(sim.Log).WhyChain(ev.Id, 8);
    }

    [Fact]
    public void ACompactedWorld_IsTheSameHoweverItWasSaved()
    {
        Simulator Make() => new(WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create());
        var whole = Make();
        whole.AdvanceDays(360 * 72);

        using var file = new TempFile();
        var first = Make();
        first.AdvanceDays(360 * 65);
        using (var store = WorldStore.Open(file.Path)) store.Save(first.World, first.Log, first.Queue);
        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);
        resumed.AdvanceDays(360 * 7);
        reopened.Save(world, log, queue);

        Assert.Equal(WorldHasher.Hash(whole.World, whole.Log, whole.Queue), WorldHasher.Hash(world, log, queue));
        Assert.Equal(log.Count, reopened.CountEvents());
        Assert.Empty(log.ForgottenUnsaved);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Compactor' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Chronicle/ChronicleSystem.cs`:
```csharp
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// Keeps the chronicle compact, last in the tick order: at the start of each decade, the decade that has just become
/// <see cref="Compactor.AgeYears"/> years old is compacted into one summary event. Running inside the simulation
/// makes compaction deterministic: a world advanced in one go or in many sessions is the same.
/// </summary>
public sealed class ChronicleSystem : ISimSystem
{
    public string Name => "chronicle";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var year = ctx.Date.Year;
        if ((year - 1) % 10 != 0) return;
        var last = year - Compactor.AgeYears - 1;
        if (last < 1) return;
        Compactor.Compact(ctx.World, ctx.Log, ctx.Queue, Math.Max(1, last - 9), last);
    }
}
```

`sim/FantasyWorld.Sim/Chronicle/Compactor.cs`:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Sim.Chronicle;

public sealed record CompactionResult(IReadOnlySet<long> Forgotten, IReadOnlyList<WorldEvent> Summaries);

/// <summary>
/// Spec §7 "Compaction": minor events older than 50 years are rolled into one summary event per decade, except any
/// event that a retained event cites as a cause or that the world's state still refers to (memories, provenance,
/// conditions, wars, treaties, scheduled actions…). History stays explainable; the log stops growing without bound.
/// Compaction runs inside the simulation (<see cref="ChronicleSystem"/>), a decade at a time, so a world is the same
/// however often it is loaded, saved or caught up.
/// </summary>
public static class Compactor
{
    public const int AgeYears = 50;
    public const int MinorBelow = 30;
    public const string SummaryType = "ChronicleCompacted";

    /// <summary>Compacts the minor events of years <paramref name="firstYear"/>–<paramref name="lastYear"/> into one summary.</summary>
    public static CompactionResult Compact(World world, EventLog log, EventQueue queue, int firstYear, int lastYear)
    {
        if (lastYear > world.Date.Year - AgeYears - 1)
            throw new ArgumentOutOfRangeException(nameof(lastYear), $"Year {lastYear} is not yet {AgeYears} years old.");
        var keep = References(world, queue);
        var forget = new HashSet<long>();
        // Newest first, so every retained event protects its causes before they are visited.
        for (var i = log.All.Count - 1; i >= 0; i--)
        {
            var ev = log.All[i];
            var year = ev.Date.Year;
            if (keep.Contains(ev.Id) || ev.Importance >= MinorBelow || year < firstYear || year > lastYear || ev.Type == SummaryType)
                foreach (var cause in ev.Causes) keep.Add(cause.EventId);
            else forget.Add(ev.Id);
        }
        if (forget.Count == 0) return new CompactionResult(forget, Array.Empty<WorldEvent>());

        var gone = log.All.Where(e => forget.Contains(e.Id)).ToList();
        log.Forget(forget);
        var summary = log.Append(world.Date, new EventDraft(SummaryType,
            Payload: new Dictionary<string, string>
            {
                ["period"] = $"Year {firstYear} to Year {lastYear}",
                ["events"] = gone.Count.ToString(CultureInfo.InvariantCulture),
                ["most common"] = string.Join(", ", gone.GroupBy(e => e.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Take(5).Select(g => $"{g.Count()} {g.Key}")),
            },
            Importance: 0, Tags: new[] { "Chronicle" }));
        return new CompactionResult(forget, new[] { summary });
    }

    /// <summary>Every event id the world's state refers to, found by walking its snapshot.</summary>
    public static HashSet<long> References(World world, EventQueue queue)
    {
        var ids = new HashSet<long>();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options));
        Walk(doc.RootElement, null, ids);
        return ids;
    }

    private static void Walk(JsonElement element, string? name, HashSet<long> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "Memory" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        // Region.Memory: event ids the region remembers.
                        foreach (var item in property.Value.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Number) ids.Add(item.GetInt64());
                    }
                    else if (property.Name is "Conditions" or "Markers" && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var value in property.Value.EnumerateObject())
                            if (value.Value.TryGetInt64(out var id)) ids.Add(id);
                    }
                    else if (name == "Data" && property.Name == "event" && long.TryParse(property.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var queued))
                        ids.Add(queued);
                    else if (property.Name.EndsWith("EventId", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Number)
                        ids.Add(property.Value.GetInt64());
                    else Walk(property.Value, property.Name, ids);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, name, ids);
                break;
        }
    }
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
    private long _checkedId;

    public Simulator(World world, EventLog log, EventQueue queue, IReadOnlyList<ISimSystem> systems, bool checkInvariants = false)
    {
        World = world;
        Log = log;
        Queue = queue;
        _systems = systems;
        _checkInvariants = checkInvariants;
        _checkedId = log.NextId - 1;
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
        var changed = World.Date.IsFirstOfMonth;

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
            changed = true;
        }

        if (_checkInvariants)
        {
            // Entities only change when a tick or scheduled action ran; the log is checked every day.
            var errors = Invariants.Check(World, Log, Log.IndexAfter(_checkedId), entities: changed);
            if (errors.Count > 0) throw new InvariantViolationException(errors);
            _checkedId = Log.NextId - 1;
        }
    }

    private SimContext ContextFor(string system, string phase) =>
        new(World, Log, Queue, Rng.ForStream(World.Seed, system + "/" + phase, World.Date.Day));
}
```

Replace `sim/FantasyWorld.Sim/Events/EventLog.cs` entirely with:
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

    private readonly List<long> _forgottenUnsaved = new();

    /// <summary>Compacted event ids the world file still holds; the next save deletes them.</summary>
    public IReadOnlyList<long> ForgottenUnsaved => _forgottenUnsaved;

    public void MarkForgottenSaved() => _forgottenUnsaved.Clear();

    /// <summary>The index of the first event with an id above <paramref name="id"/> (ids ascend).</summary>
    public int IndexAfter(long id)
    {
        int lo = 0, hi = _events.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_events[mid].Id <= id) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Removes compacted events (spec §7 "Compaction"). Ids are never reused; nothing retained may cite them.</summary>
    public void Forget(IReadOnlySet<long> ids)
    {
        if (ids.Count == 0) return;
        foreach (var ev in _events)
            if (!ids.Contains(ev.Id) && ev.Causes.Any(c => ids.Contains(c.EventId)))
                throw new InvalidOperationException($"Event #{ev.Id} cites an event being forgotten.");
        var kept = _events.Where(e => !ids.Contains(e.Id)).ToList();
        _events.Clear();
        _byId.Clear();
        _effects.Clear();
        _byEntity.Clear();
        foreach (var ev in kept) Add(ev);
        _forgottenUnsaved.AddRange(ids.Order());
    }

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

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldStore.cs` entirely with:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot, static geography (written once), the append-only event log and the LLM journal.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 4;

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
        CREATE INDEX IF NOT EXISTS ix_event_causes_effect ON event_causes(effect_id);
        CREATE TABLE IF NOT EXISTS llm_calls (
            hash TEXT PRIMARY KEY, tier TEXT NOT NULL, day INTEGER NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL,
            response TEXT NOT NULL, input_tokens INTEGER NOT NULL, output_tokens INTEGER NOT NULL,
            cache_read_tokens INTEGER NOT NULL, cache_write_tokens INTEGER NOT NULL, cost_usd TEXT NOT NULL,
            real_utc TEXT NOT NULL, note TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS followed (kind TEXT NOT NULL, entity_id INTEGER NOT NULL, PRIMARY KEY (kind, entity_id));
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

    /// <summary>
    /// Saves the month in one transaction (spec §9): compacted events removed, new events, the snapshot and new LLM
    /// answers.
    /// </summary>
    public void Save(World world, EventLog log, EventQueue queue, LlmJournal? journal = null)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            foreach (var id in log.ForgottenUnsaved) DeleteEvent(id);
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
            foreach (var call in journal?.Pending ?? Array.Empty<LlmCall>()) InsertCall(call);

            _transaction.Commit();
            journal?.MarkSaved();
            log.MarkForgottenSaved();
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

    /// <summary>Saves new LLM answers alone (narration, which changes nothing in the world).</summary>
    public void SaveJournal(LlmJournal journal)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            foreach (var call in journal.Pending) InsertCall(call);
            _transaction.Commit();
            journal.MarkSaved();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public LlmJournal LoadJournal()
    {
        var journal = new LlmJournal();
        using var cmd = Command("SELECT hash, tier, day, provider, model, response, input_tokens, output_tokens, " +
                                "cache_read_tokens, cache_write_tokens, cost_usd, real_utc, note FROM llm_calls ORDER BY hash");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            journal.Restore(new LlmCall(reader.GetString(0), Enum.Parse<ModelTier>(reader.GetString(1)), reader.GetInt64(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), decimal.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture), reader.GetString(12)));
        return journal;
    }

    public LlmSettings LoadLlmSettings() =>
        GetMeta("llm_settings") is { } json ? JsonSerializer.Deserialize<LlmSettings>(json, SimJson.Options)! : new LlmSettings();

    public void SaveLlmSettings(LlmSettings settings) => SetMeta("llm_settings", JsonSerializer.Serialize(settings, SimJson.Options));

    /// <summary>Entities the player follows (spec §9): their events rank higher in the digest.</summary>
    public SortedSet<EntityRef> LoadFollowed()
    {
        var followed = new SortedSet<EntityRef>(Comparer<EntityRef>.Create((a, b) =>
            string.CompareOrdinal(a.Kind, b.Kind) is var k and not 0 ? k : a.Id.CompareTo(b.Id)));
        using var cmd = Command("SELECT kind, entity_id FROM followed");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) followed.Add(new EntityRef(reader.GetString(0), reader.GetInt64(1)));
        return followed;
    }

    public void Follow(EntityRef entity, bool follow = true)
    {
        using var cmd = Command(follow
            ? "INSERT OR IGNORE INTO followed(kind, entity_id) VALUES ($k, $i)"
            : "DELETE FROM followed WHERE kind = $k AND entity_id = $i");
        cmd.Parameters.AddWithValue("$k", entity.Kind);
        cmd.Parameters.AddWithValue("$i", entity.Id);
        cmd.ExecuteNonQuery();
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
        // 1 → 2 adds the geography table, 2 → 3 llm_calls and 3 → 4 followed; the schema script above creates them.
        if (current < CurrentSchemaVersion)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private void DeleteEvent(long id)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM events WHERE id = $id",
                     "DELETE FROM event_causes WHERE effect_id = $id OR cause_id = $id",
                     "DELETE FROM event_actors WHERE event_id = $id",
                 })
        {
            using var cmd = Command(sql);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
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

    private void InsertCall(LlmCall call)
    {
        using var cmd = Command("INSERT OR IGNORE INTO llm_calls VALUES ($h, $t, $d, $p, $m, $r, $i, $o, $cr, $cw, $c, $u, $n)");
        cmd.Parameters.AddWithValue("$h", call.Hash);
        cmd.Parameters.AddWithValue("$t", call.Tier.ToString());
        cmd.Parameters.AddWithValue("$d", call.Day);
        cmd.Parameters.AddWithValue("$p", call.Provider);
        cmd.Parameters.AddWithValue("$m", call.Model);
        cmd.Parameters.AddWithValue("$r", call.Json);
        cmd.Parameters.AddWithValue("$i", call.InputTokens);
        cmd.Parameters.AddWithValue("$o", call.OutputTokens);
        cmd.Parameters.AddWithValue("$cr", call.CacheReadTokens);
        cmd.Parameters.AddWithValue("$cw", call.CacheWriteTokens);
        cmd.Parameters.AddWithValue("$c", call.CostUsd.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", call.RealUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", call.Note);
        cmd.ExecuteNonQuery();
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

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new ChronicleSystem(),
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CompactorTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 445` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(chronicle): compact minor history older than fifty years

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Compressed snapshots and backups

Spec §9: the snapshot (and the static geography) are stored gzip-compressed, about a fifth of their JSON size; files written before still load. Before any command that advances a world, the file is copied with SQLite's online backup to `.bak1`, after shifting older copies, so the last three sessions can always be restored. A session also refuses to save over a file another session changed since it loaded (two terminals on one world would otherwise silently lose events).

**Files:**
- Create: `sim/FantasyWorld.Cli/Backups.cs`, `sim/FantasyWorld.Sim/Persist/Packed.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.Chronicle.cs`, `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Persist/WorldStore.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Persist/StorageTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Support/TempFile.cs`

**Interfaces:**
- Consumes: `WorldStore` (Plans 1–5).
- Produces: `static class Packed { Pack(value); Unpack<T>(stored) }`; `WorldStore.BackupTo(path)` and its concurrent-session check; `static class Backups { Keep = 3; PathOf(world, n); Rotate(store, world) }` (CLI), used by `advance`, `catchup` and `live`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Persist/StorageTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Tests.Persist;

public class StorageTests
{
    private static object Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Snapshots_AreCompressed_AndOldPlainOnesStillLoad()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());

        var packed = Assert.IsType<byte[]>(Scalar(file.Path, "SELECT json FROM snapshot"));
        var plain = JsonSerializer.Serialize(WorldSnapshot.Capture(world, new EventQueue()), SimJson.Options);
        Assert.True(packed.Length * 3 < plain.Length);
        Assert.IsType<byte[]>(Scalar(file.Path, "SELECT json FROM geography"));

        using (var connection = new SqliteConnection($"Data Source={file.Path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE snapshot SET json = $json";
            cmd.Parameters.AddWithValue("$json", plain);
            cmd.ExecuteNonQuery();
        }
        using var reopened = WorldStore.Open(file.Path);
        Assert.Equal(WorldHasher.Hash(world, new EventLog(), new EventQueue()),
            WorldHasher.Hash(reopened.Load().World, new EventLog(), new EventQueue()));
    }

    [Fact]
    public void Sessions_KeepTheLastThreeBackups()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        foreach (var _ in Enumerable.Range(0, 4))
            CliApp.Run(new[] { "advance", file.Path, "30d" }, output, () => DateTimeOffset.UnixEpoch);

        Assert.True(File.Exists(Backups.PathOf(file.Path, 3)));
        Assert.False(File.Exists(Backups.PathOf(file.Path, 4)));
        using var newest = WorldStore.Open(Backups.PathOf(file.Path, 1));
        Assert.Equal(90, newest.Load().World.Date.Day);
        using var oldest = WorldStore.Open(Backups.PathOf(file.Path, 3));
        Assert.Equal(30, oldest.Load().World.Date.Day);
    }

    [Fact]
    public void ASessionNeverOverwritesAnotherSessionsSave()
    {
        using var file = new TempFile();
        var sim = new Simulator(WorldFactory.CreateNew(5, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(), new ISimSystem[] { new CoinSystem() });
        sim.AdvanceDays(100);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var first = WorldStore.Open(file.Path);
        var (w1, l1, q1) = first.Load();
        using (var second = WorldStore.Open(file.Path))
        {
            var (w2, l2, q2) = second.Load();
            new Simulator(w2, l2, q2, new ISimSystem[] { new CoinSystem() }).AdvanceDays(100);
            second.Save(w2, l2, q2);
        }
        new Simulator(w1, l1, q1, new ISimSystem[] { new CoinSystem() }).AdvanceDays(50);
        var error = Assert.Throws<InvalidOperationException>(() => first.Save(w1, l1, q1));
        Assert.Contains("changed by another session", error.Message);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Support/TempFile.cs` entirely with:
```csharp
namespace FantasyWorld.Sim.Tests.Support;

public sealed class TempFile : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fw-test-{Guid.NewGuid():N}.fw.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", ".bak1", ".bak2", ".bak3" })
            if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Backups' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Cli/Backups.cs`:
```csharp
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Cli;

/// <summary>
/// Rolling backups of the last three sessions (spec §9): before a command changes the world, the file is copied to
/// <c>.bak1</c>, after shifting older copies to <c>.bak2</c> and <c>.bak3</c>.
/// </summary>
public static class Backups
{
    public const int Keep = 3;

    public static string PathOf(string world, int n) => $"{world}.bak{n}";

    public static void Rotate(WorldStore store, string world)
    {
        File.Delete(PathOf(world, Keep));
        for (var n = Keep - 1; n >= 1; n--)
            if (File.Exists(PathOf(world, n))) File.Move(PathOf(world, n), PathOf(world, n + 1));
        store.BackupTo(PathOf(world, 1));
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.Chronicle.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int DigestCommand(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var since = Option(rest, "--since") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : (long?)null;
        var take = int.Parse(Option(rest, "--take") ?? Digest.DefaultTake.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (take < 1) throw new FormatException("--take must be at least 1.");
        output.WriteLine(TellDigest(store, sim, gateway, since, take));
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        return 0;
    }

    /// <summary>
    /// Narrates the digest since the reader last looked (or since an event) and marks everything as seen; the caller
    /// saves. Explicit --since leaves the reader's place unchanged.
    /// </summary>
    private static string TellDigest(WorldStore store, Simulator sim, LlmGateway gateway, long? sinceEventId, int take = Digest.DefaultTake)
    {
        var digest = Digest.Build(sim.Log, sim.World, sinceEventId ?? sim.World.LastSeenEventId, store.LoadFollowed(), take);
        if (sinceEventId is null) sim.World.LastSeenEventId = sim.Log.NextId - 1;
        return new LlmNarrator(new StoryContext(sim.World, sim.Log, gateway)).Digest(digest);
    }

    private static int Live(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        var minutes = int.Parse(Option(rest, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        if (minutes < 0) throw new FormatException("--minutes cannot be negative.");
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var followed = store.LoadFollowed();
        var host = new LiveHost(sim);
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            output.WriteLine($"Live from {sim.World.Date}; one world day every {TimeScale.RealTimePerWorldDay.TotalMinutes:0} real minutes.");
            host.Run(clock, wait => cts.Token.WaitHandle.WaitOne(wait), clock().AddMinutes(minutes), tick =>
            {
                store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
                output.WriteLine($"{sim.World.Date}");
                foreach (var ev in tick.Events.Where(e => Importance.Score(e, sim.World, followed) >= LivePinImportance))
                    output.WriteLine("  " + EventText.Describe(ev, NameOf(sim.World)));
            }, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Stopped at {sim.World.Date}.");
        return 0;
    }

    /// <summary>Events at least this important are shown as they happen in live mode.</summary>
    public const int LivePinImportance = 40;

    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large] [--prehistory <years>]   (default 100; tiny 0)
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed, then the digest
          fw live <world-file> [--minutes <n>]      keep the world running in real time (default 60; Ctrl+C stops)
          fw digest <world-file> [--since <event-id>] [--take <n>]   headlines since you last looked
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "live" => Live(path, rest, output, clock, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

        var years = int.Parse(Option(rest, "--prehistory") ?? Prehistory.DefaultYearsFor(size).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (years is < 0 or > 1000) throw new FormatException("--prehistory must be between 0 and 1000 years.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        var sim = Prehistory.Run(world, years);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, sim.Log, sim.Queue);
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        if (years > 0)
            output.WriteLine($"{years} years of prehistory: {sim.Log.Count} events; it is now {world.Date}. Run `fw digest` for the story so far.");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        output.WriteLine(digest);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/Persist/Packed.cs`:
```csharp
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Large JSON documents (the snapshot, the geography) are stored gzip-compressed (spec §9 "compressed JSON");
/// files written before compression hold plain text, which still loads.
/// </summary>
public static class Packed
{
    public static byte[] Pack<T>(T value)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            JsonSerializer.Serialize(gzip, value, SimJson.Options);
        return buffer.ToArray();
    }

    public static T? Unpack<T>(object stored)
    {
        switch (stored)
        {
            case byte[] bytes:
                using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
                    return JsonSerializer.Deserialize<T>(gzip, SimJson.Options);
            case string json:
                return JsonSerializer.Deserialize<T>(json, SimJson.Options);
            default:
                throw new InvalidOperationException($"Unexpected stored value of type {stored.GetType().Name}.");
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldStore.cs` entirely with:
```csharp
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot, static geography (written once), the append-only event log and the LLM journal.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 4;

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
        CREATE INDEX IF NOT EXISTS ix_event_causes_effect ON event_causes(effect_id);
        CREATE TABLE IF NOT EXISTS llm_calls (
            hash TEXT PRIMARY KEY, tier TEXT NOT NULL, day INTEGER NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL,
            response TEXT NOT NULL, input_tokens INTEGER NOT NULL, output_tokens INTEGER NOT NULL,
            cache_read_tokens INTEGER NOT NULL, cache_write_tokens INTEGER NOT NULL, cost_usd TEXT NOT NULL,
            real_utc TEXT NOT NULL, note TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS followed (kind TEXT NOT NULL, entity_id INTEGER NOT NULL, PRIMARY KEY (kind, entity_id));
        """;

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    /// <summary>The newest event id this session saw in the file (null until it loads or saves).</summary>
    private long? _knownMaxEventId;

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

    /// <summary>
    /// Saves the month in one transaction (spec §9): compacted events removed, new events, the snapshot and new LLM
    /// answers.
    /// </summary>
    public void Save(World world, EventLog log, EventQueue queue, LlmJournal? journal = null)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            if (_knownMaxEventId is { } known && known != persistedMax)
                throw new InvalidOperationException(
                    "The world file was changed by another session since this one loaded it; this session's changes were not saved.");
            foreach (var id in log.ForgottenUnsaved) DeleteEvent(id);
            foreach (var ev in log.Since(persistedMax)) InsertEvent(ev);

            if (world.Geography is { } geography && !HasGeography)
            {
                using var cmd = Command("INSERT INTO geography(id, json) VALUES (1, $json)");
                cmd.Parameters.AddWithValue("$json", Packed.Pack(geography));
                cmd.ExecuteNonQuery();
            }

            using (var cmd = Command("INSERT INTO snapshot(id, json) VALUES (1, $json) " +
                                     "ON CONFLICT(id) DO UPDATE SET json = excluded.json"))
            {
                cmd.Parameters.AddWithValue("$json", Packed.Pack(WorldSnapshot.Capture(world, queue)));
                cmd.ExecuteNonQuery();
            }
            SetMeta("world_day", world.Date.Day.ToString(CultureInfo.InvariantCulture));
            SetMeta("last_real_utc", world.LastRealUtc.ToString("O", CultureInfo.InvariantCulture));
            foreach (var call in journal?.Pending ?? Array.Empty<LlmCall>()) InsertCall(call);

            _transaction.Commit();
            journal?.MarkSaved();
            log.MarkForgottenSaved();
            _knownMaxEventId = log.NextId - 1;
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
        WorldSnapshot snapshot;
        using (var cmd = Command("SELECT json FROM snapshot WHERE id = 1"))
            snapshot = Packed.Unpack<WorldSnapshot>(cmd.ExecuteScalar() ?? throw new InvalidOperationException("No world has been saved in this file."))
                       ?? throw new InvalidOperationException("World snapshot is empty.");

        var log = new EventLog();
        _knownMaxEventId = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
        using (var cmd = Command("SELECT json FROM events ORDER BY id"))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                log.Restore(JsonSerializer.Deserialize<WorldEvent>(reader.GetString(0), SimJson.Options)!);

        var world = snapshot.ToWorld();
        using (var cmd = Command("SELECT json FROM geography WHERE id = 1"))
        {
            if (cmd.ExecuteScalar() is { } geography and not DBNull)
                world.Geography = Packed.Unpack<Geography>(geography);
        }

        return (world, log, snapshot.ToQueue());
    }

    /// <summary>
    /// Copies the world file into <paramref name="backupPath"/> through SQLite's online backup, so a backup taken
    /// while open is consistent.
    /// </summary>
    public void BackupTo(string backupPath)
    {
        var target = new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString();
        using var destination = new SqliteConnection(target);
        destination.Open();
        _connection.BackupDatabase(destination);
    }

    /// <summary>Saves new LLM answers alone (narration, which changes nothing in the world).</summary>
    public void SaveJournal(LlmJournal journal)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            foreach (var call in journal.Pending) InsertCall(call);
            _transaction.Commit();
            journal.MarkSaved();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public LlmJournal LoadJournal()
    {
        var journal = new LlmJournal();
        using var cmd = Command("SELECT hash, tier, day, provider, model, response, input_tokens, output_tokens, " +
                                "cache_read_tokens, cache_write_tokens, cost_usd, real_utc, note FROM llm_calls ORDER BY hash");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            journal.Restore(new LlmCall(reader.GetString(0), Enum.Parse<ModelTier>(reader.GetString(1)), reader.GetInt64(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), decimal.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture), reader.GetString(12)));
        return journal;
    }

    public LlmSettings LoadLlmSettings() =>
        GetMeta("llm_settings") is { } json ? JsonSerializer.Deserialize<LlmSettings>(json, SimJson.Options)! : new LlmSettings();

    public void SaveLlmSettings(LlmSettings settings) => SetMeta("llm_settings", JsonSerializer.Serialize(settings, SimJson.Options));

    /// <summary>Entities the player follows (spec §9): their events rank higher in the digest.</summary>
    public SortedSet<EntityRef> LoadFollowed()
    {
        var followed = new SortedSet<EntityRef>(Comparer<EntityRef>.Create((a, b) =>
            string.CompareOrdinal(a.Kind, b.Kind) is var k and not 0 ? k : a.Id.CompareTo(b.Id)));
        using var cmd = Command("SELECT kind, entity_id FROM followed");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) followed.Add(new EntityRef(reader.GetString(0), reader.GetInt64(1)));
        return followed;
    }

    public void Follow(EntityRef entity, bool follow = true)
    {
        using var cmd = Command(follow
            ? "INSERT OR IGNORE INTO followed(kind, entity_id) VALUES ($k, $i)"
            : "DELETE FROM followed WHERE kind = $k AND entity_id = $i");
        cmd.Parameters.AddWithValue("$k", entity.Kind);
        cmd.Parameters.AddWithValue("$i", entity.Id);
        cmd.ExecuteNonQuery();
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
        // 1 → 2 adds the geography table, 2 → 3 llm_calls and 3 → 4 followed; the schema script above creates them.
        if (current < CurrentSchemaVersion)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private void DeleteEvent(long id)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM events WHERE id = $id",
                     "DELETE FROM event_causes WHERE effect_id = $id OR cause_id = $id",
                     "DELETE FROM event_actors WHERE event_id = $id",
                 })
        {
            using var cmd = Command(sql);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
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

    private void InsertCall(LlmCall call)
    {
        using var cmd = Command("INSERT OR IGNORE INTO llm_calls VALUES ($h, $t, $d, $p, $m, $r, $i, $o, $cr, $cw, $c, $u, $n)");
        cmd.Parameters.AddWithValue("$h", call.Hash);
        cmd.Parameters.AddWithValue("$t", call.Tier.ToString());
        cmd.Parameters.AddWithValue("$d", call.Day);
        cmd.Parameters.AddWithValue("$p", call.Provider);
        cmd.Parameters.AddWithValue("$m", call.Model);
        cmd.Parameters.AddWithValue("$r", call.Json);
        cmd.Parameters.AddWithValue("$i", call.InputTokens);
        cmd.Parameters.AddWithValue("$o", call.OutputTokens);
        cmd.Parameters.AddWithValue("$cr", call.CacheReadTokens);
        cmd.Parameters.AddWithValue("$cw", call.CacheWriteTokens);
        cmd.Parameters.AddWithValue("$c", call.CostUsd.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", call.RealUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", call.Note);
        cmd.ExecuteNonQuery();
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

Run: `dotnet test sim --filter "FullyQualifiedName~StorageTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 448` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(persist): compress snapshots and keep rolling backups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Structures

Spec §5 "Structures", the minimal hooks sub-project 3 will deepen. Yearly and last in the tick order: wealthy lords commission landmarks that take years to raise, paid from their settlement's wealth, and completing one brings renown. Labyrinths open in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers (whom Plan 3's deeds already send after lost artifacts). Abandoned settlements leave ruins, and their landmarks fall with them. Landmarks and labyrinths are named by the inventor (LLM when online).

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`, `sim/FantasyWorld.Sim/Model/Structure.cs`, `sim/FantasyWorld.Sim/Systems/StructureSystem.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.Chronicle.cs`, `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/Query/Names.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/StructureSystemTests.cs`

**Interfaces:**
- Consumes: `IInventor` (Plan 5), `ArtifactForge`, `Fates`, `Conditions` (Plans 2–3).
- Produces: `enum StructureKind { Landmark, Fortification, Labyrinth, Ruin }`, `enum StructureState { Building, Standing, Ruined }`, `Structure` (kind `"structure"`); `World.Structures`; `WorldSnapshot` v5; `Invariants.Structures.cs`; `sealed class StructureSystem(IInventor? inventor = null) : ISimSystem` (name `"structures"`; `CommissionPerYear = 0.02`, `LandmarkWealth = 3000`, `LabyrinthPerYear = 0.02`, `LabyrinthLey = 70`, `LabyrinthDanger = 35`; `LabyrinthCap(world)`); `DefaultSystems` gains it before the chronicle. Events: `LandmarkBegun`, `LandmarkCompleted`, `LabyrinthOpened`, `StructureRuined` (with templates); CLI `structures`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/StructureSystemTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class StructureSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem() }, checkInvariants: true);

    [Fact]
    public void WealthyLords_RaiseLandmarks_OverYears()
    {
        var world = TestRealms.TwoKingdoms();
        var seat = world.Settlements[1];
        seat.Tier = SettlementTier.Town;
        seat.Wealth = 1_000_000;
        var sim = Make(world);

        sim.AdvanceDays(360 * 60);

        var begun = sim.Log.All.First(e => e.Type == "LandmarkBegun");
        var done = sim.Log.All.First(e => e.Type == "LandmarkCompleted");
        Assert.Equal(begun.Id, done.Causes.Single().EventId);
        Assert.True(done.Date.Year - begun.Date.Year >= 6);
        var landmark = world.Structures[done.Actors["structure"].Id];
        Assert.Equal(StructureState.Standing, landmark.State);
        Assert.Equal(seat.LordId, landmark.BuilderId);
        Assert.True(world.Characters[landmark.BuilderId].Renown >= 10 || !world.Characters[landmark.BuilderId].IsAlive);
    }

    [Fact]
    public void Labyrinths_OpenInDangerousLeyLands_AndHideARelic()
    {
        var world = TestPeople.World();
        var wild = world.Regions[2];
        (wild.LeyPower, wild.Danger) = (90, 50);
        var sim = Make(world);

        sim.AdvanceDays(360 * 150);

        var opened = sim.Log.All.Single(e => e.Type == "LabyrinthOpened");
        Assert.Equal(wild.Ref, opened.Place);
        Assert.True(wild.Danger >= 65);
        var relic = world.Artifacts.Values.Single(a => a.OriginEventId == opened.Id);
        Assert.Equal((ArtifactPlace.Lost, wild.Id), (relic.Place, relic.RegionId));
    }

    [Fact]
    public void AbandonedSettlements_LeaveRuins_CitingTheirAbandonment()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var town = world.Settlements[2];
        var lord = world.Characters[town.LordId];
        var begun = log.Append(world.Date, new EventDraft("LandmarkBegun"));
        world.Structures[world.Ids.Take(Structure.Kind)] = new Structure
        {
            Id = 1, Name = "The Spire", Type = StructureKind.Landmark, RegionId = town.RegionId, SettlementId = town.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building,
        };
        var abandoned = log.Append(world.Date, new EventDraft("SettlementAbandoned", Place: town.Ref));
        town.Abandoned = true;
        town.Children = town.Adults = town.Elders = 0;
        lord.Role = Role.None;
        town.LordId = 0;
        lord.SettlementId = 1;

        var sim = Make(world, log);
        sim.AdvanceDays(360);

        var fell = sim.Log.All.Single(e => e.Type == "StructureRuined");
        Assert.Equal(abandoned.Id, fell.Causes.Single().EventId);
        Assert.Equal(StructureState.Ruined, world.Structures[1].State);
        Assert.Contains(world.Structures.Values, s => s.Type == StructureKind.Ruin && s.Name == $"The Ruins of {town.Name}");
    }

    [Fact]
    public void StructuresAreListed()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, CliApp.Run(new[] { "structures", file.Path }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("kind        state", output.ToString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'StructureSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Cli/CliApp.Chronicle.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int DigestCommand(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var since = Option(rest, "--since") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : (long?)null;
        var take = int.Parse(Option(rest, "--take") ?? Digest.DefaultTake.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (take < 1) throw new FormatException("--take must be at least 1.");
        output.WriteLine(TellDigest(store, sim, gateway, since, take));
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        return 0;
    }

    /// <summary>
    /// Narrates the digest since the reader last looked (or since an event) and marks everything as seen; the caller
    /// saves. Explicit --since leaves the reader's place unchanged.
    /// </summary>
    private static string TellDigest(WorldStore store, Simulator sim, LlmGateway gateway, long? sinceEventId, int take = Digest.DefaultTake)
    {
        var digest = Digest.Build(sim.Log, sim.World, sinceEventId ?? sim.World.LastSeenEventId, store.LoadFollowed(), take);
        if (sinceEventId is null) sim.World.LastSeenEventId = sim.Log.NextId - 1;
        return new LlmNarrator(new StoryContext(sim.World, sim.Log, gateway)).Digest(digest);
    }

    private static int Live(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        var minutes = int.Parse(Option(rest, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        if (minutes < 0) throw new FormatException("--minutes cannot be negative.");
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var followed = store.LoadFollowed();
        var host = new LiveHost(sim);
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            output.WriteLine($"Live from {sim.World.Date}; one world day every {TimeScale.RealTimePerWorldDay.TotalMinutes:0} real minutes.");
            host.Run(clock, wait => cts.Token.WaitHandle.WaitOne(wait), clock().AddMinutes(minutes), tick =>
            {
                store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
                output.WriteLine($"{sim.World.Date}");
                foreach (var ev in tick.Events.Where(e => Importance.Score(e, sim.World, followed) >= LivePinImportance))
                    output.WriteLine("  " + EventText.Describe(ev, NameOf(sim.World)));
            }, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Stopped at {sim.World.Date}.");
        return 0;
    }

    private static int StructureList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id kind        state     name                                  where");
        foreach (var s in world.Structures.Values)
        {
            var where = s.SettlementId != 0 ? world.Settlements[s.SettlementId].Name : world.Regions[s.RegionId].Name;
            var state = s.State == StructureState.Building ? $"{s.Progress:0}% built" : s.State.ToString().ToLowerInvariant();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{s.Id,6} {s.Type.ToString().ToLowerInvariant(),-11} {state,-9} {s.Name,-37} {where}"));
        }
        return 0;
    }

    /// <summary>Events at least this important are shown as they happen in live mode.</summary>
    public const int LivePinImportance = 40;

    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large] [--prehistory <years>]   (default 100; tiny 0)
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed, then the digest
          fw live <world-file> [--minutes <n>]      keep the world running in real time (default 60; Ctrl+C stops)
          fw digest <world-file> [--since <event-id>] [--take <n>]   headlines since you last looked
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions|realms]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
          fw realms <world-file>
          fw realm <world-file> <realm-id>
          fw wars <world-file>
          fw war <world-file> <war-id>
          fw structures <world-file>                landmarks, labyrinths and ruins
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
        """;

    /// <summary>Runs one command. Providers builds the LLM backend a world's settings name (tests inject fakes).</summary>
    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null)
    {
        providers ??= settings => LlmProviders.Create(settings);
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output, providers),
                "catchup" => CatchUpCommand(path, output, clock, providers),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
                "structures" => StructureList(path, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "live" => Live(path, rest, output, clock, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
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

        var years = int.Parse(Option(rest, "--prehistory") ?? Prehistory.DefaultYearsFor(size).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (years is < 0 or > 1000) throw new FormatException("--prehistory must be between 0 and 1000 years.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        var sim = Prehistory.Run(world, years);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, sim.Log, sim.Queue);
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        if (years > 0)
            output.WriteLine($"{years} years of prehistory: {sim.Log.Count} events; it is now {world.Date}. Run `fw digest` for the story so far.");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintLlmSummary(output, gateway, fresh);
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        output.WriteLine(digest);
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
        if (layer is not ("biome" or "regions" or "realms")) throw new FormatException($"Unknown layer '{layer}'. Use biome, regions or realms.");
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
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
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

    private static Func<EntityRef, string?> NameOf(World world) => Names.Of(world);

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

`sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }
    }
}
```

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

/// <summary>Split into partial files per subject; each Check* hook is optional.</summary>
public static partial class Invariants
{
    /// <summary>Checks log events from startIndex onward, plus (unless entities is false) every entity. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0, bool entities = true)
    {
        var errors = new List<string>();
        CheckLog(world, log, startIndex, errors);
        if (!entities) return errors;
        CheckRegions(world, log, errors);
        CheckSettlements(world, log, errors);
        CheckCultures(world, errors);
        CheckPeople(world, log, errors);
        CheckArtifacts(world, log, errors);
        CheckRealms(world, log, errors);
        CheckStructures(world, log, errors);
        return errors;
    }

    static partial void CheckRealms(World world, EventLog log, List<string> errors);

    static partial void CheckStructures(World world, EventLog log, List<string> errors);

    static partial void CheckPeople(World world, EventLog log, List<string> errors);

    static partial void CheckArtifacts(World world, EventLog log, List<string> errors);

    private static void CheckCultures(World world, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.CultureId != 0 && !world.Cultures.ContainsKey(region.CultureId))
                errors.Add($"Region #{region.Id} has unknown culture #{region.CultureId}.");
            if (region.FaithId != 0 && !world.Faiths.ContainsKey(region.FaithId))
                errors.Add($"Region #{region.Id} has unknown faith #{region.FaithId}.");
        }
        foreach (var culture in world.Cultures.Values)
            if (!world.Faiths.ContainsKey(culture.FaithId))
                errors.Add($"Culture #{culture.Id} has unknown faith #{culture.FaithId}.");
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

`sim/FantasyWorld.Sim/Model/Structure.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum StructureKind { Landmark, Fortification, Labyrinth, Ruin }

public enum StructureState { Building, Standing, Ruined }

/// <summary>
/// A place-defining work (spec §5 "Structures", minimal hooks expanded in sub-project 3): a landmark raised by a
/// lord over years, a labyrinth that opens in a dangerous, ley-rich region and hides artifacts, or the ruin an
/// abandoned settlement leaves behind.
/// </summary>
public sealed class Structure
{
    public const string Kind = "structure";

    public long Id { get; init; }
    public string Name { get; set; } = "";
    public StructureKind Type { get; init; }
    public long RegionId { get; init; }

    /// <summary>The settlement it stands in, 0 for the wilds (labyrinths).</summary>
    public long SettlementId { get; init; }

    /// <summary>The character who commissioned it, 0 if none.</summary>
    public long BuilderId { get; init; }

    public long StartedEventId { get; init; }
    public long CompletedEventId { get; set; }

    /// <summary>0–100 while building.</summary>
    public double Progress { get; set; }

    public StructureState State { get; set; }

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
    public SortedDictionary<long, Culture> Cultures { get; init; } = new();
    public SortedDictionary<long, Faith> Faiths { get; init; } = new();
    public SortedDictionary<long, House> Houses { get; init; } = new();
    public SortedDictionary<long, Character> Characters { get; init; } = new();
    public SortedDictionary<long, Artifact> Artifacts { get; init; } = new();
    public SortedDictionary<long, Realm> Realms { get; init; } = new();
    public SortedDictionary<long, War> Wars { get; init; } = new();
    public SortedDictionary<long, Army> Armies { get; init; } = new();
    public SortedDictionary<long, Structure> Structures { get; init; } = new();

    /// <summary>Living notables (not faded), in id order.</summary>
    public IEnumerable<Character> ActiveCharacters => Characters.Values.Where(c => c.IsActive);

    /// <summary>Living notables who live in the settlement, in id order.</summary>
    public IEnumerable<Character> PeopleOf(Settlement settlement) =>
        ActiveCharacters.Where(c => c.SettlementId == settlement.Id);

    /// <summary>The region's settlements that are still inhabited, in id order.</summary>
    public IEnumerable<Settlement> LivingSettlements(Region region) =>
        region.Settlements.Select(id => Settlements[id]).Where(s => !s.Abandoned);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
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
    SortedDictionary<long, Settlement>? Settlements = null,
    SortedDictionary<long, Culture>? Cultures = null,
    SortedDictionary<long, Faith>? Faiths = null,
    SortedDictionary<long, House>? Houses = null,
    SortedDictionary<long, Character>? Characters = null,
    SortedDictionary<long, Artifact>? Artifacts = null,
    SortedDictionary<long, Realm>? Realms = null,
    SortedDictionary<long, War>? Wars = null,
    SortedDictionary<long, Army>? Armies = null,
    SortedDictionary<long, Structure>? Structures = null)
{
    public const int CurrentVersion = 5;

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
        world.Settlements,
        world.Cultures,
        world.Faiths,
        world.Houses,
        world.Characters,
        world.Artifacts,
        world.Realms,
        world.Wars,
        world.Armies,
        world.Structures);

    /// <summary>Rebuilds the world. Older snapshots lack newer entities, which load empty.</summary>
    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
        Ids = new IdGen { Next = Ids ?? new SortedDictionary<string, long>() },
        Regions = Copy(Regions),
        Settlements = Copy(Settlements),
        Cultures = Copy(Cultures),
        Faiths = Copy(Faiths),
        Houses = Copy(Houses),
        Characters = Copy(Characters),
        Artifacts = Copy(Artifacts),
        Realms = Copy(Realms),
        Wars = Copy(Wars),
        Armies = Copy(Armies),
        Structures = Copy(Structures),
    };

    private static SortedDictionary<long, T> Copy<T>(SortedDictionary<long, T>? source) =>
        source is null ? new SortedDictionary<long, T>() : new SortedDictionary<long, T>(source);

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}
```

Replace `sim/FantasyWorld.Sim/Query/Names.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Query;

/// <summary>Display names for entities, shared by the CLI, briefs and narration.</summary>
public static class Names
{
    public static string Full(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;

    public static Func<EntityRef, string?> Of(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? Full(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        Realm.Kind => world.Realms.TryGetValue(entity.Id, out var realm) ? Statecraft.Title(realm) : null,
        War.Kind => world.Wars.TryGetValue(entity.Id, out var war) ? war.Name : null,
        Army.Kind => world.Armies.TryGetValue(entity.Id, out var army) ? army.Name : null,
        Structure.Kind => world.Structures.TryGetValue(entity.Id, out var structure) ? structure.Name : null,
        Culture.Kind => world.Cultures.TryGetValue(entity.Id, out var culture) ? culture.Name : null,
        Faith.Kind => world.Faiths.TryGetValue(entity.Id, out var faith) ? faith.Name : null,
        _ => null,
    };
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

`sim/FantasyWorld.Sim/Systems/StructureSystem.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Structures (spec §5, minimal hooks for sub-project 3), yearly and last in the tick order. Wealthy lords commission
/// landmarks that take years to raise (paid from the settlement's wealth; completion brings renown). Labyrinths open
/// in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers. Abandoned settlements
/// leave ruins, and their unfinished or standing landmarks fall with them.
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

    private static readonly string[] Works = { "Spire", "Great Hall", "Bridge", "Temple", "Library", "Colossus", "Lighthouse", "Garden" };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "structures";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        Ruin(ctx);
        Build(ctx);
        Commission(ctx);
        OpenLabyrinths(ctx);
    }

    public static int LabyrinthCap(World world) => Math.Max(1, world.Regions.Count / 15);

    private void Commission(SimContext ctx)
    {
        var world = ctx.World;
        var busy = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= LandmarkWealth
                                                                 && !busy.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            if (!ctx.Rng.Chance(CommissionPerYear)) continue;
            var fallback = $"The {Works[ctx.Rng.NextInt(0, Works.Length)]} of {seat.Name}";
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("landmark", $"{Structure.Kind}#{id}",
                $"A landmark commissioned by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}.", fallback));
            seat.Wealth *= 0.8;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            work.Progress = Math.Min(100, work.Progress + ctx.Rng.NextInt(7, 17));
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            var done = ctx.Emit(new EventDraft("LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        var haunted = labyrinths.Select(s => s.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world))
            {
                var relic = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id, fame: 20);
                world.Artifacts[relic.Id] = relic;
                Fates.Lose(relic, region.Id, opened.Id, 0);
            }
            return; // at most one a year
        }
    }

    private static void Ruin(SimContext ctx)
    {
        var world = ctx.World;
        var ruined = world.Structures.Values.Where(s => s.Type == StructureKind.Ruin).Select(s => s.SettlementId).ToHashSet();
        foreach (var town in world.Settlements.Values.Where(s => s.Abandoned && !ruined.Contains(s.Id)).ToList())
        {
            var abandonment = ctx.Log.Involving(town.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
            var id = world.Ids.Take(Structure.Kind);
            var fell = ctx.Emit(new EventDraft("StructureRuined",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: town.Ref, Payload: new Dictionary<string, string> { ["name"] = $"The Ruins of {town.Name}" },
                Causes: abandonment is null ? null : new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) },
                Importance: 25, Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = $"The Ruins of {town.Name}", Type = StructureKind.Ruin, RegionId = town.RegionId, SettlementId = town.Id,
                StartedEventId = fell.Id, CompletedEventId = fell.Id, Progress = 100, State = StructureState.Ruined,
            };
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type == StructureKind.Landmark && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~StructureSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 452` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add landmarks, labyrinths and ruins

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: War causality and the success criteria

Spec §11 criterion 3 asks that `fw why` on a war prints a chain of linked causes reaching root events. A war declaration now cites, besides its casus belli (the motive), the two realms' latest rivalry or broken alliance and the ruler's accession as enablers, and a declaration of independence cites the overlord's accession, so chains reach back past the pretext. Then the criteria are checked on a medium world after its century of prehistory: invariants hold, at least nine in ten wars have a why-chain of three or more events with root causes, eight real hours away produce a digest of 3–8 headlines, and compacting everything old enough leaves every war's chain exactly as long as before.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`, `sim/FantasyWorld.Sim/Systems/Fates.cs`, `sim/FantasyWorld.Sim/Systems/RealmSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/SuccessCriteriaTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `Fates.Accession(log, character)`; `DiplomacySystem` war causes; `SuccessCriteriaTests` (with a shared `PrehistoricWorld` fixture and its deep `Fork()`).

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/SuccessCriteriaTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 1 failing.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>A reason to go to war, found in history: the event it cites and what it aims at.</summary>
public sealed record CasusBelliOption(string Kind, long Against, long EventId, long TargetRegionId, long TargetArtifactId, double Weight);

/// <summary>
/// Relations between independent realms (spec §6 "Diplomacy"), yearly. Realm opinion = the rulers'
/// personal opinion + realm memories + faith and culture + treaties. Friendly neighbours ally (sealed
/// by a royal marriage when kin allow), hostile ones declare rivalry. A ruler at peace weighs every
/// casus belli history offers against keeping the peace (a decision point); a declared war cites its
/// casus belli and draws in allies.
/// </summary>
public sealed class DiplomacySystem : ISimSystem
{
    private readonly IDecider _decider;
    private readonly IInventor _inventor;

    public DiplomacySystem(IDecider? decider = null, IInventor? inventor = null)
    {
        _decider = decider ?? new RulesDecider();
        _inventor = inventor ?? new RulesInventor();
    }

    public string Name => "diplomacy";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var realms = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var a in realms)
            foreach (var b in Realms.Neighbours(world, a).Where(b => b.Id > a.Id && b.LiegeId == 0))
                ReviewTreaties(ctx, a, b);

        foreach (var realm in realms)
        {
            if (realm.Destroyed || realm.LiegeId != 0 || AtWar(world, realm)) continue;
            var ruler = Realms.RulerOf(world, realm);
            if (ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var options = CasusBelliFor(ctx, realm);
            if (options.Count == 0) continue;
            var choices = options.Select((o, i) => new DecisionOption($"war:{i}", o.Weight * Aggression(world, realm, ruler)
                    * Math.Clamp(Strength(world, realm) / Math.Max(1, Strength(world, world.Realms[o.Against])), 0.3, 2.0),
                    $"{o.Kind} against {world.Realms[o.Against].Name}"))
                .Append(new DecisionOption("peace", 100 + realm.WarWeariness, "keep the peace"))
                .ToList();
            var choice = _decider.Decide(new DecisionBrief(ruler.Id, "war or peace", choices), ctx.Rng);
            if (choice.Key == "peace") continue;
            using (ctx.Annotate("motive", choice.Reasoning))
                Declare(ctx, realm, options[int.Parse(choice.Key[4..], CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>What realm a thinks of realm b, −100…100.</summary>
    public static double Opinion(World world, Realm a, Realm b)
    {
        var score = 0.0;
        if (Realms.RulerOf(world, a) is { } ra && Realms.RulerOf(world, b) is { } rb) score += Relations.Opinion(ra, rb) / 2;
        score += a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += a.FaithId == b.FaithId ? 15 : -15;
        if (a.CultureId == b.CultureId) score += 10;
        if (a.Has(TreatyKind.Alliance, b.Id)) score += 25;
        if (a.Has(TreatyKind.Rivalry, b.Id)) score -= 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Rough fighting power: levies from the territory, men-at-arms the treasury could pay, knights.</summary>
    public static double Strength(World world, Realm realm)
    {
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var levies = world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)).Sum(s => s.Adults) * 0.05;
        var knights = world.ActiveCharacters.Count(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        return levies + Math.Max(0, realm.Treasury) / 50 * 3 + knights * 20;
    }

    public static bool AtWar(World world, Realm realm) => world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id));

    private static double Aggression(World world, Realm realm, Character ruler)
    {
        var a = 1.0;
        if (ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave)) a *= 1.5;
        if (ruler.Has(Traits.Content) || ruler.Has(Traits.Craven) || ruler.Has(Traits.Kind)) a *= 0.5;
        var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
        if (tenets.Contains("pacifist")) a *= 0.3;
        if (tenets.Contains("warlike")) a *= 1.5;
        if (realm.Conditions.ContainsKey(Statecraft.StabilityCollapsed)) a *= 0.5;
        return a;
    }

    private static void ReviewTreaties(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        var ab = Opinion(world, a, b);
        var ba = Opinion(world, b, a);
        var allied = a.Has(TreatyKind.Alliance, b.Id);
        var rivals = a.Has(TreatyKind.Rivalry, b.Id);
        if (!allied && ab >= 40 && ba >= 40 && !AtWarWith(world, a, b))
        {
            var wedding = RoyalMarriage(ctx, a, b);
            var pact = Pact(ctx, a, b, TreatyKind.Alliance, "AllianceFormed", 40, wedding);
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == a.Id);
            Statecraft.Remember(a, pact.Id, b.Id, 40, 60);
            Statecraft.Remember(b, pact.Id, a.Id, 40, 60);
        }
        else if (allied && (ab < 0 || ba < 0))
        {
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
            var broken = ctx.Emit(new EventDraft("AllianceBroken", Actors: Pair(a, b), Place: world.Settlements[a.CapitalId].Ref,
                Importance: 40, Tags: new[] { "Politics", "Diplomacy" }));
            Statecraft.Remember(a, broken.Id, b.Id, -30, 60);
            Statecraft.Remember(b, broken.Id, a.Id, -30, 60);
        }
        else if (!rivals && ab <= -40 && ba <= -40)
            Pact(ctx, a, b, TreatyKind.Rivalry, "RivalryDeclared", 35, null);
    }

    private static WorldEvent Pact(SimContext ctx, Realm a, Realm b, TreatyKind kind, string type, int importance, WorldEvent? cause)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: Pair(a, b), Place: ctx.World.Settlements[a.CapitalId].Ref,
            Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Enabler, 0.8) },
            Importance: importance, Tags: new[] { "Politics", "Diplomacy" }));
        a.Treaties.Add(new Treaty(kind, b.Id, ev.Id, 0));
        b.Treaties.Add(new Treaty(kind, a.Id, ev.Id, 0));
        return ev;
    }

    /// <summary>Weds an unmarried adult child or sibling of one ruler to one of the other's, if any match.</summary>
    private static WorldEvent? RoyalMarriage(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        IEnumerable<Character> Kin(Realm realm) => Realms.RulerOf(world, realm) is { } ruler
            ? ruler.Children.Select(id => world.Characters[id]).Concat(Fates.Siblings(world, ruler)).Append(ruler)
                .Where(k => FamilySystem.Marriageable(k, ctx.Date))
            : Array.Empty<Character>();
        // Children first, then siblings, then the rulers themselves.
        foreach (var x in Kin(a))
            foreach (var y in Kin(b))
                if (x.Sex != y.Sex && !(x.Role == Role.Lord && y.Role == Role.Lord) && !Relations.AreKin(x, y))
                    return FamilySystem.Wed(ctx, x, y);
        return null;
    }

    /// <summary>Every casus belli this realm's history gives it against a neighbour.</summary>
    public static List<CasusBelliOption> CasusBelliFor(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var options = new List<CasusBelliOption>();
        var ruler = Realms.RulerOf(world, realm);
        foreach (var enemy in Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id)))
        {
            var theirs = Realms.Territory(world, enemy).ToList();
            var grudge = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling <= -50).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null)
            {
                var ev = ctx.Log.Get(grudge.EventId);
                var lost = ev.Type == "RegionCeded" && theirs.Any(r => ev.Place == r.Ref) ? ev.Place!.Value.Id : 0;
                if (ev.Type == "IndependenceDeclared")
                    options.Add(new(CasusBelli.Independence, enemy.Id, grudge.EventId, world.Settlements[enemy.CapitalId].RegionId, 0, 40));
                else if (lost != 0) options.Add(new(CasusBelli.Reconquest, enemy.Id, grudge.EventId, lost, 0, 40));
                else options.Add(new(CasusBelli.Revenge, enemy.Id, grudge.EventId, Border(world, realm, enemy), 0, 30));
            }

            var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
            if (realm.FaithId != enemy.FaithId && (tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, 0, Border(world, realm, enemy), 0, 20));

            var hungry = Realms.Domain(world, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
            var granary = theirs.Where(r => r.Fertility >= 60 && Touches(world, r, realm)).OrderByDescending(r => r.Fertility).ThenBy(r => r.Id).FirstOrDefault();
            if (hungry != 0 && granary is not null) options.Add(new(CasusBelli.ResourceNeed, enemy.Id, hungry, granary.Id, 0, 30));

            if (ruler is not null)
            {
                foreach (var artifact in world.Artifacts.Values.Where(x => HeldBy(world, x, enemy)))
                {
                    // Only a fresh wrong counts: the artifact's latest change of hands was a theft from the ruler's house.
                    var theft = artifact.Provenance.LastOrDefault();
                    if (theft is { How: "stolen" or "looted" } && ruler.HouseId != 0
                        && world.Characters.TryGetValue(theft.From, out var victim) && victim.HouseId == ruler.HouseId) options.Add(new(CasusBelli.ArtifactRecovery, enemy.Id, theft.EventId, world.Settlements[enemy.CapitalId].RegionId, artifact.Id, 25));
                }
                if (Realms.RulerOf(world, enemy) is { } theirRuler && world.Characters.TryGetValue(theirRuler.SpouseId, out var consort)
                    && (consort.HouseId == ruler.HouseId) && consort.MarriageEventId != 0)
                    options.Add(new(CasusBelli.Claim, enemy.Id, consort.MarriageEventId, world.Settlements[enemy.CapitalId].RegionId, 0, 20));
            }
        }
        return options;
    }

    private void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
    {
        var world = ctx.World;
        var defender = world.Realms[cb.Against];
        var cbEvent = cb.EventId;
        if (cb.Kind == CasusBelli.HolyWar)
            cbEvent = ctx.Emit(new EventDraft("HolyWarProclaimed", Actors: Pair(attacker, defender),
                Place: world.Settlements[attacker.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["faith"] = world.Faiths[attacker.FaithId].Name },
                Importance: 50, Tags: new[] { "Politics", "Faith" })).Id;

        var target = world.Regions.GetValueOrDefault(cb.TargetRegionId);
        var name = cb.Kind switch
        {
            CasusBelli.HolyWar => $"The Holy War of {world.Faiths[attacker.FaithId].Deity}",
            CasusBelli.Independence => $"The {defender.Name} Rebellion",
            CasusBelli.Revenge => $"The War of {attacker.Name}'s Vengeance",
            _ => $"The {target?.Name ?? defender.Name} War",
        };
        var warId = world.Ids.Peek(War.Kind);
        var invention = _inventor.Invent(new InventionBrief("war", $"{War.Kind}#{warId}",
            $"A war of {cb.Kind} declared by {Statecraft.Title(attacker)} on {Statecraft.Title(defender)}"
            + (target is null ? "" : $" over {target.Name}") + $", in year {ctx.Date.Year}.", name));
        name = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = name,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            Attackers = { attacker.Id },
            Defenders = { defender.Id },
            CasusBelli = cb.Kind,
            CasusBelliEventId = cbEvent,
            TargetRegionId = cb.TargetRegionId,
            TargetArtifactId = cb.TargetArtifactId,
            StartDay = ctx.Date.Day,
        };
        var declared = ctx.Emit(new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = attacker.Ref, ["defender"] = defender.Ref, ["war"] = war.Ref },
            Place: world.Settlements[defender.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["casus belli"] = cb.Kind, ["war"] = name },
            Causes: WarCauses(ctx, attacker, defender, cbEvent),
            Importance: 70, Tags: new[] { "Politics", "War" }));
        war.DeclaredEventId = declared.Id;
        world.Wars[war.Id] = war;
        attacker.Treaties.RemoveAll(t => t.With == defender.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        defender.Treaties.RemoveAll(t => t.With == attacker.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        Statecraft.Remember(defender, declared.Id, attacker.Id, -50, 70);

        foreach (var (side, leader) in new[] { (war.Attackers, attacker), (war.Defenders, defender) })
            foreach (var ally in Realms.Independent(world).Where(r => r.Has(TreatyKind.Alliance, leader.Id) && !AtWar(world, r)).OrderBy(r => r.Id).ToList())
            {
                // An ally of both sides stays out rather than fight a friend.
                if (war.Involves(ally.Id) || ally.Has(TreatyKind.Alliance, attacker.Id) && ally.Has(TreatyKind.Alliance, defender.Id)
                    || !ctx.Rng.Chance(0.7)) continue;
                side.Add(ally.Id);
                ctx.Emit(new EventDraft("AllyJoinedWar",
                    Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                    Place: world.Settlements[ally.CapitalId].Ref,
                    Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
            }
    }

    /// <summary>
    /// The casus belli is the motive; the realms' latest rivalry and the ruler's accession, when there are any, are
    /// enablers, so a war's why-chain reaches back past its pretext.
    /// </summary>
    private static List<Cause> WarCauses(SimContext ctx, Realm attacker, Realm defender, long cbEvent)
    {
        var causes = new List<Cause> { new(cbEvent, CauseKind.Motive, 0.9) };
        var rivalry = ctx.Log.Involving(attacker.Ref).LastOrDefault(e => e.Type is "RivalryDeclared" or "AllianceBroken"
                                                                           && e.Actors.ContainsValue(defender.Ref));
        if (rivalry is not null && rivalry.Id != cbEvent) causes.Add(new Cause(rivalry.Id, CauseKind.Enabler, 0.5));
        if (Realms.RulerOf(ctx.World, attacker) is { } ruler
            && Fates.Accession(ctx.Log, ruler) is { } accession && accession.Id != cbEvent)
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.3));
        return causes;
    }

    private static bool AtWarWith(World world, Realm a, Realm b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.IsAttacker(a.Id) && w.Defenders.Contains(b.Id) || w.IsAttacker(b.Id) && w.Defenders.Contains(a.Id)));

    private static bool HeldBy(World world, Artifact x, Realm realm) => x.Place switch
    {
        ArtifactPlace.Held => Realms.SovereignOf(world, world.Settlements[world.Characters[x.HolderId].SettlementId])?.Id == realm.Id,
        ArtifactPlace.Treasury => Realms.SovereignOf(world, world.Settlements[x.SettlementId])?.Id == realm.Id,
        _ => false,
    };

    /// <summary>The enemy's region nearest our territory: an enemy region adjacent to ours, lowest id.</summary>
    private static long Border(World world, Realm ours, Realm theirs) =>
        Realms.Territory(world, theirs).Where(r => Touches(world, r, ours)).Select(r => r.Id).DefaultIfEmpty(world.Settlements[theirs.CapitalId].RegionId).Min();

    private static bool Touches(World world, Region region, Realm realm) =>
        (world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).Any(n => Realms.SovereignOf(world, world.Regions[n])?.Id == realm.Id);

    private static Dictionary<string, EntityRef> Pair(Realm a, Realm b) => new() { ["a"] = a.Ref, ["b"] = b.Ref };
}
```

Replace `sim/FantasyWorld.Sim/Systems/Fates.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Shared consequences used by several character systems: death with succession and inheritance,
/// wounds, memories and moving artifacts. A helper library, not a system: it never runs on its own.
/// </summary>
public static class Fates
{
    /// <summary>Settlement condition: the seat is empty; maps to the death that emptied it.</summary>
    public const string VacantSeat = "vacantSeat";

    private static readonly string[] WoundSites = { "sword arm", "left leg", "right eye", "ribs", "face", "hand" };

    /// <summary>Importance of an event about this character: base + renown/2, lords +20.</summary>
    public static int Importance(Character c, int baseline) =>
        Math.Clamp(baseline + (int)(c.Renown / 2) + (c.Role == Role.Lord ? 20 : 0), 0, 100);

    public static void Remember(Character c, long eventId, long about, double feeling, double weight) =>
        c.Memories.Add(new Memory(eventId, about, feeling, weight));

    /// <summary>
    /// Kills a character: the death event, grief and (if killed) vengeance among kin, the end of a
    /// marriage, succession to their seat, and inheritance of what they held — unless they died in the
    /// wilds (lostInRegion), where what they carried is lost to be found again by delvers.
    /// </summary>
    public static WorldEvent Die(SimContext ctx, Character c, string cause, IReadOnlyList<Cause> causes,
        Character? killer = null, long lostInRegion = 0)
    {
        var world = ctx.World;
        var actors = new Dictionary<string, EntityRef> { ["deceased"] = c.Ref };
        if (killer is not null) actors["killer"] = killer.Ref;
        var death = ctx.Emit(new EventDraft("CharacterDied", Actors: actors,
            Place: world.Settlements[c.SettlementId].Ref,
            Payload: new Dictionary<string, string>
            {
                ["cause"] = cause,
                ["age"] = c.Age(ctx.Date).ToString(CultureInfo.InvariantCulture),
            },
            Causes: causes, Importance: Importance(c, 15), Tags: new[] { "Characters" }));
        c.Died = ctx.Date;
        c.DeathCause = cause;
        c.Memories.Clear();

        var widow = Living(world, c.SpouseId);
        if (widow is not null)
        {
            widow.SpouseId = 0;
            Remember(widow, death.Id, killer?.Id ?? 0, -60, 60);
        }
        c.SpouseId = 0;

        var kinIds = c.Children.Concat(new[] { c.FatherId, c.MotherId }).Concat(Siblings(world, c).Select(k => k.Id));
        foreach (var kin in kinIds.Where(id => id != 0 && id != killer?.Id).Distinct().OrderBy(id => id).Select(id => world.Characters[id]).Where(k => k.IsActive))
        {
            Remember(kin, death.Id, killer?.Id ?? 0, -50, 50);
            if (killer is not null && killer.IsAlive && kin.Stage(ctx.Date) != LifeStage.Child && !kin.Has(Traits.Craven))
            {
                kin.Ambition = Ambitions.Vengeance;
                kin.AmbitionTarget = killer.Id;
            }
        }

        foreach (var realm in world.Realms.Values)
            foreach (var seat in realm.Council.Where(s => s.Value == c.Id).Select(s => s.Key).ToList()) realm.Council.Remove(seat);
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded))
        {
            army.Knights.Remove(c.Id);
            if (army.CommanderId == c.Id) army.CommanderId = 0;
        }

        var heir = Heir(world, c, ctx.Date, killer);
        foreach (var seat in world.Settlements.Values.Where(s => s.LordId == c.Id).ToList())
            Succeed(ctx, seat, c, SeatCandidates(world, c, seat, killer, ctx.Date).FirstOrDefault(k => CanTakeSeat(world, k)), death);
        c.Role = Role.None;

        foreach (var artifact in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList())
        {
            if (lostInRegion != 0) Lose(artifact, lostInRegion, death.Id, c.Id);
            else Inherit(ctx, artifact, c, heir ?? widow, death);
        }
        return death;
    }

    /// <summary>Eldest active child, else eldest active sibling (never the killer). Faded relatives have left the story.</summary>
    public static Character? Heir(World world, Character c, Core.WorldDate today, Character? killer = null) =>
        HeirCandidates(world, c, killer).FirstOrDefault();

    /// <summary>Line of succession: active children by age, then active siblings by age, excluding the killer.</summary>
    public static IEnumerable<Character> HeirCandidates(World world, Character c, Character? killer = null) =>
        c.Children.Select(id => world.Characters[id]).Where(k => k.IsActive && k.Id != killer?.Id)
            .OrderBy(k => k.BirthDay).ThenBy(k => k.Id)
            .Concat(Siblings(world, c).Where(k => k.IsActive && k.Id != killer?.Id).OrderBy(k => k.BirthDay).ThenBy(k => k.Id));

    /// <summary>
    /// Line of succession to a seat. A realm's capital follows its law: primogeniture (children, then
    /// siblings), seniority (siblings, then children) or elective (kin and council, adults first, ablest first).
    /// </summary>
    /// <summary>The succession that brought this character to their seat, if they inherited it.</summary>
    public static WorldEvent? Accession(EventLog log, Character c) =>
        log.Involving(c.Ref).LastOrDefault(e => e.Type == "SuccessionOccurred" && e.Actors.TryGetValue("heir", out var heir) && heir == c.Ref);

    public static IEnumerable<Character> SeatCandidates(World world, Character c, Settlement seat, Character? killer, Core.WorldDate today)
    {
        var realm = world.Realms.Values.Where(r => !r.Destroyed && r.CapitalId == seat.Id).OrderBy(r => r.Id).FirstOrDefault();
        switch (realm?.SuccessionLaw)
        {
            case SuccessionLaw.Seniority:
                var siblings = Siblings(world, c).Where(k => k.IsActive && k.Id != killer?.Id).OrderBy(k => k.BirthDay).ThenBy(k => k.Id);
                var children = c.Children.Select(id => world.Characters[id]).Where(k => k.IsActive && k.Id != killer?.Id)
                    .OrderBy(k => k.BirthDay).ThenBy(k => k.Id);
                return siblings.Concat(children);
            case SuccessionLaw.Elective:
                return HeirCandidates(world, c, killer)
                    .Concat(realm.Council.Values.Select(id => world.Characters[id]).Where(k => k.IsActive && k.Id != killer?.Id && k.Id != c.Id))
                    .DistinctBy(k => k.Id)
                    .OrderBy(k => k.Stage(today) == LifeStage.Child ? 1 : 0)
                    .ThenByDescending(Electability).ThenBy(k => k.Id);
            default:
                return HeirCandidates(world, c, killer);
        }
    }

    public static double Electability(Character k) =>
        k.Attributes.Stewardship + k.Attributes.Diplomacy + k.Attributes.Martial + k.Renown / 5;

    /// <summary>A seat needs a lord who can live there: not already a lord, not married to one.</summary>
    public static bool CanTakeSeat(World world, Character k) =>
        k.Role != Role.Lord && !(world.Characters.TryGetValue(k.SpouseId, out var spouse) && spouse.Role == Role.Lord);

    /// <summary>Children of either parent, other than c (found through parents, not by scanning everyone).</summary>
    public static IEnumerable<Character> Siblings(World world, Character c) =>
        new[] { c.FatherId, c.MotherId }.Where(p => p != 0).Distinct()
            .SelectMany(p => world.Characters[p].Children).Where(id => id != c.Id).Distinct()
            .Select(id => world.Characters[id]);

    /// <summary>Whether c would inherit a seat today: only a lord's child or sibling can.</summary>
    public static bool IsHeirApparent(World world, Character c, Core.WorldDate today) =>
        new[] { c.FatherId, c.MotherId }.Where(p => p != 0).Select(p => world.Characters[p])
            .Concat(Siblings(world, c))
            .Any(k => k.Role == Role.Lord && Heir(world, k, today)?.Id == c.Id);

    private static void Succeed(SimContext ctx, Settlement seat, Character predecessor, Character? heir, WorldEvent death)
    {
        var world = ctx.World;
        if (heir is null)
        {
            seat.LordId = 0;
            seat.Conditions[VacantSeat] = death.Id;
            return;
        }
        heir.Role = Role.Lord;
        heir.SettlementId = seat.Id;
        seat.LordId = heir.Id;
        if (world.Characters.TryGetValue(heir.SpouseId, out var consort)) consort.SettlementId = seat.Id;
        if (world.Houses.TryGetValue(heir.HouseId, out var house)) house.SeatSettlementId = seat.Id;
        var law = world.Realms.Values.Where(r => !r.Destroyed && r.CapitalId == seat.Id).Select(r => r.SuccessionLaw.ToString()).FirstOrDefault();
        foreach (var realm in world.Realms.Values) realm.Council.Where(s => s.Value == heir.Id).Select(s => s.Key).ToList().ForEach(s => realm.Council.Remove(s));
        ctx.Emit(new EventDraft("SuccessionOccurred",
            Actors: new Dictionary<string, EntityRef> { ["heir"] = heir.Ref, ["predecessor"] = predecessor.Ref },
            Payload: law is null ? null : new Dictionary<string, string> { ["law"] = law },
            Place: seat.Ref,
            Causes: new[] { new Cause(death.Id, CauseKind.Trigger, 1.0) },
            Importance: seat.Tier switch { SettlementTier.City => 55, SettlementTier.Town => 40, _ => 20 },
            Tags: new[] { "Characters", "Politics" }));
    }

    private static void Inherit(SimContext ctx, Artifact artifact, Character from, Character? to, WorldEvent death)
    {
        var settlement = ctx.World.Settlements[from.SettlementId];
        var ev = ctx.Emit(new EventDraft("ArtifactInherited",
            Actors: to is null
                ? new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref }
                : new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["heir"] = to.Ref },
            Place: settlement.Ref,
            Causes: new[] { new Cause(death.Id, CauseKind.Trigger, 1.0) },
            Importance: (int)Math.Clamp(10 + artifact.Fame / 3, 0, 100), Tags: new[] { "Artifacts" }));
        if (to is null) MoveToTreasury(artifact, settlement.Id, ev.Id, from.Id, "inherited");
        else MoveToHolder(artifact, to, ev.Id, "inherited");
    }

    public static void MoveToHolder(Artifact artifact, Character to, long eventId, string how)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, artifact.Place == ArtifactPlace.Held ? artifact.HolderId : 0, to.Id, how));
        artifact.Place = ArtifactPlace.Held;
        artifact.HolderId = to.Id;
        artifact.SettlementId = 0;
        artifact.RegionId = 0;
    }

    public static void MoveToTreasury(Artifact artifact, long settlementId, long eventId, long fromId, string how)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, fromId, 0, how));
        artifact.Place = ArtifactPlace.Treasury;
        artifact.HolderId = 0;
        artifact.SettlementId = settlementId;
        artifact.RegionId = 0;
    }

    public static void Lose(Artifact artifact, long regionId, long eventId, long fromId)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, fromId, 0, "lost"));
        artifact.Place = ArtifactPlace.Lost;
        artifact.HolderId = 0;
        artifact.SettlementId = 0;
        artifact.RegionId = regionId;
    }

    /// <summary>Applies a non-lethal fate: wounds may scar; maiming leaves a lasting wound.</summary>
    public static string? Injure(Character c, Fate fate, Core.Rng rng)
    {
        if (fate is not (Fate.Wounded or Fate.Maimed)) return null;
        var site = WoundSites[rng.NextInt(0, WoundSites.Length)];
        if (fate == Fate.Maimed)
        {
            c.Wounds.Add(site);
            if (!c.Has(Traits.Maimed)) c.Traits.Add(Traits.Maimed);
        }
        else if (rng.Chance(0.3) && !c.Has(Traits.Scarred)) c.Traits.Add(Traits.Scarred);
        c.Traits.Sort(StringComparer.Ordinal);
        return site;
    }

    private static Character? Living(World world, long id) =>
        world.Characters.TryGetValue(id, out var c) && c.IsAlive ? c : null;
}
```

Replace `sim/FantasyWorld.Sim/Systems/RealmSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Politics inside realms (spec §6). Monthly: capitals stay in the domain, settlements pay taxes
/// (vassals pass a fifth to their liege), councils of duchies and greater fill their seats. Yearly:
/// legitimacy and stability drift (with collapse and recovery as condition events citing their causes),
/// war weariness fades, unhappy vassals declare independence, ranks rise and fall, and each change of
/// phase opens a named era.
/// </summary>
public sealed class RealmSystem : ISimSystem
{
    public const double TaxRate = 0.01;
    public const double VassalTribute = 0.2;
    public const double IndependencePerYear = 0.25;

    private static readonly IReadOnlyDictionary<RealmPhase, string[]> EraWords = new Dictionary<RealmPhase, string[]>
    {
        [RealmPhase.Expanding] = new[] { "Rising", "Golden", "Banners", "Conquest" },
        [RealmPhase.Stable] = new[] { "Long Peace", "Plenty", "Quiet Crowns", "Hearth" },
        [RealmPhase.Declining] = new[] { "Twilight", "Waning", "Ash", "Broken Oaths" },
    };

    private readonly IInventor _inventor;

    public RealmSystem(IInventor? inventor = null) => _inventor = inventor ?? new RulesInventor();

    public string Name => "realms";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        ILookup<long, Character>? residents = null;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            if (realm.Destroyed || !Statecraft.SecureCapital(ctx, realm, CapitalLossCauses(ctx, realm))) continue;
            Collect(world, realm);
            if (realm.Rank >= RealmRank.Duchy)
                FillCouncil(ctx, realm, residents ??= world.ActiveCharacters.ToLookup(c => c.SettlementId));
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            Drift(ctx, realm);
            if (realm.LiegeId != 0) ConsiderIndependence(ctx, realm);
            Rerank(ctx, realm);
            if (realm.LiegeId == 0) ReviewPhase(ctx, realm);
            for (var i = realm.Memories.Count - 1; i >= 0; i--)
            {
                var faded = realm.Memories[i] with { Weight = realm.Memories[i].Weight * 0.85 };
                if (faded.Weight < 3) realm.Memories.RemoveAt(i);
                else realm.Memories[i] = faded;
            }
            if (!world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id)))
                realm.WarWeariness = Math.Max(0, realm.WarWeariness - 10);
        }
    }

    private static void Collect(World world, Realm realm)
    {
        var income = 0.0;
        foreach (var s in Realms.Domain(world, realm).SelectMany(world.LivingSettlements))
        {
            var tax = s.Wealth * TaxRate;
            s.Wealth -= tax;
            income += tax;
        }
        if (realm.LiegeId != 0)
        {
            var tribute = income * VassalTribute;
            world.Realms[realm.LiegeId].Treasury += tribute;
            income -= tribute;
        }
        realm.Treasury += income;
    }

    /// <summary>Residents of the realm's domain, looked up once per tick: the cost stays flat as the dead accumulate.</summary>
    private static void FillCouncil(SimContext ctx, Realm realm, ILookup<long, Character> residents)
    {
        var world = ctx.World;
        var ruler = Realms.RulerOf(world, realm);
        List<Character>? court = null;
        foreach (var seat in CouncilSeats.All)
        {
            // Holders keep their seat while active and eligible (a member raised to a lordship must step down).
            if (realm.Council.TryGetValue(seat, out var holder) && world.Characters[holder] is { IsActive: true } member
                && member.Id != ruler?.Id && Eligible(realm, seat, member)) continue;
            realm.Council.Remove(seat);
            var seated = realm.Council.Values.ToHashSet();
            court ??= Realms.Domain(world, realm).SelectMany(world.LivingSettlements).SelectMany(s => residents[s.Id]).ToList();
            var candidate = court
                .Where(c => c.Id != ruler?.Id && !seated.Contains(c.Id)
                            && c.Stage(ctx.Date) != LifeStage.Child && Eligible(realm, seat, c))
                .OrderByDescending(c => Aptitude(seat, c)).ThenBy(c => c.Id)
                .FirstOrDefault();
            if (candidate is null) continue;
            realm.Council[seat] = candidate.Id;
            ctx.Emit(new EventDraft("CouncilAppointed",
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["member"] = candidate.Ref },
                Place: world.Settlements[realm.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["seat"] = seat },
                Importance: realm.LiegeId == 0 ? 15 : 8, Tags: new[] { "Politics" }));
        }
    }

    private static bool Eligible(Realm realm, string seat, Character c) => seat switch
    {
        CouncilSeats.CourtMage => c.Role == Role.Mage && realm.MagicLaw != MagicLaw.Outlawed,
        CouncilSeats.HighPriest => c.Role == Role.Priest && c.FaithId == realm.FaithId,
        _ => c.Role is not (Role.Rebel or Role.Lord),
    };

    public static double Aptitude(string seat, Character c) => seat switch
    {
        CouncilSeats.Marshal => c.Attributes.Martial * 2 + c.Attributes.Prowess,
        CouncilSeats.Steward => c.Attributes.Stewardship * 3,
        CouncilSeats.Spymaster => c.Attributes.Intrigue * 3,
        CouncilSeats.CourtMage => c.Attributes.Lore * 3,
        _ => c.Attributes.Lore * 2 + c.Attributes.Diplomacy,
    } + c.Renown / 10;

    private static void Drift(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var ruler = Realms.RulerOf(world, realm);
        var legitimacyTarget = 60.0;
        if (ruler is null) legitimacyTarget -= 45;
        else
        {
            if (ruler.Age(ctx.Date) < Character.AdultAge) legitimacyTarget -= 25;
            if (ruler.FaithId != realm.FaithId) legitimacyTarget -= 10;
            legitimacyTarget += ruler.Renown / 5;
            if (world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == ruler.Id && a.Powers.Any(p => p.Kind == "legitimacy")))
                legitimacyTarget += 15;
        }
        realm.Legitimacy = Math.Clamp(realm.Legitimacy + (legitimacyTarget - realm.Legitimacy) * 0.3, 0, 100);

        var settlements = Realms.Domain(world, realm).SelectMany(world.LivingSettlements).ToList();
        var unrest = settlements.Count > 0 ? settlements.Average(s => s.Unrest) : 0;
        var hungry = Realms.Domain(world, realm).Count(r => r.Conditions.ContainsKey(Conditions.Shortage));
        var stabilityTarget = 30 + realm.Legitimacy * 0.4 - realm.WarWeariness * 0.3 - unrest * 0.3 - hungry * 5;
        realm.Stability = Math.Clamp(realm.Stability + (stabilityTarget - realm.Stability) * 0.3, 0, 100);

        Threshold(ctx, realm, Statecraft.LegitimacyCollapsed, realm.Legitimacy < 25, realm.Legitimacy >= 40,
            "LegitimacyCollapsed", "LegitimacyRestored", LegitimacyCauses(ctx, realm));
        Threshold(ctx, realm, Statecraft.StabilityCollapsed, realm.Stability < 25, realm.Stability >= 40,
            "StabilityCollapsed", "StabilityRestored", StabilityCauses(ctx, realm));
    }

    private static void Threshold(SimContext ctx, Realm realm, string condition, bool begins, bool ends,
        string beginType, string endType, IReadOnlyList<Cause> causes)
    {
        var active = realm.Conditions.TryGetValue(condition, out var began);
        var place = ctx.World.Settlements[realm.CapitalId].Ref;
        if (!active && begins)
            realm.Conditions[condition] = ctx.Emit(new EventDraft(beginType,
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: place, Causes: causes,
                Importance: realm.LiegeId == 0 ? 50 : 25, Tags: new[] { "Politics" })).Id;
        else if (active && ends)
        {
            ctx.Emit(new EventDraft(endType, Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: place,
                Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "Politics" }));
            realm.Conditions.Remove(condition);
        }
    }

    /// <summary>A collapse of legitimacy cites how the current ruler came to power (or the empty seat).</summary>
    private static IReadOnlyList<Cause> LegitimacyCauses(SimContext ctx, Realm realm)
    {
        var capital = ctx.World.Settlements[realm.CapitalId];
        if (capital.Conditions.TryGetValue(Fates.VacantSeat, out var vacancy)) return new[] { new Cause(vacancy, CauseKind.Trigger, 0.9) };
        var accession = ctx.Log.Involving(capital.Ref).LastOrDefault(e => e.Type is "SuccessionOccurred" or "NewLordRose");
        return accession is null ? Array.Empty<Cause>() : new[] { new Cause(accession.Id, CauseKind.Enabler, 0.7) };
    }

    private static IReadOnlyList<Cause> StabilityCauses(SimContext ctx, Realm realm)
    {
        var causes = new List<Cause>();
        if (realm.Conditions.TryGetValue(Statecraft.LegitimacyCollapsed, out var legitimacy)) causes.Add(new Cause(legitimacy, CauseKind.Enabler, 0.8));
        var war = ctx.World.Wars.Values.Where(w => !w.Ended && w.Involves(realm.Id)).OrderBy(w => w.Id).FirstOrDefault();
        if (war is not null) causes.Add(new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.7));
        var famine = Realms.Domain(ctx.World, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
        if (famine != 0) causes.Add(new Cause(famine, CauseKind.Enabler, 0.6));
        return causes;
    }

    private static void ConsiderIndependence(SimContext ctx, Realm vassal)
    {
        var world = ctx.World;
        var liege = world.Realms[vassal.LiegeId];
        var ruler = Realms.RulerOf(world, vassal);
        var overlord = Realms.RulerOf(world, liege);
        var causes = new List<Cause>();
        if (liege.Conditions.TryGetValue(Statecraft.StabilityCollapsed, out var collapse)) causes.Add(new Cause(collapse, CauseKind.Enabler, 0.8));
        if (ruler is not null && overlord is not null && Relations.Opinion(ruler, overlord) <= Relations.RivalAt)
        {
            var grudge = ruler.Memories.Where(m => m.About == overlord.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null) causes.Add(new Cause(grudge.EventId, CauseKind.Motive, 0.9));
        }
        if (overlord is not null && Fates.Accession(ctx.Log, overlord) is { } accession)
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.4));
        var unhappy = liege.Conditions.ContainsKey(Statecraft.StabilityCollapsed)
                      || (ruler is not null && overlord is not null && Relations.Opinion(ruler, overlord) <= Relations.RivalAt);
        if (!unhappy || !ctx.Rng.Chance(IndependencePerYear)) return;

        vassal.LiegeId = 0;
        var declared = ctx.Emit(new EventDraft("IndependenceDeclared",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = vassal.Ref, ["former liege"] = liege.Ref },
            Place: world.Settlements[vassal.CapitalId].Ref, Causes: causes, Importance: 55, Tags: new[] { "Politics" }));
        Statecraft.Remember(liege, declared.Id, vassal.Id, -70, 90);
    }

    private static void Rerank(SimContext ctx, Realm realm)
    {
        var rank = Realms.RankFor(ctx.World, realm);
        if (rank == realm.Rank) return;
        var rose = rank > realm.Rank;
        realm.Rank = rank;
        ctx.Emit(new EventDraft(rose ? "RealmRose" : "RealmDiminished",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["rank"] = rank.ToString() },
            Importance: rank == RealmRank.Empire && rose ? 85 : realm.LiegeId == 0 ? 50 : 25, Tags: new[] { "Politics" }));
    }

    private void ReviewPhase(SimContext ctx, Realm realm)
    {
        var size = Realms.Territory(ctx.World, realm).Count();
        var phase = size > realm.RegionsAtLastReview ? RealmPhase.Expanding
            : size < realm.RegionsAtLastReview ? RealmPhase.Declining
            : realm.Phase is RealmPhase.Founded or RealmPhase.Expanding ? RealmPhase.Stable
            : realm.Phase;
        realm.RegionsAtLastReview = size;
        if (phase == realm.Phase || !EraWords.TryGetValue(phase, out var words)) return;
        var quietStart = realm.Phase == RealmPhase.Founded && phase == RealmPhase.Stable;
        realm.Phase = phase;
        if (quietStart) return;
        var era = $"The {(phase == RealmPhase.Stable ? "Age of " : "")}{words[ctx.Rng.NextInt(0, words.Length)]}{(phase == RealmPhase.Stable ? "" : " Age")} of {realm.Name}";
        var invention = _inventor.Invent(new InventionBrief("era", $"{realm.Ref}@{ctx.Date.Year}",
            $"{Statecraft.Title(realm)} enters a time of {phase.ToString().ToLowerInvariant()} fortunes in year {ctx.Date.Year}; "
            + $"it holds {size} region(s) and its legitimacy stands at {realm.Legitimacy.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}/100.", era));
        era = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        ctx.Emit(new EventDraft("EraBegan",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["era"] = era, ["phase"] = phase.ToString() },
            Importance: 45, Tags: new[] { "Politics", "Era" }));
    }

    /// <summary>A lost capital cites the latest cession or abandonment there.</summary>
    private static IReadOnlyList<Cause> CapitalLossCauses(SimContext ctx, Realm realm)
    {
        var capital = ctx.World.Settlements[realm.CapitalId];
        var loss = ctx.Log.Involving(capital.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned")
                   ?? ctx.Log.Involving(ctx.World.Regions[capital.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded");
        return loss is null ? Array.Empty<Cause>() : new[] { new Cause(loss.Id, CauseKind.Trigger, 1.0) };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SuccessCriteriaTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 456` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "test(sim): check the sim-core success criteria on a real world

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Benchmarks, CI and README

Spec §7 and §10: at most 2 s per world year at 120 regions, rules only. `FantasyWorld.Bench` measures one world year on medium and large worlds with 50 years of history; every test run also guards the budget coarsely, and CI runs the benchmark's short job on each push (its output folder is ignored by git). The README documents the chronicle commands.

**Files:**
- Create: `sim/FantasyWorld.Bench/FantasyWorld.Bench.csproj`, `sim/FantasyWorld.Bench/Program.cs`, `sim/FantasyWorld.Bench/WorldYearBenchmark.cs`
- Modify: `.github/workflows/ci.yml`, `.gitignore`, `README.md`, `sim/FantasyWorld.sln`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/PerformanceTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: project `sim/FantasyWorld.Bench` (`BenchmarkDotNet` 0.14.0; `WorldYearBenchmark`); `PerformanceTests`; `.github/workflows/ci.yml` job `bench`; `.gitignore` for `BenchmarkDotNet.Artifacts/`; README section "The chronicle".

- [ ] **Step 1: Write the checks**

`sim/FantasyWorld.Sim.Tests/Engine/PerformanceTests.cs`:
```csharp
using System.Diagnostics;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>
/// Spec §7: at most 2 s per world year at 120 regions with the LLM faked (here: offline rules). A coarse guard for
/// every test run; precise numbers come from the BenchmarkDotNet project (sim/FantasyWorld.Bench).
/// </summary>
public class PerformanceTests
{
    [Fact]
    public void ALargeWorldWithHistory_SimulatesAYearWellInsideTheBudget()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Large), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 20);
        var watch = Stopwatch.StartNew();
        sim.AdvanceDays(5 * 360);
        var perYear = watch.Elapsed / 5;
        Assert.True(perYear < TimeSpan.FromSeconds(2), $"{perYear.TotalMilliseconds:0} ms per world year");
    }
}
```

- [ ] **Step 2: Run tests to verify they pass against the finished stack**

Run: `dotnet test sim`
Expected: PASS. These tests pin down behaviour built in the earlier tasks; if one fails, fix the system it names before going on.

- [ ] **Step 3: Implement**

Replace `.github/workflows/ci.yml` entirely with:
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

  bench:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Benchmark one world year (short job)
        run: dotnet run --configuration Release --project sim/FantasyWorld.Bench -- --filter '*' --job short
```

Replace `.gitignore` entirely with:
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

# BenchmarkDotNet output
BenchmarkDotNet.Artifacts/
```

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
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/aerenmoor.fw.db       # living notables
dotnet run --project sim/FantasyWorld.Cli -- life worlds/aerenmoor.fw.db <char-id>    # a biography
dotnet run --project sim/FantasyWorld.Cli -- artifacts worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- artifact worlds/aerenmoor.fw.db <id>     # powers and provenance
dotnet run --project sim/FantasyWorld.Cli -- realms worlds/aerenmoor.fw.db           # kingdoms and their vassals
dotnet run --project sim/FantasyWorld.Cli -- realm worlds/aerenmoor.fw.db <id>        # laws, council, treaties, eras
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- war worlds/aerenmoor.fw.db <id>          # casus belli and the course of the war
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db --layer realms
dotnet run --project sim/FantasyWorld.Cli -- tell worlds/aerenmoor.fw.db <event-id>    # the why-chain as prose
dotnet run --project sim/FantasyWorld.Cli -- stats worlds/aerenmoor.fw.db             # world summary and LLM spend
```

## Story beats from Claude

The world runs fully offline by default: rules make every choice and templates tell the story. To let Claude
decide notables' choices, arbitrate duels, hunts and delves, name forged artifacts, wars and eras, and narrate
with `fw tell`:

```bash
export ANTHROPIC_API_KEY=...        # never stored in the world file
dotnet run --project sim/FantasyWorld.Cli -- llm worlds/aerenmoor.fw.db --provider anthropic --cap 5
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 2y
```

Defaults: `claude-opus-5-5` for every job (`--model narrate=<model>` changes one), 60 story beats and 200
narrations per rolling day, and a $10 cap per 30 days; past it, or without credentials, the rules take over.
`--provider openrouter` uses `OPENROUTER_API_KEY` instead. Every answer is journaled in the world file, so
reloading or replaying a world never asks again, and `fw why` shows the motives and accounts it gave.

`--size tiny` makes a 64×64 world with 12 regions for quick experiments (and no prehistory; medium and large
worlds start with 100 years of it, `--prehistory <years>` to change).

## The chronicle

```bash
dotnet run --project sim/FantasyWorld.Cli -- digest worlds/aerenmoor.fw.db              # headlines since you last looked
dotnet run --project sim/FantasyWorld.Cli -- follow worlds/aerenmoor.fw.db char#12      # their deeds lead the digest
dotnet run --project sim/FantasyWorld.Cli -- live worlds/aerenmoor.fw.db --minutes 30   # watch it unfold in real time
dotnet run --project sim/FantasyWorld.Cli -- structures worlds/aerenmoor.fw.db          # landmarks, labyrinths and ruins
```

`catchup` ends with the digest. Minor events older than 50 years are condensed into one summary per decade as
the world runs. Every command that changes a world first keeps a
backup of the last three sessions next to it (`.bak1`–`.bak3`). Benchmarks:
`dotnet run -c Release --project sim/FantasyWorld.Bench -- --filter '*'`.
````

`sim/FantasyWorld.Bench/FantasyWorld.Bench.csproj`:
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FantasyWorld.Sim\FantasyWorld.Sim.csproj" />
  </ItemGroup>

</Project>
```

`sim/FantasyWorld.Bench/Program.cs`:
```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(FantasyWorld.Bench.WorldYearBenchmark).Assembly).Run(args);
```

`sim/FantasyWorld.Bench/WorldYearBenchmark.cs`:
```csharp
using BenchmarkDotNet.Attributes;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Bench;

/// <summary>
/// Spec §7 performance target: at most 2 s per world year at 120 regions, rules only. Each iteration simulates one
/// year of a large world that already has 50 years of history (so the cast and the log are at working size).
/// </summary>
[MemoryDiagnoser]
public class WorldYearBenchmark
{
    private byte[] _start = Array.Empty<byte>();
    private IReadOnlyList<WorldEvent> _history = Array.Empty<WorldEvent>();
    private Sim.Model.Geography? _geography;
    private Simulator _sim = null!;

    [Params(WorldSize.Medium, WorldSize.Large)]
    public WorldSize Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, Size), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 50);
        _start = Packed.Pack(WorldSnapshot.Capture(world, sim.Queue));
        _history = sim.Log.All.ToList();
        _geography = world.Geography;
    }

    [IterationSetup]
    public void Fresh()
    {
        var snapshot = Packed.Unpack<WorldSnapshot>(_start)!;
        var world = snapshot.ToWorld();
        world.Geography = _geography;
        var log = new EventLog();
        foreach (var ev in _history) log.Restore(ev);
        _sim = new Simulator(world, log, snapshot.ToQueue(), DefaultSystems.Create());
    }

    [Benchmark]
    public void OneWorldYear() => _sim.AdvanceDays(WorldDate.DaysPerYear);
}
```

Replace `sim/FantasyWorld.sln` entirely with:
```
﻿
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Sim", "FantasyWorld.Sim\FantasyWorld.Sim.csproj", "{B0F3A6A4-AE75-4BD0-95A2-51FD45D41112}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Sim.Tests", "FantasyWorld.Sim.Tests\FantasyWorld.Sim.Tests.csproj", "{BE71F3E6-FA70-47EC-AB3C-D9670BDCE8D4}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Cli", "FantasyWorld.Cli\FantasyWorld.Cli.csproj", "{EEA819AC-4011-45F4-86F4-6CBDAB6DE238}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Llm", "FantasyWorld.Llm\FantasyWorld.Llm.csproj", "{EB6EE2F2-B58F-45DA-B673-EA6964D2B3DA}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Bench", "FantasyWorld.Bench\FantasyWorld.Bench.csproj", "{F4A34627-0F30-4F80-80E5-6CC8DFA87619}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{B0F3A6A4-AE75-4BD0-95A2-51FD45D41112}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B0F3A6A4-AE75-4BD0-95A2-51FD45D41112}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B0F3A6A4-AE75-4BD0-95A2-51FD45D41112}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B0F3A6A4-AE75-4BD0-95A2-51FD45D41112}.Release|Any CPU.Build.0 = Release|Any CPU
		{BE71F3E6-FA70-47EC-AB3C-D9670BDCE8D4}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{BE71F3E6-FA70-47EC-AB3C-D9670BDCE8D4}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{BE71F3E6-FA70-47EC-AB3C-D9670BDCE8D4}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{BE71F3E6-FA70-47EC-AB3C-D9670BDCE8D4}.Release|Any CPU.Build.0 = Release|Any CPU
		{EEA819AC-4011-45F4-86F4-6CBDAB6DE238}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{EEA819AC-4011-45F4-86F4-6CBDAB6DE238}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{EEA819AC-4011-45F4-86F4-6CBDAB6DE238}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{EEA819AC-4011-45F4-86F4-6CBDAB6DE238}.Release|Any CPU.Build.0 = Release|Any CPU
		{EB6EE2F2-B58F-45DA-B673-EA6964D2B3DA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{EB6EE2F2-B58F-45DA-B673-EA6964D2B3DA}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{EB6EE2F2-B58F-45DA-B673-EA6964D2B3DA}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{EB6EE2F2-B58F-45DA-B673-EA6964D2B3DA}.Release|Any CPU.Build.0 = Release|Any CPU
		{F4A34627-0F30-4F80-80E5-6CC8DFA87619}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{F4A34627-0F30-4F80-80E5-6CC8DFA87619}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{F4A34627-0F30-4F80-80E5-6CC8DFA87619}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{F4A34627-0F30-4F80-80E5-6CC8DFA87619}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PerformanceTests"`
Expected: `Passed!  - Failed: 0, Passed: 1`. Then `dotnet test sim` → `Passed: 457` in total.

- [ ] **Step 5: Run the full suite in Release**

Run: `dotnet test sim --configuration Release`
Expected: `Passed!  - Failed: 0, Passed: 457`

- [ ] **Step 6: Commit**

```bash
git add sim README.md .github
git commit -m "perf: add BenchmarkDotNet world-year benchmark, CI job and chronicle docs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §5 world generation: ~100-year prehistory | 3 |
| §5 structures: landmark, labyrinth, ruin; `LandmarkCompleted`, `LabyrinthOpened`, `StructureRuined`; labyrinths raise danger and hold lost artifacts | 7 |
| §7 modes: live ticking, clock regression waits | 4 |
| §7 importance (fame, scale, followed) and the digest grouped by season | 1–2 |
| §7 compaction of minor events older than 50 years, keeping causes, deterministic | 5 |
| §7 performance target ≤ 2 s per world year at 120 regions | 9 |
| §9 compressed snapshot, `followed` table, rolling backups of the last 3 sessions | 1, 6 |
| §10 BenchmarkDotNet, CI | 9 |
| §11 criteria 1–3 and 5 checked automatically (criterion 2's LLM-driven beat needs credentials; criterion 4 is Plan 3's) | 8 |

With this plan the sim-core sub-project's spec is implemented. Deferred to later sub-projects: rich chronicle views (2), depth of myth, magic and structures (3), the Godot client (4), and live-LLM evals and prompt tuning (5).
