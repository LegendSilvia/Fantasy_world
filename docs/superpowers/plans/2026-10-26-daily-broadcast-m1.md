# Daily Broadcast — Milestone 1 Implementation Plan (sub-project 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Milestone 1 of the daily broadcast: the world lives in Cloudflare R2, and every morning GitHub Actions catches it up, builds the day's episode (template script) and posts it to Discord, unattended, never twice and never losing a day.

**Architecture:** Episodes are built in the sim (`Chronicle/Episode.cs`, `fw episode`) from a broadcast marker of their own. A new console project, `sim/FantasyWorld.Broadcast`, holds all network calls and secrets: an `IObjectStore` (R2 via SigV4, or a local directory), the morning run (`DailyRun`) that drives the `fw` commands in-process, a manifest of finished steps, dated backups and a Discord webhook channel. A scheduled workflow runs it. The simulation's rules are not changed (one bug fix, Task 1).

**Tech Stack:** .NET 8 / C# 12, xUnit, GitHub Actions. No new packages: R2 is called through `HttpClient` with a hand-written SigV4 signer.

**Spec:** `docs/superpowers/specs/2026-10-26-daily-broadcast-design.md`. **Brief:** `docs/superpowers/specs/2026-10-26-daily-broadcast-brief.md`. **Builds on:** the sim core (Plans 1–6), the chronicle views (sub-project 2) and narration polish (sub-project 5).

## Global Constraints

Everything in the earlier plans' Global Constraints still applies. In addition:

- **Idempotent per broadcast date.** `manifest.json` records each finished step the moment it finishes; a rerun skips it. Nothing is posted twice.
- **One run at a time:** the workflow's `broadcast` concurrency group, never cancelling a run in progress.
- **Back up before writing:** `backups/<date>.fw.db` holds the world as it was before that morning; the last 30 days and the first of each month are kept.
- **Fail safe:** if the simulation or the episode fails, nothing is published and the stored world is left as it was (exit 1). If a channel fails, the episode is still saved and the post is made by a later run (exit 2).
- **The world is stored only after its episode:** the episode, then the world (which holds the marker and the date), then the manifest.
- **Never begin a world on a schedule:** only a run started by hand with `create`.
- **Secrets only from the environment:** never written to R2, the world file, logs or the repository; errors name status codes, not URLs or hosts.
- **No real network calls in tests:** fakes for R2 (a local directory, or a fake `HttpMessageHandler`) and Discord.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Episode key | The broadcast date in Asia/Bangkok, not the world year | A rerun later the same day advances the world and can cross a year; keying by year would publish twice |
| Which day the stored world holds | The world saves `LastBroadcastDate` beside the marker | A run stopped between the world upload and the manifest is recognised exactly, even on a day with no events |
| Missed posts | Made up by the next run, oldest first, for up to 7 days | A failed morning is late, never lost; older ones are not worth flooding the channel with |
| R2 client | Hand-written SigV4 (about 60 lines), tested against AWS's example | Four calls needed; avoids the AWS SDK's newer default checksums, a known trouble spot with S3-compatible stores |
| Running the sim | `fw` commands in-process (`catchup --quiet`, `episode`, `snapshot`) | The same code the owner runs by hand; one process, one exit code |
| `catchup --quiet` | Catches up without narrating the digest or moving `LastSeenEventId` | The owner's reading place must not move for the audience |
| Upload | A `BackupTo` snapshot, never the live WAL database | The world is opened in WAL mode; a snapshot is one consistent file |
| First episode | Tells the latest year and sums up the history behind it | A new world has a century of prehistory |
| Stories | Up to 5; one per person; one of a kind per party | A busy hero, or a fallen realm's many cessions, would fill the episode |
| Budget | 3,600 characters (about 2–4 minutes read aloud), configurable 300–4,000 | The brief's length; a Discord embed holds 4,096 |
| New worlds | Only by hand with `create`; a dry run copies the live world into `staging/` | A wrong secret must make a red run, never a silent new world |
| LLM in M1 | The workflow passes no `ANTHROPIC_API_KEY` yet | M1 is the template script; Sonnet comes in M2, with the guard and fallback |
| Keep-alive | On by default: a monthly heartbeat commit, caught up if a month is missed | GitHub disables the schedule of a public repository after 60 days without activity |
| Live runs | Refused off the default branch | A manual run from a feature branch must not change the live broadcast |
| Sim bug found | A fallen realm fell again for each region it ceded (Task 1) | Triple headlines; fixed with a test, rules unchanged |

**Verification:** each task's xUnit tests fail first and pass after its implementation, and the headless Godot checks still pass (74); the code was replayed task by task in this order. A local end-to-end run (`fw-broadcast run --dry-run --create --local <dir>`) built a medium world and a 1,456-character episode. An independent review of the prototype walked through a crash between every write. It found:
- a post that failed one morning was never made on later days;
- a crash between the world upload and the manifest could only be recovered the same day, and was ambiguous on a quiet day;
- network errors and timeouts escaped the error handling, and could print the R2 host;
- a manual non-dry run from a branch could touch the live broadcast;
- the keep-alive had one chance a month;
- a fallen realm's cessions could fill every story slot.

All are fixed in the code below and covered by tests.

## File Structure

```
sim/FantasyWorld.Sim/  Systems/Statecraft.cs (1); Model/World.cs, Chronicle/Episode.cs, Narration/TemplateNarrator.cs (2)
sim/FantasyWorld.Cli/CliApp.cs (2)
sim/FantasyWorld.Broadcast/  BroadcastApp.cs, BroadcastConfig.cs, DailyRun.cs, Manifest.cs, BackupRetention.cs, Program.cs,
                             Storage/ (IObjectStore, LocalObjectStore, R2ObjectStore, SigV4), Channels/DiscordWebhook.cs (3)
sim/FantasyWorld.Sim.Tests/  Systems/RealmSystemTests.cs (1); Chronicle/EpisodeTests.cs, Cli/EpisodeCliTests.cs (2);
                             Broadcasting/ (Storage, BroadcastParts, DailyRun tests), Client/SessionTests.cs (3)
broadcast.json, sim/FantasyWorld.sln (3)      .github/workflows/broadcast.yml, docs/broadcast-setup.md, README.md (4)
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: A realm falls once

Found while drafting episodes: a realm left with land but no town fell, ceded its land, and each cession re-entered `SecureCapital`, which found no capital and made it fall again: three `RealmFell` events for one fall (and three headlines). `Statecraft.Fall` now marks the realm destroyed before handing its land on, so the cessions below it cannot make it fall again. This is a bug fix, not a rule change: the outcome (the realm gone, its land passed on) is what the rules always meant.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Systems/Statecraft.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Systems/RealmSystemTests.cs`

**Interfaces:**
- Consumes: `Statecraft` (Plan 4).
- Produces: no new API; `Statecraft.Fall` returns early for a destroyed realm.

- [ ] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Systems/RealmSystemTests.cs` entirely with:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 1 failing.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Systems/Statecraft.cs` entirely with:
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

    /// <summary>
    /// A realm with no settlement left falls: land it still holds without one (mountains, wastes) passes to whoever
    /// occupies it, else to the realm holding most of the land around it; its vassals go free, its treaties lapse, its
    /// wars are left to others.
    /// </summary>
    public static void Fall(SimContext ctx, Realm realm, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        // A realm falls once: marked before its land is handed on, so the cessions below do not make it fall again.
        if (realm.Destroyed) return;
        realm.Destroyed = true;
        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id).OrderBy(r => r.Id).ToList())
        {
            var heir = world.Realms.TryGetValue(region.OccupierRealmId, out var occupier) && !occupier.Destroyed && occupier.Id != realm.Id
                ? occupier
                : world.Geography?.Graph.Neighbours(region.Id).Select(n => world.Regions[n].OwnerRealmId)
                    .Where(id => id != 0 && id != realm.Id && !world.Realms[id].Destroyed)
                    .GroupBy(id => id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => world.Realms[g.Key]).FirstOrDefault();
            if (heir is not null) Cede(ctx, region, realm, heir, causes);
        }
        var fell = ctx.Emit(new EventDraft("RealmFell",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: world.Settlements[realm.CapitalId].Ref, Causes: causes,
            Importance: realm.LiegeId == 0 ? 80 : 40, Tags: new[] { "Politics" }));
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
Expected: `Passed!  - Failed: 0, Passed: 10`. Then `dotnet test sim` → `Passed: 587` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "fix(sim): a fallen realm falls once, not again for each region it cedes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The broadcast marker and episodes

Spec §3.1–3.2. The audience's place is `World.LastBroadcastEventId`, separate from the owner's `LastSeenEventId`, kept in the world's markers with the broadcast date it was moved for (`LastBroadcastDate`), so the stored world itself says which day's episode it holds. `Episode.Build` tells the events since the marker: a cold open (the first episode sums up the history behind it; a gap says how many years passed; a quiet spell still makes an episode), the headline (world news first, ranked by the engine's weight), up to five stories (one per person, one of a kind per party), the headline's thread of causes as the story of why, and the chronicler's sign-off, trimmed to a character budget. `fw episode` writes `episode.json` and `script.txt` and moves the marker; `fw catchup --quiet` catches up without narrating or moving the owner's place; `fw snapshot` writes a consistent single-file copy. The headline body keeps a cause line it now has room for.

**Files:**
- Create: `sim/FantasyWorld.Sim/Chronicle/Episode.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Chronicle/EpisodeTests.cs`, `sim/FantasyWorld.Sim.Tests/Cli/EpisodeCliTests.cs`

**Interfaces:**
- Consumes: `TemplateNarrator`, `Chronicler`, `Importance`, `Digest` (sub-projects 2 and 5).
- Produces: `World { LastBroadcastEventId; LastBroadcastDate }`; `record EpisodeStory`; `record Episode { ToJson(); FromJson(json); Build(world, log, since, date, followed?, stories, maxCharacters); Thread(log, outcome, steps) }`; `TemplateNarrator.IsHeadline(type)`, `Body(ev, log, withCause)`; `fw episode <world> --out <dir> [--date] [--stories] [--max-characters] [--keep-marker]`, `fw catchup --quiet`, `fw snapshot <world> <copy>`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Chronicle/EpisodeTests.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class EpisodeTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void TheFirstEpisode_TellsTheLatestYear_AndSumsUpTheHistoryBehindIt()
    {
        var world = Aged.World;
        var episode = Episode.Build(world, Aged.Log, 0, "2026-10-27");
        Assert.StartsWith($"The chronicle of {world.Name} begins in Year {world.Date.Year}", episode.ColdOpen);
        Assert.All(episode.Stories, s => Assert.True(s.Year >= world.Date.Year - 1));
        Assert.True(episode.EventCount < Aged.Log.Count);
        Assert.Equal(Aged.Log.NextId - 1, episode.LastEventId);
        Assert.Equal(0, world.LastBroadcastEventId); // building never moves the marker
        Assert.Equal(episode, Episode.FromJson(episode.ToJson()) with { Stories = episode.Stories });
    }

    [Fact]
    public void AnEpisode_StartsWhereTheAudienceLeftOff_AndTellsAGap()
    {
        var since = Aged.Log.All.Last(e => e.Date.Year == Aged.World.Date.Year - 4).Id;
        var episode = Episode.Build(Aged.World, Aged.Log, since, "2026-10-27");
        Assert.All(episode.Stories, s => Assert.True(s.EventId > since));
        Assert.StartsWith("Four years have passed", episode.ColdOpen);
        Assert.Contains("In Year ", episode.Script);
        Assert.Equal(Aged.Log.All.Count(e => e.Id > since), episode.EventCount);
    }

    [Fact]
    public void AQuietSpell_StillMakesAnEpisode()
    {
        var episode = Episode.Build(Aged.World, Aged.Log, Aged.Log.NextId - 1, "2026-10-27");
        Assert.Null(episode.Headline);
        Assert.Empty(episode.Stories);
        Assert.Contains("quiet", episode.ColdOpen);
        Assert.EndsWith(episode.SignOff, episode.Script);
    }

    [Fact]
    public void WorldNewsLeads_OneStoryAPerson_AndTheScriptFitsItsBudget()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 100;
        for (var i = 0; i < 4; i++)
            log.Append(world.Date, new EventDraft("DistinguishedInBattle", Actors: new Dictionary<string, EntityRef> { ["character"] = hero.Ref },
                Place: world.Regions[1].Ref, Importance: 70));
        var fell = log.Append(world.Date, new EventDraft("RealmFell", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[2].Ref },
            Place: world.Settlements[2].Ref, Importance: 60));
        for (var i = 0; i < 3; i++)
            log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["from"] = world.Realms[2].Ref, ["to"] = world.Realms[1].Ref },
                Place: world.Regions[2].Ref, Importance: 45));
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        Assert.Equal(fell.Id, episode.Headline!.EventId);
        Assert.Equal(new[] { "DistinguishedInBattle", "RegionCeded" }, episode.Stories.Select(s => log.Get(s.EventId).Type));
        Assert.Null(episode.Why); // the fall has no causes in this log

        var tight = Episode.Build(Aged.World, Aged.Log, Aged.Log.All.Last(e => e.Date.Year == Aged.World.Date.Year - 10).Id, "2026-10-27", maxCharacters: 900);
        Assert.True(tight.Script.Length <= 900 || tight.Stories.Count == 0 && tight.Why is null);
    }

    [Fact]
    public void TheWhy_FollowsTheWeightiestThreadOfCauses_OldestFirst()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var drought = log.Append(new WorldDate(0), new EventDraft("Drought", Place: world.Regions[1].Ref));
        var flood = log.Append(new WorldDate(10), new EventDraft("Flood", Place: world.Regions[1].Ref));
        var shortage = log.Append(new WorldDate(400), new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref,
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 0.9), new Cause(flood.Id, CauseKind.Enabler, 0.2) }));
        var revolt = log.Append(new WorldDate(800), new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Causes: new[] { new Cause(shortage.Id, CauseKind.Trigger, 1) }));
        Assert.Equal(new[] { revolt.Id, shortage.Id, drought.Id }, Episode.Thread(log, revolt).Select(e => e.Id));
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        Assert.Equal(revolt.Id, episode.Headline!.EventId);
        Assert.StartsWith("In Year 1, no rain fell on", episode.Why);
        Assert.DoesNotContain("Before it", episode.HeadlineBody);
    }
}
```

`sim/FantasyWorld.Sim.Tests/Cli/EpisodeCliTests.cs`:
```csharp
using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Cli;

