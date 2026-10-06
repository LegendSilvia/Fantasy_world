# Characters & Artifacts Implementation Plan (Plan 3 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the world its people. Add cultures and faiths spread over the map, and a cast of individually simulated notables: lords' households, knights, mages, priests, merchants, elders and adventurers, each with traits, attributes, an ambition, memories and family. Add artifacts with powers, curses, fame and provenance. Then let them live: people are born, marry, befriend, feud, hunt monsters, delve for lost relics, duel, steal, forge, inherit and die. Every story beat is an event that cites its motive, so `fw why` on a duel death walks back to the original insult.

**Architecture:** New entities (`Culture`, `Faith`, `House`, `Character`, `Artifact`) live in `World` and the snapshot. World generation adds cultures (graph flood from spread-out homelands) and a starting cast. Behaviour comes from six new systems in spec order after the land systems: `Life` (ageing, death, succession) runs first; `Family`, `Relations`, `Deeds`, `Artifacts` and `Promotion` run after the settlements. Systems still never call each other: shared consequences (death with succession and inheritance, wounds, moving artifacts) live in the `Fates` helper library, and creation lives in `CharacterFactory`. Choices and dramatic conflicts pass through `IDecider` / `IArbiter` (namespace `Agency`), which carry explicit legal options and outcome odds. The rules implementations here are the offline fallback; Plan 5 adds LLM implementations behind the same interfaces.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite 8.0.10, System.Text.Json (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` (§3, §4, §7, §8.2–8.3 interfaces, §10). **Builds on:** Plans 1 and 2 (merged).

## Global Constraints

Everything in Plans 1–2's Global Constraints still applies. In addition:

- **Opinions are derived, never stored:** `Relations.Opinion(a, b)` sums memories about b, trait compatibility and kinship. Systems add `Memory` records, each citing an existing event.
- **One source of truth per fact:** lordship is `Settlement.LordId` (`Character.Role == Lord` exactly for seat holders); an artifact's location is its `Place` plus one of `HolderId` / `SettlementId` / `RegionId`; a character's holdings are derived from artifacts.
- **Death goes through `Fates.Die`**, which handles grief, vengeance, marriage, succession and inheritance. No system kills a character any other way.
- **Choices go through `IDecider`, conflicts through `IArbiter`**, with explicit options and outcome sets. Tests inject fixed deciders and arbiters instead of hunting for seeds.
- **Never scan all characters per character:** history accumulates the dead. Use parent and child links, `ToLookup` per tick, or `ActiveCharacters`. Costs must stay flat over centuries (checked in Task 13 and by the soak below).
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Cast size | Self-regulating at about **4 notables per settlement** (medium ≈ 750): at 16, non-heirs may fade into the populace when the world is crowded | Spec: 500–2,000 notables; without a brake, births compound |
| Sex and marriage | Binary sex used for parentage; marriages pair a woman and a man; succession is gender-neutral (eldest child first) | Simplest model that produces lineages. Same-sex unions with adoption could be added later without breaking anything |
| Lineage | Per culture: 75% patrilineal, 25% matrilineal. Children join the lineage parent's house; couples live with them (lords always keep their seat) | Gives cultures a visible difference cheaply |
| Succession | Eldest living child, else eldest sibling, else the seat is vacant and the ablest resident (or a commoner) rises. Child lords are allowed (regency comes with realms in Plan 4) | Realms' succession laws (spec §6) arrive in Plan 4 |
| Notables' populace | Promotion takes one adult from the settlement's aggregate; notables are otherwise counted separately from the populace | Keeps the two layers independent |
| Artifacts | Start with regions / 5 ancient artifacts (origin before history); forging stops at regions / 2 (medium: 50). Hunters and delvers who die in the wilds lose what they carry there | Spec: 20–60 per world, created rarely, never deleted. Prototype without a cap reached 275 in 200 years |
| Arbitration | Rules arbiter rolls on the legal outcome set; `Encounter.PlausibilityFloor = 0.05` is defined for Plan 5's LLM arbiter | Spec §8.3 |
| Vengeance | Killing a kinsman, or (for wrathful or cruel people) losing a duel or being robbed, can set ambition to vengeance, leading to a duel to the death | Without this, no feud ever starts (prototype: 0 deadly duels in 60 years) |
| Dead and faded characters | Kept for history (memories cleared). Snapshot grows to about 2.2 MB after 200 years on a medium world | Compaction is Plan 6 |

**Prototype soak (medium world, seed 7, 200 years, Release):** about 760 living notables (steady from year 75 on), 36/36 seats held, 47 artifacts (cap 50). Of about 2,500 deaths, 1,055 were illness, 1,014 old age, 165 monsters, 128 duels, 65 plague, 45 old wounds, 30 childbirth and 19 famine. There were 3,235 births, 1,281 marriages, 1,257 rivalries, 693 duels and 208 successions, at about **32 ms per world year, flat** (the land systems alone take about 15 ms). The code in this plan was replayed task by task in this order: each task's tests fail first and pass after the implementation, with the counts given.

## Roadmap (unchanged)

| Plan | Delivers |
|---|---|
| 4 — Realms & armies | realm hierarchy, government, succession laws, regency, legitimacy (crowns), diplomacy, casus belli, armies (knights and mages as commanders), battles, sieges, war score, eras; trade between regions |
| 5 — LLM layer | LLM `IDecider`/`IArbiter`, invention (names, lore, backstories), narration, budgets, cost ledger |
| 6 — Chronicle & hardening | importance + digest, prehistory (origins for ancient artifacts and houses), live mode, compaction of the dead, snapshot compression, backups, BenchmarkDotNet |

## File Structure

```
sim/
  FantasyWorld.Sim/
    Core/WorldDate.cs                    floor division for pre-history dates          (Task 1, modify)
    Engine/Invariants.cs                 partial; entity checks optional               (Task 1, modify)
    Engine/Invariants.People.cs, Invariants.Artifacts.cs                               (Task 1)
    Engine/Simulator.cs                  entity invariants only on active days         (Task 1, modify)
    Model/NameStyle.cs, Culture.cs, Traits.cs, Character.cs, Artifact.cs, Relations.cs (Task 1)
    Model/Region.cs, Settlement.cs, World.cs                                           (Task 1, modify)
    Persist/WorldSnapshot.cs             v3                                            (Task 1, modify)
    WorldGen/NameGen.cs                  culture styles, House/Proper/Given            (Task 2, modify)
    WorldGen/CultureGenerator.cs                                                       (Task 2)
    WorldGen/ArtifactForge.cs                                                          (Task 3)
    WorldGen/CharacterFactory.cs, PeopleGenerator.cs                                   (Task 4)
    WorldGen/WorldGenerator.cs                                                         (Tasks 2, 4, modify)
    Agency/Decisions.cs, Encounters.cs                                                 (Task 5)
    Systems/Fates.cs, LifeSystem.cs                                                    (Task 6)
    Systems/FamilySystem.cs                                                            (Task 7)
    Systems/RelationsSystem.cs                                                         (Task 8)
    Systems/DeedsSystem.cs                                                             (Task 9)
    Systems/ArtifactSystem.cs                                                          (Task 10)
    Systems/PromotionSystem.cs                                                         (Task 11)
    Systems/DefaultSystems.cs                                                          (Task 12, modify)
    Query/HistoryQuery.cs                + Life                                        (Task 12, modify)
  FantasyWorld.Cli/CliApp.cs (partial), CliApp.People.cs                               (Task 12)
  FantasyWorld.Sim.Tests/
    Support/TestPeople.cs; Model/PeopleModelTests.cs; WorldGen/{Culture,ArtifactForge,PeopleGenerator}Tests.cs;
    Agency/AgencyTests.cs; Systems/{Life,Family,Relations,Deeds,Artifact,Promotion}SystemTests.cs; Engine/PeopleHealthTests.cs
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: People, culture and artifact model

All of this plan's entities, persisted from the start (snapshot v3) and guarded by invariants, so later tasks only add behaviour. `Settlement.LordId` is the single source of truth for lordship. Two engine fixes ride along: `WorldDate` uses floor division so people born before Year 1 read `Y-12 M3 D4`, and entity invariants run only on days a tick or scheduled action ran (the event log is still checked daily), which keeps long soaks fast.

**Files:**
- Create: `sim/FantasyWorld.Sim/Engine/Invariants.Artifacts.cs`, `sim/FantasyWorld.Sim/Engine/Invariants.People.cs`, `sim/FantasyWorld.Sim/Model/Artifact.cs`, `sim/FantasyWorld.Sim/Model/Character.cs`, `sim/FantasyWorld.Sim/Model/Culture.cs`, `sim/FantasyWorld.Sim/Model/NameStyle.cs`, `sim/FantasyWorld.Sim/Model/Relations.cs`, `sim/FantasyWorld.Sim/Model/Traits.cs`
- Modify: `sim/FantasyWorld.Sim/Core/WorldDate.cs`, `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Engine/Simulator.cs`, `sim/FantasyWorld.Sim/Model/Region.cs`, `sim/FantasyWorld.Sim/Model/Settlement.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Model/PeopleModelTests.cs`, `sim/FantasyWorld.Sim.Tests/Support/TestPeople.cs`

**Interfaces:**
- Consumes: `IdGen`, `Region`, `Settlement`, `World`, `WorldSnapshot`, `Invariants`, `Simulator` (Plans 1–2).
- Produces: `NameStyle(string[] Onsets, Vowels, Codas) { Default; Derive(Rng) }`; `enum Lineage { Patrilineal, Matrilineal }`; `Culture` (kind `"culture"`: `Name, Style, Lineage, HomeRegionId, FaithId`); `Faith` (kind `"faith"`: `Name, Deity, Tenets, HolyRegionId`; `TenetPool`); `Traits` (nine opposed personality pairs + acquired `scarred, maimed, humiliated, renowned, bereaved`; `OppositeOf`, `Compatibility`); `Ambitions` (`renown, artifact, magic, wealth, vengeance, family`); `enum Sex`, `enum LifeStage { Child, Adult, Elder, Dead }`, `enum Role { None, Lord, Knight, Mage, Priest, Merchant, Elder, Adventurer, Rebel }`; `Attributes` (six 0–20 values, `Get/Set` by name); `record Memory(EventId, About, Feeling, Weight)`; `Character` (kind `"char"`; identity, family links, `Attributes`, `Traits`, `Ambition/AmbitionTarget`, `Role`, `Memories`, `Wounds`, `Renown`, `Faded`; `Age`, `Stage`, `IsAlive`, `IsActive`, `Has`); `House` (kind `"house"`); `ArtifactType`, `ArtifactPlace { Held, Treasury, Lost, Destroyed }`, `record ArtifactPower(Kind, Attribute, Amount)`, `record ProvenanceEntry(EventId, From, To, How)`, `Artifact` (kind `"artifact"`; `Bonus(attribute)`); `Relations { FriendAt = 40, RivalAt = −40; Opinion(a, b); AreKin; AreSiblings }`; `Region.CultureId/FaithId`; `Settlement.LordId`; `World.Cultures/Faiths/Houses/Characters/Artifacts`, `ActiveCharacters`, `PeopleOf(settlement)`; `WorldSnapshot` v3; `Invariants` becomes `partial` with `Invariants.People.cs` and `Invariants.Artifacts.cs` and `Check(…, bool entities = true)`. Test support: `TestPeople.World()`, `Add`, `Marry`, `GiveArtifact`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Model/PeopleModelTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class PeopleModelTests
{
    [Fact]
    public void Family_SatisfiesInvariants()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, Sex.Male, Role.Lord);
        var lady = TestPeople.Add(world, 1, 38, Sex.Female, houseId: lord.HouseId);
        TestPeople.Marry(world, lord, lady);
        TestPeople.Add(world, 1, 10, Sex.Female, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        TestPeople.GiveArtifact(world, lord, new ArtifactPower("attribute", "prowess", 3));
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Theory]
    [InlineData(10, LifeStage.Child)]
    [InlineData(16, LifeStage.Adult)]
    [InlineData(59, LifeStage.Adult)]
    [InlineData(60, LifeStage.Elder)]
    public void LifeStage_FollowsAge(int age, LifeStage expected)
    {
        var world = TestPeople.World();
        Assert.Equal(expected, TestPeople.Add(world, 1, age).Stage(world.Date));
    }

    [Fact]
    public void DatesBeforeHistory_ReadNaturally()
    {
        Assert.Equal("Y0 M12 D30", new WorldDate(-1).ToString());
        Assert.Equal("Y-1 M1 D1", new WorldDate(-720).ToString());
        Assert.True(new WorldDate(-360).IsFirstOfYear);
        Assert.Equal(new WorldDate(-725), WorldDate.FromYmd(-1, 12, 26).AddDays(-360));
    }

    [Fact]
    public void DeadCharacter_IsDeadStage()
    {
        var world = TestPeople.World();
        var c = TestPeople.Add(world, 1, 30);
        c.Died = world.Date;
        Assert.Equal(LifeStage.Dead, c.Stage(world.Date));
        Assert.False(c.IsActive);
    }

    [Fact]
    public void Opinion_CombinesTraitsKinshipAndMemories()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave, Traits.Honest });
        var b = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave, Traits.Deceitful });
        Assert.Equal(10 - 15, Relations.Opinion(a, b));

        a.Memories.Add(new Memory(1, b.Id, -80, 50));
        Assert.Equal(10 - 15 - 40, Relations.Opinion(a, b));

        TestPeople.Marry(world, a, b);
        Assert.Equal(10 - 15 - 40 + 30, Relations.Opinion(a, b));
    }

    [Fact]
    public void Siblings_AreKin()
    {
        var world = TestPeople.World();
        var mother = TestPeople.Add(world, 1, 40, Sex.Female);
        var x = TestPeople.Add(world, 1, 10, motherId: mother.Id, houseId: mother.HouseId);
        var y = TestPeople.Add(world, 1, 8, motherId: mother.Id, houseId: mother.HouseId);
        Assert.True(Relations.AreSiblings(x, y));
        Assert.True(Relations.AreKin(x, mother));
        Assert.False(Relations.AreSiblings(x, x));
    }

    [Fact]
    public void Attributes_AreClampedAndNamed()
    {
        var attributes = new Attributes();
        attributes.Set("lore", 25);
        Assert.Equal(20, attributes.Get("lore"));
        Assert.Throws<ArgumentException>(() => attributes.Get("charisma"));
    }

    [Fact]
    public void Artifact_BonusSumsAttributePowers()
    {
        var a = new Artifact { Powers = { new("attribute", "prowess", 3), new("attribute", "prowess", 2), new("ward", null, 0) } };
        Assert.Equal(5, a.Bonus("prowess"));
        Assert.Equal(0, a.Bonus("lore"));
    }

    [Fact]
    public void Invariants_CatchBrokenPeopleAndArtifacts()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var other = TestPeople.Add(world, 1, 40);
        lord.SpouseId = other.Id;
        var relic = TestPeople.GiveArtifact(world, other);
        other.Died = world.Date;
        world.Settlements[2].LordId = other.Id;
        relic.Fame = 120;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("not married to them"));
        Assert.Contains(errors, e => e.Contains("with a dead or faded holder"));
        Assert.Contains(errors, e => e.Contains("not a living lord"));
        Assert.Contains(errors, e => e.Contains("fame 120"));
    }

    [Fact]
    public void Snapshot_RoundTripsPeople()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, Sex.Female, Role.Lord, traits: new[] { Traits.Kind });
        lord.Memories.Add(new Memory(0, 0, 10, 10));
        lord.Died = null;
        TestPeople.GiveArtifact(world, lord, new ArtifactPower("attribute", "lore", 2));
        var queue = new EventQueue();
        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options);
        var copy = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!.ToWorld();

        Assert.Equal(json, JsonSerializer.Serialize(WorldSnapshot.Capture(copy, queue), SimJson.Options));
        Assert.Equal(Traits.Kind, copy.Characters[lord.Id].Traits.Single());
        Assert.Equal(2, copy.Artifacts[1].Bonus("lore"));
        Assert.Equal(lord.Id, copy.Settlements[1].LordId);
        Assert.Equal("Westfolk", copy.Cultures[1].Name);
    }
}
```

`sim/FantasyWorld.Sim.Tests/Support/TestPeople.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Builds notables, houses and artifacts on top of TestWorlds.</summary>
public static class TestPeople
{
    /// <summary>TwoRegions plus one culture and faith covering both regions.</summary>
    public static World World(ulong seed = 1)
    {
        var world = TestWorlds.TwoRegions(seed);
        var faith = new Faith { Id = world.Ids.Take(Faith.Kind), Name = "The Way of Ohm", Deity = "Ohm", HolyRegionId = 1 };
        var culture = new Culture { Id = world.Ids.Take(Culture.Kind), Name = "Westfolk", HomeRegionId = 1, FaithId = faith.Id };
        world.Faiths[faith.Id] = faith;
        world.Cultures[culture.Id] = culture;
        foreach (var region in world.Regions.Values) (region.CultureId, region.FaithId) = (culture.Id, faith.Id);
        return world;
    }

    public static Character Add(World world, long settlementId, int age, Sex sex = Sex.Male, Role role = Role.None,
        long houseId = 0, string[]? traits = null, long fatherId = 0, long motherId = 0)
    {
        if (houseId == 0)
        {
            var house = new House { Id = world.Ids.Take(House.Kind), Name = $"House{world.Houses.Count + 1}", SeatSettlementId = settlementId, CultureId = 1 };
            world.Houses[house.Id] = house;
            houseId = house.Id;
        }
        var c = new Character
        {
            Id = world.Ids.Take(Character.Kind),
            Name = $"Person{world.Characters.Count + 1}",
            HouseId = houseId,
            Sex = sex,
            BirthDay = world.Date.Day - (long)age * WorldDate.DaysPerYear,
            CultureId = 1,
            FaithId = 1,
            SettlementId = settlementId,
            FatherId = fatherId,
            MotherId = motherId,
            Attributes = new Attributes { Martial = 10, Diplomacy = 10, Stewardship = 10, Intrigue = 10, Lore = 10, Prowess = 10 },
            Traits = (traits ?? Array.Empty<string>()).ToList(),
            Role = role,
        };
        world.Characters[c.Id] = c;
        if (world.Houses[houseId].FounderId == 0) world.Houses[houseId].FounderId = c.Id;
        foreach (var parent in new[] { fatherId, motherId })
            if (parent != 0) world.Characters[parent].Children.Add(c.Id);
        if (role == Role.Lord) world.Settlements[settlementId].LordId = c.Id;
        return c;
    }

    public static void Marry(World world, Character a, Character b, long marriageEventId = 0)
    {
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
        a.MarriageEventId = b.MarriageEventId = marriageEventId;
    }

