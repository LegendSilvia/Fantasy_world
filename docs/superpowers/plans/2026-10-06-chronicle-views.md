# Chronicle Views Implementation Plan (sub-project 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn everything the sim core can tell into presentation-ready data for the four designed screens (morning digest, diorama map, character page, why chain) and the chronicle, realm and artifact pages, so the Godot client only renders, and every screen can be checked headlessly as JSON.

**Architecture:** A new namespace, `FantasyWorld.Sim.Views`, holds immutable record trees built by pure functions of (world, log, reader state). Names in text carry their `EntityRef`, events their id, and each event a `Category` that the client maps to colour. The map can be rebuilt at any past date by replaying the log from founding owners recorded at world creation. `fw view` prints any screen as JSON. Nothing here changes how the world evolves: the only engine changes are the world's name, the recorded founding owners and lieges, and the side an ally joined on, all stored, none read by the simulation.

**Tech Stack:** .NET 8 / C# 12, xUnit, Microsoft.Data.Sqlite, System.Text.Json, BenchmarkDotNet 0.14.0 (all unchanged).

**Spec:** `docs/superpowers/specs/2026-10-06-chronicle-views-design.md`. **Builds on:** the sim core, Plans 1–6.

## Global Constraints

Everything in Plans 1–6's Global Constraints still applies. In addition:

- **Views are pure:** they never mutate the world or the log and never call the LLM; prose comes from an `INarrator` passed in (templates by default).
- **Every visible name is a link and every card an event id;** every `EntityRef` in a view resolves and every position is in [0, 1] (checked in Task 8).
- **History is replayed, not stored:** a past map is rebuilt from the founding baseline and the log; at or after today it is the live state.
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Founding baseline | Stored as `World.FoundingOwners`/`FoundingLieges` in the snapshot, not a `WorldFounded` event | The spec suggested an event; a world-state field is read the same way, survives compaction, and keeps the event log for things that happened |
| Older worlds | Without a recorded founding, the map shows today's state with `Historical = false` | No guessing at a past the file cannot tell |
| Peace terms | The replay applies the engine's own rule (sovereign tops, wars still open), checked equal to live state over 140 years | Any divergence would show the wrong past silently |
| Timeline | Events of importance ≥ 20, at most 150 rows, highlight = most important of the last year | Spec §3.3 folds minor rows |
| Trade routes | Not modelled; the client shows the layer disabled | Spec §3.2 defers it to sub-project 3 |
| JSON shape | camelCase, enums as strings, refs as `kind#id` | Readable goldens and the same ids the CLI prints |

**Verification:** an independent review of the prototype found that the history replay drifted from the live map after peace treaties and allied joins, that dead characters were shown with a liege, that the digest wrapped seasons wrongly and pinned one place twice, and that `fw view` accepted bad options silently; all are fixed in the code below and covered by tests (the replay is checked equal to the live state on two seeds over 60 and 140 years). The code in this plan was replayed task by task in this order: each task's tests fail first and pass after the implementation, with the counts given.

## File Structure

```
sim/
  FantasyWorld.Sim/
    Views/Category.cs, Linked.cs                                                       (Task 1)
    Views/MapPoint.cs, DigestScreen.cs; Model/World.cs (Name); Persist/WorldSnapshot.cs;
      WorldGen/WorldGenerator.cs; Narration/TemplateNarrator.cs (Sentence)             (Task 2)
    Views/CharacterScreen.cs                                                           (Task 3)
    Views/WhyScreen.cs                                                                 (Task 4)
    Views/History.cs; Model/World.cs (founding); Persist/WorldSnapshot.cs;
      WorldGen/RealmGenerator.cs; Systems/DiplomacySystem.cs (ally side)                (Task 5)
    Views/MapScreen.cs                                                                 (Task 6)
    Views/ChronicleScreens.cs                                                          (Task 7)
    Views/ViewJson.cs                                                                  (Task 8)
  FantasyWorld.Cli/CliApp.cs, CliApp.Views.cs                                          (Task 8)
  FantasyWorld.Bench/ViewsBenchmark.cs                                                 (Task 8)
  FantasyWorld.Sim.Tests/Views/{Category,DigestScreen,CharacterScreen,WhyScreen,History,MapScreen,ChronicleScreen,ViewJson}Tests.cs,
    Views/Golden/{digest,map,character,why}.json
README.md                                                                              (Task 8)
```

All commands run from the repository root. Requires the .NET 8 SDK.

---

### Task 1: Categories and links

Spec §2: system colour is data, not style, and every visible name is a link. Each event gets one `Category` from its type and tags (the client maps categories to the palette), a short kicker ("War ends"), and narrated text becomes `LinkedText`: the sentence plus the spans where the names of the entities the event mentions appear, each carrying its `EntityRef`.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/Category.cs`, `sim/FantasyWorld.Sim/Views/Linked.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/CategoryTests.cs`

**Interfaces:**
- Consumes: `WorldEvent`, `EntityRef` (Plans 1–6).
- Produces (namespace `FantasyWorld.Sim.Views`): `enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }`; `static class Categories { Of(ev); Label(category); Kicker(ev) }`; `record TextLink(Start, Length, Entity)`; `record LinkedText(Text, Links) { Plain(text); Link(text, entities, nameOf); Mentions(ev) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/CategoryTests.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

public class CategoryTests
{
    private static WorldEvent Event(string type, params string[] tags) =>
        new EventLog().Append(default, new EventDraft(type, Tags: tags));

    [Fact]
    public void Events_FallIntoTheMostSpecificCategory()
    {
        Assert.Equal(Category.War, Categories.Of(Event("PeaceSigned", "Politics", "War")));
        Assert.Equal(Category.Structures, Categories.Of(Event("LabyrinthOpened", "Structures", "Magic")));
        Assert.Equal(Category.FaithAndMagic, Categories.Of(Event("HolyWarProclaimed", "Politics", "Faith")));
        Assert.Equal(Category.Politics, Categories.Of(Event("AllianceFormed", "Politics", "Diplomacy")));
        Assert.Equal(Category.Economy, Categories.Of(Event("RevoltBegan", "Settlements")));
        Assert.Equal(Category.Nature, Categories.Of(Event("Drought", "Regions")));
        Assert.Equal(Category.Characters, Categories.Of(Event("Rumour")));
        Assert.Equal("Faith & magic", Categories.Label(Category.FaithAndMagic));
        Assert.Equal("War ends", Categories.Kicker(Event("PeaceSigned")));
        Assert.Equal("Monsters slain", Categories.Kicker(Event("MonstersSlain")));
    }

    [Fact]
    public void NarratedText_LinksEveryNameItMentions()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = a.Ref, ["defender"] = b.Ref },
            Place: world.Settlements[1].Ref, Payload: new Dictionary<string, string> { ["stakes"] = "to first blood" }));
        var text = new TemplateNarrator(world).Narrate(duel);

        var linked = LinkedText.Link(text, LinkedText.Mentions(duel), Names.Of(world));

        Assert.Equal(3, linked.Links.Count);
        foreach (var link in linked.Links)
            Assert.Equal(Names.Of(world)(link.Entity), text.Substring(link.Start, link.Length));
        Assert.Equal(new[] { a.Ref, b.Ref, world.Settlements[1].Ref }, linked.Links.Select(l => l.Entity));
    }

    [Fact]
    public void Links_PreferLongerNames_AndNeverOverlap()
    {
        var names = new Dictionary<EntityRef, string> { [new("house", 1)] = "Venn", [new("char", 2)] = "Maren of Venn" };
        var linked = LinkedText.Link("Maren of Venn rode to Venn.", names.Keys, e => names[e]);
        Assert.Equal(new[] { (0, 13, new EntityRef("char", 2)), (22, 4, new EntityRef("house", 1)) },
            linked.Links.Select(l => (l.Start, l.Length, l.Entity)));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0234: The type or namespace name 'Views' does not exist in the namespace 'FantasyWorld.Sim' (are you missing an assembly reference?)`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Views/Category.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

`sim/FantasyWorld.Sim/Views/Linked.cs`:
```csharp
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Views;

/// <summary>A span of text that names an entity, so a client can make it a link.</summary>
public sealed record TextLink(int Start, int Length, EntityRef Entity);

/// <summary>Text with its entity links (every visible name can be followed).</summary>
public sealed record LinkedText(string Text, IReadOnlyList<TextLink> Links)
{
    public static LinkedText Plain(string text) => new(text, Array.Empty<TextLink>());

    /// <summary>
    /// Finds the first mention of each entity's name in the text, longest names first so "Maren Holt of Venn" wins
    /// over "Venn", and never overlapping.
    /// </summary>
    public static LinkedText Link(string text, IEnumerable<EntityRef> entities, Func<EntityRef, string?> nameOf)
    {
        var links = new List<TextLink>();
        foreach (var (entity, name) in entities.Distinct().Select(e => (e, nameOf(e))).Where(x => !string.IsNullOrEmpty(x.Item2))
                     .OrderByDescending(x => x.Item2!.Length).ThenBy(x => x.e.Kind, StringComparer.Ordinal).ThenBy(x => x.e.Id))
        {
            var from = 0;
            while (true)
            {
                var at = text.IndexOf(name!, from, StringComparison.Ordinal);
                if (at < 0) break;
                if (!links.Any(l => at < l.Start + l.Length && l.Start < at + name!.Length))
                {
                    links.Add(new TextLink(at, name!.Length, entity));
                    break;
                }
                from = at + 1;
            }
        }
        return new LinkedText(text, links.OrderBy(l => l.Start).ToList());
    }

    /// <summary>All entities an event mentions: its actors and place.</summary>
    public static IEnumerable<EntityRef> Mentions(WorldEvent ev) =>
        ev.Place is { } place ? ev.Actors.Values.Append(place) : ev.Actors.Values;
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CategoryTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 460` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): give events a category and make names in text links

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Digest screen

Spec §3.1, the launch screen after catch-up. Built from Plan 6's `DigestView`: the world's name and year, a subtitle that counts the time away and the seasons crossed, a status line ("Caught up · Year 312, day 196" or "Catching up · 12 days behind"), a headline with category, kicker and whether it can be explained, numbered items, and up to five numbered map pins, one per place. Worlds now have a name (generated; older files read as "The World"), and `TemplateNarrator.Sentence` tells one event as a bare sentence for headlines and lists.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/DigestScreen.cs`, `sim/FantasyWorld.Sim/Views/MapPoint.cs`
- Modify: `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/WorldGen/WorldGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/DigestScreenTests.cs`

**Interfaces:**
- Consumes: Task 1; `Digest`, `DigestView`, `INarrator`, `TemplateNarrator` (Plan 6).
- Produces: `record MapPoint(X, Y) { Of(world, entity) }` (normalised 0–1); `record DigestPin`, `Headline`, `DigestItem`, `DigestScreen { Build(world, log, digest, since, daysBehind, narrator); ForReader(world, log, followed, daysBehind, narrator); DisplayName(world) }`; `World.Name` (snapshot field); `TemplateNarrator.Sentence(ev)`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/DigestScreenTests.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class DigestScreenTests
{
    [Fact]
    public void Worlds_HaveAStableName_ThatPersists()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.NotEmpty(world.Name);
        Assert.Equal(world.Name, WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch).Name);
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        store.Save(world, new EventLog(), new EventQueue());
        Assert.Equal(world.Name, store.Load().World.Name);
    }

    [Fact]
    public void TheDigestScreen_NumbersEventsByImportance_AndPinsTheirPlaces()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 3);
        var since = world.Date;
        var seen = sim.Log.NextId - 1;
        sim.AdvanceDays(360 * 2);
        var digest = Digest.Build(sim.Log, world, seen, new HashSet<EntityRef>());

        var screen = DigestScreen.Build(world, sim.Log, digest, since);

        Assert.Equal(world.Name, screen.WorldName);
        Assert.Equal("Year 6.", screen.Title);
        Assert.StartsWith($"While you were away, two years passed in {world.Name}. The seasons turned, and ", screen.Subtitle);
        Assert.EndsWith(" events were recorded.", screen.Subtitle);
        Assert.Equal(screen.Pins.Count, screen.Pins.Select(p => p.Place).Distinct().Count());
        Assert.Equal("Caught up · Year 6, day 1", screen.Status);
        var numbered = new[] { screen.Headline!.Number }.Concat(screen.Items.Select(i => i.Number)).ToList();
        Assert.Equal(Enumerable.Range(1, digest.Entries.Count()), numbered);
        Assert.Equal(digest.Entries.MaxBy(e => e.Score)!.Score, digest.Entries.Single(e => e.Event.Id == screen.Headline.EventId).Score);
        Assert.InRange(screen.Pins.Count, 1, 5);
        Assert.All(screen.Pins, p => Assert.True(p.Position.X is > 0 and < 1 && p.Position.Y is > 0 and < 1));
        var nameOf = Names.Of(world);
        foreach (var text in screen.Items.Select(i => i.Text).Append(screen.Headline.Title))
            Assert.All(text.Links, l => Assert.Equal(nameOf(l.Entity), text.Text.Substring(l.Start, l.Length)));
    }

    [Fact]
    public void AnEmptyDigest_SaysSo()
    {
        var world = TestPeople.World();
        var screen = DigestScreen.Build(world, new EventLog(), Digest.Build(new EventLog(), world, 0, new HashSet<EntityRef>()), world.Date, daysBehind: 12);
        Assert.Null(screen.Headline);
        Assert.Empty(screen.Items);
        Assert.Equal("Catching up · 12 days behind", screen.Status);
        Assert.StartsWith("Nothing has passed", screen.Subtitle);
        Assert.Equal("The World", screen.WorldName);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'World' does not contain a definition for 'Name' and no accessible extension method 'Name' accepting a first argument of type 'World' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

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

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

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
    SortedDictionary<long, Structure>? Structures = null,
    string? Name = null)
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
        world.Structures,
        world.Name);

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
        Name = Name ?? "",
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

