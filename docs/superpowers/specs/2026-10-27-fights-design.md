# Fantasy World — Fights: Declared, Fought, Told (sub-project 8)

**Status:** B1 and B2 built (2026-10-27); B3 to do · **Asked by:** the owner, 2026-10-27 · **Builds on:** the sim's agency and war systems, the
LLM gateway and arbiter (Plans 5–6), the daily broadcast (sub-project 6) and the episode film (sub-project 7)

## 1. Purpose

The world is RPG-like and its people and armies already have stats; viewers should see them. Duels and wars become
events with a build-up and a payoff: **declared in one episode, fought in a later one.** The declaration gets a section of
the film showing both sides' stats and their chances as a two-colour bar; the fight gets its own section, a story of how
it went, how it turned and how it ended. **Claude decides the fight — its course, its length and its end** — from the two
sides' stats. Claude also makes the characters' choices (who challenges whom, which realm declares war).

## 2. What exists today (facts from the code)

| Piece | Today |
|---|---|
| Character stats | Six attributes, 0–20: Martial, Diplomacy, Stewardship, Intrigue, Lore, Prowess (`Model/Character.cs`); traits, renown, wounds, age. Fighting strength = prowess + martial/2 + renown/20, ±brave/craven, −wounds, −youth/age (`Encounters.Strength`) |
| Army stats | Levies, men-at-arms, knights, mages, commander, morale (0–100), supply (`Model/War.cs`); strength = troops × commander × morale × supply (`MilitarySystem.Battles.cs`) |
| Odds | Duel: attacker's odds A/(A+D) (`Encounter.AttackerOdds`); battle: a/(a+d) after home ground and terrain |
| Duels | Chosen on a monthly tick (`DeedsSystem`, "duel"/"avenge" options) and **fought at once**; no declaration; one event `DuelFought` |
| Wars | Declared on the **yearly** tick (`DiplomacySystem`); armies raised next month, battles months later; each battle **one roll** |
| Arbiter | `LlmArbiter` picks a duel/hunt/delve outcome among the plausible ones (≥ 5%) and writes an account; battles use the rules only |
| Broadcast budget | Story beats off (0 a day); narration only |
| Time | One episode = one world year (12 monthly ticks, 1 yearly tick) |

## 3. Decisions

### 3.1 Made by the owner

| Topic | Decision |
|---|---|
| Declaration first | Wars and duels are declared before they are fought, with the fight in a later episode |
| Preview section | Both sides' stats and their chances of winning as a progress bar in two colours, one per side |
| Fight section | Its own narrated story: how the fight went, how it ended, the twist |
| Who decides a fight | **Claude decides everything in the fight: the story and the length** (a day to more than a year), from the stats |
| Claude in the world | Claude decides fights **and the characters' choices** (story beats on), with a larger monthly LLM cap |

### 3.2 Proposed (flagged for review)