    public static Artifact GiveArtifact(World world, Character holder, params ArtifactPower[] powers)
    {
        var artifact = new Artifact
        {
            Id = world.Ids.Take(Artifact.Kind),
            Name = $"Relic{world.Artifacts.Count + 1}",
            Type = ArtifactType.Weapon,
            Material = "steel",
            Powers = powers.ToList(),
            Place = ArtifactPlace.Held,
            HolderId = holder.Id,
        };
        world.Artifacts[artifact.Id] = artifact;
        return artifact;
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'Sex' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Core/WorldDate.cs` entirely with:
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

    // Floor division, so days before Year 1 (people born before history began) read Y0, Y-1, …
    [JsonIgnore] public int Year => (int)FloorDiv(Day, DaysPerYear) + 1;
    [JsonIgnore] public int Month => (int)(Mod(Day, DaysPerYear) / DaysPerMonth) + 1;
    [JsonIgnore] public int DayOfMonth => (int)Mod(Day, DaysPerMonth) + 1;
    [JsonIgnore] public Season Season => (Season)((Month - 1) / 3);
    [JsonIgnore] public bool IsFirstOfMonth => Mod(Day, DaysPerMonth) == 0;
    [JsonIgnore] public bool IsFirstOfYear => Mod(Day, DaysPerYear) == 0;

    private static long Mod(long value, long divisor) => ((value % divisor) + divisor) % divisor;

    private static long FloorDiv(long value, long divisor) => (value - Mod(value, divisor)) / divisor;

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

`sim/FantasyWorld.Sim/Engine/Invariants.Artifacts.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckArtifacts(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, a) in world.Artifacts)
        {
            var name = $"Artifact #{id}";
            if (a.Id != id) errors.Add($"{name} is stored under the wrong key ({a.Id}).");
            var located = a.Place switch
            {
                ArtifactPlace.Held => world.Characters.TryGetValue(a.HolderId, out var holder) && holder.IsActive,
                ArtifactPlace.Treasury => world.Settlements.ContainsKey(a.SettlementId),
                ArtifactPlace.Lost => world.Regions.ContainsKey(a.RegionId),
                _ => true,
            };
            if (!located) errors.Add($"{name} is {a.Place} somewhere that does not exist (or with a dead or faded holder).");
            if (a.Place != ArtifactPlace.Held && a.HolderId != 0) errors.Add($"{name} is {a.Place} but names holder #{a.HolderId}.");
            if (!(a.Fame >= 0 && a.Fame <= 100)) errors.Add($"{name} has fame {a.Fame} outside 0–100.");
            if (a.OriginEventId != 0 && !log.TryGet(a.OriginEventId, out _)) errors.Add($"{name} has missing origin event #{a.OriginEventId}.");
            foreach (var entry in a.Provenance)
                if (!log.TryGet(entry.EventId, out _)) errors.Add($"{name} provenance cites missing event #{entry.EventId}.");
        }
    }
}
```

`sim/FantasyWorld.Sim/Engine/Invariants.People.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckPeople(World world, EventLog log, List<string> errors)
    {
        var seats = new HashSet<long>();
        foreach (var s in world.Settlements.Values)
        {
            if (s.LordId == 0) continue;
            if (!seats.Add(s.LordId)) errors.Add($"Character #{s.LordId} is lord of two settlements.");
            if (!world.Characters.TryGetValue(s.LordId, out var lord) || !lord.IsAlive || lord.Role != Role.Lord)
                errors.Add($"Settlement #{s.Id} names #{s.LordId} as lord, who is not a living lord.");
            else if (lord.SettlementId != s.Id) errors.Add($"Lord #{s.LordId} of settlement #{s.Id} lives elsewhere.");
            if (s.Abandoned) errors.Add($"Abandoned settlement #{s.Id} still has a lord.");
        }

        foreach (var (id, c) in world.Characters)
        {
            var name = $"Character #{id}";
            if (c.Id != id) errors.Add($"{name} is stored under the wrong key ({c.Id}).");
            if (c.BirthDay > world.Date.Day) errors.Add($"{name} is born in the future.");
            if (!c.IsAlive)
            {
                // The dead no longer change: only check that death released them.
                if (c.Role != Role.None) errors.Add($"{name} is dead but still holds the role {c.Role}.");
                if (c.SpouseId != 0) errors.Add($"{name} is dead but still married.");
                continue;
            }
            if (c.Faded && (c.Role != Role.None || c.SpouseId != 0)) errors.Add($"{name} faded but still holds a role or a marriage.");
            if (c.AmbitionTarget == id) errors.Add($"{name} seeks vengeance against themselves.");
            if (c.IsActive && world.Settlements.TryGetValue(c.SettlementId, out var home) && home.Abandoned)
                errors.Add($"{name} lives in abandoned settlement #{c.SettlementId}.");
            if (c.HouseId != 0 && !world.Houses.ContainsKey(c.HouseId)) errors.Add($"{name} has unknown house #{c.HouseId}.");
            if (!world.Settlements.ContainsKey(c.SettlementId)) errors.Add($"{name} lives in unknown settlement #{c.SettlementId}.");
            if (c.SpouseId != 0 && (!world.Characters.TryGetValue(c.SpouseId, out var spouse) || spouse.SpouseId != id))
                errors.Add($"{name} is married to #{c.SpouseId}, who is not married to them.");
            foreach (var parent in new[] { c.FatherId, c.MotherId })
                if (parent != 0 && (!world.Characters.TryGetValue(parent, out var p) || !p.Children.Contains(id)))
                    errors.Add($"{name} is not listed as a child of #{parent}.");
            foreach (var child in c.Children)
                if (!world.Characters.TryGetValue(child, out var k) || (k.FatherId != id && k.MotherId != id))
                    errors.Add($"{name} lists #{child} as a child, who does not name them as a parent.");
            foreach (var trait in c.Traits)
                if (Traits.OppositeOf(trait) is { } opposite && c.Traits.Contains(opposite))
                    errors.Add($"{name} is both {trait} and {opposite}.");
            if (c.Attributes.Martial is < 0 or > 20 || c.Attributes.Prowess is < 0 or > 20 || c.Attributes.Lore is < 0 or > 20)
                errors.Add($"{name} has attributes outside 0–20.");
            foreach (var memory in c.Memories)
                if (!log.TryGet(memory.EventId, out _)) errors.Add($"{name} remembers missing event #{memory.EventId}.");
            if (c.Role == Role.Lord && !seats.Contains(id)) errors.Add($"{name} is a lord without a seat.");
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
        return errors;
    }

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
            var errors = Invariants.Check(World, Log, _checkedCount, entities: changed);
            if (errors.Count > 0) throw new InvariantViolationException(errors);
            _checkedCount = Log.Count;
        }
    }

    private SimContext ContextFor(string system, string phase) =>
        new(World, Log, Queue, Rng.ForStream(World.Seed, system + "/" + phase, World.Date.Day));
}
```

`sim/FantasyWorld.Sim/Model/Artifact.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum ArtifactType { Weapon, Armor, Crown, Tome, Relic, Trinket }

/// <summary>Held by a character, kept in a settlement's treasury, lost in a region, or destroyed.</summary>
public enum ArtifactPlace { Held, Treasury, Lost, Destroyed }

/// <summary>
/// A mechanical effect. Kind "attribute" adds Amount to Attribute while held; other kinds are tags
/// later systems read: "ward" (monsters), "truthsight" (intrigue), "harvest" (region food), "legitimacy" (Plan 4).
/// </summary>
public sealed record ArtifactPower(string Kind, string? Attribute, int Amount);

/// <summary>One change of hands. From/To are character ids (0 = none); How: forged, found, inherited, gifted, stolen, looted, lost.</summary>
public sealed record ProvenanceEntry(long EventId, long From, long To, string How);

/// <summary>A named object with history (spec §4).</summary>
public sealed class Artifact
{
    public const string Kind = "artifact";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public ArtifactType Type { get; init; }
    public string Material { get; init; } = "";

    /// <summary>The creating event, or 0 if it predates recorded history (prehistory arrives in Plan 6).</summary>
    public long OriginEventId { get; init; }

    public List<ArtifactPower> Powers { get; init; } = new();

    /// <summary>A hidden downside, e.g. "paranoia"; revealed over time.</summary>
    public string? Curse { get; init; }
    public bool CurseRevealed { get; set; }

    /// <summary>0–100. Grows with deeds done while held.</summary>
    public double Fame { get; set; }

    public List<ProvenanceEntry> Provenance { get; init; } = new();

    public ArtifactPlace Place { get; set; }
    public long HolderId { get; set; }
    public long SettlementId { get; set; }
    public long RegionId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>Total bonus this artifact gives to an attribute while held.</summary>
    public int Bonus(string attribute) =>
        Powers.Where(p => p.Kind == "attribute" && p.Attribute == attribute).Sum(p => p.Amount);
}
```

`sim/FantasyWorld.Sim/Model/Character.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum Sex { Female, Male }

public enum LifeStage { Child, Adult, Elder, Dead }

/// <summary>A notable's place in society. Lords are the settlement's ruler (Settlement.LordId is the source of truth).</summary>
public enum Role { None, Lord, Knight, Mage, Priest, Merchant, Elder, Adventurer, Rebel }

/// <summary>The six attributes, each 0–20.</summary>
public sealed class Attributes
{
    public int Martial { get; set; }
    public int Diplomacy { get; set; }
    public int Stewardship { get; set; }
    public int Intrigue { get; set; }
    public int Lore { get; set; }
    public int Prowess { get; set; }

    public static readonly IReadOnlyList<string> Names =
        new[] { "martial", "diplomacy", "stewardship", "intrigue", "lore", "prowess" };

    public int Get(string name) => name switch
    {
        "martial" => Martial,
        "diplomacy" => Diplomacy,
        "stewardship" => Stewardship,
        "intrigue" => Intrigue,
        "lore" => Lore,
        "prowess" => Prowess,
        _ => throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name)),
    };

    public void Set(string name, int value)
    {
        value = Math.Clamp(value, 0, 20);
        switch (name)
        {
            case "martial": Martial = value; break;
            case "diplomacy": Diplomacy = value; break;
            case "stewardship": Stewardship = value; break;
            case "intrigue": Intrigue = value; break;
            case "lore": Lore = value; break;
            case "prowess": Prowess = value; break;
            default: throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name));
        }
    }
}

/// <summary>A remembered event. About is the character it concerns (0 for none); Feeling −100…100; Weight decays yearly.</summary>
public sealed record Memory(long EventId, long About, double Feeling, double Weight);

/// <summary>A named, individually simulated person (spec §3 "Notables").</summary>
public sealed class Character
{
    public const string Kind = "char";
    public const int AdultAge = 16;
    public const int ElderAge = 60;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long HouseId { get; set; }
    public Sex Sex { get; init; }
    public long BirthDay { get; init; }
    public WorldDate? Died { get; set; }
    public string? DeathCause { get; set; }

    public long CultureId { get; init; }
    public long FaithId { get; set; }

    /// <summary>The settlement the character lives in.</summary>
    public long SettlementId { get; set; }

    public long FatherId { get; init; }
    public long MotherId { get; init; }
    public long SpouseId { get; set; }

    /// <summary>The event that began the current marriage (cited by births).</summary>
    public long MarriageEventId { get; set; }

    public List<long> Children { get; init; } = new();

    public Attributes Attributes { get; init; } = new();
    public List<string> Traits { get; init; } = new();
    public string Ambition { get; set; } = Model.Ambitions.Family;

    /// <summary>Whom the ambition targets (a rival for vengeance), 0 for none.</summary>
    public long AmbitionTarget { get; set; }

    public Role Role { get; set; }
    public List<Memory> Memories { get; init; } = new();

    /// <summary>Lasting injuries, e.g. "sword arm".</summary>
    public List<string> Wounds { get; init; } = new();

    /// <summary>0–100. Grows with deeds; raises the importance of their events.</summary>
    public double Renown { get; set; }

    /// <summary>No longer individually followed: absorbed into the settlement's populace.</summary>
    public bool Faded { get; set; }

    [JsonIgnore] public bool IsAlive => Died is null;

    /// <summary>Alive and still a notable.</summary>
    [JsonIgnore] public bool IsActive => Died is null && !Faded;

    public int Age(WorldDate today) => (int)((today.Day - BirthDay) / WorldDate.DaysPerYear);

    public LifeStage Stage(WorldDate today) => Died is not null
        ? LifeStage.Dead
        : Age(today) switch { < AdultAge => LifeStage.Child, < ElderAge => LifeStage.Adult, _ => LifeStage.Elder };