`sim/FantasyWorld.Sim/Views/DigestScreen.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Views;

public sealed record DigestPin(int Number, EntityRef Place, string Label, MapPoint Position, long EventId);

public sealed record Headline(int Number, Category Category, string Kicker, LinkedText Title, LinkedText Body, long EventId, bool CanExplain);

public sealed record DigestItem(int Number, Category Category, LinkedText Text, long EventId);

/// <summary>
/// The *Morning digest* screen (sub-project 2 §3.1): the digest's events numbered by importance (1 = the headline),
/// map pins for those with a place, a title and subtitle for the time away, and the catch-up status line.
/// </summary>
public sealed record DigestScreen(
    string WorldName, int Year, string Title, string Subtitle, long ElapsedDays, string Status,
    IReadOnlyList<DigestPin> Pins, Headline? Headline, IReadOnlyList<DigestItem> Items)
{
    private static readonly string[] Numbers = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    public static DigestScreen Build(World world, EventLog log, DigestView digest, WorldDate since, long daysBehind = 0, INarrator? narrator = null)
    {
        var templates = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        var name = DisplayName(world);
        var ranked = digest.Entries.OrderByDescending(e => e.Score).ThenBy(e => e.Event.Id).Select((e, i) => (Number: i + 1, e.Event)).ToList();
        LinkedText Link(string text, WorldEvent ev) => LinkedText.Link(text, LinkedText.Mentions(ev), nameOf);

        var pins = ranked.Where(r => r.Event.Place is { } p && MapPoint.Of(world, p) is not null)
            .DistinctBy(r => r.Event.Place!.Value).Take(5)
            .Select(r => new DigestPin(r.Number, r.Event.Place!.Value, nameOf(r.Event.Place!.Value) ?? r.Event.Place!.Value.ToString(),
                MapPoint.Of(world, r.Event.Place!.Value)!, r.Event.Id))
            .ToList();
        Headline? headline = null;
        if (ranked.Count > 0)
        {
            var top = ranked[0].Event;
            var body = narrator is null ? Note(top) : narrator.Narrate(top);
            headline = new Headline(1, Categories.Of(top), Categories.Kicker(top), Link(templates.Sentence(top), top), Link(body, top), top.Id, top.Causes.Count > 0);
        }
        var items = ranked.Skip(1).Select(r => new DigestItem(r.Number, Categories.Of(r.Event), Link(templates.Sentence(r.Event), r.Event), r.Event.Id)).ToList();

        var elapsed = Math.Max(0, world.Date.Day - since.Day);
        var subtitle = elapsed == 0
            ? $"Nothing has passed since you last looked at {name}."
            : $"While you were away, {Span(elapsed)} passed in {name}. {Seasons(since, world.Date)}, and {Count(digest.EventCount)} recorded.";
        var status = daysBehind > 0
            ? $"Catching up · {daysBehind.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} day{(daysBehind == 1 ? "" : "s")} behind"
            : $"Caught up · Year {world.Date.Year}, day {(world.Date.Month - 1) * WorldDate.DaysPerMonth + world.Date.DayOfMonth}";
        return new DigestScreen(name, world.Date.Year, $"Year {world.Date.Year}.", subtitle, elapsed, status, pins, headline, items);
    }

    /// <summary>The digest a returning reader sees: everything since they last looked, without marking it seen.</summary>
    public static DigestScreen ForReader(World world, EventLog log, IReadOnlySet<EntityRef> followed, long daysBehind = 0, INarrator? narrator = null)
    {
        var since = log.TryGet(world.LastSeenEventId, out var seen) ? seen.Date : new WorldDate(0);
        return Build(world, log, Digest.Build(log, world, world.LastSeenEventId, followed), since, daysBehind, narrator);
    }

    public static string DisplayName(World world) => world.Name.Length > 0 ? world.Name : "The World";

    /// <summary>What the engine knows of why or how: motive, account or lore, else when it happened.</summary>
    private static string Note(WorldEvent ev) =>
        ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("lore")
        ?? $"{Digest.SeasonLabel(ev.Date)}.";

    private static string Word(long n) => n < Numbers.Length ? Numbers[n] : n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Span(long days) =>
        days < WorldDate.DaysPerMonth ? Plural(days, "day")
        : days < WorldDate.DaysPerYear ? Plural(days / WorldDate.DaysPerMonth, "month")
        : Plural(days / WorldDate.DaysPerYear, "year");

    private static string Plural(long n, string unit) => $"{Word(n)} {unit}{(n == 1 ? "" : "s")}";

    private static string Count(int events) => events == 1 ? "one event was" : $"{Word(events)} events were";

    private static string Seasons(WorldDate from, WorldDate to) =>
        to.Day - from.Day >= WorldDate.DaysPerYear || from.Season == to.Season && to.Day - from.Day >= 90 ? "The seasons turned"
        : from.Season == to.Season ? $"It is still {to.Season.ToString().ToLowerInvariant()}"
        : $"{from.Season} gave way to {to.Season.ToString().ToLowerInvariant()}";
}
```

`sim/FantasyWorld.Sim/Views/MapPoint.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Views;

/// <summary>A position on the world map, 0–1 across and down (the client maps it onto its diorama).</summary>
public sealed record MapPoint(double X, double Y)
{
    public static MapPoint? Of(World world, EntityRef entity)
    {
        if (world.Geography is not { } geography) return null;
        var (w, h) = ((double)geography.Terrain.Width, (double)geography.Terrain.Height);
        return entity.Kind switch
        {
            Settlement.Kind when world.Settlements.TryGetValue(entity.Id, out var s) => new MapPoint((s.Tile.X + 0.5) / w, (s.Tile.Y + 0.5) / h),
            Region.Kind when world.Regions.TryGetValue(entity.Id, out var r) => new MapPoint((r.Center.X + 0.5) / w, (r.Center.Y + 0.5) / h),
            Structure.Kind when world.Structures.TryGetValue(entity.Id, out var st) =>
                Of(world, st.SettlementId != 0 ? new EntityRef(Settlement.Kind, st.SettlementId) : new EntityRef(Region.Kind, st.RegionId)),
            Character.Kind when world.Characters.TryGetValue(entity.Id, out var c) => Of(world, new EntityRef(Settlement.Kind, c.SettlementId)),
            _ => null,
        };
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
        world.Name = new NameGen(Stream("world-name")).Region();
        return world;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DigestScreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 463` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): add the morning digest screen

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Character screen and family tree

