# Fantasy World — Chronicle & Why UI Data Design (sub-project 2)

**Status:** draft for review · **Builds on:** the sim core (Plans 1–6) · **Feeds:** the Godot client (sub-project 4)

## 1. Purpose

The sim core can explain anything (`HistoryQuery`, narration, the digest), but only as CLI text. The four
designed screens (Claude Design canvas "Fantasy World UI": *Morning digest*, *Diorama map HUD*,
*Character page*, *Why chain*) need **structured, presentation-ready data**: what to put on a pin, which
card is a root cause, how old a tie is, which system colour a cause carries. This sub-project defines
those **view models** in the engine library, so the Godot client renders and never queries the world
itself, and so the screens can be checked headlessly (`fw view … --json`) before any 3D exists.

Non-goals: rendering, layout, camera, input (sub-project 4); prompt and voice work (sub-project 5).

## 2. Principles

- **Views are pure functions of (world, log, journal, reader state).** They never mutate the world and
  never call the LLM themselves; prose comes from an `INarrator` passed in (templates offline).
- **Plain records, JSON-ready.** Every view is an immutable record tree of strings, numbers, enums and
  `EntityRef`s, serialisable with `SimJson`, stable in field order, with no engine types leaking.
- **Every visible thing is a link.** Names carry their `EntityRef`; events carry their id, so a client
  can follow *any* name or card (to a character page, a why chain, the map).
- **System colour is data, not style.** Each event gets one `Category` (Politics, War, Economy, Characters,
  Faith & magic, Structures, Nature) derived from its tags; the client maps categories to the palette
  (`#3b6fb6`, `#c4621f`, …).
- **History is explorable in time.** Map views take an `asOf` date and rebuild what was true then from
  the log (owners, occupiers, sieges, structures), not just the current snapshot.

## 3. Views

### 3.1 `DigestScreen` (Morning digest)

`{ WorldName, Year, Subtitle ("While you slept, four months passed…"), Elapsed (world days), Status
("Caught up · Year 312, day 196" | "Catching up…" | "N days behind"), Pins[], Headline, Items[] }`

- `Pins` — up to 5 numbered map pins: `{ Number, Place: EntityRef, Label, MapPosition (normalised 0–1
  on the region map), EventId }`, one per digest entry with a place.
- `Headline` — the top entry: `{ Number, Category, Kicker ("War ends"), Title, Body, EventId, CanExplain }`
  (`CanExplain` = the event has causes). Title/Body come from the narrator (LLM headline if online).
- `Items` — the rest: `{ Number, Text (with `Links[]` = name spans → EntityRef), EventId }`.
- `Subtitle` counts seasons crossed and "N things changed the realm".

### 3.2 `MapScreen` (Diorama map HUD)

`MapScreen(world, log, asOf, zoom: Realm|Region|Town, layers)` →
`{ WorldName, Summary ("Three houses, eleven settlements, one open labyrinth."), AsOf, YearRange,
Regions[], Settlements[], Structures[], Borders[], ActiveConflicts[], Legend[] }`

- `Regions` — `{ Ref, Name, Biome, Center (normalised), Owner: EntityRef?, Occupier?, RealmColour }`.
- `Settlements` — pins: `{ Ref, Name, Tier, Position, Holder (realm), Marker: Conflict|Realm|RecentEvent|Plain,
  Card: { Kicker ("Carrow Ford · Town"), Title ("Held by House Venn since day 171"), Line ("Pop. 1,240 · 3 events
  this year") } }`.
- `Structures` — landmarks and labyrinths with state and position (layer "Landmarks & labyrinths").
- `Borders` — realm outlines as region-boundary edge lists between regions of different owners.
- `ActiveConflicts` — wars ongoing at `asOf`, with the regions under siege or occupied.
- History slider: `asOf` from Year 1 to now; ownership at `asOf` is replayed from `RegionCeded`,
  `RegionOccupied`/`Liberated`, `RealmRose`/`Fell` events (§5).
- Trade routes: not modelled yet (deferred, sub-project 3); the layer reports itself empty.

### 3.3 `CharacterScreen` (Character page)

`{ Ref, Name, House: EntityRef?, Kicker ("Character · House Venn"), Line ("Knight of Venn · age 27 · born
Year 285 in Esk"), Traits[], Ambition (sentence), Location: { Ref, Label ("Now at Venn Keep"), Position },
Timeline[], Ties[], Followed: bool }`

- `Timeline` — life & deeds, one row per notable event: `{ Year, Text, EventId, Highlight }`; the
  highlight is the most important event of the last year. Minor rows (Importance < 20) fold into "…".
- `Ties` — `{ Ref, Name, Relation ("Liege", "Father", "Mentor", "Rival"), Qualifier ("loyal", "resents her
  rise", "died Year 312"), Tone: Warm|Neutral|Hostile }`, from family links, liege (the realm's ruler),
  and the strongest memories (opinion ≥ friend / ≤ rival).
- `FamilyTree(ref, generations)` — a separate view: nodes and parent/spouse edges.

### 3.4 `WhyScreen` (Why chain)

`WhyScreen(eventId, depth)` → `{ Outcome: Card, Layers[][], Edges[], Summary ("Traced back four years,
through two root causes."), People[], Places[], Systems[] }`

- `Card` — `{ EventId, Year, Category, Title (narrated sentence), Note (motive / account / lore),
  IsRoot }`; `Layers` are the DAG ranked by distance from the outcome (merged branches appear once).
- `Edges` — `{ From, To, Kind (trigger|motive|enabler), Label ("because" | "because, and") }`.
- Side panel aggregates people, places and categories in the chain; `ShowOnMap` = the places' map
  positions; `TraceFurther` = `HistoryQuery.Consequences`.

### 3.5 Chronicle, eras and realm pages

- `ChronicleScreen(from, to, filter)` — the "Full chronicle" toggle: events by season with category and
  importance, paged.
- `RealmScreen` — ruler, laws, council, vassals, treaties, wars, and `Eras` as a timeline band.
- `ArtifactScreen` — powers, curse (if revealed), lore, provenance as a timeline of holders.

## 4. CLI

`fw view <world> digest|map|character <id>|why <event>|chronicle|realm <id>|artifact <id> [--as-of <year>] [--json]`
prints either a readable rendering or the JSON the client will consume. JSON output is the contract
tests pin down (golden files on fixed seeds).

## 5. History at a date

`WorldAt(log, world, asOf)` reconstructs the political map at a past date by replaying the ownership
events from the generation baseline (the starting owners are recorded once at world creation as a
`WorldFounded` event payload, so no snapshot history is needed). Characters and artifacts at a date come
from their timelines. Anything not reconstructable (economy figures) is shown as "now" only.

## 6. Testing

- Unit tests per view on hand-built worlds (`TestRealms`, `TestPeople`).
- Golden JSON for each screen on a fixed seed after prehistory (regenerated deliberately, reviewed in diffs).
- Invariants for views: every `EntityRef` resolves, every `EventId` exists, positions in [0, 1].
- Performance: each screen ≤ 50 ms on a medium world after 300 years (BenchmarkDotNet case).

## 7. Plan outline (one plan, ~8 tasks)

1. Categories and links (event → category; text spans → refs).
2. Digest screen (pins, headline, items, status line).
3. Character screen and family tree.
4. Why screen (layered DAG, edges, side panel).
5. History at a date (`WorldFounded`, ownership replay).
6. Map screen (regions, settlements, structures, borders, conflicts, cards).
7. Chronicle, realm and artifact screens.
8. `fw view` with `--json`, golden files, and the view benchmark.