    public bool Has(string trait) => Traits.Contains(trait);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

/// <summary>A family line (spec "house/dynasty").</summary>
public sealed class House
{
    public const string Kind = "house";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long FounderId { get; set; }
    public long SeatSettlementId { get; set; }
    public long CultureId { get; init; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

`sim/FantasyWorld.Sim/Model/Culture.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Which parent's house children join, and where a married couple lives.</summary>
public enum Lineage { Patrilineal, Matrilineal }

public sealed class Culture
{
    public const string Kind = "culture";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public NameStyle Style { get; init; } = NameStyle.Default;
    public Lineage Lineage { get; init; }
    public long HomeRegionId { get; init; }
    public long FaithId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

public sealed class Faith
{
    public const string Kind = "faith";

    /// <summary>Tenet vocabulary. Plans 4–5 give them teeth (holy wars, magic law, decision weights).</summary>
    public static readonly IReadOnlyList<string> TenetPool =
        new[] { "warlike", "pacifist", "arcane", "ascetic", "mercantile", "ancestral", "zealous", "tolerant" };

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Deity { get; init; } = "";
    public List<string> Tenets { get; init; } = new();
    public long HolyRegionId { get; init; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

`sim/FantasyWorld.Sim/Model/NameStyle.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>The sounds a culture's names are built from. Persisted with each culture.</summary>
public sealed record NameStyle(string[] Onsets, string[] Vowels, string[] Codas)
{
    public static readonly NameStyle Default = new(
        new[] { "", "b", "br", "c", "d", "dr", "f", "g", "gr", "h", "k", "l", "m", "n", "r", "s", "st", "t", "th", "v", "w" },
        new[] { "a", "e", "i", "o", "u", "ae", "ia", "ei", "ou" },
        new[] { "", "", "n", "r", "l", "th", "s", "nd", "rn", "ll", "m" });

    /// <summary>A distinct subset of the default sounds, so each culture's names have their own flavour.</summary>
    public static NameStyle Derive(Rng rng) => new(
        Subset(Default.Onsets, 9, rng),
        Subset(Default.Vowels, 5, rng),
        Subset(Default.Codas, 6, rng));

    private static string[] Subset(string[] source, int count, Rng rng)
    {
        var pool = source.Distinct().ToList();
        var chosen = new List<string> { "" };
        if (!pool.Contains("")) chosen.Clear();
        pool.Remove("");
        while (chosen.Count < count && pool.Count > 0)
        {
            var i = rng.NextInt(0, pool.Count);
            chosen.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return chosen.OrderBy(s => s, StringComparer.Ordinal).ToArray();
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

`sim/FantasyWorld.Sim/Model/Relations.cs`:
```csharp
namespace FantasyWorld.Sim.Model;

/// <summary>Opinions and kinship. Opinions are derived on demand, never stored (spec §3).</summary>
public static class Relations
{
    public const int FriendAt = 40;
    public const int RivalAt = -40;

    /// <summary>
    /// What a thinks of b, −100…100: memories about b (feeling × weight / 100), trait compatibility,
    /// and kinship (+30 spouse, +20 parent, child or sibling).
    /// </summary>
    public static double Opinion(Character a, Character b)
    {
        var score = a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += Traits.Compatibility(a.Traits, b.Traits);
        if (a.SpouseId == b.Id) score += 30;
        else if (AreKin(a, b)) score += 20;
        return Math.Clamp(score, -100, 100);
    }

    public static bool AreKin(Character a, Character b) =>
        a.FatherId == b.Id || a.MotherId == b.Id || b.FatherId == a.Id || b.MotherId == a.Id || AreSiblings(a, b);

    public static bool AreSiblings(Character a, Character b) =>
        a.Id != b.Id && ((a.FatherId != 0 && a.FatherId == b.FatherId) || (a.MotherId != 0 && a.MotherId == b.MotherId));
}
```

Replace `sim/FantasyWorld.Sim/Model/Settlement.cs` entirely with:
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

    /// <summary>The ruling notable (0 for none). Character.Role is Lord exactly for these.</summary>
    public long LordId { get; set; }

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

`sim/FantasyWorld.Sim/Model/Traits.cs`:
```csharp
namespace FantasyWorld.Sim.Model;

/// <summary>
/// Personality traits. Opposed pairs never coexist; acquired traits come from events.
/// Traits modify opinions (shared +, opposed −) and the weights of rule-based decisions.
/// </summary>
public static class Traits
{
    public const string Brave = "brave", Craven = "craven";
    public const string Kind = "kind", Cruel = "cruel";
    public const string Generous = "generous", Greedy = "greedy";
    public const string Honest = "honest", Deceitful = "deceitful";
    public const string Calm = "calm", Wrathful = "wrathful";
    public const string Devout = "devout", Cynical = "cynical";
    public const string Ambitious = "ambitious", Content = "content";
    public const string Diligent = "diligent", Lazy = "lazy";
    public const string Trusting = "trusting", Paranoid = "paranoid";

    // Acquired through events.
    public const string Scarred = "scarred";
    public const string Maimed = "maimed";
    public const string Humiliated = "humiliated";
    public const string Renowned = "renowned";
    public const string Bereaved = "bereaved";

    public static readonly IReadOnlyList<(string A, string B)> Opposites = new[]
    {
        (Brave, Craven), (Kind, Cruel), (Generous, Greedy), (Honest, Deceitful), (Calm, Wrathful),
        (Devout, Cynical), (Ambitious, Content), (Diligent, Lazy), (Trusting, Paranoid),
    };

    public static string? OppositeOf(string trait)
    {
        foreach (var (a, b) in Opposites)
        {
            if (a == trait) return b;
            if (b == trait) return a;
        }
        return null;
    }

    /// <summary>+10 per shared personality trait, −15 per opposed pair.</summary>
    public static int Compatibility(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        var score = 0;
        foreach (var trait in a)
        {
            if (OppositeOf(trait) is not { } opposite) continue;
            if (b.Contains(trait)) score += 10;
            if (b.Contains(opposite)) score -= 15;
        }
        return score;
    }
}

/// <summary>What a notable is currently striving for. Drives monthly deeds.</summary>
public static class Ambitions
{
    public const string Renown = "renown";
    public const string Artifact = "artifact";
    public const string Magic = "magic";
    public const string Wealth = "wealth";
    public const string Vengeance = "vengeance";
    public const string Family = "family";
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
    SortedDictionary<long, Artifact>? Artifacts = null)
{
    public const int CurrentVersion = 3;

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
        world.Artifacts);

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

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PeopleModelTests"`
Expected: `Passed!  - Failed: 0, Passed: 13`. Then `dotnet test sim` → `Passed: 238` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add model for cultures, faiths, characters, houses and artifacts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Cultures and faiths

Cultures spread from widely spaced homelands over the region graph (each region joins the nearest homeland by travel days, so territories are contiguous). Neighbouring cultures share faiths. Each culture has its own name sounds, so a Westfolk name sounds different from an Eastmoor one.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/CultureGenerator.cs`
- Modify: `sim/FantasyWorld.Sim/WorldGen/NameGen.cs`, `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/CultureGeneratorTests.cs`

**Interfaces:**
- Consumes: `NameStyle`, `Culture`, `Faith` (Task 1); `RegionGraph` (Plan 2).
- Produces: `NameGen(Rng, NameStyle? style = null)` with new `House()` (unique), `Proper()` (unique) and `Given()` (repeats allowed); `static class CultureGenerator { (List<Culture>, List<Faith>) Generate(regions, graph, Rng, IdGen) }` (cultures = clamp(regions / 14, 3, 9), faiths = clamp((cultures + 1) / 2, 2, 5); sets each region's `CultureId`/`FaithId`); `WorldGenerator` fills `World.Cultures`/`Faiths`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/CultureGeneratorTests.cs`:
```csharp
using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class CultureGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_HasSeveralCulturesAndFaiths()
    {
        var world = Medium();
        Assert.InRange(world.Cultures.Count, 6, 9);
        Assert.InRange(world.Faiths.Count, 2, 5);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryRegion_HasACultureAndItsFaith()
    {
        var world = Medium();
        Assert.All(world.Regions.Values, r =>
        {
            Assert.True(world.Cultures.ContainsKey(r.CultureId));
            Assert.Equal(world.Cultures[r.CultureId].FaithId, r.FaithId);
        });
        Assert.All(world.Cultures.Values, c => Assert.Equal(c.Id, world.Regions[c.HomeRegionId].CultureId));
    }

    [Fact]
    public void Cultures_FormConnectedTerritories()
    {
        var world = Medium();
        var graph = world.Geography!.Graph;
        foreach (var culture in world.Cultures.Values)
        {
            var mine = world.Regions.Values.Where(r => r.CultureId == culture.Id).Select(r => r.Id).ToHashSet();
            var seen = new HashSet<long> { culture.HomeRegionId };
            var queue = new Queue<long>(seen);
            while (queue.Count > 0)
                foreach (var n in graph.Neighbours(queue.Dequeue()))
                    if (mine.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(mine.Count, seen.Count);
        }
    }

    [Fact]
    public void Cultures_HaveDistinctNameStyles()
    {
        var world = Medium();
        var styles = world.Cultures.Values.Select(c => string.Join("|", c.Style.Onsets) + "/" + string.Join("|", c.Style.Vowels)).ToList();
        Assert.True(styles.Distinct().Count() >= styles.Count - 1);
    }

    [Fact]
    public void NameStyle_ShapesNames()
    {
        var style = new NameStyle(new[] { "k" }, new[] { "a" }, new[] { "" });
        var names = new NameGen(new Rng(1), style);
        Assert.All(Enumerable.Range(0, 20).Select(_ => names.Given()), n => Assert.Matches("^Ka(ka)*$", n));
    }

    [Fact]
    public void Culture_JsonRoundTrip()
    {
        var culture = Medium().Cultures[1];
        var copy = JsonSerializer.Deserialize<Culture>(JsonSerializer.Serialize(culture))!;
        Assert.Equal(culture.Style.Onsets, copy.Style.Onsets);
        Assert.Equal(culture.Lineage, copy.Lineage);
    }

    [Fact]
    public void Invariants_CatchUnknownCulture()
    {
        var world = Medium();
        world.Regions[1].CultureId = 99;
        Assert.Contains(Invariants.Check(world, new EventLog()), e => e.Contains("unknown culture #99"));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1729: 'NameGen' does not contain a constructor that takes 2 arguments`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/CultureGenerator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Spreads cultures over the region graph from widely spaced homelands (each region joins the
/// nearest homeland by travel days), then groups neighbouring cultures under shared faiths.
/// </summary>
public static class CultureGenerator
{
    public static (List<Culture> Cultures, List<Faith> Faiths) Generate(
        IReadOnlyList<Region> regions, RegionGraph graph, Rng rng, IdGen ids)
    {
        var regionIds = regions.Select(r => r.Id).ToList();
        var cultureCount = Math.Clamp(regions.Count / 14, 3, 9);
        var homes = SpreadOut(graph, regionIds, cultureCount, rng);
        var (_, nearestHome) = Distances(graph, homes);

        var cultures = new List<Culture>();
        foreach (var home in homes)
        {
            var style = NameStyle.Derive(rng);
            cultures.Add(new Culture
            {
                Id = ids.Take(Culture.Kind),
                Name = new NameGen(rng, style).Proper(),
                Style = style,
                Lineage = rng.Chance(0.25) ? Lineage.Matrilineal : Lineage.Patrilineal,
                HomeRegionId = home,
            });
        }

        var faithCount = Math.Clamp((cultures.Count + 1) / 2, 2, 5);
        var holy = SpreadOut(graph, homes, faithCount, rng);
        var (_, nearestHoly) = Distances(graph, holy);
        var names = new NameGen(rng);
        var faiths = new List<Faith>();
        foreach (var site in holy)
        {
            var deity = names.Proper();
            var tenets = Faith.TenetPool.OrderBy(_ => rng.NextULong()).Take(2).OrderBy(t => t, StringComparer.Ordinal).ToList();
            faiths.Add(new Faith
            {
                Id = ids.Take(Faith.Kind),
                Name = rng.Chance(0.5) ? $"The Way of {deity}" : $"The {names.Proper()} Covenant",
                Deity = deity,
                Tenets = tenets,
                HolyRegionId = site,
            });
        }

        foreach (var culture in cultures)
            culture.FaithId = faiths[holy.IndexOf(nearestHoly[culture.HomeRegionId])].Id;
        foreach (var region in regions)
        {
            var culture = cultures[homes.IndexOf(nearestHome[region.Id])];
            region.CultureId = culture.Id;
            region.FaithId = culture.FaithId;
        }
        return (cultures, faiths);
    }

    /// <summary>Farthest-point sampling: a random start, then repeatedly the candidate farthest from all chosen.</summary>
    private static List<long> SpreadOut(RegionGraph graph, IReadOnlyList<long> candidates, int count, Rng rng)
    {
        var chosen = new List<long> { candidates[rng.NextInt(0, candidates.Count)] };
        while (chosen.Count < Math.Min(count, candidates.Count))
        {
            var (distance, _) = Distances(graph, chosen);
            chosen.Add(candidates.Where(c => !chosen.Contains(c))
                .OrderByDescending(c => distance.GetValueOrDefault(c, double.MaxValue)).ThenBy(c => c).First());
        }
        return chosen;
    }

    /// <summary>Multi-source Dijkstra over travel days: distance to, and identity of, the nearest source.</summary>
    private static (Dictionary<long, double> Distance, Dictionary<long, long> Nearest) Distances(
        RegionGraph graph, IReadOnlyList<long> sources)
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
        return (distance, nearest);
    }
}
```

Replace `sim/FantasyWorld.Sim/WorldGen/NameGen.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Rule-based placeholder names. Place and house names are unique per generator; given names repeat.
/// The LLM invention layer (Plan 5) may rename things; these remain the offline fallback.
/// </summary>
public sealed class NameGen
{
    private static readonly string[] RegionSuffixes =
        { "mark", "moor", "vale", "fell", "wold", "reach", "shire", "dale", "march", "holt" };
    private static readonly string[] SettlementSuffixes =
        { "ford", "by", "ton", "wick", "stead", "burg", "haven", "bridge", "mere", "cross" };
    private static readonly string[] HouseSuffixes = { "ar", "en", "is", "ow", "eth", "ard", "ing" };

    private readonly Rng _rng;
    private readonly NameStyle _style;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public NameGen(Rng rng, NameStyle? style = null)
    {
        _rng = rng;
        _style = style ?? NameStyle.Default;
    }

    public string Region() => Unique(() => Root() + (_rng.Chance(0.7) ? Pick(RegionSuffixes) : ""));

    public string Settlement() => Unique(() => Root() + (_rng.Chance(0.6) ? Pick(SettlementSuffixes) : ""));

    public string House() => Unique(() => Root() + (_rng.Chance(0.5) ? Pick(HouseSuffixes) : ""));

    /// <summary>A culture, faith or deity name.</summary>
    public string Proper() => Unique(Root);

    /// <summary>A person's given name. Not unique: many people share a name.</summary>
    public string Given()
    {
        for (var attempt = 0; ; attempt++)
        {
            var name = Root();
            if (name.Length >= 3 || attempt >= 20) return name;
        }
    }

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
        for (var i = 0; i < syllables; i++) text += Pick(_style.Onsets) + Pick(_style.Vowels) + Pick(_style.Codas);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private string Pick(string[] options) => options[_rng.NextInt(0, options.Length)];
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

/// <summary>Seed → a complete world: terrain, rivers, regions, region graph, settlements, cultures and faiths. No history yet (Plan 6).</summary>
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
        return world;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CultureGeneratorTests"`
Expected: `Passed!  - Failed: 0, Passed: 7`. Then `dotnet test sim` → `Passed: 245` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): spread cultures and faiths with culture-styled names

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Artifact forge

One place that makes artifacts (spec §4): the type decides which attributes the powers boost and which tag power it may carry. A rare material from the place of making strengthens it, about one in five carries a hidden curse, and the name is built from patterns. The mechanics are the engine's; the LLM (Plan 5) can later rename and add lore.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/ArtifactForge.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/ArtifactForgeTests.cs`

**Interfaces:**
- Consumes: `Artifact`, `ArtifactPower`, `ResourceKinds` (Task 1, Plan 2); `NameGen` (Task 2).
- Produces: `static class ArtifactForge { Curses; Artifact Create(Rng, IdGen, NameGen, long originEventId, ArtifactType? type = null, IReadOnlyList<string>? rareMaterials = null, double fame = 10) }` (no location set).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/ArtifactForgeTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class ArtifactForgeTests
{
    private static List<Artifact> Forge(ulong seed, int count, IReadOnlyList<string>? rare = null, ArtifactType? type = null)
    {
        var rng = new Rng(seed);
        var ids = new IdGen();
        var names = new NameGen(new Rng(seed + 1));
        return Enumerable.Range(0, count).Select(_ => ArtifactForge.Create(rng, ids, names, 0, type, rare)).ToList();
    }

    [Fact]
    public void Forge_IsDeterministic() =>
        Assert.Equal(Forge(3, 20).Select(a => a.Name), Forge(3, 20).Select(a => a.Name));

    [Fact]
    public void Artifacts_HaveOneToThreeFittingPowers()
    {
        foreach (var a in Forge(4, 200))
        {
            Assert.InRange(a.Powers.Count, 1, 3);
            Assert.Contains(a.Powers, p => p.Kind == "attribute");
            Assert.All(a.Powers.Where(p => p.Kind == "attribute"), p => Assert.InRange(p.Amount, 1, 5));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
        }
    }

    [Fact]
    public void Tomes_TeachLore()
    {
        Assert.All(Forge(5, 30, type: ArtifactType.Tome), a => Assert.True(a.Bonus("lore") > 0));
    }

    [Fact]
    public void RareMaterials_AppearWhenAvailable()
    {
        var made = Forge(6, 100, rare: new[] { ResourceKinds.Mithril });
        Assert.InRange(made.Count(a => a.Material == ResourceKinds.Mithril), 40, 80);
    }

    [Fact]
    public void SomeArtifacts_AreCursed()
    {
        var cursed = Forge(7, 300).Count(a => a.Curse is not null);
        Assert.InRange(cursed, 35, 90);
    }

    [Fact]
    public void Ids_AreSequential() =>
        Assert.Equal(new long[] { 1, 2, 3 }, Forge(8, 3).Select(a => a.Id));
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'ArtifactForge' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/ArtifactForge.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Makes artifacts: a type, a material (rare ones when the region yields them), 1–3 powers fitting
/// the type, sometimes a hidden curse, and a name. The engine owns the mechanics; the LLM invention
/// layer (Plan 5) may later replace the name and write lore within these slots.
/// </summary>
public static class ArtifactForge
{
    public static readonly IReadOnlyList<string> Curses = new[] { "paranoia", "wasting", "bloodlust", "misfortune" };

    private static readonly string[] CommonMaterials = { "steel", "bronze", "oak", "gold", "silver", "obsidian", "bone", "glass" };

    private static readonly IReadOnlyDictionary<ArtifactType, string[]> AttributeFor = new Dictionary<ArtifactType, string[]>
    {
        [ArtifactType.Weapon] = new[] { "prowess", "martial" },
        [ArtifactType.Armor] = new[] { "prowess" },
        [ArtifactType.Crown] = new[] { "diplomacy", "stewardship" },
        [ArtifactType.Tome] = new[] { "lore" },
        [ArtifactType.Relic] = new[] { "diplomacy", "lore" },
        [ArtifactType.Trinket] = new[] { "intrigue" },
    };

    private static readonly IReadOnlyDictionary<ArtifactType, string> TagFor = new Dictionary<ArtifactType, string>
    {
        [ArtifactType.Weapon] = "ward",
        [ArtifactType.Armor] = "ward",
        [ArtifactType.Crown] = "legitimacy",
        [ArtifactType.Tome] = "truthsight",
        [ArtifactType.Relic] = "harvest",
        [ArtifactType.Trinket] = "truthsight",
    };

    private static readonly IReadOnlyDictionary<ArtifactType, string[]> Patterns = new Dictionary<ArtifactType, string[]>
    {
        [ArtifactType.Weapon] = new[] { "{0}, Blade of {1}", "The Spear of {1}", "{0}" },
        [ArtifactType.Armor] = new[] { "The Aegis of {1}", "{0}'s Mail", "The {2} Hauberk" },
        [ArtifactType.Crown] = new[] { "The Crown of {1}", "The {2} Circlet" },
        [ArtifactType.Tome] = new[] { "The Codex of {1}", "{0}'s Grimoire" },
        [ArtifactType.Relic] = new[] { "The {2} Chalice", "The Reliquary of {1}" },
        [ArtifactType.Trinket] = new[] { "The Ring of {1}", "{0}'s Locket" },
    };

    /// <summary>
    /// A new artifact with no location yet. rareMaterials are the rare resources the place of making
    /// yields (ResourceKinds.Rare); one may become the material and strengthen the powers.
    /// </summary>
    public static Artifact Create(Rng rng, IdGen ids, NameGen names, long originEventId,
        ArtifactType? type = null, IReadOnlyList<string>? rareMaterials = null, double fame = 10)
    {
        var kind = type ?? (ArtifactType)rng.NextInt(0, 6);
        var rare = rareMaterials is { Count: > 0 } && rng.Chance(0.6) ? rareMaterials[rng.NextInt(0, rareMaterials.Count)] : null;
        var material = rare ?? CommonMaterials[rng.NextInt(0, CommonMaterials.Length)];

        var powers = new List<ArtifactPower>();
        var attributes = AttributeFor[kind];
        var count = rng.NextInt(1, 4);
        for (var i = 0; i < count; i++)
        {
            if (i == 2 || (i == 1 && rng.Chance(0.4)))
            {
                if (powers.All(p => p.Kind != TagFor[kind])) powers.Add(new ArtifactPower(TagFor[kind], null, 0));
                continue;
            }
            var attribute = attributes[rng.NextInt(0, attributes.Length)];
            var amount = rng.NextInt(1, 4) + (rare is null ? 0 : 1);
            var existing = powers.FindIndex(p => p.Attribute == attribute);
            if (existing >= 0) powers[existing] = powers[existing] with { Amount = Math.Min(5, powers[existing].Amount + amount) };
            else powers.Add(new ArtifactPower("attribute", attribute, amount));
        }

        var pattern = Patterns[kind][rng.NextInt(0, Patterns[kind].Length)];
        var name = string.Format(pattern, names.Proper(), names.Proper(), Capitalise(material));
        return new Artifact
        {
            Id = ids.Take(Artifact.Kind),
            Name = name,
            Type = kind,
            Material = material,
            OriginEventId = originEventId,
            Powers = powers,
            Curse = rng.Chance(0.2) ? Curses[rng.NextInt(0, Curses.Count)] : null,
            Fame = Math.Clamp(fame, 0, 100),
        };
    }

    private static string Capitalise(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~ArtifactForgeTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 251` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): forge artifacts with powers, materials and curses

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Starting notables and ancient artifacts

Every town and city gets a lord's household. Cities also get knights, a priest, a merchant and a mage where ley power allows. Villages get an elder, and dangerous regions an adventurer. A medium world starts with about 300 notables. regions / 5 ancient artifacts go to the greatest lords or lie lost in the most dangerous regions; their origin predates recorded history (origin 0) until Plan 6's prehistory.

**Files:**
- Create: `sim/FantasyWorld.Sim/WorldGen/CharacterFactory.cs`, `sim/FantasyWorld.Sim/WorldGen/PeopleGenerator.cs`
- Modify: `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/WorldGen/PeopleGeneratorTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; `WorldGenerator` (Plan 2).
- Produces: `static class CharacterFactory { Character Create(World, Rng, settlementId, Sex, birthDay, houseId, Role role = None, Character? father = null, Character? mother = null); House NewHouse(World, Rng, cultureId, seatSettlementId); Character? LineageParent(World, father, mother); string ChooseAmbition(Character, Rng) }` (attributes inherited from parents ± 3 or rolled, plus role bonuses; 3–4 compatible traits, some inherited; culture and faith from the lineage parent or the region); `static class PeopleGenerator { void Populate(World, Rng) }`, called at the end of `WorldGenerator.Generate`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/WorldGen/PeopleGeneratorTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class PeopleGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_StartsWithHundredsOfNotables()
    {
        var world = Medium();
        Assert.InRange(world.Characters.Count, 300, 800);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryTownAndCity_HasALord()
    {
        var world = Medium();
        foreach (var s in world.Settlements.Values.Where(s => s.Tier >= SettlementTier.Town))
        {
            var lord = world.Characters[s.LordId];
            Assert.Equal(Role.Lord, lord.Role);
            Assert.Equal(s.Id, lord.SettlementId);
            Assert.Equal(s.Id, world.Houses[lord.HouseId].SeatSettlementId);
            Assert.InRange(lord.Age(world.Date), 25, 55);
        }
    }

    [Fact]
    public void Children_AreYoungerThanBothParents_AndShareTheHouse()
    {
        var world = Medium();
        foreach (var child in world.Characters.Values.Where(c => c.FatherId != 0))
        {
            var father = world.Characters[child.FatherId];
            var mother = world.Characters[child.MotherId];
            Assert.True(father.Age(world.Date) - child.Age(world.Date) >= 18);
            Assert.True(mother.Age(world.Date) - child.Age(world.Date) >= 18);
            Assert.Equal(CharacterFactory.LineageParent(world, father, mother)!.HouseId, child.HouseId);
        }
    }

    [Fact]
    public void Notables_HaveCompatibleTraits_AndCultureOfTheirRegion()
    {
        var world = Medium();
        foreach (var c in world.Characters.Values)
        {
            Assert.InRange(c.Traits.Count, 2, 5);
            Assert.True(world.Cultures.ContainsKey(c.CultureId));
            if (c.FatherId == 0)
                Assert.Equal(world.Regions[world.Settlements[c.SettlementId].RegionId].CultureId, c.CultureId);
        }
    }

    [Fact]
    public void AncientArtifacts_AreHeldByLordsOrLostInDangerousRegions()
    {
        var world = Medium();
        Assert.Equal(world.Regions.Count / 5, world.Artifacts.Count);
        foreach (var a in world.Artifacts.Values)
        {
            Assert.Equal(0, a.OriginEventId);
            if (a.Place == ArtifactPlace.Held) Assert.Equal(Role.Lord, world.Characters[a.HolderId].Role);
            else Assert.Equal(ArtifactPlace.Lost, a.Place);
            Assert.InRange(a.Fame, 30, 60);
        }
        var lostIn = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost).Select(a => world.Regions[a.RegionId].Danger);
        Assert.True(lostIn.Min() >= world.Regions.Values.Select(r => r.Danger).OrderByDescending(d => d).ElementAt(10));
    }

    [Fact]
    public void TinyWorld_HasPeopleToo()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.True(world.Characters.Count >= 10);
        Assert.Equal(3, world.Artifacts.Count);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'CharacterFactory' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/WorldGen/CharacterFactory.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Creates notables and houses: culture-styled names, attributes (inherited from parents or rolled),
/// 3–4 non-conflicting traits and an ambition. Used by world generation, births and promotions.
/// </summary>
public static class CharacterFactory
{
    public static Character Create(World world, Rng rng, long settlementId, Sex sex, long birthDay, long houseId,
        Role role = Role.None, Character? father = null, Character? mother = null)
    {
        var region = world.Regions[world.Settlements[settlementId].RegionId];
        var lineageParent = LineageParent(world, father, mother);
        var cultureId = lineageParent?.CultureId ?? region.CultureId;
        var culture = world.Cultures.GetValueOrDefault(cultureId);

        var character = new Character
        {
            Id = world.Ids.Take(Character.Kind),
            Name = new NameGen(rng, culture?.Style).Given(),
            HouseId = houseId,
            Sex = sex,
            BirthDay = birthDay,
            CultureId = cultureId,
            FaithId = lineageParent?.FaithId ?? region.FaithId,
            SettlementId = settlementId,
            FatherId = father?.Id ?? 0,
            MotherId = mother?.Id ?? 0,
            Attributes = RollAttributes(rng, role, father, mother),
            Traits = RollTraits(rng, father, mother),
            Role = role,
        };
        character.Ambition = ChooseAmbition(character, rng);
        world.Characters[character.Id] = character;
        father?.Children.Add(character.Id);
        mother?.Children.Add(character.Id);
        if (world.Houses.TryGetValue(houseId, out var house) && house.FounderId == 0) house.FounderId = character.Id;
        return character;
    }

    /// <summary>A new house with a name unique in the world, styled by its culture.</summary>
    public static House NewHouse(World world, Rng rng, long cultureId, long seatSettlementId)
    {
        var names = new NameGen(rng, world.Cultures.GetValueOrDefault(cultureId)?.Style);
        var taken = world.Houses.Values.Select(h => h.Name).ToHashSet(StringComparer.Ordinal);
        var name = names.House();
        for (var attempt = 0; taken.Contains(name) && attempt < 20; attempt++) name = names.House();
        if (taken.Contains(name)) name = $"{name} {world.Houses.Count + 1}";
        var house = new House { Id = world.Ids.Take(House.Kind), Name = name, SeatSettlementId = seatSettlementId, CultureId = cultureId };
        world.Houses[house.Id] = house;
        return house;
    }

    /// <summary>The parent whose house and culture children follow: by the culture's lineage, else whoever exists.</summary>
    public static Character? LineageParent(World world, Character? father, Character? mother)
    {
        if (father is null || mother is null) return father ?? mother;
        var lineage = world.Cultures.GetValueOrDefault(father.CultureId)?.Lineage ?? Lineage.Patrilineal;
        return lineage == Lineage.Matrilineal ? mother : father;
    }

    public static string ChooseAmbition(Character c, Rng rng) => c.Role switch
    {
        Role.Mage => Ambitions.Magic,
        Role.Knight or Role.Adventurer or Role.Rebel => Ambitions.Renown,
        Role.Merchant => Ambitions.Wealth,
        _ when c.Has(Traits.Greedy) || c.Has(Traits.Ambitious) => rng.Chance(0.5) ? Ambitions.Artifact : Ambitions.Renown,
        _ when c.Has(Traits.Brave) => Ambitions.Renown,
        _ when c.Attributes.Lore >= 14 => Ambitions.Magic,
        _ => Ambitions.Family,
    };

    private static Attributes RollAttributes(Rng rng, Role role, Character? father, Character? mother)
    {
        var attributes = new Attributes();
        foreach (var name in Attributes.Names)
        {
            var value = father is not null && mother is not null
                ? (father.Attributes.Get(name) + mother.Attributes.Get(name)) / 2 + rng.NextInt(-3, 4)
                : rng.NextInt(1, 11) + rng.NextInt(0, 10);
            attributes.Set(name, value + RoleBonus(role, name));
        }
        return attributes;
    }

    private static int RoleBonus(Role role, string attribute) => (role, attribute) switch
    {
        (Role.Lord, "stewardship" or "diplomacy") => 3,
        (Role.Knight, "martial" or "prowess") => 4,
        (Role.Adventurer or Role.Rebel, "prowess") => 3,
        (Role.Mage, "lore") => 5,
        (Role.Priest, "lore") => 3,
        (Role.Priest, "diplomacy") => 2,
        (Role.Merchant, "stewardship") => 4,
        (Role.Elder, "stewardship") => 2,
        _ => 0,
    };

    private static List<string> RollTraits(Rng rng, Character? father, Character? mother)
    {
        var traits = new List<string>();
        void TryAdd(string trait)
        {
            if (!traits.Contains(trait) && (Traits.OppositeOf(trait) is not { } o || !traits.Contains(o))) traits.Add(trait);
        }
        foreach (var parent in new[] { father, mother })
            if (parent is not null && rng.Chance(0.5))
            {
                var inheritable = parent.Traits.Where(t => Traits.OppositeOf(t) is not null).ToList();
                if (inheritable.Count > 0) TryAdd(inheritable[rng.NextInt(0, inheritable.Count)]);
            }
        var wanted = rng.NextInt(3, 5);
        for (var attempt = 0; traits.Count < wanted && attempt < 20; attempt++)
        {
            var (a, b) = Traits.Opposites[rng.NextInt(0, Traits.Opposites.Count)];
            TryAdd(rng.Chance(0.5) ? a : b);
        }
        traits.Sort(StringComparer.Ordinal);
        return traits;
    }
}
```

`sim/FantasyWorld.Sim/WorldGen/PeopleGenerator.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The starting notables: a lord's household in every town and city (with knights, priests, merchants
/// and mages in cities), an elder in every village, adventurers in dangerous regions. Ancient
/// artifacts are given to the greatest lords or lie lost in the most dangerous regions.
/// </summary>
public static class PeopleGenerator
{
    public static void Populate(World world, Rng rng)
    {
        var today = world.Date.Day;
        long BornYearsAgo(int min, int max) => today - (long)rng.NextInt(min, max + 1) * WorldDate.DaysPerYear - rng.NextInt(0, 360);

        foreach (var settlement in world.Settlements.Values.Where(s => !s.Abandoned))
        {
            var region = world.Regions[settlement.RegionId];
            switch (settlement.Tier)
            {
                case SettlementTier.City:
                    Household(world, rng, settlement, BornYearsAgo, maxChildren: 4);
                    foreach (var role in new[] { Role.Knight, Role.Knight, Role.Priest, Role.Merchant })
                        Single(world, rng, settlement, role, BornYearsAgo(20, 50));
                    if (region.LeyPower >= 40) Single(world, rng, settlement, Role.Mage, BornYearsAgo(25, 70));
                    break;
                case SettlementTier.Town:
                    Household(world, rng, settlement, BornYearsAgo, maxChildren: 3);
                    Single(world, rng, settlement, rng.Chance(0.5) ? Role.Knight : rng.Chance(0.5) ? Role.Priest : Role.Merchant,
                        BornYearsAgo(20, 55));
                    break;
                case SettlementTier.Village:
                    var elder = Single(world, rng, settlement, Role.Elder, BornYearsAgo(45, 65));
                    if (rng.Chance(0.5)) Spouse(world, rng, elder, BornYearsAgo);
                    break;
            }
        }

        foreach (var region in world.Regions.Values.Where(r => r.Danger >= 55))
        {
            var home = world.LivingSettlements(region).OrderByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
            if (home is not null) Single(world, rng, home, Role.Adventurer, BornYearsAgo(18, 35));
        }

        PlaceAncientArtifacts(world, rng);
    }

    private static void Household(World world, Rng rng, Settlement settlement, Func<int, int, long> bornYearsAgo, int maxChildren)
    {
        var region = world.Regions[settlement.RegionId];
        var house = CharacterFactory.NewHouse(world, rng, region.CultureId, settlement.Id);
        var lord = CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male,
            bornYearsAgo(25, 55), house.Id, Role.Lord);
        settlement.LordId = lord.Id;
        var spouse = Spouse(world, rng, lord, bornYearsAgo);
        var youngest = Math.Min(lord.Age(world.Date), spouse.Age(world.Date)) - 18;
        var (father, mother) = lord.Sex == Sex.Male ? (lord, spouse) : (spouse, lord);
        var children = youngest > 0 ? rng.NextInt(0, maxChildren + 1) : 0;
        for (var i = 0; i < children; i++)
            CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male,
                bornYearsAgo(0, Math.Max(0, youngest - 1)), house.Id, father: father, mother: mother);
    }

    private static Character Single(World world, Rng rng, Settlement settlement, Role role, long birthDay)
    {
        var region = world.Regions[settlement.RegionId];
        var house = CharacterFactory.NewHouse(world, rng, region.CultureId, settlement.Id);
        return CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male, birthDay, house.Id, role);
    }

    private static Character Spouse(World world, Rng rng, Character partner, Func<int, int, long> bornYearsAgo)
    {
        var age = partner.Age(world.Date);
        var spouse = CharacterFactory.Create(world, rng, partner.SettlementId, partner.Sex == Sex.Male ? Sex.Female : Sex.Male,
            bornYearsAgo(Math.Max(18, age - 6), Math.Max(18, age + 6)), partner.HouseId);
        partner.SpouseId = spouse.Id;
        spouse.SpouseId = partner.Id;
        return spouse;
    }

    private static void PlaceAncientArtifacts(World world, Rng rng)
    {
        var count = Math.Max(3, world.Regions.Count / 5);
        var names = new NameGen(rng);
        var lords = world.Settlements.Values
            .Where(s => s.LordId != 0)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id)
            .Select(s => world.Characters[s.LordId])
            .ToList();
        var lairs = world.Regions.Values.OrderByDescending(r => r.Danger).ThenBy(r => r.Id).ToList();
        for (var i = 0; i < count; i++)
        {
            var heldByLord = i % 2 == 0 && i / 2 < lords.Count;
            var region = heldByLord ? world.Regions[world.Settlements[lords[i / 2].SettlementId].RegionId] : lairs[(i / 2) % lairs.Count];
            var rare = region.Resources.Keys.Where(ResourceKinds.IsRare).ToList();
            var artifact = ArtifactForge.Create(rng, world.Ids, names, 0, rareMaterials: rare, fame: rng.NextInt(30, 61));
            if (heldByLord)
            {
                artifact.Place = ArtifactPlace.Held;
                artifact.HolderId = lords[i / 2].Id;
            }
            else
            {
                artifact.Place = ArtifactPlace.Lost;
                artifact.RegionId = region.Id;
            }
            world.Artifacts[artifact.Id] = artifact;
        }
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

/// <summary>Seed → a complete world: terrain, rivers, regions, region graph, settlements, cultures, faiths, notables and ancient artifacts. No history yet (Plan 6).</summary>
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
        return world;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PeopleGeneratorTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 257` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(worldgen): populate worlds with households, notables and ancient artifacts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Decisions and arbitration

The seam where Plan 5's LLM plugs in (spec §8.2–8.3). A **decision point** is a brief with 2–5 legal, weighted options. An **encounter** (duel, hunt, delve) carries both sides' strengths and the full set of legal outcomes, with probabilities summing to 1. The rules decider and arbiter roll on those weights; an LLM implementation will choose within the same sets, kept within the plausibility floor.

**Files:**
- Create: `sim/FantasyWorld.Sim/Agency/Decisions.cs`, `sim/FantasyWorld.Sim/Agency/Encounters.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Agency/AgencyTests.cs`

**Interfaces:**
- Consumes: `Character`, `Artifact`, `Traits` (Task 1).
- Produces (namespace `FantasyWorld.Sim.Agency`): `record DecisionOption(Key, Weight, Summary)`, `record DecisionBrief(CharacterId, Situation, Options)`, `interface IDecider { DecisionOption Decide(DecisionBrief, Rng) }`, `RulesDecider`; `enum Fate { Unharmed, Wounded, Maimed, Dead }`, `record EncounterOutcome(Key, Probability, AttackerWins, AttackerFate, DefenderFate)`, `record Encounter(Kind, AttackerId, DefenderId, AttackerStrength, DefenderStrength, ToTheDeath, Outcomes) { PlausibilityFloor = 0.05; AttackerOdds; IsPlausible }`, `interface IArbiter { EncounterOutcome Resolve(Encounter, Rng) }`, `RulesArbiter`; `static class Encounters { Strength(World, Character); DuelBetween(World, a, b, toTheDeath); HuntIn(World, c, Region); DelveIn(World, c, Region) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Agency/AgencyTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Agency;

public class AgencyTests
{
    [Fact]
    public void RulesDecider_FollowsWeights()
    {
        var brief = new DecisionBrief(1, "test", new[]
        {
            new DecisionOption("often", 3, ""),
            new DecisionOption("rarely", 1, ""),
            new DecisionOption("never", 0, ""),
        });
        var rng = new Rng(5);
        var picks = Enumerable.Range(0, 4000).Select(_ => new RulesDecider().Decide(brief, rng).Key).ToList();
        Assert.DoesNotContain("never", picks);
        Assert.InRange(picks.Count(k => k == "often") / 4000.0, 0.72, 0.78);
    }

    [Fact]
    public void RulesDecider_RejectsBriefsWithoutChoices() =>
        Assert.Throws<ArgumentException>(() =>
            new RulesDecider().Decide(new DecisionBrief(1, "stuck", new[] { new DecisionOption("x", 0, "") }), new Rng(1)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuelOutcomes_SumToOne(bool toTheDeath)
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = Encounters.DuelBetween(world, a, b, toTheDeath);
        Assert.Equal(1.0, duel.Outcomes.Sum(o => o.Probability), 9);
        Assert.Equal(toTheDeath, duel.Outcomes.Any(o => o.AttackerFate == Fate.Dead || o.DefenderFate == Fate.Dead));
    }

    [Fact]
    public void Strength_CountsTraitsWoundsAndArtifacts()
    {
        var world = TestPeople.World();
        var plain = TestPeople.Add(world, 1, 30);
        var brave = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave });
        var wounded = TestPeople.Add(world, 1, 30);
        wounded.Wounds.Add("leg");
        var armed = TestPeople.Add(world, 1, 30);
        TestPeople.GiveArtifact(world, armed, new ArtifactPower("attribute", "prowess", 4));

        Assert.Equal(15, Encounters.Strength(world, plain));
        Assert.Equal(17, Encounters.Strength(world, brave));
        Assert.Equal(13, Encounters.Strength(world, wounded));
        Assert.Equal(19, Encounters.Strength(world, armed));
    }

    [Fact]
    public void StrongerDuellist_WinsMoreOften()
    {
        var world = TestPeople.World();
        var strong = TestPeople.Add(world, 1, 30);
        strong.Attributes.Prowess = 20;
        var weak = TestPeople.Add(world, 1, 30);
        weak.Attributes.Prowess = 4;
        var duel = Encounters.DuelBetween(world, strong, weak, toTheDeath: false);
        var rng = new Rng(9);
        var wins = Enumerable.Range(0, 2000).Count(_ => new RulesArbiter().Resolve(duel, rng).AttackerWins);
        Assert.InRange(wins / 2000.0, duel.AttackerOdds - 0.04, duel.AttackerOdds + 0.04);
        Assert.True(duel.AttackerOdds > 0.6);
    }

    [Fact]
    public void Hunts_AreHarderInMoreDangerousRegions()
    {
        var world = TestPeople.World();
        var hunter = TestPeople.Add(world, 1, 30);
        world.Regions[1].Danger = 20;
        world.Regions[2].Danger = 90;
        Assert.True(Encounters.HuntIn(world, hunter, world.Regions[1]).AttackerOdds
                    > Encounters.HuntIn(world, hunter, world.Regions[2]).AttackerOdds);
        Assert.Equal(1.0, Encounters.DelveIn(world, hunter, world.Regions[2]).Outcomes.Sum(o => o.Probability), 9);
    }

    [Fact]
    public void PlausibilityFloor_FlagsUnlikelyOutcomes()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = Encounters.DuelBetween(world, a, b, toTheDeath: true);
        Assert.False(duel.IsPlausible(duel.Outcomes.Single(o => o.Key == "both_die")));
        Assert.True(duel.IsPlausible(duel.Outcomes.Single(o => o.Key == "attacker_kills")));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Agency' does not exist in the namespace 'FantasyWorld.Sim' (are you missing an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Agency/Decisions.cs`:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Agency;

/// <summary>One legal choice. Weight is the rules engine's prior (from traits, ambition, opinion).</summary>
public sealed record DecisionOption(string Key, double Weight, string Summary);

/// <summary>
/// A decision point (spec §8.2): a notable chooses among 2–5 legal options. The rules decider picks by
/// weight; Plan 5 adds an LLM decider behind the same interface, falling back to rules.
/// </summary>
public sealed record DecisionBrief(long CharacterId, string Situation, IReadOnlyList<DecisionOption> Options);

public interface IDecider
{
    DecisionOption Decide(DecisionBrief brief, Rng rng);
}

/// <summary>Seeded weighted choice over the options' weights.</summary>
public sealed class RulesDecider : IDecider
{
    public DecisionOption Decide(DecisionBrief brief, Rng rng)
    {
        var options = brief.Options.Where(o => o.Weight > 0).ToList();
        if (options.Count == 0) throw new ArgumentException($"Decision '{brief.Situation}' has no option with positive weight.", nameof(brief));
        var roll = rng.NextDouble() * options.Sum(o => o.Weight);
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll < 0) return option;
        }
        return options[^1];
    }
}
```

`sim/FantasyWorld.Sim/Agency/Encounters.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Agency;

public enum Fate { Unharmed, Wounded, Maimed, Dead }

/// <summary>A legal result of an encounter, with its base probability from the odds.</summary>
public sealed record EncounterOutcome(string Key, double Probability, bool AttackerWins, Fate AttackerFate, Fate DefenderFate);

/// <summary>
/// A dramatic personal conflict (spec §8.3). The engine sets the stage (strengths) and enumerates the
/// legal outcomes with probabilities summing to 1; an arbiter picks one. DefenderId 0 means the wilds
/// (monsters, a labyrinth) rather than a person.
/// </summary>
public sealed record Encounter(
    string Kind,
    long AttackerId,
    long DefenderId,
    double AttackerStrength,
    double DefenderStrength,
    bool ToTheDeath,
    IReadOnlyList<EncounterOutcome> Outcomes)
{
    /// <summary>Outcomes below this base probability are implausible; the LLM arbiter (Plan 5) may not choose them.</summary>
    public const double PlausibilityFloor = 0.05;

    public double AttackerOdds => AttackerStrength / (AttackerStrength + DefenderStrength);

    public bool IsPlausible(EncounterOutcome outcome) => outcome.Probability >= PlausibilityFloor;
}

public interface IArbiter
{
    EncounterOutcome Resolve(Encounter encounter, Rng rng);
}

/// <summary>Seeded roll on the base probabilities: the offline fallback and the test baseline.</summary>
public sealed class RulesArbiter : IArbiter
{
    public EncounterOutcome Resolve(Encounter encounter, Rng rng)
    {
        var roll = rng.NextDouble();
        foreach (var outcome in encounter.Outcomes)
        {
            roll -= outcome.Probability;
            if (roll < 0) return outcome;
        }
        return encounter.Outcomes[^1];
    }
}

/// <summary>Builds encounters: strengths from attributes, traits, wounds, renown and artifacts; legal outcome sets.</summary>
public static class Encounters
{
    public const string Duel = "duel";
    public const string Hunt = "hunt";
    public const string Delve = "delve";

    /// <summary>Fighting strength: prowess + martial/2 + artifact bonuses + traits − wounds + renown/20 (min 1).</summary>
    public static double Strength(World world, Character c)
    {
        var artifacts = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList();
        var strength = c.Attributes.Prowess + artifacts.Sum(a => a.Bonus("prowess"))
                       + (c.Attributes.Martial + artifacts.Sum(a => a.Bonus("martial"))) / 2.0
                       + c.Renown / 20;
        if (c.Has(Traits.Brave)) strength += 2;
        if (c.Has(Traits.Craven)) strength -= 3;
        strength -= 2 * Math.Min(3, c.Wounds.Count);
        var age = c.Age(world.Date);
        if (age < 18) strength -= 4;
        if (age >= 55) strength -= (age - 50) / 5;
        return Math.Max(1, strength);
    }

    public static Encounter DuelBetween(World world, Character attacker, Character defender, bool toTheDeath)
    {
        var a = Strength(world, attacker);
        var d = Strength(world, defender);
        var p = a / (a + d);
        var outcomes = toTheDeath
            ? new List<EncounterOutcome>
            {
                new("attacker_kills", p * 0.97 * 0.6, true, Fate.Unharmed, Fate.Dead),
                new("attacker_maims", p * 0.97 * 0.2, true, Fate.Unharmed, Fate.Maimed),
                new("attacker_wounds", p * 0.97 * 0.2, true, Fate.Wounded, Fate.Wounded),
                new("defender_kills", (1 - p) * 0.97 * 0.6, false, Fate.Dead, Fate.Unharmed),
                new("defender_maims", (1 - p) * 0.97 * 0.2, false, Fate.Maimed, Fate.Unharmed),
                new("defender_wounds", (1 - p) * 0.97 * 0.2, false, Fate.Wounded, Fate.Wounded),
                new("both_die", 0.03, false, Fate.Dead, Fate.Dead),
            }
            : new List<EncounterOutcome>
            {
                new("attacker_first_blood", p * 0.8, true, Fate.Unharmed, Fate.Wounded),
                new("attacker_yields_win", p * 0.2, true, Fate.Unharmed, Fate.Unharmed),
                new("defender_first_blood", (1 - p) * 0.8, false, Fate.Wounded, Fate.Unharmed),
                new("defender_yields_win", (1 - p) * 0.2, false, Fate.Unharmed, Fate.Unharmed),
            };
        return new Encounter(Duel, attacker.Id, defender.Id, a, d, toTheDeath, outcomes);
    }

    /// <summary>Hunting the monsters of a region: the wilds' strength is danger / 4.</summary>
    public static Encounter HuntIn(World world, Character hunter, Region region) =>
        Wilds(Hunt, world, hunter, Math.Max(1, region.Danger / 4), deathShare: 0.15);

    /// <summary>Delving for a lost artifact: the wilds' strength is danger / 3; failure is deadlier.</summary>
    public static Encounter DelveIn(World world, Character delver, Region region) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3), deathShare: 0.3);

    private static Encounter Wilds(string kind, World world, Character attacker, double wilds, double deathShare)
    {
        var a = Strength(world, attacker);
        var p = a / (a + wilds);
        var outcomes = new List<EncounterOutcome>
        {
            new("success", p * 0.7, true, Fate.Unharmed, Fate.Unharmed),
            new("success_wounded", p * 0.3, true, Fate.Wounded, Fate.Unharmed),
            new("failure_wounded", (1 - p) * (1 - deathShare) * 0.7, false, Fate.Wounded, Fate.Unharmed),
            new("failure_maimed", (1 - p) * (1 - deathShare) * 0.3, false, Fate.Maimed, Fate.Unharmed),
            new("failure_death", (1 - p) * deathShare, false, Fate.Dead, Fate.Unharmed),
        };
        return new Encounter(kind, attacker.Id, 0, a, wilds, true, outcomes);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~AgencyTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 265` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add decision and arbitration interfaces with rules implementations

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Life, death and succession

Each month every notable may die of old age, illness, plague, famine or old wounds; a plague or famine death cites the condition's event. Death is handled in one shared place (`Fates.Die`): grief for kin, vengeance against a killer, the end of a marriage, the seat passing to the eldest active child (else sibling; never the killer, never someone who is or is married to a lord; else left vacant and marked), and artifacts inherited, or lost where the holder fell in the wilds. Children come of age at 16; when the world already has enough notables, non-heirs may fade into the populace, which keeps the cast at about four per settlement (lords, the married and artifact holders never fade; the faded die off-stage without events).

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/Fates.cs`, `sim/FantasyWorld.Sim/Systems/LifeSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/LifeSystemTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 4–5; `Conditions` (Plan 2).
- Produces: `static class Fates { VacantSeat = "vacantSeat"; Importance(c, baseline); Remember(...); WorldEvent Die(ctx, c, cause, causes, Character? killer = null, long lostInRegion = 0); Character? Heir(World, c, today, killer = null); HeirCandidates(World, c, killer); CanTakeSeat(World, k); Siblings(World, c); IsHeirApparent(World, c, today); MoveToHolder; MoveToTreasury; Lose; string? Injure(c, Fate, Rng) }`; `sealed class LifeSystem : ISimSystem` (name `"life"`; `NotablesPerSettlement = 4`; `Risks`, `BaseMortality(age)`, `TurnsAgeThisMonth`). Events: `CharacterDied` (payload `cause`, `age`; actors `deceased`, `killer`), `SuccessionOccurred`, `ArtifactInherited`, `ComingOfAge`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/LifeSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class LifeSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new LifeSystem() }, checkInvariants: true);

    private static SimContext Context(World world, EventLog log) => new(world, log, new EventQueue(), new Rng(1));

    [Fact]
    public void Mortality_RisesWithAge()
    {
        Assert.True(LifeSystem.BaseMortality(30) < LifeSystem.BaseMortality(62));
        Assert.True(LifeSystem.BaseMortality(62) < LifeSystem.BaseMortality(90));
    }

    [Fact]
    public void TheVeryOld_Die_OfOldAge()
    {
        var world = TestPeople.World();
        var elders = Enumerable.Range(0, 30).Select(_ => TestPeople.Add(world, 1, 88)).ToList();
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.True(elders.Count(e => !e.IsAlive) >= 20);
        Assert.All(sim.Log.All.Where(e => e.Type == "CharacterDied"), e => Assert.Equal("old age", e.Payload["cause"]));
    }

    [Fact]
    public void PlagueDeaths_CiteThePlague()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var plague = log.Append(world.Date, new EventDraft("PlagueOutbreak"));
        world.Regions[1].Conditions[Conditions.Plague] = plague.Id;
        for (var i = 0; i < 60; i++) TestPeople.Add(world, 1, 30);
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        var died = sim.Log.All.Where(e => e.Type == "CharacterDied" && e.Payload["cause"] == "plague").ToList();
        Assert.NotEmpty(died);
        Assert.All(died, d => Assert.Equal(plague.Id, Assert.Single(d.Causes).EventId));
    }

    [Fact]
    public void LordsDeath_PassesSeatAndArtifactsToEldestChild()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, Sex.Male, Role.Lord);
        var lady = TestPeople.Add(world, 1, 58, Sex.Female, houseId: lord.HouseId);
        TestPeople.Marry(world, lord, lady);
        var younger = TestPeople.Add(world, 2, 20, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        var elder = TestPeople.Add(world, 2, 30, Sex.Female, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        var sword = TestPeople.GiveArtifact(world, lord);

        var death = Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());

        Assert.Equal(elder.Id, world.Settlements[1].LordId);
        Assert.Equal(Role.Lord, elder.Role);
        Assert.Equal(1, elder.SettlementId);
        Assert.Equal(Role.None, lord.Role);
        Assert.Equal(0, lady.SpouseId);
        Assert.Equal(elder.Id, sword.HolderId);
        Assert.Equal("inherited", sword.Provenance.Last().How);
        var succession = Assert.Single(log.All, e => e.Type == "SuccessionOccurred");
        Assert.Equal(death.Id, succession.Causes[0].EventId);
        Assert.Contains(younger.Memories, m => m.EventId == death.Id);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void LordWithoutKin_LeavesTheSeatEmpty_AndTheTreasuryFull()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 50, role: Role.Lord);
        var crown = TestPeople.GiveArtifact(world, lord);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(0, world.Settlements[1].LordId);
        Assert.Equal(log.All[0].Id, world.Settlements[1].Conditions[Fates.VacantSeat]);
        Assert.Equal(ArtifactPlace.Treasury, crown.Place);
        Assert.Equal(1, crown.SettlementId);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void ChildlessHolder_LeavesArtifactsToTheWidow()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var knight = TestPeople.Add(world, 1, 50);
        var widow = TestPeople.Add(world, 1, 45, Sex.Female);
        TestPeople.Marry(world, knight, widow);
        var blade = TestPeople.GiveArtifact(world, knight);
        Fates.Die(Context(world, log), knight, "illness", Array.Empty<Cause>());
        Assert.Equal(widow.Id, blade.HolderId);
    }

    [Fact]
    public void DeathInTheWilds_LosesWhatTheyCarried()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hunter = TestPeople.Add(world, 1, 30);
        TestPeople.Add(world, 1, 5, fatherId: hunter.Id, houseId: hunter.HouseId);
        var spear = TestPeople.GiveArtifact(world, hunter);
        Fates.Die(Context(world, log), hunter, "monsters", Array.Empty<Cause>(), lostInRegion: 2);
        Assert.Equal(ArtifactPlace.Lost, spear.Place);
        Assert.Equal(2, spear.RegionId);
        Assert.Empty(hunter.Memories);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void Killer_NeitherInherits_NorSwearsVengeanceOnThemselves()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var elder = TestPeople.Add(world, 1, 35, fatherId: lord.Id, houseId: lord.HouseId);
        var younger = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        Fates.Die(Context(world, log), lord, "a duel", Array.Empty<Cause>(), killer: elder);
        Assert.Equal(younger.Id, world.Settlements[1].LordId);
        Assert.NotEqual(elder.Id, elder.AmbitionTarget);
        Assert.Equal(elder.Id, younger.AmbitionTarget);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void FadedChildren_DoNotInherit()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var faded = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        faded.Faded = true;
        var crown = TestPeople.GiveArtifact(world, lord);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(0, world.Settlements[1].LordId);
        Assert.Equal(ArtifactPlace.Treasury, crown.Place);
    }

    [Fact]
    public void HeirMarriedToALord_PassesTheSeatOn()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var lord = TestPeople.Add(world, 1, 60, role: Role.Lord);
        var eldest = TestPeople.Add(world, 2, 35, Sex.Female, fatherId: lord.Id, houseId: lord.HouseId);
        var otherLord = TestPeople.Add(world, 2, 35, Sex.Male, Role.Lord);
        TestPeople.Marry(world, eldest, otherLord);
        var second = TestPeople.Add(world, 1, 30, fatherId: lord.Id, houseId: lord.HouseId);
        Fates.Die(Context(world, log), lord, "illness", Array.Empty<Cause>());
        Assert.Equal(second.Id, world.Settlements[1].LordId);
        Assert.Equal(2, otherLord.SettlementId);
        Assert.Empty(Invariants.Check(world, log));
    }

    [Fact]
    public void ChildLord_NeverFades()
    {
        var world = TestPeople.World();
        for (var i = 0; i < 40; i++) TestPeople.Add(world, 2, 30);
        var young = TestPeople.Add(world, 1, 15, role: Role.Lord);
        new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new LifeSystem() }, checkInvariants: true)
            .AdvanceDays(360);
        Assert.False(young.Faded);
    }

    [Fact]
    public void Killing_StirsVengeanceInKin()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var victim = TestPeople.Add(world, 1, 40);
        var son = TestPeople.Add(world, 1, 20, fatherId: victim.Id, houseId: victim.HouseId);
        var killer = TestPeople.Add(world, 2, 30);
        Fates.Die(Context(world, log), victim, "duel", Array.Empty<Cause>(), killer);
        Assert.Equal(Ambitions.Vengeance, son.Ambition);
        Assert.Equal(killer.Id, son.AmbitionTarget);
        Assert.True(Relations.Opinion(son, killer) < 0);
    }

    [Fact]
    public void Children_ComeOfAge()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var heir = TestPeople.Add(world, 1, 15, fatherId: lord.Id, houseId: lord.HouseId);
        heir.Traits.AddRange(new[] { Traits.Brave });
        var sim = Make(world);
        sim.AdvanceDays(360);
        var came = Assert.Single(sim.Log.All, e => e.Type == "ComingOfAge");
        Assert.Equal(heir.Ref, came.Actors["character"]);
        Assert.False(heir.Faded);
    }

    [Fact]
    public void Crowded_World_FadesSomeYoungsters_ButNeverTheHeir()
    {
        var world = TestPeople.World();
        for (var i = 0; i < 40; i++) TestPeople.Add(world, 2, 30);
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var heir = TestPeople.Add(world, 1, 15, fatherId: lord.Id, houseId: lord.HouseId);
        var others = Enumerable.Range(0, 20).Select(_ => TestPeople.Add(world, 2, 15)).ToList();
        Make(world).AdvanceDays(360);
        Assert.False(heir.Faded);
        Assert.InRange(others.Count(o => o.Faded), 12, 20);
    }

    [Fact]
    public void Risks_IncludeFamineForTheYoung()
    {
        var world = TestPeople.World();
        world.Regions[1].Conditions[Conditions.Shortage] = 7;
        var child = TestPeople.Add(world, 1, 8);
        var risks = LifeSystem.Risks(child, world.Regions[1], world.Date);
        Assert.Contains(risks, r => r.Cause == "famine" && r.Citing == 7 && r.PerYear == 0.06);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'LifeSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/Fates.cs`:
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

`sim/FantasyWorld.Sim/Systems/LifeSystem.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Ageing and death (spec §3 "Death and succession"). Monthly, each notable may die of old age,
/// illness, plague, famine or old wounds; the death cites the plague or shortage behind it. Children
/// come of age at 16, choosing an ambition, and some fade into the populace when the world already
/// has enough notables (heirs, lords, the married and artifact holders never fade).
/// </summary>
public sealed class LifeSystem : ISimSystem
{
    public const double NotablesPerSettlement = 4;

    public string Name => "life";

    /// <summary>The faded age and die off-stage, without events: they left the story when they faded.</summary>
    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var c in ctx.World.Characters.Values.Where(c => c.Faded && c.IsAlive))
            if (ctx.Rng.Chance(BaseMortality(c.Age(ctx.Date))))
            {
                c.Died = ctx.Date;
                c.DeathCause = "old age";
            }
    }

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var target = NotablesPerSettlement * world.Settlements.Values.Count(s => !s.Abandoned);
        var people = world.ActiveCharacters.ToList();
        foreach (var c in people)
        {
            if (!c.IsActive) continue;
            var region = world.Regions[world.Settlements[c.SettlementId].RegionId];
            var risks = Risks(c, region, ctx.Date);
            var total = risks.Sum(r => r.PerYear);
            if (ctx.Rng.Chance(total / 12))
            {
                var (cause, _, citing) = Pick(risks, ctx.Rng);
                Fates.Die(ctx, c, cause, citing == 0 ? Array.Empty<Cause>() : new[] { new Cause(citing, CauseKind.Trigger, 0.9) });
                continue;
            }
            if (TurnsAgeThisMonth(c, Character.AdultAge, ctx.Date)) ComeOfAge(ctx, c, people.Count / Math.Max(1, target));
        }
    }

    /// <summary>Yearly death risks with their causes and the event each cites (0 for none).</summary>
    public static List<(string Cause, double PerYear, long Citing)> Risks(Character c, Region region, WorldDate today)
    {
        var age = c.Age(today);
        var risks = new List<(string, double, long)> { (age >= Character.ElderAge ? "old age" : "illness", BaseMortality(age), 0) };
        if (region.Conditions.TryGetValue(Conditions.Plague, out var plague)) risks.Add(("plague", 0.08, plague));
        if (region.Conditions.TryGetValue(Conditions.Shortage, out var shortage))
            risks.Add(("famine", age < Character.AdultAge || age >= Character.ElderAge ? 0.06 : 0.03, shortage));
        if (c.Wounds.Count > 0) risks.Add(("old wounds", 0.01 * c.Wounds.Count, 0));
        return risks;
    }

    /// <summary>Yearly chance of dying of natural causes at this age.</summary>
    public static double BaseMortality(int age) => age switch
    {
        < 5 => 0.02,
        < 16 => 0.005,
        < 40 => 0.006,
        < 50 => 0.01,
        < 60 => 0.02,
        < 65 => 0.04,
        < 70 => 0.06,
        < 75 => 0.1,
        < 80 => 0.15,
        < 85 => 0.25,
        _ => 0.4,
    };

    private static (string Cause, double PerYear, long Citing) Pick(List<(string Cause, double PerYear, long Citing)> risks, Rng rng)
    {
        var roll = rng.NextDouble() * risks.Sum(r => r.PerYear);
        foreach (var risk in risks)
        {
            roll -= risk.PerYear;
            if (roll < 0) return risk;
        }
        return risks[^1];
    }

    public static bool TurnsAgeThisMonth(Character c, int years, WorldDate today)
    {
        var threshold = c.BirthDay + (long)years * WorldDate.DaysPerYear;
        return today.Day >= threshold && today.Day - WorldDate.DaysPerMonth < threshold;
    }

    /// <param name="crowding">Notables per target notable (1.0 = exactly as many as wanted).</param>
    private static void ComeOfAge(SimContext ctx, Character c, double crowding)
    {
        var world = ctx.World;
        var heirApparent = Fates.IsHeirApparent(world, c, ctx.Date);
        var keep = Math.Clamp(1.5 - crowding, 0.15, 1.0);
        var bound = heirApparent || c.Role != Role.None || c.SpouseId != 0
                    || world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id);
        if (!bound && !ctx.Rng.Chance(keep))
        {
            c.Faded = true;
            c.Memories.Clear();
            return;
        }
        c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
        ctx.Emit(new EventDraft("ComingOfAge",
            Actors: new Dictionary<string, EntityRef> { ["character"] = c.Ref },
            Place: world.Settlements[c.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["ambition"] = c.Ambition },
            Importance: Fates.Importance(c, heirApparent ? 15 : 5), Tags: new[] { "Characters" }));
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~LifeSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 15`. Then `dotnet test sim` → `Passed: 280` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add life system with death, succession and inheritance

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Marriages and births

Courtship is a decision point between the three best nearby matches (by opinion, same settlement, rank) and staying unwed. Kin never marry, and two lords never marry each other (each must keep a seat). The couple lives where the culture's lineage says, but a lord always stays at the seat. Fertile couples have children, who cite the marriage and join the lineage parent's house; childbirth is occasionally fatal.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/FamilySystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/FamilySystemTests.cs`

**Interfaces:**
- Consumes: `IDecider` (Task 5); `CharacterFactory` (Task 4); `Fates` (Task 6).
- Produces: `sealed class FamilySystem(IDecider? decider = null) : ISimSystem` (name `"family"`; `CourtshipPerMonth = 0.015`, `BirthPerMonth = 0.025`; `static bool Marriageable(c, today)`; `static WorldEvent Wed(ctx, a, b)`). Events: `MarriageWed` (actors `spouse`, `partner`), `CharacterBorn` (actors `child`, `mother`, `father`).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/FamilySystemTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FamilySystemTests
{
    private static Simulator Make(World world, IDecider? decider = null) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new FamilySystem(decider) }, checkInvariants: true);

    /// <summary>Always picks the first option.</summary>
    private sealed class EagerDecider : IDecider
    {
        public List<DecisionBrief> Briefs { get; } = new();
        public DecisionOption Decide(DecisionBrief brief, Rng rng)
        {
            Briefs.Add(brief);
            return brief.Options[0];
        }
    }

    [Fact]
    public void Neighbours_Marry()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 25, Sex.Male);
        var b = TestPeople.Add(world, 1, 24, Sex.Female);
        var decider = new EagerDecider();
        var sim = Make(world, decider);

        sim.AdvanceDays(360 * 10);

        Assert.Equal(b.Id, a.SpouseId);
        var wedding = sim.Log.All.First(e => e.Type == "MarriageWed");
        Assert.Equal(wedding.Id, a.MarriageEventId);
        Assert.Contains(decider.Briefs, br => br.Situation == "courtship" && br.Options.Any(o => o.Key == "unwed"));
    }

    [Fact]
    public void Kin_DoNotMarry()
    {
        var world = TestPeople.World();
        var mother = TestPeople.Add(world, 1, 60, Sex.Female);
        TestPeople.Add(world, 1, 25, Sex.Male, motherId: mother.Id, houseId: mother.HouseId);
        TestPeople.Add(world, 1, 24, Sex.Female, motherId: mother.Id, houseId: mother.HouseId);
        var sim = Make(world, new EagerDecider());
        sim.AdvanceDays(360 * 10);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "MarriageWed");
    }

    [Fact]
    public void Lords_StayAtTheirSeat()
    {
        var world = TestPeople.World();
        var lady = TestPeople.Add(world, 2, 30, Sex.Female, Role.Lord);
        var suitor = TestPeople.Add(world, 1, 30, Sex.Male);
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        FamilySystem.Wed(ctx, suitor, lady);
        Assert.Equal(2, suitor.SettlementId);
        Assert.Equal(2, lady.SettlementId);
    }

    [Fact]
    public void Matrilineal_Husbands_MoveToTheirWives()
    {
        var world = TestPeople.World();
        var culture = world.Cultures[1];
        world.Cultures[1] = new Culture { Id = 1, Name = culture.Name, HomeRegionId = 1, FaithId = 1, Lineage = Lineage.Matrilineal };
        var wife = TestPeople.Add(world, 2, 30, Sex.Female);
        var husband = TestPeople.Add(world, 1, 30, Sex.Male);
        FamilySystem.Wed(new SimContext(world, new EventLog(), new EventQueue(), new Rng(1)), husband, wife);
        Assert.Equal(2, husband.SettlementId);
    }

    [Fact]
    public void Couples_HaveChildren_InTheLineageHouse()
    {
        var world = TestPeople.World();
        var father = TestPeople.Add(world, 1, 25, Sex.Male);
        var mother = TestPeople.Add(world, 1, 22, Sex.Female);
        TestPeople.Marry(world, father, mother);
        var sim = Make(world);

        sim.AdvanceDays(360 * 10);

        Assert.InRange(father.Children.Count, 1, 6);
        foreach (var child in father.Children.Select(id => world.Characters[id]))
        {
            Assert.Equal(mother.Id, child.MotherId);
            Assert.Equal(father.HouseId, child.HouseId);
            Assert.Equal(1, child.SettlementId);
        }
        Assert.All(sim.Log.All.Where(e => e.Type == "CharacterBorn"), e => Assert.Equal(mother.Ref, e.Actors["mother"]));
    }

    [Fact]
    public void Elderly_Couples_HaveNoChildren()
    {
        var world = TestPeople.World();
        var father = TestPeople.Add(world, 1, 55, Sex.Male);
        var mother = TestPeople.Add(world, 1, 50, Sex.Female);
        TestPeople.Marry(world, father, mother);
        Make(world).AdvanceDays(360 * 5);
        Assert.Empty(mother.Children);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'FamilySystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/FamilySystem.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Marriages and births. Unmarried adults (18–50) court partners nearby, a decision point between the
/// best few matches and staying unwed. The couple lives where the culture's lineage says (a lord always
/// stays at their seat). Fertile couples have children, who join the lineage parent's house.
/// </summary>
public sealed class FamilySystem : ISimSystem
{
    public const double CourtshipPerMonth = 0.015;
    public const double BirthPerMonth = 0.025;

    private readonly IDecider _decider;

    public FamilySystem(IDecider? decider = null) => _decider = decider ?? new RulesDecider();

    public string Name => "family";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var singles = world.ActiveCharacters.Where(c => Marriageable(c, ctx.Date)).ToList();
        foreach (var c in singles)
            if (Marriageable(c, ctx.Date) && ctx.Rng.Chance(CourtshipPerMonth * (c.Ambition == Ambitions.Family ? 2 : 1)))
                Court(ctx, c, singles);

        foreach (var mother in world.ActiveCharacters.Where(c => c.Sex == Sex.Female && c.SpouseId != 0).ToList())
        {
            var age = mother.Age(ctx.Date);
            if (age is < 16 or > 45) continue;
            var father = world.Characters[mother.SpouseId];
            if (!father.IsActive) continue;
            var odds = BirthPerMonth * Math.Max(0.2, 1 - mother.Children.Count / 6.0);
            if (ctx.Rng.Chance(odds)) Birth(ctx, mother, father);
        }
    }

    public static bool Marriageable(Character c, WorldDate today) =>
        c.IsActive && c.SpouseId == 0 && c.Age(today) is >= 18 and <= 50;

    private void Court(SimContext ctx, Character suitor, IReadOnlyList<Character> singles)
    {
        var world = ctx.World;
        var home = world.Settlements[suitor.SettlementId];
        var nearby = new HashSet<long> { home.RegionId };
        foreach (var n in world.Geography?.Graph.Neighbours(home.RegionId) ?? Array.Empty<long>()) nearby.Add(n);
        var isLord = suitor.Role == Role.Lord;

        var matches = singles
            .Where(o => o.Sex != suitor.Sex && Marriageable(o, ctx.Date) && !Relations.AreKin(o, suitor))
            .Where(o => !(isLord && o.Role == Role.Lord))
            .Where(o => nearby.Contains(world.Settlements[o.SettlementId].RegionId))
            .Select(o => (Partner: o, Score: Relations.Opinion(suitor, o) + (o.SettlementId == suitor.SettlementId ? 15 : 0)
                                              + (o.Role == Role.Lord ? 10 : 0)))
            .Where(m => m.Score > -20)
            .OrderByDescending(m => m.Score).ThenBy(m => m.Partner.Id)
            .Take(3)
            .ToList();
        if (matches.Count == 0) return;

        var options = matches.Select(m => new DecisionOption(m.Partner.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Max(1, 40 + m.Score), $"marry {m.Partner.Name}"))
            .Append(new DecisionOption("unwed", 15, "stay unwed"))
            .ToList();
        var choice = _decider.Decide(new DecisionBrief(suitor.Id, "courtship", options), ctx.Rng);
        if (choice.Key == "unwed") return;
        Wed(ctx, suitor, world.Characters[long.Parse(choice.Key, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    public static WorldEvent Wed(SimContext ctx, Character a, Character b)
    {
        var world = ctx.World;
        var (husband, wife) = a.Sex == Sex.Male ? (a, b) : (b, a);
        var (stays, moves) = a.Role == Role.Lord ? (a, b)
            : b.Role == Role.Lord ? (b, a)
            : CharacterFactory.LineageParent(world, husband, wife) == wife ? (wife, husband) : (husband, wife);
        moves.SettlementId = stays.SettlementId;

        var wedding = ctx.Emit(new EventDraft("MarriageWed",
            Actors: new Dictionary<string, EntityRef> { ["spouse"] = stays.Ref, ["partner"] = moves.Ref },
            Place: world.Settlements[stays.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(a, 10), Fates.Importance(b, 10)),
            Tags: new[] { "Characters", "Family" }));
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
        a.MarriageEventId = b.MarriageEventId = wedding.Id;
        Fates.Remember(a, wedding.Id, b.Id, 40, 40);
        Fates.Remember(b, wedding.Id, a.Id, 40, 40);
        return wedding;
    }

    private static void Birth(SimContext ctx, Character mother, Character father)
    {
        var world = ctx.World;
        var houseId = CharacterFactory.LineageParent(world, father, mother)!.HouseId;
        var child = CharacterFactory.Create(world, ctx.Rng, mother.SettlementId, ctx.Rng.Chance(0.5) ? Sex.Female : Sex.Male,
            ctx.Date.Day, houseId, father: father, mother: mother);
        var born = ctx.Emit(new EventDraft("CharacterBorn",
            Actors: new Dictionary<string, EntityRef> { ["child"] = child.Ref, ["mother"] = mother.Ref, ["father"] = father.Ref },
            Place: world.Settlements[mother.SettlementId].Ref,
            Causes: mother.MarriageEventId != 0 ? new[] { new Cause(mother.MarriageEventId, CauseKind.Enabler, 0.5) } : null,
            Importance: Math.Max(Fates.Importance(mother, 5), Fates.Importance(father, 5)),
            Tags: new[] { "Characters", "Family" }));
        if (ctx.Rng.Chance(0.01))
            Fates.Die(ctx, mother, "childbirth", new[] { new Cause(born.Id, CauseKind.Trigger, 1.0) });
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~FamilySystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 286` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add family system with marriages and births

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Friendships, rivalries and fading memories

Notables who share a settlement sometimes meet. Trait compatibility plus chance decides whether they become friends or rivals, recorded once while the memory lasts. Memories fade 20% a year, so old grudges soften unless renewed. Opinions are always derived from memories, traits and kinship, never stored.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/RelationsSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/RelationsSystemTests.cs`

**Interfaces:**
- Consumes: `Relations`, `Memory`, `Traits` (Task 1); `Fates.Remember` (Task 6).
- Produces: `sealed class RelationsSystem : ISimSystem` (name `"relations"`; `MeetingPerMonth = 0.15`, `YearlyFade = 0.8`, `ForgetBelow = 2`). Events: `FriendshipFormed`, `RivalryBegan` (payload `over`, e.g. `honest vs deceitful`).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/RelationsSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class RelationsSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new RelationsSystem() }, checkInvariants: true);

    private static readonly string[] Virtuous = { Traits.Kind, Traits.Honest, Traits.Generous, Traits.Calm };
    private static readonly string[] Vicious = { Traits.Cruel, Traits.Deceitful, Traits.Greedy, Traits.Wrathful };

    [Fact]
    public void LikeMinds_BecomeFriends()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var b = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.Contains(sim.Log.All, e => e.Type == "FriendshipFormed");
        Assert.True(Relations.Opinion(a, b) >= Relations.FriendAt);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RivalryBegan");
    }

    [Fact]
    public void Opposites_BecomeRivals()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var b = TestPeople.Add(world, 1, 30, traits: Vicious);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        var rivalry = sim.Log.All.First(e => e.Type == "RivalryBegan");
        Assert.Contains(" vs ", rivalry.Payload["over"]);
        Assert.True(Relations.Opinion(a, b) <= Relations.RivalAt);
    }

    [Fact]
    public void Strangers_InDifferentSettlements_NeverMeet()
    {
        var world = TestPeople.World();
        TestPeople.Add(world, 1, 30, traits: Virtuous);
        TestPeople.Add(world, 2, 30, traits: Virtuous);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.Empty(sim.Log.All);
    }

    [Fact]
    public void Memories_FadeAndAreForgotten()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ev = log.Append(world.Date, new EventDraft("Insult"));
        var c = TestPeople.Add(world, 1, 30);
        c.Memories.Add(new Memory(ev.Id, 0, -50, 10));
        var sim = Make(world, log);

        sim.AdvanceDays(360);
        Assert.Equal(8, c.Memories.Single().Weight, 6);

        sim.AdvanceDays(360 * 7);
        Assert.Empty(c.Memories);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'RelationsSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/RelationsSystem.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Friendships and rivalries. Each month, two notables who share a settlement may meet; how it goes
/// depends on trait compatibility plus chance. A warm meeting makes them friends, a bitter one rivals,
/// each recorded as an event both remember (once: not again while that memory lasts). Memories fade by 20% a year and are forgotten below weight 2.
/// </summary>
public sealed class RelationsSystem : ISimSystem
{
    public const double MeetingPerMonth = 0.15;
    public const double YearlyFade = 0.8;
    public const double ForgetBelow = 2;

    public string Name => "relations";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var adults = world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child).ToList();
        foreach (var group in adults.GroupBy(c => c.SettlementId).OrderBy(g => g.Key))
        {
            var people = group.ToList();
            if (people.Count < 2 || !ctx.Rng.Chance(MeetingPerMonth)) continue;
            var i = ctx.Rng.NextInt(0, people.Count);
            var j = ctx.Rng.NextInt(0, people.Count - 1);
            Meet(ctx, people[i], people[j >= i ? j + 1 : j]);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var c in ctx.World.ActiveCharacters)
        {
            for (var i = c.Memories.Count - 1; i >= 0; i--)
            {
                var faded = c.Memories[i] with { Weight = c.Memories[i].Weight * YearlyFade };
                if (faded.Weight < ForgetBelow) c.Memories.RemoveAt(i);
                else c.Memories[i] = faded;
            }
        }
    }

    private static void Meet(SimContext ctx, Character a, Character b)
    {
        var quality = Traits.Compatibility(a.Traits, b.Traits) + ctx.Rng.NextInt(-30, 31);
        if (a.Has(Traits.Wrathful) || b.Has(Traits.Wrathful)) quality -= 10;
        if (a.Has(Traits.Kind) || b.Has(Traits.Kind)) quality += 5;
        var place = ctx.World.Settlements[a.SettlementId].Ref;
        var actors = new Dictionary<string, EntityRef> { ["a"] = a.Ref, ["b"] = b.Ref };

        if (quality >= 30 && !Shares(a, b, positive: true))
        {
            var ev = ctx.Emit(new EventDraft("FriendshipFormed", Actors: actors, Place: place,
                Importance: Math.Max(Fates.Importance(a, 5), Fates.Importance(b, 5)), Tags: new[] { "Characters" }));
            Fates.Remember(a, ev.Id, b.Id, 50, 60);
            Fates.Remember(b, ev.Id, a.Id, 50, 60);
        }
        else if (quality <= -35 && !Shares(a, b, positive: false))
        {
            var ev = ctx.Emit(new EventDraft("RivalryBegan", Actors: actors, Place: place,
                Payload: new Dictionary<string, string> { ["over"] = Grievance(a, b) },
                Importance: Math.Max(Fates.Importance(a, 10), Fates.Importance(b, 10)), Tags: new[] { "Characters" }));
            Fates.Remember(a, ev.Id, b.Id, -60, 60);
            Fates.Remember(b, ev.Id, a.Id, -60, 60);
        }
    }

    /// <summary>Whether a already remembers a warm (or bitter) moment with b.</summary>
    private static bool Shares(Character a, Character b, bool positive) =>
        a.Memories.Any(m => m.About == b.Id && (positive ? m.Feeling > 0 : m.Feeling < 0));

    /// <summary>A short reason for a rivalry, from the first clashing trait pair.</summary>
    private static string Grievance(Character a, Character b)
    {
        foreach (var trait in a.Traits)
            if (Traits.OppositeOf(trait) is { } opposite && b.Has(opposite))
                return $"{trait} vs {opposite}";
        return "an insult";
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~RelationsSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 290` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add friendships, rivalries and fading memories

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Deeds

What notables do with their lives. Each month some adults face a decision point shaped by ambition and traits: hunt the monsters of a nearby region (taming it), delve for a lost artifact, steal a coveted one, study or forge, fight a rival to first blood, or seek vengeance to the death. Encounters go to the arbiter and outcomes wound, maim or kill. Each deed is an event citing its motive (a duel cites the grudge memory), so `fw why` on a duel death walks back to the original insult. Wrathful or cruel losers of duels and thefts may swear vengeance, which grows feuds. Forging stops at the world's artifact cap (regions / 2).

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/DeedsSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/DeedsSystemTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3, 5, 6.
- Produces: `sealed class DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null) : ISimSystem` (name `"deeds"`; `ActPerMonth = 0.04`; `static int ArtifactCap(World)`; `static List<DecisionOption> Options(ctx, c, neighbours?)` with keys `rest`, `hunt:<region>`, `delve:<artifact>`, `steal:<artifact>`, `study`, `forge`, `avenge:<char>`, `duel:<char>`). Events: `MonstersSlain`/`MonsterHuntFailed`, `ArtifactFound`/`DelveFailed`, `ArtifactStolen`/`TheftFoiled`, `ArtifactForged`, `DuelFought` (payload `stakes`, `outcome`).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/DeedsSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class DeedsSystemTests
{
    /// <summary>Takes the first option whose key starts with the wanted verb, else rests.</summary>
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    /// <summary>Always returns the outcome with the given key.</summary>
    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static Simulator Make(World world, string verb, string? outcome = null, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider(verb), outcome is null ? null : new FixedArbiter(outcome)) },
            checkInvariants: true);

    [Fact]
    public void Hunter_SlaysMonsters_AndTamesTheRegion()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 80;
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = Make(world, "hunt", "success");

        sim.AdvanceDays(360 * 3);

        var slain = sim.Log.All.First(e => e.Type == "MonstersSlain");
        Assert.Equal(world.Regions[2].Ref, slain.Place);
        Assert.True(world.Regions[2].Danger < 80);
        Assert.True(hunter.Renown > 0);
    }

    [Fact]
    public void FailedHunt_CanKill_AndTheDeathCitesTheHunt()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 80;
        var hunter = TestPeople.Add(world, 1, 25);
        hunter.Ambition = Ambitions.Renown;
        var sim = Make(world, "hunt", "failure_death");

        sim.AdvanceDays(360 * 5);

        var died = Assert.Single(sim.Log.All, e => e.Type == "CharacterDied");
        Assert.Equal("monsters", died.Payload["cause"]);
        Assert.Equal("MonsterHuntFailed", sim.Log.Get(died.Causes[0].EventId).Type);
    }

    [Fact]
    public void Delver_FindsTheLostArtifact()
    {
        var world = TestPeople.World();
        var seeker = TestPeople.Add(world, 1, 25);
        seeker.Ambition = Ambitions.Artifact;
        var relic = TestPeople.GiveArtifact(world, seeker);
        (relic.Place, relic.HolderId, relic.RegionId) = (ArtifactPlace.Lost, 0, 2);
        var sim = Make(world, "delve", "success");

        sim.AdvanceDays(360 * 3);

        Assert.Equal(ArtifactPlace.Held, relic.Place);
        Assert.Equal(seeker.Id, relic.HolderId);
        Assert.Equal("found", relic.Provenance.Single().How);
    }

    [Fact]
    public void Avenger_KillsTheKinslayer_InADuelCitingTheGrudge()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var murder = log.Append(world.Date, new EventDraft("CharacterDied"));
        var avenger = TestPeople.Add(world, 1, 25, traits: new[] { Traits.Brave });
        var foe = TestPeople.Add(world, 2, 40);
        avenger.Ambition = Ambitions.Vengeance;
        avenger.AmbitionTarget = foe.Id;
        avenger.Memories.Add(new Memory(murder.Id, foe.Id, -80, 80));
        var sim = Make(world, "avenge", "attacker_kills", log);

        sim.AdvanceDays(360 * 3);

        var duel = sim.Log.All.Single(e => e.Type == "DuelFought");
        Assert.Equal(murder.Id, duel.Causes.Single().EventId);
        Assert.False(foe.IsAlive);
        Assert.NotEqual(Ambitions.Vengeance, avenger.Ambition);
        Assert.Contains(new HistoryQuery(sim.Log).RootCauses(sim.Log.All.Single(e => e.Type == "CharacterDied" && e.Id != murder.Id).Id),
            e => e.Id == murder.Id);
    }