Spec §3.3. A character's name, house, one-line role ("Once …" for the dead), traits and ambition, where they are now (or were last), a timeline of notable deeds with the last year's most important one highlighted, and ties: family, the realm's ruler as liege (for the living and active), and the strongest friendships and rivalries from memory, each with a qualifier and tone. The family tree is a separate view of nodes and parent/spouse edges, a set number of generations up and down.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/CharacterScreen.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/CharacterScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `Character`, `Memories`, `HistoryQuery` (Plans 1–6).
- Produces: `enum Tone { Warm, Neutral, Hostile }`; `record Place`, `TimelineRow`, `Tie`; `record CharacterScreen { TimelineImportance = 20; MaxTimeline = 150; Build(world, log, characterId, followed) }`; `record FamilyNode`, `FamilyEdge`, `FamilyTree { Build(world, characterId, generations = 2) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/CharacterScreenTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class CharacterScreenTests
{
    [Fact]
    public void TheCharacterPage_ShowsWhoTheyAre_TheirLife_AndTheirTies()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new FantasyWorld.Sim.Core.WorldDate(360 * 40);
        var log = new EventLog();
        var father = TestPeople.Add(world, 1, 60);
        var knight = TestPeople.Add(world, 1, 27, role: Role.Knight, fatherId: father.Id);
        knight.Traits.AddRange(new[] { Traits.Brave, Traits.Wrathful });
        knight.Ambition = Ambitions.Renown;
        var rival = TestPeople.Add(world, 1, 30);
        var born = log.Append(new FantasyWorld.Sim.Core.WorldDate(knight.BirthDay), new EventDraft("CharacterBorn",
            Actors: new Dictionary<string, EntityRef> { ["child"] = knight.Ref, ["father"] = father.Ref }, Place: world.Settlements[1].Ref, Importance: 20));
        var duel = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = rival.Ref, ["defender"] = knight.Ref }, Place: world.Settlements[1].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = "to first blood" }, Importance: 40));
        log.Append(world.Date, new EventDraft("Gossip", Actors: new Dictionary<string, EntityRef> { ["subject"] = knight.Ref }, Importance: 5));
        knight.Memories.Add(new Memory(duel.Id, rival.Id, -90, 90));

        var page = CharacterScreen.Build(world, log, knight.Id, followed: true);

        Assert.Equal($"Knight of {world.Realms[1].Name} · age 27 · born Year {born.Date.Year} in {world.Settlements[1].Name}", page.Line);
        Assert.Equal(new[] { Traits.Brave, Traits.Wrathful }, page.Traits);
        Assert.Equal("Ambition: renown", page.Ambition);
        Assert.Equal($"Now at {world.Settlements[1].Name}", page.Location!.Label);
        Assert.Equal(new[] { born.Id, duel.Id }, page.Timeline.Select(r => r.EventId));
        Assert.True(page.Timeline.Single(r => r.EventId == duel.Id).Highlight);
        Assert.Equal(1, page.Folded);
        Assert.Contains(page.Ties, t => t.Relation == "Liege" && t.Ref == world.Characters[world.Settlements[1].LordId].Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Father" && t.Ref == father.Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Rival" && t.Ref == rival.Ref && t.Tone == Tone.Hostile);
        Assert.True(page.Followed);

        var tree = FamilyTree.Build(world, knight.Id);
        Assert.Contains(tree.Edges, e => e.From == father.Ref && e.To == knight.Ref && e.Kind == "parent");
    }

    [Fact]
    public void EveryCharacterOfARealHistory_HasAPage()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var nameOf = Names.Of(world);
        foreach (var c in world.Characters.Values)
        {
            var page = CharacterScreen.Build(world, sim.Log, c.Id);
            Assert.All(page.Ties, t => Assert.NotNull(nameOf(t.Ref)));
            foreach (var row in page.Timeline)
                Assert.All(row.Text.Links, l => Assert.Equal(nameOf(l.Entity), row.Text.Text.Substring(l.Start, l.Length)));
            FamilyTree.Build(world, c.Id);
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'CharacterScreen' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Views/CharacterScreen.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum Tone { Warm, Neutral, Hostile }

public sealed record Place(EntityRef Ref, string Label, MapPoint? Position);

public sealed record TimelineRow(int Year, LinkedText Text, long EventId, bool Highlight);

public sealed record Tie(EntityRef Ref, string Name, string Relation, string Qualifier, Tone Tone);

/// <summary>
/// The *Character page* (sub-project 2 §3.3): who they are, their traits and ambition, where they are, the timeline of
/// their life (minor events folded) and their ties — family, liege, and the people they love or hate most.
/// </summary>
public sealed record CharacterScreen(
    EntityRef Ref, string Name, EntityRef? House, string Kicker, string Line, IReadOnlyList<string> Traits, string Ambition,
    Place? Location, IReadOnlyList<TimelineRow> Timeline, int Folded, int Earlier, IReadOnlyList<Tie> Ties, bool Followed)
{
    public const int TimelineImportance = 20;

    /// <summary>A long life shows its most recent rows; the rest are counted in <c>Earlier</c>.</summary>
    public const int MaxTimeline = 150;

    public static CharacterScreen Build(World world, EventLog log, long characterId, bool followed = false)
    {
        var c = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var nameOf = Names.Of(world);
        var narrator = new TemplateNarrator(world);
        var house = world.Houses.GetValueOrDefault(c.HouseId);
        var life = log.Involving(c.Ref);
        var born = life.FirstOrDefault(e => e.Type == "CharacterBorn" && e.Actors.GetValueOrDefault("child") == c.Ref);
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);

        var line = new List<string> { RoleLine(world, c) };
        var today = c.Died ?? world.Date;
        line.Add($"age {c.Age(today)}");
        var birthYear = new Core.WorldDate(c.BirthDay).Year;
        line.Add(born?.Place is { } birthplace && nameOf(birthplace) is { } bp
            ? $"born Year {birthYear} in {bp}"
            : birthYear < 1 ? "born before recorded history" : $"born Year {birthYear}");
        if (c.Died is { } died) line.Add($"died Year {died.Year}{(c.DeathCause is { } cause ? $" of {cause}" : "")}");

        var notable = life.Where(e => e.Importance >= TimelineImportance).ToList();
        var highlight = c.IsAlive
            ? notable.Where(e => world.Date.Day - e.Date.Day < Core.WorldDate.DaysPerYear).OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault()
            : notable.OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault();
        var earlier = Math.Max(0, notable.Count - MaxTimeline);
        var timeline = notable.Skip(earlier).Select(e => new TimelineRow(e.Date.Year,
            LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf), e.Id, e.Id == highlight?.Id)).ToList();

        Place? location = home is null ? null
            : new Place(home.Ref, c.IsActive ? $"Now at {home.Name}" : $"Last at {home.Name}", MapPoint.Of(world, home.Ref));
        return new CharacterScreen(c.Ref, Names.Full(world, c), house?.Ref, house is null ? "Character" : $"Character · House {house.Name}",
            string.Join(" · ", line), c.Traits.ToList(), AmbitionText(world, c), location, timeline, life.Count - notable.Count, earlier,
            TiesOf(world, c), followed);
    }

    private static string RoleLine(World world, Character c)
    {
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);
        var realm = home is null ? null : Realms.SovereignOf(world, home);
        var role = c.Role switch
        {
            Role.Lord when home is not null => $"Lord of {home.Name}",
            Role.Knight when realm is not null => $"Knight of {realm.Name}",
            Role.None => home is null ? "Commoner" : $"Of {home.Name}",
            _ => home is null ? c.Role.ToString() : $"{c.Role} of {home.Name}",
        };
        return c.IsAlive || c.Role == Role.None ? role : "Once " + char.ToLowerInvariant(role[0]) + role[1..];
    }

    private static string AmbitionText(World world, Character c) => c.Ambition switch
    {
        Ambitions.Renown => "Ambition: renown",
        Ambitions.Artifact => "Ambition: a great artifact",
        Ambitions.Magic => "Ambition: mastery of magic",
        Ambitions.Wealth => "Ambition: wealth",
        Ambitions.Vengeance when world.Characters.TryGetValue(c.AmbitionTarget, out var foe) => $"Ambition: vengeance on {Names.Full(world, foe)}",
        Ambitions.Vengeance => "Ambition: vengeance",
        _ => "Ambition: a family of their own",
    };

    private static List<Tie> TiesOf(World world, Character c)
    {
        var ties = new List<Tie>();
        void Add(Character? other, string relation, string qualifier, Tone tone)
        {
            if (other is null || other.Id == c.Id || ties.Any(t => t.Ref == other.Ref)) return;
            var q = other.Died is { } d ? (qualifier.Length > 0 ? $"{qualifier}, died Year {d.Year}" : $"died Year {d.Year}") : qualifier;
            ties.Add(new Tie(other.Ref, Names.Full(world, other), relation, q, tone));
        }
        Tone ToneOf(Character other) => Relations.Opinion(c, other) is var o && o >= Relations.FriendAt ? Tone.Warm : o <= Relations.RivalAt ? Tone.Hostile : Tone.Neutral;
        string Feeling(Character other) => ToneOf(other) switch { Tone.Warm => "loyal", Tone.Hostile => "resentful", _ => "" };

        // Only the living have a liege today; the dead's lords are in their timeline.
        if (c.IsActive && world.Settlements.TryGetValue(c.SettlementId, out var home) && Realms.SovereignOf(world, home) is { } realm
            && Realms.RulerOf(world, realm) is { } liege)
            Add(liege, "Liege", Feeling(liege), ToneOf(liege));
        Add(world.Characters.GetValueOrDefault(c.SpouseId), "Spouse", "", Tone.Warm);
        Add(world.Characters.GetValueOrDefault(c.FatherId), "Father", "", Tone.Neutral);
        Add(world.Characters.GetValueOrDefault(c.MotherId), "Mother", "", Tone.Neutral);
        foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>())
            Add(child, "Child", "", Tone.Neutral);
        foreach (var other in c.Memories.Select(m => m.About).Distinct().Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()
                     .Select(o => (Other: o, Opinion: Relations.Opinion(c, o)))
                     .Where(x => x.Opinion >= Relations.FriendAt || x.Opinion <= Relations.RivalAt)
                     .OrderByDescending(x => Math.Abs(x.Opinion)).ThenBy(x => x.Other.Id).Take(3))
            Add(other.Other, other.Opinion > 0 ? "Friend" : "Rival", "", other.Opinion > 0 ? Tone.Warm : Tone.Hostile);
        return ties;
    }
}

public sealed record FamilyNode(EntityRef Ref, string Name, int Born, int? Died, EntityRef? House);

public sealed record FamilyEdge(EntityRef From, EntityRef To, string Kind);

/// <summary>A character's family over a few generations up and down: nodes, parent and spouse edges.</summary>
public sealed record FamilyTree(EntityRef Root, IReadOnlyList<FamilyNode> Nodes, IReadOnlyList<FamilyEdge> Edges)
{
    public static FamilyTree Build(World world, long characterId, int generations = 2)
    {
        var root = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var people = new SortedDictionary<long, Character>();
        void Up(Character c, int depth)
        {
            people[c.Id] = c;
            if (depth == 0) return;
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Up(parent, depth - 1);
        }
        void Down(Character c, int depth)
        {
            people[c.Id] = c;
            if (world.Characters.TryGetValue(c.SpouseId, out var spouse)) people[spouse.Id] = spouse;
            if (depth == 0) return;
            foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Down(child, depth - 1);
        }
        Up(root, generations);
        Down(root, generations);
        var nodes = people.Values.Select(c => new FamilyNode(c.Ref, Names.Full(world, c), new Core.WorldDate(c.BirthDay).Year, c.Died?.Year,
            world.Houses.TryGetValue(c.HouseId, out var h) ? h.Ref : null)).ToList();
        var edges = new List<FamilyEdge>();
        foreach (var c in people.Values)
        {
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Where(people.ContainsKey))
                edges.Add(new FamilyEdge(people[parent].Ref, c.Ref, "parent"));
            if (people.TryGetValue(c.SpouseId, out var spouse) && c.Id < spouse.Id) edges.Add(new FamilyEdge(c.Ref, spouse.Ref, "spouse"));
        }
        return new FamilyTree(root.Ref, nodes, edges);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CharacterScreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 2`. Then `dotnet test sim` → `Passed: 465` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): add the character screen and family tree

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Why screen