public class EpisodeCliTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(DateTimeOffset now, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => now);
        return (code, output.ToString());
    }

    private static long Marker(string path, Func<FantasyWorld.Sim.Model.World, long> read)
    {
        using var store = WorldStore.Open(path);
        return read(store.Load().World);
    }

    [Fact]
    public void FwEpisode_WritesTheEpisode_AndMovesOnlyTheBroadcastMarker()
    {
        using var file = new TempFile();
        using var dir = new TempDir();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(T0, "advance", file.Path, "3y");
        var seen = Marker(file.Path, w => w.LastSeenEventId);

        var (code, output) = Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "2026-10-27");
        Assert.Equal(0, code);
        Assert.Contains("Episode 2026-10-27", output);
        var episode = Episode.FromJson(File.ReadAllText(Path.Combine(dir.Path, "episode.json")));
        Assert.Equal(episode.Script, File.ReadAllText(Path.Combine(dir.Path, "script.txt")));
        Assert.Equal(episode.LastEventId, Marker(file.Path, w => w.LastBroadcastEventId));
        Assert.Equal(seen, Marker(file.Path, w => w.LastSeenEventId));
        using (var store = WorldStore.Open(file.Path)) Assert.Equal("2026-10-27", store.Load().World.LastBroadcastDate);

        Run(T0, "advance", file.Path, "1y");
        Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "2026-10-28", "--keep-marker");
        Assert.Equal(episode.LastEventId, Marker(file.Path, w => w.LastBroadcastEventId));
        using (var store = WorldStore.Open(file.Path)) Assert.Equal("2026-10-27", store.Load().World.LastBroadcastDate);
        Assert.Equal(1, Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "28/10/2026").Code);
    }

    [Fact]
    public void QuietCatchUp_NarratesNothing_AndLeavesTheReadersPlace()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        var (code, output) = Run(T0.AddDays(2), "catchup", file.Path, "--quiet");
        Assert.Equal(0, code);
        Assert.Contains("Advanced 720 days", output);
        Assert.DoesNotContain("The chronicle remembers", output);
        Assert.Equal(0, Marker(file.Path, w => w.LastSeenEventId));
    }

    [Fact]
    public void FwSnapshot_WritesACopyThatOpensAsTheSameWorld()
    {
        using var file = new TempFile();
        using var copy = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(T0, "advance", file.Path, "1y");
        Assert.Equal(0, Run(T0, "snapshot", file.Path, copy.Path).Code);
        using var a = WorldStore.Open(file.Path);
        using var b = WorldStore.Open(copy.Path);
        Assert.Equal(a.Load().World.Date, b.Load().World.Date);
        Assert.Equal(a.Load().Log.Count, b.Load().Log.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'Episode' does not exist in the current context`.

- [ ] **Step 3: Implement**

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;
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
          fw catchup <world-file> [--quiet]         simulate the real time that has passed, then the digest (--quiet: none)
          fw episode <world-file> --out <dir> [--date <yyyy-mm-dd>] [--keep-marker]   the broadcast's episode since it last aired
          fw snapshot <world-file> <copy>           a consistent single-file copy (safe to upload)
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
          fw view <world-file> digest | map [--as-of <year>] | character <id> | family <id> [--generations <n>]
                 | why <event-id> [--depth <n>] | chronicle [--from <year>] [--to <year>] [--category <c>] [--page <n>]
                 | realm <id> | artifact <id>         a screen's data as JSON, for clients
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
          fw eval narration|decisions [--provider anthropic|openrouter] [--model <tier>=<model>]... [--samples <n>]
                 [--draws <n>] [--seeds 1,2,3] [--cap <usd>] [--out <dir>]   live quality evals with a judge; results in evals/
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
                "catchup" => CatchUpCommand(path, rest, output, clock, providers),
                "episode" => EpisodeCommand(path, rest, output, clock),
                "snapshot" => Snapshot(path, rest, output),
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
                "view" => View(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "live" => Live(path, rest, output, clock, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
                "eval" => Eval(path, rest, output, providers),
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

    /// <summary>
    /// Simulates the real time that passed, then tells the digest and marks it seen. With --quiet (the broadcast's
    /// unattended runs) no digest is told: nothing is narrated and the owner's reading place is left alone.
    /// </summary>
    private static int CatchUpCommand(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        var quiet = rest.Contains("--quiet");
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = quiet ? null : TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        if (digest is not null) output.WriteLine(digest);
        return 0;
    }

    /// <summary>
    /// The daily broadcast's episode (sub-project 6): everything since the audience last listened, as episode.json and
    /// script.txt in the --out directory. Moves the broadcast marker (never the owner's reading place) unless --keep-marker.
    /// </summary>
    private static int EpisodeCommand(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock)
    {
        var dir = Option(rest, "--out") ?? throw new FormatException("Use --out <directory>.");
        var date = Option(rest, "--date") ?? clock().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FormatException($"--date must look like 2026-10-27 (got '{date}').");
        using var store = OpenExisting(path);
        var (world, log, queue) = store.Load();
        var episode = Episode.Build(world, log, world.LastBroadcastEventId, date, store.LoadFollowed());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "episode.json"), episode.ToJson());
        File.WriteAllText(Path.Combine(dir, "script.txt"), episode.Script);
        if (!rest.Contains("--keep-marker"))
        {
            world.LastBroadcastEventId = episode.LastEventId;
            world.LastBroadcastDate = date;
            store.Save(world, log, queue);
        }
        output.WriteLine($"Episode {date}: {episode.EventCount} events, Years {episode.FromYear}–{episode.ToYear}, {episode.Script.Length} characters; written to {dir}.");
        return 0;
    }

    /// <summary>A consistent single-file copy of the world (SQLite's online backup), safe to upload while WAL is in use.</summary>
    private static int Snapshot(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        using var store = OpenExisting(path);
        if (File.Exists(rest[0])) File.Delete(rest[0]);
        store.BackupTo(rest[0]);
        output.WriteLine($"Snapshot written to {rest[0]}.");
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

`sim/FantasyWorld.Sim/Chronicle/Episode.cs`:
```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>One story of an episode: the event, its year, its category and how the chronicle tells it.</summary>
public sealed record EpisodeStory(long EventId, int Year, string Category, string Text);

/// <summary>
/// One episode of the daily broadcast (sub-project 6 §3.2): what happened since the audience last listened, as data
/// (the cold open, the headline, the other stories, the headline's chain of causes, the sign-off) and as a script to
/// read aloud. Built from the digest's ranking, so the broadcast and the morning digest agree on what mattered.
/// </summary>
public sealed record Episode(
    string Date, string WorldName, string Chronicler, int FromYear, int ToYear, int EventCount, long FirstEventId, long LastEventId,
    string ColdOpen, EpisodeStory? Headline, string HeadlineBody, IReadOnlyList<EpisodeStory> Stories, string? Why, string SignOff, string Script)
{
    public const int DefaultStories = 5;

    /// <summary>The template script stays under this many characters (about four minutes read aloud).</summary>
    public const int DefaultMaxCharacters = 3600;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [JsonIgnore] public int YearsPassed => Math.Max(0, ToYear - FromYear);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Episode FromJson(string json) => JsonSerializer.Deserialize<Episode>(json, Json)
                                                   ?? throw new JsonException("An episode file was empty.");

    /// <summary>
    /// The episode for <paramref name="date"/> (the broadcast date, e.g. "2026-10-27") from the events after
    /// <paramref name="sinceEventId"/>. It never moves the broadcast marker; the caller does, when it saves the world.
    /// </summary>
    public static Episode Build(World world, EventLog log, long sinceEventId, string date, IReadOnlySet<EntityRef>? followed = null,
        int stories = DefaultStories, int maxCharacters = DefaultMaxCharacters)
    {
        followed ??= new HashSet<EntityRef>();
        var narrator = new TemplateNarrator(world);
        var name = DigestScreen.DisplayName(world);
        var chronicler = Narration.Chronicler.Of(world);
        // The first episode tells the latest year; the history before it is summed up in the cold open.
        var first = sinceEventId == 0;
        var events = log.Since(sinceEventId).Where(e => !first || e.Date.Day > world.Date.Day - WorldDate.DaysPerYear).ToList();
        var fromYear = log.TryGet(sinceEventId, out var last) ? last.Date.Year : events.Count > 0 ? events[0].Date.Year : world.Date.Year;
        var toYear = world.Date.Year;
        var ranked = events.Select(e => (Event: e, Score: Importance.Score(e, world, followed)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Event.Id).Select(x => x.Event).ToList();
        // News that shapes the world leads (a war, a fall, a legend), however famous the people in smaller stories are.
        // The engine's own weight decides among them, so a realm's fall outranks a famous lord's death.
        var top = ranked.Where(e => TemplateNarrator.IsHeadline(e.Type)).OrderByDescending(e => e.Importance).FirstOrDefault() ?? ranked.FirstOrDefault();
        // One story per person, so a busy hero does not fill the episode; and one of a kind per party, so a fallen
        // realm's many cessions are told once.
        var chosen = new List<WorldEvent>();
        var people = new HashSet<EntityRef>(top?.Actors.Values.Where(a => a.Kind == Character.Kind) ?? Array.Empty<EntityRef>());
        foreach (var ev in ranked.Where(e => e != top))
        {
            if (chosen.Count == stories) break;
            var who = ev.Actors.Values.Where(a => a.Kind == Character.Kind).ToList();
            if (who.Any(people.Contains)) continue;
            if (chosen.Prepend(top).Any(c => c is not null && c.Type == ev.Type && c.Actors.Values.Intersect(ev.Actors.Values).Any())) continue;
            people.UnionWith(who);
            chosen.Add(ev);
        }

        EpisodeStory Story(WorldEvent ev) => new(ev.Id, ev.Date.Year, Categories.Label(Categories.Of(ev)), narrator.Sentence(ev));
        var headline = top is null ? null : new EpisodeStory(top.Id, top.Date.Year, Categories.Label(Categories.Of(top)), narrator.Title(top));
        var others = chosen.OrderBy(e => e.Id).Select(Story).ToList();
        // A why needs at least one cause still in the log (old minor events are condensed away).
        IReadOnlyList<WorldEvent> thread = top is null ? [] : Thread(log, top);
        var why = thread.Count > 1 ? narrator.Story(thread) : null;
        // The body leaves the cause to the story of why when there is one, and says nothing if all it has is when.
        var body = top is null ? "" : narrator.Body(top, log, withCause: why is null);
        if (top is not null && body == $"{Digest.SeasonLabel(top.Date)}.") body = "";
        var coldOpen = OpeningFor(name, fromYear, toYear, events.Count, sinceEventId == 0);
        var signOff = SignOffFor(chronicler, name);

        // The template script: drop the least important stories, then the why, until it fits the budget.
        // Within a year the stories need no dates; across years each says when.
        var dated = toYear - fromYear > 1 || chosen.Select(e => e.Date.Year).Distinct().Count() > 1;
        var lines = chosen.ToDictionary(e => e.Id, e => dated ? $"In Year {e.Date.Year}, {narrator.Clause(e)}" : narrator.Sentence(e));
        string Write(IReadOnlyList<EpisodeStory> told, string? because) => Compose(coldOpen, headline, body, told, lines, because, signOff);
        var keep = others.ToList();
        var script = Write(keep, why);
        while (script.Length > maxCharacters && keep.Count > 0)
        {
            var weakest = keep.OrderBy(s => chosen.FindIndex(e => e.Id == s.EventId)).Last();
            keep.Remove(weakest);
            script = Write(keep, why);
        }
        if (script.Length > maxCharacters && why is not null) script = Write(keep, why = null);

        return new Episode(date, name, chronicler.Name, fromYear, toYear, events.Count, events.Count > 0 ? events[0].Id : 0,
            events.Count > 0 ? events[^1].Id : sinceEventId, coldOpen, headline, body, keep, why, signOff, script);
    }

    public const int WhySteps = 5;

    /// <summary>
    /// The headline's strongest thread of causes: the outcome, then up to <paramref name="steps"/> causes behind it, each
    /// step back following the weightiest cause still in the log.
    /// </summary>
    public static IReadOnlyList<WorldEvent> Thread(EventLog log, WorldEvent outcome, int steps = WhySteps)
    {
        var thread = new List<WorldEvent> { outcome };
        var current = outcome;
        while (thread.Count <= steps && current.Causes.OrderByDescending(c => c.Weight).ThenByDescending(c => c.EventId)
                   .Select(c => log.TryGet(c.EventId, out var cause) ? cause : null).FirstOrDefault(c => c is not null && thread.All(t => t.Id != c.Id)) is { } next)
        {
            thread.Add(next);
            current = next;
        }
        return thread;
    }

    private static string OpeningFor(string world, int fromYear, int toYear, int events, bool first)
    {
        if (first) return $"The chronicle of {world} begins in Year {toYear}, with {English.Count(toYear - 1, "year")} of history behind it.";
        if (events == 0) return $"Year {toYear} in {world}, and a quiet one: nothing of note was recorded.";
        var years = toYear - fromYear;
        return years <= 1
            ? $"This is the chronicle of {world}, Year {toYear}."
            : $"{English.Capitalise(English.Count(years, "year"))} have passed in {world} since the chronicle last spoke. It is now Year {toYear}.";
    }

    private static string SignOffFor(Chronicler chronicler, string world) => chronicler.Temperament switch
    {
        "devout" => $"So it was set down by {chronicler.Name}. May the gods keep {world} until we meet again.",
        "wry" => $"{chronicler.Name}, keeper of the chronicle, will be here tomorrow, whether or not {world} behaves.",
        "melancholy" => $"Remember them. This was {chronicler.Name}, for the chronicle of {world}.",
        "proud" => $"Thus records {chronicler.Name}, chronicler of {world}. Until the next dawn.",
        _ => $"That is the year. This was {chronicler.Name}, for the chronicle of {world}.",
    };

    private static string Compose(string coldOpen, EpisodeStory? headline, string body, IReadOnlyList<EpisodeStory> stories,
        IReadOnlyDictionary<long, string> lines, string? why, string signOff)
    {
        var text = new StringBuilder(coldOpen);
        if (headline is not null) text.Append("\n\n").Append(headline.Text).Append('.');
        if (body.Length > 0) text.Append("\n\n").Append(body);
        if (stories.Count > 0)
        {
            text.Append("\n\nElsewhere in the chronicle:");
            foreach (var story in stories) text.Append("\n\n").Append(lines[story.EventId]);
        }
        if (why is not null) text.Append("\n\nHow did it come to this? ").Append(why);
        return text.Append("\n\n").Append(signOff).ToString();
    }
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

    /// <summary>The world's own name (shown on every screen); empty for worlds made before it was named.</summary>
    public string Name { get; set; } = "";
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    public const string BroadcastMarker = "broadcast.lastEventId";

    /// <summary>
    /// Where the daily broadcast's audience left off (sub-project 6): separate from <see cref="LastSeenEventId"/>, so the
    /// owner reading a digest never changes what the audience hears. Kept in <see cref="Markers"/>.
    /// </summary>
    public long LastBroadcastEventId
    {
        get => Markers.GetValueOrDefault(BroadcastMarker);
        set => Markers[BroadcastMarker] = value;
    }

    public const string BroadcastDateMarker = "broadcast.date";

    /// <summary>
    /// The broadcast date (yyyy-MM-dd) of the episode the marker was last moved for, or null before the first. Saved with
    /// the world, so the stored world itself says which day's episode it already holds.
    /// </summary>
    public string? LastBroadcastDate
    {
        get => Markers.TryGetValue(BroadcastDateMarker, out var d) ? $"{d / 10000:D4}-{d / 100 % 100:D2}-{d % 100:D2}" : null;
        set
        {
            if (value is null) Markers.Remove(BroadcastDateMarker);
            else Markers[BroadcastDateMarker] = long.Parse(value.Replace("-", ""), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

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
    public SortedDictionary<long, TradeRoute> Routes { get; init; } = new();
    public SortedDictionary<long, Monster> Monsters { get; init; } = new();

    /// <summary>Region → realm at world creation: the baseline that history replays from (map history slider).</summary>
    public SortedDictionary<long, long> FoundingOwners { get; init; } = new();

    /// <summary>Realm → liege at world creation (0 = independent), the baseline for replaying vassalage.</summary>
    public SortedDictionary<long, long> FoundingLieges { get; init; } = new();

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
        ["ArmyRouted"] = "{army} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, {=a:breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut {=cut}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["WildMagic"] = "Wild magic broke loose at {@place}: {=a:kind} that killed {=dead}.",
        ["MageTrained"] = "{mage} was trained in the mage tower of {@place}.",
        ["MagicLawChanged"] = "{realm} changed its law on magic from {=from} to {=law}.",
        ["MageFled"] = "{mage} fled the law against magic to {@place}.",
        ["WorksDelayed"] = "Work on {structure} stopped.",
        ["WorksCollapsed"] = "{structure} collapsed while it was being built; {=dead} died.",
        ["FortificationBegun"] = "{builder} began {structure} at {@place}.",
        ["FortificationCompleted"] = "{structure} was completed at {@place}.",
        ["RestorationBegun"] = "{builder} began to restore {structure}.",
        ["LabyrinthDelved"] = "{delver} reached level {=level} of {structure}.",
        ["LabyrinthDelveFailed"] = "{delver} was driven back from level {=level} of {structure}.",
        ["LabyrinthCleared"] = "{delver} cleared {structure}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} {=death}, aged {=age}.",
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
        ["PeaceSigned"] = "{war} ended in victory for {victor} over {vanquished}: {=terms}.",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} {=why}, and so began {war}.",
        // The land and the towns.
        ["Drought"] = "Drought parched {@place}.",
        ["DroughtEnded"] = "The rains returned to {@place}.",
        ["Flood"] = "The rivers of {@place} burst their banks.",
        ["Blight"] = "Blight struck the fields of {@place}.",
        ["BlightEnded"] = "The blight passed from {@place}.",
        ["PlagueEnded"] = "The plague left {@place}.",
        ["PlagueSpread"] = "Plague spread from {from} to {@place}.",
        ["LeySurge"] = "The ley lines of {@place} surged.",
        ["DangerRose"] = "{@place} grew dangerous.",
        ["DangerSubsided"] = "{@place} grew safer.",
        ["FertilityExhausted"] = "The soil of {@place} was worked out.",
        ["FertilityRecovered"] = "The soil of {@place} recovered.",
        ["FoodShortageEnded"] = "Food was plentiful again in {@place}.",
        ["Earthquake"] = "An earthquake shook {@place}.",
        ["ResourceDiscovered"] = "{=resource} was found in {@place}.",
        ["MonstersMigrated"] = "Monsters from {from} came down into {@place}.",
        ["ElderChosen"] = "{character} was chosen as an elder of {@place}.",
        ["Exodus"] = "{=people} left {@place} for {to}.",
        ["NotablesFled"] = "{=notables} of {from} fled to {@place}.",
        ["RealmRose"] = "{realm} grew into a {=rank}.",
        ["RealmDiminished"] = "{realm} was reduced to a {=rank}.",
        ["StabilityCollapsed"] = "Order broke down in {realm}.",
        ["StabilityRestored"] = "Order returned to {realm}.",
        ["RebelLeaderRose"] = "{character} rose to lead the rebels of {@place}.",
        ["RevoltEnded"] = "The revolt in {@place} ended.",
        ["SettlementGrew"] = "{@place} grew into a {=tier}.",
        ["SettlementDeclined"] = "{@place} dwindled to a {=tier}.",
        ["BuildingCompleted"] = "{@place} finished its {=building}.",
        ["WallsRaised"] = "{@place} raised its walls higher.",
    };

    /// <summary>
    /// Other ways to tell the most common events (sub-project 5 §3). The phrasing is chosen by the event's id, so a
    /// world always reads the same and neighbouring entries of one kind read differently.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Alternates = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["MarriageWed"] = new[] { "{spouse} married {partner} at {@place}.", "At {@place}, {spouse} took {partner} in marriage." },
        ["CharacterBorn"] = new[] { "At {@place}, {mother} bore {father} a child, {child}." },
        ["CharacterDied"] = new[] { "{deceased} {=death} at the age of {=age}." },
        ["WoundedInBattle"] = new[] { "{character} came away {=wound} from the battle at {@place}." },
        ["ComingOfAge"] = new[] { "{character} came of age with dreams of {=ambition}." },
        ["RivalryBegan"] = new[] { "{a} and {b} fell out over {=over}." },
        ["DistinguishedInBattle"] = new[] { "{character} fought with distinction at {@place}." },
        ["BattleFought"] = new[] { "{victor} beat {vanquished} at {@place} in {war}." },
        ["DuelFought"] = new[] { "At {@place}, {challenger} met {defender} {=stakes}." },
        ["FriendshipFormed"] = new[] { "{a} and {b} grew close.", "A friendship grew between {a} and {b}." },
        ["RegionOccupied"] = new[] { "{@place} fell to {army}." },
        ["ArtifactInherited"] = new[] { "{artifact} passed to {heir}." },
        ["RegionCeded"] = new[] { "{to} gained {@place} from {from}." },
        ["Drought"] = new[] { "No rain fell on {@place}.", "The fields of {@place} withered in a drought." },
        ["Flood"] = new[] { "Floodwaters covered {@place}." },
        ["LeySurge"] = new[] { "Power welled up in the ley lines of {@place}." },
        ["SuccessionOccurred"] = new[] { "At {@place}, {heir} took the seat of {predecessor}." },
        ["PlagueSpread"] = new[] { "The plague reached {@place} from {from}." },
        ["PlagueOutbreak"] = new[] { "Sickness swept through {@place}." },
        ["MonsterRaid"] = new[] { "{monster} attacked {@place}; {=dead} died." },
        ["RegionLiberated"] = new[] { "{@place} was freed by {realm}." },
        ["Knighted"] = new[] { "{knight} was knighted by {liege} to serve {realm}." },
        ["TheftFoiled"] = new[] { "{thief} was caught trying to steal {artifact} from {victim}." },
        ["RegionConverted"] = new[] { "{@place} turned from {old} to {faith}." },
        ["ElderChosen"] = new[] { "{@place} chose {character} as an elder." },
        ["SiegeBegan"] = new[] { "{army} besieged {@place}." },
        ["WarDeclared"] = new[] { "{attacker} went to war against {defender} {=why}: {war}." },
        ["PeaceSigned"] = new[] { "{war} ended; {victor} prevailed over {vanquished}: {=terms}." },
        ["ArtifactStolen"] = new[] { "{artifact} was stolen from {victim} by {thief}." },
        ["Earthquake"] = new[] { "The earth shook in {@place}." },
        ["ResourceDiscovered"] = new[] { "Prospectors found {=resource} in {@place}." },
        ["RouteSevered"] = new[] { "Trade on {route} stopped, cut {=cut}." },
        ["RouteRestored"] = new[] { "Merchants took to {route} again." },
        ["LabyrinthDelveFailed"] = new[] { "{delver} could not get past level {=level} of {structure}." },
        ["MonstersSlain"] = new[] { "{hunter} cleared {@place} of monsters.", "{hunter} hunted down the monsters of {@place}." },
    };

    /// <summary>Short headline titles (present tense, no full stop) for events that lead a digest.</summary>
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WarDeclared"] = "{war} begins",
        ["PeaceSigned"] = "{war} ends",
        ["BattleFought"] = "Battle at {@place}",
        ["RealmFell"] = "{realm} falls",
        ["RealmRose"] = "{realm} rises",
        ["EraBegan"] = "{=era} begins",
        ["IndependenceDeclared"] = "{realm} breaks free",
        ["RegionCeded"] = "{@place} changes hands",
        ["SiegeEnded"] = "The siege of {@place} ends",
        ["SettlementSacked"] = "{@place} is sacked",
        ["SuccessionOccurred"] = "A new lord at {@place}",
        ["CharacterDied"] = "{deceased} is dead",
        ["MonsterRose"] = "A beast rises in {@place}",
        ["MonsterLegendary"] = "{monster} becomes a legend",
        ["MonsterSlain"] = "{monster} is slain",
        ["MonsterRaid"] = "{monster} raids {@place}",
        ["HolyWarProclaimed"] = "A holy war on {b}",
        ["SchismBegan"] = "A schism in {old}",
        ["SaintDeclared"] = "{saint} is made a saint",
        ["RealmConverted"] = "{realm} takes up {faith}",
        ["HolySiteLost"] = "{faith} loses its holy land",
        ["WildMagic"] = "Wild magic at {@place}",
        ["LandmarkCompleted"] = "{structure} stands complete",
        ["WorksCollapsed"] = "{structure} collapses",
        ["LabyrinthOpened"] = "{structure} opens",
        ["LabyrinthCleared"] = "{structure} is cleared",
        ["PlagueOutbreak"] = "Plague in {@place}",
        ["RevoltBegan"] = "Revolt in {@place}",
        ["ArtifactStolen"] = "{artifact} is stolen",
        ["ArtifactForged"] = "{artifact} is forged",
    };

    /// <summary>How a cause is joined to what it caused, by depth (the first layer, then further back).</summary>
    private static readonly string[] FirstConnectives = { "This happened because", "It came about because" };

    private static readonly string[] DeeperConnectives = { "Before that,", "That came about because", "Earlier still," };

    /// <summary>Templates for events whose payload changes how they read (nobody died; a white peace).</summary>
    private static readonly IReadOnlyList<(string Type, Func<WorldEvent, bool> When, string Template)> Variants = new (string, Func<WorldEvent, bool>, string)[]
    {
        ("MonsterRaid", e => e.Payload.GetValueOrDefault("dead") == "0", "{monster} fell on {@place}."),
        ("WildMagic", e => e.Payload.GetValueOrDefault("dead") is null or "0", "Wild magic broke loose at {@place}: {=a:kind}."),
        ("WorksCollapsed", e => e.Payload.GetValueOrDefault("dead") == "0", "{structure} collapsed while it was being built."),
        ("PeaceSigned", e => e.Payload.GetValueOrDefault("outcome") == "white peace", "{war} ended with neither side the victor."),
        ("CharacterDied", e => e.Payload.GetValueOrDefault("age") == "0", "{deceased} {=death} in infancy."),
        ("MonsterHuntFailed", e => e.Actors.ContainsKey("monster"), "{hunter} hunted {monster} in {@place} and was driven back."),
        ("WallsRaised", e => e.Payload.GetValueOrDefault("level") == "1", "{@place} raised walls."),
        ("MonsterHuntFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death" && e.Actors.ContainsKey("monster"), "{hunter} went after {monster} in {@place} and did not come back."),
        ("MonsterHuntFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{hunter} hunted the monsters of {@place} and was killed."),
        ("LabyrinthDelveFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{delver} went down to level {=level} of {structure} and never returned."),
        ("DelveFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{delver} sought {artifact} in {@place} and died there."),
        ("ArtifactInherited", e => !e.Actors.ContainsKey("heir"), "With no heir to claim it, {artifact} went to the treasury of {@place}."),
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    private string? TemplateFor(WorldEvent ev)
    {
        if (Variants.FirstOrDefault(v => v.Type == ev.Type && v.When(ev)).Template is { } variant) return variant;
        if (!Templates.TryGetValue(ev.Type, out var template)) return null;
        if (!Alternates.TryGetValue(ev.Type, out var others)) return template;
        var pick = (int)(ev.Id % (others.Length + 1));
        return pick == 0 ? template : others[pick - 1];
    }

    /// <summary>A name as it reads in a sentence: realms take their article, and "The …" names lower it.</summary>
    private string? Prose(EntityRef entity) => _nameOf(entity) is not { } name ? null
        : entity.Kind == Realm.Kind ? "the " + name : English.InProse(name);

    /// <summary>A payload value in words; some keys stand for phrases (a death, a cause of war, peace terms, a cut road).</summary>
    private string? Value(WorldEvent ev, string key) => key switch
    {
        // Lost in a labyrinth reads "died in the Deeps of Tael", not "was killed by" it.
        "death" => !ev.Payload.TryGetValue("cause", out var cause) ? null
            : world.Structures.Values.Any(s => s.Name == cause) ? $"died in {English.InProse(cause)}" : English.Death(cause),
        "age" => ev.Payload.GetValueOrDefault("age"),
        "stakes" => ev.Payload.GetValueOrDefault("stakes") is { } stakes ? stakes.StartsWith("to ", StringComparison.Ordinal) ? stakes : "to " + stakes : null,
        "ambition" => ev.Payload.GetValueOrDefault("ambition") is { } dream ? English.Ambition(dream) : null,
        "why" => ev.Payload.TryGetValue("casus belli", out var kind) ? English.CasusBelli(kind) : null,
        "terms" => ev.Payload.TryGetValue("terms", out var terms) ? English.Terms(terms) : null,
        "cut" => ev.Payload.GetValueOrDefault("reason") switch
        {
            "war" => "by war",
            "occupation" => "where an enemy held the way",
            "danger" => "by danger on the way",
            "raid" => "by a beast's raids",
            { } other => $"by {other}",
            null => null,
        },
        "over" => ev.Payload.TryGetValue("over", out var over) ? Over(over) : null,
        _ when key.StartsWith("a:", StringComparison.Ordinal) => ev.Payload.TryGetValue(key[2..], out var noun) ? English.WithArticle(noun) : null,
        "people" => ev.Payload.TryGetValue("people", out var many) && long.TryParse(many, out var n) ? n == 1 ? "one person" : $"{English.Number(n)} people" : null,
        "notables" => ev.Payload.TryGetValue("people", out var few) && long.TryParse(few, out var k) ? English.Count(k, "notable") : null,
        "most common" => ev.Payload.TryGetValue("most common", out var common)
            ? English.List(common.Split(", ").Select(part => part.Split(' ', 2) is [var count, var type] ? $"{count} {Briefs.Spaced(type).ToLowerInvariant()}" : part).ToList())
            : null,
        "tier" or "building" or "resource" or "rank" => ev.Payload.TryGetValue(key, out var word) ? word.ToLowerInvariant() : null,
        _ => ev.Payload.TryGetValue(key, out var value) ? English.Value(value) : null,
    };

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = TemplateFor(ev) is { } template && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return English.Capitalise(sentence);
    }

    /// <summary>Whether the event type has a headline title of its own (the kinds of news that lead a broadcast).</summary>
    public static bool IsHeadline(string type) => Titles.ContainsKey(type);

    /// <summary>A short headline title ("The War of the Ash begins"), else the event's sentence without its full stop.</summary>
    public string Title(WorldEvent ev)
    {
        var title = Titles.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Sentence(ev).TrimEnd('.');
        return English.Capitalise(title);
    }

    /// <summary>
    /// What a headline's title leaves out: the event told in full (when the title is short), what led to it, and the
    /// engine's note on why or how — never the title again. Without the cause when a story of why follows.
    /// </summary>
    public string Body(WorldEvent ev, EventLog log, bool withCause = true)
    {
        var parts = new List<string>();
        // The full sentence only when it names someone or somewhere the title leaves out.
        var title = Title(ev);
        var sentence = Sentence(ev);
        var names = ev.Actors.Values.Concat(ev.Place is { } at ? new[] { at } : Array.Empty<EntityRef>()).Select(Prose).OfType<string>();
        // ... or when it tells how (a death's cause).
        if (Titles.ContainsKey(ev.Type) && (ev.Payload.ContainsKey("cause")
                || names.Any(n => sentence.Contains(n, StringComparison.OrdinalIgnoreCase) && !title.Contains(n, StringComparison.OrdinalIgnoreCase))))
            parts.Add(sentence);
        if (withCause && ev.Causes.OrderByDescending(c => c.Weight).ThenBy(c => c.EventId).Select(c => log.TryGet(c.EventId, out var cause) ? cause : null)
                .FirstOrDefault(c => c is not null) is { } first)
        {
            parts.Add($"{(first.Date.Year == ev.Date.Year ? "Earlier that year," : $"Before it, in Year {first.Date.Year},")} {Clause(first)}");
        }
        if ((ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("lore")) is { } note) parts.Add(note);
        return parts.Count == 0 ? $"{FantasyWorld.Sim.Chronicle.Digest.SeasonLabel(ev.Date)}." : string.Join(" ", parts);
    }

    /// <summary>
    /// The story of why (sub-project 5 §3): a chain told as one paragraph, oldest cause first, ending with the outcome.
    /// </summary>
    public string Story(IReadOnlyList<WorldEvent> chain)
    {
        var ordered = chain.DistinctBy(e => e.Id).OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        if (ordered.Count == 0) return "";
        var text = new StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            var ev = ordered[i];
            var lead = i == 0 ? $"In Year {ev.Date.Year}," : i == ordered.Count - 1 ? (ev.Date.Year == ordered[i - 1].Date.Year ? "And so" : $"And so, in Year {ev.Date.Year},")
                : ev.Date.Year == ordered[i - 1].Date.Year ? "That same year," : $"In Year {ev.Date.Year},";
            if (text.Length > 0) text.Append(' ');
            text.Append(lead).Append(' ').Append(Clause(ev));
        }
        return text.ToString();
    }

    /// <summary>The event as a clause to follow "In Year 12," or "And so": names keep their case, other openings lower theirs.</summary>
    public string Clause(WorldEvent ev) =>
        TemplateFor(ev) is { } template && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));

    public string Narrate(WorldEvent ev)
    {
        var text = new StringBuilder($"In Year {ev.Date.Year}, {Clause(ev)}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true, new HashSet<long> { chain.Event.Id });
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {English.Count(digest.EventCount, "event")} {(digest.EventCount == 1 ? "was" : "were")} recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    /// <summary>Each cause once, joined by a connective that varies with depth and event, so a chain does not repeat itself.</summary>
    private void Causes(StringBuilder text, ChainNode node, bool first, HashSet<long> told)
    {
        var second = false;
        foreach (var cause in node.Causes)
        {
            if (!told.Add(cause.Event.Id)) continue;
            // A second cause of the same event is joined as such; otherwise the connective varies with depth and event.
            var choices = first ? FirstConnectives : DeeperConnectives;
            var connective = second ? "And also because" : choices[(int)(cause.Event.Id % choices.Length)];
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}"); // "In Year …" becomes "in Year …"
            second = true;
            Causes(text, cause, first: false, told);
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
                "@place" => ev.Place is { } place ? Prose(place) : null,
                _ when key.StartsWith('=') => Value(ev, key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? Prose(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => Prose(a.Value)).OfType<string>().ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {English.List(who)}";
        if (ev.Place is { } place && Prose(place) is { } where) text += $" at {where}";
        return English.Capitalise(text) + ".";
    }

    /// <summary>What a rivalry is over: "calm vs wrathful" reads as a clash of natures.</summary>
    private static string Over(string over) =>
        over.Split(" vs ") is [var mine, var theirs] ? $"the clash of {English.WithArticle(mine)} nature with {English.WithArticle(theirs)} one" : English.InProse(over);

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~EpisodeCliTests|FullyQualifiedName~EpisodeTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 595` in total.

- [ ] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(chronicle): build daily episodes from a broadcast marker of their own

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The broadcast

Spec §3. A new console project, `FantasyWorld.Broadcast` (`fw-broadcast run`), holds every network call and secret. Storage is an `IObjectStore`: Cloudflare R2 through its S3 API with a hand-written SigV4 signer (checked against AWS's published example), or a local directory. The morning run pulls the world, backs it up once a day, runs `fw catchup --quiet`, `fw episode` and `fw snapshot` in-process, stores the episode and then the world, and marks each step in `manifest.json` the moment it finishes. It then posts every episode of the last week not yet posted, oldest first, to Discord (one embed that can ping nobody). A rerun repeats nothing; a run stopped anywhere is picked up; a failed simulation publishes nothing and leaves the stored world as it was; a scheduled run never begins a world; a dry run works under `staging/` and only reads the live world. Transport errors and timeouts name no host or secret. One older test is made deterministic (it cancelled through an asynchronous `Progress<T>`, which the busier suite could outrun).

**Files:**
- Create: `broadcast.json`, `sim/FantasyWorld.Broadcast/BackupRetention.cs`, `sim/FantasyWorld.Broadcast/BroadcastApp.cs`, `sim/FantasyWorld.Broadcast/BroadcastConfig.cs`, `sim/FantasyWorld.Broadcast/Channels/DiscordWebhook.cs`, `sim/FantasyWorld.Broadcast/DailyRun.cs`, `sim/FantasyWorld.Broadcast/FantasyWorld.Broadcast.csproj`, `sim/FantasyWorld.Broadcast/Manifest.cs`, `sim/FantasyWorld.Broadcast/Program.cs`, `sim/FantasyWorld.Broadcast/Storage/IObjectStore.cs`, `sim/FantasyWorld.Broadcast/Storage/LocalObjectStore.cs`, `sim/FantasyWorld.Broadcast/Storage/R2ObjectStore.cs`, `sim/FantasyWorld.Broadcast/Storage/SigV4.cs`
- Modify: `sim/FantasyWorld.Cli/CliApp.cs`, `sim/FantasyWorld.sln`
- Test (create): `sim/FantasyWorld.Sim.Tests/Broadcasting/BroadcastPartsTests.cs`, `sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs`, `sim/FantasyWorld.Sim.Tests/Broadcasting/StorageTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Client/SessionTests.cs`, `sim/FantasyWorld.Sim.Tests/FantasyWorld.Sim.Tests.csproj`

**Interfaces:**
- Consumes: Task 2; `CliApp.Run`, `WorldStore` (sim core).
- Produces: `interface IObjectStore`, `PrefixedStore`, `LocalObjectStore`, `R2ObjectStore { FromEnvironment(http, env) }`, `static class SigV4`; `record BroadcastConfig { Load; Parse; DateAt(now) }`; `Manifest`, `ManifestDay`; `static class BackupRetention { KeyFor; ToDelete }`; `interface IEpisodeChannel`, `ChannelException`, `DiscordWebhook { Payload }`; `record RunOptions(DryRun, Create)`; `DailyRun { Ok, Failed, Partial; MissedDays; RunAsync() }`; `BroadcastApp.RunAsync(args, output, clock, env, handler?)`; `broadcast.json`.

- [ ] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Broadcasting/BroadcastPartsTests.cs`:
```csharp
using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class BroadcastPartsTests
{
    internal static Episode SampleEpisode(string script = "The chronicle of Aerenmoor, Year 12.") =>
        new("2026-10-27", "Aerenmoor", "Ilsa the Grey", 11, 12, 40, 100, 140, "The chronicle of Aerenmoor, Year 12.",
            null, "", [], null, "So it is told.", script);

    [Fact]
    public void Retention_KeepsTheLastDays_AndTheFirstOfEachMonth()
    {
        var keys = Enumerable.Range(0, 75).Select(d => BackupRetention.KeyFor(new DateOnly(2026, 8, 20).AddDays(d).ToString("yyyy-MM-dd")))
            .Append("backups/notes.txt").Append("world.fw.db").ToList();
        var deleted = BackupRetention.ToDelete(keys, "2026-11-02", keepDays: 30);
        var kept = keys.Except(deleted).ToList();
        Assert.Contains("backups/2026-08-20.fw.db", kept); // the first August backup
        Assert.Contains("backups/2026-09-01.fw.db", kept);
        Assert.Contains("backups/2026-10-01.fw.db", kept);
        Assert.Contains("backups/2026-10-04.fw.db", kept); // 30 days ending 2026-11-02
        Assert.Contains("backups/2026-10-03.fw.db", deleted);
        Assert.Contains("backups/notes.txt", kept);
        Assert.Equal(75 - 30 - 3, deleted.Count);
    }

    [Fact]
    public void Manifest_RoundTrips_AndMarksStepsPerDate()
    {
        var manifest = new Manifest();
        manifest.Record("2026-10-27", SampleEpisode());
        manifest.Day("2026-10-27").Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch;
        var back = Manifest.FromBytes(manifest.ToBytes());
        Assert.True(back.Day("2026-10-27").Done(Manifest.EpisodeStep));
        Assert.False(back.Day("2026-10-27").Done(Manifest.DiscordStep));
        Assert.Equal(12, back.Day("2026-10-27").ToYear);
        Assert.False(back.Day("2026-10-28").Done(Manifest.EpisodeStep));
        Assert.Empty(Manifest.FromBytes(null).Episodes);
    }

    [Fact]
    public void Config_HasSafeDefaults_AndRefusesWhatItCannotDo()
    {
        var config = BroadcastConfig.Parse("{}");
        Assert.Equal("2026-10-27", config.DateAt(new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-10-26", config.DateAt(new DateTimeOffset(2026, 10, 26, 16, 59, 0, TimeSpan.Zero)));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "language": "th" }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "maxCharacters": 5000 }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "livePrefix": "x/", "stagingPrefix": "x/" }"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => BroadcastConfig.Parse("""{ "voiceIdd": "x" }"""));
    }

    [Fact]
    public void TheCheckedInConfig_IsValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "broadcast.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var config = BroadcastConfig.Load(Path.Combine(dir!.FullName, "broadcast.json"));
        Assert.Equal("Asia/Bangkok", config.TimeZone);
    }

    [Fact]
    public async Task Discord_PostsOneEmbed_ThatCanPingNobody()
    {
        var payload = DiscordWebhook.Payload(SampleEpisode(new string('x', 5000)), "The Chronicle");
        var embed = payload["embeds"]![0]!;
        Assert.Equal("Aerenmoor, Years 11–12", (string)embed["title"]!);
        Assert.Equal(DiscordWebhook.MaxDescription, ((string)embed["description"]!).Length);
        Assert.Empty(payload["allowed_mentions"]!["parse"]!.AsArray());

        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"), (HttpStatusCode.TooManyRequests, ""));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "The Chronicle");
        await discord.PublishAsync(SampleEpisode());
        Assert.Equal("https://discord.test/api/webhooks/1/SECRET-TOKEN?wait=true", http.Seen[0].Request.RequestUri!.ToString());
        Assert.Equal("The Chronicle", (string)JsonNode.Parse(http.Seen[0].Body)!["username"]!);
        var error = await Assert.ThrowsAsync<ChannelException>(() => discord.PublishAsync(SampleEpisode()));
        Assert.Equal("Discord answered HTTP 429.", error.Message);

        var slow = new DiscordWebhook(new HttpClient(new ThrowingHttp()), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "x");
        var late = await Assert.ThrowsAsync<ChannelException>(() => slow.PublishAsync(SampleEpisode()));
        Assert.DoesNotContain("SECRET", late.Message);
    }

    private sealed class ThrowingHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new TaskCanceledException();
    }
}
```

`sim/FantasyWorld.Sim.Tests/Broadcasting/DailyRunTests.cs`:
```csharp
using System.Net;
using System.Text;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class DailyRunTests : IDisposable
{
    /// <summary>05:30 in Bangkok on 2026-10-27.</summary>
    private static readonly DateTimeOffset Morning = new(2026, 10, 26, 22, 30, 0, TimeSpan.Zero);

    private static readonly BroadcastConfig Config = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny" }""");

    private sealed class CountingChannel(bool fails = false) : IEpisodeChannel
    {
        public List<Episode> Posts { get; } = [];
        public bool Fails { get; set; } = fails;
        public string Name => "discord";

        public Task PublishAsync(Episode episode, CancellationToken ct = default)
        {
            if (Fails) throw new ChannelException("Discord answered HTTP 500.");
            Posts.Add(episode);
            return Task.CompletedTask;
        }
    }

    private readonly TempDir _dir = new();
    private readonly LocalObjectStore _root;
    private readonly CountingChannel _discord = new();
    private readonly StringWriter _log = new();

    public DailyRunTests() => _root = new LocalObjectStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private Task<int> Run(DateTimeOffset now, RunOptions? options = null, IEpisodeChannel? channel = null) =>
        new DailyRun(Config, _root, [channel ?? _discord], () => now, _log, options).RunAsync();

    private async Task<byte[]?> Get(string key) => await _root.GetAsync(key);

    private async Task<Episode> StoredEpisode(string prefix, string date) =>
        Episode.FromJson(Encoding.UTF8.GetString((await Get(prefix + DailyRun.EpisodeKey(date)))!));

    [Fact]
    public async Task AScheduledRun_NeverBeginsAWorld()
    {
        Assert.Equal(DailyRun.Failed, await Run(Morning));
        Assert.Contains("There is no world at live/world.fw.db", _log.ToString());
        Assert.Empty(await _root.ListAsync(""));
        Assert.Empty(_discord.Posts);
    }

    [Fact]
    public async Task ACreateRun_BeginsTheWorld_AndARerunTheSameDayPostsNothing()
    {
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(Create: true)));
        Assert.Single(_discord.Posts);
        Assert.Equal("2026-10-27", _discord.Posts[0].Date);
        Assert.Equal(_discord.Posts[0].Script, Encoding.UTF8.GetString((await Get("live/" + DailyRun.ScriptKey("2026-10-27")))!));
        var world = await Get("live/world.fw.db");

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(3)));
        Assert.Single(_discord.Posts);
        Assert.Equal(world, await Get("live/world.fw.db"));
        Assert.Contains("already posted today", _log.ToString());
    }

    [Fact]
    public async Task TheNextMorning_BacksUpFirst_LivesAYear_AndTellsOnlyWhatIsNew()
    {
        await Run(Morning, new RunOptions(Create: true));
        var before = await Get("live/world.fw.db");

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1)));
        Assert.Equal(before, await Get("live/backups/2026-10-28.fw.db"));
        Assert.Equal(2, _discord.Posts.Count);
        var (first, second) = (_discord.Posts[0], _discord.Posts[1]);
        Assert.Equal("2026-10-28", second.Date);
        Assert.Equal(first.ToYear + 1, second.ToYear);
        Assert.True(second.FirstEventId > first.LastEventId);
        var manifest = Manifest.FromBytes(await Get("live/" + Manifest.Key));
        Assert.True(manifest.Day("2026-10-28").Done(Manifest.DiscordStep));
        Assert.Equal(second.EventCount, manifest.Day("2026-10-28").Events);
    }

    [Fact]
    public async Task AFailedPost_KeepsTheEpisode_AndIsRetriedOnceByTheNextRun()
    {
        _discord.Fails = true;
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true)));
        var world = await Get("live/world.fw.db");
        Assert.NotNull(world);

        _discord.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(1)));
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddHours(2)));
        Assert.Single(_discord.Posts);
        Assert.Equal(world, await Get("live/world.fw.db"));
    }

    [Fact]
    public async Task ABrokenWorld_PublishesNothing_AndIsLeftAsItWas()
    {
        var garbage = Encoding.UTF8.GetBytes("not a world");
        await _root.PutAsync("live/world.fw.db", garbage, "application/octet-stream");
        Assert.Equal(DailyRun.Failed, await Run(Morning));
        Assert.Equal(garbage, await Get("live/world.fw.db"));
        Assert.Null(await Get("live/" + DailyRun.EpisodeKey("2026-10-27")));
        Assert.Empty(_discord.Posts);
        Assert.Contains("Nothing was published", _log.ToString());
    }

    [Fact]
    public async Task AMissedPost_IsMadeTheNextMorning_BeforeThatMorningsOwn()
    {
        _discord.Fails = true;
        Assert.Equal(DailyRun.Partial, await Run(Morning, new RunOptions(Create: true)));
        _discord.Fails = false;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
        Assert.Contains("posted the missed episode of 2026-10-27", _log.ToString());
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(2, _discord.Posts.Count);
    }

    [Fact]
    public async Task ARunStoppedAfterSavingTheWorld_IsPickedUp_AndNothingIsToldTwiceOrLost()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        flaky.Failing = Manifest.Key;
        Assert.Equal(DailyRun.Failed, await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync());
        Assert.Contains("The world and the episode were saved, but not the manifest", _log.ToString());
        var world = await Get("live/world.fw.db");

        // The same morning: the stored world already holds the day's episode, so it is posted and the world stays.
        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(world, await Get("live/world.fw.db"));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
    }

    [Fact]
    public async Task ARunStoppedAfterSavingTheWorld_IsPostedTheNextMorning()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        flaky.Failing = Manifest.Key;
        await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync();
        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(2)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28", "2026-10-29" }, _discord.Posts.Select(p => p.Date));
        Assert.True(_discord.Posts[2].FirstEventId > _discord.Posts[1].LastEventId);
    }

    [Fact]
    public async Task ARunStoppedBeforeSavingTheWorld_LeavesItAsItWas_AndTheDayIsBuiltAgain()
    {
        var flaky = new FlakyStore(_root);
        await new DailyRun(Config, flaky, [_discord], () => Morning, _log, new RunOptions(Create: true)).RunAsync();
        var world = await Get("live/world.fw.db");
        flaky.Failing = "world.fw.db";
        Assert.Equal(DailyRun.Failed, await new DailyRun(Config, flaky, [_discord], () => Morning.AddDays(1), _log).RunAsync());
        Assert.Contains("the stored world is as it was", _log.ToString());
        Assert.Equal(world, await Get("live/world.fw.db"));

        flaky.Failing = null;
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1).AddHours(1)));
        Assert.Equal(new[] { "2026-10-27", "2026-10-28" }, _discord.Posts.Select(p => p.Date));
        Assert.True(_discord.Posts[1].EventCount > 0);
    }

    /// <summary>A store whose writes to one key fail, as if the run stopped there.</summary>
    private sealed class FlakyStore(IObjectStore inner) : IObjectStore
    {
        public string? Failing { get; set; }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);

        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default) =>
            Failing is not null && key.EndsWith("/" + Failing, StringComparison.Ordinal)
                ? throw new IOException("R2 did not answer in time.")
                : inner.PutAsync(key, content, contentType, ct);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);

        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
    }

    [Fact]
    public async Task ADryRun_WorksOnACopyUnderStaging_AndLeavesTheLiveBroadcastAlone()
    {
        await Run(Morning, new RunOptions(Create: true));
        var liveWorld = await Get("live/world.fw.db");
        var liveManifest = await Get("live/" + Manifest.Key);
        var test = new CountingChannel();

        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), new RunOptions(DryRun: true), test));
        Assert.Single(test.Posts);
        Assert.Single(_discord.Posts);
        Assert.Equal(liveWorld, await Get("live/world.fw.db"));
        Assert.Equal(liveManifest, await Get("live/" + Manifest.Key));
        Assert.Null(await Get("live/backups/2026-10-28.fw.db"));
        Assert.NotNull(await Get("staging/world.fw.db"));
        var stored = await StoredEpisode("staging/", "2026-10-28");
        Assert.Equal(DailyRun.Ok, await Run(Morning.AddDays(1), new RunOptions(DryRun: true, Create: true), test));
        Assert.Equal((test.Posts[0].LastEventId, test.Posts[0].Script), (stored.LastEventId, stored.Script));
    }

    [Fact]
    public async Task ADryRunThatCreates_WritesNothingUnderLive()
    {
        Assert.Equal(DailyRun.Ok, await Run(Morning, new RunOptions(DryRun: true, Create: true)));
        Assert.Empty(await _root.ListAsync("live/"));
        Assert.NotEmpty(await _root.ListAsync("staging/"));
    }

    [Fact]
    public async Task TheApp_TakesSecretsOnlyFromTheEnvironment_AndNeverPrintsThem()
    {
        using var config = new TempFile();
        await File.WriteAllTextAsync(config.Path, """{ "seed": 3, "size": "tiny" }""");
        var env = new Dictionary<string, string> { ["DISCORD_TEST_WEBHOOK_URL"] = "https://discord.test/api/webhooks/9/SECRET-TOKEN" };
        var output = new StringWriter();

        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["run", "--config", config.Path], output, () => Morning, env.GetValueOrDefault));
        Assert.Contains("R2 is not configured", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["run", "--config", config.Path, "--local", _dir.Path], output, () => Morning, env.GetValueOrDefault));
        Assert.Contains("DISCORD_WEBHOOK_URL is not set", output.ToString());

        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var code = await BroadcastApp.RunAsync(["run", "--dry-run", "--create", "--config", config.Path, "--local", _dir.Path],
            output, () => Morning, env.GetValueOrDefault, http);
        Assert.Equal(DailyRun.Ok, code);
        Assert.Single(http.Seen);
        Assert.DoesNotContain("SECRET-TOKEN", output.ToString());
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["publish"], output, () => Morning, env.GetValueOrDefault));
    }
}
```

`sim/FantasyWorld.Sim.Tests/Broadcasting/StorageTests.cs`:
```csharp
using System.Net;
using System.Text;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class StorageTests
{
    /// <summary>Answers each request from a list of canned responses and keeps every request it saw.</summary>
    internal sealed class FakeHttp(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpRequestMessage Request, byte[]? Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct)));
            var (status, body) = answers[Math.Min(_next++, answers.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public void SigV4_MatchesAwsPublishedExample()
    {
        // "GET Object" from AWS's Signature Version 4 examples for S3.
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = SigV4.Hash([]),
            ["x-amz-date"] = "20130524T000000Z",
        };
        var auth = SigV4.Authorization("GET", "/test.txt", [], headers, SigV4.Hash([]),
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero), "us-east-1", "s3",
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        Assert.Equal("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, " +
                     "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, " +
                     "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", auth);
        Assert.Equal("a%20b%2Fc~d", SigV4.Encode("a b/c~d"));
        Assert.Equal("live/backups/2026-10-27.fw.db", SigV4.Encode("live/backups/2026-10-27.fw.db", keepSlashes: true));
    }

    [Fact]
    public async Task R2_SignsPathStyleRequests_AndReadsMissingAsNull()
    {
        var http = new FakeHttp((HttpStatusCode.OK, ""), (HttpStatusCode.NotFound, "<Error/>"), (HttpStatusCode.Forbidden, ""));
        var store = new R2ObjectStore(new HttpClient(http), "acct", "fw", "KEYID", "SECRET",
            () => new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero));
        await store.PutAsync("live/world.fw.db", [1, 2, 3], "application/octet-stream");
        Assert.Null(await store.GetAsync("live/missing.json"));
        var error = await Assert.ThrowsAsync<IOException>(() => store.GetAsync("live/world.fw.db"));

        var (put, body) = http.Seen[0];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://acct.r2.cloudflarestorage.com/fw/live/world.fw.db", put.RequestUri!.ToString());
        Assert.Equal([1, 2, 3], body);
        Assert.Equal(SigV4.Hash([1, 2, 3]), put.Headers.GetValues("x-amz-content-sha256").Single());
        var auth = put.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=KEYID/20261026/auto/s3/aws4_request, SignedHeaders=host;x-amz-content-sha256;x-amz-date,", auth);
        Assert.DoesNotContain("SECRET", auth);
        Assert.Contains("HTTP 403", error.Message);
        Assert.DoesNotContain("SECRET", error.Message);
    }

    private sealed class Unreachable(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw error;
    }

    [Fact]
    public async Task R2_TurnsTransportFailuresIntoIOErrors_ThatNameNoHost()
    {
        foreach (var error in new Exception[] { new HttpRequestException(HttpRequestError.NameResolutionError, "acct.r2.cloudflarestorage.com:443"), new TaskCanceledException() })
        {
            var store = new R2ObjectStore(new HttpClient(new Unreachable(error)), "acct", "fw", "KEYID", "SECRET");
            var failure = await Assert.ThrowsAsync<IOException>(() => store.GetAsync("live/world.fw.db"));
            Assert.DoesNotContain("acct", failure.Message);
        }
    }

    [Fact]
    public async Task R2_ListsEveryPage()
    {
        const string ns = "http://s3.amazonaws.com/doc/2006-03-01/";
        var http = new FakeHttp(
            (HttpStatusCode.OK, $"<ListBucketResult xmlns=\"{ns}\"><IsTruncated>true</IsTruncated><NextContinuationToken>t/1=</NextContinuationToken>" +
                                "<Contents><Key>live/backups/b</Key></Contents><Contents><Key>live/backups/a</Key></Contents></ListBucketResult>"),
            (HttpStatusCode.OK, $"<ListBucketResult xmlns=\"{ns}\"><IsTruncated>false</IsTruncated><Contents><Key>live/backups/c</Key></Contents></ListBucketResult>"));
        var store = new R2ObjectStore(new HttpClient(http), "acct", "fw", "KEYID", "SECRET");
        Assert.Equal(["live/backups/a", "live/backups/b", "live/backups/c"], await store.ListAsync("live/backups/"));
        Assert.Equal("?list-type=2&prefix=live%2Fbackups%2F", http.Seen[0].Request.RequestUri!.Query);
        Assert.Equal("?list-type=2&prefix=live%2Fbackups%2F&continuation-token=t%2F1%3D", http.Seen[1].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task LocalAndPrefixedStores_KeepKeysApart()
    {
        using var dir = new TempDir();
        var root = new LocalObjectStore(dir.Path);
        var live = new PrefixedStore(root, "live/");
        var staging = new PrefixedStore(root, "staging/");
        await live.PutAsync("backups/2026-10-27.fw.db", Encoding.UTF8.GetBytes("a"), "application/octet-stream");
        await staging.PutAsync("world.fw.db", Encoding.UTF8.GetBytes("b"), "application/octet-stream");
        Assert.Equal(["backups/2026-10-27.fw.db"], await live.ListAsync(""));
        Assert.Equal(["live/backups/2026-10-27.fw.db", "staging/world.fw.db"], await root.ListAsync(""));
        Assert.Null(await live.GetAsync("world.fw.db"));
        await live.DeleteAsync("backups/2026-10-27.fw.db");
        Assert.Empty(await live.ListAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => root.GetAsync("../outside"));
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Client/SessionTests.cs` entirely with:
```csharp
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class SessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = T0;
    }

    [Fact]
    public async Task ASession_CatchesUpOnAWorker_ThenLivesAndSaves()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (var created = Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }

        clock.Now = T0.AddHours(8);
        using (var session = Session.Open(file.Path, () => clock.Now))
        {
            Assert.Equal(120, session.DaysBehind);
            var months = new List<WorldDate>();
            var result = await session.CatchUpAsync(new Progress<WorldDate>(months.Add));
            Assert.Equal(120, result.DaysAdvanced);
            Assert.Equal(0, session.DaysBehind);
            Assert.False(session.Busy);

            clock.Now = clock.Now.AddMinutes(4);
            Assert.Equal(1, session.Step().Days);
            Assert.Equal(0, session.Step().Days);
            Assert.Equal(clock.Now.AddMinutes(4), session.NextDayAt);
            Assert.NotNull(session.TryRead((w, _) => w.Name));
        }
        Assert.True(File.Exists(Backups.PathOf(file.Path, 1)));
        using var reopened = WorldStore.Open(file.Path);
        Assert.Equal(121, reopened.Load().World.Date.Day);
    }

    [Fact]
    public void TheDigest_IsReadOnce_AndFollowingLeadsIt()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using var session = Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now, prehistoryYears: 5);

        var digest = session.Digest();
        Assert.NotNull(digest.Headline);
        Assert.Equal("Caught up · Year 6, day 1", digest.Status);
        session.MarkSeen();
        Assert.Null(session.Digest().Headline);

        var lord = session.Read((w, _) => w.Settlements.Values.First(s => s.LordId != 0).LordId);
        session.Follow(new EntityRef("char", lord));
        Assert.True(session.Character(lord).Followed);
        Assert.NotEmpty(session.Map().Settlements);
        Assert.NotEmpty(session.Map(year: 2).Regions);
    }

    [Fact]
    public void OpeningAMissingWorld_IsAClearError()
    {
        Assert.Throws<FileNotFoundException>(() => Session.Open("/nowhere/none.fw.db", () => T0));
        using var file = new TempFile();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => T0)) { }
        Assert.Throws<InvalidOperationException>(() => Session.Create(file.Path, 3, WorldSize.Tiny, () => T0));
    }

    [Fact]
    public async Task ACancelledCatchUp_KeepsTheWorldClockInStepWithTheDaysSimulated()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }
        clock.Now = T0.AddDays(20); // 20 real days: 20 world years owed
        using var cts = new CancellationTokenSource();
        using (var session = Session.Open(file.Path, () => clock.Now))
        {
            var owed = session.DaysBehind;
            var before = session.Read((w, _) => w.Date.Day);
            var progress = new ListProgress<WorldDate>(_ => cts.Cancel()); // synchronous: the cancel lands before the next day
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CatchUpAsync(progress, cts.Token));
            var simulated = session.Read((w, _) => w.Date.Day) - before;
            Assert.InRange(simulated, 1, owed - 1);
            Assert.Equal(owed - simulated, session.DaysBehind);
        }
        using var reopened = Session.Open(file.Path, () => clock.Now);
        Assert.True(reopened.DaysBehind > 0);
    }

    [Fact]
    public void AWorldThatFailsToLoad_DoesNotPushOutTheBackups()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }
        using (Session.Open(file.Path, () => clock.Now)) { }
        var backup = Backups.PathOf(file.Path, 1);
        Assert.True(File.Exists(backup));
        var good = File.ReadAllBytes(backup);
        File.WriteAllText(file.Path, "not a world");
        Assert.ThrowsAny<Exception>(() => Session.Open(file.Path, () => clock.Now));
        Assert.Equal(good, File.ReadAllBytes(backup));
        Assert.False(File.Exists(Backups.PathOf(file.Path, 2)));
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/FantasyWorld.Sim.Tests.csproj` entirely with:
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FantasyWorld.Sim\FantasyWorld.Sim.csproj" />
    <ProjectReference Include="..\FantasyWorld.Cli\FantasyWorld.Cli.csproj" />
    <ProjectReference Include="..\FantasyWorld.Llm\FantasyWorld.Llm.csproj" />
    <ProjectReference Include="..\FantasyWorld.Client\FantasyWorld.Client.csproj" />
    <ProjectReference Include="..\FantasyWorld.Broadcast\FantasyWorld.Broadcast.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Broadcast' does not exist in the namespace 'FantasyWorld' (are you missing an assembly reference?)`.

- [ ] **Step 3: Implement**

`broadcast.json`:
```json
{
  // The daily broadcast's settings (sub-project 6). Secrets never go here: they are GitHub Actions secrets.
  "worldKey": "world.fw.db",
  "seed": 20261027,
  "size": "medium",
  "timeZone": "Asia/Bangkok",
  "language": "en",
  "livePrefix": "live/",
  "stagingPrefix": "staging/",
  "backupDays": 30,
  "stories": 5,
  "maxCharacters": 3600,
  "discordName": "The Chronicle",
  "keepAlive": true
}
```

`sim/FantasyWorld.Broadcast/BackupRetention.cs`:
```csharp
using System.Globalization;

namespace FantasyWorld.Broadcast;

/// <summary>Dated world backups (<c>backups/2026-10-27.fw.db</c>): the last N days, and the first of every month.</summary>
public static class BackupRetention
{
    public const string Prefix = "backups/";
    public const string Suffix = ".fw.db";

    public static string KeyFor(string date) => $"{Prefix}{date}{Suffix}";

    /// <summary>The backup keys to delete on <paramref name="today"/>. Keys that are not dated backups are never touched.</summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> keys, string today, int keepDays)
    {
        var cutoff = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1 - keepDays);
        var dated = keys
            .Select(k => (Key: k, Date: DateOf(k)))
            .Where(b => b.Date is not null)
            .Select(b => (b.Key, Date: b.Date!.Value))
            .OrderBy(b => b.Date)
            .ToList();
        var firstOfMonth = dated.GroupBy(b => (b.Date.Year, b.Date.Month)).Select(g => g.First().Key).ToHashSet();
        return dated.Where(b => b.Date < cutoff && !firstOfMonth.Contains(b.Key)).Select(b => b.Key).ToList();
    }

    private static DateOnly? DateOf(string key) =>
        key.StartsWith(Prefix, StringComparison.Ordinal) && key.EndsWith(Suffix, StringComparison.Ordinal)
        && DateOnly.TryParseExact(key[Prefix.Length..^Suffix.Length], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
```

`sim/FantasyWorld.Broadcast/BroadcastApp.cs`:
```csharp
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast;

/// <summary>
/// <c>fw-broadcast run [--dry-run] [--create] [--config broadcast.json] [--local &lt;dir&gt;]</c>. Secrets come only
/// from the environment: R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, DISCORD_WEBHOOK_URL and, for
/// dry runs, DISCORD_TEST_WEBHOOK_URL. <c>--local</c> uses a directory instead of R2.
/// </summary>
public static class BroadcastApp
{
    public const string Usage = """
        usage: fw-broadcast run [--dry-run] [--create] [--config <broadcast.json>] [--local <dir>]
          --dry-run   every step, under the staging prefix, posting to DISCORD_TEST_WEBHOOK_URL (if set)
          --create    begin a new world when none is stored (never done by a scheduled run)
          --local     keep the world in a local directory instead of R2
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, Func<DateTimeOffset> clock,
        Func<string, string?> env, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (args is not ["run", ..] || args.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                                             && a is not ("--dry-run" or "--create" or "--config" or "--local")))
        {
            output.WriteLine(Usage);
            return DailyRun.Failed;
        }
        var options = new RunOptions(args.Contains("--dry-run"), args.Contains("--create"));

        BroadcastConfig config;
        try
        {
            config = BroadcastConfig.Load(Option(args, "--config") ?? "broadcast.json");
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or TimeZoneNotFoundException)
        {
            output.WriteLine($"error: broadcast.json: {e.Message}");
            return DailyRun.Failed;
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);

        IObjectStore? store = Option(args, "--local") is { } dir ? new LocalObjectStore(dir) : R2ObjectStore.FromEnvironment(http, env);
        if (store is null)
        {
            output.WriteLine("error: R2 is not configured. Set R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY.");
            return DailyRun.Failed;
        }

        var webhookName = options.DryRun ? "DISCORD_TEST_WEBHOOK_URL" : "DISCORD_WEBHOOK_URL";
        var channels = new List<IEpisodeChannel>();
        if (env(webhookName) is { Length: > 0 } webhook)
            channels.Add(new DiscordWebhook(http, webhook, config.DiscordName));
        else if (!options.DryRun)
        {
            output.WriteLine($"error: {webhookName} is not set, so the episode could not be posted.");
            return DailyRun.Failed;
        }

        return await new DailyRun(config, store, channels, clock, output, options).RunAsync(ct).ConfigureAwait(false);
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
```

`sim/FantasyWorld.Broadcast/BroadcastConfig.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The broadcast's checked-in settings (<c>broadcast.json</c>). Nothing secret lives here: keys and webhooks come from
/// the environment (GitHub Actions secrets).
/// </summary>
public sealed record BroadcastConfig
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The world's object key, under the live or staging prefix.</summary>
    public string WorldKey { get; init; } = "world.fw.db";

    /// <summary>The seed and size a <c>create</c> run starts the world with.</summary>
    public ulong Seed { get; init; } = 20261027;

    public string Size { get; init; } = "medium";

    /// <summary>Episodes are keyed by the broadcast date in this time zone.</summary>
    public string TimeZone { get; init; } = "Asia/Bangkok";

    public string Language { get; init; } = "en";

    public string LivePrefix { get; init; } = "live/";

    public string StagingPrefix { get; init; } = "staging/";

    /// <summary>Daily backups kept; the first backup of each month is kept as well, for good.</summary>
    public int BackupDays { get; init; } = 30;

    public int Stories { get; init; } = Episode.DefaultStories;

    public int MaxCharacters { get; init; } = Episode.DefaultMaxCharacters;

    /// <summary>The name the Discord post appears under.</summary>
    public string DiscordName { get; init; } = "The Chronicle";

    /// <summary>When on, the workflow commits a heartbeat monthly so GitHub keeps the schedule of a public repository alive.</summary>
    public bool KeepAlive { get; init; } = true;

    public static BroadcastConfig Load(string path) => Parse(File.ReadAllText(path));

    public static BroadcastConfig Parse(string json) =>
        (JsonSerializer.Deserialize<BroadcastConfig>(json, Json) ?? throw new FormatException("broadcast.json is empty.")).Validated();

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>The broadcast date (yyyy-MM-dd) at <paramref name="now"/>, in the broadcast's time zone.</summary>
    public string DateAt(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private BroadcastConfig Validated()
    {
        if (Language != "en") throw new FormatException($"Only English broadcasts exist so far (language \"{Language}\").");
        if (Size is not ("tiny" or "medium" or "large")) throw new FormatException($"Unknown size \"{Size}\".");
        if (BackupDays < 1) throw new FormatException("backupDays must be at least 1.");
        if (Stories is < 0 or > 10) throw new FormatException("stories must be between 0 and 10.");
        if (MaxCharacters is < 300 or > 4000) throw new FormatException("maxCharacters must be between 300 and 4000 (a Discord post holds 4096).");
        if (LivePrefix == StagingPrefix || !LivePrefix.EndsWith('/') || !StagingPrefix.EndsWith('/'))
            throw new FormatException("livePrefix and stagingPrefix must differ and end with '/'.");
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
    }
}
```

`sim/FantasyWorld.Broadcast/Channels/DiscordWebhook.cs`:
```csharp
using System.Text;
using System.Text.Json.Nodes;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast.Channels;

/// <summary>Somewhere an episode is published. Throws <see cref="ChannelException"/> when it could not be.</summary>
public interface IEpisodeChannel
{
    string Name { get; }

    Task PublishAsync(Episode episode, CancellationToken ct = default);
}

public sealed class ChannelException(string message) : Exception(message);

/// <summary>
/// Posts the episode to a Discord channel through a webhook: one embed with the script. The webhook URL is a secret
/// (it carries its token), so errors name the status code only.
/// </summary>
public sealed class DiscordWebhook(HttpClient http, string webhookUrl, string username) : IEpisodeChannel
{
    /// <summary>Discord's limits on an embed.</summary>
    public const int MaxTitle = 256, MaxDescription = 4096;

    public string Name => "discord";

    public static JsonObject Payload(Episode episode, string username)
    {
        var years = episode.FromYear == episode.ToYear ? $"Year {episode.ToYear}" : $"Years {episode.FromYear}–{episode.ToYear}";
        return new JsonObject
        {
            ["username"] = username,
            // Names come from the world, so nothing in a post may ping anyone.
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
            ["embeds"] = new JsonArray
            {
                new JsonObject
                {
                    ["title"] = Clip($"{episode.WorldName}, {years}", MaxTitle),
                    ["description"] = Clip(episode.Script, MaxDescription),
                    ["footer"] = new JsonObject { ["text"] = $"Told by {episode.Chronicler} · {episode.EventCount} events · {episode.Date}" },
                },
            },
        };
    }

    public async Task PublishAsync(Episode episode, CancellationToken ct = default)
    {
        var url = webhookUrl + (webhookUrl.Contains('?') ? "&" : "?") + "wait=true";
        using var content = new StringContent(Payload(episode, username).ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ChannelException($"Discord could not be reached ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChannelException("Discord did not answer in time (the post may have gone out).");
        }
        using (response)
            if (!response.IsSuccessStatusCode)
                throw new ChannelException($"Discord answered HTTP {(int)response.StatusCode}.");
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
```

`sim/FantasyWorld.Broadcast/DailyRun.cs`:
```csharp
using System.Globalization;
using System.Text;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Broadcast;

public sealed record RunOptions(bool DryRun = false, bool Create = false);

/// <summary>
/// One morning's broadcast (spec §3): pull the world, back it up, catch it up, build the episode, push the episode and
/// then the world, then publish to each channel every episode of the last week not yet posted there. The world goes back
/// to the store only after the catch-up and the episode both succeeded, and each finished step is written to the
/// manifest at once, so a rerun repeats nothing that already happened.
/// </summary>
public sealed class DailyRun(BroadcastConfig config, IObjectStore root, IReadOnlyList<IEpisodeChannel> channels,
    Func<DateTimeOffset> clock, TextWriter log, RunOptions? options = null)
{
    /// <summary>Exit codes: published; nothing published (the world is untouched); the episode is out but a channel failed.</summary>
    public const int Ok = 0, Failed = 1, Partial = 2;

    /// <summary>How far back a failed post is still made up for.</summary>
    public const int MissedDays = 7;

    private readonly RunOptions _options = options ?? new RunOptions();
    private bool _worldSaved;

    public static string EpisodeKey(string date) => $"episodes/{date}/episode.json";

    public static string ScriptKey(string date) => $"episodes/{date}/script.txt";

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var store = new PrefixedStore(root, _options.DryRun ? config.StagingPrefix : config.LivePrefix);
        var date = config.DateAt(clock());
        log.WriteLine($"Broadcast for {date}{(_options.DryRun ? " (dry run)" : "")}, under {store.Prefix}.");

        Manifest manifest;
        Episode episode;
        _worldSaved = false;
        try
        {
            manifest = Manifest.FromBytes(await store.GetAsync(Manifest.Key, ct).ConfigureAwait(false));
            if (manifest.Day(date).Done(Manifest.EpisodeStep))
            {
                log.WriteLine("Today's episode is already built; the world is not touched again today.");
                episode = await StoredEpisodeAsync(store, date, ct).ConfigureAwait(false);
            }
            else
                episode = await MakeEpisodeAsync(store, manifest, date, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.WriteLine($"error: {e.Message}");
            log.WriteLine(_worldSaved
                ? "The world and the episode were saved, but not the manifest; the next run picks them up and posts them."
                : "Nothing was published, and the stored world is as it was.");
            return Failed;
        }

        // Days whose post failed are posted first, oldest first, so a missed morning is late but never lost.
        var earliest = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1 - MissedDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dates = manifest.Episodes
            .Where(d => d.Value.Done(Manifest.EpisodeStep) && string.CompareOrdinal(d.Key, earliest) >= 0 && string.CompareOrdinal(d.Key, date) <= 0)
            .Select(d => d.Key).ToList();
        var result = Ok;
        if (channels.Count == 0) log.WriteLine("No channel is configured: the episode is stored but posted nowhere.");
        foreach (var channel in channels)
        foreach (var day in dates)
        {
            if (manifest.Day(day).Done(channel.Name))
            {
                if (day == date) log.WriteLine($"{channel.Name}: already posted today, not posting again.");
                continue;
            }
            try
            {
                var told = day == date ? episode : await StoredEpisodeAsync(store, day, ct).ConfigureAwait(false);
                await channel.PublishAsync(told, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is ChannelException or IOException or InvalidOperationException && !ct.IsCancellationRequested)
            {
                log.WriteLine($"{channel.Name}: the episode of {day} was not posted: {e.Message} It will be tried again on the next run.");
                result = Partial;
                continue;
            }
            manifest.Day(day).Steps[channel.Name] = clock();
            try
            {
                await SaveAsync(store, manifest, ct).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                log.WriteLine($"error: {channel.Name} was posted, but the manifest could not be saved ({e.Message}); a rerun today would post again.");
                return Failed;
            }
            log.WriteLine(day == date ? $"{channel.Name}: posted." : $"{channel.Name}: posted the missed episode of {day}.");
        }

        await PruneBackupsAsync(store, date, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<Episode> MakeEpisodeAsync(PrefixedStore store, Manifest manifest, string date, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("fw-broadcast-");
        try
        {
            var worldPath = Path.Combine(work.FullName, "world.fw.db");
            var pulled = await store.GetAsync(config.WorldKey, ct).ConfigureAwait(false);
            var copiedFromLive = false;
            if (pulled is null && _options.DryRun)
            {
                pulled = await root.GetAsync(config.LivePrefix + config.WorldKey, ct).ConfigureAwait(false);
                copiedFromLive = pulled is not null;
                if (copiedFromLive) log.WriteLine("The staging world starts as a copy of the live one (the live world is only read).");
            }

            if (pulled is null)
            {
                if (!_options.Create)
                    throw new InvalidOperationException($"There is no world at {store.Prefix}{config.WorldKey}. " +
                                                        "Start the workflow by hand with 'create' to begin one; a scheduled run never does.");
                Fw("new", worldPath, "--seed", config.Seed.ToString(CultureInfo.InvariantCulture), "--size", config.Size);
            }
            else
            {
                await File.WriteAllBytesAsync(worldPath, pulled, ct).ConfigureAwait(false);
                var backup = BackupRetention.KeyFor(date);
                if (!(await store.ListAsync(backup, ct).ConfigureAwait(false)).Contains(backup))
                {
                    await store.PutAsync(backup, pulled, "application/octet-stream", ct).ConfigureAwait(false);
                    log.WriteLine($"Backed up the world to {store.Prefix}{backup}.");
                }
            }

            // The stored world says which day's episode it holds. A run that saved the episode and the world, then
            // stopped before the manifest, left that day unmarked: mark it now (its post follows), and if the day is
            // today, the world must not move again. (A copy of the live world holds a live episode, not a staging one.)
            if (pulled is not null && !copiedFromLive && BroadcastDateOf(worldPath) is { } held
                && !(manifest.Episodes.TryGetValue(held, out var heldDay) && heldDay.Done(Manifest.EpisodeStep)))
            {
                var stored = await StoredEpisodeAsync(store, held, ct).ConfigureAwait(false);
                manifest.Record(held, stored);
                manifest.Day(held).Steps[Manifest.EpisodeStep] = clock();
                log.WriteLine($"The episode of {held} was saved with the world by a run that stopped early; it is marked now.");
                if (held == date)
                {
                    await SaveAsync(store, manifest, ct).ConfigureAwait(false);
                    return stored;
                }
            }

            Fw("catchup", worldPath, "--quiet");
            var dir = Path.Combine(work.FullName, "episode");
            Fw("episode", worldPath, "--out", dir, "--date", date,
                "--stories", config.Stories.ToString(CultureInfo.InvariantCulture),
                "--max-characters", config.MaxCharacters.ToString(CultureInfo.InvariantCulture));
            var episode = Episode.FromJson(await File.ReadAllTextAsync(Path.Combine(dir, "episode.json"), ct).ConfigureAwait(false));
            var snapshot = Path.Combine(work.FullName, "upload.fw.db");
            Fw("snapshot", worldPath, snapshot);

            // The episode first, then the world that holds its marker: a world in the store always has its episode.
            await store.PutAsync(EpisodeKey(date), Encoding.UTF8.GetBytes(episode.ToJson()), "application/json", ct).ConfigureAwait(false);
            await store.PutAsync(ScriptKey(date), Encoding.UTF8.GetBytes(episode.Script), "text/plain; charset=utf-8", ct).ConfigureAwait(false);
            await store.PutAsync(config.WorldKey, await File.ReadAllBytesAsync(snapshot, ct).ConfigureAwait(false), "application/octet-stream", ct).ConfigureAwait(false);
            _worldSaved = true;
            log.WriteLine($"Saved the world and the episode to {store.Prefix}.");

            manifest.Record(date, episode);
            manifest.Day(date).Steps[Manifest.EpisodeStep] = clock();
            await SaveAsync(store, manifest, ct).ConfigureAwait(false);
            return episode;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            work.Delete(recursive: true);
        }
    }

    private static async Task<Episode> StoredEpisodeAsync(PrefixedStore store, string date, CancellationToken ct) =>
        await store.GetAsync(EpisodeKey(date), ct).ConfigureAwait(false) is { } bytes
            ? Episode.FromJson(Encoding.UTF8.GetString(bytes))
            : throw new InvalidOperationException($"The manifest says today's episode was built, but {store.Prefix}{EpisodeKey(date)} is missing.");

    private static string? BroadcastDateOf(string worldPath)
    {
        using var world = WorldStore.Open(worldPath);
        return world.Load().World.LastBroadcastDate;
    }

    private static Task SaveAsync(IObjectStore store, Manifest manifest, CancellationToken ct) =>
        store.PutAsync(Manifest.Key, manifest.ToBytes(), "application/json", ct);

    /// <summary>Runs one <c>fw</c> command in-process, its output indented in the log; a failure stops the run.</summary>
    private void Fw(params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, clock);
        foreach (var line in output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            log.WriteLine($"  fw {args[0]}: {line}");
        if (code != 0) throw new InvalidOperationException($"fw {args[0]} failed (exit code {code}).");
    }

    private async Task PruneBackupsAsync(IObjectStore store, string date, CancellationToken ct)
    {
        try
        {
            var keys = await store.ListAsync(BackupRetention.Prefix, ct).ConfigureAwait(false);
            foreach (var key in BackupRetention.ToDelete(keys, date, config.BackupDays))
                await store.DeleteAsync(key, ct).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            log.WriteLine($"warning: old backups could not be pruned ({e.Message}); the next run will try again.");
        }
    }
}
```

`sim/FantasyWorld.Broadcast/FantasyWorld.Broadcast.csproj`:
```
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\FantasyWorld.Sim\FantasyWorld.Sim.csproj" />
    <ProjectReference Include="..\FantasyWorld.Cli\FantasyWorld.Cli.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>fw-broadcast</AssemblyName>
    <RootNamespace>FantasyWorld.Broadcast</RootNamespace>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`sim/FantasyWorld.Broadcast/Manifest.cs`:
```csharp
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>What one broadcast date has done: each step is marked the moment it finishes, so a rerun skips it.</summary>
public sealed class ManifestDay
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
    public int Events { get; set; }
    public string? Headline { get; set; }
    public SortedDictionary<string, DateTimeOffset> Steps { get; set; } = new(StringComparer.Ordinal);

    public bool Done(string step) => Steps.ContainsKey(step);
}

/// <summary>
/// <c>manifest.json</c>: per broadcast date, which steps finished and when. The rule that keeps reruns safe: a channel
/// is posted to only when its step is not marked, and the mark is written back straight after.
/// </summary>
public sealed class Manifest
{
    public const string Key = "manifest.json";
    public const string EpisodeStep = "episode";
    public const string DiscordStep = "discord";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, ManifestDay> Episodes { get; set; } = new(StringComparer.Ordinal);

    public ManifestDay Day(string date) => Episodes.TryGetValue(date, out var day) ? day : Episodes[date] = new ManifestDay();

    public void Record(string date, Episode episode)
    {
        var day = Day(date);
        (day.FromYear, day.ToYear, day.Events, day.Headline) = (episode.FromYear, episode.ToYear, episode.EventCount, episode.Headline?.Text);
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static Manifest FromBytes(byte[]? bytes) =>
        bytes is null ? new Manifest() : JsonSerializer.Deserialize<Manifest>(bytes, Json) ?? new Manifest();
}
```

`sim/FantasyWorld.Broadcast/Program.cs`:
```csharp
using FantasyWorld.Broadcast;

return await BroadcastApp.RunAsync(args, Console.Out, () => DateTimeOffset.UtcNow, Environment.GetEnvironmentVariable);
```

`sim/FantasyWorld.Broadcast/Storage/IObjectStore.cs`:
```csharp
namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// Where the broadcast keeps the world, its backups, the manifest and every episode: Cloudflare R2 in production, a
/// directory in tests and local dry runs. Keys use forward slashes (<c>backups/2026-10-27.fw.db</c>).
/// </summary>
public interface IObjectStore
{
    /// <summary>The object's bytes, or null when there is no such key.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>Every key that starts with <paramref name="prefix"/>, in ordinal order.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>A view of a store under a key prefix: the live broadcast under <c>live/</c>, dry runs under <c>staging/</c>.</summary>
public sealed class PrefixedStore(IObjectStore inner, string prefix) : IObjectStore
{
    public string Prefix => prefix;

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(prefix + key, ct);

    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default) =>
        inner.PutAsync(prefix + key, content, contentType, ct);

    public async Task<IReadOnlyList<string>> ListAsync(string keyPrefix, CancellationToken ct = default) =>
        (await inner.ListAsync(prefix + keyPrefix, ct).ConfigureAwait(false)).Select(k => k[prefix.Length..]).ToList();

    public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(prefix + key, ct);
}
```

`sim/FantasyWorld.Broadcast/Storage/LocalObjectStore.cs`:
```csharp
namespace FantasyWorld.Broadcast.Storage;

/// <summary>An object store in a local directory, for tests and for trying the daily run without R2.</summary>
public sealed class LocalObjectStore(string root) : IObjectStore
{
    private string PathOf(string key)
    {
        if (key.Length == 0 || key.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException($"Not a valid object key: '{key}'.");
        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        IReadOnlyList<string> keys = !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        return Task.FromResult(keys);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }
}
```

`sim/FantasyWorld.Broadcast/Storage/R2ObjectStore.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;

namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// Cloudflare R2 through its S3 API (<c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>, region <c>auto</c>),
/// path-style: <c>/&lt;bucket&gt;/&lt;key&gt;</c>. Errors carry the status code only, never a credential.
/// </summary>
public sealed class R2ObjectStore(HttpClient http, string accountId, string bucket, string accessKeyId, string secretAccessKey,
    Func<DateTimeOffset>? clock = null) : IObjectStore
{
    public const string Region = "auto";

    private static readonly XNamespace S3 = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string Host => $"{accountId}.r2.cloudflarestorage.com";

    /// <summary>Reads the store's settings from the environment, or null when any of them is missing.</summary>
    public static R2ObjectStore? FromEnvironment(HttpClient http, Func<string, string?> env) =>
        (env("R2_ACCOUNT_ID"), env("R2_BUCKET"), env("R2_ACCESS_KEY_ID"), env("R2_SECRET_ACCESS_KEY")) is
            ({ Length: > 0 } account, { Length: > 0 } bucket, { Length: > 0 } id, { Length: > 0 } secret)
            ? new R2ObjectStore(http, account, bucket, id, secret)
            : null;

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, key, [], null, null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        Ensure(response, "read", key);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Put, key, [], content, contentType, ct).ConfigureAwait(false);
        Ensure(response, "write", key);
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        string? token = null;
        do
        {
            var query = new List<KeyValuePair<string, string>> { new("list-type", "2"), new("prefix", prefix) };
            if (token is not null) query.Add(new("continuation-token", token));
            using var response = await SendAsync(HttpMethod.Get, null, query, null, null, ct).ConfigureAwait(false);
            Ensure(response, "list", prefix);
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            keys.AddRange(xml.Descendants(S3 + "Contents").Select(c => (string)c.Element(S3 + "Key")!));
            token = (string?)xml.Root!.Element(S3 + "IsTruncated") == "true" ? (string?)xml.Root.Element(S3 + "NextContinuationToken") : null;
        }
        while (token is not null);
        return keys.Order(StringComparer.Ordinal).ToList();
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, key, [], null, null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) Ensure(response, "delete", key);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string? key, List<KeyValuePair<string, string>> query,
        byte[]? content, string? contentType, CancellationToken ct)
    {
        var path = "/" + SigV4.Encode(bucket) + (key is null ? "" : "/" + SigV4.Encode(key, keepSlashes: true));
        var queryText = string.Join("&", query.Select(kv => $"{SigV4.Encode(kv.Key)}={SigV4.Encode(kv.Value)}"));
        var request = new HttpRequestMessage(method, $"https://{Host}{path}{(queryText.Length > 0 ? "?" + queryText : "")}");
        var payloadHash = SigV4.Hash(content ?? []);
        var now = _clock();
        var headers = new Dictionary<string, string>
        {
            ["host"] = Host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = SigV4.AmzDate(now),
        };
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", headers["x-amz-date"]);
        request.Headers.TryAddWithoutValidation("Authorization",
            SigV4.Authorization(method.Method, path, query, headers, payloadHash, now, Region, "s3", accessKeyId, secretAccessKey));
        if (content is not null)
        {
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/octet-stream");
        }
        // Transport failures become IOException, and name no host: the host carries the account id.
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new IOException($"R2 could not be reached ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("R2 did not answer in time.");
        }
    }

    private static void Ensure(HttpResponseMessage response, string action, string key)
    {
        if (!response.IsSuccessStatusCode)
            throw new IOException($"R2 could not {action} '{key}': HTTP {(int)response.StatusCode}.");
    }
}
```

`sim/FantasyWorld.Broadcast/Storage/SigV4.cs`:
```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// AWS Signature Version 4, which R2's S3 API accepts. Written by hand (about sixty lines) rather than taking the AWS SDK:
/// the broadcast needs four calls, and the SDK's newer default checksums are a known source of trouble with S3-compatible
/// stores. Checked against AWS's published example signature.
/// </summary>
public static class SigV4
{
    public const string Algorithm = "AWS4-HMAC-SHA256";

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// The Authorization header for a request. <paramref name="headers"/> are the headers to sign, with lowercase names;
    /// they must include <c>host</c>, <c>x-amz-date</c> and <c>x-amz-content-sha256</c>.
    /// </summary>
    public static string Authorization(string method, string canonicalUri, IEnumerable<KeyValuePair<string, string>> query,
        IReadOnlyDictionary<string, string> headers, string payloadHash, DateTimeOffset time, string region, string service,
        string accessKeyId, string secretAccessKey)
    {
        var day = time.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var stamp = AmzDate(time);
        var canonicalQuery = string.Join("&", query
            .Select(kv => (Key: Encode(kv.Key), Value: Encode(kv.Value)))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        var signed = headers.OrderBy(h => h.Key, StringComparer.Ordinal).ToList();
        var canonicalHeaders = string.Concat(signed.Select(h => $"{h.Key}:{h.Value.Trim()}\n"));
        var signedHeaders = string.Join(";", signed.Select(h => h.Key));
        var canonicalRequest = $"{method}\n{canonicalUri}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var scope = $"{day}/{region}/{service}/aws4_request";
        var stringToSign = $"{Algorithm}\n{stamp}\n{scope}\n{Hash(Encoding.UTF8.GetBytes(canonicalRequest))}";
        var key = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), day), region), service), "aws4_request");
        var signature = Convert.ToHexString(Hmac(key, stringToSign)).ToLowerInvariant();
        return $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    public static string AmzDate(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>S3's URI encoding: every byte but the unreserved characters as %XX; slashes kept only in paths.</summary>
    public static string Encode(string value, bool keepSlashes = false)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~' || (keepSlashes && c == '/'))
                builder.Append(c);
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}
```

Replace `sim/FantasyWorld.Cli/CliApp.cs` entirely with:
```csharp
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;
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
          fw catchup <world-file> [--quiet]         simulate the real time that has passed, then the digest (--quiet: none)
          fw episode <world-file> --out <dir> [--date <yyyy-mm-dd>] [--stories <n>] [--max-characters <n>] [--keep-marker]   the broadcast's episode since it last aired
          fw snapshot <world-file> <copy>           a consistent single-file copy (safe to upload)
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
          fw view <world-file> digest | map [--as-of <year>] | character <id> | family <id> [--generations <n>]
                 | why <event-id> [--depth <n>] | chronicle [--from <year>] [--to <year>] [--category <c>] [--page <n>]
                 | realm <id> | artifact <id>         a screen's data as JSON, for clients
          fw tell <world-file> <event-id> [--depth <n>]   the why-chain as prose
          fw follow <world-file> <entity>           e.g. char#12, realm#3: their events lead the digest
          fw unfollow <world-file> <entity>
          fw stats <world-file>
          fw llm <world-file> [--provider none|anthropic|openrouter] [--model <tier>=<model>]...
                 [--story-beats <per-day>] [--narrations <per-day>] [--cap <usd-per-month>]
          fw eval narration|decisions [--provider anthropic|openrouter] [--model <tier>=<model>]... [--samples <n>]
                 [--draws <n>] [--seeds 1,2,3] [--cap <usd>] [--out <dir>]   live quality evals with a judge; results in evals/
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
                "catchup" => CatchUpCommand(path, rest, output, clock, providers),
                "episode" => EpisodeCommand(path, rest, output, clock),
                "snapshot" => Snapshot(path, rest, output),
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
                "view" => View(path, rest, output),
                "tell" => Tell(path, rest, output, providers),
                "digest" => DigestCommand(path, rest, output, providers),
                "live" => Live(path, rest, output, clock, providers),
                "follow" => Follow(path, rest, output, follow: true),
                "unfollow" => Follow(path, rest, output, follow: false),
                "stats" => Stats(path, output, clock),
                "llm" => LlmCommand(path, rest, output),
                "eval" => Eval(path, rest, output, providers),
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

    /// <summary>
    /// Simulates the real time that passed, then tells the digest and marks it seen. With --quiet (the broadcast's
    /// unattended runs) no digest is told: nothing is narrated and the owner's reading place is left alone.
    /// </summary>
    private static int CatchUpCommand(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?> providers)
    {
        var quiet = rest.Contains("--quiet");
        using var store = OpenExisting(path);
        Backups.Rotate(store, path);
        var (sim, gateway) = LoadLive(store, providers);
        var result = CatchUp.Run(sim, clock());
        var fresh = gateway.Journal.Pending.ToList();
        var digest = quiet ? null : TellDigest(store, sim, gateway, sinceEventId: null);
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintLlmSummary(output, gateway, fresh);
        if (digest is not null) output.WriteLine(digest);
        return 0;
    }

    /// <summary>
    /// The daily broadcast's episode (sub-project 6): everything since the audience last listened, as episode.json and
    /// script.txt in the --out directory. Moves the broadcast marker (never the owner's reading place) unless --keep-marker.
    /// </summary>
    private static int EpisodeCommand(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock)
    {
        var dir = Option(rest, "--out") ?? throw new FormatException("Use --out <directory>.");
        var date = Option(rest, "--date") ?? clock().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FormatException($"--date must look like 2026-10-27 (got '{date}').");
        var stories = int.Parse(Option(rest, "--stories") ?? Episode.DefaultStories.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (stories is < 0 or > 10) throw new FormatException("--stories must be between 0 and 10.");
        var budget = int.Parse(Option(rest, "--max-characters") ?? Episode.DefaultMaxCharacters.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (budget is < 300 or > 10_000) throw new FormatException("--max-characters must be between 300 and 10000.");
        using var store = OpenExisting(path);
        var (world, log, queue) = store.Load();
        var episode = Episode.Build(world, log, world.LastBroadcastEventId, date, store.LoadFollowed(), stories, budget);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "episode.json"), episode.ToJson());
        File.WriteAllText(Path.Combine(dir, "script.txt"), episode.Script);
        if (!rest.Contains("--keep-marker"))
        {
            world.LastBroadcastEventId = episode.LastEventId;
            world.LastBroadcastDate = date;
            store.Save(world, log, queue);
        }
        output.WriteLine($"Episode {date}: {episode.EventCount} events, Years {episode.FromYear}–{episode.ToYear}, {episode.Script.Length} characters; written to {dir}.");
        return 0;
    }

    /// <summary>A consistent single-file copy of the world (SQLite's online backup), safe to upload while WAL is in use.</summary>
    private static int Snapshot(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        using var store = OpenExisting(path);
        if (File.Exists(rest[0])) File.Delete(rest[0]);
        store.BackupTo(rest[0]);
        output.WriteLine($"Snapshot written to {rest[0]}.");
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
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Client", "FantasyWorld.Client\FantasyWorld.Client.csproj", "{F0DD3E2C-2C7F-4E4C-9766-7F44EED518A2}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FantasyWorld.Broadcast", "FantasyWorld.Broadcast\FantasyWorld.Broadcast.csproj", "{AB1F8990-DCD0-40FF-8E99-13FAF85E6B2F}"
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
		{F0DD3E2C-2C7F-4E4C-9766-7F44EED518A2}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{F0DD3E2C-2C7F-4E4C-9766-7F44EED518A2}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{F0DD3E2C-2C7F-4E4C-9766-7F44EED518A2}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{F0DD3E2C-2C7F-4E4C-9766-7F44EED518A2}.Release|Any CPU.Build.0 = Release|Any CPU
		{AB1F8990-DCD0-40FF-8E99-13FAF85E6B2F}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{AB1F8990-DCD0-40FF-8E99-13FAF85E6B2F}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{AB1F8990-DCD0-40FF-8E99-13FAF85E6B2F}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{AB1F8990-DCD0-40FF-8E99-13FAF85E6B2F}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~BroadcastPartsTests|FullyQualifiedName~DailyRunTests|FullyQualifiedName~SessionTests|FullyQualifiedName~StorageTests"`
Expected: `Passed!  - Failed: 0, Passed: 30`. Then `dotnet test sim` → `Passed: 617` in total.

- [ ] **Step 5: Commit**

```bash
git add broadcast.json sim
git commit -m "feat(broadcast): run the daily broadcast from R2 to Discord, idempotent and fail-safe

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The workflow and owner setup

Spec §3.4–3.5, §6. `.github/workflows/broadcast.yml` runs at 22:30 UTC (05:30 in Bangkok) and by hand (`dry-run`, on by default, and `create`), in a `broadcast` concurrency group that never cancels a run in progress. Secrets reach the run only as environment variables; inputs pass through the environment, never into the script. A run that is not a dry run is refused off the default branch. A keep-alive job commits a monthly heartbeat (catching up a missed month) so GitHub keeps the schedule of a public repository. `docs/broadcast-setup.md` walks the owner through R2, Discord, the secrets and the first runs; the README gains a section.

**Files:**
- Create: `.github/workflows/broadcast.yml`, `docs/broadcast-setup.md`
- Modify: `README.md`, `broadcast.json`

**Interfaces:**
- Consumes: Task 3.
- Produces: `.github/workflows/broadcast.yml`; `docs/broadcast-setup.md`; README section.

- [ ] **Step 1: Implement**

`.github/workflows/broadcast.yml`:
```yaml
name: broadcast

# The daily broadcast (sub-project 6): the world lives one more year and the episode is published, at 05:30 in
# Bangkok every morning. GitHub runs scheduled workflows only from the default branch, and may start them late.
on:
  schedule:
    - cron: '30 22 * * *'
  workflow_dispatch:
    inputs:
      dry-run:
        description: 'Work under staging/ and post to the test Discord channel'
        type: boolean
        default: true
      create:
        description: 'Begin a new world if none is stored (a scheduled run never does)'
        type: boolean
        default: false

# One run at a time, and a run in progress is never cancelled: two runs must not touch the world together.
concurrency:
  group: broadcast
  cancel-in-progress: false

permissions:
  contents: read

jobs:
  broadcast:
    runs-on: ubuntu-latest
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Build
        run: dotnet build sim/FantasyWorld.Broadcast --configuration Release
      - name: Broadcast
        env:
          R2_ACCOUNT_ID: ${{ secrets.R2_ACCOUNT_ID }}
          R2_BUCKET: ${{ secrets.R2_BUCKET }}
          R2_ACCESS_KEY_ID: ${{ secrets.R2_ACCESS_KEY_ID }}
          R2_SECRET_ACCESS_KEY: ${{ secrets.R2_SECRET_ACCESS_KEY }}
          DISCORD_WEBHOOK_URL: ${{ secrets.DISCORD_WEBHOOK_URL }}
          DISCORD_TEST_WEBHOOK_URL: ${{ secrets.DISCORD_TEST_WEBHOOK_URL }}
          DRY_RUN: ${{ inputs.dry-run }}
          CREATE: ${{ inputs.create }}
          REF: ${{ github.ref }}
          DEFAULT_REF: refs/heads/${{ github.event.repository.default_branch }}
        run: |
          if [ "$DRY_RUN" != "true" ] && [ "$REF" != "$DEFAULT_REF" ]; then
            echo "error: the live broadcast runs only from the default branch; tick dry-run to try a branch."
            exit 1
          fi
          args=(run --config broadcast.json)
          if [ "$DRY_RUN" = "true" ]; then args+=(--dry-run); fi
          if [ "$CREATE" = "true" ]; then args+=(--create); fi
          dotnet run --no-build --configuration Release --project sim/FantasyWorld.Broadcast -- "${args[@]}"

  # GitHub turns off the schedule of a public repository after 60 days without activity: the first scheduled run of
  # each month (Bangkok time) commits a one-line heartbeat, unless broadcast.json turns keepAlive off. A month whose
  # heartbeat failed is caught up by the next morning's run.
  keep-alive:
    needs: broadcast
    if: always() && github.event_name == 'schedule'
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
      - name: Monthly heartbeat
        run: |
          month="$(TZ=Asia/Bangkok date +%Y-%m)"
          if [ "$(jq -r .keepAlive broadcast.json)" != "true" ] || [ "$(cat .github/broadcast-heartbeat 2>/dev/null)" = "$month" ]; then
            echo "No heartbeat needed."
            exit 0
          fi
          echo "$month" > .github/broadcast-heartbeat
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add .github/broadcast-heartbeat
          git commit -m "chore: broadcast keep-alive [skip ci]" || exit 0
          git push || (git pull --rebase -q && git push)
```

Replace `README.md` entirely with:
````markdown
# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `sim/FantasyWorld.Client/` — the game's engine-free logic (session, terrain, layout), tested with the rest
- `sim/FantasyWorld.Broadcast/` — the daily broadcast: R2 storage, the morning run and Discord (`broadcast.json`)
- `game/` — the Godot 4.3 diorama client
- `docs/superpowers/specs/` — design specs
- `docs/superpowers/plans/` — implementation plans

## The game

![The morning digest over the diorama](docs/images/digest.png)

The world is a floating diorama: drag to orbit, scroll to descend from the whole realm to a single town. On launch
the world catches up on the time you were away and opens on the *morning digest*: the year, what changed, the
headline and *why did this happen?* Every name is a link: people open their life and ties, places fly the camera
there, realms and artifacts open their pages, and any event opens the chain of causes that led to it. The *map*
adds layers, a history slider back to Year 1, and a card for every settlement. While the game is open the world
lives on (one world day every four real minutes) and announces what matters.

![A region seen from above, with realm borders and settlements](docs/images/region.png)

War is drawn like a strategy map: each host's banner (in its realm's colours) marches from region to region along
a broad campaign arrow toward its target, attackers in red and defenders in blue; fronts carry teeth facing the
attacker, occupied land is hatched, and crossed swords mark this year's battles. Click a banner for its commander,
a battle for why it was fought. Knights ride with their realm's host, and battles can wound, kill or make them famous.

![A host on the march: campaign arrow, banner, front and occupied land](docs/images/war.png)
![Why a war began: the chain of causes](docs/images/why.png)

Run it with [Godot 4.3 .NET](https://godotengine.org/download/archive/4.3-stable/) and the .NET 8 SDK:

```bash
dotnet build game/FantasyWorld.Game.csproj
godot --path game                                   # first launch creates a world with a century of prehistory
godot --path game -- --world worlds/aerenmoor.fw.db # or open a world made with the CLI
```

Keys: **Esc** back · **H** home (the digest) · **M** the map. Worlds live in Godot's user folder unless given.

Checks: `godot --headless --path game res://tests/HeadlessChecks.tscn` drives every screen on a fresh world and
exits non-zero on failure (CI runs it). `godot --path game res://tests/Showcase.tscn -- --shots <dir>` renders a
full-size world from the angles above, and `godot --path game --write-movie war.avi --fixed-fps 30 res://tests/WarMovie.tscn`
films a host on the march (both need a display).

## Myths, magic and structures

The world grows its own legends, each with a cause you can follow:

- **Beasts** rise where the land is dangerous, more often after a ley surge: a wyrm in the mountains, a hydra in the
  swamp. They raid nearby towns until a hero hunts them by name; some become legends, very old ones slumber.
- **Trade roads** join markets at peace with each other; wars, occupations and danger sever them, and peace reopens
  them (the map's *Trade routes* layer).
- **Faiths** spread from realms and neighbours, held back by temples and saints' relics; a realm whose lands have
  turned takes up the new faith. A faith that loses its holy land grows zealous, may wage a holy war to take it
  back, and may split when a priest founds a heresy. Devout notables of renown become saints.
- **Magic** ebbs and flows in slow ley tides. Mage towers train mages where the law allows; a surge at a tower may go
  wild (a storm, a blight, a portal that lets danger in), and realms tighten their law after it. Where magic is
  outlawed, mages flee or burn.
- **Great works** take years: an architect, a patron, delays from war, plague and famine, collapses when rushed;
  towns that knew siege raise fortresses, and fallen works are restored.
- **Labyrinths** open in wild, ley-charged land with three to seven levels and a guardian on the deepest. Delvers go
  down level by level; slaying the guardian and taking the last relic clears it, leaving a ruin or a shrine.
- Rivals quarrel over their borders, and rulers who crave renown seek glory against weaker neighbours.

![A town with its temple and mage tower, a beast in the hills and a labyrinth's walls](docs/images/town.png)

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

Each world has a chronicler (a name, a temperament and a diction, the same every time the world opens) whose voice
and house style every narration follows. Narration may use only the facts it is given: one that invents a name or a
number is rejected and journaled, and the templates speak instead. The template voice is a product in its own right
(articles, numbers in words, several phrasings per event chosen by its id, headlines with a body that adds what the
title leaves out, and the story of why as one paragraph); its structure is checked on every test run against a real
history and a golden narration.

Live quality evals grade the LLM voice with a judge model on fixed seeds, within a cost cap:

```bash
dotnet run --project sim/FantasyWorld.Cli -- eval narration --cap 2      # events and chains: faithful, clear, voice, varied
dotnet run --project sim/FantasyWorld.Cli -- eval decisions --draws 40   # choices in character; arbitrations against their odds
```

Each run prints the mean scores and saves a report under `evals/`; no call is made once a run has spent its cap (default $2).

`--size tiny` makes a 64×64 world with 12 regions for quick experiments (and no prehistory; medium and large
worlds start with 100 years of it, `--prehistory <years>` to change).

## The chronicle

```bash
dotnet run --project sim/FantasyWorld.Cli -- digest worlds/aerenmoor.fw.db              # headlines since you last looked
dotnet run --project sim/FantasyWorld.Cli -- follow worlds/aerenmoor.fw.db char#12      # their deeds lead the digest
dotnet run --project sim/FantasyWorld.Cli -- live worlds/aerenmoor.fw.db --minutes 30   # watch it unfold in real time
dotnet run --project sim/FantasyWorld.Cli -- structures worlds/aerenmoor.fw.db          # landmarks, labyrinths and ruins
```

Every screen of the game also exists as data, for clients (the Godot diorama) and for checking without one:

```bash
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db digest            # the morning digest, as JSON
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db map --as-of 150   # the political map in Year 150
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db why <event-id>     # a why-chain as a layered graph
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db character <id>     # also: family, chronicle, realm, artifact
```

`catchup` ends with the digest. Minor events older than 50 years are condensed into one summary per decade as
the world runs. Every command that changes a world first keeps a
backup of the last three sessions next to it (`.bak1`–`.bak3`). Benchmarks:
`dotnet run -c Release --project sim/FantasyWorld.Bench -- --filter '*'`.

## The daily broadcast

Every morning at 05:30 Bangkok time, GitHub Actions pulls the world from Cloudflare R2, lets it live the year that passed,
builds the episode and posts it to Discord. A rerun the same day posts nothing new. Setup:
[docs/broadcast-setup.md](docs/broadcast-setup.md).

```bash
dotnet run --project sim/FantasyWorld.Cli -- episode worlds/aerenmoor.fw.db --out ep/   # episode.json and script.txt since the last one
dotnet run --project sim/FantasyWorld.Broadcast -- run --dry-run --create --local bucket/   # the whole morning, with a local folder for R2
```
````

Replace `broadcast.json` entirely with:
```json
{
  "worldKey": "world.fw.db",
  "seed": 20261027,
  "size": "medium",
  "timeZone": "Asia/Bangkok",
  "language": "en",
  "livePrefix": "live/",
  "stagingPrefix": "staging/",
  "backupDays": 30,
  "stories": 5,
  "maxCharacters": 3600,
  "discordName": "The Chronicle",
  "keepAlive": true
}
```

`docs/broadcast-setup.md`:
````markdown
# Daily broadcast: owner setup

The broadcast runs in GitHub Actions (`.github/workflows/broadcast.yml`) every morning at 05:30 Bangkok time. The world
lives in Cloudflare R2, and each episode is posted to Discord. Nothing runs on your computer. You set it up once.

## 1. Cloudflare R2

1. In the Cloudflare dashboard, open **R2** and create a bucket (for example `fantasy-world`). Keep it private: the
   world file and its backups are not for the public.
2. Under **R2 → Manage API tokens**, create a token with **Object Read & Write** on that bucket only. Note the
   **Access Key ID** and **Secret Access Key** (shown once), and your **Account ID** (on the R2 overview page).

## 2. Discord

In your server: **Channel settings → Integrations → Webhooks → New webhook**, then **Copy webhook URL**. Make one for the
real channel, and (recommended) one for a private test channel used by dry runs.

## 3. GitHub secrets

In the repository: **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|---|---|
| `R2_ACCOUNT_ID` | Cloudflare account id |
| `R2_BUCKET` | the bucket's name |
| `R2_ACCESS_KEY_ID` | the token's access key id |
| `R2_SECRET_ACCESS_KEY` | the token's secret access key |
| `DISCORD_WEBHOOK_URL` | the real channel's webhook |
| `DISCORD_TEST_WEBHOOK_URL` | the test channel's webhook (optional; without it a dry run posts nowhere) |

Secrets are only ever read from the environment. They are never written to R2, the world file, the logs or the repository.

## 4. Start it

1. Merge to the default branch. GitHub runs scheduled workflows only from there, and a run that is not a dry run is
   refused on any other branch.
2. **Actions → broadcast → Run workflow** with `dry-run` ticked and `create` ticked. This makes a staging world under
   `staging/` and posts to the test channel. Read the episode there.
3. Run it again with `dry-run` unticked and `create` ticked. This begins the real world under `live/` and posts the first
   episode. From then on the schedule takes over; never tick `create` again unless you want to start over after
   deleting `live/world.fw.db`.
4. Watch three mornings in a row. Each should post one episode. Re-running the workflow the same day posts nothing new.

## Settings (`broadcast.json`)

| Field | Default | Meaning |
|---|---|---|
| `worldKey` | `world.fw.db` | the world's name in the bucket, under the prefix |
| `seed`, `size` | `20261027`, `medium` | the world a `create` run begins |
| `timeZone` | `Asia/Bangkok` | episodes are dated in this zone |
| `language` | `en` | only English exists so far |
| `livePrefix`, `stagingPrefix` | `live/`, `staging/` | where real and dry runs keep their files |
| `backupDays` | `30` | daily backups kept; the first backup of each month is kept for good |
| `stories`, `maxCharacters` | `5`, `3600` | episode length (about 2–4 minutes read aloud) |
| `discordName` | `The Chronicle` | the name posts appear under |
| `keepAlive` | `true` | a monthly heartbeat commit, so GitHub keeps the schedule of a public repository; turn it off for a private one |

## What is in the bucket

```
live/world.fw.db                       the world (a consistent single-file copy)
live/manifest.json                     per date, which steps finished: a rerun repeats none of them
live/episodes/2026-10-27/episode.json  the episode as data (years, headline, stories, why)
live/episodes/2026-10-27/script.txt    the script as posted
live/backups/2026-10-27.fw.db          the world as it was before that morning's run
```

## When something goes wrong

- **A red run with "There is no world"**: the bucket is empty or the R2 secrets point somewhere else. A scheduled run never
  begins a new world, so check the secrets.
- **"Discord answered HTTP …"**: the episode was saved and the world moved on. The post is made by the next run, before
  that morning's own (re-run the workflow to post it now); posts missed for up to a week are made up, oldest first. A
  post is never made twice, except when Discord took it but timed out before answering.
- **A run stopped part-way** (a timeout, an R2 hiccup): re-run it. Each step it finished is recorded, and the stored world
  says which day's episode it holds, so nothing is told twice or skipped.
- **The world misbehaves**: copy a file from `live/backups/` over `live/world.fw.db` and leave `live/manifest.json` alone.
  The next run carries on from the restored world. Never delete the manifest: it is what stops a day being posted twice.
````

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test sim` → `Passed: 617` in total.

- [ ] **Step 3: Commit**

```bash
git add .github README.md broadcast.json docs
git commit -m "ci(broadcast): schedule the daily broadcast and document its setup

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §3.1 broadcast marker, separate from the owner's reading place | 2 |
| §3.2 episodes: cold open, gap, stories, why, sign-off; `fw episode` | 2 |
| §3 storage (R2 and local), pulling, uploading a snapshot | 3 |
| §3.3 manifest and idempotency | 3 |
| §3.4 one run at a time, backups, fail safe, long gaps, dry run | 3, 4 |
| §3.5 secrets and `broadcast.json` | 3, 4 |
| §4 M1: daily cloud world and a text post to Discord | 1–4 |
| §6 owner setup | 4 |

**Done when (M1):** three scheduled runs in a row post one episode each with nothing on the owner's computer, and a rerun the same day posts nothing. Then M2 (Sonnet script with guard and template fallback; the political map as PNG) gets its own plan.

Deferred to M2–M4: the LLM-written script, the map image, narration audio and pronunciation, the podcast feed and archive page, the monthly speech budget, video and YouTube.