    [Fact]
    public void Vengeance_IsDropped_WhenTheFoeIsGone()
    {
        var world = TestPeople.World();
        var avenger = TestPeople.Add(world, 1, 25, role: Role.Knight);
        var foe = TestPeople.Add(world, 2, 40);
        avenger.Ambition = Ambitions.Vengeance;
        avenger.AmbitionTarget = foe.Id;
        foe.Died = world.Date;
        Make(world, "rest").AdvanceDays(360 * 5);
        Assert.Equal(Ambitions.Renown, avenger.Ambition);
        Assert.Equal(0, avenger.AmbitionTarget);
    }

    [Fact]
    public void Rivals_FightToFirstBlood()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var insult = log.Append(world.Date, new EventDraft("RivalryBegan"));
        var hothead = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Wrathful });
        var other = TestPeople.Add(world, 1, 30);
        hothead.Memories.Add(new Memory(insult.Id, other.Id, -90, 90));
        var sim = Make(world, "duel", "attacker_first_blood", log);

        sim.AdvanceDays(360 * 3);

        var duel = sim.Log.All.First(e => e.Type == "DuelFought");
        Assert.Equal("first blood", duel.Payload["stakes"]);
        Assert.True(hothead.IsAlive && other.IsAlive);
    }

    [Fact]
    public void WrathfulLosers_SwearVengeance()
    {
        var sworn = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var world = TestPeople.World(seed);
            var log = new EventLog();
            var insult = log.Append(world.Date, new EventDraft("RivalryBegan"));
            var bully = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave });
            var victim = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Wrathful });
            bully.Memories.Add(new Memory(insult.Id, victim.Id, -90, 90));
            Make(world, "duel", "attacker_first_blood", log).AdvanceDays(360 * 3);
            if (victim.Ambition == Ambitions.Vengeance && victim.AmbitionTarget == bully.Id) sworn++;
        }
        Assert.InRange(sworn, 5, 20);
    }

    [Fact]
    public void Thief_TakesTheArtifact()
    {
        var world = TestPeople.World();
        var thief = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Deceitful });
        thief.Ambition = Ambitions.Artifact;
        thief.Attributes.Intrigue = 20;
        var victim = TestPeople.Add(world, 1, 30);
        victim.Attributes.Intrigue = 0;
        var crown = TestPeople.GiveArtifact(world, victim);
        var sim = Make(world, "steal");

        sim.AdvanceDays(360 * 6);

        Assert.Contains(sim.Log.All, e => e.Type is "ArtifactStolen" or "TheftFoiled");
        Assert.True(Relations.Opinion(victim, thief) < 0);
        if (crown.HolderId == thief.Id) Assert.Equal("stolen", crown.Provenance.Last().How);
    }

    [Fact]
    public void Mage_ForgesAnArtifact_WhoseOriginIsTheForging()
    {
        var world = TestPeople.World();
        world.Regions[1].LeyPower = 80;
        var mage = TestPeople.Add(world, 1, 40, role: Role.Mage);
        mage.Ambition = Ambitions.Magic;
        mage.Attributes.Lore = 20;
        var sim = Make(world, "forge");

        sim.AdvanceDays(360 * 6);

        var forged = sim.Log.All.First(e => e.Type == "ArtifactForged");
        var artifact = world.Artifacts[forged.Actors["artifact"].Id];
        Assert.Equal(forged.Id, artifact.OriginEventId);
        Assert.Equal(mage.Id, artifact.Provenance[0].To);
    }

    [Fact]
    public void Options_AlwaysIncludeRest_AndFitTheAmbition()
    {
        var world = TestPeople.World();
        var scholar = TestPeople.Add(world, 1, 30);
        scholar.Ambition = Ambitions.Magic;
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        var keys = DeedsSystem.Options(ctx, scholar).Select(o => o.Key).ToList();
        Assert.Contains("rest", keys);
        Assert.Contains("study", keys);
        Assert.DoesNotContain("forge", keys);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'DeedsSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/DeedsSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What notables do with their lives (spec §3 "Monthly decisions"). Each month some adults act on
/// their ambition: a decision point offering hunts, delves, duels, study, forging or theft (plus
/// "carry on"). Risky deeds are encounters resolved by the arbiter; outcomes wound, kill, move
/// artifacts, tame regions and build renown. Every deed is an event citing its motive.
/// </summary>
public sealed class DeedsSystem : ISimSystem
{
    public const double ActPerMonth = 0.04;

    /// <summary>Artifacts the world can hold before forging becomes impossible (spec: 20–60 per world).</summary>
    public static int ArtifactCap(World world) => Math.Max(10, world.Regions.Count / 2);

    private readonly IDecider _decider;
    private readonly IArbiter _arbiter;

    public DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null)
    {
        _decider = decider ?? new RulesDecider();
        _arbiter = arbiter ?? new RulesArbiter();
    }

    public string Name => "deeds";

    public void OnMonthlyTick(SimContext ctx)
    {
        var residents = ctx.World.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var c in residents.SelectMany(g => g).OrderBy(c => c.Id).ToList())
        {
            if (!c.IsActive || c.Stage(ctx.Date) == LifeStage.Child) continue;
            if (c.Ambition == Ambitions.Vengeance
                && !(ctx.World.Characters.TryGetValue(c.AmbitionTarget, out var target) && target.IsActive && target.Id != c.Id))
            {
                c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
                c.AmbitionTarget = 0;
            }
            var drive = c.Has(Traits.Diligent) ? 1.5 : c.Has(Traits.Lazy) ? 0.5 : 1.0;
            if (!ctx.Rng.Chance(ActPerMonth * drive)) continue;
            var options = Options(ctx, c, residents[c.SettlementId]);
            if (options.Count <= 1) continue;
            var choice = _decider.Decide(new DecisionBrief(c.Id, c.Ambition, options), ctx.Rng);
            Act(ctx, c, choice.Key);
        }
    }

    /// <summary>The legal deeds for this character now, weighted by ambition and traits. Keys: verb:target.</summary>
    public static List<DecisionOption> Options(SimContext ctx, Character c, IEnumerable<Character>? neighbours = null)
    {
        var world = ctx.World;
        var home = world.Settlements[c.SettlementId];
        var nearby = Nearby(world, home.RegionId).ToList();
        var options = new List<DecisionOption> { new("rest", 30, "carry on as before") };
        var bold = c.Has(Traits.Brave) ? 2.0 : c.Has(Traits.Craven) ? 0.2 : 1.0;

        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        var lost = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && nearby.Any(r => r.Id == a.RegionId))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (lost is not null)
            options.Add(new($"delve:{lost.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 10 : 2) * bold,
                $"seek {lost.Name} in {world.Regions[lost.RegionId].Name}"));

        if (c.Ambition == Ambitions.Artifact && (c.Has(Traits.Deceitful) || c.Has(Traits.Greedy)))
        {
            var coveted = world.Artifacts.Values
                .Where(a => a.Place == ArtifactPlace.Held && a.HolderId != c.Id && world.Characters[a.HolderId].SettlementId == c.SettlementId)
                .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
            if (coveted is not null) options.Add(new($"steal:{coveted.Id}", 10, $"steal {coveted.Name}"));
        }

        if (c.Ambition == Ambitions.Magic)
        {
            options.Add(new("study", 40, "study the arcane"));
            if (c.Attributes.Lore >= 15 && world.Regions[home.RegionId].LeyPower >= 50) options.Add(new("forge", 20, "forge an artifact"));
        }

        if (c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive && foe.Id != c.Id
            && nearby.Any(r => r.Id == world.Settlements[foe.SettlementId].RegionId))
            options.Add(new($"avenge:{foe.Id}", 50 * bold, $"challenge {foe.Name} to the death"));

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        if (rival is not null && (c.Has(Traits.Wrathful) || c.Has(Traits.Brave)))
            options.Add(new($"duel:{rival.Id}", 25 * bold, $"challenge {rival.Name} to first blood"));
        return options;
    }

    private void Act(SimContext ctx, Character c, string key)
    {
        var parts = key.Split(':');
        var target = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        switch (parts[0])
        {
            case "hunt": Hunt(ctx, c, ctx.World.Regions[target]); break;
            case "delve": Delve(ctx, c, ctx.World.Artifacts[target]); break;
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            case "avenge" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        var outcome = _arbiter.Resolve(Encounters.HuntIn(ctx.World, hunter, region), ctx.Rng);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonstersSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? 25 : 15), Tags: new[] { "Characters", "Deeds" }));
        if (outcome.AttackerWins)
        {
            region.Danger = Math.Max(0, region.Danger - 15);
            Glory(ctx, hunter, 8);
        }
        Suffer(ctx, hunter, outcome.AttackerFate, "monsters", hunt, lostIn: region.Id);
    }

    private void Delve(SimContext ctx, Character delver, Artifact artifact)
    {
        var region = ctx.World.Regions[artifact.RegionId];
        var outcome = _arbiter.Resolve(Encounters.DelveIn(ctx.World, delver, region), ctx.Rng);
        var delve = ctx.Emit(new EventDraft(outcome.AttackerWins ? "ArtifactFound" : "DelveFailed",
            Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = artifact.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: artifact.Provenance.Count > 0 ? new[] { new Cause(artifact.Provenance[^1].EventId, CauseKind.Enabler, 0.5) } : null,
            Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(20 + artifact.Fame / 3) : 15),
            Tags: new[] { "Characters", "Deeds", "Artifacts" }));
        if (outcome.AttackerWins)
        {
            Fates.MoveToHolder(artifact, delver, delve.Id, "found");
            artifact.Fame = Math.Min(100, artifact.Fame + 5);
            Glory(ctx, delver, 10);
        }
        Suffer(ctx, delver, outcome.AttackerFate, "a labyrinth's guardians", delve, lostIn: region.Id);
    }

    private static void Steal(SimContext ctx, Character thief, Artifact artifact)
    {
        var victim = ctx.World.Characters[artifact.HolderId];
        var guard = victim.Attributes.Intrigue + artifact.Bonus("intrigue") + 5 + (victim.Has(Traits.Paranoid) ? 5 : 0);
        var success = ctx.Rng.Chance(thief.Attributes.Intrigue / (double)(thief.Attributes.Intrigue + guard));
        var ev = ctx.Emit(new EventDraft(success ? "ArtifactStolen" : "TheftFoiled",
            Actors: new Dictionary<string, EntityRef> { ["thief"] = thief.Ref, ["victim"] = victim.Ref, ["artifact"] = artifact.Ref },
            Place: ctx.World.Settlements[thief.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(victim, 15), (int)(10 + artifact.Fame / 3)), Tags: new[] { "Characters", "Artifacts" }));
        Fates.Remember(victim, ev.Id, thief.Id, success ? -80 : -50, 80);
        if (success)
        {
            Fates.MoveToHolder(artifact, thief, ev.Id, "stolen");
            SwearVengeance(ctx, victim, thief);
        }
        else Shame(thief);
    }

    private static void Forge(SimContext ctx, Character mage)
    {
        var world = ctx.World;
        var region = world.Regions[world.Settlements[mage.SettlementId].RegionId];
        var room = 1 - world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) / (double)ArtifactCap(world);
        if (!ctx.Rng.Chance(Math.Max(0, room) * (mage.Attributes.Lore - 12) / 30.0)) return;
        var artifactRef = new EntityRef(Artifact.Kind, world.Ids.Peek(Artifact.Kind));
        var forged = ctx.Emit(new EventDraft("ArtifactForged",
            Actors: new Dictionary<string, EntityRef> { ["maker"] = mage.Ref, ["artifact"] = artifactRef },
            Place: world.Settlements[mage.SettlementId].Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(mage, 35), Tags: new[] { "Characters", "Artifacts" }));
        var artifact = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(mage.CultureId)?.Style),
            forged.Id, rareMaterials: region.Resources.Keys.Where(ResourceKinds.IsRare).ToList());
        world.Artifacts[artifact.Id] = artifact;
        Fates.MoveToHolder(artifact, mage, forged.Id, "forged");
        Glory(ctx, mage, 10);
    }

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath), ctx.Rng);
        var duel = ctx.Emit(new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key },
            Causes: grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : null,
            Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 30 : 15), Fates.Importance(defender, toTheDeath ? 30 : 15)),
            Tags: new[] { "Characters", "Deeds" }));
        var (winner, loser) = outcome.AttackerWins ? (challenger, defender) : (defender, challenger);
        Glory(ctx, winner, toTheDeath ? 8 : 4);
        Fates.Remember(loser, duel.Id, winner.Id, -60, 60);
        if (!toTheDeath && ctx.Rng.Chance(0.5)) Shame(loser);
        if (!toTheDeath && loser.Id != winner.Id) SwearVengeance(ctx, loser, winner);
        if (toTheDeath && outcome.AttackerWins && challenger.Ambition == Ambitions.Vengeance)
            challenger.Ambition = CharacterFactory.ChooseAmbition(challenger, ctx.Rng);
        Suffer(ctx, challenger, outcome.AttackerFate, "a duel", duel, outcome.AttackerFate == Fate.Dead ? defender : null);
        Suffer(ctx, defender, outcome.DefenderFate, "a duel", duel, outcome.DefenderFate == Fate.Dead ? challenger : null);
    }

    private static void Suffer(SimContext ctx, Character c, Fate fate, string cause, WorldEvent deed,
        Character? killer = null, long lostIn = 0)
    {
        if (fate == Fate.Dead)
        {
            if (c.IsAlive)
                Fates.Die(ctx, c, cause, new[] { new Cause(deed.Id, CauseKind.Trigger, 1.0) },
                    killer is { IsAlive: true } ? killer : null, lostIn);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
    }

    /// <summary>Renown for the deed; the artifacts they carry grow famous with them.</summary>
    private static void Glory(SimContext ctx, Character c, double renown)
    {
        c.Renown = Math.Min(100, c.Renown + renown);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
        foreach (var a in ctx.World.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id))
            a.Fame = Math.Min(100, a.Fame + renown / 2);
    }

    /// <summary>The wrathful and the cruel do not forgive a humiliation or a theft: half of them swear vengeance.</summary>
    private static void SwearVengeance(SimContext ctx, Character wronged, Character wrongdoer)
    {
        if (wronged.Has(Traits.Craven) || !(wronged.Has(Traits.Wrathful) || wronged.Has(Traits.Cruel))) return;
        if (wronged.Ambition == Ambitions.Vengeance || !ctx.Rng.Chance(0.5)) return;
        wronged.Ambition = Ambitions.Vengeance;
        wronged.AmbitionTarget = wrongdoer.Id;
    }

    private static void Shame(Character c)
    {
        if (c.Has(Traits.Humiliated)) return;
        c.Traits.Add(Traits.Humiliated);
        c.Traits.Sort(StringComparer.Ordinal);
    }

    private static IEnumerable<Region> Nearby(World world, long regionId) =>
        new[] { regionId }.Concat(world.Geography?.Graph.Neighbours(regionId) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]);
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DeedsSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 300` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add deeds: hunts, delves, duels, theft and forging

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Artifact fame, curses and treasuries

Artifacts have lives too (spec §4). Hidden curses come to light, citing how the holder acquired the artifact, and mark them: paranoia, bloodlust, a wasting sickness, misfortune. Fame grows in renowned hands and fades while an artifact lies lost. A lord claims what lies in their seat's treasury.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/ArtifactSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/ArtifactSystemTests.cs`

