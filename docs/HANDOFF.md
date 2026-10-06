# Handoff — where things stand (2026-10-06)

Context for picking up this project on another machine / in a new Claude session.

## Status

| Step | State |
|---|---|
| Brainstorming | Done |
| UI design (Claude Design canvas) | Done — https://claude.ai/artifact/1q5esun6tMtyMP75sMwsvb |
| Sim core spec | Done, approved — `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` |
| Plan 1 of 6 (Sim foundation) | Written — `docs/superpowers/plans/2026-10-05-sim-foundation.md` |
| Plan 1 execution | **Done** — Tasks 1–12 committed on `ccr-81a7454e-12gx87`; 93/93 tests pass (Debug + Release) |
| Plan 2 of 6 (World & regions) | **Done** — `docs/superpowers/plans/2026-10-05-world-and-regions.md`, Tasks 1–16 committed on `ccr-81a7454e-12gx87`; 225/225 tests pass (Debug + Release) |
| Plan 3 of 6 (Characters & artifacts) | **Done** — `docs/superpowers/plans/2026-10-06-characters-and-artifacts.md`, Tasks 1–13; 318/318 tests pass (Debug + Release); 300-year medium soaks with invariants clean |
| Plan 4 of 6 (Realms & armies) | **Done** — `docs/superpowers/plans/2026-10-06-realms-and-armies.md`, Tasks 1–11; 382/382 tests pass (Debug + Release); 200-year medium soaks (seeds 1, 7, 42) with invariants clean |
| Plan 5 of 6 (LLM layer) | **Done** — `docs/superpowers/plans/2026-10-06-llm-layer.md`, Tasks 1–10; 429/429 tests pass (Debug + Release); all LLM paths tested with a fake provider (no API key was available here) |
| Plan 6 of 6 (Chronicle & hardening) | **Done** — `docs/superpowers/plans/2026-10-06-chronicle-and-hardening.md`, Tasks 1–9; 457/457 tests pass (Debug + Release); spec §11 criteria 1–3 and 5 checked automatically; ~50–90 ms per world year (budget 2 s) |
| Sim core (sub-project 1) | **Complete** |
| Sub-project 2 (Chronicle & why UI data) | Spec drafted — `docs/superpowers/specs/2026-10-06-chronicle-views-design.md`; prototype in progress |

## Next step

1. Merge the working branch into `main` (Plans 4–6).
2. Sub-project 2 (view models for the four designed screens), then 4 (Godot client), 3 (depth) and 5 (narration evals).

Notes from Plan 6 execution:
- Try it: `fw new worlds/w.fw.db --seed 7` (100 years of prehistory, ~6 s), `fw digest …`, `fw follow … char#12`, `fw live … --minutes 10`, `fw structures …`, `fw stats …`.
- Compaction runs inside the simulation (one summary per decade at fifty years old), so worlds stay deterministic however they are played.
- Two sessions on one world file: the second to save is refused rather than silently overwriting.

Notes from Plan 5 execution:
- Turn Claude on per world: `export ANTHROPIC_API_KEY=…; fw llm <world> --provider anthropic --cap 5`; without the key everything runs offline.
- First live run worth doing by hand: `fw advance <world> 1y` with a small `--story-beats` budget, then `fw log` for `motive=`/`account=` and `fw stats` for spend. Live LLM behaviour (in-character choices, odds respected) has not been evaluated yet; that is sub-project 5.

Notes from Plan 4 execution:
- Try it: `fw advance … 100y`, `fw realms …`, `fw realm … <id>`, `fw wars …`, `fw war … <id>`, `fw map … --layer realms`, `fw why … <battle-death-id> --depth 5`.
- Inter-region trade routes were deferred to sub-project 3; regency is implicit (a child ruler costs legitimacy, the marshal leads the army).
- Known pre-existing cost: `PromotionSystem` and `World.ActiveCharacters` walk every character ever created; Plan 6's compaction of the dead addresses it.

Notes from Plan 3 execution:
- Try it: `fw new …`, `fw advance … 60y`, `fw characters …`, `fw life … <id>`, `fw artifact … <id>`, `fw why … <duel-death-id> --depth 5`.
- Plans are now prototyped in a scratch worktree, replayed task by task, reviewed by an independent agent, then generated from the verified commits and executed on the branch.

Notes from Plan 2 execution:
- Try it: `fw new worlds/w.fw.db --seed 7`, `fw map …`, `fw advance … 50y`, `fw regions …`, `fw why … <id>` (e.g. a revolt → its food shortage).
- Balance knobs live as constants in `EconomySystem`, `RegionOdds` and `SettlementSystem`; `WorldHealthTests` guards against collapse.

Notes from Plan 1 execution:
- Toolchain on Linux: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0` (installs to `~/.dotnet`).
- The demo harvest/unrest systems revolt only occasionally (e.g. seed 7 has none in its first 20 years, 29 in 100 years); use `fw advance <file> 100y` when demoing `fw why` on a `RevoltBegan`.
- `CliApp` does not catch `SqliteException`, so pointing `fw` at a non-SQLite file crashes with a stack trace instead of `error:`.

## Key decisions (don't re-ask)

- **Player role:** observer/chronicler — no direct control. The payoff is character achievements, drama, major world history, and above all *why* (a causal event graph).
- **Time:** catch-up on launch + live ticking while open; **1 real day = 1 world year**; max 30 world years caught up per launch.
- **Engine:** Godot 4 + C#; the sim is an engine-agnostic headless .NET 8 library with a `fw` CLI that comes first.
- **Camera:** god's-eye diorama (a floating cross-section world block).
- **World size:** medium-large — 100–120 regions, 6–8 starting realms.
- **Simulation:** day-granular loop with monthly ticks plus a scheduled-event queue; deterministic per seed; every event cites up to 3 earlier events as causes.
- **Depth areas:** characters (notables + aggregate populace), artifacts (provenance, fame, curses), regions/settlements, realms (Empire › Kingdom › Duchy › County) and armies, plus structures (landmarks, labyrinths) as hooks.
- **LLM is core for story beats:** important decisions, **arbitration** of duels/assassinations/delves/rituals (can wound or kill, with fairness guardrails), invention (names, lore), and all narration. Rules + templates are the offline fallback. LLM results are stored in the event log, so replays never re-query.
- **LLM providers:** Anthropic official C# SDK (local testing via `ant auth login`) and OpenRouter; model tiers per job; prompt-cached world bible; a daily story-beat budget and a spend cap.
- **Persistence:** one SQLite file per world (snapshot + append-only event log).
- **UI style:** *clean* "diorama gallery" — light warm-grey ground, italic serif display (Instrument Serif), small sans (Geist), black pill buttons, a floating control card. Inspired by an "Island Jelly" reference image.

## Sub-projects

1. Sim core *(spec written; Plans 1–6)*
2. Chronicle & why UI data
3. Myth, magic & structures (depth)
4. Godot 3D diorama client
5. Narration polish
