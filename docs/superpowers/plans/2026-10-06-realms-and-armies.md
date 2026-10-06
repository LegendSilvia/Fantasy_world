# Realms & Armies Implementation Plan (Plan 4 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the world its politics. Lords become rulers of realms (counties, duchies, kingdoms, empires) with a government, succession and magic law, a treasury, a council, legitimacy and stability. Realms ally, feud and go to war for reasons history gives them, each war citing the event behind its casus belli. Armies are raised, march, fight battles, besiege and occupy, and wars end in peace terms that follow the casus belli. Realms rise, decline and fall, and each change opens a named era. `fw why` on a knight's battle death walks back through the battle and the war to the theft, killing or lost land that started it.

**Architecture:** New entities (`Realm`, `War`, `Army`) live in `World` and the snapshot. A realm's ruler is never stored: it is the lord of its capital, so Plan 3's succession passes crowns without new code paths (Task 4 only chooses *who* by law). World generation founds kingdoms around cities and vassal counties around towns. Three new systems run after the land and before the characters: `Realms` (taxes, councils, legitimacy, stability, independence, rank, eras), `Diplomacy` (opinion, alliances, rivalries, casus belli, war declarations) and `Military` (armies, campaigns, battles, sieges, occupation, peace; one partial class in four files). Systems still never call each other: shared realm consequences (moving a capital, a realm's fall, ceding regions) live in the `Statecraft` helper library, and death still goes through `Fates.Die`. War declarations and peace terms are `IDecider` decision points; battle fates of notables go to the `IArbiter`.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite 8.0.10, System.Text.Json (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` (§6, §7, §10). **Builds on:** Plans 1–3 (merged).

## Global Constraints

Everything in Plans 1–3's Global Constraints still applies. In addition:

- **One source of truth per fact:** a realm's ruler is `Realms.RulerOf` (the capital's lord); a region's realm is `Region.OwnerRealmId` (occupation is a separate `OccupierRealmId`); a realm's rank, territory and vassals are derived by the `Realms` queries.
- **Every war cites its casus belli event**, every peace cites the war, every battle and siege cites the war. Collapse and recovery of legitimacy and stability are condition events citing their causes.
- **Realm consequences go through `Statecraft`**, death through `Fates.Die`. No system edits another system's state any other way.
- **Armies borrow levies:** raising an army takes people from settlements and disbanding returns the survivors, so no one is created or lost off the books.
- **Realm costs stay flat:** per-tick work is per realm, per war or per army, never per character × character.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Starting realms | One kingdom per city (tiny worlds without two cities crown their two largest towns); towns outside capital regions become vassal counties | Spec: 6–8 starting realms; medium worlds have 6–9 cities |
| Ranks | County < 3 regions, Duchy < 8, Kingdom, Empire at 20+ regions with two kingdom-sized vassals | Rank follows size and changes as land changes hands |
| Regency | No separate regent: a child ruler costs legitimacy and the marshal (else a knight) commands the army | Keeps one source of truth for rulership; a regent title can be added later |
| Casus belli | Claim (lost land), holy war, revenge (a killing), reconquest, resource need, artifact recovery, independence; each found in the event log | Spec §6; every war has a citable reason |
| War score | Occupation (capitals count more) plus battles, −100…100; decisive at ±50, white peace after 5 years, forced end after 10 | Wars end; long stalemates don't freeze the map |
| Armies | One army per realm per war, raised at the capital, marching 30 travel days a month | Simple and legible on the map; spec's "armies" without micro-management |
| Trade between regions | Deferred | The economy's in-region trade (Plan 2) suffices for now; inter-region trade routes come with sub-project 3 |

**Prototype soaks (medium worlds, seeds 1, 7 and 42, 200 years each, invariants checked every day):** no violations. Seed 1 fought 40 wars (24 revenge, 11 independence, 2 resource need, 2 artifact recovery, 1 claim) and ended them in terms such as "vassalage restored", "independence", "return of Neithroll's Locket; 1 region(s) ceded" and white peaces; 7 realms fell or rose, 61 regions changed hands and 67 eras began. Seed 7 fought 47 wars with 113 battles and 101 cessions; seed 42, a quieter map, 10. The cast stays near 700–760 living notables. Cost is about 60–75 ms per world year with daily invariant checks on (three soaks running at once), flat after year 50. An independent review of the prototype found seven defects (independence terms reversed, beaten armies never regrouping, village capitals left without rulers, a crash on tiny worlds, allies of both sides, council cost growing with history, lords keeping council seats); all are fixed in the code below and covered by tests. The code in this plan was replayed task by task in this order: each task's tests fail first and pass after the implementation, with the counts given.

## Roadmap (unchanged)

| Plan | Delivers |
|---|---|
| 5 — LLM layer | LLM `IDecider`/`IArbiter`, invention (names, lore, backstories), narration, budgets, cost ledger |
| 6 — Chronicle & hardening | importance + digest, prehistory (origins for ancient artifacts and houses), live mode, compaction of the dead, snapshot compression, backups, BenchmarkDotNet |

## File Structure

```
sim/
  FantasyWorld.Sim/
    Model/Realm.cs, War.cs, Realms.cs                                                  (Task 1)
    Model/Region.cs, World.cs; Persist/WorldSnapshot.cs (v4); Engine/Invariants.cs     (Task 1, modify)
    Engine/Invariants.Realms.cs                                                        (Task 1)
    WorldGen/RealmGenerator.cs                                                         (Task 2)
    WorldGen/WorldGenerator.cs                                                         (Task 2, modify)
    Systems/Statecraft.cs, RealmSystem.cs                                              (Task 3)
    Systems/Fates.cs                     council cleanup; succession laws              (Tasks 3–4, modify)
    Systems/DiplomacySystem.cs                                                         (Task 5)
    Systems/MilitarySystem.cs, .Battles.cs, .Sieges.cs, .Peace.cs                      (Tasks 6–9)
    Systems/DefaultSystems.cs; Query/HistoryQuery.cs (+ Eras)                          (Task 10, modify)
  FantasyWorld.Cli/CliApp.Realms.cs; CliApp.cs, MapRenderer.cs                         (Task 10)
  FantasyWorld.Sim.Tests/
    Support/TestRealms.cs; Model/RealmModelTests.cs; WorldGen/RealmGeneratorTests.cs;
    Systems/{RealmSystem,SuccessionLaw,DiplomacySystem,Campaign,Battle,Siege,Peace}Tests.cs; Engine/RealmHealthTests.cs
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: Realm, war and army model

All of this plan's entities, persisted from the start (snapshot v4) and guarded by invariants, so later tasks only add behaviour. A realm's ruler is never stored: it is whoever is lord of the capital, so Plan 3's succession automatically passes crowns. Territory is the regions a realm owns directly; vassals hang below a liege, and the top of each chain is an independent realm.

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/Invariants.Realms.cs`, `sim/FantasyWorld.Sim/Model/Realm.cs`, `sim/FantasyWorld.Sim/Model/Realms.cs`, `sim/FantasyWorld.Sim/Model/War.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Model/Region.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Model/RealmModelTests.cs`, `sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs`

**Interfaces:**
- Consumes: `World`, `Region`, `Settlement`, `Character`, `WorldSnapshot`, `Invariants` (Plans 1–3).
- Produces: enums `RealmRank { County, Duchy, Kingdom, Empire }`, `Government`, `SuccessionLaw { Primogeniture, Elective, Seniority }`, `MagicLaw`, `RealmPhase { Founded, Expanding, Stable, Declining, Collapsed }`, `TreatyKind { Alliance, NonAggression, TradePact, Tribute, Rivalry }`; `record Treaty(Kind, With, SinceEventId, UntilDay)`; `record RealmMemory(EventId, About, Feeling, Weight)`; `CouncilSeats` (marshal, steward, spymaster, court mage, high priest); `Realm` (kind `"realm"`: capital, liege, rank, government, laws, faith and culture, treasury, legitimacy, stability, war weariness, phase and era, council, conditions, treaties, memories, colours); `CasusBelli` constants; `War` (kind `"war"`: sides, casus belli event, target region or artifact, score, battles; `IsAttacker`, `Involves`, `LeaderOf`, `EnemiesOf`); `ArmyObjective { Defend, Siege, Return }`; `Army` (kind `"army"`); static `Realms` queries (`RulerOf`, `Top`, `Independent`, `VassalsOf`, `Domain`, `Territory`, `SovereignOf`, `Neighbours`, `IsBelow`, `RankFor`); `Region.OwnerRealmId/OccupierRealmId/BesiegerArmyId/SiegeProgress`; `World.Realms/Wars/Armies`; `WorldSnapshot` v4; `Invariants.Realms.cs`. Test support: `TestRealms.TwoKingdoms()`, `AddRealm`, `DeclareWar`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Model/RealmModelTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class RealmModelTests
{
    [Fact]
    public void TwoKingdoms_SatisfyInvariants()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Empty(Invariants.Check(world, new EventLog()));
        Assert.Equal(2, Realms.Independent(world).Count());
    }

    [Fact]
    public void Ruler_IsTheLordOfTheCapital()
    {
        var world = TestRealms.TwoKingdoms();
        var realm = world.Realms[1];
        Assert.Equal(world.Settlements[1].LordId, Realms.RulerOf(world, realm)!.Id);
        world.Characters[world.Settlements[1].LordId].Role = Role.None;
        world.Settlements[1].LordId = 0;
        Assert.Null(Realms.RulerOf(world, realm));
    }

    [Fact]
    public void Vassals_AddToTerritory_AndTopIsTheIndependentLiege()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[2].LiegeId = 1;
        Assert.Equal(1, Realms.Top(world, world.Realms[2]).Id);
        Assert.Equal(new long[] { 1, 2 }, Realms.Territory(world, world.Realms[1]).Select(r => r.Id));
        Assert.Single(Realms.Domain(world, world.Realms[1]));
        Assert.Equal(1, Realms.SovereignOf(world, world.Regions[2])!.Id);
        Assert.Single(Realms.Independent(world));
    }

    [Fact]
    public void Neighbours_AreFoundAcrossTheGraph()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Equal(2, Realms.Neighbours(world, world.Realms[1]).Single().Id);
    }

    [Fact]
    public void Invariants_CatchBrokenRealms()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].LiegeId = 2;
        world.Realms[2].LiegeId = 1;
        world.Regions[1].OwnerRealmId = 2;
        world.Realms[2].Council[CouncilSeats.Marshal] = 999;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("cycle"));
        Assert.Contains(errors, e => e.Contains("outside its domain"));
        Assert.Contains(errors, e => e.Contains("inactive marshal"));
    }

    [Fact]
    public void Snapshot_RoundTripsRealmsWarsAndArmies()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].Treaties.Add(new Treaty(TreatyKind.Alliance, 2, 0, 0));
        world.Realms[1].Council[CouncilSeats.Steward] = world.Settlements[1].LordId;
        world.Wars[1] = new War { Id = 1, Name = "The Border War", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 }, CasusBelli = CasusBelli.Revenge };
        world.Armies[1] = new Army { Id = 1, RealmId = 1, WarId = 1, Levies = 300, RegionId = 1, Path = { 2 }, RaisedFrom = { [1] = 300 } };
        var queue = new EventQueue();
        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options);
        var copy = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!.ToWorld();
        Assert.Equal(json, JsonSerializer.Serialize(WorldSnapshot.Capture(copy, queue), SimJson.Options));
        Assert.True(copy.Realms[1].Has(TreatyKind.Alliance, 2));
        Assert.Equal(300, copy.Armies[1].RaisedFrom[1]);
        Assert.Same(StringComparer.Ordinal, copy.Realms[1].Council.Comparer);
    }

    [Fact]
    public void Rank_FollowsSize()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Equal(RealmRank.County, Realms.RankFor(world, world.Realms[1]));
    }
}
```

`sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs`:
```csharp
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Two neighbouring kingdoms on TestPeople's two-region world, each ruled by its capital's lord.</summary>
public static class TestRealms
{
    public static World TwoKingdoms(ulong seed = 1)
    {
        var world = TestPeople.World(seed);
        foreach (var settlement in world.Settlements.Values)
        {
            var lord = TestPeople.Add(world, settlement.Id, 40, role: Role.Lord);
            lord.Attributes.Martial = 12;
            AddRealm(world, settlement.Id, $"Realm of {settlement.Name}");
        }
        return world;
    }

    public static Realm AddRealm(World world, long capitalId, string name, long liegeId = 0)
    {
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = name,
            CapitalId = capitalId,
            LiegeId = liegeId,
            Legitimacy = 70,
            Stability = 70,
            Treasury = 500,
            CultureId = 1,
            FaithId = 1,
        };
        world.Realms[realm.Id] = realm;
        world.Regions[world.Settlements[capitalId].RegionId].OwnerRealmId = realm.Id;
        return realm;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'Realm' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Engine/Invariants.Realms.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckRealms(World world, EventLog log, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.OwnerRealmId != 0 && (!world.Realms.TryGetValue(region.OwnerRealmId, out var owner) || owner.Destroyed))
                errors.Add($"Region #{region.Id} is owned by missing or destroyed realm #{region.OwnerRealmId}.");
            if (region.OccupierRealmId != 0 && !world.Realms.ContainsKey(region.OccupierRealmId))
                errors.Add($"Region #{region.Id} is occupied by unknown realm #{region.OccupierRealmId}.");
            if (region.BesiegerArmyId != 0 && (!world.Armies.TryGetValue(region.BesiegerArmyId, out var besieger) || besieger.Disbanded))
                errors.Add($"Region #{region.Id} is besieged by a missing army #{region.BesiegerArmyId}.");
        }

        foreach (var (id, realm) in world.Realms)
        {
            var name = $"Realm #{id}";
            if (realm.Id != id) errors.Add($"{name} is stored under the wrong key ({realm.Id}).");
            if (realm.Destroyed) continue;
            if (!world.Settlements.TryGetValue(realm.CapitalId, out var capital))
                errors.Add($"{name} has unknown capital #{realm.CapitalId}.");
            else if (world.Regions[capital.RegionId].OwnerRealmId != id)
                errors.Add($"{name}'s capital #{capital.Id} lies outside its domain.");
            if (realm.LiegeId != 0 && (!world.Realms.TryGetValue(realm.LiegeId, out var liege) || liege.Destroyed))
                errors.Add($"{name} has a missing or destroyed liege #{realm.LiegeId}.");
            if (realm.LiegeId != 0 && Realms.IsBelow(world, world.Realms[realm.LiegeId], realm))
                errors.Add($"{name} is its own liege's liege (cycle).");
            if (!(realm.Legitimacy >= 0 && realm.Legitimacy <= 100) || !(realm.Stability >= 0 && realm.Stability <= 100))
                errors.Add($"{name} has legitimacy or stability outside 0–100.");
            foreach (var (seat, member) in realm.Council)
                if (!world.Characters.TryGetValue(member, out var c) || !c.IsActive)
                    errors.Add($"{name} has an inactive {seat} #{member}.");
            foreach (var treaty in realm.Treaties)
                if (!world.Realms.TryGetValue(treaty.With, out var other) || other.Destroyed)
                    errors.Add($"{name} has a treaty with missing or destroyed realm #{treaty.With}.");
            foreach (var condition in realm.Conditions.Values.Concat(realm.Memories.Select(m => m.EventId)))
                if (!log.TryGet(condition, out _)) errors.Add($"{name} cites missing event #{condition}.");
        }

        foreach (var (id, war) in world.Wars)
        {
            if (!log.TryGet(war.CasusBelliEventId, out _)) errors.Add($"War #{id} has no casus belli event.");
            if (!war.Ended && war.Attackers.Concat(war.Defenders).Any(r => !world.Realms.TryGetValue(r, out var realm) || realm.Destroyed))
                errors.Add($"War #{id} involves a missing or destroyed realm.");
        }

        foreach (var (id, army) in world.Armies)
        {
            if (army.Disbanded) continue;
            var name = $"Army #{id}";
            if (!world.Wars.TryGetValue(army.WarId, out var war) || war.Ended) errors.Add($"{name} serves a missing or ended war.");
            if (!world.Regions.ContainsKey(army.RegionId)) errors.Add($"{name} stands in unknown region #{army.RegionId}.");
            if (army.Levies < 0 || army.MenAtArms < 0 || army.Mages < 0) errors.Add($"{name} has negative troops.");
            if (army.CommanderId != 0 && (!world.Characters.TryGetValue(army.CommanderId, out var commander) || !commander.IsActive))
                errors.Add($"{name} is led by an inactive commander #{army.CommanderId}.");
            foreach (var knight in army.Knights)
                if (!world.Characters.TryGetValue(knight, out var k) || !k.IsActive) errors.Add($"{name} carries an inactive knight #{knight}.");
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
        return errors;
    }

    static partial void CheckRealms(World world, EventLog log, List<string> errors);

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

