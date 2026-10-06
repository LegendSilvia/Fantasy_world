# Fantasy World — Myth, Magic & Structures Design (sub-project 3)

**Status:** draft for review · **Builds on:** the sim core (Plans 1–6), the chronicle views (sub-project 2) and the
Godot client (sub-project 4)

## 1. Purpose

The sim core gave the world its hooks: regions carry danger and ley power, faiths have tenets, labyrinths open and
hide relics, landmarks get built, and characters hunt "the monsters of" a region. This sub-project gives those hooks
depth, so the world grows its own myths: named beasts that terrorise valleys until a hero ends them, faiths that
spread, split and go to war, ley tides that make magic surge and go wild, great works that take a generation and can
fall, labyrinths with levels and a guardian, and the trade roads that bind towns together, which the player sees
on the map's *Trade routes* layer.

Non-goals: player intervention, new screens (everything appears through the existing views and client), and any
change to the determinism and journal rules.

## 2. Principles

- **Everything is an entity with a history.** Monsters, routes and works are first-class (`EntityRef`, events with
  causes, names from the inventor), so they appear in why-chains, the digest, the map and the chronicle.
- **Causes, not dice.** A beast appears *because* danger rose after a ley surge; a schism *because* a zealous faith
  lost its holy site; a route is severed *because* a war occupied the pass.
- **Rules first, the LLM on top.** Rules decide; the inventor names monsters and faiths and writes their lore; the
  arbiter resolves hunts against a monster's might. Offline, templates and rules suffice.
- **Budgeted.** Each system stays within the 2 s per world-year budget at 120 regions (target: under 15 ms each).

## 3. Monsters

`Monster { id, name, kind (wyrm, troll, wraith, kraken, chimera… by biome), lairRegion, might 1–100, age, notoriety,
state (roaming | lairing | slain | slumbering), slayer?, lairStructure? }`.

- **Rising:** each year, regions whose danger is high (and more so after a ley surge) may raise a beast whose kind
  fits the biome; the bigger the danger, the mightier. Monsters age and grow; old ones may slumber and wake.
- **Raids:** a roaming monster raids a nearby settlement (population, food and wealth lost, unrest up, the region's
  memory remembers "the year the wyrm came"). Raids are the monster's events; heroes answer them.
- **Hunts:** `DeedsSystem` hunts target a named monster; the arbiter weighs the hunter against its might. A slain
  beast leaves a trophy (sometimes an artifact) and fame; a failed hunt feeds its notoriety.
- **Legends:** a monster with high notoriety is legendary: the digest follows it and its death is an era-level event.

## 4. Faiths

- **Spread:** each region's faith drifts toward the faith of its realm and its neighbours, faster with temples
  (settlement buildings) and saints, slower for ascetic or tolerant faiths; conversions are events.
- **Holy sites:** a faith's holy region is a site; losing it to another faith's realm raises zeal.
- **Schism:** a zealous faith under strain (lost holy site, unstable realm) may split: a heresiarch character founds
  a new faith with shifted tenets, and regions choose.
- **Holy war:** a *holy war* casus belli for realms whose faith is warlike or zealous against a realm holding their
  holy site or of a rival faith.
- **Saints:** a character of exceptional deeds and piety may be declared a saint after death; their relics are
  artifacts.

## 5. Magic

- **Ley tides:** each region's ley power rises and falls in slow tides around its base; peaks are *surges*.
- **Wild magic:** a surge in a region with a mage tower and a weak magic law may go wild (storm, blight, a portal
  that raises danger): the cause of later monsters and labyrinths.
- **Mage towers:** settlements may raise a tower (a building) where ley is strong and magic is allowed; towers
  train mages (realm armies already count mages) and make rituals possible.
- **Magic law:** realms' magic law (banned, licensed, free) now matters: banned drives mages to emigrate and can
  spark persecution; free raises wild-magic risk.

## 6. Great works

Landmarks become projects: an architect (a character), a patron, years of work drawing wealth and labour from the
settlement, delays from war, plague or famine, a chance of collapse (with deaths) when rushed, and on completion
renown for the patron and stability for the realm. Fortifications (walls) are works too, and raise a settlement's
defence. Ruins can be restored.

## 7. Labyrinths

A labyrinth has levels (3–7) and a guardian (a monster that lairs there), and holds its relics on its deepest
levels. Delves go level by level: each delve may reach deeper, fail, or meet the guardian. Slaying the guardian
and taking the last relic *clears* it: danger falls and the labyrinth becomes a ruin or a shrine.

## 8. Trade routes

Markets in towns and cities link into a network: a route follows the region graph between two markets within a
travel budget, opened when both are at peace with each other's realms and the way is not too dangerous. Each year
a route moves wealth both ways in proportion to the smaller market. Routes are *severed* when a war occupies a
region on the way or a monster raids it, and reopen later. Events: `RouteOpened`, `RouteSevered`, `RouteRestored`.
On the map, routes are drawn as dotted roads between settlements (the *Trade routes* layer), with severed ones
broken.

## 9. Views and client

- `MapScreen` gains `Routes[]` (settlement chain, state) and `Monsters[]` (lair position, kind, legendary), and
  structures gain temples and mage towers; `RealmScreen` gains faith; `CharacterScreen` shows faith and saints.
- Categories: monsters and magic are *Faith & magic*; routes are *Economy*.
- Client: the *Trade routes* layer draws routes as dotted ribbons along the region path; lairs get a beast marker;
  towers and temples get their own markers.

## 10. Testing

Unit tests per system on hand-built worlds; a 200-year soak on medium worlds checks invariants (every monster's
lair exists, routes join living markets, faith shares sum to one region each); determinism and chunk invariance as
before; the performance test keeps the per-year budget; the why-chain criterion is extended to monster raids and
holy wars.

## 11. Plan outline (~9 tasks)

1. Monsters: entity, rising, aging, raids (with templates).
2. Hunts against named monsters, trophies, legends.
3. Faith spread, temples and conversions.
4. Schisms, holy sites, holy war, saints.
5. Ley tides, surges, wild magic, mage towers and magic law.
6. Great works: projects, architects, delays, collapse, fortifications, restoration.
7. Labyrinth levels, guardians and clearing.
8. Trade routes: network, wealth flow, severing and restoring.
9. Views and client: routes layer, lairs, towers and temples; soak, performance and README.
