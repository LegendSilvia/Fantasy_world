# Fantasy World — Campaign Moves (sub-project 10)

**Status:** C1 in progress · **Asked by:** the owner, 2026-10-27 · **Builds on:** the war system (`MilitarySystem`), the
LLM decider, fights (sub-project 8), the episode film (sub-project 7)

## 1. Purpose

A host today plans its whole road to the war's target at once and the map draws it as one long arrow, often from a far
capital straight over a mountain range (the owner's screenshot, 2026-10-27). Wars should instead unfold in **moves**:
a host makes one move at a time, mountains are barriers crossed only at passes, the commander chooses each move, and
viewers can follow the campaign move by move.

## 2. What exists today (facts from the code)

| Piece | Today |
|---|---|
| Orders | `GiveOrders` each month: go home if beaten; meet an invader on our land; retake lost land; attackers march on the war's target, else the nearest enemy holding |
| Road | `RegionGraph.ShortestPath` by `TravelDays`; the whole path is stored on the army and drawn as one arrow |
| March | 30 travel days a month along the path, stopping where an enemy host stands |
| Mountains | A border mostly of mountain tiles costs 1.5× (`MountainPass`); region centres in mountains cost more by biome. Ranges are crossed freely |
| Choices | `IDecider` (rules: weighted roll; `LlmDecider`: Claude on the decide tier within the story-beat budget) |

## 3. Decisions

### 3.1 Made by the owner

| Topic | Decision |
|---|---|
| Step by step | A host makes one move at a time and the arrow shows only the current move |
| Mountains | Ranges are barriers; crossed only at passes, slowly; a realm behind mountains is protected |
| Who moves | Claude picks the commander's moves (advance, hold, besiege, fall back, strike) |
| Told | The film and its log name the moves, so viewers follow the campaign |

### 3.2 Proposed (flagged for review)

| Topic | Proposed | Why |
|---|---|---|
| One move | One step to a neighbouring region; it may take more than a month when the way is long (a pass) | Readable on the map; a campaign creeps outward from the border |
| Mountain test | Along the straight line between two region centres, the share of mountain tiles. A crossing that is mostly mountain (≥ 60%) and whose shared border has no low tile pair (no pass) is closed to armies; otherwise each step costs `days × (1 + 4 × share)` | Uses the stored terrain only: trade, migration and old worlds are unchanged |
| Goals | Attackers take the nearest enemy holding first, then push on towards the war's target; defenders meet invaders, retake land, or hold | The front advances; no leap to a far target |
| Move events | `ArmyMoved` (from, to, into own/enemy land); minor importance unless it crosses into enemy land | The log and the film can tell the campaign without crowding the stories |
| Claude | The decide tier, counted as a choice (not a fight); the rules choose past the budget. The rules weight the move they would make highest | Same bounds as other choices |

## 4. Milestones

- **C1 — Moves in the sim:** one move at a time; mountains as barriers with passes; frontier-first goals; `ArmyMoved`.
  The arrow shows the current move. Tests.
- **C2 — The commander chooses:** each month's move is a decision (options: advance towards the goal, besiege here,
  hold, fall back, strike at a host next door, go round by another neighbour), answered by Claude within the budget.
- **C3 — Told and shown:** the chronicler's brief for a war carries its moves; the film's log lists them under the war's
  section and the diorama draws each move's arrow as it is told.