`sim/FantasyWorld.Sim/Model/Realm.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Rank follows size: counties hold a few regions, empires many plus kingdom-sized vassals.</summary>
public enum RealmRank { County, Duchy, Kingdom, Empire }

public enum Government { FeudalMonarchy, ElectiveKingdom, Theocracy, Magocracy, MerchantRepublic, Tribal }

/// <summary>Who inherits the ruler's seat: eldest child, the court's ablest, or the eldest sibling first.</summary>
public enum SuccessionLaw { Primogeniture, Elective, Seniority }

public enum MagicLaw { Embraced, Licensed, Outlawed }

/// <summary>Lifecycle (spec §6); each change of phase opens a named era.</summary>
public enum RealmPhase { Founded, Expanding, Stable, Declining, Collapsed }

public enum TreatyKind { Alliance, NonAggression, TradePact, Tribute, Rivalry }

/// <summary>A standing agreement with another realm. UntilDay 0 means open-ended.</summary>
public sealed record Treaty(TreatyKind Kind, long With, long SinceEventId, long UntilDay);

/// <summary>A realm-level memory: grudges outlive rulers. About is the other realm.</summary>
public sealed record RealmMemory(long EventId, long About, double Feeling, double Weight);

public static class CouncilSeats
{
    public const string Marshal = "marshal";
    public const string Steward = "steward";
    public const string Spymaster = "spymaster";
    public const string CourtMage = "court mage";
    public const string HighPriest = "high priest";

    public static readonly IReadOnlyList<string> All = new[] { Marshal, Steward, Spymaster, CourtMage, HighPriest };
}

/// <summary>
/// A political realm (spec §6). Its ruler is the lord of its capital settlement (derived, never stored).
/// It holds regions directly (Region.OwnerRealmId) and indirectly through vassal realms.
/// </summary>
public sealed class Realm
{
    public const string Kind = "realm";

    private SortedDictionary<string, long> _council = new(StringComparer.Ordinal);
    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; set; } = "";
    public RealmRank Rank { get; set; }
    public long CapitalId { get; set; }
    public long LiegeId { get; set; }
    public Government Government { get; set; }
    public SuccessionLaw SuccessionLaw { get; set; }
    public MagicLaw MagicLaw { get; set; }
    public double Legitimacy { get; set; }
    public double Stability { get; set; }
    public double Treasury { get; set; }
    public double WarWeariness { get; set; }
    public long CultureId { get; set; }
    public long FaithId { get; set; }
    public string[] Colors { get; init; } = { "grey", "white" };
    public long FoundedEventId { get; init; }
    public RealmPhase Phase { get; set; }

    /// <summary>Directly held regions at the last yearly review (for expanding/declining phases).</summary>
    public int RegionsAtLastReview { get; set; }

    public bool Destroyed { get; set; }

    /// <summary>Seat name → character id.</summary>
    public SortedDictionary<string, long> Council
    {
        get => _council;
        init => _council = Sorted.From(value);
    }

    public List<Treaty> Treaties { get; init; } = new();
    public List<RealmMemory> Memories { get; init; } = new();

    /// <summary>Active conditions ("legitimacyCollapsed", …) mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool Has(TreatyKind kind, long with) => Treaties.Any(t => t.Kind == kind && t.With == with);
}
```

`sim/FantasyWorld.Sim/Model/Realms.cs`:
```csharp
namespace FantasyWorld.Sim.Model;

/// <summary>Queries over realms: rulers, hierarchy, territory and neighbours.</summary>
public static class Realms
{
    /// <summary>The ruler is whoever is lord of the capital (null during an interregnum).</summary>
    public static Character? RulerOf(World world, Realm realm) =>
        world.Settlements.TryGetValue(realm.CapitalId, out var capital) && capital.LordId != 0
            ? world.Characters[capital.LordId]
            : null;

    /// <summary>The independent realm at the top of the realm's chain of lieges.</summary>
    public static Realm Top(World world, Realm realm)
    {
        var current = realm;
        for (var guard = 0; current.LiegeId != 0 && guard < 64; guard++) current = world.Realms[current.LiegeId];
        return current;
    }

    public static IEnumerable<Realm> Independent(World world) =>
        world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == 0);

    public static IEnumerable<Realm> VassalsOf(World world, Realm realm) =>
        world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == realm.Id);

    /// <summary>Regions held directly.</summary>
    public static IEnumerable<Region> Domain(World world, Realm realm) =>
        world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id);

    /// <summary>Regions held directly or through vassals, at any depth.</summary>
    public static IEnumerable<Region> Territory(World world, Realm realm)
    {
        var members = new HashSet<long> { realm.Id };
        foreach (var r in world.Realms.Values.Where(r => !r.Destroyed))
            if (IsBelow(world, r, realm)) members.Add(r.Id);
        return world.Regions.Values.Where(r => members.Contains(r.OwnerRealmId));
    }

    /// <summary>The independent realm a region ultimately belongs to (null if unclaimed).</summary>
    public static Realm? SovereignOf(World world, Region region) =>
        world.Realms.TryGetValue(region.OwnerRealmId, out var owner) ? Top(world, owner) : null;

    /// <summary>Independent realms whose territory touches this realm's territory on the region graph.</summary>
    public static IEnumerable<Realm> Neighbours(World world, Realm realm)
    {
        var graph = world.Geography?.Graph;
        if (graph is null) return Array.Empty<Realm>();
        var ids = new SortedSet<long>();
        foreach (var region in Territory(world, realm))
            foreach (var n in graph.Neighbours(region.Id))
                if (SovereignOf(world, world.Regions[n]) is { } other && other.Id != realm.Id) ids.Add(other.Id);
        return ids.Select(id => world.Realms[id]);
    }

    public static bool IsBelow(World world, Realm realm, Realm liege)
    {
        var current = realm;
        for (var guard = 0; current.LiegeId != 0 && guard < 64; guard++)
        {
            if (current.LiegeId == liege.Id) return true;
            current = world.Realms[current.LiegeId];
        }
        return false;
    }

    /// <summary>The independent realm a settlement belongs to.</summary>
    public static Realm? SovereignOf(World world, Settlement settlement) => SovereignOf(world, world.Regions[settlement.RegionId]);

    /// <summary>Rank by size: County &lt; 3 regions, Duchy &lt; 8, Kingdom otherwise; Empire with 20+ regions and 2+ kingdom-sized vassals.</summary>
    public static RealmRank RankFor(World world, Realm realm)
    {
        var size = Territory(world, realm).Count();
        if (size >= 20 && VassalsOf(world, realm).Count(v => Territory(world, v).Count() >= 8) >= 2) return RealmRank.Empire;
        return size switch { >= 8 => RealmRank.Kingdom, >= 3 => RealmRank.Duchy, _ => RealmRank.County };
    }
}
```

Replace `sim/FantasyWorld.Sim/Model/Region.cs` entirely with:
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

    /// <summary>The culture and faith most of the region's people share (0 for worlds generated before Plan 3).</summary>
    public long CultureId { get; set; }

    public long FaithId { get; set; }

    /// <summary>The realm that holds the region directly (0 = unclaimed).</summary>
    public long OwnerRealmId { get; set; }

    /// <summary>A realm occupying it in war (0 = none). Control is loyal, occupied or contested (see Realms).</summary>
    public long OccupierRealmId { get; set; }

    /// <summary>Progress (0–100) of a siege of the region's chief settlement, and the besieging army.</summary>
    public double SiegeProgress { get; set; }

    public long BesiegerArmyId { get; set; }

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

`sim/FantasyWorld.Sim/Model/War.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Why a war is fought (spec §6). Every war cites the event that gave it its casus belli.</summary>
public static class CasusBelli
{
    public const string Claim = "claim";
    public const string HolyWar = "holy war";
    public const string Revenge = "revenge";
    public const string Reconquest = "reconquest";
    public const string ResourceNeed = "resource need";
    public const string ArtifactRecovery = "artifact recovery";
    public const string Independence = "independence";
}

public sealed class War
{
    public const string Kind = "war";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long AttackerId { get; init; }
    public long DefenderId { get; init; }
    public List<long> Attackers { get; init; } = new();
    public List<long> Defenders { get; init; } = new();
    public string CasusBelli { get; init; } = "";
    public long CasusBelliEventId { get; init; }
    public long TargetRegionId { get; init; }
    public long TargetArtifactId { get; init; }
    public long DeclaredEventId { get; set; }
    public long StartDay { get; init; }

    /// <summary>−100…100, positive favours the attackers: occupations plus BattleScore.</summary>
    public double Score { get; set; }

    /// <summary>Accumulated battle results, positive favours the attackers.</summary>
    public double BattleScore { get; set; }

    public int Battles { get; set; }
    public bool Ended { get; set; }
    public long EndedEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool IsAttacker(long realm) => Attackers.Contains(realm);
    public bool Involves(long realm) => Attackers.Contains(realm) || Defenders.Contains(realm);
    public long LeaderOf(long realm) => IsAttacker(realm) ? AttackerId : DefenderId;
    public IReadOnlyList<long> EnemiesOf(long realm) => IsAttacker(realm) ? Defenders : Attackers;
}

public enum ArmyObjective { Defend, Siege, Return }

/// <summary>An army in the field (spec §6 "Armies"). Levies are borrowed from settlements and returned on disbanding.</summary>
public sealed class Army
{
    public const string Kind = "army";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long RealmId { get; init; }
    public long WarId { get; init; }
    public long CommanderId { get; set; }
    public int Levies { get; set; }
    public int MenAtArms { get; set; }
    public List<long> Knights { get; init; } = new();
    public int Mages { get; set; }
    public double Morale { get; set; } = 70;
    public double Supply { get; set; } = 100;
    public long RegionId { get; set; }
    public List<long> Path { get; init; } = new();

    /// <summary>Travel days left before reaching the next region on the path.</summary>
    public double DaysToNext { get; set; }

    public ArmyObjective Objective { get; set; }
    public long TargetRegionId { get; set; }

    /// <summary>Settlement id → levies raised there (returned when the army disbands).</summary>
    public SortedDictionary<long, int> RaisedFrom { get; init; } = new();

    public bool Disbanded { get; set; }

    [JsonIgnore] public int Soldiers => Levies + MenAtArms + Knights.Count + Mages;
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
    SortedDictionary<long, Army>? Armies = null)
{
    public const int CurrentVersion = 4;

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
        world.Armies);

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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RealmModelTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 325` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add model for realms, wars and armies

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Starting realms

Spec §6: 6–8 starting realms. Every city's lord rules a kingdom whose land is the regions nearest its capital by travel days (multi-source Dijkstra, so territories are contiguous); every town's lord outside a capital region rules a vassal county inside it. Tiny worlds without two cities crown their largest towns. Government, succession and magic law follow the realm's faith and land.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/RealmGenerator.cs`
- Modify: `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/RealmGeneratorTests.cs`

