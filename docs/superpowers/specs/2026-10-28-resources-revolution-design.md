# Fantasy World — Realms' Resources and Revolution (sub-project 12)

**Status:** R1 built (2026-10-28); R2 next · **Asked by:** the owner, 2026-10-27 ("kingdom etc's resources and revolution"); decisions
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

### 3.2 Revolution (R2)

| Topic | Proposed |
|---|---|
| When | A realm whose stability has collapsed, with settlements in revolt (two, or a third of its towns), may see a revolution, at most one at a time |
| Leader | The most fitting discontented notable of the realm, not the ruler or the ruler's heir: ambitious or wronged, bold, from a revolting town; becomes a rebel |
| Civil war | The rebels hold the revolting towns' regions as a rebel realm under the leader, at war with the crown (casus belli "revolution"); the war system fights it |
| Ends | Crown wins: the rebel realm falls, its land returns, the leader dies or flees. Rebels win: the leader chooses (Claude, within the choice budget; the rules weigh it otherwise) to **overthrow** the ruler (takes the throne; the realm may change its government) or **break away** (the rebel realm becomes independent) |
| Told | Revolution begun and ended are headline news; the civil war gets a face-off |

### 3.3 Told and shown (R3)

The realm screen lists its stocks and shortages; the face-off adds Ore and Horses for wars; the website shows the same.

## 4. Milestones

- **R1 — Stocks:** realm stocks, spending, shortages, arms, building, contentment. Tests.
- **R2 — Revolution:** discontent, the leader, the civil war, its three ends, Claude's choice. Tests.
- **R3 — Shown:** realm screen, face-off figures, website.