Spec §3.4. The causes of an event as a layered DAG: the outcome card, then layers ranked by distance from it (merged branches appear once), edges labelled "because" or "because, and" by kind, root cards marked, cards with further causes beyond the depth marked `HasMore`. A summary sentence says how far back it reaches and through how many roots, and a side panel gathers the people, places and systems in the chain.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/WhyScreen.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/WhyScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `HistoryQuery.Why`, `CauseKind` (Plans 1–6).
- Produces: `record EventCard(EventId, Year, Category, Title, Note, IsRoot, HasMore)`, `CauseEdge(From, To, Kind, Label)`, `Mention(Ref, Name)`; `record WhyScreen { Build(world, log, eventId, depth = 4) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/WhyScreenTests.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

public class WhyScreenTests
{
    [Fact]
    public void TheWhyScreen_LayersCausesOnce_AndLabelsJointCauses()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ruler = TestPeople.Add(world, 1, 40);
        var drought = log.Append(new FantasyWorld.Sim.Core.WorldDate(10), new EventDraft("Drought", Place: world.Regions[1].Ref, Tags: new[] { "Regions" }));
        var shortage = log.Append(new FantasyWorld.Sim.Core.WorldDate(400), new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref,
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 1) }, Tags: new[] { "Economy" }));
        var insult = log.Append(new FantasyWorld.Sim.Core.WorldDate(500), new EventDraft("Insult", Actors: new Dictionary<string, EntityRef> { ["by"] = ruler.Ref },
            Payload: new Dictionary<string, string> { ["motive"] = "pride" }));
        var war = log.Append(new FantasyWorld.Sim.Core.WorldDate(800), new EventDraft("WarDeclared", Actors: new Dictionary<string, EntityRef> { ["attacker"] = ruler.Ref },
            Causes: new[] { new Cause(insult.Id, CauseKind.Motive, 0.9), new Cause(shortage.Id, CauseKind.Enabler, 0.5) }, Tags: new[] { "War" }));
        var peace = log.Append(new FantasyWorld.Sim.Core.WorldDate(900), new EventDraft("PeaceSigned", Causes: new[] { new Cause(war.Id, CauseKind.Trigger, 1) }));

        var why = WhyScreen.Build(world, log, war.Id);

        Assert.Equal(war.Id, why.Outcome.EventId);
        Assert.Equal(new[] { new[] { shortage.Id, insult.Id }, new[] { drought.Id } }, why.Layers.Select(l => l.Select(c => c.EventId).ToArray()));
        Assert.All(why.Edges.Where(e => e.To == war.Id), e => Assert.Equal("because, and", e.Label));
        Assert.Equal("because", why.Edges.Single(e => e.To == shortage.Id).Label);
        Assert.True(why.Layers[1].Single().IsRoot);
        Assert.Equal("pride", why.Layers[0].Single(c => c.EventId == insult.Id).Note);
        Assert.Equal("Traced back two years, through two root causes.", why.Summary);
        Assert.Equal(new[] { ruler.Ref }, why.People.Select(p => p.Ref));
        Assert.Equal(new[] { world.Regions[1].Ref }, why.Places.Select(p => p.Ref));
        Assert.Equal(new[] { Category.War, Category.Economy, Category.Characters, Category.Nature }.OrderBy(c => c), why.Systems);
        Assert.Equal(new[] { peace.Id }, why.Consequences.Select(c => c.EventId));

        var shallow = WhyScreen.Build(world, log, war.Id, depth: 1);
        Assert.True(shallow.Layers[0].Single(c => c.EventId == shortage.Id).HasMore);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'WhyScreen' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Views/WhyScreen.cs`:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Views;

/// <summary>An event as a card: what happened, when, its system, the engine's note on why or how, and whether it is a root.</summary>
public sealed record EventCard(long EventId, int Year, Category Category, LinkedText Title, string? Note, bool IsRoot, bool HasMore);

public sealed record CauseEdge(long From, long To, CauseKind Kind, string Label);

public sealed record Mention(EntityRef Ref, string Name);

