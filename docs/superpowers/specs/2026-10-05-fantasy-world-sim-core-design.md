# Fantasy World — Sim Core Design

**Date:** 2026-10-05
**Status:** Draft for review
**Scope:** Sub-project 1 of 5 — the headless simulation core, its LLM layer, persistence and CLI.
**UI reference:** Claude Design canvas — https://claude.ai/artifact/1q5esun6tMtyMP75sMwsvb (Morning digest, Diorama map HUD, Character page, Why chain)

---

## 1. Vision

A persistent 3D fantasy world that **keeps living while the player sleeps**. The player is an **observer / chronicler**: they don't control anyone. Each time they return, the world has moved on, and the game shows them:

- **Character achievements and drama** — who rose, fell, loved, betrayed, died.
- **Major world history** — wars, successions, landmarks raised, labyrinths opened, artifacts forged or lost.
- **Above all, *why*** — every event can be traced back through a causal chain to its roots.

### Fixed decisions

| Topic | Decision |
|---|---|
| Player role | Observer / chronicler — no direct control |
| Time model | Catch-up on launch + live ticking while open; **1 real day = 1 world year** |
| Platform | Native 3D: **Godot 4 + C#**; sim is an engine-agnostic **.NET 8** library |
| Camera | God's-eye **diorama** (floating cross-section world block) |
| World size | Medium-large: **100–120 regions, 6–8 starting realms** |
| LLM role | **Story beats**: important decisions, arbitration of personal conflicts (duels etc.), content invention, all narration. Rules simulate everything else |
| LLM providers | Anthropic (official C# SDK; local testing via `ant auth login` OAuth profile) and OpenRouter |
| UI style | Clean "diorama gallery": warm light-grey ground, italic serif display (Instrument Serif), small sans (Geist), black pill buttons, floating control card |

### Sub-project decomposition

1. **Sim core** *(this spec)* — world gen, characters, artifacts, regions, settlements, realms, armies, time & catch-up, causal event log, LLM layer, persistence, CLI.
2. **Chronicle & why UI data** — rich chronicle views, eras, digest polish (core queries are in this spec; presentation is not).
3. **Myth, magic & structures (depth)** — monsters, religions, magic systems, landmark construction projects, labyrinth internals. *This spec defines only the minimal hooks: Structure entity, labyrinth-opening and landmark-completion events, ley power.*
4. **Godot 3D diorama client** — renders world state, implements the four designed screens.
5. **Narration polish** — prompt tuning, voice, evals for narration quality.

### Non-goals (this sub-project)

- Any 3D rendering or Godot code.
- Player intervention (blessings, disasters). The player only observes and *follows*.
- Multiplayer / shared worlds / server hosting.

---

## 2. Architecture

```
Fantasy_world/
  sim/
    FantasyWorld.Sim/            .NET 8 class library — no Godot dependency
      Core/      WorldClock, Rng (seeded, per-system streams), IdGen, EventLog
      Model/     World, Character, Artifact, Region, Settlement, Structure, Realm, Army
      Systems/   Aging, Relationships, Economy, Regions, Politics, Military, Characters, Structures
      Llm/       ILlmProvider, AnthropicProvider, OpenRouterProvider, FakeLlmProvider,
                 DecisionService, ArbitrationService, InventionService, Narrator
      Engine/    Simulator (catch-up + live), TickScheduler, EventQueue, Invariants
      Query/     HistoryQuery (WhyChain, Consequences, Life, History, Digest, Eras)
      Persist/   SQLite store, migrations, backups
      WorldGen/  heightmap, climate, rivers, regions, cultures, realms, prehistory
    FantasyWorld.Sim.Tests/      xUnit
    FantasyWorld.Sim.Bench/      BenchmarkDotNet
    FantasyWorld.Cli/            `fw` command-line tool
  game/                          Godot 4 C# project (sub-project 4)
  docs/
```

### Boundaries

- **Godot ↔ Sim:** the client only calls `Simulator.CatchUpAsync(now, progress)`, `Simulator.TickLive(now)`, and reads via `World` (read-only views) and `HistoryQuery`. The sim never references the renderer.
- **System ↔ System:** systems never call each other. Each reads the world and emits **events** and **state mutations**. Cross-system effects flow through events (a dam built by Structures is an event that Economy later cites when the harvest fails). This is what makes causality traceable.
- **Sim ↔ LLM:** the sim depends only on `ILlmProvider`. Every LLM result is validated by the engine and persisted; the LLM never mutates state directly.

### CLI (`fw`)

```
fw new <name> [--seed N] [--size medium|large]   generate world + prehistory
fw advance <duration>                           e.g. 4mo, 7y (debug fast-forward)
fw catchup                                      simulate real elapsed time
fw digest [--since <date>]                      morning digest as text
fw why <eventId> [--depth N]                    causal chain as text
fw life <characterId> | fw history <entityId>   biographies
fw follow <entityId>                            mark followed
fw stats                                        history-health metrics, LLM spend
```

The game is playable as text before any 3D exists; the CLI is also the main test harness.

---

## 3. Characters

### Tiers

- **Notables** (~500–2,000): individually simulated named characters — rulers, heirs, generals, councillors, knights, mages, heroes, notable commoners and their families.
- **Populace**: aggregate numbers per settlement (age bands, occupations, mood).
- **Promotion:** a populace member becomes a notable when a story-worthy event involves them (e.g. saving a lord's son). The LLM invents their backstory (§8).

### Notable data

| Field | Notes |
|---|---|
| Identity | name, house/dynasty, birth date & place, culture, faith, life stage (child, adult, elder, dead) |
| Attributes (0–20) | martial, diplomacy, stewardship, intrigue, lore, prowess |
| Traits (3–5) | e.g. brave, greedy, devout, cruel, ambitious; modify decision weights; can be gained/lost through events (scarred, humiliated, war hero) |
| Ambition | one active goal: gain title, avenge, marry well, build landmark, master magic, clear labyrinth, obtain artifact |
| Memories | `(eventId, feeling, weight)`, weight decays over time |
| Opinion | of another character = Σ memories involving them + trait compatibility |
| Relationships | family (parents, spouse, children); derived: friend, rival, lover, mentor, liege |
| Roles | ruler, heir, general, councillor, knight, mage, priest, adventurer |
| Health | wounds, injuries (persist), illness |
| Holdings | titles, artifacts held |
| Deeds | events the character caused or took part in (feeds the character page timeline) |

### Monthly decisions

Each notable rolls a small set of candidate actions from ambition and role, weighted by attributes, traits and opinions. Routine choices are resolved by the rules engine (seeded weighted choice). Choices that are **decision points** (§8) go to the LLM. The resulting event's `Causes` cite the memories and condition events behind the choice.

### Death and succession

Causes: old age, illness, battle, assassination, monster, execution, duel. Every death triggers succession/inheritance and creates memories for those close to the deceased.

---

## 4. Artifacts

| Field | Notes |
|---|---|
| Identity | name, type (weapon, armor, crown, tome, relic, trinket), material/creature of origin |
| Origin | the creating event (forged, enchanted, hoard, found in a labyrinth) — root of its own why-chain |
| Powers (1–3) | holder stat bonus, special effect (wards monsters, reveals lies…), or realm effect (legitimacy, harvest blessing) |
| Curse (optional) | hidden downside revealed over time; the chronicle discovers it retroactively |
| Fame (0–100) | grows with notable deeds done while held; famous artifacts become ambition targets |
| Provenance | `(holder, from, to, how: forged / inherited / gifted / stolen / looted / found)` |
| Location | held by character · settlement treasury · enshrined in structure · **lost** (in a labyrinth, ruin or battlefield) |

- **Scale:** ~20–60 per world; created rarely; never deleted — only lost or destroyed (destruction is an event).
- **Integration:** "obtain artifact" ambition; regalia affect realm legitimacy (losing a crown can trigger a succession crisis); labyrinths are the main source/sink; artifacts modify arbitration odds (§8.3).
- The LLM **invents** each artifact's name, lore and power flavour within engine-defined mechanical slots.

---

## 5. Regions & settlements

### Two map layers

- **Terrain grid** (~256×256 tiles): elevation, biome, moisture, river flags. Used for world gen and Godot rendering.
- **Regions** (100–120): tile groups forming provinces, connected by an **adjacency graph** with travel cost, roads, river crossings and passes. All movement (armies, trade, migration, monsters) uses the graph.

### Region data

| Field | Notes |
|---|---|
| Geography | dominant biome, climate band, river/coast access |
| Resources | grain, timber, ore, stone, horses, fish; rare: mithril, dragonbone, arcane crystal — yields deplete/recover |
| Fertility (0–100) | drifts with climate events, war devastation, overfarming, magic |
| Danger (0–100) | monsters, bandits, wild magic; rises when depopulated or near labyrinths, falls with patrols and heroes |
| Ley power (0–100) | magic strength; governs mages, labyrinth openings, artifact forging |
| Control | owner realm, control level (contested / occupied / loyal), local culture and faith |
| Contents | settlements, structures, lost artifacts |
| Memory | defining events ("the burned valley") — used in naming and narration |

### Settlements

Tier (hamlet, village, town, city) · aggregate population by age band and occupation · food stores · wealth · defences (walls, garrison) · buildings (forge, temple, mage tower, market…) · unrest (0–100: high unrest → revolt, emigration, or a new notable rising).

### Structures (minimal, expanded in sub-project 3)

`Structure { id, kind (landmark | fortification | labyrinth | ruin), region, builder, startedOn, completedOn, state }`. Events: `LandmarkCompleted`, `LabyrinthOpened`, `StructureRuined`. Labyrinths raise regional danger and hold lost artifacts.

### Region events

Drought, flood, plague, earthquake, monster migration, ley surge, resource discovery, labyrinth opening, ruination — each with causes (e.g. earthquake ← ley surge).

### World generation

Seed → heightmap → climate/biomes → rivers → regions → resources → cultures, settlements, realms → **~100-year prehistory run** so the player starts with existing history, grudges, ruins and artifacts. Prehistory uses the rules engine plus a reduced LLM budget for founding myths and the first artifacts.

---

## 6. Realms & armies

### Realms

**Hierarchy:** Empire › Kingdom › Duchy › County. Realms hold regions directly and through vassal realms. Empires form by conquest/vassalisation; fragment when vassals break away.

| Field | Notes |
|---|---|
| Ruler & council | ruler + seats (marshal, steward, spymaster, court mage, high priest) filled by notables; council attributes drive capability |
| Government | feudal monarchy, elective kingdom, theocracy, magocracy, merchant republic, tribal |
| Succession law | primogeniture, elective, gavelkind, seniority |
| Legitimacy (0–100) | inheritance, regalia artifacts, faith endorsement, victories |
| Stability (0–100) | vassal opinion, regional unrest, legitimacy, war weariness |
| Treasury | taxes, trade, tribute; spending on armies, structures, bribes |
| Laws | crown authority, magic status (embraced / licensed / outlawed), religious tolerance, conscription |
| Identity | culture, faith, heraldry colours, founding myth event |

**Diplomacy:** realm opinion = ruler opinions + realm-level memories (grudges outlive rulers). Relations: alliance, non-aggression, vassalage, tribute, royal marriage, trade pact, rivalry. **Casus belli** (claim, holy war, revenge, reconquest, resource need, artifact recovery) are cited as causes of every war.

**Lifecycle:** founded → expanding → stable → declining → fragmenting / collapsed. Each phase change opens a named **era**.

### Armies

**Composition:** levies (drain workforce and food), men-at-arms and knights (paid; knights link to notables), mercenary companies (captain is a notable; may switch sides if unpaid), exotic units (battle mages, priests, beasts — limited by ley power and law). **Commander** is a notable whose martial, traits and artifacts modify battles.

**State:** strength per unit type, morale, supply, position (region node), objective, battle history.

**Campaign flow (monthly):**
1. Mobilise on war or threat.
2. Move along the region graph (terrain and season set speed; winter/mountains cost supply).
3. Supply: forage the current region (hurts its fertility/food); no supply → attrition and desertion.
4. Battle: abstract rules resolution (strength, unit mix, terrain, commander, morale, artifacts, magic, seeded roll) → casualties, captures, traits, memories. **Notable fates** inside a battle (commander falls, is captured, becomes a hero) are LLM-arbitrated (§8.3).
5. Siege: walls and garrison vs siege strength over months → surrender, storm (possibly a massacre), or relief.
6. War score from battles, sieges, occupation → peace when war score, weariness and traits demand. Terms: cede regions, tribute, vassalage, return an artifact, hostage heir.

---

## 7. Time, catch-up & the event log

### Calendar and rate

360-day year (12 × 30 days, four seasons). 1 real day = 1 world year → 1 real hour ≈ 15 world days; 8 h sleep ≈ 4 months; a week ≈ 7 years.

### Modes

- **Catch-up:** on launch, `elapsed = now − meta.lastRealUtc` → world days; simulate on a background thread with progress ("Catching up… Year 309 → 312"); then the digest.
- **Live:** while open, ~1 world day per 4 real minutes; events surface as pins. No player pause or fast-forward (debug builds only).
- **Cap:** at most **30 world years per launch**; any remainder carries over to later sessions; the digest states how far behind the world is.
- **Clock regression:** if the system clock moves backwards, the world waits; it never rewinds.

### Engine loop

- Day-granular; empty days are near-free.
- **Event queue** of scheduled actions (army arrivals, siege resolutions, births, plots), ordered by `(date, priority, seq)`.
- **Monthly tick** (day 1), fixed system order: Aging → Economy → Regions → Politics → Military → Characters (decisions) → Structures.
- **Annual tick:** population growth, harvest, era checks.
- Catch-up and live run the **same tick function**.

### Determinism

- `World = f(seed, eventLog)`. Each system and tick draws from its own derived RNG stream (`hash(seed, system, tick)`), so adding a system doesn't reshuffle others.
- **Chunk invariance:** advancing in one chunk, many chunks, or live yields an identical world.
- LLM results are stored in the log; replay reads them and never re-queries.

### Performance target

**≤ 2 s per world year** on a typical desktop with 120 regions, rules only (LLM faked). A week's absence (~7 years) catches up in ~15 s plus LLM latency, which is pipelined (§8.4).

### Event model

```
Event {
  Id          long, monotonic
  Date        world day
  Type        e.g. WarDeclared, BattleFought, DuelFought, ArtifactForged, LabyrinthOpened
  Actors      role → entity ref
  Place       region / settlement
  Payload     type-specific fields
  Causes      [(EventId, kind: trigger | motive | enabler, weight)]  — max 3
  Importance  0–100
  Tags        systems involved
  LlmCallId   nullable — link to the decision/arbitration/invention that produced it
}
```

**Rule — causes are always events.** When a significant state value crosses a threshold, the engine emits a **condition event** (`GrainShortageBegan`, `LegitimacyCollapsed`, `LeySurge`). Decisions then cite those events, so every chain is traceable to the root.

**Storage:** append-only; indexed by entity, region, date, type; reverse cause→effect index. **Compaction:** minor events older than 50 years are rolled into summary events, except any event that is a cause of a retained event.

### Importance and the digest

Importance = f(actor fame, scale — realm/region/personal, rarity, involvement of followed entities). The digest shows the top events since `lastSeenDate`, grouped by season for long absences.

### History queries (`HistoryQuery`)

| Query | Returns | Consumer |
|---|---|---|
| `WhyChain(eventId, depth)` | cause DAG with merged branches | Why chain screen |
| `RootCauses(eventId)` | leaf causes | root-cause cards |
| `Consequences(eventId)` | forward effects | "Trace further" |
| `Life(characterId)` | deeds timeline | Character page |
| `History(entityId)` | biography of region / realm / artifact | map, artifact page |
| `Digest(from, to, followed)` | ranked events | Morning digest |
| `Eras(realmId)` | named periods | chronicle |

---

## 8. LLM layer — story beats

### 8.1 Jobs

| Job | Trigger | Output |
|---|---|---|
| **Decide** | a notable reaches a decision point | one of the engine's legal options + reasoning |
| **Arbitrate** | a dramatic personal conflict | an outcome from the engine's legal outcome set + consequences |
| **Invent** | something new appears | names, lore, backstories, founding myths, era names |
| **Narrate** | the player reads | digest prose, biographies, why-chain prose |

### 8.2 Decisions

1. Engine detects a **decision point**: casus belli available, succession, ambition within reach, artifact acquired, betrayal opportunity, major diplomatic offer. Routine choices stay rule-based.
2. Engine builds a **situation brief**: character (traits, ambition, top memories, key opinions), stakes, relevant world state, and **2–5 legal options**.
3. LLM returns structured JSON `{ choice, reasoning, flavor }`.
4. Engine validates (choice ∈ options), emits the event; `reasoning` is stored as the event's **motive**.

### 8.3 Arbitration

**Encounters:** duels and trials by combat, assassination attempts, labyrinth delves and monster hunts, magical rituals/confrontations, high-stakes parleys, and notable fates within battles.

1. **Engine sets the stage:** both sides' attributes, traits, wounds, age, artifacts, terrain, witnesses, stakes → **base odds**.
2. **Engine enumerates legal outcomes** with plausibility: win, loss, yield, draw, wounded (injury), maimed, death, mutual death, interrupted — filtered by encounter rules (e.g. "to first blood" forbids death).
3. **LLM arbitrates**, receiving odds as guidance, and returns:
   ```json
   { "outcome": "victory_by_yield", "winner": "<characterId>",
     "consequences": [ {"who": "<id>", "effect": "wounded", "detail": "sword arm"},
                       {"who": "<id>", "trait": "humiliated"},
                       {"artifact": "<id>", "effect": "notched"} ],
     "turning_point": "…", "account": "…" }
   ```
4. **Engine validates and applies:** outcome legal, consequences from allowed vocabularies, death permitted → events, memories, traits, provenance, fame. `turning_point` is recorded as a cause (linked to an existing event where the engine can match it, e.g. an old wound).

**Fairness guardrails:**
- **Plausibility floor:** outcomes with base probability < 5% are rejected.
- **Drama budget:** a rolling count of upsets; when exceeded, the brief tightens its odds guidance.
- **No plot armour:** followed characters can die. Only encounter rules restrict death.
- **Fallback:** seeded roll on the same odds; LLM narrates afterwards if available.

### 8.4 Budget, pipelining, fallback

- **Story-beat budget** per real day (rolling 24 h, default 60 decide/arbitrate/invent calls, configurable), shared by catch-up and live mode, allocated by importance; arbitrations outrank decisions. Narration calls are budgeted separately (they are driven by what the player reads).
- Over budget → seeded weighted choice / roll (the offline path).
- **Pipelining:** independent decisions in the same month are requested in parallel; the engine blocks only on results it needs to continue.
- **Offline / no credentials / provider error / invalid JSON after one retry** → rules + templates. The game always runs.

### 8.5 Providers

```
ILlmProvider
  Task<T> CompleteJsonAsync<T>(LlmRequest request, JsonSchema schema, ModelTier tier, CancellationToken ct)
├─ AnthropicProvider    official Anthropic C# SDK; credentials resolved by the SDK
│                       (local testing: `ant auth login` OAuth profile; or ANTHROPIC_API_KEY)
├─ OpenRouterProvider   OpenRouter HTTP API; key from user config
└─ FakeLlmProvider      scripted responses for tests
```

- **Model tiers** (config): `decide`, `arbitrate`, `invent`, `narrate`. Default Anthropic model for all tiers: `claude-opus-5-5`; users may assign cheaper models per tier.
- Structured output via the provider's JSON-schema output mode; engine re-validates regardless.
- **Prompt caching:** a stable **world bible** (setting, rules, cultures, tone, output contract) is the cached system prefix; per-call briefs follow it. Nothing volatile (timestamps, IDs) goes in the prefix.
- **Cost ledger:** tokens and cost per call in `llm_calls`; `fw stats` and a debug panel show spend; a configurable monthly spend cap switches to the offline path when reached.
- **Note:** Claude Pro/Max subscription logins are intended for Claude's own apps and Claude Code; the game does not rely on them.

### 8.6 Safety of generated text

LLM-generated names and lore re-enter later prompts as **data** inside clearly delimited sections, never as instructions.

### 8.7 Narration

`INarrator.Narrate(EventView)` / `NarrateChain(ChainView)` / `Digest(DigestView)`.
- **LlmNarrator** is primary.
- **TemplateNarrator** (per-event-type templates + connectives "because", "which led to") is the offline fallback and the test baseline.
- Narration results are cached in `llm_calls` keyed by input hash.

---

## 9. Persistence

One SQLite file per world (`worlds/<name>.fw.db`, Microsoft.Data.Sqlite, WAL mode).

| Table | Contents |
|---|---|
| `meta` | seed, schema version, world date, `lastRealUtc`, `lastSeenDate`, settings (models, budgets, cap) |
| `snapshot` | latest full world state (compressed JSON), rewritten each month tick |
| `events` | append-only log, indexed (date, type, entity, region) |
| `event_causes` | cause → effect edges |
| `llm_calls` | request hash, response JSON, provider, model, tokens, cost, timestamp |
| `followed` | entity ids the player follows |

- **One transaction per month tick** (events + snapshot + LLM records) — a crash loses at most the in-progress month.
- **Load** = read snapshot. Log replay is for verification and tests.
- **Versioned migrations**; **rolling backups** of the last 3 sessions.

---

## 10. Testing

| Layer | What |
|---|---|
| Unit (xUnit) | each system on small hand-built worlds |
| Determinism | same seed + log → identical state hash; chunk invariance (1×4mo = 4×1mo = live); replay reproduces snapshot |
| Invariants (every tick in debug/tests) | causes exist and precede effects (acyclic); dead characters hold no titles; one owner per region; one location per artifact; non-negative populations |
| Causality | `WhyChain` terminates; roots are real events; every war has a casus belli |
| LLM contract | `FakeLlmProvider`; malformed / illegal / sub-floor outputs rejected → fallback; recorded-fixture replays |
| LLM live evals | manual only (costs money): decisions in character, arbitrations respect odds within guardrails |
| History health | 300-year headless soaks on fixed seeds: wars per century, dynasty turnover, artifacts created/lost, labyrinths, upset rate — world neither collapses nor stagnates |
| Performance | BenchmarkDotNet: ≤ 2 s per world year at 120 regions, LLM faked |
| CI | GitHub Actions: `dotnet test` + benchmark on push; live LLM tests never run in CI |

---

## 11. Success criteria for sub-project 1

1. `fw new` generates a medium-large world with ~100 years of prehistory, invariants holding.
2. `fw catchup` after 8 real hours produces ~4 world months, with a digest of 3–8 headline events, at least one with an LLM-driven decision or arbitration (when credentials exist).
3. `fw why <id>` on any war prints a chain of ≥ 3 linked causes ending in root events.
4. Duels and other arbitrated encounters can end in victory, defeat, injury or death, and their consequences appear in the involved characters' memories, traits and life timelines.
5. With no credentials, the same commands work via rules + templates.
6. All determinism, invariant and performance tests pass in CI.
