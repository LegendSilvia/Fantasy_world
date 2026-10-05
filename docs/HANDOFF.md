# Handoff — where things stand (2026-10-05)

Context for picking up this project on another machine / in a new Claude session.

## Status

| Step | State |
|---|---|
| Brainstorming | Done |
| UI design (Claude Design canvas) | Done — https://claude.ai/artifact/1q5esun6tMtyMP75sMwsvb |
| Sim core spec | Done, approved — `docs/superpowers/specs/2026-10-05-fantasy-world-sim-core-design.md` |
| Plan 1 of 6 (Sim foundation) | Written — `docs/superpowers/plans/2026-10-05-sim-foundation.md` |
| Plan 1 execution | **Not started** (no code committed yet) |

## Next step

Execute Plan 1 with **subagent-driven development** (chosen), in an isolated worktree / feature branch, not on `main`:

1. Install the .NET 8 SDK if missing (Plan 1, Task 1, Step 1).
2. Start Task 1 and work through Tasks 1–12; each task is TDD + commit.
3. Final whole-branch review, then merge.

Then write Plan 2 (World & regions). The roadmap of Plans 2–6 is in the plan file.

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
