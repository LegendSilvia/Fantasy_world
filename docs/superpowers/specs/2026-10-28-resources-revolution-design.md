# Fantasy World — Realms' Resources and Revolution (sub-project 12)

**Status:** R1–R3 built (2026-10-28) · **Asked by:** the owner, 2026-10-27 ("kingdom etc's resources and revolution"); decisions
2026-10-28 · **Builds on:** the economy (region deposits), realms' stability, settlement revolts, the war system, the
decider (Claude)

## 1. Purpose

Realms today have land, treasure, legitimacy and stability; regions have deposits (grain, timber, ore, stone, horses,
fish and the rare mithril, dragonbone and arcane crystal) that only earn wealth. Settlements can revolt for a season over
hunger, plague or danger, and vassals can break away, but a realm's people never rise against its ruler. This
sub-project gives realms **stocks** of what their land yields, makes the stocks **matter** (arms, building, contentment)
and lets discontent grow into a **revolution**.

## 2. Decisions (the owner, 2026-10-28)

| Topic | Decision |
|---|---|
| What resources do | They **affect stability and how often riots and revolutions happen**; they **arm the hosts** (ore and horses make armies stronger and faster); they are **spent on building** (timber and stone for walls, castles and great works) |
| How far a revolution goes | **All the way:** a rebel leader rises; a civil war follows; the rebels may be crushed, break away as a new realm, or overthrow the ruler and even change the government. Claude decides the leader's choices |

## 3. Proposed design (flagged for review)

### 3.1 Stocks (R1)

| Topic | Proposed |
|---|---|
| Stocks | Each realm keeps a stock of each non-food resource its domain yields (ore, horses, timber, stone and the rare three), filled monthly by its regions' deposit yields; a stock holds at most two years' output |
| Spending | Hosts in the field use ore and horses each month (more for knights); raising a host costs ore at once; a great work or fortress uses timber and stone each year it rises |
| Shortage | A realm with a host in the field and no ore, or a work rising and no timber or stone, is **short**: a shortage begins as an event (citing what drained it) and ends when the stock recovers |
| Arms | A host's strength is scaled by its realm's ore (short: three quarters) and horses (in hand: a tenth more); with horses a host marches a fifth faster |
| Building | A work uses 50 timber and 50 stone a year; what the stock lacks is bought from the treasury (2 gold each); a work whose realm can do neither is held up, like war or plague holds it up today |
| Contentment | Each kind in plenty raises the realm's stability target a little (at most three); each shortage lowers it and raises its settlements' unrest, so want breeds riots and riots breed revolution |

### 3.2 Revolution (R2, as built)

| Topic | Built |
|---|---|
| When | Yearly, an independent realm that is **unsettled** (stability below 35) with towns in revolt beyond its capital's land, and no revolution under way, rises with a chance of 30% for each such town (at most 75%). Want, instability and war weariness stir its towns (unrest rises as stability falls below 45, by a tenth of war weariness, and by 10 for each shortage), so riots breed revolution |
| Leader | An adult of a revolting town who is not the ruler, the ruler's heir or a lord: the more martial, cunning and persuasive, renowned, ambitious, bold and wronged by the ruler, and a rebel first. Takes the town (a lord there is put out) |
| Rebel realm | The revolting towns' lands, named after the leader's land (or town), new colours, the towns' coffers and the lands' share of the crown's treasury and stores |
| Civil war | The rebels declare war on the crown (casus belli "revolution", "The … Revolution"); the war system fights it, with the usual muster of a year and a day and a face-off |
| Ends | **Crushed** (the crown wins): the rebel realm folds back into the crown, the leader is executed or flees into exile. **Breakaway** (a stalemate, or the leader's choice): the rebels stay free. **Overthrow** (the leader's choice): the leader takes the crown's throne (the old ruler executed or exiled), the rebel realm folds into the crown, and if the leader chose to remake it, its government changes (a merchant republic under a steward, else an elective kingdom; an elective kingdom is made a crown). The leader chooses through the decider (Claude, within the choice budget): break away, seize, or remake |
| Told | `RevolutionBegan` and `RevolutionEnded` are headline news (importance 80, with templates for each end), `FledIntoExile` for the exiled; the history replay follows the rebel realm's land |

In six 150-year test worlds: ten revolutions, five crushed, three broken away, two overthrown.

### 3.3 Told and shown (R3, as built)

The realm screen (game and website) lists its **stores**: each kind it holds or yields, the amount, the yearly yield, and
"short" when it is. A war's face-off adds **Ore** and **Horses** in store (both sides on one scale, like the treasury) and
notes each side's shortages ("short of ore"); the film card shows the two extra rows and the chronicler is given them.
Revolutions are told by their own headline events (§3.2) and their civil war has the war face-off.

## 4. Milestones

- **R1 — Stocks:** realm stocks, spending, shortages, arms, building, contentment. Tests.
- **R2 — Revolution:** discontent, the leader, the civil war, its three ends, Claude's choice. Tests.
- **R3 — Shown:** realm screen, face-off figures, website.