**Interfaces:**
- Consumes: Task 1; `RegionGraph`, `PeopleGenerator` (Plans 2–3).
- Produces: `static class RealmGenerator { void Generate(World, Rng); Dictionary<long, long> NearestSource(RegionGraph, IReadOnlyList<long> sources) }`, called by `WorldGenerator` after the people.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/RealmGeneratorTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RealmGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_StartsWithSixToEightKingdoms_AndTheirCounties()
    {
        var world = Medium();
        Assert.InRange(Realms.Independent(world).Count(), 6, 8);
        Assert.True(world.Realms.Values.Count(r => r.LiegeId != 0) >= 10);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryRegion_BelongsToARealm()
    {
        var world = Medium();
        Assert.All(world.Regions.Values, r => Assert.NotNull(Realms.SovereignOf(world, r)));
    }

    [Fact]
    public void Kingdoms_AreRuledByCityLords_AndCountiesByTownLords()
    {
        var world = Medium();
        foreach (var realm in world.Realms.Values)
        {
            var capital = world.Settlements[realm.CapitalId];
            Assert.Equal(realm.LiegeId == 0 ? SettlementTier.City : SettlementTier.Town, capital.Tier);
            Assert.Equal(capital.LordId, Realms.RulerOf(world, realm)!.Id);
            Assert.Equal(2, realm.Colors.Distinct().Count());
        }
    }

    [Fact]
    public void Kingdoms_HoldContiguousTerritories()
    {
        var world = Medium();
        var graph = world.Geography!.Graph;
        foreach (var kingdom in Realms.Independent(world))
        {
            var mine = Realms.Territory(world, kingdom).Select(r => r.Id).ToHashSet();
            var start = world.Settlements[kingdom.CapitalId].RegionId;
            var seen = new HashSet<long> { start };
            var queue = new Queue<long>(seen);
            while (queue.Count > 0)
                foreach (var n in graph.Neighbours(queue.Dequeue()))
                    if (mine.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(mine.Count, seen.Count);
            Assert.Equal(RealmRank.Kingdom, kingdom.Rank);
        }
    }

    [Fact]
    public void Laws_FollowFaithAndGovernment()
    {
        var world = Medium();
        foreach (var realm in world.Realms.Values)
        {
            if (realm.Government == Government.Magocracy) Assert.Equal(MagicLaw.Embraced, realm.MagicLaw);
            if (realm.Government == Government.Tribal) Assert.Equal(SuccessionLaw.Seniority, realm.SuccessionLaw);
        }
    }

    [Fact]
    public void TinyWorlds_CrownTheirLargestTowns()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.Equal(2, Realms.Independent(world).Count());
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Theory]
    [InlineData(14UL)]
    [InlineData(17UL)]
    [InlineData(49UL)]
    [InlineData(57UL)]
    public void TinyWorlds_WithTwoTownsInOneRegion_CrownOneOfThem(ulong seed)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var capitals = Realms.Independent(world).Select(r => world.Settlements[r.CapitalId].RegionId).ToList();
        Assert.NotEmpty(capitals);
        Assert.Equal(capitals.Count, capitals.Distinct().Count());
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 7 failing.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/RealmGenerator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The starting political map (spec: 6–8 starting realms). Every city's lord (in small worlds without
/// two cities, the largest towns') rules a kingdom whose
/// territory is the regions nearest its capital by travel days; every town's lord outside a capital
/// region rules a vassal county of the kingdom around it. Government, succession and magic law follow
/// the realm's faith and land.
/// </summary>
public static class RealmGenerator
{
    private static readonly string[] Palette =
        { "crimson", "azure", "gold", "silver", "sable", "vert", "purpure", "orange", "white", "russet" };

    public static void Generate(World world, Rng rng)
    {
        var graph = world.Geography?.Graph ?? throw new InvalidOperationException("Realms need a region graph.");
        // Kingdom seats: every city; small worlds without two cities crown their largest towns instead.
        var seats = world.Settlements.Values.Where(s => s.LordId != 0 && !s.Abandoned && s.Tier >= SettlementTier.Town)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id)
            .DistinctBy(s => s.RegionId).ToList(); // one seat per region: the largest
        var cities = seats.Where(s => s.Tier == SettlementTier.City).ToList();
        if (cities.Count < 2) cities = seats.Take(2).ToList();
        cities = cities.OrderBy(s => s.Id).ToList();
        if (cities.Count == 0) return;

        var kingdoms = cities.Select(city => Found(world, rng, city, liegeId: 0)).ToList();
        var nearest = NearestSource(graph, kingdoms.Select(k => world.Settlements[k.CapitalId].RegionId).ToList());
        foreach (var region in world.Regions.Values)
            if (nearest.TryGetValue(region.Id, out var capitalRegion))
                region.OwnerRealmId = kingdoms.Single(k => world.Settlements[k.CapitalId].RegionId == capitalRegion).Id;

        var claimed = new HashSet<long>(kingdoms.Select(k => world.Settlements[k.CapitalId].RegionId));
        foreach (var town in world.Settlements.Values.Where(s => s.Tier == SettlementTier.Town && s.LordId != 0 && !s.Abandoned).OrderBy(s => s.Id))
        {
            var region = world.Regions[town.RegionId];
            if (!claimed.Add(region.Id) || region.OwnerRealmId == 0) continue;
            var county = Found(world, rng, town, liegeId: region.OwnerRealmId);
            region.OwnerRealmId = county.Id;
        }

        foreach (var realm in world.Realms.Values)
        {
            realm.Rank = Realms.RankFor(world, realm);
            realm.RegionsAtLastReview = Realms.Domain(world, realm).Count();
            realm.Treasury = Math.Round(Realms.Domain(world, realm).SelectMany(world.LivingSettlements).Sum(s => s.Wealth) * 0.2);
        }
    }

    private static Realm Found(World world, Rng rng, Settlement capital, long liegeId)
    {
        var region = world.Regions[capital.RegionId];
        var faith = world.Faiths.GetValueOrDefault(region.FaithId);
        var tenets = faith?.Tenets ?? new List<string>();
        var government = tenets.Contains("zealous") && rng.Chance(0.5) ? Government.Theocracy
            : region.LeyPower >= 75 && rng.Chance(0.5) ? Government.Magocracy
            : tenets.Contains("mercantile") && region.Coastal && rng.Chance(0.6) ? Government.MerchantRepublic
            : region.Climate == ClimateBand.Cold && rng.Chance(0.4) ? Government.Tribal
            : rng.Chance(0.2) ? Government.ElectiveKingdom
            : Government.FeudalMonarchy;
        var colors = new List<string>();
        while (colors.Count < 2)
        {
            var color = Palette[rng.NextInt(0, Palette.Length)];
            if (!colors.Contains(color)) colors.Add(color);
        }
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = region.Name,
            CapitalId = capital.Id,
            LiegeId = liegeId,
            Government = government,
            SuccessionLaw = government switch
            {
                Government.ElectiveKingdom or Government.MerchantRepublic or Government.Theocracy => SuccessionLaw.Elective,
                Government.Tribal => SuccessionLaw.Seniority,
                _ => SuccessionLaw.Primogeniture,
            },
            MagicLaw = tenets.Contains("arcane") || government == Government.Magocracy ? MagicLaw.Embraced
                : tenets.Contains("zealous") || tenets.Contains("pacifist") ? MagicLaw.Outlawed
                : MagicLaw.Licensed,
            Legitimacy = rng.NextInt(60, 81),
            Stability = rng.NextInt(60, 81),
            CultureId = region.CultureId,
            FaithId = region.FaithId,
            Colors = colors.ToArray(),
        };
        world.Realms[realm.Id] = realm;
        region.OwnerRealmId = realm.Id;
        return realm;
    }

    /// <summary>Multi-source Dijkstra over travel days: each region → the nearest source region.</summary>
    public static Dictionary<long, long> NearestSource(RegionGraph graph, IReadOnlyList<long> sources)
    {
        var distance = new Dictionary<long, double>();
        var nearest = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Region)>();
        foreach (var s in sources)
        {
            distance[s] = 0;
            nearest[s] = s;
            queue.Enqueue(s, (0, s));
        }
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (priority.Days > distance[region]) continue;
            foreach (var link in graph.LinksOf(region))
            {
                var next = link.Other(region);
                var days = priority.Days + link.TravelDays;
                if (distance.TryGetValue(next, out var known) && known <= days) continue;
                distance[next] = days;
                nearest[next] = nearest[region];
                queue.Enqueue(next, (days, next));
            }
        }
        return nearest;
    }
}
```

Replace `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs` entirely with:
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

/// <summary>Seed → a complete world: terrain, rivers, regions, region graph, settlements, cultures, faiths, notables, ancient artifacts and realms. No history yet (Plan 6).</summary>
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

        var (cultures, faiths) = CultureGenerator.Generate(regions, graph, Stream("cultures"), world.Ids);

        world.Geography = new Geography(terrain, layout.RegionOf, graph);
        foreach (var region in regions) world.Regions[region.Id] = region;
        foreach (var culture in cultures) world.Cultures[culture.Id] = culture;
        foreach (var faith in faiths) world.Faiths[faith.Id] = faith;
        foreach (var settlement in settlements) world.Settlements[settlement.Id] = settlement;
        PeopleGenerator.Populate(world, Stream("people"));
        RealmGenerator.Generate(world, Stream("realms"));
        return world;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RealmGeneratorTests"`
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 335` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): found starting kingdoms and vassal counties

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Politics inside realms

Monthly, capitals stay inside the domain (a lost capital moves, citing the loss), settlements pay taxes (vassals pass a fifth upward) and councils fill with the ablest courtiers. Yearly, legitimacy and stability drift toward targets set by the ruler (an interregnum or a child ruler costs legitimacy, a crown artifact adds to it), war weariness and hunger. Collapse and recovery are condition events citing their causes. Unhappy vassals declare independence, ranks follow size, and each change of phase opens a named era. Shared realm consequences (moving capitals, a realm's fall, ceding regions) live in the `Statecraft` helper library.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/RealmSystem.cs`, `sim/FantasyWorld.Sim/Systems/Statecraft.cs`
- Modify: `sim/FantasyWorld.Sim/Systems/Fates.cs`, `sim/FantasyWorld.Sim/Systems/PromotionSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/RealmSystemTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `Fates` (Plan 3).
- Produces: `static class Statecraft { LegitimacyCollapsed; StabilityCollapsed; Title(realm); Remember(...); SecureCapital(ctx, realm, causes); Fall(ctx, realm, causes); Cede(ctx, region, from, to, causes); Disband(world, army) }`; `sealed class RealmSystem : ISimSystem` (name `"realms"`; `TaxRate = 0.01`, `VassalTribute = 0.2`, `IndependencePerYear = 0.25`; `static double Aptitude(seat, character)`). `Fates.Die` now also clears council seats and army commands. Events: `CapitalMoved`, `CouncilAppointed`, `LegitimacyCollapsed`/`LegitimacyRestored`, `StabilityCollapsed`/`StabilityRestored`, `IndependenceDeclared`, `RealmRose`/`RealmDiminished`, `RealmFell`, `EraBegan`.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs` entirely with:
```csharp
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Two neighbouring kingdoms on TestPeople's two-region world, each ruled by its capital's lord.</summary>
public static class TestRealms
{
    public static World TwoKingdoms(ulong seed = 1)
    {
        var world = TestPeople.World(seed);
        foreach (var settlement in world.Settlements.Values)
        {
            var lord = TestPeople.Add(world, settlement.Id, 40, role: Role.Lord);
            lord.Attributes.Martial = 12;
            AddRealm(world, settlement.Id, $"Realm of {settlement.Name}");
        }
        return world;
    }

    public static Realm AddRealm(World world, long capitalId, string name, long liegeId = 0)
    {
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = name,
            CapitalId = capitalId,
            LiegeId = liegeId,
            Legitimacy = 70,
            Stability = 70,
            Treasury = 500,
            CultureId = 1,
            FaithId = 1,
            RegionsAtLastReview = 1,
        };
        world.Realms[realm.Id] = realm;
        world.Regions[world.Settlements[capitalId].RegionId].OwnerRealmId = realm.Id;
        return realm;
    }
}
```

`sim/FantasyWorld.Sim.Tests/Systems/RealmSystemTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'RealmSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

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
            Succeed(ctx, seat, c, HeirCandidates(world, c, killer).FirstOrDefault(k => CanTakeSeat(world, k)), death);
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
        ctx.Emit(new EventDraft("SuccessionOccurred",
            Actors: new Dictionary<string, EntityRef> { ["heir"] = heir.Ref, ["predecessor"] = predecessor.Ref },
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

Replace `sim/FantasyWorld.Sim/Systems/PromotionSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Populace promotion (spec §3): when a story needs a person, one steps out of the aggregate
/// population. Empty seats get a new lord (citing the death that emptied them), revolts get a
/// leader, monster-ridden regions get a hero, leaderless villages get an elder.
/// </summary>
public sealed class PromotionSystem : ISimSystem
{
    public const double HeroPerMonth = 0.2;
    public const double ElderPerMonth = 0.05;

    public string Name => "promotion";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var ruin in world.Settlements.Values.Where(s => s.Abandoned))
            Evacuate(ctx, ruin);
        var residents = world.ActiveCharacters.ToLookup(c => c.SettlementId);
        // A realm's capital always gets a ruler, even after it has shrunk to a village.
        var capitals = world.Realms.Values.Where(r => !r.Destroyed).Select(r => r.CapitalId).ToHashSet();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned).ToList())
        {
            if ((s.Tier >= SettlementTier.Town || capitals.Contains(s.Id)) && s.LordId == 0) RaiseLord(ctx, s, residents[s.Id]);
            if (s.Conditions.TryGetValue(Conditions.Revolt, out var revolt) && !residents[s.Id].Any(c => c.Role == Role.Rebel))
                RaiseRebel(ctx, s, revolt);
            if (s.Tier == SettlementTier.Village && !residents[s.Id].Any() && ctx.Rng.Chance(ElderPerMonth))
                Promote(ctx, s, Role.Elder, "ElderChosen", 5, Array.Empty<Cause>());
        }

        foreach (var region in world.Regions.Values)
        {
            if (!region.Conditions.TryGetValue(Conditions.Dangerous, out var danger)) continue;
            var settlements = world.LivingSettlements(region).ToList();
            if (settlements.Count == 0 || settlements.Any(s => residents[s.Id].Any(c => c.Role is Role.Adventurer or Role.Knight)))
                continue;
            if (!ctx.Rng.Chance(HeroPerMonth)) continue;
            var home = settlements.OrderByDescending(s => s.Population).ThenBy(s => s.Id).First();
            var (hero, _) = Promote(ctx, home, Role.Adventurer, "HeroRose", 25, new[] { new Cause(danger, CauseKind.Trigger, 0.9) });
            hero.Ambition = Ambitions.Renown;
        }
    }

    /// <summary>Settlements are abandoned in the settlements' yearly tick; promotion runs after it and evacuates the same day.</summary>
    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var ruin in ctx.World.Settlements.Values.Where(s => s.Abandoned))
            Evacuate(ctx, ruin);
    }

    /// <summary>The ablest adult already living there takes the seat; failing that, someone from the populace.</summary>
    private static void RaiseLord(SimContext ctx, Settlement seat, IEnumerable<Character> residents)
    {
        var world = ctx.World;
        var causes = seat.Conditions.TryGetValue(Fates.VacantSeat, out var death)
            ? new[] { new Cause(death, CauseKind.Trigger, 1.0) }
            : Array.Empty<Cause>();
        var candidate = residents
            .Where(c => c.Stage(ctx.Date) != LifeStage.Child && c.Role is not (Role.Lord or Role.Rebel))
            .OrderByDescending(c => c.Attributes.Stewardship + c.Attributes.Diplomacy + c.Renown / 5).ThenBy(c => c.Id)
            .FirstOrDefault();
        if (candidate is null)
        {
            (candidate, _) = Promote(ctx, seat, Role.Lord, "NewLordRose", Importance(seat), causes);
        }
        else
        {
            candidate.Role = Role.Lord;
            ctx.Emit(new EventDraft("NewLordRose",
                Actors: new Dictionary<string, EntityRef> { ["lord"] = candidate.Ref },
                Place: seat.Ref, Causes: causes, Importance: Importance(seat), Tags: new[] { "Characters", "Politics" }));
        }
        seat.LordId = candidate.Id;
        seat.Conditions.Remove(Fates.VacantSeat);
        world.Houses[candidate.HouseId].SeatSettlementId = seat.Id;
    }

    private static void RaiseRebel(SimContext ctx, Settlement s, long revolt)
    {
        var (rebel, rose) = Promote(ctx, s, Role.Rebel, "RebelLeaderRose", 30, new[] { new Cause(revolt, CauseKind.Trigger, 1.0) });
        rebel.Renown = 10;
        rebel.Ambition = Ambitions.Renown;
        if (s.LordId == 0) return;
        var lord = ctx.World.Characters[s.LordId];
        Fates.Remember(rebel, rose.Id, lord.Id, -50, 60);
        Fates.Remember(lord, rose.Id, rebel.Id, -50, 60);
    }

    /// <summary>A new notable from the settlement's populace (one adult fewer in the aggregate), with a new house.</summary>
    private static (Character Character, WorldEvent Rose) Promote(SimContext ctx, Settlement s, Role role, string eventType, int importance, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        var region = world.Regions[s.RegionId];
        var house = CharacterFactory.NewHouse(world, ctx.Rng, region.CultureId, s.Id);
        var born = ctx.Date.Day - (long)ctx.Rng.NextInt(18, 46) * WorldDate.DaysPerYear - ctx.Rng.NextInt(0, 360);
        var c = CharacterFactory.Create(world, ctx.Rng, s.Id, ctx.Rng.Chance(0.5) ? Sex.Female : Sex.Male, born, house.Id, role);
        if (s.Adults > 0) s.Adults--;
        var rose = ctx.Emit(new EventDraft(eventType,
            Actors: new Dictionary<string, EntityRef> { [role == Role.Lord ? "lord" : "character"] = c.Ref },
            Place: s.Ref, Causes: causes, Importance: importance, Tags: new[] { "Characters" }));
        return (c, rose);
    }

    /// <summary>
    /// Notables leave a settlement that has been abandoned for the largest living settlement in the
    /// region (else a neighbouring region, else anywhere); its lord loses the dead seat.
    /// </summary>
    private static void Evacuate(SimContext ctx, Settlement ruin)
    {
        var world = ctx.World;
        var people = world.ActiveCharacters.Where(c => c.SettlementId == ruin.Id).ToList();
        if (people.Count == 0 && ruin.LordId == 0) return;
        var region = world.Regions[ruin.RegionId];
        var regions = new[] { region.Id }.Concat(world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]).Concat(world.Regions.Values);
        var refuge = regions.Select(r => world.LivingSettlements(r).OrderByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault())
            .FirstOrDefault(s => s is not null);
        if (refuge is null) return;

        var abandonment = ctx.Log.Involving(ruin.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
        if (ruin.LordId != 0)
        {
            world.Characters[ruin.LordId].Role = Role.None;
            ruin.LordId = 0;
        }
        foreach (var c in people) c.SettlementId = refuge.Id;
        ctx.Emit(new EventDraft("NotablesFled",
            Actors: new Dictionary<string, EntityRef> { ["from"] = ruin.Ref },
            Place: refuge.Ref,
            Payload: new Dictionary<string, string> { ["people"] = people.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: abandonment is not null ? new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) } : null,
            Importance: 15, Tags: new[] { "Characters" }));
    }

    private static int Importance(Settlement seat) => seat.Tier == SettlementTier.City ? 45 : 30;
}
```

`sim/FantasyWorld.Sim/Systems/RealmSystem.cs`:
```csharp
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

    private static void ReviewPhase(SimContext ctx, Realm realm)
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