| Topic | Proposed | Why |
|---|---|---|
| Bounds on Claude | The outcome must be one the stats make at least plausible (the existing 5% floor and the drama budget); everything else (course, turning points, length, losses within a battle's range, who is wounded) is Claude's | Keeps the stats meaningful — the bar on screen means something — while the story is Claude's |
| Length limits | A duel: 1 day to 2 years; a battle: 1 day to 1 year (a long siege-like stand); a war still ends by the existing peace rules (score, weariness, 10-year cap) | A fight longer than two episodes stops being news |
| Model | Fights and choices on the existing tiers: `arbitrate` (fights) and `decide` (choices) on Claude Opus, `narrate` on Sonnet | As configured today |
| Budget | Story beats 120 a day, of which 40 are kept for fights (so choices can never starve them); monthly cap $25 (from $5) | A world year has many small choices; fights matter most |
| Rules fallback | Over budget or offline, the rules decide as today (a fight then lasts one day) | The world never stops |

## 4. Declared, then fought

An episode covers the ~360 world days since the last broadcast, which do not line up with the world's calendar year,
so "fought at the start of next year" could fall in the same episode. The delay is therefore fixed: **a year and a
day** (361 world days), which always lands in a later episode.

**Duels.** When a character chooses "duel"/"avenge", the sim records `DuelDeclared` (challenger, defender, stakes, place,
odds, the day of the fight) and schedules the fight a year and a day later (`EventQueue`, kind `duel`). Nobody issues or
receives a second challenge while one is pending. If either side has died or gone by then, the duel is called off
(`DuelCalledOff`, with the reason) and told as such; otherwise it is fought (`DuelFought`, citing the challenge and the
grudge).

**Wars.** A declared war first **musters for a year**: `WarDeclared` carries both realms' strengths and the odds, and its
armies are raised and march only once the year is out (`War.HostilitiesDay`), so the declaration is news in one episode
and the fighting begins in the next. Wars declared before this change fight as before.

**Odds** are the same numbers the sim fights with: for a duel `Encounter.AttackerOdds`; for a war, the realms' strength
ratio (`DiplomacySystem.Strength`) and, once armies exist, each battle's a/(a+d). They are computed once at declaration and
stored on the event, so the bar on screen matches what the world will use.

## 5. Claude decides the fight

When a duel's day comes, or two hosts meet, the gateway asks the `arbitrate` tier once for **the whole fight**:

- **In the prompt:** both sides' full stats (attributes, traits, wounds, age, renown, artifacts; or troops, commander,
  morale, supply, terrain), the odds, the stakes, the place and season, and the allowed outcomes with their chances.
- **The answer (JSON, schema-checked):** `outcome` (one of the allowed), `days` (within §3.2's limits), `chapters` — 2 to 5
  dated beats (`day`, `text`) telling the fight's course with at least one turning point — and, for battles, `losses` per
  side (within the rules' ranges) and `fates` for the commanders and knights present (from the rules' list).
- **In the world:** the outcome and fates are applied on the fight's last day (scheduled), the chapters are stored on the
  `DuelFought`/`BattleFought` event and told as they fall: a fight that runs past the year's end appears in the next
  episode as "the duel goes on" with the chapters so far, and ends there or later.
- **Guards:** names and numbers in the chapters must come from the brief (the narration guard); a failed answer is
  retried once, then the rules decide.

## 6. Claude makes the choices

`decide` answers the characters' monthly choices (deeds, duels) and the realms' yearly war choice through the existing
`LlmDecider`, within the existing weighted options (so no choice is outside the rules). The budget (§3.2) bounds it; past
it, the rules choose as today.

## 7. In the episode and the film

| Section | When | On screen |
|---|---|---|
| **The challenge** | A duel or war declared this episode | Both sides side by side: portraits or banners, stats as bars (six attributes for people; troops, commander, morale, supply for hosts), the stakes, and the **odds bar** in the two sides' colours (realm colours for wars, house colours for duels), filling to the odds |
| **The fight** | A fight fought (or going on) this episode | The diorama at the place; each chapter narrated with a caption; the odds bar shifts with the turning points as Claude's chapters tell them; ends on the outcome card |
| Stats elsewhere | Any spotlight or caption | The character card shows the six stats as bars; a host's tag shows its numbers |

The episode script gains these sections; the chronicler (Sonnet) narrates them from the declaration and the chapters,
held by the guard to their facts. Discord's post lists the challenges and results with the odds.

## 8. Milestones

- **B1 — Declarations in the sim:** `DuelDeclared`/`DuelCalledOff`, wars declared at the year's end, mobilisation the next
  year, odds stored. Rules still fight (one day). Tests; the game shows a pending duel.
- **B2 — Claude fights:** the whole-fight arbitration for duels and battles (outcome, days, chapters, losses, fates),
  scheduled endings, fights spanning episodes; the budget split; the broadcast's LLM settings (story beats, cap).
- **B3 — Told and shown:** the challenge and fight sections in the episode script and film (stats bars, odds bar), Discord
  lines. Depends on sub-project 7's film.

Each is planned, reviewed, executed and tried in a dry run before it goes live.