/// <summary>
/// The *Why chain* screen (sub-project 2 §3.4): the outcome and its causes as a layered graph (each event once, at its
/// shortest distance from the outcome), edges labelled "because" or "because, and" when an event had several causes,
/// dashed roots, and the people, places and systems involved. Consequences are the "trace further" direction.
/// </summary>
public sealed record WhyScreen(
    EventCard Outcome, IReadOnlyList<IReadOnlyList<EventCard>> Layers, IReadOnlyList<CauseEdge> Edges, string Summary,
    IReadOnlyList<Mention> People, IReadOnlyList<Mention> Places, IReadOnlyList<Category> Systems, IReadOnlyList<EventCard> Consequences)
{
    public static WhyScreen Build(World world, EventLog log, long eventId, int depth = 4)
    {
        var outcome = log.TryGet(eventId, out var found) ? found : throw new KeyNotFoundException($"No event #{eventId}.");
        var nameOf = Names.Of(world);
        var narrator = new TemplateNarrator(world);
        EventCard Card(WorldEvent ev, bool hasMore) => new(ev.Id, ev.Date.Year, Categories.Of(ev),
            LinkedText.Link(narrator.Sentence(ev), LinkedText.Mentions(ev), nameOf),
            ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("lore"),
            ev.Causes.Count == 0, hasMore);

        var distance = new Dictionary<long, int> { [outcome.Id] = 0 };
        var frontier = new List<WorldEvent> { outcome };
        var edges = new List<CauseEdge>();
        for (var level = 1; level <= depth && frontier.Count > 0; level++)
        {
            var next = new List<WorldEvent>();
            foreach (var ev in frontier)
                foreach (var cause in ev.Causes)
                {
                    if (!log.TryGet(cause.EventId, out var c)) continue;
                    edges.Add(new CauseEdge(c.Id, ev.Id, cause.Kind, ev.Causes.Count > 1 ? "because, and" : "because"));
                    if (distance.TryAdd(c.Id, level)) next.Add(c);
                }
            frontier = next;
        }
        var chain = distance.Keys.Select(log.Get).ToList();
        var layers = distance.Where(kv => kv.Value > 0).GroupBy(kv => kv.Value).OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<EventCard>)g.Select(kv => log.Get(kv.Key)).OrderBy(e => e.Id)
                .Select(e => Card(e, e.Causes.Any(c => !distance.ContainsKey(c.EventId)))).ToList())
            .ToList();

        var roots = chain.Count(e => e.Causes.Count == 0 && e.Id != outcome.Id);
        var years = outcome.Date.Year - chain.Min(e => e.Date.Year);
        var summary = chain.Count == 1
            ? "Nothing recorded led to this."
            : $"Traced back {(years == 0 ? "within the year" : years == 1 ? "one year" : $"{Words(years)} years")}, "
              + (roots == 0 ? "and further than this view goes." : $"through {(roots == 1 ? "one root cause" : $"{Words(roots)} root causes")}.");
        var mentions = chain.SelectMany(LinkedText.Mentions).Distinct().Select(r => new Mention(r, nameOf(r) ?? r.ToString())).ToList();
        var consequences = log.EffectsOf(outcome.Id).Select(log.Get).Select(e => Card(e, false)).ToList();
        return new WhyScreen(Card(outcome, outcome.Causes.Any(c => !distance.ContainsKey(c.EventId))), layers, edges, summary,
            mentions.Where(m => m.Ref.Kind == Character.Kind).ToList(),
            mentions.Where(m => m.Ref.Kind is Settlement.Kind or Region.Kind).ToList(),
            chain.Select(Categories.Of).Distinct().OrderBy(c => c).ToList(), consequences);
    }

    private static readonly string[] Numbers = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    private static string Words(int n) => n < Numbers.Length ? Numbers[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~WhyScreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 1`. Then `dotnet test sim` → `Passed: 466` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): add the why-chain screen as a layered graph

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: History at a date

Spec §5: the map's history slider needs who held and occupied each region at any date. World generation now records the founding owners and lieges once, and the political map is replayed from them through the log: wars declared and joined (allies now record their side), regions ceded, occupied and liberated, sieges that took a region, peace (with the engine's own rule for what changes hands and vassalage terms), independence and fallen realms. At or after today, or for worlds made before founding was recorded, it reads the live state. The replay is checked to match the live state exactly on long real histories.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/History.cs`
- Modify: `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`, `sim/FantasyWorld.Sim/WorldGen/RealmGenerator.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/HistoryTests.cs`

**Interfaces:**
- Consumes: `World`, `EventLog`, `RealmGenerator`, `DiplomacySystem`, `WorldSnapshot` (Plans 1–6).
- Produces: `World.FoundingOwners`, `World.FoundingLieges` (snapshot fields); `AllyJoinedWar` payload `side`; `record RegionState(RegionId, OwnerRealmId, OccupierRealmId, OccupyingWarId)`, `WarSides`, `PoliticalMap(Historical, Regions, Lieges, Fallen, Wars) { TopOf(realm) }`; `static class History { PoliticalMapAt(world, log, asOf) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/HistoryTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class HistoryTests
{
    private sealed record Seen(WorldDate Date, Dictionary<long, (long Owner, long Occupier)> Regions, Dictionary<long, long> Lieges,
        HashSet<long> Fallen, Dictionary<long, (long[] Attackers, long[] Defenders)> Wars);

    [Theory]
    [InlineData(2UL, 140)]
    [InlineData(7UL, 60)]
    public void ThePoliticalWorld_ReplaysToWhatItWas(ulong seed, int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        Assert.Equal(world.Regions.Values.Where(r => r.OwnerRealmId != 0).Select(r => (r.Id, r.OwnerRealmId)),
            world.FoundingOwners.Select(kv => (kv.Key, kv.Value)));
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        var seen = new List<Seen>();
        for (var year = 0; year < years; year += 2)
        {
            sim.AdvanceDays(720);
            seen.Add(new Seen(world.Date,
                world.Regions.Values.ToDictionary(r => r.Id, r => (r.OwnerRealmId, r.OccupierRealmId)),
                world.Realms.Values.Where(r => !r.Destroyed).ToDictionary(r => r.Id, r => r.LiegeId),
                world.Realms.Values.Where(r => r.Destroyed).Select(r => r.Id).ToHashSet(),
                world.Wars.Values.Where(w => !w.Ended).ToDictionary(w => w.Id, w => (w.Attackers.ToArray(), w.Defenders.ToArray()))));
        }
        Assert.Contains(sim.Log.All, e => e.Type == "RegionCeded");
        if (seed == 2) Assert.Contains(sim.Log.All, e => e.Type == "RealmFell");

        foreach (var past in seen.Take(seen.Count - 1))
        {
            var map = History.PoliticalMapAt(world, sim.Log, past.Date);
            Assert.True(map.Historical);
            Assert.All(past.Regions, kv => Assert.Equal(kv.Value, (map.Regions[kv.Key].OwnerRealmId, map.Regions[kv.Key].OccupierRealmId)));
            Assert.All(past.Lieges, kv => Assert.Equal(kv.Value, map.Lieges[kv.Key]));
            Assert.Equal(past.Fallen.Order(), map.Fallen.Order());
            Assert.All(past.Wars, kv => Assert.Equal($"{string.Join(",", kv.Value.Attackers)} v {string.Join(",", kv.Value.Defenders)}",
                $"{string.Join(",", map.Wars[kv.Key].Attackers)} v {string.Join(",", map.Wars[kv.Key].Defenders)}"));
        }
    }

    [Fact]
    public void WorldsWithoutAFoundingRecord_AreNotReplayed()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new WorldDate(3600);
        var map = History.PoliticalMapAt(world, new EventLog(), new WorldDate(10));
        Assert.False(map.Historical);
        Assert.Equal(1, map.Regions[1].OwnerRealmId);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'World' does not contain a definition for 'FoundingOwners' and no accessible extension method 'FoundingOwners' accepting a first argument of type 'World' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

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
    SortedDictionary<long, Structure>? Structures = null,
    string? Name = null,
    SortedDictionary<long, long>? FoundingOwners = null,
    SortedDictionary<long, long>? FoundingLieges = null)
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
        world.Structures,
        world.Name,
        world.FoundingOwners,
        world.FoundingLieges);

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
        Name = Name ?? "",
        FoundingOwners = FoundingOwners is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingOwners),
        FoundingLieges = FoundingLieges is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingLieges),
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
                    Payload: new Dictionary<string, string> { ["side"] = side == war.Attackers ? "attackers" : "defenders" },
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

`sim/FantasyWorld.Sim/Views/History.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Views;

/// <summary>Who held and who occupied a region at some date, and for which war.</summary>
public sealed record RegionState(long RegionId, long OwnerRealmId, long OccupierRealmId, long OccupyingWarId = 0);

/// <summary>The sides of a war at some date.</summary>
public sealed record WarSides(IReadOnlyList<long> Attackers, IReadOnlyList<long> Defenders);

/// <summary>The political world at a date: regions, who is whose vassal, which realms have fallen, and the wars' sides.</summary>
public sealed record PoliticalMap(
    bool Historical, IReadOnlyDictionary<long, RegionState> Regions, IReadOnlyDictionary<long, long> Lieges,
    IReadOnlySet<long> Fallen, IReadOnlyDictionary<long, WarSides> Wars)
{
    /// <summary>The independent realm at the top of a realm's chain of lieges at that date.</summary>
    public long TopOf(long realm)
    {
        for (var guard = 0; guard < 64 && Lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }
}

/// <summary>
/// History at a date (sub-project 2 §5): the political world replayed from the founding baseline through cessions,
/// occupations, liberations, peaces (with their vassalage terms), declarations of independence, realms' falls and
/// allies joining wars. Today's answer is the live state. Worlds saved before the baseline existed cannot be replayed:
/// their map is the live state, marked not historical.
/// </summary>
public static class History
{
    public static PoliticalMap PoliticalMapAt(World world, EventLog log, WorldDate asOf)
    {
        if (asOf >= world.Date || world.FoundingOwners.Count == 0) return Live(world, historical: asOf >= world.Date);

        var owner = world.Regions.Keys.ToDictionary(id => id, id => world.FoundingOwners.GetValueOrDefault(id));
        var lieges = world.Realms.Keys.ToDictionary(id => id, id => world.FoundingLieges.GetValueOrDefault(id));
        var fallen = new HashSet<long>();
        var occupation = new Dictionary<long, (long Realm, long War)>();
        var sides = new Dictionary<long, (List<long> Attackers, List<long> Defenders)>();
        var over = new HashSet<long>();
        foreach (var ev in log.All)
        {
            if (ev.Date > asOf) break;
            var region = RegionOf(world, ev.Place);
            switch (ev.Type)
            {
                case "WarDeclared" when ev.Actors.TryGetValue("war", out var war):
                    sides[war.Id] = (new List<long> { ev.Actors["attacker"].Id }, new List<long> { ev.Actors["defender"].Id });
                    break;
                case "AllyJoinedWar" when ev.Actors.TryGetValue("war", out var joined) && sides.TryGetValue(joined.Id, out var s):
                    var ally = ev.Actors["ally"].Id;
                    var attacking = ev.Payload.TryGetValue("side", out var side)
                        ? side == "attackers"
                        : world.Wars.TryGetValue(joined.Id, out var known) && known.Attackers.Contains(ally);
                    (attacking ? s.Attackers : s.Defenders).Add(ally);
                    break;
                case "RegionCeded" when region != 0 && ev.Actors.TryGetValue("to", out var to):
                    owner[region] = to.Id;
                    occupation.Remove(region);
                    break;
                case "RegionOccupied" or "SiegeEnded" when region != 0 && ev.Actors.TryGetValue("realm", out var realm)
                                                           && ev.Actors.TryGetValue("war", out var at)
                                                           && (ev.Type == "RegionOccupied" || ev.Payload.GetValueOrDefault("outcome") == "fell"):
                    occupation[region] = (realm.Id, at.Id);
                    break;
                case "RegionLiberated" when region != 0:
                    occupation.Remove(region);
                    break;
                case "PeaceSigned" when ev.Actors.TryGetValue("war", out var ended):
                    // As MilitarySystem.MakePeace: the war's occupations end, unless the occupier still fights the owner in another war.
                    over.Add(ended.Id);
                    var involved = sides.TryGetValue(ended.Id, out var e) ? e.Attackers.Concat(e.Defenders).ToHashSet() : new HashSet<long>();
                    foreach (var (held, occ) in occupation.ToList())
                    {
                        var holder = Top(lieges, owner[held]);
                        if (involved.Contains(occ.Realm) && (holder == 0 || involved.Contains(holder) || !AtWar(sides, over, occ.Realm, holder)))
                            occupation.Remove(held);
                    }
                    if (ev.Payload.GetValueOrDefault("terms") is { } terms && terms.Contains("vassalage", StringComparison.Ordinal)
                        && ev.Actors.TryGetValue("vanquished", out var vassal) && ev.Actors.TryGetValue("victor", out var overlord)
                        && !fallen.Contains(vassal.Id))
                        lieges[vassal.Id] = overlord.Id;
                    break;
                case "IndependenceDeclared" when ev.Actors.TryGetValue("realm", out var free):
                    lieges[free.Id] = 0;
                    break;
                case "RealmFell" when ev.Actors.TryGetValue("realm", out var gone):
                    fallen.Add(gone.Id);
                    foreach (var id in owner.Where(o => o.Value == gone.Id).Select(o => o.Key).ToList()) owner[id] = 0;
                    foreach (var id in occupation.Where(o => o.Value.Realm == gone.Id).Select(o => o.Key).ToList()) occupation.Remove(id);
                    foreach (var id in lieges.Where(l => l.Value == gone.Id).Select(l => l.Key).ToList()) lieges[id] = 0;
                    foreach (var (attackers, defenders) in sides.Values)
                    {
                        attackers.Remove(gone.Id);
                        defenders.Remove(gone.Id);
                    }
                    break;
            }
        }
        var regions = owner.ToDictionary(o => o.Key, o => occupation.TryGetValue(o.Key, out var occ)
            ? new RegionState(o.Key, o.Value, occ.Realm, occ.War) : new RegionState(o.Key, o.Value, 0));
        return new PoliticalMap(true, regions, lieges, fallen, sides.ToDictionary(s => s.Key, s => new WarSides(s.Value.Attackers, s.Value.Defenders)));
    }

    private static PoliticalMap Live(World world, bool historical) => new(historical,
        world.Regions.Values.ToDictionary(r => r.Id, r => new RegionState(r.Id, r.OwnerRealmId, r.OccupierRealmId,
            r.OccupierRealmId == 0 ? 0 : world.Wars.Values.Where(w => !w.Ended && w.Involves(r.OccupierRealmId)).Select(w => w.Id).FirstOrDefault())),
        world.Realms.Values.ToDictionary(r => r.Id, r => r.LiegeId),
        world.Realms.Values.Where(r => r.Destroyed).Select(r => r.Id).ToHashSet(),
        world.Wars.Values.ToDictionary(w => w.Id, w => new WarSides(w.Attackers.ToList(), w.Defenders.ToList())));

    private static long Top(Dictionary<long, long> lieges, long realm)
    {
        for (var guard = 0; guard < 64 && realm != 0 && lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }

    private static bool AtWar(Dictionary<long, (List<long> Attackers, List<long> Defenders)> sides, HashSet<long> over, long a, long b) =>
        sides.Any(w => !over.Contains(w.Key) && (w.Value.Attackers.Contains(a) && w.Value.Defenders.Contains(b) || w.Value.Attackers.Contains(b) && w.Value.Defenders.Contains(a)));

    private static long RegionOf(World world, EntityRef? place) => place switch
    {
        { Kind: Region.Kind } p => p.Id,
        { Kind: Settlement.Kind } p when world.Settlements.TryGetValue(p.Id, out var s) => s.RegionId,
        _ => 0,
    };
}
```

Replace `sim/FantasyWorld.Sim/WorldGen/RealmGenerator.cs` entirely with:
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

        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId != 0))
            world.FoundingOwners[region.Id] = region.OwnerRealmId;
        foreach (var realm in world.Realms.Values) world.FoundingLieges[realm.Id] = realm.LiegeId;
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

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~HistoryTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 469` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): replay the political map at any past date

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Map screen

Spec §3.2. Regions with biome, centre, owner, occupier and the realm's colour; settlements as pins with a marker (conflict, recent event, realm seat, plain) and a hover card ("Held by the …", population and this year's events); landmarks and labyrinths; borders between differently held regions; wars ongoing at the date with their sides and occupied regions; and a legend. Everything political comes from Task 5's map, so a past date shows the past.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/MapScreen.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/MapScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5; `Structure`, `Settlement`, `Region` (Plans 1–6).
- Produces: `enum PinMarker { Plain, Realm, RecentEvent, Conflict }`; `record MapRegion`, `MapCard`, `MapSettlement`, `MapStructure`, `Border`, `Conflict`, `LegendEntry`; `record MapScreen { Build(world, log, asOf = null) }` (with `Historical`).

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/MapScreenTests.cs`:
```csharp
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
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'MapScreen' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Views/MapScreen.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum PinMarker { Plain, Realm, RecentEvent, Conflict }

public sealed record MapRegion(EntityRef Ref, string Name, string Biome, MapPoint? Center, EntityRef? Owner, EntityRef? Occupier, string Colour);

public sealed record MapCard(string Kicker, string Title, string Line);

public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card);