`sim/FantasyWorld.Sim/Systems/Statecraft.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Shared consequences for realms, used by the politics, diplomacy and military systems: moving a
/// capital, a realm's fall, ceding regions, and realm-level memories. A helper library, not a system.
/// </summary>
public static class Statecraft
{
    public const string LegitimacyCollapsed = "legitimacyCollapsed";
    public const string StabilityCollapsed = "stabilityCollapsed";

    public static string Title(Realm realm) => $"{realm.Rank} of {realm.Name}";

    public static void Remember(Realm realm, long eventId, long aboutRealm, double feeling, double weight) =>
        realm.Memories.Add(new RealmMemory(eventId, aboutRealm, feeling, weight));

    /// <summary>
    /// Keeps the capital inside the domain: moves it to the largest living settlement the realm still
    /// holds, or lets the realm fall if it holds none. Returns false if the realm fell.
    /// </summary>
    public static bool SecureCapital(SimContext ctx, Realm realm, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        var capital = world.Settlements[realm.CapitalId];
        if (!capital.Abandoned && world.Regions[capital.RegionId].OwnerRealmId == realm.Id) return true;
        var refuge = Realms.Domain(world, realm).SelectMany(world.LivingSettlements)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
        if (refuge is null)
        {
            Fall(ctx, realm, causes);
            return false;
        }
        realm.CapitalId = refuge.Id;
        ctx.Emit(new EventDraft("CapitalMoved",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["from"] = capital.Ref },
            Place: refuge.Ref, Causes: causes, Importance: 40, Tags: new[] { "Politics" }));
        return true;
    }

    /// <summary>A realm with no land falls: its vassals go free, its treaties lapse, its wars are left to others.</summary>
    public static void Fall(SimContext ctx, Realm realm, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        var fell = ctx.Emit(new EventDraft("RealmFell",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: world.Settlements[realm.CapitalId].Ref, Causes: causes,
            Importance: realm.LiegeId == 0 ? 80 : 40, Tags: new[] { "Politics" }));
        realm.Destroyed = true;
        realm.Phase = RealmPhase.Collapsed;
        realm.Council.Clear();
        foreach (var vassal in Realms.VassalsOf(world, realm).ToList()) vassal.LiegeId = 0;
        foreach (var other in world.Realms.Values) other.Treaties.RemoveAll(t => t.With == realm.Id);
        realm.Treaties.Clear();
        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id)) region.OwnerRealmId = 0;
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId == realm.Id)) region.OccupierRealmId = 0;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended && w.Involves(realm.Id)))
        {
            war.Attackers.Remove(realm.Id);
            war.Defenders.Remove(realm.Id);
        }
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded && a.RealmId == realm.Id))
            Disband(world, army);
        realm.Memories.Clear();
    }

    /// <summary>Hands a region to another realm (war, inheritance): occupation and sieges end there.</summary>
    public static WorldEvent Cede(SimContext ctx, Region region, Realm from, Realm to, IReadOnlyList<Cause> causes)
    {
        region.OwnerRealmId = to.Id;
        region.OccupierRealmId = 0;
        region.SiegeProgress = 0;
        region.BesiegerArmyId = 0;
        var ceded = ctx.Emit(new EventDraft("RegionCeded",
            Actors: new Dictionary<string, EntityRef> { ["from"] = from.Ref, ["to"] = to.Ref },
            Place: region.Ref, Causes: causes, Importance: 45, Tags: new[] { "Politics", "War" }));
        Remember(from, ceded.Id, to.Id, -60, 80);
        if (!from.Destroyed) SecureCapital(ctx, from, new[] { new Cause(ceded.Id, CauseKind.Trigger, 1.0) });
        return ceded;
    }

    /// <summary>Sends the levies home (survivors return to their settlements' adults) and dissolves the army.</summary>
    public static void Disband(World world, Army army)
    {
        var raised = army.RaisedFrom.Values.Sum();
        var survival = raised > 0 ? Math.Clamp((double)army.Levies / raised, 0, 1) : 0;
        foreach (var (settlementId, levies) in army.RaisedFrom)
            if (world.Settlements.TryGetValue(settlementId, out var s) && !s.Abandoned)
                s.Adults += (int)(levies * survival);
        army.Disbanded = true;
        army.Levies = army.MenAtArms = army.Mages = 0;
        army.Knights.Clear();
        army.CommanderId = 0;
        foreach (var region in world.Regions.Values.Where(r => r.BesiegerArmyId == army.Id))
        {
            region.BesiegerArmyId = 0;
            region.SiegeProgress = 0;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RealmSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 9`. Then `dotnet test sim` → `Passed: 344` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add realm system with taxes, councils, legitimacy and eras

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Succession laws

