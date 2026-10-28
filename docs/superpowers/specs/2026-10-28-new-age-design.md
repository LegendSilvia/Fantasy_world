# Fantasy World — A New Age: continents, races, gods and the demon lord (sub-project 13)

**Status:** N1–N6 built (2026-10-28), with the fight result face-offs (F3) and the map badges (M1); N7 (the new age) and V1 (the vertical short) to come · **Asked by:** the owner, 2026-10-28 ("i want more fantasy aspect") · **Builds on:** world
generation, cultures and faiths, monsters, labyrinths, heroes, the decider and inventor (Claude), the daily broadcast,
the website (Cloudflare Pages)

## 1. Purpose

The world today is one landmass of about 100 regions, peopled by cultures that differ only in names, with faiths,
monsters, labyrinths and heroes as hooks. The owner wants it to feel far more fantastic: **continents and seas**, a map
**three times as large**, **races**, **labyrinths that hold artifacts**, **heroes and a demon lord**, **monsters**,
**gods that answer prayers — the viewers being the gods** — and **a festival unique to every nation**. The live world
cannot be reshaped, so all of this arrives as **a new age**: a new world, generated with everything, becomes the live
one, and the old world's episodes stay archived.

## 2. Decisions (the owner, 2026-10-28)

| Topic | Decision |
|---|---|
| Live world | **A new age:** generate a new, bigger world with continents and races and make it the live world; the old world's history is archived |
| Prayer | **The player is a god.** Characters and nations pray; the players answer on the website; the players can also sway a face-off |
| Races | **Original:** invented races with their own names and natures (Claude names and describes them at world creation) |
| Demon lord | **A world-ending saga:** every few generations a demon lord rises; hordes, fallen realms, a hero foretold; ends in the hero's victory, a sealing, or the demon lord's rule |
| Festivals | **Every nation has a unique festival**, which Claude invents |

## 3. Design (proposed; flagged for review)

### 3.1 Continents and sea (N1)

World generation makes **two to four continents** and scattered islands, separated by open sea, instead of one
landmass. Coastal regions with a town are **ports**; the region graph links ports across the sea (sea lanes, longer
travel). Armies cross the sea only between ports (embarking costs a month); trade crosses by sea lanes. The diorama, the
film and the website draw the wider ocean and the sea lanes.

### 3.2 A bigger map (N1)