public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend)
{
    public static MapScreen Build(World world, EventLog log, WorldDate? asOf = null)
    {
        var requested = asOf is { } d && d < world.Date ? d : world.Date;
        var map = History.PoliticalMapAt(world, log, requested);
        var date = map.Historical ? requested : world.Date;
        var today = date == world.Date;
        var political = map.Regions;
        EntityRef? RealmRef(long id) => id != 0 && world.Realms.TryGetValue(id, out var r) ? r.Ref : null;
        string Colour(long id) => id != 0 && world.Realms.TryGetValue(map.TopOf(id), out var top) ? top.Colors[0] : "none";

        var regions = world.Regions.Values.Select(r => new MapRegion(r.Ref, r.Name, r.Biome.ToString(), MapPoint.Of(world, r.Ref),
            RealmRef(political[r.Id].OwnerRealmId), RealmRef(political[r.Id].OccupierRealmId), Colour(political[r.Id].OwnerRealmId))).ToList();

        var abandoned = log.All.Where(e => e.Type == "SettlementAbandoned" && e.Date <= date && e.Place is { Kind: Settlement.Kind })
            .Select(e => e.Place!.Value.Id).ToHashSet();
        var season = date.Day - 90;
        var settlements = new List<MapSettlement>();
        foreach (var s in world.Settlements.Values.Where(s => !abandoned.Contains(s.Id)))
        {
            var state = political[s.RegionId];
            var here = log.Involving(s.Ref).Where(e => e.Date <= date).ToList();
            var marker = state.OccupierRealmId != 0 ? PinMarker.Conflict
                : here.Any(e => e.Date.Day > season && e.Importance >= 30) ? PinMarker.RecentEvent
                : state.OwnerRealmId != 0 ? PinMarker.Realm : PinMarker.Plain;
            var holder = world.Realms.GetValueOrDefault(state.OwnerRealmId);
            var ceded = log.Involving(world.Regions[s.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded" && e.Date <= date);
            var title = holder is null ? "Held by no realm"
                : $"Held by the {Statecraft.Title(holder)} {(ceded is null ? "since the founding" : $"since Year {ceded.Date.Year}")}";
            if (state.OccupierRealmId != 0 && world.Realms.TryGetValue(state.OccupierRealmId, out var occupier))
                title += $", occupied by {occupier.Name}";
            var thisYear = here.Count(e => e.Date.Year == date.Year);
            var line = (today ? $"Pop. {s.Population.ToString("N0", CultureInfo.InvariantCulture)} · " : "")
                       + $"{thisYear} event{(thisYear == 1 ? "" : "s")} this year";
            settlements.Add(new MapSettlement(s.Ref, s.Name, s.Tier.ToString(), MapPoint.Of(world, s.Ref), holder?.Ref, marker,
                new MapCard($"{s.Name} · {s.Tier}", title, line)));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
            .ToList();

        var borders = world.Geography is { } geo
            ? geo.Graph.Links.Where(l => political[l.A].OwnerRealmId != political[l.B].OwnerRealmId)
                .Select(l => new Border(world.Regions[Math.Min(l.A, l.B)].Ref, world.Regions[Math.Max(l.A, l.B)].Ref)).Distinct().ToList()
            : new List<Border>();

        var conflicts = world.Wars.Values.Where(w => w.StartDay <= date.Day && (!w.Ended || log.TryGet(w.EndedEventId, out var end) && end.Date > date))
            .Select(w => (War: w, Sides: map.Wars.GetValueOrDefault(w.Id) ?? new WarSides(w.Attackers, w.Defenders)))
            .Select(x => new Conflict(x.War.Ref, x.War.Name, x.Sides.Attackers.Select(id => world.Realms[id].Ref).ToList(),
                x.Sides.Defenders.Select(id => world.Realms[id].Ref).ToList(),
                political.Values.Where(p => p.OccupyingWarId == x.War.Id).Select(p => world.Regions[p.RegionId].Ref).ToList()))
            .ToList();

        var independent = political.Values.Select(p => p.OwnerRealmId).Where(id => id != 0).Select(map.TopOf).Distinct()
            .Select(id => world.Realms[id]).OrderBy(r => r.Id).ToList();
        var legend = independent.Select(r => new LegendEntry(r.Name, r.Colors[0]))
            .Append(new LegendEntry("Active conflict", "conflict")).Append(new LegendEntry("Event this season", "event")).ToList();
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend);
    }

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type == StructureKind.Landmark && !(st.CompletedEventId != 0 && log.TryGet(st.CompletedEventId, out var done) && done.Date <= date))
            return st.State == Model.StructureState.Ruined && RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Building);
        if (st.Type == StructureKind.Landmark && st.State == Model.StructureState.Ruined)
            return RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Standing);
        return st.State.ToString();
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~MapScreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 472` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): add the diorama map screen

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Chronicle, realm and artifact screens

Spec §3.5. The full chronicle (the digest's "Full chronicle" toggle) as events by season with category and importance, filtered by years and category and paged; a realm page with ruler, laws, council, vassals, treaties, wars and eras; an artifact page with powers, curse (only once revealed), lore and provenance as a timeline of holders.

**Files:**
- Create: `sim/FantasyWorld.Sim/Views/ChronicleScreens.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/ChronicleScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; `Realm`, `Artifact`, `Eras` (Plans 1–6).
- Produces: `record ChronicleRow`, `ChronicleSeason`, `ChronicleScreen { PageSize = 50; Build(world, log, fromYear, toYear, only, minImportance, page) }`; `record CouncilSeat`, `TreatyRow`, `WarRow`, `EraRow`, `RealmScreen { Build(world, log, realmId) }`; `record ProvenanceRow`, `ArtifactScreen { Build(world, log, artifactId) }`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/ChronicleScreenTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class ChronicleScreenTests
{
    private static Simulator History(int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * years);
        return sim;
    }

    [Fact]
    public void TheChronicle_IsNewestFirst_FilteredAndPaged()
    {
        var sim = History(12);
        var all = ChronicleScreen.Build(sim.World, sim.Log);
        Assert.Equal((4, 13), (all.FromYear, all.ToYear));
        Assert.Equal(sim.Log.All.Count(e => e.Date.Year >= 4), all.Total);
        var ids = all.Seasons.SelectMany(s => s.Rows).Select(r => r.EventId).ToList();
        Assert.Equal(ChronicleScreen.PageSize, ids.Count);
        Assert.Equal(ids.OrderByDescending(id => id), ids);
        Assert.Equal(all.Pages, ChronicleScreen.Build(sim.World, sim.Log, page: 999).Page);

        var wars = ChronicleScreen.Build(sim.World, sim.Log, 1, 13, Category.War, minImportance: 40);
        Assert.All(wars.Seasons.SelectMany(s => s.Rows), r => Assert.True(r.Category == Category.War && r.Importance >= 40));
    }

    [Fact]
    public void EveryRealmAndArtifact_HasAPage_WhoseNamesResolve()
    {
        var sim = History(30);
        var world = sim.World;
        var nameOf = Names.Of(world);
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed))
        {
            var page = RealmScreen.Build(world, sim.Log, realm.Id);
            Assert.Contains(realm.Name, page.Title);
            Assert.All(page.Vassals.Concat(page.Council.Select(c => c.Member)).Concat(page.Treaties.Select(t => t.With)),
                m => Assert.Equal(nameOf(m.Ref), m.Name));
            Assert.All(page.Eras, e => Assert.True(sim.Log.TryGet(e.EventId, out _)));
        }
        Assert.Contains(world.Realms.Values, r => RealmScreen.Build(world, sim.Log, r.Id).Wars.Count > 0);
        foreach (var artifact in world.Artifacts.Values)
        {
            var page = ArtifactScreen.Build(world, sim.Log, artifact.Id);
            Assert.NotEmpty(page.Powers);
            Assert.Equal(artifact.Provenance.Count, page.Provenance.Count);
            Assert.All(page.Made.Links, l => Assert.Equal(nameOf(l.Entity), page.Made.Text.Substring(l.Start, l.Length)));
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'ChronicleScreen' does not exist in the current context`.

- [x] **Step 3: Implement**

`sim/FantasyWorld.Sim/Views/ChronicleScreens.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public sealed record ChronicleRow(long EventId, Category Category, int Importance, LinkedText Text);

public sealed record ChronicleSeason(string Label, IReadOnlyList<ChronicleRow> Rows);

/// <summary>The "Full chronicle" view (sub-project 2 §3.5): every event in a span, newest season first, paged.</summary>
public sealed record ChronicleScreen(int FromYear, int ToYear, int Total, int Page, int Pages, IReadOnlyList<ChronicleSeason> Seasons)
{
    public const int PageSize = 50;

    public static ChronicleScreen Build(World world, EventLog log, int? fromYear = null, int? toYear = null,
        Category? only = null, int minImportance = 0, int page = 1)
    {
        var to = toYear ?? world.Date.Year;
        var from = fromYear ?? Math.Max(1, to - 9);
        var narrator = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        var events = log.All.Where(e => e.Date.Year >= from && e.Date.Year <= to && e.Importance >= minImportance
                                        && (only is null || Categories.Of(e) == only)).Reverse().ToList();
        var pages = Math.Max(1, (events.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        var seasons = events.Skip((page - 1) * PageSize).Take(PageSize)
            .GroupBy(e => Digest.SeasonLabel(e.Date))
            .Select(g => new ChronicleSeason(g.Key, g.Select(e => new ChronicleRow(e.Id, Categories.Of(e), e.Importance,
                LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf))).ToList()))
            .ToList();
        return new ChronicleScreen(from, to, events.Count, page, pages, seasons);
    }
}

public sealed record CouncilSeat(string Seat, Mention Member);

public sealed record TreatyRow(string Kind, Mention With, int SinceYear);

public sealed record WarRow(EntityRef War, string Name, string Side, bool Ended);

public sealed record EraRow(int Year, string Name, string Phase, long EventId);

/// <summary>A realm's page (sub-project 2 §3.5): who rules and by what law, its council, vassals, treaties, wars and eras.</summary>
public sealed record RealmScreen(
    EntityRef Ref, string Title, string Rank, string Government, string SuccessionLaw, string MagicLaw, Mention? Ruler, Place Capital,
    int Legitimacy, int Stability, int Treasury, string Phase, int Regions, Mention? Liege, IReadOnlyList<Mention> Vassals,
    IReadOnlyList<CouncilSeat> Council, IReadOnlyList<TreatyRow> Treaties, IReadOnlyList<WarRow> Wars, IReadOnlyList<EraRow> Eras)
{
    public static RealmScreen Build(World world, EventLog log, long realmId)
    {
        var realm = world.Realms.TryGetValue(realmId, out var found) ? found : throw new KeyNotFoundException($"No realm #{realmId}.");
        Mention Of(EntityRef r) => new(r, Names.Of(world)(r) ?? r.ToString());
        var capital = world.Settlements[realm.CapitalId];
        return new RealmScreen(realm.Ref, Statecraft.Title(realm), realm.Rank.ToString(), Words(realm.Government.ToString()),
            realm.SuccessionLaw.ToString(), realm.MagicLaw.ToString(),
            Realms.RulerOf(world, realm) is { } ruler ? Of(ruler.Ref) : null,
            new Place(capital.Ref, capital.Name, MapPoint.Of(world, capital.Ref)),
            (int)Math.Round(realm.Legitimacy), (int)Math.Round(realm.Stability), (int)Math.Round(realm.Treasury), realm.Phase.ToString(),
            Realms.Domain(world, realm).Count(),
            realm.LiegeId != 0 && world.Realms.TryGetValue(realm.LiegeId, out var liege) ? Of(liege.Ref) : null,
            Realms.VassalsOf(world, realm).Select(v => Of(v.Ref)).ToList(),
            realm.Council.Where(kv => world.Characters.ContainsKey(kv.Value)).Select(kv => new CouncilSeat(kv.Key, Of(world.Characters[kv.Value].Ref))).ToList(),
            realm.Treaties.Where(t => world.Realms.ContainsKey(t.With)).Select(t => new TreatyRow(t.Kind.ToString(), Of(world.Realms[t.With].Ref),
                log.TryGet(t.SinceEventId, out var since) ? since.Date.Year : 1)).ToList(),
            world.Wars.Values.Where(w => w.Involves(realm.Id)).OrderByDescending(w => w.Id)
                .Select(w => new WarRow(w.Ref, w.Name, w.IsAttacker(realm.Id) ? "attacker" : "defender", w.Ended)).ToList(),
            new HistoryQuery(log).Eras(realm.Ref).Select(e => new EraRow(e.Date.Year, e.Payload["era"], e.Payload.GetValueOrDefault("phase") ?? "", e.Id)).ToList());
    }

    private static string Words(string pascal) => string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString()));
}