**Interfaces:**
- Consumes: `Artifact`, `Fates` (Tasks 1, 6).
- Produces: `sealed class ArtifactSystem : ISimSystem` (name `"artifacts"`; yearly; `CurseRevealPerYear = 0.15`; `static void Become(Character, trait)`). Events: `ArtifactCurseRevealed` (payload `curse`), `ArtifactClaimed`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/ArtifactSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class ArtifactSystemTests
{
    private static Simulator Make(World world) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new ArtifactSystem() }, checkInvariants: true);

    [Fact]
    public void Curse_ComesToLight_AndMarksTheHolder()
    {
        var world = TestPeople.World();
        var holder = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Trusting });
        var cursed = new Artifact
        {
            Id = world.Ids.Take(Artifact.Kind), Name = "The Whispering Ring", Curse = "paranoia",
            Place = ArtifactPlace.Held, HolderId = holder.Id, Powers = { new("attribute", "intrigue", 2) },
        };
        world.Artifacts[cursed.Id] = cursed;
        var sim = Make(world);

        sim.AdvanceDays(360 * 30);

        Assert.True(cursed.CurseRevealed);
        var revealed = Assert.Single(sim.Log.All, e => e.Type == "ArtifactCurseRevealed");
        Assert.Equal("paranoia", revealed.Payload["curse"]);
        Assert.Contains(Traits.Paranoid, holder.Traits);
        Assert.DoesNotContain(Traits.Trusting, holder.Traits);
    }

    [Fact]
    public void LostArtifacts_FadeFromMemory()
    {
        var world = TestPeople.World();
        var relic = new Artifact { Id = world.Ids.Take(Artifact.Kind), Name = "The Drowned Crown", Fame = 40, Place = ArtifactPlace.Lost, RegionId = 2 };
        world.Artifacts[relic.Id] = relic;
        Make(world).AdvanceDays(360 * 10);
        Assert.Equal(30, relic.Fame);
    }

    [Fact]
    public void RenownedHolders_MakeArtifactsFamous()
    {
        var world = TestPeople.World();
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 60;
        var blade = TestPeople.GiveArtifact(world, hero);
        Make(world).AdvanceDays(360 * 10);
        Assert.Equal(10, blade.Fame);
    }

    [Fact]
    public void Lord_ClaimsTheTreasury()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var crown = new Artifact { Id = world.Ids.Take(Artifact.Kind), Name = "The Oak Crown", Place = ArtifactPlace.Treasury, SettlementId = 1 };
        world.Artifacts[crown.Id] = crown;
        var sim = Make(world);
        sim.AdvanceDays(360);
        Assert.Equal(lord.Id, crown.HolderId);
        Assert.Equal("claimed", crown.Provenance.Single().How);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'ArtifactSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/ArtifactSystem.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Artifacts over the years (spec §4): hidden curses come to light and mark their holders; fame grows