A realm's law now decides who takes its capital's seat: primogeniture keeps Plan 3's eldest-child order, seniority prefers the eldest sibling, and elective realms choose the ablest adult kinsman or courtier. `SuccessionOccurred` records the law it followed.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Systems/Fates.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/SuccessionLawTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3; `Fates.Heir` (Plan 3).
- Produces: `Fates.SeatCandidates(world, deceased, seat, killer, today)`, `Fates.Electability(character)`; payload `law` on `SuccessionOccurred`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/SuccessionLawTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SuccessionLawTests
{
    private static (World World, Character Ruler, Character Son, Character Brother) Dynasty(SuccessionLaw law)
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].SuccessionLaw = law;
        var ruler = world.Characters[world.Settlements[1].LordId];
        var father = TestPeople.Add(world, 1, 80, houseId: ruler.HouseId);
        var mother = TestPeople.Add(world, 1, 78, Sex.Female, houseId: ruler.HouseId);
        world.Characters.Remove(ruler.Id);
        var king = new Character
        {
            Id = ruler.Id, Name = ruler.Name, HouseId = ruler.HouseId, Sex = Sex.Male, BirthDay = ruler.BirthDay,
            CultureId = 1, FaithId = 1, SettlementId = 1, FatherId = father.Id, MotherId = mother.Id,
            Attributes = ruler.Attributes, Role = Role.Lord,
        };
        world.Characters[king.Id] = king;
        father.Children.Add(king.Id);
        mother.Children.Add(king.Id);
        var brother = TestPeople.Add(world, 1, 35, fatherId: father.Id, motherId: mother.Id, houseId: king.HouseId);
        var son = TestPeople.Add(world, 1, 18, fatherId: king.Id, houseId: king.HouseId);
        return (world, king, son, brother);
    }

    private static void Kill(World world, Character c) =>
        Fates.Die(new SimContext(world, new EventLog(), new EventQueue(), new Rng(1)), c, "illness", Array.Empty<Cause>());

    [Fact]
    public void Primogeniture_CrownsTheChild()
    {
        var (world, king, son, _) = Dynasty(SuccessionLaw.Primogeniture);
        Kill(world, king);
        Assert.Equal(son.Id, world.Settlements[1].LordId);
    }

    [Fact]
    public void Seniority_CrownsTheBrother()
    {
        var (world, king, _, brother) = Dynasty(SuccessionLaw.Seniority);
        Kill(world, king);
        Assert.Equal(brother.Id, world.Settlements[1].LordId);
    }

    [Fact]
    public void Elective_CrownsTheAblest_EvenFromTheCouncil()
    {
        var (world, king, _, _) = Dynasty(SuccessionLaw.Elective);
        var chancellor = TestPeople.Add(world, 1, 50);
        chancellor.Attributes.Stewardship = chancellor.Attributes.Diplomacy = chancellor.Attributes.Martial = 20;
        world.Realms[1].Council[CouncilSeats.Steward] = chancellor.Id;
        var log = new EventLog();
        Fates.Die(new SimContext(world, log, new EventQueue(), new Rng(1)), king, "illness", Array.Empty<Cause>());
        Assert.Equal(chancellor.Id, world.Settlements[1].LordId);
        Assert.False(world.Realms[1].Council.ContainsValue(chancellor.Id));
        Assert.Equal("Elective", log.All.Single(e => e.Type == "SuccessionOccurred").Payload["law"]);
        Assert.Empty(Invariants.Check(world, log));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 2 failing.

- [ ] **Step 3: Implement**

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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SuccessionLawTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 347` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): apply realm succession laws to ruling seats

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Diplomacy and casus belli

Yearly, independent realms weigh each other: the rulers' personal opinion, realm memories (grudges outlive rulers), faith, culture and treaties. Friendly neighbours ally, sealing it with a royal marriage when kin allow; hostile ones declare rivalry. A ruler at peace weighs every casus belli history offers (a claim on lost land, a holy war, revenge for a killing, reconquest, need of a resource, a stolen artifact) against keeping the peace, as a decision point. A declared war cites its casus belli event and draws in allies.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/DiplomacySystemTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3; `IDecider`, `Relations` (Plan 3).
- Produces: `record CasusBelliOption(Kind, Against, EventId, TargetRegionId, TargetArtifactId, Weight)`; `sealed class DiplomacySystem(IDecider? decider = null) : ISimSystem` (name `"diplomacy"`; `static Opinion(world, a, b)`, `Strength(world, realm)`, `AtWar(world, realm)`, `CasusBelliFor(ctx, realm)`). Events: `AllianceFormed`, `AllianceBroken`, `RivalryDeclared`, `HolyWarProclaimed`, `WarDeclared` (payload `casus belli`, `war`), `AllyJoinedWar`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/DiplomacySystemTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class DiplomacySystemTests
{
    private sealed class Choose(string prefix) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(prefix, StringComparison.Ordinal)) ?? brief.Options[^1];
    }

    private static Simulator Make(World world, EventLog log, string choice = "peace") =>
        new(world, log, new EventQueue(), new ISimSystem[] { new DiplomacySystem(new Choose(choice)) }, checkInvariants: true);

    private static WorldEvent Grudge(World world, EventLog log, long realm, long about, double feeling = -80)
    {
        var ev = log.Append(world.Date, new EventDraft("BorderRaid"));
        world.Realms[realm].Memories.Add(new RealmMemory(ev.Id, about, feeling, 90));
        return ev;
    }

    [Fact]
    public void Friends_Ally_WithARoyalMarriage()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var a = world.Characters[world.Settlements[1].LordId];
        var b = world.Characters[world.Settlements[2].LordId];
        var prince = TestPeople.Add(world, 1, 20, Sex.Male, fatherId: a.Id, houseId: a.HouseId);
        var princess = TestPeople.Add(world, 2, 20, Sex.Female, fatherId: b.Id, houseId: b.HouseId);
        var feast = log.Append(world.Date, new EventDraft("Feast"));
        a.Memories.Add(new Memory(feast.Id, b.Id, 100, 100));
        b.Memories.Add(new Memory(feast.Id, a.Id, 100, 100));
        foreach (var r in world.Realms.Values) r.Memories.Add(new RealmMemory(feast.Id, r.Id == 1 ? 2 : 1, 60, 60));
        var sim = Make(world, log);

        sim.AdvanceDays(360);

        Assert.True(world.Realms[1].Has(TreatyKind.Alliance, 2));
        var pact = sim.Log.All.Single(e => e.Type == "AllianceFormed");
        Assert.Equal("MarriageWed", sim.Log.Get(pact.Causes.Single().EventId).Type);
        Assert.Equal(princess.Id, prince.SpouseId);
    }

    [Fact]
    public void Enemies_DeclareRivalry()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Grudge(world, log, 2, 1);
        world.Realms[2].FaithId = 2;
        world.Faiths[2] = new Faith { Id = 2, Name = "Other", Deity = "Zed" };
        var sim = Make(world, log);
        sim.AdvanceDays(360);
        Assert.True(world.Realms[1].Has(TreatyKind.Rivalry, 2));
    }

    [Fact]
    public void Grudge_GivesACasusBelli_AndTheWarCitesIt()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var raid = Grudge(world, log, 1, 2);
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.Revenge, war.CasusBelli);
        Assert.Equal(raid.Id, war.CasusBelliEventId);
        var declared = sim.Log.Get(war.DeclaredEventId);
        Assert.Equal("WarDeclared", declared.Type);
        Assert.Equal(raid.Id, declared.Causes.Single().EventId);
        Assert.Contains(world.Realms[2].Memories, m => m.EventId == declared.Id && m.About == 1);
    }

    [Fact]
    public void Zealots_ProclaimHolyWar_AgainstOtherFaiths()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Faiths[1].Tenets.Add("zealous");
        world.Faiths[2] = new Faith { Id = 2, Name = "The Other Way", Deity = "Zed" };
        world.Realms[2].FaithId = 2;
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.HolyWar, war.CasusBelli);
        Assert.Equal("HolyWarProclaimed", sim.Log.Get(war.CasusBelliEventId).Type);
        Assert.StartsWith("The Holy War of", war.Name);
    }

    [Fact]
    public void Peaceful_Rulers_KeepThePeace()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Make(world, log, "peace").AdvanceDays(360 * 5);
        Assert.Empty(world.Wars);
    }

    [Fact]
    public void StolenHeirloom_GivesArtifactRecovery()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.ArtifactRecovery);
        Assert.Equal(theft.Id, cb.EventId);
        Assert.Equal(crown.Id, cb.TargetArtifactId);
    }

    [Fact]
    public void AnInheritedHeirloom_IsNoLongerACasusBelli()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var inherited = log.Append(world.Date, new EventDraft("ArtifactInherited"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        crown.Provenance.Add(new ProvenanceEntry(inherited.Id, thief.Id, thief.Id, "inherited"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        Assert.DoesNotContain(DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]), o => o.Kind == CasusBelli.ArtifactRecovery);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'DiplomacySystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`:
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

    public DiplomacySystem(IDecider? decider = null) => _decider = decider ?? new RulesDecider();

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

    private static void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
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
            Causes: new[] { new Cause(cbEvent, CauseKind.Motive, 0.9) },
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DiplomacySystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 354` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add diplomacy with alliances, rivalries and casus belli

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Armies and campaigns

Monthly, every realm at war raises one army at its capital: levies borrowed from its settlements, men-at-arms its treasury can hire, its knights and mages, led by the marshal (else the adult ruler, else the most martial knight). Armies take orders (liberate their own occupied land first, then besiege the war's target or the nearest enemy holding, or meet invaders), march 30 travel days a month along the region graph, forage or draw supply, pay upkeep and suffer attrition and desertion. A broke or broken army goes home and returns its levies.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/MilitarySystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/CampaignTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs`

**Interfaces:**
- Consumes: Tasks 1, 3, 5; `IArbiter`, `IDecider` (Plan 3).
- Produces: `sealed partial class MilitarySystem(IArbiter? arbiter = null, IDecider? decider = null) : ISimSystem` (name `"military"`; `LevyShare = 0.05`, `MarchDaysPerMonth = 30`; `static Active(world)`, `ArmyOf(world, war, realm)`, `Mobilise(ctx, war, realm)`, `OurSide`, `Side`, `Friendly`, `Hostile`). Event: `ArmyRaised`.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Support/TestRealms.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Two neighbouring kingdoms on TestPeople's two-region world, each ruled by its capital's lord.</summary>
public static class TestRealms
{
    public static World TwoKingdoms(ulong seed = 1)
    {
        var world = TestPeople.World(seed);
        foreach (var settlement in world.Settlements.Values)
        {
            var lord = TestPeople.Add(world, settlement.Id, 40, role: Role.Lord);
            lord.Attributes.Martial = 12;
            AddRealm(world, settlement.Id, $"Realm of {settlement.Name}");
        }
        return world;
    }

    public static Realm AddRealm(World world, long capitalId, string name, long liegeId = 0)
    {
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = name,
            CapitalId = capitalId,
            LiegeId = liegeId,
            Legitimacy = 70,
            Stability = 70,
            Treasury = 500,
            CultureId = 1,
            FaithId = 1,
            RegionsAtLastReview = 1,
        };
        world.Realms[realm.Id] = realm;
        world.Regions[world.Settlements[capitalId].RegionId].OwnerRealmId = realm.Id;
        return realm;
    }

    /// <summary>Realm 1 declares war on realm 2 over the given region (default: region 2).</summary>
    public static War DeclareWar(World world, EventLog log, long target = 2)
    {
        var cb = log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Test War", AttackerId = 1, DefenderId = 2,
            Attackers = { 1 }, Defenders = { 2 }, CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id,
            DeclaredEventId = declared.Id, TargetRegionId = target, StartDay = world.Date.Day,
        };
        world.Wars[war.Id] = war;
        return war;
    }
}
```

`sim/FantasyWorld.Sim.Tests/Systems/CampaignTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class CampaignTests
{
    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Mobilise_RaisesLevies_KnightsAndAMarshal()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var marshal = TestPeople.Add(world, 1, 40);
        world.Realms[1].Council[CouncilSeats.Marshal] = marshal.Id;
        var knight = TestPeople.Add(world, 1, 30, role: Role.Knight);
        var adults = world.Settlements[1].Adults;

        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);

        Assert.Equal(marshal.Id, army.CommanderId);
        Assert.Contains(knight.Id, army.Knights);
        Assert.Equal((int)(adults * MilitarySystem.LevyShare), army.Levies);
        Assert.Equal(adults - army.Levies, world.Settlements[1].Adults);
        Assert.Equal(army.Levies, army.RaisedFrom[1]);
        Assert.Equal(25, army.MenAtArms);
        Assert.Single(log.All, e => e.Type == "ArmyRaised");
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Attackers_MarchOnTheTarget()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        TestRealms.DeclareWar(world, log);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);

        sim.AdvanceDays(30);

        var host = world.Armies.Values.Single(a => a.RealmId == 1);
        Assert.Equal(2, host.RegionId);
        Assert.Equal(ArmyObjective.Siege, host.Objective);
    }

    [Fact]
    public void Foraging_EatsTheEnemysFood_AndScarsTheLand()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        TestRealms.DeclareWar(world, log);
        var (food, fertility) = (world.Regions[2].Food, world.Regions[2].Fertility);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(60);
        Assert.True(world.Regions[2].Food < food);
        Assert.True(world.Regions[2].Fertility < fertility);
    }

    [Fact]
    public void BrokeRealms_LoseTheirMenAtArms()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.MenAtArms = 400;
        army.Objective = ArmyObjective.Return;
        army.Morale = 45;
        world.Realms[1].Treasury = -10;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.True(army.MenAtArms < 400);
        Assert.True(army.Morale < 45);
    }

    [Fact]
    public void BeatenArmies_RejoinTheWarOnceRecovered()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.Objective = ArmyObjective.Return;
        army.Morale = 30;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        sim.AdvanceDays(31);
        Assert.Equal(ArmyObjective.Return, army.Objective);
        army.Morale = 80;
        sim.AdvanceDays(30);
        Assert.Equal(ArmyObjective.Siege, army.Objective);
    }

    [Fact]
    public void Occupations_LapseWithoutAWar()
    {
        var world = TestRealms.TwoKingdoms();
        world.Regions[2].OccupierRealmId = 1;
        new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true).AdvanceDays(31);
        Assert.Equal(0, world.Regions[2].OccupierRealmId);
    }

    [Fact]
    public void Disbanding_SendsSurvivorsHome()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var before = world.Settlements[1].Adults;
        var army = MilitarySystem.Mobilise(Context(world, log), war, world.Realms[1]);
        army.Levies /= 2;
        Statecraft.Disband(world, army);
        Assert.True(army.Disbanded);
        Assert.InRange(world.Settlements[1].Adults, before - army.RaisedFrom[1] / 2 - 1, before - army.RaisedFrom[1] / 2 + 1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'MilitarySystem' does not exist in the current context`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/MilitarySystem.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Armies and campaigns (spec §6 "Campaign flow"), monthly: every realm at war raises an army (levies
/// from its settlements, men-at-arms its treasury can hire, its knights and mages, led by its marshal);
/// armies take orders (besiege the war's target or the nearest enemy holding; defend against invaders),
/// march along the region graph (30 travel days a month), forage or draw supply, pay upkeep, and suffer
/// attrition and desertion. Battles, sieges and peace live in the other parts of this class.
/// </summary>
public sealed partial class MilitarySystem : ISimSystem
{
    public const double LevyShare = 0.05;
    public const int MarchDaysPerMonth = 30;

    private readonly IArbiter _arbiter;
    private readonly IDecider _decider;

    public MilitarySystem(IArbiter? arbiter = null, IDecider? decider = null)
    {
        _arbiter = arbiter ?? new RulesArbiter();
        _decider = decider ?? new RulesDecider();
    }

    public string Name => "military";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var realmId in war.Attackers.Concat(war.Defenders).ToList())
                if (world.Realms[realmId] is { Destroyed: false } realm && ArmyOf(world, war, realm) is null && (ctx.Date.Month % 6 == 1 || war.StartDay >= ctx.Date.Day - 30))
                    Mobilise(ctx, war, realm);

        // Occupations lapse when occupier and sovereign are no longer at war (the land changed hands mid-war).
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
            if (Realms.SovereignOf(world, region) is not { } sovereign || !AtWarWith(world, region.OccupierRealmId, sovereign.Id))
                region.OccupierRealmId = 0;

        foreach (var army in Active(world).ToList())
        {
            if (army.Disbanded) continue;
            if (!world.Characters.TryGetValue(army.CommanderId, out var leader) || !leader.IsActive)
            {
                var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
                var successor = Commander(world, world.Realms[army.RealmId], serving, ctx.Date);
                army.CommanderId = successor?.Id ?? 0;
                if (successor is not null) army.Knights.Remove(successor.Id);
            }
            GiveOrders(world, army);
            March(world, army);
            Provision(ctx, army);
        }
        FightBattles(ctx);
        Besiege(ctx);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) Score(ctx, war);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) ConsiderPeace(ctx, war);
    }

    partial void FightBattles(SimContext ctx);

    partial void Besiege(SimContext ctx);

    partial void Score(SimContext ctx, War war);

    partial void ConsiderPeace(SimContext ctx, War war);

    public static IEnumerable<Army> Active(World world) => world.Armies.Values.Where(a => !a.Disbanded);

    public static Army? ArmyOf(World world, War war, Realm realm) =>
        Active(world).FirstOrDefault(a => a.WarId == war.Id && a.RealmId == realm.Id);

    /// <summary>Raises the realm's host for this war at its capital.</summary>
    public static Army Mobilise(SimContext ctx, War war, Realm realm)
    {
        var world = ctx.World;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var raised = new SortedDictionary<long, int>();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)))
        {
            var levies = (int)(s.Adults * LevyShare);
            if (levies <= 0) continue;
            s.Adults -= levies;
            raised[s.Id] = levies;
        }
        var menAtArms = (int)Math.Clamp(realm.Treasury / 20, 0, 500);
        realm.Treasury -= menAtArms * 5;
        var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
        var knights = world.ActiveCharacters
            .Where(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId) && !serving.Contains(c.Id)
                        && c.Stage(ctx.Date) == LifeStage.Adult)
            .Select(c => c.Id).ToList();
        var mages = realm.MagicLaw == MagicLaw.Outlawed ? 0
            : world.ActiveCharacters.Count(c => c.Role == Role.Mage && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        var commander = Commander(world, realm, serving, ctx.Date);
        if (commander is not null) knights.Remove(commander.Id);

        var army = new Army
        {
            Id = world.Ids.Take(Army.Kind),
            Name = $"The Host of {realm.Name}",
            RealmId = realm.Id,
            WarId = war.Id,
            CommanderId = commander?.Id ?? 0,
            Levies = raised.Values.Sum(),
            MenAtArms = menAtArms,
            Knights = knights,
            Mages = mages,
            RegionId = world.Settlements[realm.CapitalId].RegionId,
            Objective = ArmyObjective.Defend,
            RaisedFrom = raised,
        };
        world.Armies[army.Id] = army;
        var actors = new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["war"] = war.Ref, ["army"] = army.Ref };
        if (commander is not null) actors["commander"] = commander.Ref;
        ctx.Emit(new EventDraft("ArmyRaised", Actors: actors, Place: world.Regions[army.RegionId].Ref,
            Payload: new Dictionary<string, string> { ["soldiers"] = army.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Trigger, 1.0) }, Importance: 25, Tags: new[] { "War" }));
        return army;
    }

    /// <summary>The marshal, else the adult ruler, else the most martial knight.</summary>
    private static Character? Commander(World world, Realm realm, HashSet<long> serving, Core.WorldDate today)
    {
        bool Free(Character c) => c.IsActive && !serving.Contains(c.Id) && c.Stage(today) != LifeStage.Child;
        if (realm.Council.TryGetValue(CouncilSeats.Marshal, out var marshal) && Free(world.Characters[marshal])) return world.Characters[marshal];
        if (Realms.RulerOf(world, realm) is { } ruler && Free(ruler)) return ruler;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        return world.ActiveCharacters.Where(c => c.Role == Role.Knight && Free(c) && territory.Contains(world.Settlements[c.SettlementId].RegionId))
            .OrderByDescending(c => c.Attributes.Martial).ThenBy(c => c.Id).FirstOrDefault();
    }

    /// <summary>
    /// Orders, in priority: a beaten army goes home (and rejoins the war once its morale recovers); anyone retakes their side's occupied land; defenders meet
    /// invaders; attackers besiege the war's target, else the nearest enemy holding; strong defenders strike back.
    /// </summary>
    private static void GiveOrders(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var graph = world.Geography!.Graph;
        long target;
        var enemies = war.EnemiesOf(Side(war, realm));
        var invader = Active(world).Where(a => enemies.Contains(a.RealmId) && Friendly(world, war, realm, world.Regions[a.RegionId]))
            .OrderBy(a => graph.ShortestPath(army.RegionId, a.RegionId)?.Days ?? double.MaxValue).ThenBy(a => a.Id).FirstOrDefault();
        var lost = world.Regions.Values.Where(r => r.OccupierRealmId != 0 && !Hostile(world, war, realm, r)
                && !Friendly(world, war, realm, r) && Realms.SovereignOf(world, r) is { } owner && OurSide(war, realm).Contains(owner.Id))
            .OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
        var home = world.Settlements[realm.CapitalId].RegionId;
        if (army.Objective == ArmyObjective.Return && army.RegionId == home && army.Morale >= 50)
            army.Objective = ArmyObjective.Defend; // regrouped at home: back to the war
        if (army.Objective == ArmyObjective.Return)
            target = home;
        else if (lost is not null)
        {
            target = lost.Id;
            army.Objective = ArmyObjective.Defend;
        }
        else if (invader is not null && !war.IsAttacker(Side(war, realm)))
        {
            target = invader.RegionId;
            army.Objective = ArmyObjective.Defend;
        }
        else
        {
            var goals = world.Regions.Values.Where(r => Hostile(world, war, realm, r) && world.LivingSettlements(r).Any()).ToList();
            var preferred = goals.FirstOrDefault(r => r.Id == war.TargetRegionId);
            var nearest = goals.OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
            var goal = war.IsAttacker(Side(war, realm)) ? preferred ?? nearest : invader is null && Strong(world, army) ? nearest : null;
            target = goal?.Id ?? world.Settlements[realm.CapitalId].RegionId;
            army.Objective = goal is null ? ArmyObjective.Defend : ArmyObjective.Siege;
        }
        if (target == army.TargetRegionId && (army.Path.Count > 0 || army.RegionId == target)) return;
        army.TargetRegionId = target;
        army.Path.Clear();
        army.DaysToNext = 0;
        if (graph.ShortestPath(army.RegionId, target) is { } route) army.Path.AddRange(route.Path.Skip(1));
    }

    private static void March(World world, Army army)
    {
        var budget = (double)MarchDaysPerMonth;
        var graph = world.Geography!.Graph;
        while (army.Path.Count > 0 && budget > 0)
        {
            if (army.DaysToNext <= 0) army.DaysToNext = graph.Between(army.RegionId, army.Path[0])?.TravelDays ?? 1;
            var step = Math.Min(budget, army.DaysToNext);
            army.DaysToNext -= step;
            budget -= step;
            if (army.DaysToNext > 0) break;
            army.RegionId = army.Path[0];
            army.Path.RemoveAt(0);
            if (EnemyArmyIn(world, army, army.RegionId)) break;
        }
    }

    /// <summary>Supply from friendly land is paid for; in enemy land the army forages and devastates.</summary>
    private static void Provision(SimContext ctx, Army army)
    {
        var world = ctx.World;
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var region = world.Regions[army.RegionId];
        if (Friendly(world, war, realm, region)) army.Supply = Math.Min(100, army.Supply + 20);
        else
        {
            var need = army.Soldiers * 0.5;
            var taken = Math.Min(region.Food, need);
            region.Food -= taken;
            region.Fertility = Math.Max(0, region.Fertility - 1);
            army.Supply = Math.Clamp(army.Supply + (taken >= need ? 10 : -15), 0, 100);
        }

        realm.Treasury -= army.MenAtArms * 0.5 + army.Knights.Count * 2 + army.Mages * 3;
        if (realm.Treasury < 0)
        {
            army.MenAtArms = (int)(army.MenAtArms * 0.9);
            army.Morale = Math.Max(0, army.Morale - 10);
        }
        if (army.Supply < 30)
        {
            army.Levies = (int)(army.Levies * 0.95);
            army.MenAtArms = (int)(army.MenAtArms * 0.97);
            army.Morale = Math.Max(0, army.Morale - 5);
        }
        else army.Morale = Math.Min(100, army.Morale + 2);
        foreach (var r in war.Attackers.Concat(war.Defenders)) world.Realms[r].WarWeariness = Math.Min(100, world.Realms[r].WarWeariness + 0.2);
    }

    public static IReadOnlyList<long> OurSide(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;

    /// <summary>The side leader a realm fights for in this war (its own id if it leads).</summary>
    public static long Side(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.AttackerId : war.DefenderId;

    /// <summary>Held (and not occupied) by our side, or occupied by our side.</summary>
    public static bool Friendly(World world, War war, Realm realm, Region region)
    {
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        if (region.OccupierRealmId != 0) return ours.Contains(region.OccupierRealmId);
        return Realms.SovereignOf(world, region) is { } owner && ours.Contains(owner.Id);
    }

    /// <summary>Held by the other side and not already occupied by ours.</summary>
    public static bool Hostile(World world, War war, Realm realm, Region region)
    {
        var theirs = war.IsAttacker(realm.Id) ? war.Defenders : war.Attackers;
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        return Realms.SovereignOf(world, region) is { } owner && theirs.Contains(owner.Id) && !ours.Contains(region.OccupierRealmId);
    }

    private static bool EnemyArmyIn(World world, Army army, long regionId)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.RegionId == regionId && a.WarId == war.Id && enemies.Contains(a.RealmId));
    }

    /// <summary>Two realms fight on opposite sides of some ongoing war.</summary>
    private static bool AtWarWith(World world, long a, long b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.Attackers.Contains(a) && w.Defenders.Contains(b) || w.Attackers.Contains(b) && w.Defenders.Contains(a)));

    private static bool Strong(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        var theirs = Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId)).Sum(a => a.Soldiers);
        return army.Soldiers >= theirs * 1.2;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CampaignTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 361` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): raise armies and march them on campaign

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Battles

Where hostile armies meet, the largest of each side fight. Strength weighs troops by quality (knights and mages count for many levies) and scales by the commander's martial skill, morale and supply; defenders are favoured on their own ground and in rough terrain, and a seeded roll decides. Losers take heavier casualties and retreat, or break and disband. Every commander and knight present faces a fate chosen by the arbiter (losers are far likelier to fall), so a battle death cites the battle, which cites the war, which cites its casus belli.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/MilitarySystem.Battles.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/BattleTests.cs`