public sealed record ProvenanceRow(int Year, string How, Mention? From, Mention? To, long EventId);

/// <summary>An artifact's page (sub-project 2 §3.5): powers, a revealed curse, lore, where it is, how it was made and every change of hands.</summary>
public sealed record ArtifactScreen(
    EntityRef Ref, string Name, string Kind, string Material, int Fame, IReadOnlyList<string> Powers, string? Curse, string Lore,
    string Whereabouts, Place? Location, LinkedText Made, IReadOnlyList<ProvenanceRow> Provenance)
{
    public static ArtifactScreen Build(World world, EventLog log, long artifactId)
    {
        var x = world.Artifacts.TryGetValue(artifactId, out var found) ? found : throw new KeyNotFoundException($"No artifact #{artifactId}.");
        var nameOf = Names.Of(world);
        Mention? Of(long characterId) => world.Characters.TryGetValue(characterId, out var c) ? new Mention(c.Ref, Names.Full(world, c)) : null;
        var (whereabouts, location) = x.Place switch
        {
            ArtifactPlace.Held when world.Characters.TryGetValue(x.HolderId, out var holder) =>
                ($"Held by {Names.Full(world, holder)}", world.Settlements.TryGetValue(holder.SettlementId, out var s) ? new Place(s.Ref, s.Name, MapPoint.Of(world, s.Ref)) : null),
            ArtifactPlace.Treasury => ($"In the treasury of {world.Settlements[x.SettlementId].Name}",
                new Place(world.Settlements[x.SettlementId].Ref, world.Settlements[x.SettlementId].Name, MapPoint.Of(world, world.Settlements[x.SettlementId].Ref))),
            ArtifactPlace.Lost => ($"Lost in {world.Regions[x.RegionId].Name}",
                new Place(world.Regions[x.RegionId].Ref, world.Regions[x.RegionId].Name, MapPoint.Of(world, world.Regions[x.RegionId].Ref))),
            _ => ("Destroyed", (Place?)null),
        };
        var made = x.OriginEventId != 0 && log.TryGet(x.OriginEventId, out var origin)
            ? LinkedText.Link($"Year {origin.Date.Year}: {new TemplateNarrator(world).Sentence(origin)}", LinkedText.Mentions(origin), nameOf)
            : LinkedText.Plain("Made before recorded history.");
        return new ArtifactScreen(x.Ref, x.Name, x.Type.ToString(), x.Material, (int)Math.Round(x.Fame),
            x.Powers.Select(p => p.Kind == "attribute" ? $"+{p.Amount} {p.Attribute}" : p.Kind).ToList(),
            x.CurseRevealed ? x.Curse : null, x.Lore, whereabouts, location, made,
            x.Provenance.Select(p => new ProvenanceRow(log.TryGet(p.EventId, out var e) ? e.Date.Year : 0, p.How, Of(p.From), Of(p.To), p.EventId)).ToList());
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~ChronicleScreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 2`. Then `dotnet test sim` → `Passed: 474` in total.

- [x] **Step 5: Commit**

```bash
git add sim
git commit -m "feat(views): add chronicle, realm and artifact screens

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: fw view, golden JSON and the view benchmark

Spec §4 and §6. `fw view <world> <screen> …` prints any screen as the JSON a client will consume (ids may be written as the CLI prints them, `char#12`, or as numbers; options are checked). `ViewJson` fixes the format (camel case, enums as strings, refs as `kind#id`). Golden files pin each screen on a fixed seed after its prehistory (regenerate deliberately with `UPDATE_GOLDEN=1`), invariants check that every ref resolves, every event id exists and every position is in [0, 1], and a benchmark measures each screen on a medium world after 300 years.

**Files:**
- Create: `sim/FantasyWorld.Bench/ViewsBenchmark.cs`, `sim/FantasyWorld.Cli/CliApp.Views.cs`, `sim/FantasyWorld.Sim/Views/ViewJson.cs`
- Modify: `README.md`, `sim/FantasyWorld.Cli/CliApp.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/why.json`, `sim/FantasyWorld.Sim.Tests/Views/ViewJsonTests.cs`

**Interfaces:**
- Consumes: Tasks 1–7.
- Produces: `static class ViewJson { Options; Serialize(view) }`; CLI `view` (`CliApp.Views.cs`); `ViewJsonTests` with `Golden/{digest,map,character,why}.json`; `ViewsBenchmark` in `FantasyWorld.Bench`; README section on screens as data.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Views/Golden/character.json`:
```json
{
  "ref": "char#1",
  "name": "Fath of Cunvei",
  "house": "house#1",
  "kicker": "Character · House Cunvei",
  "line": "Lord of Drerithby · age 66 · born before recorded history",
  "traits": [
    "calm",
    "devout",
    "lazy",
    "trusting"
  ],
  "ambition": "Ambition: mastery of magic",
  "location": {
    "ref": "settlement#3",
    "label": "Now at Drerithby",
    "position": {
      "x": 0.3515625,
      "y": 0.1953125
    }
  },
  "timeline": [
    {
      "year": 2,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 41,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei and Lond of Cunvei became rivals over calm vs wrathful.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 19,
            "length": 14,
            "entity": "char#3"
          }
        ]
      },
      "eventId": 122,
      "highlight": false
    },
    {
      "year": 18,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 127,
      "highlight": false
    }
  ],
  "folded": 0,
  "earlier": 0,
  "ties": [
    {
      "ref": "char#2",
      "name": "Farnfand of Cunvei",
      "relation": "Spouse",
      "qualifier": "",
      "tone": "warm"
    },
    {
      "ref": "char#3",
      "name": "Lond of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    },
    {
      "ref": "char#4",
      "name": "Broth of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    }
  ],
  "followed": false
}
```

`sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json`:
```json
{
  "worldName": "Dromothholt",
  "year": 21,
  "title": "Year 21.",
  "subtitle": "While you were away, 20 years passed in Dromothholt. The seasons turned, and 140 events were recorded.",
  "elapsedDays": 7200,
  "status": "Caught up · Year 21, day 1",
  "pins": [
    {
      "number": 1,
      "place": "settlement#3",
      "label": "Drerithby",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "eventId": 30
    },
    {
      "number": 3,
      "place": "region#7",
      "label": "Gisreach",
      "position": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "eventId": 138
    },
    {
      "number": 4,
      "place": "region#3",
      "label": "Thonae",
      "position": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "eventId": 58
    },
    {
      "number": 5,
      "place": "settlement#16",
      "label": "Greindrondcross",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "eventId": 61
    }
  ],
  "headline": {
    "number": 1,
    "category": "politics",
    "kicker": "A new era",
    "title": {
      "text": "The Banners Age of Thonae began.",
      "links": []
    },
    "body": {
      "text": "Spring of Year 2.",
      "links": []
    },
    "eventId": 30,
    "canExplain": false
  },
  "items": [
    {
      "number": 2,
      "category": "politics",
      "text": {
        "text": "The Age of Long Peace of Thonae began.",
        "links": []
      },
      "eventId": 42
    },
    {
      "number": 3,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia slew the monsters of Gisreach.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 37,
            "length": 8,
            "entity": "region#7"
          }
        ]
      },
      "eventId": 138
    },
    {
      "number": 4,
      "category": "nature",
      "text": {
        "text": "Plague broke out in Thonae.",
        "links": [
          {
            "start": 20,
            "length": 6,
            "entity": "region#3"
          }
        ]
      },
      "eventId": 58
    },
    {
      "number": 5,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia and Nasgre of Stel became friends.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#19"
          }
        ]
      },
      "eventId": 61
    },
    {
      "number": 6,
      "category": "politics",
      "text": {
        "text": "Nei of Thisthis became spymaster of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#27"
          },
          {
            "start": 36,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 27
    },
    {
      "number": 7,
      "category": "characters",
      "text": {
        "text": "Keth of Bandbia and Nasgre of Stel became friends.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#19"
          }
        ]
      },
      "eventId": 43
    }
  ]
}
```

`sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, no open labyrinths.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      }
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      }
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      }
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      }
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      }
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      }
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      }
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      }
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      }
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      }
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      }
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      }
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      }
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      }
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      }
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      }
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      }
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      }
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      }
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      }
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      }
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      }
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      }
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      }
    }
  ],
  "structures": [],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ]
}
```

`sim/FantasyWorld.Sim.Tests/Views/Golden/why.json`:
```json
{
  "outcome": {
    "eventId": 55,
    "year": 5,
    "category": "characters",
    "title": {
      "text": "Keth of Bandbia died of childbirth at 29.",
      "links": [
        {
          "start": 0,
          "length": 15,
          "entity": "char#17"
        }
      ]
    },
    "note": null,
    "isRoot": false,
    "hasMore": false
  },
  "layers": [
    [
      {
        "eventId": 54,
        "year": 5,
        "category": "characters",
        "title": {
          "text": "Steil of Bandbia was born at Greindrondcross to Keth of Bandbia and Gral of Bandbia.",
          "links": [
            {
              "start": 0,
              "length": 16,
              "entity": "char#31"
            },
            {
              "start": 29,
              "length": 15,
              "entity": "settlement#16"
            },
            {
              "start": 48,
              "length": 15,
              "entity": "char#17"
            },
            {
              "start": 68,
              "length": 15,
              "entity": "char#16"
            }
          ]
        },
        "note": null,
        "isRoot": true,
        "hasMore": false
      }
    ]
  ],
  "edges": [
    {
      "from": 54,
      "to": 55,
      "kind": "trigger",
      "label": "because"
    }
  ],
  "summary": "Traced back within the year, through one root cause.",
  "people": [
    {
      "ref": "char#17",
      "name": "Keth of Bandbia"
    },
    {
      "ref": "char#31",
      "name": "Steil of Bandbia"
    },
    {
      "ref": "char#16",
      "name": "Gral of Bandbia"
    }
  ],
  "places": [
    {
      "ref": "settlement#16",
      "name": "Greindrondcross"
    }
  ],
  "systems": [
    "characters"
  ],
  "consequences": []
}
```

`sim/FantasyWorld.Sim.Tests/Views/ViewJsonTests.cs`:
```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