A new **Huge** size: about **300 regions** on a map about √3 as wide (so three times the area), with proportionally more
settlements, cultures and starting realms. The simulation, the web bundle and the film must stay within their budgets
(the yearly tick, the bundle size, the film's render time), checked by the bench and a preview run.

### 3.3 Races (N2)

At world creation **four to six original races** are made, each with: a name and a people's name, a short nature (Claude
writes it; the rules' fallback builds one from tables), a **lifespan**, **homelands** (biomes they favour), **gifts**
(attribute leanings), a **temper** (warlike, pious, arcane, mercantile, …) and **old friendships and hatreds** with the
others. Each continent has its own mix. Cultures belong to a race; characters inherit their race (mixed marriages are
possible and rare). Race shapes the story: realms of hated races go to war more readily ("ancient hatred" casus belli),
a realm ruling many of another race is less stable, and the chronicler names peoples by their race.

### 3.4 Labyrinths that hold artifacts (N3)

A labyrinth holds **several artifacts at its levels**, each forged in the deep or lost there long ago, guarded by its
monsters; delvers who reach a level take what lies there; the deepest holds the greatest. A cleared labyrinth is empty.
Labyrinths open on every continent, more in dark and ley-rich places.

### 3.5 Monsters (N3)

Breeds by continent and biome, including **sea monsters** that prey on sea lanes, and monster **lairs** that grow
legendary beasts. Demon hordes come with the demon lord (§3.6).

### 3.6 Heroes and the demon lord (N4)

Every few generations, in the darkest place of the world (high danger and ley), **a demon lord rises**: a named
character and a demon realm that spreads by conquest, raising monsters as its hosts and corrupting land. A **prophecy**
names a hero to come; heroes rise across the realms; realms may **ally against it**. The saga ends when the **hero slays**
the demon lord, the **demon lord is sealed** (by a ritual at a great cost), or the **demon lord rules** the land it took
(and the age turns dark). Claude decides its turning points (within the decision budget); the rules' fallback weighs
them. Its rise, its conquests and its end are headline news.

### 3.7 Gods and prayer — the players are the gods (N5)

- **Prayers in the world.** Characters and nations pray to their faith's god in need or hope: for victory, healing, a
  child, rain or harvest, vengeance, protection from a beast or the demon lord. A prayer is an event; each episode
  tells the day's prayers.
- **The players answer.** The website lists the open prayers (with who prays, to which god, and for what) and the
  coming face-offs. Each visitor may answer each prayer (**grant** or **deny**) and **bless one side** of each face-off,
  once a day. Answers are stored by a Cloudflare Pages Function in a small database (D1), one vote per visitor per item.
- **The gods act.** Before the next day's simulation, the daily run reads the tallies: a prayer most players granted
  is answered (a miracle: a healing, rain, a victory's favour, a child, a beast struck down), a denied one is not (and
  the faithful may doubt). A blessed side's odds in its coming fight rise. Faiths whose prayers are answered grow;
  neglected gods wane. Each answer is an event ("the gods answered"), told in the next episode.
- With no votes, the rules decide quietly, so the world goes on without players.

### 3.8 Festivals (N6)

Every nation (independent realm) has **a festival of its own**: Claude invents its name, the month it falls in, what is
done and what it honours (its founding, its god, its land), from the nation's race, faith and history; the rules'
fallback builds one from tables. It is held every year (an event), steadies the realm a little, and may host things
that happen around it (marriages, duels, tourneys). A new realm founds its festival; a realm that changes greatly
(overthrow, conversion) may reshape it.

### 3.9 The new age (N7)

When N1–N6 are built and checked: a new Huge world is generated (with Claude's races, festivals and names) and
pre-simulated, the old world is archived in the bucket (its episodes, videos and site stay), and the broadcast's first
episode of the new age introduces the world. The website shows the new world; the old one stays at an archive link.

## 3.10 Also asked (the owner, 2026-10-28)

- **F3 — a fight's face-off:** a fight's section shows its face-off as it stands: the result (victor, how it ended, the
  losses) or its course so far (its day, the chance reckoned at its challenge); the film brings the card up after the
  section's still and moves.
- **M1 — map badges:** beasts are boss markers (a horned skull on a shield; gold for legends, grey asleep), fights are
  crossed swords (gold once fought this past year, red while under way, battles and duels alike); game, film, website.
- **V1 — a vertical short:** each episode also as a 9:16 short for YouTube Shorts and TikTok, the same content in a layout
  of its own.

## 3.11 As built

- N1: `TerrainOptions.Continents`, `ContinentNamer`, `RegionGraphBuilder.AddCrossings` (three crossings between every two
  continents), `WorldSize.Huge` (448², 300 regions).
- N2: `Race`, `RaceGenerator` (and `Invent` via `fw invent`), `Peoples`; gifts at birth, span in mortality, opinion ±20,
  `ancient hatred` casus belli, unrest +3/+7 under another race.
- N3: treasures every other level (`Artifact.Level`), continent breeds, sea monsters (`RiseFromTheSea`), `HostAttackedAtSea`.
- N4: `SagaSystem`, `Saga`, `Government.Dominion`; ends slain, sealed, driven out, or a dark age.
- N5: `PrayerSystem`, `Prayer`, `World.Favour`, `Tally`; `fw answer`; the episode's prayers section; the website's
  Prayers panel; Pages Functions `site/functions/api/vote.js` and `tally.js` on the D1 database `fantasy-world-prayers`;
  the daily run fetches the tally before the day's history.
- N6: `FestivalSystem`, `Realm.Festival`; `fw invent` asks Claude for every nation's festival.
- N7 (so far): a world the daily run creates is made with no history (`fw new --prehistory 0`), named by Claude
  (`fw invent`: races and festivals), then lives `prehistoryYears` (a century; none for tiny) offline with Claude off
  (`fw advance --quiet`), before its first episode. The switch itself (a new `worldKey`, `size` huge, a new seed) waits
  for the owner.

## 4. Milestones

- **N1 — Continents, sea and a bigger map:** continents, ports and sea lanes, armies and trade across the sea, the Huge
  size; diorama/film/web draw it; bench and preview within budget.
- **N2 — Races:** original races (Claude), cultures and characters by race, race in war, stability and narration.
- **N3 — Labyrinths and monsters:** artifacts at labyrinth levels, breeds by biome and continent, sea monsters, lairs.
- **N4 — Heroes and the demon lord:** the rising, the prophecy, the demon realm and hordes, alliances, the three ends.
- **N5 — Gods and prayer:** prayers in the world and the episode; the website's prayer and blessing page; the Pages
  Function and D1; the daily run applies the players' answers.
- **N6 — Festivals:** a unique festival per nation (Claude), held yearly, told.
- **N7 — The new age:** generate, pre-simulate, archive the old world, switch the broadcast and the site.