**Interfaces:**
- Consumes: Task 6; `Encounter`, `Fates.Die`, `Fates.Injure` (Plan 3).
- Produces: `MilitarySystem.Strength(world, army, region)`, `MilitarySystem.BattleFate(character, won)`. Events: `BattleFought` (actors `victor`, `vanquished`, `war`; payload `victor losses`, `vanquished losses`), `ArmyRouted`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/BattleTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class BattleTests
{
    private sealed class Fixed(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static (World World, EventLog Log, War War, Army Host, Army Guard) Armies()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var host = MilitarySystem.Mobilise(ctx, war, world.Realms[1]);
        var guard = MilitarySystem.Mobilise(ctx, war, world.Realms[2]);
        return (world, log, war, host, guard);
    }

    [Fact]
    public void Armies_InTheSameRegion_Fight()
    {
        var (world, log, war, host, guard) = Armies();
        host.Levies = 2000;
        guard.Levies = 20;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(new Fixed("unharmed")) }, checkInvariants: true);

        sim.AdvanceDays(30);

        var battle = sim.Log.All.Single(e => e.Type == "BattleFought");
        Assert.Equal(world.Realms[1].Ref, battle.Actors["victor"]);
        Assert.Equal(1, war.Battles);
        Assert.True(war.BattleScore > 0);
        Assert.True(guard.Disbanded);
        Assert.Contains(sim.Log.All, e => e.Type == "ArmyRouted");
    }

    [Fact]
    public void FallenCommanders_DieOfBattle_CitingIt()
    {
        var (world, log, _, host, guard) = Armies();
        host.Levies = 2000;
        guard.Levies = 1000;
        var commander = world.Characters[guard.CommanderId];
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(new Fixed("slain")) }, checkInvariants: true);

        sim.AdvanceDays(30);

        Assert.False(commander.IsAlive);
        var death = sim.Log.All.First(e => e.Type == "CharacterDied" && e.Actors["deceased"] == commander.Ref);
        Assert.Equal("battle", death.Payload["cause"]);
        Assert.Equal("BattleFought", sim.Log.Get(death.Causes.Single().EventId).Type);
    }

    [Fact]
    public void Defenders_AreFavouredAtHome()
    {
        var (world, _, _, host, guard) = Armies();
        host.Levies = guard.Levies = 1000;
        Assert.Equal(MilitarySystem.Strength(world, host, world.Regions[2]), MilitarySystem.Strength(world, guard, world.Regions[2]));
        var outcomes = MilitarySystem.BattleFate(world.Characters[host.CommanderId], won: false).Outcomes;
        Assert.Equal(1.0, outcomes.Sum(o => o.Probability), 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0117: 'MilitarySystem' does not contain a definition for 'Strength'`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/MilitarySystem.Battles.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Battles (spec §6 step 4): where hostile armies meet, the largest of each side fight. Strength comes
/// from troops, the commander's martial skill, morale and supply, with defenders favoured on their own
/// ground and in rough terrain; a seeded roll decides. Losers suffer heavier casualties and retreat (or
/// break and disband); every commander and knight present faces a fate chosen by the arbiter.
/// </summary>
public sealed partial class MilitarySystem
{
    partial void FightBattles(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var regionId in Active(world).Where(a => a.WarId == war.Id).Select(a => a.RegionId).Distinct().OrderBy(id => id).ToList())
            {
                var here = Active(world).Where(a => a.WarId == war.Id && a.RegionId == regionId).ToList();
                var attacker = here.Where(a => war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                var defender = here.Where(a => !war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                if (attacker is not null && defender is not null) Battle(ctx, war, attacker, defender, world.Regions[regionId]);
            }
    }

    /// <summary>Fighting strength: troops weighted by quality, scaled by commander, morale and supply.</summary>
    public static double Strength(World world, Army army, Region region)
    {
        var magic = region.LeyPower / 50.0;
        var troops = army.Levies + army.MenAtArms * 3.0 + army.Knights.Count * 25 + army.Mages * 30 * magic;
        var martial = world.Characters.TryGetValue(army.CommanderId, out var c) ? c.Attributes.Martial : 5;
        return troops * (0.6 + martial / 25.0) * (army.Morale / 100 + 0.2) * (0.5 + army.Supply / 200);
    }

    private void Battle(SimContext ctx, War war, Army attacker, Army defender, Region region)
    {
        var world = ctx.World;
        var a = Strength(world, attacker, region);
        var d = Strength(world, defender, region);
        if (Friendly(world, war, world.Realms[defender.RealmId], region)) d *= 1.2;
        if (region.Biome is Biome.Hills or Biome.Mountain or Biome.Forest or Biome.Swamp or Biome.Jungle) d *= 1.15;
        var attackerWins = ctx.Rng.Chance(a / Math.Max(1, a + d));
        var (winner, loser) = attackerWins ? (attacker, defender) : (defender, attacker);
        var winnerLosses = Casualties(winner, ctx.Rng.NextInt(5, 16) / 100.0);
        var loserLosses = Casualties(loser, ctx.Rng.NextInt(25, 41) / 100.0);

        var actors = new Dictionary<string, EntityRef>
        {
            ["victor"] = world.Realms[winner.RealmId].Ref,
            ["vanquished"] = world.Realms[loser.RealmId].Ref,
            ["war"] = war.Ref,
        };
        if (world.Characters.TryGetValue(winner.CommanderId, out var victorious)) actors["victor commander"] = victorious.Ref;
        if (world.Characters.TryGetValue(loser.CommanderId, out var defeated)) actors["vanquished commander"] = defeated.Ref;
        var previous = ctx.Log.Involving(war.Ref).LastOrDefault(e => e.Type is "BattleFought" or "SiegeEnded");
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        if (previous is not null) causes.Add(new Cause(previous.Id, CauseKind.Enabler, 0.4));
        var battle = ctx.Emit(new EventDraft("BattleFought", Actors: actors, Place: region.Ref,
            Payload: new Dictionary<string, string>
            {
                ["victor losses"] = winnerLosses.ToString(CultureInfo.InvariantCulture),
                ["vanquished losses"] = loserLosses.ToString(CultureInfo.InvariantCulture),
            },
            Causes: causes, Importance: Math.Clamp(30 + (winnerLosses + loserLosses) / 100, 30, 75), Tags: new[] { "War" }));

        war.Battles++;
        var share = (winnerLosses + loserLosses) / (double)Math.Max(1, attacker.Soldiers + defender.Soldiers + winnerLosses + loserLosses);
        war.BattleScore = Math.Clamp(war.BattleScore + (attackerWins ? 1 : -1) * (5 + 20 * share), -60, 60);
        winner.Morale = Math.Min(100, winner.Morale + 10);
        loser.Morale = Math.Max(0, loser.Morale - 25);
        Statecraft.Remember(world.Realms[loser.RealmId], battle.Id, winner.RealmId, -40, 60);

        foreach (var (army, won, foe) in new[] { (winner, true, loser), (loser, false, winner) })
            foreach (var id in army.Knights.Append(army.CommanderId).Where(id => id != 0).ToList())
                if (world.Characters.TryGetValue(id, out var c) && c.IsActive)
                    FaceBattle(ctx, c, won, battle, world.Characters.GetValueOrDefault(foe.CommanderId));

        if (loser.Morale < 20 || loser.Soldiers < 50)
        {
            ctx.Emit(new EventDraft("ArmyRouted", Actors: new Dictionary<string, EntityRef> { ["army"] = loser.Ref, ["realm"] = world.Realms[loser.RealmId].Ref },
                Place: region.Ref, Causes: new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) }, Importance: 35, Tags: new[] { "War" }));
            Statecraft.Disband(world, loser);
        }
        else
        {
            loser.Objective = ArmyObjective.Return;
            loser.TargetRegionId = 0;
        }
    }

    private static int Casualties(Army army, double share)
    {
        var levies = (int)(army.Levies * share);
        var menAtArms = (int)(army.MenAtArms * share);
        var mages = (int)(army.Mages * share);
        army.Levies -= levies;
        army.MenAtArms -= menAtArms;
        army.Mages -= mages;
        return levies + menAtArms + mages;
    }

    /// <summary>The legal fates of a notable in battle; losers are far likelier to fall.</summary>
    public static Encounter BattleFate(Character c, bool won)
    {
        var (dead, maimed, wounded) = won ? (0.03, 0.03, 0.12) : (0.12, 0.08, 0.2);
        return new Encounter("battle", c.Id, 0, 1, 1, true, new[]
        {
            new EncounterOutcome("unharmed", 1 - dead - maimed - wounded, won, Fate.Unharmed, Fate.Unharmed),
            new EncounterOutcome("wounded", wounded, won, Fate.Wounded, Fate.Unharmed),
            new EncounterOutcome("maimed", maimed, won, Fate.Maimed, Fate.Unharmed),
            new EncounterOutcome("slain", dead, won, Fate.Dead, Fate.Unharmed),
        });
    }

    private void FaceBattle(SimContext ctx, Character c, bool won, WorldEvent battle, Character? enemyCommander)
    {
        var fate = _arbiter.Resolve(BattleFate(c, won), ctx.Rng).AttackerFate;
        if (fate == Fate.Dead)
        {
            Fates.Die(ctx, c, "battle", new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) }, enemyCommander is { IsActive: true } ? enemyCommander : null);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
        if (!won) return;
        c.Renown = Math.Min(100, c.Renown + 6);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~BattleTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 364` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): fight battles between hostile armies

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Sieges and occupation

An army that reaches its target besieges the region's chief settlement: undefended places fall at once, walled ones after months of pressure against walls and garrison. A cruel commander sacks what falls. Defenders who return to occupied land unopposed liberate it. War score is occupation (enemy capitals count more) plus the tally of battles.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/MilitarySystem.Sieges.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/SiegeTests.cs`

**Interfaces:**
- Consumes: Tasks 6–7.
- Produces: siege, occupation and score logic in `MilitarySystem.Sieges.cs`. Events: `SiegeBegan`, `SiegeEnded` (payload `outcome` when the place fell), `RegionOccupied`, `SettlementSacked`, `RegionLiberated`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/SiegeTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SiegeTests
{
    /// <summary>Realm 1's host stands at the target; realm 2 has no army (it fell back elsewhere).</summary>
    private static (World World, EventLog Log, War War, Army Host, Simulator Sim) AtTheGates(int walls, int garrison, string[]? commanderTraits = null)
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var town = world.Settlements[2];
        (town.Walls, town.Garrison) = (walls, garrison);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var host = MilitarySystem.Mobilise(ctx, war, world.Realms[1]);
        host.Levies = 3000;
        if (commanderTraits is not null) world.Characters[host.CommanderId].Traits.AddRange(commanderTraits);
        var guard = MilitarySystem.Mobilise(ctx, war, world.Realms[2]);
        Statecraft.Disband(world, guard);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem() }, checkInvariants: true);
        return (world, log, war, host, sim);
    }

    [Fact]
    public void UndefendedRegion_FallsAtOnce_AndScores()
    {
        var (world, _, war, _, sim) = AtTheGates(walls: 0, garrison: 10);
        sim.AdvanceDays(30);
        Assert.Equal(1, world.Regions[2].OccupierRealmId);
        Assert.Single(sim.Log.All, e => e.Type == "RegionOccupied");
        Assert.True(war.Score >= 10);
    }

    [Fact]
    public void WalledTown_IsBesiegedForMonths_ThenFalls()
    {
        var (world, _, _, _, sim) = AtTheGates(walls: 2, garrison: 100);
        sim.AdvanceDays(30);
        Assert.Single(sim.Log.All, e => e.Type == "SiegeBegan");
        Assert.Equal(0, world.Regions[2].OccupierRealmId);

        sim.AdvanceDays(90);

        var fell = sim.Log.All.First(e => e.Type == "SiegeEnded");
        Assert.Equal("SiegeBegan", sim.Log.Get(fell.Causes[0].EventId).Type);
        Assert.Equal(1, world.Regions[2].OccupierRealmId);
        Assert.Equal(1, world.Settlements[2].Walls);
    }

    [Fact]
    public void CruelCommanders_SackWhatFalls()
    {
        var (world, _, _, _, sim) = AtTheGates(walls: 1, garrison: 100, commanderTraits: new[] { Traits.Cruel });
        var people = world.Settlements[2].Population;
        sim.AdvanceDays(360 * 2);
        var sack = sim.Log.All.Single(e => e.Type == "SettlementSacked");
        Assert.Equal("SiegeEnded", sim.Log.Get(sack.Causes.Single().EventId).Type);
        Assert.True(world.Settlements[2].Population < people * 0.95);
    }

    [Fact]
    public void Defenders_LiberateOccupiedLand()
    {
        var (world, log, war, host, sim) = AtTheGates(walls: 0, garrison: 10);
        sim.AdvanceDays(30);
        Statecraft.Disband(world, host);
        var relief = MilitarySystem.Mobilise(new SimContext(world, log, new EventQueue(), new Rng(2)), war, world.Realms[2]);
        relief.RegionId = 2;
        sim.AdvanceDays(30);
        Assert.Equal(0, world.Regions[2].OccupierRealmId);
        Assert.Contains(sim.Log.All, e => e.Type == "RegionLiberated");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 4 failing.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/MilitarySystem.Sieges.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Sieges and occupation (spec §6 steps 5–6). An army that reaches its target besieges the region's chief
/// settlement: undefended places fall at once, walled ones after months of pressure against walls and
/// garrison, and a cruel commander sacks what falls. Defenders who return to occupied land unopposed
/// liberate it. War score is occupation (enemy capitals count more) plus the tally of battles.
/// </summary>
public sealed partial class MilitarySystem
{
    partial void Besiege(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var region in world.Regions.Values.Where(r => r.BesiegerArmyId != 0).ToList())
        {
            var besieger = world.Armies[region.BesiegerArmyId];
            if (besieger.Disbanded || besieger.RegionId != region.Id || besieger.Objective != ArmyObjective.Siege)
            {
                region.BesiegerArmyId = 0;
                region.SiegeProgress = 0;
            }
        }

        foreach (var army in Active(world).Where(a => a.Objective == ArmyObjective.Siege && a.RegionId == a.TargetRegionId).ToList())
        {
            if (army.Disbanded) continue;
            var war = world.Wars[army.WarId];
            var realm = world.Realms[army.RealmId];
            var region = world.Regions[army.RegionId];
            if (!Hostile(world, war, realm, region) || Contested(world, war, army)) continue;
            var chief = world.LivingSettlements(region).OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
            var defense = chief is null ? 0 : chief.Walls * 400 + chief.Garrison * 3 + 50;
            if (defense <= 150)
            {
                Occupy(ctx, war, army, region, chief, "RegionOccupied", null);
                continue;
            }
            if (region.BesiegerArmyId != army.Id)
            {
                region.BesiegerArmyId = army.Id;
                region.SiegeProgress = 0;
                ctx.Emit(new EventDraft("SiegeBegan", Actors: Actors(world, army, war), Place: chief!.Ref,
                    Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.5) }, Importance: 35, Tags: new[] { "War" }));
            }
            region.SiegeProgress = Math.Min(100, region.SiegeProgress + Strength(world, army, region) / defense * 25);
            if (region.SiegeProgress >= 100) Occupy(ctx, war, army, region, chief, "SiegeEnded", chief);
        }

        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0).ToList())
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var liberator = Active(world).Where(a => a.RegionId == region.Id).OrderBy(a => a.Id).FirstOrDefault(a =>
            {
                var w = world.Wars[a.WarId];
                var ours = w.IsAttacker(a.RealmId) ? w.Attackers : w.Defenders;
                var theirs = w.IsAttacker(a.RealmId) ? w.Defenders : w.Attackers;
                return ours.Contains(owner.Id) && theirs.Contains(region.OccupierRealmId);
            });
            if (liberator is null || Contested(world, world.Wars[liberator.WarId], liberator)) continue;
            region.OccupierRealmId = 0;
            ctx.Emit(new EventDraft("RegionLiberated", Actors: Actors(world, liberator, world.Wars[liberator.WarId]), Place: region.Ref,
                Importance: 35, Tags: new[] { "War" }));
        }
    }

    partial void Score(SimContext ctx, War war)
    {
        var world = ctx.World;
        var occupation = 0.0;
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var weight = world.Realms.Values.Any(r => !r.Destroyed && world.Settlements[r.CapitalId].RegionId == region.Id && r.LiegeId == 0) ? 25 : 10;
            if (war.Attackers.Contains(region.OccupierRealmId) && war.Defenders.Contains(owner.Id)) occupation += weight;
            if (war.Defenders.Contains(region.OccupierRealmId) && war.Attackers.Contains(owner.Id)) occupation -= weight;
        }
        war.Score = Math.Clamp(occupation + war.BattleScore, -100, 100);
    }

    private static void Occupy(SimContext ctx, War war, Army army, Region region, Settlement? chief, string type, Settlement? sacked)
    {
        var world = ctx.World;
        region.OccupierRealmId = army.RealmId;
        region.BesiegerArmyId = 0;
        region.SiegeProgress = 0;
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        var began = ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
        if (type == "SiegeEnded" && chief is not null)
        {
            var siege = ctx.Log.Involving(chief.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
            if (siege is not null) causes.Insert(0, new Cause(siege.Id, CauseKind.Trigger, 1.0));
        }
        else if (began is not null) causes.Add(new Cause(began.Id, CauseKind.Enabler, 0.3));
        var fell = ctx.Emit(new EventDraft(type, Actors: Actors(world, army, war), Place: chief?.Ref ?? region.Ref,
            Payload: type == "SiegeEnded" ? new Dictionary<string, string> { ["outcome"] = "fell" } : null,
            Causes: causes, Importance: type == "SiegeEnded" ? 50 : 30, Tags: new[] { "War" }));
        if (chief is not null)
        {
            chief.Garrison = 0;
            chief.Walls = Math.Max(0, chief.Walls - 1);
        }
        Statecraft.Remember(Realms.SovereignOf(world, region)!, fell.Id, army.RealmId, -50, 70);

        if (sacked is not null && world.Characters.TryGetValue(army.CommanderId, out var commander) && commander.Has(Traits.Cruel))
        {
            var dead = Demography.Kill(sacked, 0.1, ctx.Rng);
            sacked.Wealth *= 0.5;
            var sack = ctx.Emit(new EventDraft("SettlementSacked",
                Actors: new Dictionary<string, EntityRef> { ["commander"] = commander.Ref, ["war"] = war.Ref },
                Place: sacked.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                Causes: new[] { new Cause(fell.Id, CauseKind.Trigger, 1.0) }, Importance: 60, Tags: new[] { "War" }));
            Statecraft.Remember(Realms.SovereignOf(world, region)!, sack.Id, army.RealmId, -90, 100);
        }
    }

    /// <summary>An enemy army stands in the same region.</summary>
    private static bool Contested(World world, War war, Army army)
    {
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.WarId == war.Id && a.RegionId == army.RegionId && enemies.Contains(a.RealmId));
    }

    private static Dictionary<string, EntityRef> Actors(World world, Army army, War war)
    {
        var actors = new Dictionary<string, EntityRef> { ["realm"] = world.Realms[army.RealmId].Ref, ["army"] = army.Ref, ["war"] = war.Ref };
        if (world.Characters.TryGetValue(army.CommanderId, out var commander)) actors["commander"] = commander.Ref;
        return actors;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~SiegeTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 368` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): besiege, occupy and liberate regions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Peace

At a war score of ±50 the losing leader weighs terms against fighting on (a decision point); stalemates end in a white peace after five years, and every war after ten. Terms follow the casus belli: independence confirmed or vassalage restored, a stolen artifact returned, or the winner keeps the land it occupies (at least the target), and a crushed, much smaller loser becomes its vassal. A defender who wins takes reparations. Then armies go home and occupations end; both realms remember the war.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/MilitarySystem.Peace.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/PeaceTests.cs`

**Interfaces:**
- Consumes: Tasks 3, 6–8.
- Produces: `MilitarySystem.DecisiveScore = 50`, `MilitarySystem.MakePeace(ctx, war, attackerWon, white)`. Events: `PeaceSigned` (payload `terms`, `outcome`), `RegionCeded`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/PeaceTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class PeaceTests
{
    private sealed class Choose(string key) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) => brief.Options.Single(o => o.Key == key);
    }

    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Conquest_CedesOccupiedLand_AndAFallenRealmIsGone()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        world.Regions[2].OccupierRealmId = 1;
        war.Score = 70;

        var peace = MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);

        Assert.True(war.Ended);
        Assert.Equal("attackers won", peace.Payload["outcome"]);
        Assert.Equal(1, world.Regions[2].OwnerRealmId);
        Assert.True(world.Realms[2].Destroyed);
        Assert.Contains(log.All, e => e.Type == "RegionCeded" && e.Causes[0].EventId == peace.Id);
        Assert.Contains(log.All, e => e.Type == "RealmFell");
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Losers_AcceptTerms_AtADecisiveScore()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        war.BattleScore = 60;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(decider: new Choose("accept")) }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.True(war.Ended);
        Assert.All(world.Armies.Values, a => Assert.True(a.Disbanded));
    }

    [Fact]
    public void StubbornLosers_FightOn()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        war.BattleScore = 60;
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MilitarySystem(decider: new Choose("fight")) }, checkInvariants: true);
        sim.AdvanceDays(30);
        Assert.False(war.Ended);
    }

    [Fact]
    public void Stalemates_EndInWhitePeace()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        world.Date = new WorldDate(world.Date.Day + 360 * 5);
        var peace = MilitarySystem.MakePeace(Context(world, log), war, attackerWon: false, white: true);
        Assert.Equal("status quo", peace.Payload["terms"]);
        Assert.False(world.Realms[2].Destroyed);
        Assert.Equal(2, world.Regions[2].OwnerRealmId);
    }

    [Fact]
    public void FailedRebellion_RestoresVassalage()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var declared = log.Append(world.Date, new EventDraft("IndependenceDeclared"));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Rebellion", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 },
            CasusBelli = CasusBelli.Independence, CasusBelliEventId = declared.Id, DeclaredEventId = declared.Id,
        };
        world.Wars[war.Id] = war;
        world.Realms[2].Treaties.Add(new Treaty(TreatyKind.Rivalry, 1, declared.Id, 0));
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);
        Assert.Equal(1, world.Realms[2].LiegeId);
        Assert.Empty(world.Realms[2].Treaties);
        Assert.Contains("vassalage restored", log.All.Last().Payload["terms"]);
    }

    [Fact]
    public void SuccessfulRebellion_ConfirmsIndependence()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var declared = log.Append(world.Date, new EventDraft("IndependenceDeclared"));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Rebellion", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 },
            CasusBelli = CasusBelli.Independence, CasusBelliEventId = declared.Id, DeclaredEventId = declared.Id,
        };
        world.Wars[war.Id] = war;
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: false, white: false);
        Assert.Equal(0, world.Realms[1].LiegeId);
        Assert.Equal(0, world.Realms[2].LiegeId);
        Assert.Equal("independence", log.All.Last().Payload["terms"]);
    }

    [Fact]
    public void ArtifactRecovery_ReturnsTheRelic()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var holder = world.Characters[world.Settlements[2].LordId];
        var crown = TestPeople.GiveArtifact(world, holder);
        world.Wars[war.Id] = new War
        {
            Id = war.Id, Name = war.Name, AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 },
            CasusBelli = CasusBelli.ArtifactRecovery, CasusBelliEventId = war.CasusBelliEventId, DeclaredEventId = war.DeclaredEventId,
            TargetArtifactId = crown.Id,
        };
        MilitarySystem.MakePeace(Context(world, log), world.Wars[war.Id], attackerWon: true, white: false);
        Assert.Equal(world.Settlements[1].LordId, crown.HolderId);
        Assert.Equal("returned", crown.Provenance.Last().How);
    }

    [Fact]
    public void AVictorWithNothingToTake_TakesTribute()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log, target: 1);
        var (theirs, ours) = (world.Realms[2].Treasury, world.Realms[1].Treasury);
        MilitarySystem.MakePeace(Context(world, log), war, attackerWon: true, white: false);
        Assert.Equal("tribute", log.All.Last().Payload["terms"]);
        Assert.True(world.Realms[1].Treasury > ours);
        Assert.True(world.Realms[2].Treasury < theirs);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0117: 'MilitarySystem' does not contain a definition for 'MakePeace'`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/MilitarySystem.Peace.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Peace (spec §6 step 6). At a war score of ±50 the losing leader weighs terms against fighting on
/// (a decision point); stalemates end in a white peace after five years, and any war after ten. Terms
/// follow the casus belli: independence confirmed or vassalage restored, a stolen artifact returned,
/// or the winner keeps the land it occupies (at least the war's target), and a crushed, much smaller loser becomes its vassal.
/// A defender who wins takes reparations. Then armies go home and occupations end.
/// </summary>
public sealed partial class MilitarySystem
{
    public const double DecisiveScore = 50;

    partial void ConsiderPeace(SimContext ctx, War war)
    {
        var world = ctx.World;
        bool Standing(long id) => world.Realms.TryGetValue(id, out var r) && !r.Destroyed;
        if (!Standing(war.AttackerId) || !war.Attackers.Any(Standing)) { MakePeace(ctx, war, attackerWon: false, white: !Standing(war.DefenderId)); return; }
        if (!Standing(war.DefenderId) || !war.Defenders.Any(Standing)) { MakePeace(ctx, war, attackerWon: true, white: false); return; }

        var years = (ctx.Date.Day - war.StartDay) / (double)Core.WorldDate.DaysPerYear;
        if (Math.Abs(war.Score) >= DecisiveScore)
        {
            var attackerWon = war.Score > 0;
            var loser = world.Realms[attackerWon ? war.DefenderId : war.AttackerId];
            var ruler = Realms.RulerOf(world, loser);
            var accept = ruler is null || _decider.Decide(new DecisionBrief(ruler.Id, "sue for peace", new[]
            {
                new DecisionOption("accept", 30 + Math.Abs(war.Score), "accept the victor's terms"),
                new DecisionOption("fight", Math.Max(5, 100 - Math.Abs(war.Score) - loser.WarWeariness), "fight on"),
            }), ctx.Rng).Key == "accept";
            if (accept) MakePeace(ctx, war, attackerWon, white: false);
        }
        else if (years >= 5 && Math.Abs(war.Score) < 25) MakePeace(ctx, war, attackerWon: false, white: true);
        else if (years >= 10) MakePeace(ctx, war, attackerWon: war.Score > 0, white: Math.Abs(war.Score) < 10);
    }

    public static WorldEvent MakePeace(SimContext ctx, War war, bool attackerWon, bool white)
    {
        var world = ctx.World;
        var winner = world.Realms[attackerWon ? war.AttackerId : war.DefenderId];
        var loser = world.Realms[attackerWon ? war.DefenderId : war.AttackerId];
        var winners = attackerWon ? war.Attackers : war.Defenders;
        var terms = new List<string>();
        var cessions = new List<Region>();
        var vassalise = false;

        if (white) terms.Add("status quo");
        else if (war.CasusBelli == CasusBelli.Independence)
        {
            // The former liege declares this war: winning restores its vassal, losing confirms independence.
            if (attackerWon)
            {
                terms.Add("vassalage restored");
                vassalise = true;
            }
            else terms.Add("independence");
        }
        else
        {
            if (attackerWon && war.CasusBelli == CasusBelli.ArtifactRecovery && world.Artifacts.TryGetValue(war.TargetArtifactId, out var relic)
                && relic.Place != ArtifactPlace.Destroyed)
                terms.Add($"return of {relic.Name}");
            cessions = world.Regions.Values
                .Where(r => winners.Contains(r.OccupierRealmId) && Realms.SovereignOf(world, r) is { } owner
                            && (attackerWon ? war.Defenders : war.Attackers).Contains(owner.Id))
                .OrderBy(r => r.Id).ToList();
            if (attackerWon && cessions.Count == 0 && world.Regions.TryGetValue(war.TargetRegionId, out var target)
                && Realms.SovereignOf(world, target) is { } held && war.Defenders.Contains(held.Id))
                cessions.Add(target);
            if (cessions.Count > 0) terms.Add($"{cessions.Count} region(s) ceded");
            var loserSize = Realms.Territory(world, loser).Count() - cessions.Count;
            if (attackerWon && Math.Abs(war.Score) >= 90 && loserSize > 0 && loserSize * 2 <= Realms.Territory(world, winner).Count())
            {
                terms.Add("vassalage");
                vassalise = true;
            }
            if (!attackerWon || terms.Count == 0)
            {
                // Defenders take reparations; an attacker with nothing else to gain takes tribute.
                var reparations = Math.Max(0, loser.Treasury) * 0.3;
                loser.Treasury -= reparations;
                winner.Treasury += reparations;
                terms.Add(attackerWon ? "tribute" : "reparations");
            }
        }

        var decisive = ctx.Log.Involving(war.Ref).LastOrDefault(e => e.Type is "BattleFought" or "SiegeEnded" or "RegionOccupied");
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.6) };
        if (decisive is not null) causes.Insert(0, new Cause(decisive.Id, CauseKind.Trigger, 0.9));
        var peace = ctx.Emit(new EventDraft("PeaceSigned",
            Actors: new Dictionary<string, EntityRef> { ["victor"] = winner.Ref, ["vanquished"] = loser.Ref, ["war"] = war.Ref },
            Place: world.Settlements[loser.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["terms"] = string.Join("; ", terms), ["outcome"] = white ? "white peace" : attackerWon ? "attackers won" : "defenders won" },
            Causes: causes, Importance: white ? 45 : 65, Tags: new[] { "War", "Politics" }));
        war.Ended = true;
        war.EndedEventId = peace.Id;

        foreach (var army in Active(world).Where(a => a.WarId == war.Id).ToList()) Statecraft.Disband(world, army);
        // End this war's occupations (not those of other wars), including land whose sovereign changed mid-war.
        foreach (var region in world.Regions.Values.Where(r => war.Involves(r.OccupierRealmId)
                     && (Realms.SovereignOf(world, r) is not { } owner || war.Involves(owner.Id) || !AtWarWith(world, r.OccupierRealmId, owner.Id))))
            region.OccupierRealmId = 0;

        if (!white && terms.Any(t => t.StartsWith("return of", StringComparison.Ordinal)) && Realms.RulerOf(world, winner) is { } claimant)
            Fates.MoveToHolder(world.Artifacts[war.TargetArtifactId], claimant, peace.Id, "returned");
        foreach (var region in cessions.Where(r => !winner.Destroyed && r.OwnerRealmId != 0))
        {
            var from = world.Realms[region.OwnerRealmId];
            if (from.Destroyed) continue;
            Statecraft.Cede(ctx, region, from, winner, new[] { new Cause(peace.Id, CauseKind.Trigger, 1.0) });
        }
        if (vassalise && !loser.Destroyed && !Realms.IsBelow(world, winner, loser))
        {
            // A vassal has no foreign policy of its own: its treaties lapse.
            loser.LiegeId = winner.Id;
            loser.Treaties.Clear();
            foreach (var other in world.Realms.Values) other.Treaties.RemoveAll(t => t.With == loser.Id);
        }

        if (!white)
        {
            Statecraft.Remember(loser, peace.Id, winner.Id, -60, 90);
            if (Realms.RulerOf(world, winner) is { } victor) victor.Renown = Math.Min(100, victor.Renown + 10);
        }
        return peace;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PeaceTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 376` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): end wars with terms that follow the casus belli

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Realm systems in the world, and CLI