/// in renowned hands and fades while lost; a lord claims artifacts lying in their seat's treasury.
/// </summary>
public sealed class ArtifactSystem : ISimSystem
{
    public const double CurseRevealPerYear = 0.15;

    public string Name => "artifacts";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var artifact in world.Artifacts.Values)
        {
            switch (artifact.Place)
            {
                case ArtifactPlace.Held:
                    var holder = world.Characters[artifact.HolderId];
                    if (holder.Renown >= 30) artifact.Fame = Math.Min(100, artifact.Fame + 1);
                    if (artifact.Curse is not null && !artifact.CurseRevealed && ctx.Rng.Chance(CurseRevealPerYear))
                        RevealCurse(ctx, artifact, holder);
                    break;
                case ArtifactPlace.Lost:
                    artifact.Fame = Math.Max(5, artifact.Fame - 1);
                    break;
                case ArtifactPlace.Treasury:
                    var seat = world.Settlements[artifact.SettlementId];
                    if (seat.LordId != 0) Claim(ctx, artifact, world.Characters[seat.LordId], seat);
                    break;
            }
        }
    }

    private static void RevealCurse(SimContext ctx, Artifact artifact, Character holder)
    {
        artifact.CurseRevealed = true;
        var acquired = artifact.Provenance.Count > 0 ? artifact.Provenance[^1].EventId : artifact.OriginEventId;
        var ev = ctx.Emit(new EventDraft("ArtifactCurseRevealed",
            Actors: new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["holder"] = holder.Ref },
            Place: ctx.World.Settlements[holder.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["curse"] = artifact.Curse! },
            Causes: acquired != 0 ? new[] { new Cause(acquired, CauseKind.Enabler, 0.5) } : null,
            Importance: (int)Math.Clamp(20 + artifact.Fame / 2, 0, 100), Tags: new[] { "Artifacts" }));
        switch (artifact.Curse)
        {
            case "paranoia": Become(holder, Traits.Paranoid); break;
            case "bloodlust": Become(holder, Traits.Wrathful); break;
            case "wasting": holder.Wounds.Add("wasting sickness"); break;
            case "misfortune": holder.Renown = Math.Max(0, holder.Renown - 10); break;
        }
        Fates.Remember(holder, ev.Id, 0, -40, 40);
    }

    private static void Claim(SimContext ctx, Artifact artifact, Character lord, Settlement seat)
    {
        var ev = ctx.Emit(new EventDraft("ArtifactClaimed",
            Actors: new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["claimant"] = lord.Ref },
            Place: seat.Ref,
            Importance: (int)Math.Clamp(10 + artifact.Fame / 3, 0, 100), Tags: new[] { "Artifacts" }));
        Fates.MoveToHolder(artifact, lord, ev.Id, "claimed");
    }

    /// <summary>Gains a trait, dropping its opposite.</summary>
    public static void Become(Character c, string trait)
    {
        if (Traits.OppositeOf(trait) is { } opposite) c.Traits.Remove(opposite);
        if (!c.Has(trait)) c.Traits.Add(trait);
        c.Traits.Sort(StringComparer.Ordinal);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~ArtifactSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 304` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): add artifact fame, curses and treasury claims

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Populace promotion

Spec §3 promotion: when the story needs a person, one steps out of the aggregate population. An empty seat gets a new lord: the ablest resident, else a commoner with a new house, citing the death that emptied the seat. A revolt finds a leader citing the revolt; a monster-ridden region raises a hero citing the danger; a leaderless village chooses an elder. Notables in an abandoned settlement move to the nearest living one and its lord loses the seat, citing the abandonment.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/PromotionSystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/PromotionSystemTests.cs`

**Interfaces:**
- Consumes: `CharacterFactory` (Task 4); `Fates.VacantSeat` (Task 6); `Conditions` (Plan 2).
- Produces: `sealed class PromotionSystem : ISimSystem` (name `"promotion"`; `HeroPerMonth = 0.2`, `ElderPerMonth = 0.05`). Events: `NewLordRose`, `RebelLeaderRose`, `HeroRose`, `ElderChosen`, `NotablesFled`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/PromotionSystemTests.cs`:
```csharp
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
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'PromotionSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Systems/PromotionSystem.cs`:
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
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned).ToList())
        {
            if (s.Tier >= SettlementTier.Town && s.LordId == 0) RaiseLord(ctx, s, residents[s.Id]);
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

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PromotionSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 309` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(sim): promote commoners to lords, rebels, heroes and elders

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Character systems in the world, and CLI

The full system order is now Life → Economy → Regions → Settlements → Family → Relations → Deeds → Artifacts → Promotion (spec §7: ageing first, characters after politics). The CLI can tell people's stories: `fw characters`, `fw life <id>` (a biography with family, traits, bonds and a timeline), `fw artifacts` and `fw artifact <id>` (powers and provenance). Event lines name people (`Greithbuth of Soen (char#251)`) and artifacts.

**Files:**
- Create: `sim/FantasyWorld.Cli/CliApp.People.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Query/HistoryQuery.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Cli/CliAppTests.cs`, `sim/FantasyWorld.Sim.Tests/Query/HistoryQueryTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `DefaultSystems.Create()` with nine systems; `HistoryQuery.Life(EntityRef)`; `CliApp` becomes `partial` with `CliApp.People.cs`; CLI commands `characters [--settlement id]`, `life <id>`, `artifacts`, `artifact <id>`; `fw region` shows culture, faith and lords.

- [x] **Step 1: Write the failing tests**

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
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'HistoryQuery' does not contain a definition for 'Life' and no accessible extension method 'Life' accepting a first argument of type 'HistoryQuery' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Cli/CliApp.People.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

/// <summary>Commands about people and artifacts: characters, life, artifacts, artifact.</summary>
public static partial class CliApp
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Characters(string path, string[] rest, TextWriter output)
    {
        var settlementText = Option(rest, "--settlement");
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var people = world.ActiveCharacters;
        if (settlementText is not null)
        {
            var id = long.Parse(settlementText, Inv);
            if (!world.Settlements.ContainsKey(id)) throw new KeyNotFoundException($"No settlement #{id}.");
            people = people.Where(c => c.SettlementId == id);
        }
        output.WriteLine("    id name                           age role        renown ambition   home");
        foreach (var c in people)
            output.WriteLine(string.Create(Inv,
                $"{c.Id,6} {FullName(world, c),-30} {c.Age(world.Date),3} {c.Role,-10} {c.Renown,6:0} {c.Ambition,-10} {world.Settlements[c.SettlementId].Name}"));
        return 0;
    }

    private static int Life(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var c = world.Characters.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No character #{id}.");
        string Name(long who) => who != 0 && world.Characters.TryGetValue(who, out var k) ? $"{FullName(world, k)} (char#{who})" : "—";

        var born = new Sim.Core.WorldDate(c.BirthDay);
        var status = c.Died is { } died ? $"died {died} aged {c.Age(died)} of {c.DeathCause}" : $"aged {c.Age(world.Date)}";
        output.WriteLine($"{FullName(world, c)} (char#{c.Id}) — born {born}, {status}");
        output.WriteLine($"  {c.Sex}, {world.Cultures.GetValueOrDefault(c.CultureId)?.Name ?? "?"}, of {world.Faiths.GetValueOrDefault(c.FaithId)?.Name ?? "?"}; " +
                         $"{c.Role} at {world.Settlements[c.SettlementId].Name}{(c.Faded ? " (faded into the populace)" : "")}");
        output.WriteLine($"  traits: {string.Join(", ", c.Traits)}; ambition: {c.Ambition}; renown {c.Renown.ToString("0", Inv)}");
        var a = c.Attributes;
        output.WriteLine($"  martial {a.Martial}, diplomacy {a.Diplomacy}, stewardship {a.Stewardship}, intrigue {a.Intrigue}, lore {a.Lore}, prowess {a.Prowess}");
        if (c.Wounds.Count > 0) output.WriteLine($"  wounds: {string.Join(", ", c.Wounds)}");
        output.WriteLine($"  parents: {Name(c.FatherId)}, {Name(c.MotherId)}; spouse: {Name(c.SpouseId)}");
        if (c.Children.Count > 0) output.WriteLine($"  children: {string.Join(", ", c.Children.Select(Name))}");
        var held = world.Artifacts.Values.Where(x => x.Place == ArtifactPlace.Held && x.HolderId == c.Id).ToList();
        if (held.Count > 0) output.WriteLine($"  carries: {string.Join(", ", held.Select(x => $"{x.Name} (artifact#{x.Id})"))}");
        var feelings = c.Memories.Where(m => m.About != 0).GroupBy(m => m.About)
            .Select(g => (Who: g.Key, Opinion: Relations.Opinion(c, world.Characters[g.Key])))
            .Where(f => Math.Abs(f.Opinion) >= Relations.FriendAt).OrderBy(f => f.Who).ToList();
        if (feelings.Count > 0)
            output.WriteLine("  bonds: " + string.Join(", ", feelings.Select(f => $"{(f.Opinion > 0 ? "friend" : "rival")} {Name(f.Who)}")));
        output.WriteLine("  Life:");
        foreach (var ev in new HistoryQuery(log).Life(c.Ref)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
        return 0;
    }

    private static int ArtifactList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id name                                type     material     fame  where");
        foreach (var x in world.Artifacts.Values)
            output.WriteLine(string.Create(Inv, $"{x.Id,6} {x.Name,-35} {x.Type,-8} {x.Material,-12} {x.Fame,4:0}  {Whereabouts(world, x)}"));
        return 0;
    }

    private static int ArtifactDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var x = world.Artifacts.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No artifact #{id}.");
        output.WriteLine($"{x.Name} (artifact#{x.Id}) — {x.Type.ToString().ToLowerInvariant()} of {x.Material}, fame {x.Fame.ToString("0", Inv)}");
        output.WriteLine($"  {Whereabouts(world, x)}");
        output.WriteLine("  powers: " + string.Join(", ", x.Powers.Select(p => p.Kind == "attribute" ? $"+{p.Amount} {p.Attribute}" : p.Kind)));
        if (x.CurseRevealed) output.WriteLine($"  cursed: {x.Curse}");
        output.WriteLine(x.OriginEventId == 0 ? "  Made before recorded history." : "  Made: " + EventText.Describe(log.Get(x.OriginEventId), NameOf(world)));
        if (x.Provenance.Count > 0)
        {
            output.WriteLine("  Provenance:");
            foreach (var entry in x.Provenance)
                output.WriteLine($"    {entry.How}: {EventText.Describe(log.Get(entry.EventId), NameOf(world))}");
        }
        return 0;
    }

    private static string Whereabouts(World world, Artifact x) => x.Place switch
    {
        ArtifactPlace.Held => $"held by {FullName(world, world.Characters[x.HolderId])} (char#{x.HolderId})",
        ArtifactPlace.Treasury => $"in the treasury of {world.Settlements[x.SettlementId].Name}",
        ArtifactPlace.Lost => $"lost in {world.Regions[x.RegionId].Name}",
        _ => "destroyed",
    };

    private static string FullName(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;
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
          fw map <world-file> [--layer biome|regions]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
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
/// Politics → Military → Characters → Structures). Settlements stand in for politics until realms
/// arrive in Plan 4; the character systems follow.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new FamilySystem(),
        new RelationsSystem(),
        new DeedsSystem(),
        new ArtifactSystem(),
        new PromotionSystem(),
    };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CliAppTests|FullyQualifiedName~HistoryQueryTests"`
Expected: `Passed!  - Failed: 0, Passed: 28`. Then `dotnet test sim` → `Passed: 314` in total.

- [x] **Step 5: Smoke-test the real binary**

```bash
dotnet run --project sim/FantasyWorld.Cli -- new worlds/demo.fw.db --seed 7
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/demo.fw.db 60y
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/demo.fw.db
```
Expected: roughly 4,000 named event lines in a few seconds (births, marriages, rivalries, hunts, duels, successions…), then a table of several hundred living notables. Pick a lord's id and run `fw life worlds/demo.fw.db <id>`: you should see a biography with traits, parents, spouse, children and a dated life timeline. Pick a `CharacterDied … cause=a duel` id and run `fw why worlds/demo.fw.db <id> --depth 5`: the chain should run death ← duel ← the grudge (a rivalry, theft or earlier killing). Then `rm -r worlds`.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(cli): run character systems; add characters, life and artifact commands

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: People health checks and README

These checks hold the character layer to its promises: across two centuries the cast stays near its target, every town keeps a lord, artifacts stay within the cap, and the life cycle (births, marriages, coming of age, succession, duels) all happens. A medium world grows feuds whose deaths trace back through duels to grudges.

**Files:**
- Modify: `README.md`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/PeopleHealthTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `PeopleHealthTests` (4 tests); together with Task 1's invariants (faded people hold nothing, lords live at their seat, nobody lives in a ruin, nobody hunts themselves) these were run for 300 years on three medium worlds with invariants on, clean; README commands for people and artifacts.

- [x] **Step 1: Write the checks**

`sim/FantasyWorld.Sim.Tests/Engine/PeopleHealthTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks of the character systems: a lasting cast, kept seats, bounded artifacts, feuds with causes.</summary>
public class PeopleHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size, bool checkInvariants) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoCenturies_KeepALivingCast(ulong seed)
    {
        var sim = NewSim(seed, WorldSize.Tiny, checkInvariants: true);
        var world = sim.World;
        var startingArtifacts = world.Artifacts.Count;

        sim.AdvanceDays(200 * 360);

        var target = LifeSystem.NotablesPerSettlement * world.Settlements.Values.Count(s => !s.Abandoned);
        Assert.InRange(world.ActiveCharacters.Count(), target * 0.3, target * 1.5);
        Assert.All(world.Settlements.Values.Where(s => s.Tier >= SettlementTier.Town && !s.Abandoned),
            s => Assert.NotEqual(0, s.LordId));
        Assert.True(world.Characters.Values.Count(c => !c.IsAlive) > 100, "two centuries should see many deaths");
        Assert.InRange(world.Artifacts.Count, startingArtifacts, DeedsSystem.ArtifactCap(world) + startingArtifacts);
        foreach (var type in new[] { "CharacterBorn", "MarriageWed", "ComingOfAge", "SuccessionOccurred", "DuelFought" })
            Assert.Contains(sim.Log.All, e => e.Type == type);
    }

    [Fact]
    public void MediumWorld_GrowsFeuds_WithTraceableCauses()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(80 * 360);

        var duelDeaths = sim.Log.All.Where(e => e.Type == "CharacterDied" && e.Payload["cause"] == "a duel").ToList();
        Assert.NotEmpty(duelDeaths);
        Assert.Contains(sim.Log.All, e => e.Type == "DuelFought" && e.Payload["stakes"] == "to the death");

        var query = new HistoryQuery(sim.Log);
        Assert.Contains(duelDeaths, d => query.WhyChain(d.Id).Causes.Any(duel => duel.Causes.Count > 0));
        Assert.Contains(sim.Log.All, e => e.Type is "ArtifactFound" or "ArtifactInherited" or "ArtifactStolen");
        Assert.InRange(sim.World.ActiveCharacters.Count(), 300, 1200);
    }
}
```

- [x] **Step 2: Run tests to verify they pass against the finished stack**

Run: `dotnet test sim`
Expected: PASS. These tests pin down behaviour built in Tasks 12–15; if one fails, fix the system it names before going on.

- [x] **Step 3: Implement**

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
```

`--size tiny` makes a 64×64 world with 12 regions for quick experiments.
````

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~PeopleHealthTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 318` in total.

- [x] **Step 5: Run the full suite in Release**

Run: `dotnet test sim --configuration Release`
Expected: `Passed!  - Failed: 0, Passed: 318`

- [x] **Step 6: Commit**

```bash
git add sim README.md
git commit -m "test(sim): add people health and feud causality checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §3 notables: identity, house, birth, culture, faith, life stage, attributes, traits, ambition, memories, opinion, relationships, roles, health, holdings, deeds | 1, 4, 6–9, 12 |
| §3 populace promotion | 11 |
| §3 monthly decisions (rules; decision points as `IDecider` briefs) | 5, 7, 9 |
| §3 death and succession, inheritance, memories for those close | 6 |
| §4 artifacts: identity, origin event, 1–3 powers, curse revealed over time, fame, provenance, location; never deleted, only lost | 1, 3, 6, 9, 10 |
| §4 integration: "obtain artifact" ambition, labyrinth/ruin source and sink (delves in dangerous regions), artifacts modify arbitration odds | 5, 9 |
| §5 region culture and faith | 2 |
| §7 `Life` history query; causes always events | 6–12 |
| §8.2–8.3 decision and arbitration contracts, legal outcomes, plausibility floor (LLM implementations in Plan 5) | 5 |
| §10 invariants (dead hold no titles, one location per artifact), determinism, history health | 1, 13 |
| §11 criterion 4: duels end in victory, defeat, injury or death, with consequences in memories, traits and life timelines | 6, 9, 12 |

Deferred: realm-level succession laws and regency, legitimacy effects of crowns (Plan 4); landmarks and labyrinth internals (sub-project 3); LLM decisions, arbitration and invention (Plan 5); prehistory origins and compaction of the dead (Plan 6).