/// <summary>
/// The JSON contract (sub-project 2 §4, §6): each screen of a fixed tiny world after 20 years is pinned in a golden
/// file. Simulation changes alter them on purpose; regenerate with UPDATE_GOLDEN=1 and review the diff.
/// </summary>
public class ViewJsonTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");

    private static string Run(string path, params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "view", path }.Concat(args).ToArray(), output, () => DateTimeOffset.UnixEpoch));
        return output.ToString();
    }

    private static void Golden(string name, string json)
    {
        var file = Path.Combine(GoldenDir(), name + ".json");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1") File.WriteAllText(file, json);
        Assert.True(File.Exists(file), $"Missing golden file {file}; run with UPDATE_GOLDEN=1.");
        Assert.Equal(File.ReadAllText(file), json);
    }

    [Fact]
    public void Screens_MatchTheirGoldenJson()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "20y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        long duel, lord;
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, log, _) = store.Load();
            duel = log.All.Last(e => e.Type == "DuelFought" || e.Type == "CharacterDied" && e.Causes.Count > 0).Id;
            lord = world.Settlements.Values.First(s => s.LordId != 0).LordId;
        }

        Golden("digest", Run(file.Path, "digest"));
        Golden("map", Run(file.Path, "map"));
        Golden("why", Run(file.Path, "why", duel.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Golden("character", Run(file.Path, "character", lord.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ViewJson_UsesNamesAndRefsAClientCanRead_AndRoundTrips()
    {
        var link = new LinkedText("Maren rode.", new[] { new TextLink(0, 5, new EntityRef("char", 12)) });
        var json = ViewJson.Serialize(new Headline(1, Category.FaithAndMagic, "Labyrinth", link, LinkedText.Plain("x"), 7, true));
        Assert.Contains("\"category\": \"faithAndMagic\"", json);
        Assert.Contains("\"entity\": \"char#12\"", json);
        var back = JsonSerializer.Deserialize<Headline>(json, ViewJson.Options)!;
        Assert.Equal(json, ViewJson.Serialize(back));
    }

    [Fact]
    public void UnknownViews_AreErrors()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        var output = new StringWriter();
        Assert.Equal(1, CliApp.Run(new[] { "view", file.Path, "dreams" }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("error: Unknown view 'dreams'", output.ToString());
        Assert.Equal(1, CliApp.Run(new[] { "view", file.Path, "character" }, output, () => DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ViewArguments_AreReadAsTheCliPrintsThem_AndChecked()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "3y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        long lord;
        using (var store = WorldStore.Open(file.Path)) lord = store.Load().World.Settlements.Values.First(s => s.LordId != 0).LordId;
        int Code(params string[] args) => CliApp.Run(new[] { "view", file.Path }.Concat(args).ToArray(), new StringWriter(), () => DateTimeOffset.UnixEpoch);
        string Error(params string[] args)
        {
            var output = new StringWriter();
            CliApp.Run(new[] { "view", file.Path }.Concat(args).ToArray(), output, () => DateTimeOffset.UnixEpoch);
            return output.ToString();
        }

        Assert.Equal(0, Code("character", $"char#{lord}", "--json"));
        Assert.Equal(0, Code("why", "--depth", "2", "1"));
        Assert.Contains("error: No character #999999", Error("family", "999999"));
        Assert.Contains("error: --as-of must be between 1 and 4", Error("map", "--as-of", "0"));
        Assert.Contains("error: --as-of needs a value", Error("map", "--as-of"));
        Assert.Contains("error: Unknown category '42'", Error("chronicle", "--category", "42"));
        Assert.Contains("error: Unknown option --bogus", Error("digest", "--bogus"));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0103: The name 'ViewJson' does not exist in the current context`.

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
````

`sim/FantasyWorld.Bench/ViewsBenchmark.cs`:
```csharp
using BenchmarkDotNet.Attributes;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Bench;

/// <summary>Sub-project 2 §6: each screen at most 50 ms on a medium world after 300 years.</summary>
[MemoryDiagnoser]
public class ViewsBenchmark
{
    private Simulator _sim = null!;
    private long _war;
    private long _lord;

    [GlobalSetup]
    public void Setup()
    {
        _sim = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch), 300);
        _war = _sim.Log.All.Last(e => e.Type == "WarDeclared").Id;
        _lord = _sim.World.Settlements.Values.First(s => s.LordId != 0).LordId;
    }

    [Benchmark]
    public DigestScreen Digest() => DigestScreen.Build(_sim.World, _sim.Log,
        Sim.Chronicle.Digest.Build(_sim.Log, _sim.World, _sim.Log.NextId - 500, new HashSet<EntityRef>()), _sim.World.Date.AddDays(-360));

    [Benchmark]
    public MapScreen MapToday() => MapScreen.Build(_sim.World, _sim.Log);

    [Benchmark]
    public MapScreen MapFiftyYearsAgo() => MapScreen.Build(_sim.World, _sim.Log, _sim.World.Date.AddDays(-50 * 360));

    [Benchmark]
    public WhyScreen Why() => WhyScreen.Build(_sim.World, _sim.Log, _war);

    [Benchmark]
    public CharacterScreen Character() => CharacterScreen.Build(_sim.World, _sim.Log, _lord);
}
```

`sim/FantasyWorld.Cli/CliApp.Views.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static readonly HashSet<string> ViewOptions = new(StringComparer.Ordinal)
        { "--as-of", "--generations", "--depth", "--from", "--to", "--category", "--min-importance", "--page" };

    /// <summary>
    /// `fw view`: a screen's view model as the JSON the client consumes (sub-project 2 §4). Output is always JSON
    /// (`--json` is accepted for scripts that pass it). Ids may be bare numbers or entities as printed (char#12).
    /// </summary>
    private static int View(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < rest.Length; i++)
        {
            if (rest[i] == "--json") continue;
            if (ViewOptions.Contains(rest[i]))
            {
                if (i + 1 >= rest.Length || rest[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new FormatException($"{rest[i]} needs a value.");
                options[rest[i]] = rest[++i];
            }
            else if (rest[i].StartsWith("--", StringComparison.Ordinal)) throw new FormatException($"Unknown option {rest[i]}.");
            else positional.Add(rest[i]);
        }
        long Id()
        {
            if (positional.Count == 0) throw new FormatException($"`fw view {rest[0]}` needs an id.");
            var text = positional[0];
            return text.Contains('#') ? Importance.ParseRef(text).Id : long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        int? Number(string option, int min, int max)
        {
            if (!options.TryGetValue(option, out var text)) return null;
            var n = int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return n >= min && n <= max ? n : throw new FormatException($"{option} must be between {min} and {max}.");
        }

        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var now = world.Date.Year;
        output.WriteLine(rest[0] switch
        {
            "digest" => ViewJson.Serialize(DigestScreen.ForReader(world, log, store.LoadFollowed())),
            "map" => ViewJson.Serialize(MapScreen.Build(world, log,
                Number("--as-of", 1, now) is { } year ? new WorldDate((long)year * WorldDate.DaysPerYear - 1) : null)),
            "character" => ViewJson.Serialize(CharacterScreen.Build(world, log, Id(), store.LoadFollowed().Contains(new(Sim.Model.Character.Kind, Id())))),
            "family" => ViewJson.Serialize(FamilyTree.Build(world, Id(), Number("--generations", 0, 10) ?? 2)),
            "why" => ViewJson.Serialize(WhyScreen.Build(world, log, Id(), Number("--depth", 1, 20) ?? 4)),
            "chronicle" => ViewJson.Serialize(ChronicleScreen.Build(world, log, Number("--from", int.MinValue, now), Number("--to", int.MinValue, now),
                options.TryGetValue("--category", out var category) ? ParseCategory(category) : null,
                Number("--min-importance", 0, 100) ?? 0, Number("--page", 1, int.MaxValue) ?? 1)),
            "realm" => ViewJson.Serialize(RealmScreen.Build(world, log, Id())),
            "artifact" => ViewJson.Serialize(ArtifactScreen.Build(world, log, Id())),
            _ => throw new FormatException($"Unknown view '{rest[0]}'. Use digest, map, character, family, why, chronicle, realm or artifact."),
        });
        return 0;
    }

    private static Category ParseCategory(string text) =>
        Enum.GetNames<Category>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<Category>(name)
            : throw new FormatException($"Unknown category '{text}'. Use {string.Join(", ", Enum.GetNames<Category>().Select(n => n.ToLowerInvariant()))}.");
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
          fw view <world-file> digest | map [--as-of <year>] | character <id> | family <id> [--generations <n>]
                 | why <event-id> [--depth <n>] | chronicle [--from <year>] [--to <year>] [--category <c>] [--page <n>]
                 | realm <id> | artifact <id>         a screen's data as JSON, for clients
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
                "view" => View(path, rest, output),
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

`sim/FantasyWorld.Sim/Views/ViewJson.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Views;

/// <summary>
/// The JSON contract a client reads (sub-project 2 §2): camelCase names, enums as strings, entity refs as the
/// "char#12" strings the CLI prints, indented for diffs.
/// </summary>
public static class ViewJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new RefConverter() },
    };

    public static string Serialize<T>(T view) => JsonSerializer.Serialize(view, Options);

    private sealed class RefConverter : JsonConverter<EntityRef>
    {
        public override EntityRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Importance.ParseRef(reader.GetString() ?? throw new JsonException("Expected an entity like char#12."));

        public override void Write(Utf8JsonWriter writer, EntityRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~ViewJsonTests|FullyQualifiedName~character.j|FullyQualifiedName~digest.j|FullyQualifiedName~map.j|FullyQualifiedName~why.j"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 478` in total.

- [x] **Step 5: Commit**

```bash
git add sim README.md
git commit -m "feat(cli): add fw view with golden JSON and a view benchmark

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §2 categories as data, every name a link | 1 |
| §3.1 digest screen: pins, headline, items, status | 2 |
| §3.3 character screen and family tree | 3 |
| §3.4 why chain as layered DAG with side panel | 4 |
| §5 history at a date | 5 |
| §3.2 map screen, with the history slider's `asOf` | 6 |
| §3.5 chronicle, realm and artifact screens | 7 |
| §4 `fw view` JSON; §6 golden files, invariants, benchmark (each screen under a millisecond, budget 50 ms) | 8 |

Next: sub-project 4, the Godot diorama client, renders these views (`docs/superpowers/specs/2026-10-06-godot-diorama-client-design.md`).