The full system order is now Life → Economy → Regions → Settlements → Realms → Diplomacy → Military → Family → Relations → Deeds → Artifacts → Promotion (spec §7: politics after the land, characters after politics). The CLI shows the political world: `fw realms`, `fw realm <id>` (ruler, laws, council, vassals, treaties, wars), `fw wars`, `fw war <id>` (sides, casus belli, score, battles) and a `--layer realms` map; a realm's detail lists its named eras.

**Files:**
- Create: `sim/FantasyWorld.Cli/CliApp.Realms.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Cli/MapRenderer.cs`, `sim/FantasyWorld.Sim/Query/HistoryQuery.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs`, `sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `DefaultSystems.Create()` with twelve systems; `HistoryQuery.Eras(EntityRef realm)`; `CliApp.Realms.cs`; CLI commands `realms`, `realm <id>`, `wars`, `war <id>`; `fw map --layer realms`.

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

    [Fact]
    public void Characters_ListsTheNotables()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "characters", file.Path);
        Assert.Equal(0, code);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(world.ActiveCharacters.Count() + 1, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Lord", output);
    }

    [Fact]
    public void Life_TellsABiography()
    {
        using var file = new TempFile();
        NewTiny(file);
        Run(T0, "advance", file.Path, "20y");
        long lordId;
        using (var store = WorldStore.Open(file.Path))
            lordId = store.Load().World.Characters.Values.First(c => c.FatherId == 0 && c.Children.Count > 0).Id;

        var (code, output) = Run(T0, "life", file.Path, lordId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, code);
        Assert.Contains($"(char#{lordId})", output);
        Assert.Contains("traits:", output);
        Assert.Contains("children:", output);
        Assert.Contains("Life:", output);
    }

    [Fact]
    public void Artifacts_ShowWhereaboutsAndProvenance()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "artifacts", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("held by", output);

        var (detailCode, detail) = Run(T0, "artifact", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("(artifact#1)", detail);
        Assert.Contains("powers:", detail);
        Assert.Contains("Made before recorded history.", detail);
    }

    [Fact]
    public void UnknownCharacter_IsAnError()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "life", file.Path, "99999");
        Assert.Equal(1, code);
        Assert.Contains("error: No character #99999", output);
    }

    [Fact]
    public void Realms_AndTheRealmsMap()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "realms", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("Kingdom of", output);
        var (mapCode, map) = Run(T0, "map", file.Path, "--layer", "realms");
        Assert.Equal(0, mapCode);
        Assert.Contains("unclaimed", map);
        var (detailCode, detail) = Run(T0, "realm", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("(realm#1)", detail);
        Assert.Contains("succession", detail);
    }

    [Fact]
    public void Wars_ListAndDetail()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), T0);
        var log = new EventLog();
        var cb = log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = log.Append(world.Date, new EventDraft("WarDeclared"));
        world.Wars[1] = new War
        {
            Id = 1, Name = "The Test War", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 },
            CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id, DeclaredEventId = declared.Id,
        };
        using (var store = WorldStore.Open(file.Path)) store.Save(world, log, new EventQueue());

        var (code, output) = Run(T0, "wars", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("The Test War", output);
        var (detailCode, detail) = Run(T0, "war", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("casus belli: #1", detail);
        Assert.Contains("attackers:", detail);
    }
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

    [Fact]
    public void Life_ListsAnEntitysEvents()
    {
        var log = new EventLog();
        var hero = new EntityRef("char", 7);
        log.Append(new WorldDate(1), new EventDraft("Born", Actors: new Dictionary<string, EntityRef> { ["child"] = hero }));
        log.Append(new WorldDate(2), new EventDraft("Elsewhere"));
        log.Append(new WorldDate(3), new EventDraft("Died", Actors: new Dictionary<string, EntityRef> { ["deceased"] = hero }));
        Assert.Equal(new[] { "Born", "Died" }, new HistoryQuery(log).Life(hero).Select(e => e.Type));
    }

    [Fact]
    public void Eras_ListARealmsAges()
    {
        var log = new EventLog();
        var realm = new EntityRef("realm", 1);
        log.Append(new WorldDate(1), new EventDraft("EraBegan", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm }));
        log.Append(new WorldDate(2), new EventDraft("WarDeclared", Actors: new Dictionary<string, EntityRef> { ["attacker"] = realm }));
        Assert.Single(new HistoryQuery(log).Eras(realm));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'HistoryQuery' does not contain a definition for 'Eras' and no accessible extension method 'Eras' accepting a first argument of type 'HistoryQuery' could be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implement**

`sim/FantasyWorld.Cli/CliApp.Realms.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Cli;

/// <summary>Commands about realms and wars: realms, realm, wars, war.</summary>
public static partial class CliApp
{
    private static int RealmList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id realm                               ruler                          regions legit stab  treasury");
        foreach (var r in world.Realms.Values.Where(r => !r.Destroyed).OrderBy(r => r.LiegeId == 0 ? 0 : 1).ThenBy(r => r.Id))
        {
            var ruler = Realms.RulerOf(world, r);
            var title = (r.LiegeId == 0 ? "" : "  ") + Statecraft.Title(r);
            output.WriteLine(string.Create(Inv,
                $"{r.Id,6} {title,-35} {(ruler is null ? "(vacant)" : FullName(world, ruler)),-30} {Realms.Territory(world, r).Count(),7} {r.Legitimacy,5:0} {r.Stability,4:0} {r.Treasury,9:0}"));
        }
        return 0;
    }

    private static int RealmDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var r = world.Realms.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No realm #{id}.");
        string Name(long who) => world.Characters.TryGetValue(who, out var c) ? $"{FullName(world, c)} (char#{who})" : "—";

        output.WriteLine($"{Statecraft.Title(r)} (realm#{r.Id}){(r.Destroyed ? " — fallen" : "")}");
        output.WriteLine($"  {r.Government}, {r.SuccessionLaw} succession, magic {r.MagicLaw.ToString().ToLowerInvariant()}; colours {string.Join(" and ", r.Colors)}; phase {r.Phase}");
        output.WriteLine($"  capital {world.Settlements[r.CapitalId].Name}; ruler {Name(Realms.RulerOf(world, r)?.Id ?? 0)}");
        if (r.LiegeId != 0) output.WriteLine($"  vassal of {Statecraft.Title(world.Realms[r.LiegeId])} (realm#{r.LiegeId})");
        output.WriteLine(string.Create(Inv, $"  legitimacy {r.Legitimacy:0}, stability {r.Stability:0}, treasury {r.Treasury:0}, war weariness {r.WarWeariness:0}"));
        output.WriteLine($"  culture {world.Cultures.GetValueOrDefault(r.CultureId)?.Name ?? "?"}, faith {world.Faiths.GetValueOrDefault(r.FaithId)?.Name ?? "?"}");
        if (r.Council.Count > 0) output.WriteLine("  council: " + string.Join(", ", r.Council.Select(s => $"{s.Key} {Name(s.Value)}")));
        var vassals = Realms.VassalsOf(world, r).ToList();
        if (vassals.Count > 0) output.WriteLine("  vassals: " + string.Join(", ", vassals.Select(v => $"{Statecraft.Title(v)} (realm#{v.Id})")));
        if (r.Treaties.Count > 0)
            output.WriteLine("  treaties: " + string.Join(", ", r.Treaties.Select(t => $"{t.Kind.ToString().ToLowerInvariant()} with {world.Realms[t.With].Name}")));
        output.WriteLine("  regions: " + string.Join(", ", Realms.Domain(world, r).Select(x => x.Name)));
        var eras = new HistoryQuery(log).Eras(r.Ref);
        if (eras.Count > 0)
        {
            output.WriteLine("  Eras:");
            foreach (var era in eras) output.WriteLine($"    {era.Date}: {era.Payload["era"]}");
        }
        return 0;
    }

    private static int WarList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id war                                      began         casus belli         score  status");
        foreach (var w in world.Wars.Values)
            output.WriteLine(string.Create(Inv,
                $"{w.Id,6} {w.Name,-40} {new Sim.Core.WorldDate(w.StartDay),-13} {w.CasusBelli,-18} {w.Score,6:0}  {(w.Ended ? "ended" : "raging")}"));
        return 0;
    }

    private static int WarDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var w = world.Wars.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No war #{id}.");
        string Side(IEnumerable<long> realms) => string.Join(", ", realms.Select(r => Statecraft.Title(world.Realms[r])));
        output.WriteLine($"{w.Name} (war#{w.Id}) — {w.CasusBelli}, score {w.Score.ToString("0", Inv)}, {w.Battles} battle(s){(w.Ended ? "" : ", raging")}");
        output.WriteLine($"  attackers: {Side(w.Attackers.DefaultIfEmpty(w.AttackerId))}");
        output.WriteLine($"  defenders: {Side(w.Defenders.DefaultIfEmpty(w.DefenderId))}");
        output.WriteLine("  casus belli: " + EventText.Describe(log.Get(w.CasusBelliEventId), NameOf(world)));
        output.WriteLine("  Course:");
        foreach (var ev in new HistoryQuery(log).Life(w.Ref)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
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
using FantasyWorld.Sim.Events;
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
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                "realms" => RealmList(path, output),
                "realm" => RealmDetail(path, rest, output),
                "wars" => WarList(path, output),
                "war" => WarDetail(path, rest, output),
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

    private static Func<EntityRef, string?> NameOf(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? FullName(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        Realm.Kind => world.Realms.TryGetValue(entity.Id, out var realm) ? Statecraft.Title(realm) : null,
        War.Kind => world.Wars.TryGetValue(entity.Id, out var war) ? war.Name : null,
        Army.Kind => world.Armies.TryGetValue(entity.Id, out var army) ? army.Name : null,
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

Replace `sim/FantasyWorld.Cli/MapRenderer.cs` entirely with:
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
                        : layer == "realms"
                            ? RealmGlyph(world, geography.RegionOf[i])
                            : Glyph(grid, i));
            }
            text.AppendLine();
        }
        text.Append(layer switch
        {
            "regions" => "Each glyph is a region (id mod 62); @ O o are settlements.",
            "realms" => string.Join("  ", Realms.Independent(world).OrderBy(r => r.Id)
                .Select(r => $"{RealmGlyphs[(int)(r.Id % RealmGlyphs.Length)]} {r.Rank} of {r.Name}")) + "  · unclaimed",
            _ => Legend,
        });
        return text.ToString();
    }

    private const string RealmGlyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>Each independent realm's territory (vassals included) shares a letter.</summary>
    private static char RealmGlyph(World world, int regionId)
    {
        if (regionId == 0) return '~';
        var sovereign = Realms.SovereignOf(world, world.Regions[regionId]);
        return sovereign is null ? '·' : RealmGlyphs[(int)(sovereign.Id % RealmGlyphs.Length)];
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

Replace `sim/FantasyWorld.Sim/Query/HistoryQuery.cs` entirely with:
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

    /// <summary>Every event an entity took part in or that happened at it, oldest first (spec "Life"/"History").</summary>
    public IReadOnlyList<WorldEvent> Life(EntityRef entity) => _log.Involving(entity);

    /// <summary>A realm's named eras, oldest first (spec "Eras").</summary>
    public IReadOnlyList<WorldEvent> Eras(EntityRef realm) => _log.Involving(realm).Where(e => e.Type == "EraBegan").ToList();

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

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(),
        new DiplomacySystem(),
        new MilitarySystem(),
        new FamilySystem(),
        new RelationsSystem(),
        new DeedsSystem(),
        new ArtifactSystem(),
        new PromotionSystem(),
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CliAppTests|FullyQualifiedName~HistoryQueryTests"`
Expected: `Passed!  - Failed: 0, Passed: 31`. Then `dotnet test sim` → `Passed: 379` in total.

- [ ] **Step 5: Smoke-test the real binary**

```bash
dotnet run --project sim/FantasyWorld.Cli -- new worlds/demo.fw.db --seed 7
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/demo.fw.db 100y
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/demo.fw.db
```
Expected: a list of wars with their casus belli and outcome. Pick one and run `fw war worlds/demo.fw.db <id>`: you should see its sides, score, battles and the event that gave it its casus belli. Run `fw realms …` and `fw map … --layer realms` to see the political map, and `fw realm … <id>` for a realm's council, treaties and named eras. Pick a `BattleFought` death and run `fw why worlds/demo.fw.db <id> --depth 5`: death ← battle ← war ← casus belli. Then `rm -r worlds`.

- [ ] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(cli): run realm systems; add realms, wars and political map

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Realm health checks and README

These checks hold the political layer to its promises over two centuries: realms survive and change, wars start for varied reasons and end in peace, regions change hands, and every war traces back to its casus belli.

**Files:**
- Modify: `README.md`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/RealmHealthTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `RealmHealthTests`; together with Task 1's invariants these were run for 200 years on medium worlds with invariants on, clean; README commands for realms and wars.

- [ ] **Step 1: Write the checks**

`sim/FantasyWorld.Sim.Tests/Engine/RealmHealthTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks of realms and war: wars happen and end, every war has a casus belli, the map neither freezes nor collapses.</summary>
public class RealmHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size, bool checkInvariants) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    [Fact]
    public void ACenturyAndAHalf_OfWarAndPeace()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        var world = sim.World;
        sim.AdvanceDays(150 * 360);

        Assert.InRange(world.Wars.Count, 5, 120);
        Assert.Contains(world.Wars.Values, w => w.Ended);
        Assert.All(world.Wars.Values, w => Assert.True(sim.Log.TryGet(w.CasusBelliEventId, out _), $"{w.Name} has no casus belli"));
        Assert.All(world.Wars.Values.Where(w => w.Ended), w => Assert.Equal("PeaceSigned", sim.Log.Get(w.EndedEventId).Type));
        Assert.InRange(Realms.Independent(world).Count(), 2, 20);
        Assert.All(world.Regions.Values, r => Assert.NotNull(Realms.SovereignOf(world, r)));
        Assert.Contains(sim.Log.All, e => e.Type == "BattleFought");
        Assert.Contains(sim.Log.All, e => e.Type == "RegionCeded");
        Assert.Contains(sim.Log.All, e => e.Type == "EraBegan");
        Assert.Empty(Invariants.Check(world, sim.Log));
    }

    [Fact]
    public void Wars_TraceBackThroughTheirCasusBelli()
    {
        var sim = NewSim(7, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(100 * 360);
        var query = new HistoryQuery(sim.Log);
        var declared = sim.Log.All.Where(e => e.Type == "WarDeclared").ToList();
        Assert.NotEmpty(declared);
        Assert.All(declared, d => Assert.NotEmpty(query.WhyChain(d.Id).Causes));
    }

    [Fact]
    public void RealmsAndWars_AreChunkInvariant_AndSurviveSaveLoad()
    {
        using var file = new TempFile();
        var oneShot = NewSim(5, WorldSize.Tiny, checkInvariants: true);
        oneShot.AdvanceDays(80 * 360);

        var chunked = NewSim(5, WorldSize.Tiny, checkInvariants: true);
        chunked.AdvanceDays(40 * 360 + 17);
        using (var store = WorldStore.Open(file.Path)) store.Save(chunked.World, chunked.Log, chunked.Queue);
        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(), checkInvariants: true);
        resumed.AdvanceDays(40 * 360 - 17);

        Assert.Equal(WorldHasher.Hash(oneShot.World, oneShot.Log, oneShot.Queue), WorldHasher.Hash(resumed.World, resumed.Log, resumed.Queue));
    }
}
```

- [ ] **Step 2: Run tests to verify they pass against the finished stack**

Run: `dotnet test sim`
Expected: PASS. These tests pin down behaviour built in the earlier tasks; if one fails, fix the system it names before going on.

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
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/aerenmoor.fw.db       # living notables
dotnet run --project sim/FantasyWorld.Cli -- life worlds/aerenmoor.fw.db <char-id>    # a biography
dotnet run --project sim/FantasyWorld.Cli -- artifacts worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- artifact worlds/aerenmoor.fw.db <id>     # powers and provenance
dotnet run --project sim/FantasyWorld.Cli -- realms worlds/aerenmoor.fw.db           # kingdoms and their vassals
dotnet run --project sim/FantasyWorld.Cli -- realm worlds/aerenmoor.fw.db <id>        # laws, council, treaties, eras
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- war worlds/aerenmoor.fw.db <id>          # casus belli and the course of the war
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db --layer realms
```

`--size tiny` makes a 64×64 world with 12 regions for quick experiments.
````

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RealmHealthTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 382` in total.

- [ ] **Step 5: Run the full suite in Release**

Run: `dotnet test sim --configuration Release`
Expected: `Passed!  - Failed: 0, Passed: 382`

- [ ] **Step 6: Commit**

```bash
git add sim README.md
git commit -m "test(sim): add realm health and war causality checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §6 realm hierarchy (county → empire), vassalage, independence | 1–3 |
| §6 government, succession laws, magic law, council | 1–4 |
| §6 legitimacy and stability (crowns, child rulers, interregnum), lifecycle and eras | 3 |
| §6 diplomacy: opinion, alliances, royal marriages, rivalries, casus belli from history | 5 |
| §6 campaign flow: armies, march, supply, battles, sieges, occupation, war score, peace terms | 6–9 |
| §6 knights and mages as commanders and in battle; notables' battle fates via the arbiter | 6–7 |
| §7 system order; realm and war queries; eras | 10 |
| §10 invariants (owners, capitals, liege cycles, treaties, wars and armies), determinism, history health | 1, 11 |

Deferred: trade routes between regions and myth/magic structures (sub-project 3); LLM decisions and narration of wars (Plan 5); importance-ranked chronicle and live mode (Plan 6).
