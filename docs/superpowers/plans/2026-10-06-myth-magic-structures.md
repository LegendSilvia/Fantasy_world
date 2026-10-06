# Myth, Magic & Structures Implementation Plan (sub-project 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the world's hooks depth so it grows its own myths, each with causes you can follow: named beasts that raid until a hero ends them, trade roads that wars sever, faiths that spread, split and go to war over holy lands, saints and their relics, ley tides and wild magic, magic laws, great works that take a generation (and can fall), fortresses, and labyrinths delved level by level past a guardian. Everything appears through the existing views and the diorama.

**Architecture:** Four new systems (`TradeSystem`, `MonsterSystem`, `FaithSystem`, `MagicSystem`) join `DefaultSystems` after the military; `StructureSystem`, `DeedsSystem` and `DiplomacySystem` grow. New entities (`TradeRoute`, `Monster`) live in the `World`, are saved in the snapshot (version 6) and checked by invariants; new state on existing entities (faith zeal, saints, labyrinth levels, architects) rides in their JSON. Views gain optional fields, and the client draws roads, beasts, temples, towers, fortresses and shrines.

**Tech Stack:** unchanged (.NET 8 / C# 12, xUnit, Godot 4.3 .NET).

**Spec:** `docs/superpowers/specs/2026-10-06-myth-magic-structures-design.md`. **Builds on:** the sim core (Plans 1–6), the chronicle views (sub-project 2) and the Godot client (sub-project 4).

## Global Constraints

Everything in the earlier plans' Global Constraints still applies. In addition:

- **Causes, not dice.** Every new event cites what made it possible when something did (a surge, a war, a loss, a rising); rolls only decide *whether* and *when*.
- **State lives in the world.** Systems hold no fields beyond their collaborators; anything a later tick needs is on an entity.
- **History replays from events that outlive compaction** (importance ≥ 30 or kept), so every change the map replays is recorded at that importance.
- **Determinism:** ties are broken by id, decisions taken on last year's state are applied together, and every roll uses the context's RNG.
- **Budgets:** each new system stays well inside the 2 s per world-year budget at 120 regions (a 300-year medium world takes about 17 s with everything on).
- Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Decisions made while drafting (review these)

| Topic | Drafted choice | Why |
|---|---|---|
| Order | Trade routes first (the layer the client already showed disabled), then monsters, faith, magic, works, labyrinths, views | The spec's outline, with routes and beasts brought forward so the map shows them early |
| Monster cap | One roaming or slumbering beast per eight regions; guardians aside | Enough legends without a bestiary |
| Conversion pace | 4% a year at most, scaled by pressure against the region's own devotion; a people that turned keeps its faith 40 years | Without the settling period lands flipped back and forth a dozen times |
| Saints | Devout and renown ≥ 45 at death; no roll | One coin flip decided whether anyone was ever canonised |
| Holy land lost | Only by a cession or the holder's conversion | Holy lands already held by another faith at the founding were reported "lost" in Year 2 |
| Wild magic odds | 25% / 8% / 3% a year during a surge at a tower (embraced / licensed / outlawed) | Rare but felt, and the law matters |
| New causes of war | Border disputes between declared rivals; glory for rulers who crave renown, against weaker neighbours only | With the new history one seed went a century without a war |
| Labyrinth depth | Shared by all delvers (the way down is remembered) | Delves read as a story across generations |
| Plan size | 11 tasks (the spec outlined 9) | The map tasks for roads and beasts were split from their systems |

**Verification:** each task's xUnit tests fail first and pass after its implementation, and the headless Godot checks pass with the counts given (the code was replayed task by task in this order). Calibration was done on 300-year medium worlds (seeds 7 and 42): about 140–170 conversions, 10–30 saints, 2–8 wild-magic events, 20–40 landmarks, 5–10 fortresses, 13–27 labyrinths and 70–80 wars. An independent review of the prototype found that restored roads, collapsed works and fortresses under construction replayed wrongly on past maps, that a region's settling period and the magic laws relied on a region memory that forgets, that a holy land left to no realm counted as regained, that a fortress could finish in an abandoned town, that raids never cut roads, and that most holy wars cited no cause; all are fixed in the code below and covered by tests.

## File Structure

```
sim/FantasyWorld.Sim/Model/      TradeRoute.cs (1); Monster.cs (3, 9); Culture.cs (5, 6); Character.cs (6); Structure.cs (8, 9); War.cs (8)
sim/FantasyWorld.Sim/Systems/    TradeSystem.cs (1); MonsterSystem.cs (3, 9); FaithSystem.cs (5, 6, 9); MagicSystem.cs (7);
                                 StructureSystem.cs (8, 9); DeedsSystem.cs (3, 9); DiplomacySystem.cs (6, 8, 11); RegionSystem.cs,
                                 SettlementSystem.cs (7); MilitarySystem.cs (5); MilitarySystem.Sieges.cs (8); DefaultSystems.cs
sim/FantasyWorld.Sim/Views/      MapScreen.cs (2, 4, 10); ChronicleScreens.cs, CharacterScreen.cs (10); History.cs (5); Category.cs
sim/FantasyWorld.Client/         RouteLayout.cs (2); Markers.cs, Palette.cs (10)
game/scripts/                    Diorama.cs (2, 4, 10); Main.cs (4); screens/RealmView.cs, CharacterView.cs (10)
sim/FantasyWorld.Sim.Tests/      Systems/{TradeSystem,MonsterSystem,FaithSystem,MagicSystem,GreatWorks,Labyrinth}Tests.cs; Engine/MythHealthTests.cs
README.md, docs/images/town.png (11)
```

All commands run from the repository root. Requires the .NET 8 SDK and Godot 4.3 .NET on the `PATH` as `godot` (for the headless checks and Task 11's screenshot).

---

### Task 1: Trade routes

Spec §8. Towns and cities are markets. Each year a few pairs within a forty-day journey along the region graph open a road when their realms are at peace with each other and the way is safe; each road carries wealth both ways in proportion to the smaller market. Each month a road cut by war between its markets' realms, by an occupied region on the way or by danger is severed (citing the war, the occupation or the danger), and it reopens once the way is clear; a road whose market is abandoned closes for good. Routes are entities with names, saved with the world (snapshot version 6) and checked by invariants.

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/TradeRoute.cs`, `sim/FantasyWorld.Sim/Systems/TradeSystem.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/Query/Names.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/TradeSystemTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/why.json`

**Interfaces:**
- Consumes: `RegionGraph.ShortestPath`, `Realms.SovereignOf`, `World.Wars`, the event log (Plans 1–6).
- Produces: `enum RouteState { Open, Severed, Closed }`; `sealed class TradeRoute { Kind = "route"; Id; Name; FromId; ToId; Regions; State; Volume; OpenedEventId; SeveredEventId; Ref }`; `World.Routes`; `WorldSnapshot.Routes` (`CurrentVersion = 6`); `sealed class TradeSystem : ISimSystem { MaxTravelDays = 40; OpenChance; MaxNewPerYear; MaxPerMarket; DangerLimit = 60; FlowShare; IsMarket(s) }` (after `MilitarySystem` in `DefaultSystems`); events `RouteOpened`, `RouteSevered` (payload `reason`: war, occupation, danger), `RouteRestored`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/TradeSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class TradeSystemTests
{
    /// <summary>A tiny world with exactly two markets in neighbouring regions of different realms.</summary>
    private static (Simulator Sim, Settlement A, Settlement B) TwoMarkets()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var link = world.Geography!.Graph.Links.First(l => Realms.SovereignOf(world, world.Regions[l.A]) is { } x && Realms.SovereignOf(world, world.Regions[l.B]) is { } y
                                                          && x.Id != y.Id
                                                          && world.LivingSettlements(world.Regions[l.A]).Any() && world.LivingSettlements(world.Regions[l.B]).Any());
        foreach (var s in world.Settlements.Values) s.Tier = SettlementTier.Village;
        var a = world.LivingSettlements(world.Regions[link.A]).First();
        var b = world.LivingSettlements(world.Regions[link.B]).First();
        foreach (var (s, wealth) in new[] { (a, 4000.0), (b, 1000.0) }) (s.Tier, s.Wealth) = (SettlementTier.Town, wealth);
        foreach (var region in world.Regions.Values) region.Danger = 0;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new TradeSystem() }, checkInvariants: true);
        return (sim, a, b);
    }

    [Fact]
    public void MarketsAtPeace_OpenARoad_ThatEnrichesBoth()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = Assert.Single(sim.World.Routes.Values);
        Assert.Equal((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), (route.FromId, route.ToId));
        Assert.Equal(sim.World.Settlements[route.FromId].RegionId, route.Regions[0]);
        Assert.Equal(sim.World.Settlements[route.ToId].RegionId, route.Regions[^1]);
        Assert.Equal(RouteState.Open, route.State);
        Assert.True(route.Volume > 0);
        Assert.True(b.Wealth > 1000);
        var opened = sim.Log.Get(route.OpenedEventId);
        Assert.Equal("RouteOpened", opened.Type);
        Assert.Equal(route.Ref, opened.Actors["route"]);
    }

    [Fact]
    public void War_SeversTheRoad_AndPeaceReopensIt()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = sim.World.Routes.Values.Single();
        var world = sim.World;
        var (x, y) = (Realms.SovereignOf(world, a)!.Id, Realms.SovereignOf(world, b)!.Id);
        var cb = sim.Log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = sim.Log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Toll War", AttackerId = x, DefenderId = y, Attackers = { x }, Defenders = { y },
            CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id, DeclaredEventId = declared.Id, StartDay = world.Date.Day,
        };
        world.Wars[war.Id] = war;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Severed, route.State);
        var severed = sim.Log.Get(route.SeveredEventId);
        Assert.Equal("war", severed.Payload["reason"]);
        Assert.Equal(declared.Id, severed.Causes.Single().EventId);
        Assert.Equal(0, route.Volume);

        war.Ended = true;
        war.EndedEventId = sim.Log.Append(world.Date, new EventDraft("PeaceSigned")).Id;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Open, route.State);
        Assert.Equal(route.SeveredEventId, sim.Log.All.Last(e => e.Type == "RouteRestored").Causes.Single().EventId);
    }

    [Fact]
    public void AnAbandonedMarket_ClosesItsRoads()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        foreach (var c in sim.World.Characters.Values.Where(c => c.SettlementId == a.Id)) c.SettlementId = b.Id;
        if (a.LordId != 0) a.LordId = 0;
        a.Abandoned = true;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Contains(sim.Log.All, e => e.Type == "RouteClosed");
    }

    [Fact]
    public void AMarketThatShrinksBelowATown_ClosesItsRoads()
    {
        var (sim, a, _) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        a.Tier = SettlementTier.Village;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Equal(0, sim.World.Routes.Values.Single().Volume);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json` entirely with:
```json
{
  "ref": "char#1",
  "name": "Fath of Cunvei",
  "house": "house#1",
  "kicker": "Character · House Cunvei",
  "line": "Lord of Drerithby · age 66 · born before recorded history",
  "traits": [
    "calm",
    "devout",
    "lazy",
    "trusting"
  ],
  "ambition": "Ambition: mastery of magic",
  "location": {
    "ref": "settlement#3",
    "label": "Now at Drerithby",
    "position": {
      "x": 0.3515625,
      "y": 0.1953125
    }
  },
  "timeline": [
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted Keth of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 58,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 32,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted End of Gil in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 10,
            "entity": "char#15"
          },
          {
            "start": 53,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 33,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 45,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei knighted Teilbeith of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 20,
            "entity": "char#18"
          },
          {
            "start": 63,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 126,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei and Lond of Cunvei became rivals over calm vs wrathful.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 19,
            "length": 14,
            "entity": "char#3"
          }
        ]
      },
      "eventId": 131,
      "highlight": false
    },
    {
      "year": 18,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 138,
      "highlight": false
    }
  ],
  "folded": 0,
  "earlier": 0,
  "ties": [
    {
      "ref": "char#2",
      "name": "Farnfand of Cunvei",
      "relation": "Spouse",
      "qualifier": "",
      "tone": "warm"
    },
    {
      "ref": "char#3",
      "name": "Lond of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    },
    {
      "ref": "char#4",
      "name": "Broth of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    }
  ],
  "followed": false
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "year": 21,
  "title": "Year 21.",
  "subtitle": "While you were away, 20 years passed in Dromothholt. The seasons turned, and 151 events were recorded.",
  "elapsedDays": 7200,
  "status": "Caught up · Year 21, day 1",
  "pins": [
    {
      "number": 1,
      "place": "settlement#3",
      "label": "Drerithby",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "eventId": 30
    },
    {
      "number": 3,
      "place": "region#7",
      "label": "Gisreach",
      "position": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "eventId": 149
    },
    {
      "number": 4,
      "place": "region#3",
      "label": "Thonae",
      "position": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "eventId": 64
    },
    {
      "number": 5,
      "place": "settlement#16",
      "label": "Greindrondcross",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "eventId": 32
    }
  ],
  "headline": {
    "number": 1,
    "category": "politics",
    "kicker": "A new era",
    "title": {
      "text": "The Banners Age of Thonae began.",
      "links": []
    },
    "body": {
      "text": "Spring of Year 2.",
      "links": []
    },
    "eventId": 30,
    "canExplain": false
  },
  "items": [
    {
      "number": 2,
      "category": "politics",
      "text": {
        "text": "The Age of Long Peace of Thonae began.",
        "links": []
      },
      "eventId": 46
    },
    {
      "number": 3,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia slew the monsters of Gisreach.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 37,
            "length": 8,
            "entity": "region#7"
          }
        ]
      },
      "eventId": 149
    },
    {
      "number": 4,
      "category": "nature",
      "text": {
        "text": "Plague broke out in Thonae.",
        "links": [
          {
            "start": 20,
            "length": 6,
            "entity": "region#3"
          }
        ]
      },
      "eventId": 64
    },
    {
      "number": 5,
      "category": "characters",
      "text": {
        "text": "Fath of Cunvei knighted Keth of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 58,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 32
    },
    {
      "number": 6,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia and Nasgre of Stel became friends.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#19"
          }
        ]
      },
      "eventId": 67
    },
    {
      "number": 7,
      "category": "economy",
      "text": {
        "text": "Merchants opened The Drerithby–Greindrondcross Road between Drerithby and Greindrondcross.",
        "links": [
          {
            "start": 17,
            "length": 34,
            "entity": "route#2"
          },
          {
            "start": 60,
            "length": 9,
            "entity": "settlement#3"
          },
          {
            "start": 74,
            "length": 15,
            "entity": "settlement#16"
          }
        ]
      },
      "eventId": 47
    }
  ]
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, one open labyrinth.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      }
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      }
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      }
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      }
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      }
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      }
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      }
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      }
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      }
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      }
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      }
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      }
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      }
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      }
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      }
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      }
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      }
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      }
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      }
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      }
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      }
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      }
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      }
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      }
    }
  ],
  "structures": [
    {
      "ref": "structure#1",
      "name": "The Deeps of Taramdale",
      "kind": "Labyrinth",
      "state": "Standing",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      }
    }
  ],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ],
  "armies": [],
  "battles": []
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/why.json` entirely with:
```json
{
  "outcome": {
    "eventId": 61,
    "year": 5,
    "category": "characters",
    "title": {
      "text": "Keth of Bandbia died of childbirth at 29.",
      "links": [
        {
          "start": 0,
          "length": 15,
          "entity": "char#17"
        }
      ]
    },
    "note": null,
    "isRoot": false,
    "hasMore": false
  },
  "layers": [
    [
      {
        "eventId": 60,
        "year": 5,
        "category": "characters",
        "title": {
          "text": "Steil of Bandbia was born at Greindrondcross to Keth of Bandbia and Gral of Bandbia.",
          "links": [
            {
              "start": 0,
              "length": 16,
              "entity": "char#31"
            },
            {
              "start": 29,
              "length": 15,
              "entity": "settlement#16"
            },
            {
              "start": 48,
              "length": 15,
              "entity": "char#17"
            },
            {
              "start": 68,
              "length": 15,
              "entity": "char#16"
            }
          ]
        },
        "note": null,
        "isRoot": true,
        "hasMore": false
      }
    ]
  ],
  "edges": [
    {
      "from": 60,
      "to": 61,
      "kind": "trigger",
      "label": "because"
    }
  ],
  "summary": "Traced back within the year, through one root cause.",
  "people": [
    {
      "ref": "char#17",
      "name": "Keth of Bandbia"
    },
    {
      "ref": "char#31",
      "name": "Steil of Bandbia"
    },
    {
      "ref": "char#16",
      "name": "Gral of Bandbia"
    }
  ],
  "places": [
    {
      "ref": "settlement#16",
      "name": "Greindrondcross"
    }
  ],
  "systems": [
    "characters"
  ],
  "consequences": []
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'TradeSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }

        foreach (var r in world.Routes.Values)
        {
            if (!world.Settlements.ContainsKey(r.FromId) || !world.Settlements.ContainsKey(r.ToId)) errors.Add($"Route #{r.Id} joins an unknown market.");
            else if (r.Regions.Count == 0 || r.Regions[0] != world.Settlements[r.FromId].RegionId || r.Regions[^1] != world.Settlements[r.ToId].RegionId)
                errors.Add($"Route #{r.Id} does not run from market to market.");
            if (r.Regions.Any(id => !world.Regions.ContainsKey(id))) errors.Add($"Route #{r.Id} crosses an unknown region.");
            if (!log.TryGet(r.OpenedEventId, out _)) errors.Add($"Route #{r.Id} cites missing event #{r.OpenedEventId}.");
            if (r.State != RouteState.Open && r.Volume != 0) errors.Add($"Route #{r.Id} carries trade while {r.State}.");
        }
    }
}
```

`sim/FantasyWorld.Sim/Model/TradeRoute.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum RouteState { Open, Severed, Closed }

/// <summary>
/// A trade road between two markets (sub-project 3 §8): the regions it crosses, end to end, how much wealth it carried
/// last year, and whether it is open, severed (by war, occupation or danger, and may reopen) or closed for good (a
/// market was abandoned).
/// </summary>
public sealed class TradeRoute
{
    public const string Kind = "route";

    public long Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>The two market settlements, the lower id first.</summary>
    public long FromId { get; init; }
    public long ToId { get; init; }

    /// <summary>The regions the road crosses, from the first market's region to the second's.</summary>
    public List<long> Regions { get; init; } = new();

    public RouteState State { get; set; }

    /// <summary>Wealth the road carried each way last year.</summary>
    public double Volume { get; set; }

    public long OpenedEventId { get; init; }
    public long SeveredEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Model/World.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>All simulated state. Grows with characters, realms and armies in Plans 3–4.</summary>
public sealed class World
{
    public required ulong Seed { get; init; }

    /// <summary>The world's own name (shown on every screen); empty for worlds made before it was named.</summary>
    public string Name { get; set; } = "";
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    /// <summary>Named scalar state for systems without entities of their own.</summary>
    public SortedDictionary<string, double> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Named references to "currently active" events, e.g. an ongoing shortage.</summary>
    public SortedDictionary<string, long> Markers { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The static map. Null only for bare worlds built in tests.</summary>
    public Geography? Geography { get; set; }

    public IdGen Ids { get; init; } = new();
    public SortedDictionary<long, Region> Regions { get; init; } = new();
    public SortedDictionary<long, Settlement> Settlements { get; init; } = new();
    public SortedDictionary<long, Culture> Cultures { get; init; } = new();
    public SortedDictionary<long, Faith> Faiths { get; init; } = new();
    public SortedDictionary<long, House> Houses { get; init; } = new();
    public SortedDictionary<long, Character> Characters { get; init; } = new();
    public SortedDictionary<long, Artifact> Artifacts { get; init; } = new();
    public SortedDictionary<long, Realm> Realms { get; init; } = new();
    public SortedDictionary<long, War> Wars { get; init; } = new();
    public SortedDictionary<long, Army> Armies { get; init; } = new();
    public SortedDictionary<long, Structure> Structures { get; init; } = new();
    public SortedDictionary<long, TradeRoute> Routes { get; init; } = new();

    /// <summary>Region → realm at world creation: the baseline that history replays from (map history slider).</summary>
    public SortedDictionary<long, long> FoundingOwners { get; init; } = new();

    /// <summary>Realm → liege at world creation (0 = independent), the baseline for replaying vassalage.</summary>
    public SortedDictionary<long, long> FoundingLieges { get; init; } = new();

    /// <summary>Living notables (not faded), in id order.</summary>
    public IEnumerable<Character> ActiveCharacters => Characters.Values.Where(c => c.IsActive);

    /// <summary>Living notables who live in the settlement, in id order.</summary>
    public IEnumerable<Character> PeopleOf(Settlement settlement) =>
        ActiveCharacters.Where(c => c.SettlementId == settlement.Id);

    /// <summary>The region's settlements that are still inhabited, in id order.</summary>
    public IEnumerable<Settlement> LivingSettlements(Region region) =>
        region.Settlements.Select(id => Settlements[id]).Where(s => !s.Abandoned);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Serializable world + queue state. The event log and the static geography are stored separately.
/// Capture references the live entities, so serialize the snapshot immediately.
/// </summary>
public sealed record WorldSnapshot(
    int SchemaVersion,
    ulong Seed,
    long Day,
    DateTimeOffset LastRealUtc,
    long LastSeenEventId,
    SortedDictionary<string, double> Stats,
    SortedDictionary<string, long> Markers,
    List<ScheduledAction> Queue,
    long QueueNextSeq,
    SortedDictionary<string, long>? Ids = null,
    SortedDictionary<long, Region>? Regions = null,
    SortedDictionary<long, Settlement>? Settlements = null,
    SortedDictionary<long, Culture>? Cultures = null,
    SortedDictionary<long, Faith>? Faiths = null,
    SortedDictionary<long, House>? Houses = null,
    SortedDictionary<long, Character>? Characters = null,
    SortedDictionary<long, Artifact>? Artifacts = null,
    SortedDictionary<long, Realm>? Realms = null,
    SortedDictionary<long, War>? Wars = null,
    SortedDictionary<long, Army>? Armies = null,
    SortedDictionary<long, Structure>? Structures = null,
    string? Name = null,
    SortedDictionary<long, long>? FoundingOwners = null,
    SortedDictionary<long, long>? FoundingLieges = null,
    SortedDictionary<long, TradeRoute>? Routes = null)
{
    public const int CurrentVersion = 6;

    public static WorldSnapshot Capture(World world, EventQueue queue) => new(
        CurrentVersion,
        world.Seed,
        world.Date.Day,
        world.LastRealUtc,
        world.LastSeenEventId,
        Sorted.From(world.Stats),
        Sorted.From(world.Markers),
        queue.Snapshot().ToList(),
        queue.NextSeq,
        Sorted.From(world.Ids.Next),
        world.Regions,
        world.Settlements,
        world.Cultures,
        world.Faiths,
        world.Houses,
        world.Characters,
        world.Artifacts,
        world.Realms,
        world.Wars,
        world.Armies,
        world.Structures,
        world.Name,
        world.FoundingOwners,
        world.FoundingLieges,
        world.Routes);

    /// <summary>Rebuilds the world. Older snapshots lack newer entities, which load empty.</summary>
    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
        Ids = new IdGen { Next = Ids ?? new SortedDictionary<string, long>() },
        Regions = Copy(Regions),
        Settlements = Copy(Settlements),
        Cultures = Copy(Cultures),
        Faiths = Copy(Faiths),
        Houses = Copy(Houses),
        Characters = Copy(Characters),
        Artifacts = Copy(Artifacts),
        Realms = Copy(Realms),
        Wars = Copy(Wars),
        Armies = Copy(Armies),
        Structures = Copy(Structures),
        Name = Name ?? "",
        FoundingOwners = FoundingOwners is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingOwners),
        FoundingLieges = FoundingLieges is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingLieges),
        Routes = Copy(Routes),
    };

    private static SortedDictionary<long, T> Copy<T>(SortedDictionary<long, T>? source) =>
        source is null ? new SortedDictionary<long, T>() : new SortedDictionary<long, T>(source);

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}
```

Replace `sim/FantasyWorld.Sim/Query/Names.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Query;

/// <summary>Display names for entities, shared by the CLI, briefs and narration.</summary>
public static class Names
{
    public static string Full(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;

    public static Func<EntityRef, string?> Of(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? Full(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        Realm.Kind => world.Realms.TryGetValue(entity.Id, out var realm) ? Statecraft.Title(realm) : null,
        War.Kind => world.Wars.TryGetValue(entity.Id, out var war) ? war.Name : null,
        Army.Kind => world.Armies.TryGetValue(entity.Id, out var army) ? army.Name : null,
        Structure.Kind => world.Structures.TryGetValue(entity.Id, out var structure) ? structure.Name : null,
        TradeRoute.Kind => world.Routes.TryGetValue(entity.Id, out var route) ? route.Name : null,
        Culture.Kind => world.Cultures.TryGetValue(entity.Id, out var culture) ? culture.Name : null,
        Faith.Kind => world.Faiths.TryGetValue(entity.Id, out var faith) ? faith.Name : null,
        _ => null,
    };
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

`sim/FantasyWorld.Sim/Systems/TradeSystem.cs`:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Trade routes (sub-project 3 §8). Towns and cities are markets; each year a few pairs within a journey's reach open
/// a road along the region graph when their realms are at peace with each other and the way is safe. A road carries
/// wealth both ways each year in proportion to the smaller market. Each month, a road that a war cuts (an occupied
/// region on the way, or its markets' realms at war with each other) or that grows too dangerous is severed, and it
/// reopens once the way is clear again; a road whose market is abandoned or shrinks below a town closes for good.
/// </summary>
public sealed class TradeSystem : ISimSystem
{
    public const double MaxTravelDays = 40;
    public const double OpenChance = 0.12;
    public const int MaxNewPerYear = 3;
    public const int MaxPerMarket = 3;
    public const double DangerLimit = 60;
    public const double FlowShare = 0.03;

    public string Name => "trade";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var route in world.Routes.Values.Where(r => r.State != RouteState.Closed).ToList())
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            if (!IsMarket(from) || !IsMarket(to))
            {
                // A market abandoned, or shrunk below a town, ends the road for good.
                route.State = RouteState.Closed;
                route.Volume = 0;
                var gone = !IsMarket(from) ? from : to;
                var why = ctx.Log.Involving(gone.Ref).LastOrDefault(e => e.Type is "SettlementAbandoned" or "SettlementDeclined");
                ctx.Emit(new EventDraft("RouteClosed", Actors: Ends(route, from, to), Place: gone.Ref,
                    Causes: why is null ? null : new[] { new Cause(why.Id, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
                continue;
            }
            var blocked = Blocked(ctx, route);
            if (route.State == RouteState.Open && blocked is { } cut)
                Sever(ctx, route, cut.Reason, cut.Place ?? from.Ref, cut.Cause);
            else if (route.State == RouteState.Severed && blocked is null)
            {
                route.State = RouteState.Open;
                // Recorded at a weight compaction keeps, so past maps see the road reopen.
                ctx.Emit(new EventDraft("RouteRestored", Actors: Ends(route, from, to),
                    Place: from.Ref, Causes: new[] { new Cause(route.SeveredEventId, CauseKind.Enabler, 0.5) },
                    Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
            }
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open))
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            route.Volume = FlowShare * Math.Min(from.Wealth, to.Wealth);
            from.Wealth += route.Volume;
            to.Wealth += route.Volume;
        }
        Open(ctx);
    }

    /// <summary>Cuts an open road, citing what cut it.</summary>
    public static void Sever(SimContext ctx, TradeRoute route, string reason, EntityRef place, long? cause)
    {
        var world = ctx.World;
        route.State = RouteState.Severed;
        route.Volume = 0;
        route.SeveredEventId = ctx.Emit(new EventDraft("RouteSevered", Actors: Ends(route, world.Settlements[route.FromId], world.Settlements[route.ToId]),
            Place: place, Payload: new Dictionary<string, string> { ["reason"] = reason },
            Causes: cause is { } c ? new[] { new Cause(c, CauseKind.Trigger, 0.9) } : null,
            Importance: 30, Tags: new[] { "Economy" })).Id;
    }

    private static Dictionary<string, EntityRef> Ends(TradeRoute route, Settlement from, Settlement to) =>
        new() { ["route"] = route.Ref, ["from"] = from.Ref, ["to"] = to.Ref };

    public static bool IsMarket(Settlement s) => !s.Abandoned && s.Tier >= SettlementTier.Town;

    private static void Open(SimContext ctx)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        if (graph is null) return;
        var markets = world.Settlements.Values.Where(IsMarket).ToList();
        int Roads(long id) => world.Routes.Values.Count(r => r.State != RouteState.Closed && (r.FromId == id || r.ToId == id));
        var linked = world.Routes.Values.Where(r => r.State != RouteState.Closed).Select(r => (r.FromId, r.ToId)).ToHashSet();
        var opened = 0;
        for (var i = 0; i < markets.Count && opened < MaxNewPerYear; i++)
            for (var j = i + 1; j < markets.Count && opened < MaxNewPerYear; j++)
            {
                var (a, b) = (markets[i], markets[j]);
                if (a.RegionId == b.RegionId || linked.Contains((a.Id, b.Id)) || Roads(a.Id) >= MaxPerMarket || Roads(b.Id) >= MaxPerMarket) continue;
                if (graph.ShortestPath(a.RegionId, b.RegionId) is not { } way || way.Days > MaxTravelDays) continue;
                if (AtWar(world, a, b) || way.Path.Any(r => world.Regions[r].OccupierRealmId != 0 || world.Regions[r].Danger >= DangerLimit)) continue;
                if (!ctx.Rng.Chance(OpenChance)) continue;
                var id = world.Ids.Take(TradeRoute.Kind);
                var road = ctx.Emit(new EventDraft("RouteOpened",
                    Actors: new Dictionary<string, EntityRef> { ["route"] = new(TradeRoute.Kind, id), ["from"] = a.Ref, ["to"] = b.Ref },
                    Place: a.Ref, Importance: 25, Tags: new[] { "Economy" }));
                var route = new TradeRoute
                {
                    Id = id, Name = $"The {a.Name}–{b.Name} Road", FromId = a.Id, ToId = b.Id,
                    Regions = way.Path.ToList(), State = RouteState.Open, OpenedEventId = road.Id,
                };
                world.Routes[route.Id] = route;
                linked.Add((a.Id, b.Id));
                opened++;
            }
    }

    /// <summary>Why a road cannot be used now (and the event to blame), or null when the way is clear.</summary>
    private static (string Reason, EntityRef? Place, long? Cause)? Blocked(SimContext ctx, TradeRoute route)
    {
        var world = ctx.World;
        var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
        if (AtWar(world, from, to))
        {
            var war = world.Wars.Values.Where(w => !w.Ended).FirstOrDefault(w => Opposed(w, Realms.SovereignOf(world, from)?.Id, Realms.SovereignOf(world, to)?.Id));
            return ("war", null, war?.DeclaredEventId);
        }
        foreach (var id in route.Regions)
        {
            var region = world.Regions[id];
            if (region.OccupierRealmId != 0)
                return ("occupation", region.Ref, ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type is "RegionOccupied" or "SiegeEnded")?.Id);
            if (region.Danger >= DangerLimit)
                return ("danger", region.Ref, region.Conditions.TryGetValue(Conditions.Dangerous, out var danger) ? danger : null);
        }
        return null;
    }

    private static bool AtWar(World world, Settlement a, Settlement b)
    {
        var (x, y) = (Realms.SovereignOf(world, a)?.Id, Realms.SovereignOf(world, b)?.Id);
        return x is not null && y is not null && x != y && world.Wars.Values.Any(w => !w.Ended && Opposed(w, x, y));
    }

    private static bool Opposed(War w, long? a, long? b) =>
        a is { } x && b is { } y && (w.Attackers.Contains(x) && w.Defenders.Contains(y) || w.Attackers.Contains(y) && w.Defenders.Contains(x));
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~TradeSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 4`. Then `dotnet test sim` → `Passed: 516` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (69)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): link markets with trade routes that wars and danger sever

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Trade roads on the map

Spec §8–§9: the map's *Trade routes* layer, disabled in sub-project 4, now shows the network. `MapScreen` lists the roads open at the date (their state then replayed from severing and restoring), each as a path from market through the regions between to market; the client lays them on the surface as smoothed lines, and the diorama draws them as brown dotted roads, reddened where severed.

**Files:**
- Create: `sim/FantasyWorld.Client/RouteLayout.cs`
- Modify: `game/scripts/Diorama.cs`, `game/scripts/screens/MapHudView.cs`, `game/tests/HeadlessChecks.cs`, `sim/FantasyWorld.Client/MapLabels.cs`, `sim/FantasyWorld.Sim/Views/MapScreen.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Systems/TradeSystemTests.cs`, `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`

**Interfaces:**
- Consumes: Task 1; `MapScreen` (sub-project 2), `Markers`, `Diorama.DashedRibbons` (sub-project 4).
- Produces: `record MapRoute(Ref, Name, From, To, State, Volume, Path)`; `MapScreen.Routes`; `record Road(Ref, Name, Open, Line)`; `static class RouteLayout { Step; Lift; Roads(map, geography) }`; the trade routes layer enabled and on by default.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Systems/TradeSystemTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class TradeSystemTests
{
    /// <summary>A tiny world with exactly two markets in neighbouring regions of different realms.</summary>
    private static (Simulator Sim, Settlement A, Settlement B) TwoMarkets()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var link = world.Geography!.Graph.Links.First(l => Realms.SovereignOf(world, world.Regions[l.A]) is { } x && Realms.SovereignOf(world, world.Regions[l.B]) is { } y
                                                          && x.Id != y.Id
                                                          && world.LivingSettlements(world.Regions[l.A]).Any() && world.LivingSettlements(world.Regions[l.B]).Any());
        foreach (var s in world.Settlements.Values) s.Tier = SettlementTier.Village;
        var a = world.LivingSettlements(world.Regions[link.A]).First();
        var b = world.LivingSettlements(world.Regions[link.B]).First();
        foreach (var (s, wealth) in new[] { (a, 4000.0), (b, 1000.0) }) (s.Tier, s.Wealth) = (SettlementTier.Town, wealth);
        foreach (var region in world.Regions.Values) region.Danger = 0;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new TradeSystem() }, checkInvariants: true);
        return (sim, a, b);
    }

    [Fact]
    public void MarketsAtPeace_OpenARoad_ThatEnrichesBoth()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = Assert.Single(sim.World.Routes.Values);
        Assert.Equal((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), (route.FromId, route.ToId));
        Assert.Equal(sim.World.Settlements[route.FromId].RegionId, route.Regions[0]);
        Assert.Equal(sim.World.Settlements[route.ToId].RegionId, route.Regions[^1]);
        Assert.Equal(RouteState.Open, route.State);
        Assert.True(route.Volume > 0);
        Assert.True(b.Wealth > 1000);
        var opened = sim.Log.Get(route.OpenedEventId);
        Assert.Equal("RouteOpened", opened.Type);
        Assert.Equal(route.Ref, opened.Actors["route"]);
    }

    [Fact]
    public void War_SeversTheRoad_AndPeaceReopensIt()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = sim.World.Routes.Values.Single();
        var world = sim.World;
        var (x, y) = (Realms.SovereignOf(world, a)!.Id, Realms.SovereignOf(world, b)!.Id);
        var cb = sim.Log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = sim.Log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Toll War", AttackerId = x, DefenderId = y, Attackers = { x }, Defenders = { y },
            CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id, DeclaredEventId = declared.Id, StartDay = world.Date.Day,
        };
        world.Wars[war.Id] = war;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Severed, route.State);
        var severed = sim.Log.Get(route.SeveredEventId);
        Assert.Equal("war", severed.Payload["reason"]);
        Assert.Equal(declared.Id, severed.Causes.Single().EventId);
        Assert.Equal(0, route.Volume);

        war.Ended = true;
        war.EndedEventId = sim.Log.Append(world.Date, new EventDraft("PeaceSigned")).Id;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Open, route.State);
        Assert.Equal(route.SeveredEventId, sim.Log.All.Last(e => e.Type == "RouteRestored").Causes.Single().EventId);
    }

    [Fact]
    public void AnAbandonedMarket_ClosesItsRoads()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        foreach (var c in sim.World.Characters.Values.Where(c => c.SettlementId == a.Id)) c.SettlementId = b.Id;
        if (a.LordId != 0) a.LordId = 0;
        a.Abandoned = true;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Contains(sim.Log.All, e => e.Type == "RouteClosed");
    }

    [Fact]
    public void AMarketThatShrinksBelowATown_ClosesItsRoads()
    {
        var (sim, a, _) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        a.Tier = SettlementTier.Village;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Equal(0, sim.World.Routes.Values.Single().Volume);
    }

    [Fact]
    public void TheMap_ShowsRoadsAsTheyWere()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = sim.World.Routes.Values.Single();
        var opened = sim.Log.Get(route.OpenedEventId).Date;
        var map = FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log);
        var road = Assert.Single(map.Routes!);
        Assert.Equal(route.Ref, road.Ref);
        Assert.Equal("open", road.State);
        Assert.Equal(FantasyWorld.Sim.Views.MapPoint.Of(sim.World, sim.World.Settlements[route.FromId].Ref), road.Path[0]);
        Assert.Equal(FantasyWorld.Sim.Views.MapPoint.Of(sim.World, sim.World.Settlements[route.ToId].Ref), road.Path[^1]);
        Assert.Empty(FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log, opened.AddDays(-1)).Routes!);
    }

    [Fact]
    public void Roads_FollowTheGround_FromMarketToMarket()
    {
        var (sim, _, _) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var map = FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log);
        var road = Assert.Single(FantasyWorld.Client.RouteLayout.Roads(map, sim.World.Geography!));
        Assert.True(road.Open);
        Assert.True(road.Line.Count > 4);
        var grid = sim.World.Geography!.Terrain;
        Assert.All(road.Line, p => Assert.Equal(Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(grid, (p.X + 1) / 2, (p.Z + 1) / 2)) + FantasyWorld.Client.RouteLayout.Lift, p.Y, 3));
        var start = FantasyWorld.Client.Projection.ToBlock(grid, map.Routes![0].Path[0], 0);
        Assert.True(Math.Abs(road.Line[0].X - start.X) < 1e-4 && Math.Abs(road.Line[0].Z - start.Z) < 1e-4);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, one open labyrinth.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      }
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      }
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      }
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      }
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      }
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      }
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      }
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      }
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      }
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      }
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      }
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      }
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      }
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      }
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      }
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      }
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      }
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      }
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      }
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      }
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      }
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      }
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      }
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      }
    }
  ],
  "structures": [
    {
      "ref": "structure#1",
      "name": "The Deeps of Taramdale",
      "kind": "Labyrinth",
      "state": "Standing",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      }
    }
  ],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ],
  "armies": [],
  "battles": [],
  "routes": [
    {
      "ref": "route#1",
      "name": "The Greindrondcross–Niar Road",
      "from": "settlement#16",
      "to": "settlement#24",
      "state": "open",
      "volume": 76,
      "path": [
        {
          "x": 0.8828125,
          "y": 0.4296875
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    },
    {
      "ref": "route#2",
      "name": "The Drerithby–Greindrondcross Road",
      "from": "settlement#3",
      "to": "settlement#16",
      "state": "open",
      "volume": 106,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.4921875,
          "y": 0.4296875
        },
        {
          "x": 0.8828125,
          "y": 0.4296875
        }
      ]
    },
    {
      "ref": "route#3",
      "name": "The Drerithby–Niar Road",
      "from": "settlement#3",
      "to": "settlement#24",
      "state": "open",
      "volume": 78,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    }
  ]
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'MapScreen' does not contain a definition for 'Routes' and no accessible extension method 'Routes' accepting a first argument of type 'MapScreen' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `game/scripts/Diorama.cs` entirely with:
```csharp
using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The floating world block (sub-project 4 §3): terrain, sea, strata walls, markers, realm borders and digest pins,
/// seen through the orbit camera. Geography is built once; markers and borders are rebuilt when the map changes.
/// Drag to orbit, scroll to descend; clicking near a marker raises <see cref="Picked"/>.
/// </summary>
public partial class Diorama : Node3D
{
    public const float PickRadius = 16f;
    public const float FrameOffset = 0.08f;

    private Geography? _geography;
    private bool _dragging;
    private Vector2 _pressedAt;
    private readonly List<Marker> _markers = new();
    private readonly List<Pin> _pins = new();

    public OrbitRig Rig { get; } = new();
    public Camera3D Camera { get; private set; } = null!;
    public MeshInstance3D Block { get; private set; } = null!;
    public Node3D MarkerLayer { get; private set; } = null!;
    public Node3D SettlementLayer { get; private set; } = null!;
    public Node3D StructureLayer { get; private set; } = null!;
    public MapLayers Layers { get; private set; } = MapLayers.Default;
    public Node3D PinLayer { get; private set; } = null!;
    public WarLayer War { get; private set; } = null!;
    public MeshInstance3D BorderLines { get; private set; } = null!;
    public MeshInstance3D RoadLines { get; private set; } = null!;
    public IReadOnlyList<Road> Roads { get; private set; } = Array.Empty<Road>();
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary>A marker or pin was clicked.</summary>
    public event Action<EntityRef>? Picked;

    public override void _Ready()
    {
        Name = "Diorama";
        Camera = new Camera3D { Name = "Camera", Fov = 32, Near = 0.01f, Far = 50 };
        AddChild(Camera);
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 0.55f, ShadowEnabled = true });
        // A soft fill from the viewer's side keeps the strata walls readable.
        AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-15, 35, 0), LightEnergy = 0.25f });
        AddChild(new DirectionalLight3D { Name = "Back", RotationDegrees = new Vector3(-20, 215, 0), LightEnergy = 0.2f });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.C(Palette.Ground),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f,
            },
        });
        Block = new MeshInstance3D { Name = "Block" };
        AddChild(Block);
        BorderLines = new MeshInstance3D { Name = "Borders" };
        AddChild(BorderLines);
        RoadLines = new MeshInstance3D { Name = "Roads" };
        AddChild(RoadLines);
        MarkerLayer = new Node3D { Name = "Markers" };
        AddChild(MarkerLayer);
        SettlementLayer = new Node3D { Name = "Settlements" };
        StructureLayer = new Node3D { Name = "Structures" };
        MarkerLayer.AddChild(SettlementLayer);
        MarkerLayer.AddChild(StructureLayer);
        PinLayer = new Node3D { Name = "Pins" };
        AddChild(PinLayer);
        War = new WarLayer();
        AddChild(War);
        UpdateCamera();
    }

    /// <summary>
    /// Builds the terrain block, its walls, the sea, the woods and the block's shadow (once per world). Woods leave
    /// clearings around the given points (today's settlements).
    /// </summary>
    public void Build(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null) =>
        Build(geography, Prepare(geography, clearings, resolution));

    /// <summary>The block's geometry and woods, computed without Godot so it can run on a worker thread.</summary>
    public sealed record Parts(MeshData Top, MeshData Sides, MeshData Water, IReadOnlyList<Client.Tree> Trees);

    public static Parts Prepare(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null)
    {
        var grid = geography.Terrain;
        // About one and a half vertices per tile: smooth on small worlds, detailed on large ones.
        var detail = resolution ?? Math.Clamp(grid.Width * 3 / 2, 192, 384);
        return new Parts(BlockMesh.Top(grid, detail), BlockMesh.Sides(grid, detail), BlockMesh.Water(grid), Scatter.Trees(grid, clearings));
    }

    public void Build(Geography geography, Parts parts)
    {
        _geography = geography;
        var mesh = new ArrayMesh();
        AddSurface(mesh, parts.Top, VertexColours());
        AddSurface(mesh, parts.Sides, VertexColours());
        Block.Mesh = mesh;

        var water = new ArrayMesh();
        AddSurface(water, parts.Water, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.18f,
            MetallicSpecular = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        Child<MeshInstance3D>("Water").Mesh = water;

        var trees = parts.Trees;
        Child<MultiMeshInstance3D>("Broadleaves").Multimesh = Instances(
            new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 },
            trees.Where(t => t.Kind == TreeKind.Broadleaf).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.55f, 0), new Vector3(t.Height * 0.95f, t.Height, t.Height * 0.95f), 0), t.Colour)));
        Child<MultiMeshInstance3D>("Conifers").Multimesh = Instances(
            new CylinderMesh { TopRadius = 0, BottomRadius = 0.36f, Height = 1f, RadialSegments = 7, Rings = 1 },
            trees.Where(t => t.Kind == TreeKind.Conifer).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.5f, 0), new Vector3(t.Height, t.Height, t.Height), 0), t.Colour)));
        foreach (var name in new[] { "Broadleaves", "Conifers" }) Child<MultiMeshInstance3D>(name).MaterialOverride = Foliage();

        // A soft shadow on the ground beneath the floating block.
        Child<MeshInstance3D>("Shadow").Mesh = new PlaneMesh { Size = new Vector2(3.1f, 3.1f) };
        var shadow = Child<MeshInstance3D>("Shadow");
        shadow.Position = new Vector3(0.06f, -BlockMesh.BaseDepth - 0.12f, 0.06f);
        shadow.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = SoftShadow(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 1),
        };
    }

    public int TreeCount => Child<MultiMeshInstance3D>("Broadleaves").Multimesh?.InstanceCount + Child<MultiMeshInstance3D>("Conifers").Multimesh?.InstanceCount ?? 0;

    /// <summary>Places settlements, structures and borders for a map: today's, or the given past year's.</summary>
    public void Show(MapScreen map, int? pastYear = null)
    {
        if (_geography is null) return;
        Ui.Clear(SettlementLayer);
        Ui.Clear(StructureLayer);
        _markers.Clear();
        _markers.AddRange(FantasyWorld.Client.Markers.For(map, _geography));
        var houses = _markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, _geography.Terrain)).ToList();
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Walls", MaterialOverride = Painted(),
            Multimesh = Instances(new BoxMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Size * (h.Tower ? 0.75f : 0.35f), 0), h.Tower ? new Vector3(h.Size, h.Size * 1.5f, h.Size) : new Vector3(h.Size, h.Size * 0.7f, h.Size * 0.8f), h.Yaw),
                h.Tower ? Stone : Plaster))),
        });
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Roofs", MaterialOverride = Painted(),
            Multimesh = Instances(new PrismMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Tower ? h.Size * 1.85f : h.Size * 0.95f, 0), h.Tower ? new Vector3(h.Size * 1.15f, h.Size * 0.75f, h.Size * 1.15f) : new Vector3(h.Size * 1.1f, h.Size * 0.5f, h.Size * 0.9f), h.Yaw),
                Roof(h.Roof)))),
        });
        foreach (var marker in _markers.Where(m => m.Shape != MarkerShape.Settlement)) StructureLayer.AddChild(MarkerNode(marker));
        BorderLines.Mesh = DashedRibbons(FantasyWorld.Client.Markers.BorderLines(map, _geography), Palette.Ink);
        War.Show(map, _geography);
        Roads = RouteLayout.Roads(map, _geography);
        // Open roads are close-dotted in road brown; severed ones broken and reddened.
        var roads = new ArrayMesh();
        foreach (var (open, dash, gap, colour) in new[] { (true, 0.006f, 0.005f, BlockMesh.Hex(0x7a5a35)), (false, 0.004f, 0.012f, BlockMesh.Hex(0xa8483a)) })
        {
            var lines = Roads.Where(r => r.Open == open).Select(r => r.Line).ToList();
            if (lines.Count == 0) continue;
            var part = DashedRibbons(lines, colour, dash, gap, width: 0.0019f, halo: 0.0036f);
            for (var s = 0; s < part.GetSurfaceCount(); s++)
            {
                roads.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, part.SurfaceGetArrays(s));
                roads.SurfaceSetMaterial(roads.GetSurfaceCount() - 1, part.SurfaceGetMaterial(s));
            }
        }
        RoadLines.Mesh = roads;
        ShownYear = pastYear;
    }

    /// <summary>The past year the markers show, or null for today.</summary>
    public int? ShownYear { get; private set; }

    /// <summary>Shows or hides the borders, settlements and structures (news tags are the screen's).</summary>
    public void SetLayers(MapLayers layers)
    {
        Layers = layers;
        BorderLines.Visible = layers.HasFlag(MapLayers.Borders);
        SettlementLayer.Visible = layers.HasFlag(MapLayers.Settlements);
        StructureLayer.Visible = layers.HasFlag(MapLayers.Structures);
        War.Visible = layers.HasFlag(MapLayers.Armies);
        RoadLines.Visible = layers.HasFlag(MapLayers.TradeRoutes);
    }

    /// <summary>The digest's pins as stems over their places (the screen draws their numbered labels).</summary>
    public void ShowPins(IEnumerable<Pin> pins)
    {
        Ui.Clear(PinLayer);
        _pins.Clear();
        _pins.AddRange(pins);
        foreach (var pin in _pins)
        {
            var node = new Node3D { Name = $"Pin{pin.Number}", Position = ToGodot(pin.Position) };
            node.AddChild(new MeshInstance3D
            {
                Name = "Stem", Position = new Vector3(0, -Client.Markers.PinLift / 2, 0),
                Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = Client.Markers.PinLift },
                MaterialOverride = Flat(Palette.Ink),
            });
            node.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f }, MaterialOverride = Flat(Palette.Ink) });
            PinLayer.AddChild(node);
        }
    }

    /// <summary>Eases the camera to a place on the map.</summary>
    public void Focus(MapPoint point, Zoom? zoom = null)
    {
        if (_geography is null) return;
        var at = Client.Projection.ToBlock(_geography.Terrain, point, 0);
        Rig.Focus(at, zoom);
    }

    /// <summary>The marker or pin drawn nearest a screen point, within <see cref="PickRadius"/> pixels.</summary>
    public EntityRef? Pick(Vector2 screen)
    {
        // Banners pick as their host, battle marks as the battle (an "event" ref); both above the settlements they stand on.
        var war = !War.Visible ? Enumerable.Empty<(EntityRef, Rgb)>()
            : War.Armies.Select(a => (a.Ref, War.BannerAt(a.Ref) is { } b ? new Rgb(b.X, b.Y + WarLayer.BannerHeight * 0.7f, b.Z) : Rgb.Zero))
                .Concat(War.RecentBattles.Select(b => (new EntityRef("event", b.EventId), Client.Projection.ToBlock(_geography!.Terrain, b.Position, 0.018f))));
        var candidates = war.Concat(_pins.Select(p => (p.Place, p.Position))).Concat(_markers
            .Where(m => m.Shape == MarkerShape.Settlement ? SettlementLayer.Visible : StructureLayer.Visible).Select(m => (m.Ref, m.Position)));
        EntityRef? best = null;
        var bestDistance = PickRadius;
        foreach (var (entity, position) in candidates)
        {
            var world = ToGodot(position);
            if (Camera.IsPositionBehind(world)) continue;
            var distance = Camera.UnprojectPosition(world).DistanceTo(screen);
            if (distance < bestDistance) (best, bestDistance) = (entity, distance);
        }
        return best;
    }

    /// <summary>Where a block-space point appears on screen.</summary>
    public Vector2 ScreenOf(Rgb position) => Camera.UnprojectPosition(ToGodot(position));

    public override void _Process(double delta)
    {
        Rig.Update((float)delta);
        UpdateCamera();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) (_dragging, _pressedAt) = (true, button.Position);
                else
                {
                    _dragging = false;
                    if (button.Position.DistanceTo(_pressedAt) < 4 && Pick(button.Position) is { } picked) Picked?.Invoke(picked);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Rig.Scroll(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Rig.Scroll(-1);
                break;
            case InputEventMouseMotion motion when _dragging:
                Rig.Drag(motion.Relative.X, motion.Relative.Y);
                break;
        }
    }

    private void UpdateCamera()
    {
        if (Camera is null) return;
        var eye = Rig.Eye;
        // Never inside a mountain: keep the eye a little above the ground beneath it.
        if (_geography is not null && Math.Abs(eye.X) <= 1 && Math.Abs(eye.Z) <= 1)
            eye.Y = Math.Max(eye.Y, Math.Max(0, BlockMesh.HeightAt(_geography.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)) + 0.04f);
        Camera.Position = ToGodot(eye);
        Camera.LookAt(ToGodot(Rig.Target), Vector3.Up);
        // The screens' cards sit on the right, so the world is framed slightly left of centre.
        Camera.HOffset = Rig.Distance * FrameOffset;
    }

    private static readonly Rgb Plaster = BlockMesh.Hex(0xf1ebde);
    private static readonly Rgb Stone = BlockMesh.Hex(0xb9b1a2);

    /// <summary>Roofs take the holder's colour, softened toward terracotta so heraldry reads without shouting.</summary>
    private static Rgb Roof(Rgb heraldic)
    {
        var roof = Rgb.Lerp(heraldic, BlockMesh.Hex(0xb5654a), 0.3f);
        // Keep roofs mid-toned: sable (black) and argent (white) houses would read as holes and glare.
        var luma = 0.3f * roof.X + 0.59f * roof.Y + 0.11f * roof.Z;
        return Rgb.Clamp(roof * (Math.Clamp(luma, 0.32f, 0.62f) / Math.Max(0.01f, luma)), Rgb.Zero, Rgb.One);
    }

    /// <summary>Landmarks are pale obelisks, labyrinths a ring of standing walls, ruins a few broken stones.</summary>
    private static Node3D MarkerNode(Marker marker)
    {
        var node = new Node3D { Name = $"{marker.Ref.Kind}{marker.Ref.Id}", Position = ToGodot(marker.Position) };
        var s = marker.Size;
        switch (marker.Shape)
        {
            case MarkerShape.Landmark:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.35f, s * 1.6f, s * 0.35f) }, Stone, s * 0.8f));
                node.AddChild(Solid(new PrismMesh { Size = new Vector3(s * 0.4f, s * 0.35f, s * 0.4f) }, marker.Colour, s * 1.78f));
                break;
            case MarkerShape.Labyrinth:
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * Mathf.Tau / 8;
                    var wall = Solid(new BoxMesh { Size = new Vector3(s * 0.38f, s * 0.35f, s * 0.08f) }, Stone, s * 0.17f);
                    wall.Position += new Vector3(Mathf.Cos(angle) * s * 0.5f, 0, Mathf.Sin(angle) * s * 0.5f);
                    wall.Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0);
                    node.AddChild(wall);
                }
                node.AddChild(Solid(new CylinderMesh { TopRadius = s * 0.16f, BottomRadius = s * 0.16f, Height = s * 0.06f }, Rgb.Lerp(marker.Colour, Stone, 0.45f), s * 0.03f));
                break;
            default:
                for (var k = 0; k < 3; k++)
                {
                    var stone = Solid(new BoxMesh { Size = new Vector3(s * 0.22f, s * (0.25f + 0.2f * k), s * 0.22f) }, marker.Colour, s * 0.15f);
                    stone.Position += new Vector3((k - 1) * s * 0.3f, 0, (k % 2) * s * 0.2f);
                    stone.Rotation = new Vector3(0.15f * k, 0.6f * k, 0);
                    node.AddChild(stone);
                }
                break;
        }
        return node;
    }

    private static MeshInstance3D Solid(Mesh mesh, Rgb colour, float lift) =>
        new() { Mesh = mesh, Position = new Vector3(0, lift, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(colour), Roughness = 0.8f } };

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    private static StandardMaterial3D VertexColours() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };

    /// <summary>
    /// Lines drawn at a constant thickness on screen whatever the zoom: each vertex carries the direction across its
    /// line (NORMAL) and which side it is on (UV.x), and the shader pushes it out in proportion to its distance from
    /// the camera. Width is in thousandths of the distance (about 1.6 px at 1440×900 per 0.001).
    /// </summary>
    internal const string LineShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform vec4 colour : source_color = vec4(0.1, 0.1, 0.09, 0.9);
        uniform float width = 0.0016;
        void vertex() {
            vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            float distance_to_eye = length(CAMERA_POSITION_WORLD - world);
            VERTEX += NORMAL * UV.x * width * distance_to_eye;
            VERTEX.y += 0.0008 * distance_to_eye;
        }
        void fragment() {
            ALBEDO = colour.rgb;
            ALPHA = colour.a;
        }
        """;

    /// <summary>Realm borders as dashed lines of constant screen width, ink over a pale halo so they read on any ground.</summary>
    internal static ArrayMesh DashedRibbons(IReadOnlyList<IReadOnlyList<Rgb>> lines, Rgb colour, float dash = 0.014f, float gap = 0.008f, float width = 0.0016f, float halo = 0.0034f)
    {
        var mesh = new ArrayMesh();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var line in lines)
        {
            var travelled = 0f;
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (ToGodot(line[i]), ToGodot(line[i + 1]));
                var length = a.DistanceTo(b);
                if (length < 1e-6f) continue;
                var across = new Vector3(-(b.Z - a.Z), 0, b.X - a.X).Normalized();
                for (var t = 0f; t < length;)
                {
                    var phase = (travelled + t) % (dash + gap);
                    var span = phase < dash ? Math.Min(dash - phase, length - t) : Math.Min(dash + gap - phase, length - t);
                    if (phase < dash)
                    {
                        var (p, q) = (a.Lerp(b, t / length), a.Lerp(b, (t + span) / length));
                        vertices.AddRange(new[] { p, q, p, q, q, p });
                        normals.AddRange(Enumerable.Repeat(across, 6));
                        uvs.AddRange(new[] { new Vector2(-1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 0) });
                    }
                    t += Math.Max(span, 1e-5f);
                }
                travelled += length;
            }
        }
        if (vertices.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        // The same dashes twice: a wide pale halo, then the ink line on top.
        foreach (var (tint, size, priority) in new[] { (Palette.Card, halo, 0), (colour, width, 1) })
        {
            if (size <= 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = new Shader { Code = LineShader }, RenderPriority = priority };
            material.SetShaderParameter("colour", Ui.C(tint, priority == 0 ? 0.75f : 0.95f));
            material.SetShaderParameter("width", size);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        return mesh;
    }

    /// <summary>Instances of one mesh with per-instance transforms and colours (colours given in sRGB).</summary>
    private static MultiMesh Instances(Mesh mesh, IEnumerable<(Transform3D Transform, Rgb Colour)> items)
    {
        var list = items.ToList();
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
        for (var i = 0; i < list.Count; i++)
        {
            multi.SetInstanceTransform(i, list[i].Transform);
            multi.SetInstanceColor(i, Ui.C(list[i].Colour).SrgbToLinear());
        }
        return multi;
    }

    private static Transform3D Place(Rgb at, Vector3 scale, float yaw) =>
        new(new Basis(Vector3.Up, yaw).Scaled(scale), ToGodot(at));

    private static StandardMaterial3D Foliage() => new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    private static StandardMaterial3D Painted() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.9f };

    /// <summary>A radial falloff from soft grey to clear, for the shadow under the block.</summary>
    private static ImageTexture SoftShadow(int size = 128)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // A rounded square: the block is square, its shadow a soft square.
                var dx = Math.Max(0, Math.Abs((x + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var dy = Math.Max(0, Math.Abs((y + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp(1 - d, 0, 1);
                image.SetPixel(x, y, new Color(0.1f, 0.1f, 0.09f, 0.22f * a * a));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private T Child<T>(string name) where T : Node, new()
    {
        if (GetNodeOrNull<T>(name) is { } existing) return existing;
        var child = new T { Name = name };
        AddChild(child);
        return child;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select((c, i) => Ui.C(c, data.Alphas?[i] ?? 1).SrgbToLinear()).ToArray();
        // Godot treats clockwise triangles as front faces; the client's indices are counter-clockwise.
        var indices = new int[data.Indices.Length];
        for (var i = 0; i < indices.Length; i += 3) (indices[i], indices[i + 1], indices[i + 2]) = (data.Indices[i], data.Indices[i + 2], data.Indices[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    public static Vector3 ToGodot(Rgb v) => new(v.X, v.Y, v.Z);
}
```

Replace `game/scripts/screens/MapHudView.cs` entirely with:
```csharp
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Diorama map HUD* (sub-project 4 §4): the world's name and summary, name tags over settlements (more as you
/// descend), a dark hover card per settlement, and the control card: zoom presets, layer toggles, the history slider
/// that rebuilds the map at any past year, reset and snapshot. A legend of the realms sits bottom left.
/// </summary>
public partial class MapHudView : Control
{
    public Main Host { get; init; } = null!;

    /// <summary>A place to fly to when the map opens (a linked settlement, region or structure).</summary>
    public EntityRef? Focus { get; init; }

    public MapScreen Map { get; private set; } = null!;
    public int Year { get; private set; }
    public int Now { get; private set; }
    public Control TagLayer { get; private set; } = null!;
    public PanelContainer HoverCard { get; private set; } = null!;
    public HSlider History { get; private set; } = null!;

    private Label _kicker = null!;
    private Label _summary = null!;
    private Label _yearLabel = null!;
    private VBoxContainer _legend = null!;
    private Zoom _zoom;
    private int? _pendingYear;

    public override void _Ready()
    {
        Name = "MapScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Map = Host.Session!.Map();
        Now = Map.Year;
        Year = Host.Diorama.ShownYear ?? Now;
        if (Year != Now) Map = Host.Session.Map(Year);
        _zoom = Host.Diorama.Rig.NearestZoom;

        TagLayer = new Control { Name = "Tags", MouseFilter = MouseFilterEnum.Ignore };
        TagLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(TagLayer);

        var intro = Ui.Place(Ui.Column("Intro", 10), 0, 0, 56, 48);
        intro.CustomMinimumSize = new Vector2(300, 0);
        _kicker = Ui.Kicker("", "Kicker");
        intro.AddChild(_kicker);
        intro.AddChild(Ui.FittedTitle($"{Map.WorldName}.", 64, 440));
        _summary = Ui.Body("", "Summary", Palette.Muted);
        intro.AddChild(_summary);
        AddChild(intro);

        AddChild(ControlCard());

        _legend = Ui.Place(Ui.Column("Legend", 8), 0, 1, 56, -44);
        AddChild(_legend);

        HoverCard = Ui.Card("HoverCard", 250);
        var dark = (StyleBoxFlat)HoverCard.GetThemeStylebox("panel").Duplicate();
        (dark.BgColor, dark.BorderColor, dark.CornerRadiusBottomLeft, dark.CornerRadiusBottomRight, dark.CornerRadiusTopLeft, dark.CornerRadiusTopRight) =
            (Ui.C(Palette.Ink), Ui.C(Palette.Ink), 12, 12, 12, 12);
        (dark.ContentMarginLeft, dark.ContentMarginRight, dark.ContentMarginTop, dark.ContentMarginBottom) = (16, 16, 14, 14);
        HoverCard.AddThemeStyleboxOverride("panel", dark);
        HoverCard.MouseFilter = MouseFilterEnum.Ignore;
        HoverCard.Visible = false;
        AddChild(HoverCard);

        Refresh();
        if (Focus is { } focus && MapLabels.Find(Map, focus) is { } point) Host.Diorama.Focus(point, MapLabels.ZoomFor(focus));
    }

    /// <summary>Shows the map as it was at the end of a year (today's map for the current year).</summary>
    public void ShowYear(int year)
    {
        Year = Math.Clamp(year, 1, Now);
        Map = Year == Now ? Host.Session!.Map() : Host.Session!.Map(Year);
        Host.Diorama.Show(Map, Year == Now ? null : Year);
        if (Math.Abs(History.Value - Year) > 0.5) History.SetValueNoSignal(Year);
        Refresh();
    }

    public void SetLayer(MapLayers layer, bool on)
    {
        Host.Diorama.SetLayers(on ? Host.Diorama.Layers | layer : Host.Diorama.Layers & ~layer);
        RebuildTags();
    }

    private void Refresh()
    {
        _kicker.Text = $"{Map.WorldName} · {_zoom} view".ToUpperInvariant();
        _summary.Text = Year != Now ? $"Year {Map.Year}. {Map.Summary}" : Map.Summary;
        _yearLabel.Text = Year == Now ? $"Year {Now}" : $"Year {Year} of {Now}";
        Ui.Clear(_legend);
        var legend = Map.Legend.ToList();
        if (Map.Armies is { Count: > 0 } && Host.Diorama.Layers.HasFlag(MapLayers.Armies))
            legend.InsertRange(legend.Count - 2, new[] { new LegendEntry("Attacking host", "attacker"), new LegendEntry("Defending host", "defender") });
        foreach (var entry in legend)
        {
            var row = Ui.Row("Entry", 8);
            row.AddChild(Ui.Dot(entry.Colour switch
            {
                "conflict" => Palette.Conflict, "event" => Palette.Ink, "attacker" => WarLayout.Attacker, "defender" => WarLayout.Defender,
                var name => Palette.Heraldic(name),
            }, 10));
            var label = Ui.Body(entry.Label, "Label", Palette.Body, 12);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            row.AddChild(label);
            _legend.AddChild(row);
        }
        RebuildTags();
    }

    /// <summary>The name tags for the settlements this zoom and these layers show.</summary>
    public void RebuildTags()
    {
        Ui.Clear(TagLayer);
        HoverCard.Visible = false;
        foreach (var settlement in Map.Settlements.Where(s => MapLabels.Shows(s, _zoom, Host.Diorama.Layers)))
            TagLayer.AddChild(Tag(settlement));
        if (Host.Diorama.Layers.HasFlag(MapLayers.Armies))
            foreach (var army in Map.Armies ?? Array.Empty<MapArmy>()) TagLayer.AddChild(ArmyTag(army));
    }

    /// <summary>A host's tag above its banner: its name, strength and who leads it; clicking opens the commander.</summary>
    private ArmyTag ArmyTag(MapArmy army)
    {
        var tag = new ArmyTag { Name = $"Army{army.Ref.Id}", Army = army, MouseDefaultCursorShape = CursorShape.PointingHand };
        var pill = Ui.Capsule("Pill", Ui.C(Palette.Ink), null, 10, 4);
        var column = Ui.Column("Column", 0);
        column.AddChild(Ui.Line(army.Name, "Name", Palette.Card, 12));
        var led = army.Commander is { } c ? $" · led by {c.Name}" : "";
        var knights = army.Knights.Count > 0 ? $" · {army.Knights.Count} knight{(army.Knights.Count == 1 ? "" : "s")}" : "";
        column.AddChild(Ui.Line($"{army.Soldiers:N0} strong{led}{knights}", "Detail", new System.Numerics.Vector3(0.78f, 0.77f, 0.74f), 11));
        pill.AddChild(column);
        pill.MouseFilter = MouseFilterEnum.Ignore;
        tag.AddChild(pill);
        tag.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && army.Commander is { } commander)
                Host.Router.Open(commander.Ref);
        };
        return tag;
    }

    /// <summary>A live day passed: if the map shows today, it shows the new today.</summary>
    public void Today(MapScreen today)
    {
        var wasNow = Year == Now;
        Now = today.Year;
        History.MaxValue = Math.Max(2, Now);
        if (!wasNow) return;
        (Year, Map) = (Now, today);
        History.SetValueNoSignal(Now);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_pendingYear is { } pending)
        {
            _pendingYear = null;
            if (pending != Year) ShowYear(pending);
        }
        if (Host.Diorama.Rig.NearestZoom != _zoom)
        {
            _zoom = Host.Diorama.Rig.NearestZoom;
            Refresh();
        }
        var grid = Host.Geography?.Terrain;
        if (grid is null) return;
        var tags = TagLayer.GetChildren().OfType<Control>().ToList();
        var boxes = new List<LabelBox>(tags.Count);
        foreach (var tag in tags)
        {
            Godot.Vector3 at;
            int priority;
            if (tag is SettlementTag s)
            {
                at = Diorama.ToGodot(Client.Projection.ToBlock(grid, s.Settlement.Position!, 0.02f));
                tag.Position = Host.Diorama.Camera.UnprojectPosition(at) - new Vector2(11, 24);
                priority = Priority(s.Settlement);
            }
            else if (tag is ArmyTag a && Host.Diorama.War.BannerTop(a.Army.Ref) is { } top)
            {
                // Above the banner, which may be marching.
                at = top;
                tag.Position = Host.Diorama.Camera.UnprojectPosition(at) - new Vector2(tag.Size.X / 2, tag.Size.Y + 4);
                priority = 11;
            }
            else
            {
                boxes.Add(new LabelBox(-1e5f, -1e5f, 0, 0, 0));
                continue;
            }
            var behind = Host.Diorama.Camera.IsPositionBehind(at);
            boxes.Add(behind ? new LabelBox(-1e5f, -1e5f, 0, 0, 0) : new LabelBox(tag.Position.X, tag.Position.Y, tag.Size.X, tag.Size.Y, priority));
        }
        var visible = Declutter.Visible(boxes, GetViewportRect().Size.X, GetViewportRect().Size.Y, Reserved(this, "Card", "Intro", "Legend"));
        for (var i = 0; i < tags.Count; i++) tags[i].Visible = visible[i];
    }

    /// <summary>News first, then cities, towns, villages: the order in which tags claim screen space.</summary>
    public static int Priority(MapSettlement settlement) => settlement.Marker switch
    {
        PinMarker.Conflict => 10,
        PinMarker.RecentEvent => 9,
        _ => Enum.TryParse<FantasyWorld.Sim.Model.SettlementTier>(settlement.Tier, out var tier) ? (int)tier : 0,
    };

    /// <summary>Screen boxes of a screen's own panels, which labels must not cover.</summary>
    public static IEnumerable<LabelBox> Reserved(Control screen, params string[] panels) =>
        panels.Select(p => screen.GetNodeOrNull<Control>(p)).Where(c => c is { Visible: true })
            .Select(c => c!.GetGlobalRect()).Select(r => new LabelBox(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, int.MaxValue));

    private SettlementTag Tag(MapSettlement settlement)
    {
        var tag = new SettlementTag { Name = $"Tag{settlement.Ref.Id}", Settlement = settlement, MouseDefaultCursorShape = CursorShape.PointingHand };
        tag.AddThemeConstantOverride("separation", 6);
        var marker = Host.Diorama.Markers.FirstOrDefault(m => m.Ref == settlement.Ref);
        var colour = settlement.Marker switch
        {
            PinMarker.Conflict => Palette.Conflict,
            PinMarker.RecentEvent => Palette.Ink,
            _ => marker?.Colour ?? Palette.Ink,
        };
        var dot = Ui.Capsule("Dot", Ui.C(colour), Ui.C(Palette.Card), 0, 0);
        dot.CustomMinimumSize = new Vector2(18, 18);
        dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var pill = Ui.Capsule("Name", Ui.C(Palette.Card), Ui.C(settlement.Marker == PinMarker.Conflict ? Palette.Ink : Palette.Border));
        var name = Ui.Body(settlement.Name, "Text", Palette.Ink, 12);
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        pill.AddChild(name);
        tag.AddChild(dot);
        tag.AddChild(pill);
        foreach (var part in new Control[] { dot, pill }) part.MouseFilter = MouseFilterEnum.Ignore;
        tag.MouseEntered += () => ShowCard(tag);
        tag.MouseExited += () => HoverCard.Visible = false;
        tag.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Host.Diorama.Focus(settlement.Position!, Zoom.Town);
        };
        return tag;
    }

    /// <summary>The settlement's dark card above its tag: "Carrow Ford · Town", who holds it, people and events.</summary>
    public void ShowCard(SettlementTag tag)
    {
        Ui.Clear(HoverCard);
        var column = Ui.Column("Column", 6);
        var kicker = Ui.Kicker(tag.Settlement.Card.Kicker);
        kicker.AddThemeColorOverride("font_color", new Color(0.73f, 0.72f, 0.69f));
        column.AddChild(kicker);
        var title = Ui.Title(tag.Settlement.Card.Title, 22, italic: false);
        title.AddThemeColorOverride("font_color", Ui.C(Palette.Card));
        column.AddChild(title);
        if (tag.Settlement.Card.Line.Length > 0)
            column.AddChild(Ui.Body(tag.Settlement.Card.Line, "Line", new System.Numerics.Vector3(0.83f, 0.82f, 0.8f), 12));
        HoverCard.AddChild(column);
        HoverCard.Position = tag.Position + new Vector2(30, -110);
        HoverCard.Visible = true;
    }

    private PanelContainer ControlCard()
    {
        var card = Ui.Place(Ui.Card("Card", 280), 1, 0, -40, 40);
        var column = Ui.Column("Column", 18);
        card.AddChild(column);

        var zoom = Ui.Column("Zoom", 8);
        zoom.AddChild(Ui.Kicker("Zoom"));
        var presets = Ui.Row("Presets", 6);
        foreach (var preset in Enum.GetValues<Zoom>())
        {
            var button = Ui.Button(preset.ToString(), preset.ToString());
            button.Pressed += () => Host.Diorama.Rig.Preset(preset);
            presets.AddChild(button);
        }
        zoom.AddChild(presets);
        column.AddChild(zoom);

        var layers = Ui.Column("Layers", 4);
        layers.AddChild(Ui.Kicker("Layers"));
        foreach (var (layer, label) in new[]
                 {
                     (MapLayers.Armies, "Armies & fronts"), (MapLayers.Borders, "Borders"), (MapLayers.Settlements, "Settlements"), (MapLayers.Events, "Events"),
                     (MapLayers.Structures, "Landmarks & labyrinths"), (MapLayers.TradeRoutes, "Trade routes"),
                 })
        {
            var box = new CheckBox { Name = layer.ToString(), Text = label, ButtonPressed = Host.Diorama.Layers.HasFlag(layer) };
            box.AddThemeFontSizeOverride("font_size", 12);
            box.Toggled += on => SetLayer(layer, on);
            layers.AddChild(box);
        }
        column.AddChild(layers);

        var history = Ui.Column("HistoryBlock", 8);
        var header = Ui.Row("Header", 0);
        var historyKicker = Ui.Kicker("History");
        historyKicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(historyKicker);
        _yearLabel = Ui.Kicker("", "Year");
        header.AddChild(_yearLabel);
        history.AddChild(header);
        History = new HSlider { Name = "History", MinValue = 1, MaxValue = Math.Max(2, Now), Step = 1, Value = Year, CustomMinimumSize = new Vector2(0, 18) };
        // Dragging the slider passes many years a frame; rebuild only for the last one each frame.
        History.ValueChanged += value => _pendingYear = (int)value;
        history.AddChild(History);
        var ends = Ui.Row("Ends", 0);
        var first = Ui.Line("Year 1", "First", Palette.Muted, 12);
        first.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ends.AddChild(first);
        ends.AddChild(Ui.Line("Now", "Now", Palette.Muted, 12));
        history.AddChild(ends);
        column.AddChild(history);

        var actions = Ui.Row("Actions", 6);
        var reset = Ui.Button("Reset view", "Reset");
        reset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        reset.Pressed += () =>
        {
            Host.Diorama.Rig.Reset();
            ShowYear(Now);
        };
        var snapshot = Ui.Button("Snapshot", "Snapshot");
        snapshot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        snapshot.Pressed += () => Snapshot();
        actions.AddChild(reset);
        actions.AddChild(snapshot);
        column.AddChild(Ui.Ruled("ActionsRow", actions));
        return card;
    }

    /// <summary>Saves what the screen shows to user://snapshots; returns the file, or null without a real display.</summary>
    public string? Snapshot()
    {
        var image = GetViewport().GetTexture()?.GetImage();
        if (image is null || image.IsEmpty()) return null;
        var folder = System.IO.Path.Combine(OS.GetUserDataDir(), "snapshots");
        System.IO.Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, $"{Map.WorldName.ToLowerInvariant().Replace(' ', '-')}-year-{Year}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        image.SavePng(file);
        Host.Status.Text = $"Snapshot saved · {System.IO.Path.GetFileName(file)}";
        return file;
    }
}
```

Replace `game/tests/HeadlessChecks.cs` entirely with:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// Runs the real scenes headless against a fresh tiny world and checks what they show (sub-project 4 §7). Exits 0 when
/// every check passes, 1 otherwise; CI runs it with `godot --headless --path game res://tests/HeadlessChecks.tscn`.
/// </summary>
public partial class HeadlessChecks : Node
{
    private readonly List<string> _failures = new();
    private int _checks;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "checks", $"check-{System.Guid.NewGuid():N}.fw.db");
        try
        {
            var session = await Task.Run(() => Session.Create(path, 3, WorldSize.Tiny, () => System.DateTimeOffset.UtcNow, prehistoryYears: 8));
            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            main.Use(session);
            AddChild(main);
            await Until(() => main.CaughtUp, "the world catches up");
            await RunChecks(main);
        }
        catch (System.Exception e)
        {
            Fail($"unexpected {e}");
        }
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GD.Print(_failures.Count == 0 ? $"HEADLESS CHECKS PASSED ({_checks})" : $"HEADLESS CHECKS FAILED: {string.Join("; ", _failures)}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>The checks for each screen; later tasks add theirs.</summary>
    private async Task RunChecks(Main main)
    {
        Check(main.Status.Text.StartsWith("Caught up · "), $"status reads caught up (\"{main.Status.Text}\")");
        await Frames(2);
        await DioramaChecks(main);
        await DigestChecks(main);
        await MapChecks(main);
        await PageChecks(main);
        await ReadingChecks(main);
        await WarChecks(main);
    }

    /// <summary>Stages a small war (a host marching across a border into land it occupies) and checks the war layer.</summary>
    private async Task WarChecks(Main main)
    {
        var session = main.Session!;
        var (armyRef, commander) = session.Read((world, log) =>
        {
            var link = world.Geography!.Graph.Links.First(l => world.Regions[l.A].OwnerRealmId != 0 && world.Regions[l.B].OwnerRealmId != 0
                                                             && world.Regions[l.A].OwnerRealmId != world.Regions[l.B].OwnerRealmId);
            var (home, target) = (world.Regions[link.A], world.Regions[link.B]);
            var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
            var war = new FantasyWorld.Sim.Model.War { Id = world.Ids.Take("war"), Name = "The Staged War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id } };
            world.Wars[war.Id] = war;
            var leader = world.Characters.Values.First(c => c.IsActive);
            var army = new FantasyWorld.Sim.Model.Army
            {
                Id = world.Ids.Take("army"), Name = $"The Host of {a.Name}", RealmId = a.Id, WarId = war.Id, CommanderId = leader.Id, Levies = 800,
                RegionId = home.Id, Path = { target.Id }, TargetRegionId = target.Id,
            };
            world.Armies[army.Id] = army;
            target.OccupierRealmId = a.Id;
            return (army.Ref, leader.Ref);
        });
        main.Router.Go(new Screen(ScreenKind.Map));
        main.Diorama.Show(session.Map());
        (main.Current as MapHudView)?.Today(session.Map());
        main.Diorama.Rig.Reset();
        await Frames(90);
        var war = main.Diorama.War;
        Check(war.Banners.GetChildCount() == 1 && war.Arrows.GetChildCount() == 1, "a host on the march has a banner and a campaign arrow");
        Check(war.Hatch.Mesh is not null && war.Outline.Mesh is not null, "occupied land is hatched and outlined");
        Check(war.Fronts.Mesh is ArrayMesh { } fronts && fronts.GetSurfaceCount() >= 2, "the front between the sides is drawn with its teeth");
        var tag = (main.Current as MapHudView)?.TagLayer.GetChildren().OfType<ArmyTag>().FirstOrDefault();
        Check(tag is not null && tag.Army.Ref == armyRef, "the host is named on the map");
        await Shot("war");
        var banner = war.BannerAt(armyRef)!.Value;
        var screen = main.Diorama.Camera.UnprojectPosition(banner + new Godot.Vector3(0, WarLayer.BannerHeight * 0.7f, 0));
        Check(main.Diorama.Pick(screen) == armyRef, "clicking a banner picks its host");
        main.Router.Home();
        await Frames(2);
        main.Router.Open(commander);
        await Frames(2);
        Check(main.Current is CharacterView c && c.Character.Ref == commander, "a host's commander has a page");
        main.Router.Home();
    }

    private async Task ReadingChecks(Main main)
    {
        var session = main.Session!;
        var realm = session.Read((w, _) => w.Realms.Keys.First());
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("realm", realm));
        await Frames(2);
        Check(main.Current is RealmView r && r.GetNode<Label>("Left/Title").Text == $"{r.Realm.Title}.", "a realm link opens the realm's page");
        await Shot("realm");
        if (session.Read((w, _) => w.Artifacts.Keys.Cast<long?>().FirstOrDefault()) is { } artifact)
        {
            main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("artifact", artifact));
            await Frames(2);
            Check(main.Current is ArtifactView a && a.GetNode<Label>("Left/Title").Text == $"{a.Artifact.Name}.", "an artifact link opens its page");
        }
        main.Router.Go(new Screen(ScreenKind.Chronicle));
        await Frames(2);
        var chronicle = main.Current as ChronicleView;
        Check(chronicle is not null && chronicle.Page == chronicle.Chronicle.Pages && chronicle.Body.GetChildCount() == chronicle.Chronicle.Seasons.Count,
            "the full chronicle opens at its latest page, by season");
        if (chronicle is not null)
        {
            chronicle.Filter(FantasyWorld.Sim.Views.Category.War);
            Check(chronicle.Chronicle.Seasons.SelectMany(s => s.Rows).All(r => r.Category == FantasyWorld.Sim.Views.Category.War), "the chronicle filters by system");
            await Shot("chronicle");
        }

        main.Router.Home();
        main.Router.Go(new Screen(ScreenKind.Map));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView, "Esc goes back");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, Pressed = true });
        await Frames(2);
        Check(main.Current is MapHudView, "M opens the map");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView && !main.Router.CanGoBack, "H goes home");

        var news = session.Read((w, l) => l.All.Last(e => e.Importance >= Main.LiveToastImportance).Id);
        main.Toast("A test of the live notice.", news);
        await Frames(2);
        Check(main.CurrentToast is { Visible: true }, "notable events are announced while the game is open");
        main.CurrentToast!.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Check(main.Router.Current == new Screen(ScreenKind.Why, news), "and the notice opens its why chain");
        main.Router.Home();
    }

    private async Task PageChecks(Main main)
    {
        var session = main.Session!;
        var person = session.Read((w, l) => l.All.Reverse().SelectMany(e => e.Actors.Values).First(a => a.Kind == "char" && w.Characters.ContainsKey(a.Id)).Id);
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("char", person));
        await Frames(3);
        var character = main.Current as CharacterView;
        Check(character is not null && !main.Diorama.Visible, "a character link opens their page over the diorama");
        if (character is null) return;
        var screen = character.Character;
        Check(character.GetNode<Label>("Left/Who/Title").Text == $"{screen.Name}.", "the page is titled with their name");
        Check(character.GetNode("Left/Chips").GetChildCount() == screen.Traits.Count + (screen.Ambition.Length > 0 ? 1 : 0), "traits and ambition are chips");
        Check(character.Timeline.GetChildren().Count(c => c.Name.ToString().StartsWith("Event") || c.Name == "Highlight") == screen.Timeline.Count, "every deed is on the timeline");
        Check(character.GetNode("Ties/Column/List").GetChildCount() == Math.Max(1, screen.Ties.Count), "ties are listed");
        await Shot("character");
        var followed = session.IsFollowed(screen.Ref);
        character.ToggleFollow();
        Check(session.IsFollowed(screen.Ref) != followed && character.FollowButton.Text == (followed ? "Follow" : "Following"), "Follow follows");
        character.ToggleFollow();

        character.GetNode<Button>("Ties/Column/Actions/Family").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shown = (main.Current as FamilyView)?.FindChildren("Person*", "", true, false).Count ?? -1;
        Check(shown == session.Family(person).Nodes.Count, $"the family tree shows everyone ({shown} of {session.Family(person).Nodes.Count})");
        await Shot("family");
        main.Router.Back();
        await Frames(2);
        Check(main.Current is CharacterView, "Back returns to the character");

        var eventId = session.Read((w, l) => l.All.Last(e => e.Causes.Count > 0 && l.TryGet(e.Causes[0].EventId, out _)).Id);
        main.Router.Explain(eventId);
        await Frames(3);
        var why = main.Current as WhyView;
        Check(why is not null, "a why chain opens");
        if (why is null) return;
        Check(why.Graph.Cards.Count == 1 + why.Why.Layers.Sum(l => l.Count) && why.Canvas.FindChildren("Card*", "", false, false).Count == why.Graph.Cards.Count,
            "every cause is a card");
        Check(why.GetNode<Label>("Intro/Summary").Text == why.Why.Summary, "the chain is summarised");
        await Shot("why");
        Check(why.Canvas.FindChildren("Title", "RichTextLabel", true, false).Cast<Control>().All(t => t.MouseFilter == Control.MouseFilterEnum.Pass),
            "clicks on a card's words reach the card");
        var cause = why.Graph.Cards[1].Card.EventId;
        why.Canvas.GetNode<PanelContainer>($"Card{cause}").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(2);
        Check(main.Router.Current == new Screen(ScreenKind.Why, cause), "clicking a cause traces it further back");
        main.Router.Back();
        await Frames(2);
        var again = (WhyView)main.Current!;
        again.GetNode<Button>("Chain/Column/ActionsRow/Actions/TraceFurther").EmitSignal(BaseButton.SignalName.Pressed);
        Check(again.ShowingConsequences && again.Graph.Cards.Count == 1 + again.Why.Consequences.Count, "trace further shows what it led to");
        main.Router.Explain(long.MaxValue);
        await Frames(3);
        Check(main.Router.Current != new Screen(ScreenKind.Why, long.MaxValue) && main.Current is not null && main.CurrentToast is not null,
            "a page whose subject is gone says so and goes back instead of breaking");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.Visible && main.Current is DigestView, "home is the digest over the diorama");

        var rig = main.Diorama.Rig;
        var peak = main.Diorama.Markers.Count == 0 ? Godot.Vector3.Zero : Diorama.ToGodot(main.Diorama.Markers[0].Position);
        rig.Focus(new System.Numerics.Vector3(peak.X, 0, peak.Z), Zoom.Town);
        rig.Drag(0, 100_000);
        rig.Drag(0, -100_000);
        rig.Scroll(100);
        await Frames(120);
        var eye = main.Diorama.Camera.Position;
        Check(eye.Y > Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(main.Geography!.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)),
            "the camera never goes below the ground");
        rig.Reset();
    }

    private async Task MapChecks(Main main)
    {
        main.Diorama.Rig.Reset();
        main.Router.Go(new Screen(ScreenKind.Map));
        await Frames(30);
        var view = main.Current as MapHudView;
        Check(view is not null, "the map screen opens");
        if (view is null) return;
        var map = view.Map;
        Check(view.GetNode<Label>("Intro/Title").Text == $"{map.WorldName}.", "the map is titled with the world's name");
        Check(view.GetNode<Label>("Intro/Summary").Text == map.Summary, "and summarised");
        var tagged = map.Settlements.Count(s => MapLabels.Shows(s, view.Host.Diorama.Rig.NearestZoom, main.Diorama.Layers));
        Check(view.TagLayer.GetChildren().OfType<SettlementTag>().Count() == tagged, $"settlements big enough for the zoom are named ({tagged})");
        Check(view.GetNode("Legend").GetChildCount() == map.Legend.Count + (map.Armies is { Count: > 0 } ? 2 : 0), "the legend lists the realms");
        await Shot("map");

        var tags = view.TagLayer.GetChildren().OfType<SettlementTag>().ToList();
        if (tags.Count > 0)
        {
            view.ShowCard(tags[0]);
            Check(view.HoverCard.Visible && view.HoverCard.FindChildren("Title", "Label", true, false).OfType<Label>().Single().Text == tags[0].Settlement.Card.Title,
                "hovering a settlement shows who holds it");
            await Shot("map-card");
        }

        var borders = view.GetNode<CheckBox>("Card/Column/Layers/Borders");
        borders.ButtonPressed = false;
        Check(!main.Diorama.BorderLines.Visible && !main.Diorama.Layers.HasFlag(MapLayers.Borders), "the borders layer can be hidden");
        borders.ButtonPressed = true;
        var trade = view.GetNode<CheckBox>("Card/Column/Layers/TradeRoutes");
        trade.ButtonPressed = false;
        Check(!main.Diorama.RoadLines.Visible, "the trade routes layer can be hidden");
        trade.ButtonPressed = true;
        Check(main.Diorama.RoadLines.Visible && main.Diorama.Roads.Count == (map.Routes?.Count ?? 0), "every trade road is drawn");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = false;
        Check(!main.Diorama.SettlementLayer.Visible && view.TagLayer.GetChildren().OfType<SettlementTag>().All(t => t.Settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent),
            "hiding settlements leaves only places with news");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = true;

        view.History.Value = 2;
        await Frames(2);
        Check(view.Year == 2 && view.Map.Historical && view.Map.Year == 2 && main.Diorama.ShownYear == 2, "the history slider shows the map of Year 2");
        await Shot("map-year-2");
        view.GetNode<Button>("Card/Column/ActionsRow/Actions/Reset").EmitSignal(BaseButton.SignalName.Pressed);
        Check(view.Year == view.Now && main.Diorama.ShownYear is null && view.History.Value == view.Now, "reset view returns to today");

        view.GetNode<Button>("Card/Column/Zoom/Presets/Region").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(90);
        Check(main.Diorama.Rig.NearestZoom == Zoom.Region && view.GetNode<Label>("Intro/Kicker").Text.EndsWith("REGION VIEW"), "the zoom presets descend");

        view.History.Value = 2;
        var settlement = map.Settlements.First(s => s.Position is not null);
        main.Router.Open(settlement.Ref);
        await Frames(2);
        Check(main.Current is MapHudView { Focus: { } focus } && focus == settlement.Ref, "a linked place opens the map");
        Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Town)
              && Diorama.ToGodot(main.Diorama.Rig.GoalTarget).DistanceTo(Diorama.ToGodot(FantasyWorld.Client.Projection.ToBlock(main.Geography!.Terrain, settlement.Position!, 0))) < 0.01f,
            "and flies to it");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.ShownYear is null, "leaving the map returns the diorama to today");
    }

    private async Task DigestChecks(Main main)
    {
        main.Router.Home();
        main.Diorama.Rig.Reset();
        await Frames(60);
        var view = main.Current as DigestView;
        Check(view is not null, "the digest is the first screen");
        if (view is null) return;
        var digest = main.Digest!;
        Check(view.GetNode<Label>("Intro/Title").Text == digest.Title, "the digest shows the year");
        Check(view.GetNode<Label>("Intro/Subtitle").Text == digest.Subtitle, "and the time away");
        Check(main.Session!.Digest().Items.Count == 0 && main.Session.Digest().Headline is null, "showing the digest marks it seen");
        Check(digest.Headline is null || view.Content.GetNode<RichTextLabel>("Headline/HeadlineTitle").GetParsedText() == digest.Headline.Title.Text,
            "the headline is shown");
        Check(view.Content.GetNodeOrNull("Items")?.GetChildCount() == digest.Items.Count, $"every item is listed ({digest.Items.Count})");
        Check(view.PinLayer.GetChildCount() == digest.Pins.Count && main.Diorama.Pins.Count == digest.Pins.Count, $"one pin per place ({digest.Pins.Count})");
        await Shot("digest");

        if (digest.Pins.Count > 0)
        {
            var pin = view.PinLayer.GetChild<PinTag>(0);
            var expected = main.Diorama.ScreenOf(main.Diorama.Pins[0].Position) - new Godot.Vector2(11, 11);
            Check(pin.Position.DistanceTo(expected) < 1, "pins follow their places on screen");
            view.FlyTo(digest.Pins[0].EventId);
            await Frames(2);
            Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Region), "an item flies the camera to its place");
        }

        view.ShowChronicle(true);
        await Frames(2);
        Check(view.Content.GetNodeOrNull("Chronicle") is not null && view.ShowingChronicle, "the toggle shows the full chronicle");
        await Shot("digest-chronicle");
        view.ShowChronicle(false);

        if (digest.Headline is { CanExplain: true } headline)
        {
            view.Content.GetNode<Button>("Headline/Why").EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.Router.Current == new Screen(ScreenKind.Why, headline.EventId), "\"Why did this happen?\" opens the why chain");
            main.Router.Back();
            await Frames(2);
            Check(main.Current is DigestView, "Back returns to the digest");
        }
    }

    private async Task DioramaChecks(Main main)
    {
        var diorama = main.Diorama;
        var map = main.Session!.Map();
        Check(diorama.Block.Mesh is ArrayMesh { } block && block.GetSurfaceCount() == 2, "the block has a top and walls");
        Check(diorama.GetNodeOrNull<MeshInstance3D>("Water")?.Mesh is not null, "the sea is drawn");
        var settlements = map.Settlements.Count(s => s.Position is not null);
        Check(diorama.Markers.Count(m => m.Shape == MarkerShape.Settlement) == settlements
              && diorama.SettlementLayer.GetNode<MultiMeshInstance3D>("Roofs").Multimesh.InstanceCount >= 3 * settlements,
            "every settlement is a cluster of houses");
        Check(diorama.StructureLayer.GetChildCount() == map.Structures.Count(s => s.Position is not null), "every landmark and labyrinth stands on the block");
        Check(diorama.TreeCount > 100, $"woods are planted ({diorama.TreeCount} trees)");
        Check(map.Borders.Count == 0 || diorama.BorderLines.Mesh is ArrayMesh { } lines && lines.GetSurfaceCount() == 2, "realm borders are drawn, ink over a halo");

        diorama.Rig.Reset();
        await Frames(90);
        await Shot("diorama");
        Check(diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Eye)) < 0.01f, "the camera follows the rig");
        var target = diorama.Markers[0];
        var onScreen = diorama.ScreenOf(target.Position);
        Check(diorama.Pick(onScreen) is { } picked && (picked == target.Ref || diorama.Markers.Any(m => m.Ref == picked && diorama.ScreenOf(m.Position).DistanceTo(onScreen) < 1)),
            "clicking a marker picks it");
        Check(diorama.Pick(new Godot.Vector2(-500, -500)) is null, "clicking empty space picks nothing");

        var settlement = map.Settlements.First(s => s.Position is not null);
        diorama.Focus(settlement.Position!, Zoom.Town);
        await Frames(120);
        await Shot("diorama-town");
        Check(diorama.Rig.NearestZoom == Zoom.Town && diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Target)) < 1f, "focusing descends to the town");
    }

    /// <summary>With `-- --shots &lt;dir&gt;` (and a real display), saves what the screen shows, for visual review.</summary>
    private async Task Shot(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = System.Array.IndexOf(args, "--shots");
        if (at < 0 || at + 1 >= args.Length || DisplayServer.GetName() == "headless") return;
        await Frames(4);
        System.IO.Directory.CreateDirectory(args[at + 1]);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(args[at + 1], name + ".png"));
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) Fail(what);
    }

    private void Fail(string what)
    {
        _failures.Add(what);
        GD.PrintErr("FAIL: " + what);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Until(System.Func<bool> condition, string what, int maxFrames = 600)
    {
        for (var i = 0; i < maxFrames && !condition(); i++) await Frames(1);
        Check(condition(), what);
    }
}
```

Replace `sim/FantasyWorld.Client/MapLabels.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>The map HUD's layer toggles (sub-project 4 §4); Armies covers hosts, fronts, occupation and battles.</summary>
[Flags]
public enum MapLayers
{
    None = 0,
    Borders = 1,
    Settlements = 2,
    Events = 4,
    Structures = 8,
    TradeRoutes = 16,
    Armies = 32,
    Default = Borders | Settlements | Events | Structures | Armies | TradeRoutes,
}

/// <summary>Which settlements get a name tag at each zoom, and where a linked place is on the map.</summary>
public static class MapLabels
{
    /// <summary>
    /// Settlements with news (a conflict or this season's event) are tagged whenever the Events layer is on; others
    /// when Settlements is on and they are big enough for the zoom: towns and cities from afar, villages at region
    /// zoom, everything at town zoom.
    /// </summary>
    public static bool Shows(MapSettlement settlement, Zoom zoom, MapLayers layers)
    {
        if (settlement.Position is null) return false;
        if (settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent) return layers.HasFlag(MapLayers.Events);
        if (!layers.HasFlag(MapLayers.Settlements)) return false;
        var tier = Enum.TryParse<SettlementTier>(settlement.Tier, out var t) ? t : SettlementTier.Hamlet;
        return tier >= zoom switch { Zoom.Realm => SettlementTier.Town, Zoom.Region => SettlementTier.Village, _ => SettlementTier.Hamlet };
    }

    /// <summary>Where a settlement, region or structure sits on the map, if it is on it.</summary>
    public static MapPoint? Find(MapScreen map, EntityRef entity) =>
        map.Settlements.FirstOrDefault(s => s.Ref == entity)?.Position
        ?? map.Regions.FirstOrDefault(r => r.Ref == entity)?.Center
        ?? map.Structures.FirstOrDefault(s => s.Ref == entity)?.Position;

    /// <summary>The zoom a linked place opens at: a region shows its whole extent, anything smaller the town view.</summary>
    public static Zoom ZoomFor(EntityRef entity) => entity.Kind == Region.Kind ? Zoom.Region : Zoom.Town;
}
```

`sim/FantasyWorld.Client/RouteLayout.cs`:
```csharp
using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>A trade road on the block: its line over the ground and whether it is open.</summary>
public sealed record Road(EntityRef Ref, string Name, bool Open, IReadOnlyList<Vector3> Line);

/// <summary>
/// Trade roads laid on the diorama (the map's *Trade routes* layer): each route's way, market to market through the
/// regions between, smoothed into a gentle curve and laid over the ground point by point so it follows the land.
/// </summary>
public static class RouteLayout
{
    public const float Step = 0.008f;
    public const float Lift = 0.004f;

    public static IReadOnlyList<Road> Roads(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var roads = new List<Road>();
        foreach (var route in map.Routes ?? Array.Empty<MapRoute>())
        {
            var points = route.Path.Select(p => new Vector2((float)p.X, (float)p.Y)).ToList();
            for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed: false);
            var line = new List<Vector3>();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var (a, b) = (points[i], points[i + 1]);
                var steps = Math.Max(1, (int)(Vector2.Distance(a, b) * 2 / Step));
                for (var s = 0; s < steps; s++) line.Add(Ground(grid, Vector2.Lerp(a, b, s / (float)steps)));
            }
            line.Add(Ground(grid, points[^1]));
            roads.Add(new Road(route.Ref, route.Name, route.State == "open", line));
        }
        return roads;
    }

    private static Vector3 Ground(Sim.Geo.TerrainGrid grid, Vector2 p) =>
        new(p.X * 2 - 1, Math.Max(0, BlockMesh.HeightAt(grid, p.X, p.Y)) + Lift, p.Y * 2 - 1);
}
```

Replace `sim/FantasyWorld.Sim/Views/MapScreen.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum PinMarker { Plain, Realm, RecentEvent, Conflict }

public sealed record MapRegion(EntityRef Ref, string Name, string Biome, MapPoint? Center, EntityRef? Owner, EntityRef? Occupier, string Colour);

public sealed record MapCard(string Kicker, string Title, string Line);

public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card);

public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// A host in the field: who leads it and who rides with it, its strength, what it is doing, and the road it means to
/// take (its region, then each region on its path, ending at its target).
/// </summary>
public sealed record MapArmy(
    EntityRef Ref, string Name, EntityRef Realm, string Colour, EntityRef War, string Side, Mention? Commander, IReadOnlyList<Mention> Knights,
    int Soldiers, string Objective, bool Besieging, MapPoint Position, IReadOnlyList<MapPoint> Route);

/// <summary>A trade road on the map: its markets, the way it runs (market, regions between, market) and its state.</summary>
public sealed record MapRoute(EntityRef Ref, string Name, EntityRef From, EntityRef To, string State, int Volume, IReadOnlyList<MapPoint> Path);

/// <summary>A battle of the last year: where, who won, and the event to explain it.</summary>
public sealed record MapBattle(long EventId, int Year, MapPoint Position, EntityRef Victor, EntityRef Vanquished, EntityRef War);

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend,
    IReadOnlyList<MapArmy>? Armies = null, IReadOnlyList<MapBattle>? Battles = null, IReadOnlyList<MapRoute>? Routes = null)
{
    /// <summary>Battles shown on the map: those of the last year.</summary>
    public const int BattleDays = 360;
    public static MapScreen Build(World world, EventLog log, WorldDate? asOf = null)
    {
        var requested = asOf is { } d && d < world.Date ? d : world.Date;
        var map = History.PoliticalMapAt(world, log, requested);
        var date = map.Historical ? requested : world.Date;
        var today = date == world.Date;
        var political = map.Regions;
        EntityRef? RealmRef(long id) => id != 0 && world.Realms.TryGetValue(id, out var r) ? r.Ref : null;
        string Colour(long id) => id != 0 && world.Realms.TryGetValue(map.TopOf(id), out var top) ? top.Colors[0] : "none";

        var regions = world.Regions.Values.Select(r => new MapRegion(r.Ref, r.Name, r.Biome.ToString(), MapPoint.Of(world, r.Ref),
            RealmRef(political[r.Id].OwnerRealmId), RealmRef(political[r.Id].OccupierRealmId), Colour(political[r.Id].OwnerRealmId))).ToList();

        var abandoned = log.All.Where(e => e.Type == "SettlementAbandoned" && e.Date <= date && e.Place is { Kind: Settlement.Kind })
            .Select(e => e.Place!.Value.Id).ToHashSet();
        var season = date.Day - 90;
        var settlements = new List<MapSettlement>();
        foreach (var s in world.Settlements.Values.Where(s => !abandoned.Contains(s.Id)))
        {
            var state = political[s.RegionId];
            var here = log.Involving(s.Ref).Where(e => e.Date <= date).ToList();
            var marker = state.OccupierRealmId != 0 ? PinMarker.Conflict
                : here.Any(e => e.Date.Day > season && e.Importance >= 30) ? PinMarker.RecentEvent
                : state.OwnerRealmId != 0 ? PinMarker.Realm : PinMarker.Plain;
            var holder = world.Realms.GetValueOrDefault(state.OwnerRealmId);
            var ceded = log.Involving(world.Regions[s.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded" && e.Date <= date);
            var title = holder is null ? "Held by no realm"
                : $"Held by the {Statecraft.Title(holder)} {(ceded is null ? "since the founding" : $"since Year {ceded.Date.Year}")}";
            if (state.OccupierRealmId != 0 && world.Realms.TryGetValue(state.OccupierRealmId, out var occupier))
                title += $", occupied by {occupier.Name}";
            var thisYear = here.Count(e => e.Date.Year == date.Year);
            var line = (today ? $"Pop. {s.Population.ToString("N0", CultureInfo.InvariantCulture)} · " : "")
                       + $"{thisYear} event{(thisYear == 1 ? "" : "s")} this year";
            settlements.Add(new MapSettlement(s.Ref, s.Name, s.Tier.ToString(), MapPoint.Of(world, s.Ref), holder?.Ref, marker,
                new MapCard($"{s.Name} · {s.Tier}", title, line)));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
            .ToList();

        var borders = world.Geography is { } geo
            ? geo.Graph.Links.Where(l => political[l.A].OwnerRealmId != political[l.B].OwnerRealmId)
                .Select(l => new Border(world.Regions[Math.Min(l.A, l.B)].Ref, world.Regions[Math.Max(l.A, l.B)].Ref)).Distinct().ToList()
            : new List<Border>();

        var conflicts = world.Wars.Values.Where(w => w.StartDay <= date.Day && (!w.Ended || log.TryGet(w.EndedEventId, out var end) && end.Date > date))
            .Select(w => (War: w, Sides: map.Wars.GetValueOrDefault(w.Id) ?? new WarSides(w.Attackers, w.Defenders)))
            .Select(x => new Conflict(x.War.Ref, x.War.Name, x.Sides.Attackers.Select(id => world.Realms[id].Ref).ToList(),
                x.Sides.Defenders.Select(id => world.Realms[id].Ref).ToList(),
                political.Values.Where(p => p.OccupyingWarId == x.War.Id).Select(p => world.Regions[p.RegionId].Ref).ToList()))
            .ToList();

        var independent = political.Values.Select(p => p.OwnerRealmId).Where(id => id != 0).Select(map.TopOf).Distinct()
            .Select(id => world.Realms[id]).OrderBy(r => r.Id).ToList();
        var legend = independent.Select(r => new LegendEntry(r.Name, r.Colors[0]))
            .Append(new LegendEntry("Active conflict", "conflict")).Append(new LegendEntry("Event this season", "event")).ToList();
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend, today ? ArmiesIn(world) : Array.Empty<MapArmy>(), BattlesBefore(world, log, date), RoutesAt(world, log, date, today));
    }

    /// <summary>Hosts in the field today (armies are not kept in history, so past maps show none).</summary>
    private static IReadOnlyList<MapArmy> ArmiesIn(World world)
    {
        Mention? Of(long id) => world.Characters.TryGetValue(id, out var c) && c.IsActive ? new Mention(c.Ref, Names.Full(world, c)) : null;
        MapPoint? At(long region) => world.Regions.TryGetValue(region, out var r) ? MapPoint.Of(world, r.Ref) : null;
        var armies = new List<MapArmy>();
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded).OrderBy(a => a.Id))
        {
            if (At(army.RegionId) is not { } here || !world.Wars.TryGetValue(army.WarId, out var war) || !world.Realms.TryGetValue(army.RealmId, out var realm)) continue;
            var route = new List<MapPoint> { here };
            route.AddRange(army.Path.Select(At).OfType<MapPoint>());
            armies.Add(new MapArmy(army.Ref, army.Name, realm.Ref, realm.Colors[0], war.Ref, war.IsAttacker(realm.Id) ? "attacker" : "defender",
                Of(army.CommanderId), army.Knights.Select(Of).OfType<Mention>().ToList(), army.Soldiers, army.Objective.ToString().ToLowerInvariant(),
                world.Regions[army.RegionId].BesiegerArmyId == army.Id, here, route));
        }
        return armies;
    }

    /// <summary>Roads opened by the date; their state then is replayed from their severing and restoring.</summary>
    private static IReadOnlyList<MapRoute> RoutesAt(World world, EventLog log, WorldDate date, bool today)
    {
        var routes = new List<MapRoute>();
        foreach (var route in world.Routes.Values)
        {
            if (!log.TryGet(route.OpenedEventId, out var opened) || opened.Date > date) continue;
            var state = route.State;
            if (!today)
            {
                var last = log.Involving(route.Ref).LastOrDefault(e => e.Date <= date && e.Type is "RouteSevered" or "RouteRestored");
                state = last?.Type == "RouteSevered" ? RouteState.Severed : RouteState.Open;
                // A road closes for good when a market is abandoned.
                if (new[] { route.FromId, route.ToId }.Any(id => log.Involving(world.Settlements[id].Ref).Any(e => e.Type == "SettlementAbandoned" && e.Date <= date)))
                    state = RouteState.Closed;
            }
            if (state == RouteState.Closed) continue;
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            var path = new List<MapPoint>();
            if (MapPoint.Of(world, from.Ref) is { } start) path.Add(start);
            path.AddRange(route.Regions.Skip(1).SkipLast(1).Select(r => MapPoint.Of(world, world.Regions[r].Ref)).OfType<MapPoint>());
            if (MapPoint.Of(world, to.Ref) is { } end) path.Add(end);
            if (path.Count < 2) continue;
            routes.Add(new MapRoute(route.Ref, route.Name, from.Ref, to.Ref, state.ToString().ToLowerInvariant(), (int)Math.Round(today ? route.Volume : 0), path));
        }
        return routes;
    }

    private static IReadOnlyList<MapBattle> BattlesBefore(World world, EventLog log, WorldDate date) =>
        log.All.Where(e => e.Type == "BattleFought" && e.Date <= date && e.Date.Day > date.Day - BattleDays && e.Place is { } p && MapPoint.Of(world, p) is not null)
            .Select(e => new MapBattle(e.Id, e.Date.Year, MapPoint.Of(world, e.Place!.Value)!, e.Actors["victor"], e.Actors["vanquished"], e.Actors["war"]))
            .ToList();

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type == StructureKind.Landmark && !(st.CompletedEventId != 0 && log.TryGet(st.CompletedEventId, out var done) && done.Date <= date))
            return st.State == Model.StructureState.Ruined && RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Building);
        if (st.Type == StructureKind.Landmark && st.State == Model.StructureState.Ruined)
            return RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Standing);
        return st.State.ToString();
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~TradeSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 518` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (70)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add game sim
git commit -m "feat(game): draw trade roads on the map's trade routes layer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Monsters

Spec §3. Each year a named beast may rise in a dangerous land, three times as often during a ley surge, of a breed that fits the land (a wyrm in the mountains, a hydra in the swamp) and mightier the wilder the land; the inventor names it. Beasts grow with age and very old ones slumber until a surge wakes them. Each month a roaming beast may raid the largest settlement in or beside its land: people, food and wealth are lost, unrest rises and its notoriety grows until it becomes a legend. Hunts in `DeedsSystem` now target a named beast when one roams nearby: the arbiter weighs the hunter against half its might; slaying it ends its story with fame (and from a mighty beast a trophy artifact), failing feeds its notoriety.

**Files:**
- Create: `sim/FantasyWorld.Sim/Model/Monster.cs`, `sim/FantasyWorld.Sim/Systems/MonsterSystem.cs`
- Modify: `sim/FantasyWorld.Sim/Agency/Encounters.cs`, `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`, `sim/FantasyWorld.Sim/Model/World.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs`, `sim/FantasyWorld.Sim/Query/Names.cs`, `sim/FantasyWorld.Sim/Systems/DeedsSystem.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`, `sim/FantasyWorld.Sim/Systems/TradeSystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/MonsterSystemTests.cs`

**Interfaces:**
- Consumes: `IInventor`, `IArbiter`, `Encounters`, `DeedsSystem`, `Conditions.LeySurge`/`Dangerous` (Plans 3–5).
- Produces: `enum MonsterState { Roaming, Slumbering, Slain }`; `sealed class Monster { Kind = "monster"; Id; Name; Breed; RegionId; Might; Notoriety; Legendary; BornDay; State; RoseEventId; SlainEventId; SlayerId; Alive; Ref; BreedsOf(biome) }`; `World.Monsters`; `WorldSnapshot.Monsters`; `sealed class MonsterSystem(IInventor?) { RiseDanger; RisePerYear; RaidPerMonth; LegendNotoriety; SlumberAgeYears; Cap(world) }`; `Encounters.HuntBeast(world, hunter, beast)`; events `MonsterRose`, `MonsterRaid`, `MonsterLegendary`, `MonsterSlumbered`, `MonsterWoke`, `MonsterSlain`, `MonsterHuntFailed`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/MonsterSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class MonsterSystemTests
{
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static Monster Stage(World world, EventLog log, long region, double might)
    {
        var id = world.Ids.Take(Monster.Kind);
        var rose = log.Append(world.Date, new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
            Place: world.Regions[region].Ref));
        var beast = new Monster { Id = id, Name = "The Wyrm of Esk", Breed = "wyrm", RegionId = region, Might = might, RoseEventId = rose.Id };
        world.Monsters[beast.Id] = beast;
        return beast;
    }

    [Fact]
    public void ABeastRises_InDangerousLand_OfABreedThatFits()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 95;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 80);
        var rose = sim.Log.All.First(e => e.Type == "MonsterRose");
        var beast = world.Monsters[rose.Actors["monster"].Id];
        Assert.Equal(2, beast.RegionId);
        Assert.Contains(beast.Breed, Monster.BreedsOf(world.Regions[2].Biome));
        Assert.InRange(beast.Might, 1, 100);
        Assert.True(world.Regions[2].Danger >= beast.Might / 2 || !beast.Alive);
    }

    [Fact]
    public void AMightyBeast_Raids_UntilItBecomesALegend()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var town = world.Settlements[1];
        var people = town.Adults;
        var beast = Stage(world, log, town.RegionId, 90);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 20);
        var raids = sim.Log.All.Where(e => e.Type == "MonsterRaid").ToList();
        Assert.NotEmpty(raids);
        Assert.All(raids, r => Assert.Equal(beast.Ref, r.Actors["monster"]));
        Assert.True(town.Adults < people || raids.All(r => r.Place != town.Ref));
        Assert.True(beast.Legendary);
        Assert.Contains(sim.Log.All, e => e.Type == "MonsterLegendary");
    }

    [Fact]
    public void AHero_HuntsTheBeastByName_AndSlaysIt()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var beast = Stage(world, log, 2, 70);
        world.Regions[2].Danger = 60;
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("hunt"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 3);
        var slain = sim.Log.All.First(e => e.Type == "MonsterSlain");
        Assert.Equal(hunter.Ref, slain.Actors["hunter"]);
        Assert.Equal(beast.Ref, slain.Actors["monster"]);
        Assert.Equal((MonsterState.Slain, slain.Id, hunter.Id), (beast.State, beast.SlainEventId, beast.SlayerId));
        Assert.True(hunter.Renown > 0);
        Assert.Contains(world.Artifacts.Values, a => a.HolderId == hunter.Id && a.OriginEventId == slain.Id);
    }

    [Fact]
    public void AFailedHunt_FeedsTheBeastsNotoriety()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var beast = Stage(world, log, 2, 70);
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("hunt"), new FixedArbiter("failure_wounded")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 5);
        Assert.Contains(sim.Log.All, e => e.Type == "MonsterHuntFailed" && e.Actors["monster"] == beast.Ref);
        Assert.True(beast.Notoriety >= 8);
        Assert.True(beast.Alive);
    }

    [Fact]
    public void ARaid_SeversTheRoadsThroughItsLand_ForASeason()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        foreach (var town in world.Settlements.Values) town.Tier = SettlementTier.Town;
        var opened = log.Append(world.Date, new EventDraft("RouteOpened"));
        var road = new TradeRoute { Id = world.Ids.Take(TradeRoute.Kind), Name = "The Road", FromId = 1, ToId = 2, Regions = { 1, 2 }, State = RouteState.Open, OpenedEventId = opened.Id };
        world.Routes[road.Id] = road;
        var beast = Stage(world, log, 1, 90);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MonsterSystem(), new TradeSystem() }, checkInvariants: true);
        while (road.State == RouteState.Open) sim.AdvanceDays(30);
        var cut = sim.Log.Get(road.SeveredEventId);
        Assert.Equal("raid", cut.Payload["reason"]);
        Assert.Equal("MonsterRaid", sim.Log.Get(cut.Causes.Single().EventId).Type);
        (beast.State, beast.SlainEventId) = (MonsterState.Slain, cut.Id);
        sim.AdvanceDays(360);
        var reopened = sim.Log.All.Single(e => e.Type == "RouteRestored");
        Assert.True(reopened.Date.Day - cut.Date.Day >= TradeSystem.RaidDays);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'Monster' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Agency/Encounters.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Agency;

public enum Fate { Unharmed, Wounded, Maimed, Dead }

/// <summary>A legal result of an encounter, with its base probability from the odds.</summary>
public sealed record EncounterOutcome(string Key, double Probability, bool AttackerWins, Fate AttackerFate, Fate DefenderFate)
{
    /// <summary>How it happened, when an LLM arbitrated (spec §8.3): stored as the event's account.</summary>
    public string? Account { get; init; }
}

/// <summary>
/// A dramatic personal conflict (spec §8.3). The engine sets the stage (strengths) and enumerates the
/// legal outcomes with probabilities summing to 1; an arbiter picks one. DefenderId 0 means the wilds
/// (monsters, a labyrinth) rather than a person.
/// </summary>
public sealed record Encounter(
    string Kind,
    long AttackerId,
    long DefenderId,
    double AttackerStrength,
    double DefenderStrength,
    bool ToTheDeath,
    IReadOnlyList<EncounterOutcome> Outcomes)
{
    /// <summary>Outcomes below this base probability are implausible; the LLM arbiter (Plan 5) may not choose them.</summary>
    public const double PlausibilityFloor = 0.05;

    public double AttackerOdds => AttackerStrength / (AttackerStrength + DefenderStrength);

    public bool IsPlausible(EncounterOutcome outcome) => outcome.Probability >= PlausibilityFloor;
}

public interface IArbiter
{
    EncounterOutcome Resolve(Encounter encounter, Rng rng);
}

/// <summary>Seeded roll on the base probabilities: the offline fallback and the test baseline.</summary>
public sealed class RulesArbiter : IArbiter
{
    public EncounterOutcome Resolve(Encounter encounter, Rng rng)
    {
        var roll = rng.NextDouble();
        foreach (var outcome in encounter.Outcomes)
        {
            roll -= outcome.Probability;
            if (roll < 0) return outcome;
        }
        return encounter.Outcomes[^1];
    }
}

/// <summary>Builds encounters: strengths from attributes, traits, wounds, renown and artifacts; legal outcome sets.</summary>
public static class Encounters
{
    public const string Duel = "duel";
    public const string Hunt = "hunt";
    public const string Delve = "delve";

    /// <summary>Fighting strength: prowess + martial/2 + artifact bonuses + traits − wounds + renown/20 (min 1).</summary>
    public static double Strength(World world, Character c)
    {
        var artifacts = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList();
        var strength = c.Attributes.Prowess + artifacts.Sum(a => a.Bonus("prowess"))
                       + (c.Attributes.Martial + artifacts.Sum(a => a.Bonus("martial"))) / 2.0
                       + c.Renown / 20;
        if (c.Has(Traits.Brave)) strength += 2;
        if (c.Has(Traits.Craven)) strength -= 3;
        strength -= 2 * Math.Min(3, c.Wounds.Count);
        var age = c.Age(world.Date);
        if (age < 18) strength -= 4;
        if (age >= 55) strength -= (age - 50) / 5;
        return Math.Max(1, strength);
    }

    public static Encounter DuelBetween(World world, Character attacker, Character defender, bool toTheDeath)
    {
        var a = Strength(world, attacker);
        var d = Strength(world, defender);
        var p = a / (a + d);
        var outcomes = toTheDeath
            ? new List<EncounterOutcome>
            {
                new("attacker_kills", p * 0.97 * 0.6, true, Fate.Unharmed, Fate.Dead),
                new("attacker_maims", p * 0.97 * 0.2, true, Fate.Unharmed, Fate.Maimed),
                new("attacker_wounds", p * 0.97 * 0.2, true, Fate.Wounded, Fate.Wounded),
                new("defender_kills", (1 - p) * 0.97 * 0.6, false, Fate.Dead, Fate.Unharmed),
                new("defender_maims", (1 - p) * 0.97 * 0.2, false, Fate.Maimed, Fate.Unharmed),
                new("defender_wounds", (1 - p) * 0.97 * 0.2, false, Fate.Wounded, Fate.Wounded),
                new("both_die", 0.03, false, Fate.Dead, Fate.Dead),
            }
            : new List<EncounterOutcome>
            {
                new("attacker_first_blood", p * 0.8, true, Fate.Unharmed, Fate.Wounded),
                new("attacker_yields_win", p * 0.2, true, Fate.Unharmed, Fate.Unharmed),
                new("defender_first_blood", (1 - p) * 0.8, false, Fate.Wounded, Fate.Unharmed),
                new("defender_yields_win", (1 - p) * 0.2, false, Fate.Unharmed, Fate.Unharmed),
            };
        return new Encounter(Duel, attacker.Id, defender.Id, a, d, toTheDeath, outcomes);
    }

    /// <summary>Hunting the monsters of a region: the wilds' strength is danger / 4.</summary>
    public static Encounter HuntIn(World world, Character hunter, Region region) =>
        Wilds(Hunt, world, hunter, Math.Max(1, region.Danger / 4), deathShare: 0.15);

    /// <summary>Hunting a named beast: its strength is half its might, and losing to it is often fatal.</summary>
    public static Encounter HuntBeast(World world, Character hunter, Monster beast) =>
        Wilds(Hunt, world, hunter, Math.Max(1, beast.Might / 2), deathShare: 0.25);

    /// <summary>Delving for a lost artifact: the wilds' strength is danger / 3; failure is deadlier.</summary>
    public static Encounter DelveIn(World world, Character delver, Region region) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3), deathShare: 0.3);

    private static Encounter Wilds(string kind, World world, Character attacker, double wilds, double deathShare)
    {
        var a = Strength(world, attacker);
        var p = a / (a + wilds);
        var outcomes = new List<EncounterOutcome>
        {
            new("success", p * 0.7, true, Fate.Unharmed, Fate.Unharmed),
            new("success_wounded", p * 0.3, true, Fate.Wounded, Fate.Unharmed),
            new("failure_wounded", (1 - p) * (1 - deathShare) * 0.7, false, Fate.Wounded, Fate.Unharmed),
            new("failure_maimed", (1 - p) * (1 - deathShare) * 0.3, false, Fate.Maimed, Fate.Unharmed),
            new("failure_death", (1 - p) * deathShare, false, Fate.Dead, Fate.Unharmed),
        };
        return new Encounter(kind, attacker.Id, 0, a, wilds, true, outcomes);
    }
}
```

Replace `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }

        foreach (var r in world.Routes.Values)
        {
            if (!world.Settlements.ContainsKey(r.FromId) || !world.Settlements.ContainsKey(r.ToId)) errors.Add($"Route #{r.Id} joins an unknown market.");
            else if (r.Regions.Count == 0 || r.Regions[0] != world.Settlements[r.FromId].RegionId || r.Regions[^1] != world.Settlements[r.ToId].RegionId)
                errors.Add($"Route #{r.Id} does not run from market to market.");
            if (r.Regions.Any(id => !world.Regions.ContainsKey(id))) errors.Add($"Route #{r.Id} crosses an unknown region.");
            if (!log.TryGet(r.OpenedEventId, out _)) errors.Add($"Route #{r.Id} cites missing event #{r.OpenedEventId}.");
            if (r.State != RouteState.Open && r.Volume != 0) errors.Add($"Route #{r.Id} carries trade while {r.State}.");
        }

        foreach (var m in world.Monsters.Values)
        {
            if (!world.Regions.ContainsKey(m.RegionId)) errors.Add($"Monster #{m.Id} lairs in unknown region #{m.RegionId}.");
            if (m.Might is < 1 or > 100) errors.Add($"Monster #{m.Id} has might {m.Might}.");
            if (!log.TryGet(m.RoseEventId, out _)) errors.Add($"Monster #{m.Id} cites missing event #{m.RoseEventId}.");
            if (m.State == MonsterState.Slain && m.SlainEventId == 0) errors.Add($"Monster #{m.Id} is slain without a slaying.");
        }
    }
}
```

`sim/FantasyWorld.Sim/Model/Monster.cs`:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

public enum MonsterState { Roaming, Slumbering, Slain }

/// <summary>
/// A named beast (sub-project 3 §3): what breed it is, where it lairs, how mighty and how infamous it has grown, and
/// how its story ended. Danger in its region never falls below half its might while it lives.
/// </summary>
public sealed class Monster
{
    public const string Kind = "monster";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Breed { get; init; } = "";
    public long RegionId { get; init; }

    /// <summary>1–100: how hard it is to kill, and how hard it raids.</summary>
    public double Might { get; set; }

    /// <summary>0–100: how widely it is feared; legends lead the digest.</summary>
    public double Notoriety { get; set; }

    public bool Legendary { get; set; }
    public long BornDay { get; init; }
    public MonsterState State { get; set; }
    public long RoseEventId { get; init; }
    public long SlainEventId { get; set; }
    public long SlayerId { get; set; }

    [JsonIgnore] public bool Alive => State != MonsterState.Slain;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>The breeds that haunt each kind of land.</summary>
    public static IReadOnlyList<string> BreedsOf(Biome biome) => biome switch
    {
        Biome.Mountain => new[] { "wyrm", "troll", "roc" },
        Biome.Hills => new[] { "troll", "ogre", "griffin" },
        Biome.Forest => new[] { "wolf-king", "basilisk", "wild hunt" },
        Biome.Jungle => new[] { "basilisk", "manticore", "serpent" },
        Biome.Swamp => new[] { "hydra", "bog wight", "serpent" },
        Biome.Desert => new[] { "sand wyrm", "manticore", "sphinx" },
        Biome.Tundra or Biome.Taiga => new[] { "frost giant", "wendigo", "white wolf" },
        Biome.Savanna or Biome.Grassland => new[] { "chimera", "griffin", "wild hunt" },
        _ => new[] { "kraken", "serpent" },
    };
}
```

Replace `sim/FantasyWorld.Sim/Model/World.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>All simulated state. Grows with characters, realms and armies in Plans 3–4.</summary>
public sealed class World
{
    public required ulong Seed { get; init; }

    /// <summary>The world's own name (shown on every screen); empty for worlds made before it was named.</summary>
    public string Name { get; set; } = "";
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    /// <summary>Named scalar state for systems without entities of their own.</summary>
    public SortedDictionary<string, double> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Named references to "currently active" events, e.g. an ongoing shortage.</summary>
    public SortedDictionary<string, long> Markers { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The static map. Null only for bare worlds built in tests.</summary>
    public Geography? Geography { get; set; }

    public IdGen Ids { get; init; } = new();
    public SortedDictionary<long, Region> Regions { get; init; } = new();
    public SortedDictionary<long, Settlement> Settlements { get; init; } = new();
    public SortedDictionary<long, Culture> Cultures { get; init; } = new();
    public SortedDictionary<long, Faith> Faiths { get; init; } = new();
    public SortedDictionary<long, House> Houses { get; init; } = new();
    public SortedDictionary<long, Character> Characters { get; init; } = new();
    public SortedDictionary<long, Artifact> Artifacts { get; init; } = new();
    public SortedDictionary<long, Realm> Realms { get; init; } = new();
    public SortedDictionary<long, War> Wars { get; init; } = new();
    public SortedDictionary<long, Army> Armies { get; init; } = new();
    public SortedDictionary<long, Structure> Structures { get; init; } = new();
    public SortedDictionary<long, TradeRoute> Routes { get; init; } = new();
    public SortedDictionary<long, Monster> Monsters { get; init; } = new();

    /// <summary>Region → realm at world creation: the baseline that history replays from (map history slider).</summary>
    public SortedDictionary<long, long> FoundingOwners { get; init; } = new();

    /// <summary>Realm → liege at world creation (0 = independent), the baseline for replaying vassalage.</summary>
    public SortedDictionary<long, long> FoundingLieges { get; init; } = new();

    /// <summary>Living notables (not faded), in id order.</summary>
    public IEnumerable<Character> ActiveCharacters => Characters.Values.Where(c => c.IsActive);

    /// <summary>Living notables who live in the settlement, in id order.</summary>
    public IEnumerable<Character> PeopleOf(Settlement settlement) =>
        ActiveCharacters.Where(c => c.SettlementId == settlement.Id);

    /// <summary>The region's settlements that are still inhabited, in id order.</summary>
    public IEnumerable<Settlement> LivingSettlements(Region region) =>
        region.Settlements.Select(id => Settlements[id]).Where(s => !s.Abandoned);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Persist/WorldSnapshot.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Serializable world + queue state. The event log and the static geography are stored separately.
/// Capture references the live entities, so serialize the snapshot immediately.
/// </summary>
public sealed record WorldSnapshot(
    int SchemaVersion,
    ulong Seed,
    long Day,
    DateTimeOffset LastRealUtc,
    long LastSeenEventId,
    SortedDictionary<string, double> Stats,
    SortedDictionary<string, long> Markers,
    List<ScheduledAction> Queue,
    long QueueNextSeq,
    SortedDictionary<string, long>? Ids = null,
    SortedDictionary<long, Region>? Regions = null,
    SortedDictionary<long, Settlement>? Settlements = null,
    SortedDictionary<long, Culture>? Cultures = null,
    SortedDictionary<long, Faith>? Faiths = null,
    SortedDictionary<long, House>? Houses = null,
    SortedDictionary<long, Character>? Characters = null,
    SortedDictionary<long, Artifact>? Artifacts = null,
    SortedDictionary<long, Realm>? Realms = null,
    SortedDictionary<long, War>? Wars = null,
    SortedDictionary<long, Army>? Armies = null,
    SortedDictionary<long, Structure>? Structures = null,
    string? Name = null,
    SortedDictionary<long, long>? FoundingOwners = null,
    SortedDictionary<long, long>? FoundingLieges = null,
    SortedDictionary<long, TradeRoute>? Routes = null,
    SortedDictionary<long, Monster>? Monsters = null)
{
    public const int CurrentVersion = 6;

    public static WorldSnapshot Capture(World world, EventQueue queue) => new(
        CurrentVersion,
        world.Seed,
        world.Date.Day,
        world.LastRealUtc,
        world.LastSeenEventId,
        Sorted.From(world.Stats),
        Sorted.From(world.Markers),
        queue.Snapshot().ToList(),
        queue.NextSeq,
        Sorted.From(world.Ids.Next),
        world.Regions,
        world.Settlements,
        world.Cultures,
        world.Faiths,
        world.Houses,
        world.Characters,
        world.Artifacts,
        world.Realms,
        world.Wars,
        world.Armies,
        world.Structures,
        world.Name,
        world.FoundingOwners,
        world.FoundingLieges,
        world.Routes,
        world.Monsters);

    /// <summary>Rebuilds the world. Older snapshots lack newer entities, which load empty.</summary>
    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
        Ids = new IdGen { Next = Ids ?? new SortedDictionary<string, long>() },
        Regions = Copy(Regions),
        Settlements = Copy(Settlements),
        Cultures = Copy(Cultures),
        Faiths = Copy(Faiths),
        Houses = Copy(Houses),
        Characters = Copy(Characters),
        Artifacts = Copy(Artifacts),
        Realms = Copy(Realms),
        Wars = Copy(Wars),
        Armies = Copy(Armies),
        Structures = Copy(Structures),
        Name = Name ?? "",
        FoundingOwners = FoundingOwners is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingOwners),
        FoundingLieges = FoundingLieges is null ? new SortedDictionary<long, long>() : new SortedDictionary<long, long>(FoundingLieges),
        Routes = Copy(Routes),
        Monsters = Copy(Monsters),
    };

    private static SortedDictionary<long, T> Copy<T>(SortedDictionary<long, T>? source) =>
        source is null ? new SortedDictionary<long, T>() : new SortedDictionary<long, T>(source);

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}
```

Replace `sim/FantasyWorld.Sim/Query/Names.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Query;

/// <summary>Display names for entities, shared by the CLI, briefs and narration.</summary>
public static class Names
{
    public static string Full(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;

    public static Func<EntityRef, string?> Of(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? Full(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        Realm.Kind => world.Realms.TryGetValue(entity.Id, out var realm) ? Statecraft.Title(realm) : null,
        War.Kind => world.Wars.TryGetValue(entity.Id, out var war) ? war.Name : null,
        Army.Kind => world.Armies.TryGetValue(entity.Id, out var army) ? army.Name : null,
        Structure.Kind => world.Structures.TryGetValue(entity.Id, out var structure) ? structure.Name : null,
        TradeRoute.Kind => world.Routes.TryGetValue(entity.Id, out var route) ? route.Name : null,
        Monster.Kind => world.Monsters.TryGetValue(entity.Id, out var monster) ? monster.Name : null,
        Culture.Kind => world.Cultures.TryGetValue(entity.Id, out var culture) ? culture.Name : null,
        Faith.Kind => world.Faiths.TryGetValue(entity.Id, out var faith) ? faith.Name : null,
        _ => null,
    };
}
```

Replace `sim/FantasyWorld.Sim/Systems/DeedsSystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What notables do with their lives (spec §3 "Monthly decisions"). Each month some adults act on
/// their ambition: a decision point offering hunts, delves, duels, study, forging or theft (plus
/// "carry on"). Risky deeds are encounters resolved by the arbiter; outcomes wound, kill, move
/// artifacts, tame regions and build renown. Every deed is an event citing its motive.
/// </summary>
public sealed class DeedsSystem : ISimSystem
{
    public const double ActPerMonth = 0.04;

    /// <summary>Artifacts the world can hold before forging becomes impossible (spec: 20–60 per world).</summary>
    public static int ArtifactCap(World world) => Math.Max(10, world.Regions.Count / 2);

    private readonly IDecider _decider;
    private readonly IArbiter _arbiter;
    private readonly IInventor _inventor;

    public DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null)
    {
        _inventor = inventor ?? new RulesInventor();
        _decider = decider ?? new RulesDecider();
        _arbiter = arbiter ?? new RulesArbiter();
    }

    public string Name => "deeds";

    public void OnMonthlyTick(SimContext ctx)
    {
        var residents = ctx.World.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var c in residents.SelectMany(g => g).OrderBy(c => c.Id).ToList())
        {
            if (!c.IsActive || c.Stage(ctx.Date) == LifeStage.Child) continue;
            if (c.Ambition == Ambitions.Vengeance
                && !(ctx.World.Characters.TryGetValue(c.AmbitionTarget, out var target) && target.IsActive && target.Id != c.Id))
            {
                c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
                c.AmbitionTarget = 0;
            }
            var drive = c.Has(Traits.Diligent) ? 1.5 : c.Has(Traits.Lazy) ? 0.5 : 1.0;
            if (!ctx.Rng.Chance(ActPerMonth * drive)) continue;
            var options = Options(ctx, c, residents[c.SettlementId]);
            if (options.Count <= 1) continue;
            var choice = _decider.Decide(new DecisionBrief(c.Id, c.Ambition, options), ctx.Rng);
            using (ctx.Annotate("motive", choice.Reasoning)) Act(ctx, c, choice.Key);
        }
    }

    /// <summary>The legal deeds for this character now, weighted by ambition and traits. Keys: verb:target.</summary>
    public static List<DecisionOption> Options(SimContext ctx, Character c, IEnumerable<Character>? neighbours = null)
    {
        var world = ctx.World;
        var home = world.Settlements[c.SettlementId];
        var nearby = Nearby(world, home.RegionId).ToList();
        var options = new List<DecisionOption> { new("rest", 30, "carry on as before") };
        var bold = c.Has(Traits.Brave) ? 2.0 : c.Has(Traits.Craven) ? 0.2 : 1.0;

        var beast = world.Monsters.Values.Where(m => m.State == MonsterState.Roaming && nearby.Any(r => r.Id == m.RegionId))
            .OrderByDescending(m => m.Notoriety).ThenBy(m => m.Id).FirstOrDefault();
        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (beast is not null)
            options.Add(new($"hunt:{beast.RegionId}", (c.Ambition == Ambitions.Renown ? 50 : 6) * bold, $"hunt {beast.Name} in {world.Regions[beast.RegionId].Name}"));
        else if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        var lost = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && nearby.Any(r => r.Id == a.RegionId))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (lost is not null)
            options.Add(new($"delve:{lost.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 10 : 2) * bold,
                $"seek {lost.Name} in {world.Regions[lost.RegionId].Name}"));

        if (c.Ambition == Ambitions.Artifact && (c.Has(Traits.Deceitful) || c.Has(Traits.Greedy)))
        {
            var coveted = world.Artifacts.Values
                .Where(a => a.Place == ArtifactPlace.Held && a.HolderId != c.Id && world.Characters[a.HolderId].SettlementId == c.SettlementId)
                .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
            if (coveted is not null) options.Add(new($"steal:{coveted.Id}", 10, $"steal {coveted.Name}"));
        }

        if (c.Ambition == Ambitions.Magic)
        {
            options.Add(new("study", 40, "study the arcane"));
            if (c.Attributes.Lore >= 15 && world.Regions[home.RegionId].LeyPower >= 50) options.Add(new("forge", 20, "forge an artifact"));
        }

        if (c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive && foe.Id != c.Id
            && nearby.Any(r => r.Id == world.Settlements[foe.SettlementId].RegionId))
            options.Add(new($"avenge:{foe.Id}", 50 * bold, $"challenge {foe.Name} to the death"));

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        if (rival is not null && (c.Has(Traits.Wrathful) || c.Has(Traits.Brave)))
            options.Add(new($"duel:{rival.Id}", 25 * bold, $"challenge {rival.Name} to first blood"));
        return options;
    }

    private void Act(SimContext ctx, Character c, string key)
    {
        var parts = key.Split(':');
        var target = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        switch (parts[0])
        {
            case "hunt": Hunt(ctx, c, ctx.World.Regions[target]); break;
            case "delve": Delve(ctx, c, ctx.World.Artifacts[target]); break;
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            case "avenge" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        if (ctx.World.Monsters.Values.Where(m => m.State == MonsterState.Roaming && m.RegionId == region.Id).OrderBy(m => m.Id).FirstOrDefault() is { } beast)
        {
            HuntBeast(ctx, hunter, region, beast);
            return;
        }
        var outcome = _arbiter.Resolve(Encounters.HuntIn(ctx.World, hunter, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonstersSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? 25 : 15), Tags: new[] { "Characters", "Deeds" }));
        if (outcome.AttackerWins)
        {
            region.Danger = Math.Max(0, region.Danger - 15);
            Glory(ctx, hunter, 8);
        }
        Suffer(ctx, hunter, outcome.AttackerFate, "monsters", hunt, lostIn: region.Id);
    }

    /// <summary>
    /// A hunt for a named beast: the arbiter weighs the hunter against its might. Slaying it ends its story (fame, a
    /// safer land, and from a mighty beast a trophy); failing feeds its notoriety.
    /// </summary>
    private void HuntBeast(SimContext ctx, Character hunter, Region region, Monster beast)
    {
        var world = ctx.World;
        var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, hunter, beast), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref, ["monster"] = beast.Ref },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: new[] { new Cause(ctx.Log.Involving(beast.Ref).LastOrDefault()?.Id ?? beast.RoseEventId, CauseKind.Trigger, 0.8) },
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? (int)(30 + beast.Might / 3 + (beast.Legendary ? 15 : 0)) : 18),
            Tags: new[] { "Characters", "Deeds", "Magic" }));
        if (outcome.AttackerWins)
        {
            beast.State = MonsterState.Slain;
            beast.SlainEventId = hunt.Id;
            beast.SlayerId = hunter.Id;
            region.Danger = Math.Max(0, region.Danger - 25);
            Glory(ctx, hunter, 8 + beast.Might / 8);
            if (beast.Might >= 60 && world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < ArtifactCap(world))
            {
                var trophy = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), hunt.Id,
                    fame: 20 + beast.Notoriety / 2);
                world.Artifacts[trophy.Id] = trophy;
                Fates.MoveToHolder(trophy, hunter, hunt.Id, "won");
            }
        }
        else beast.Notoriety = Math.Min(100, beast.Notoriety + 8);
        Suffer(ctx, hunter, outcome.AttackerFate, beast.Name, hunt, lostIn: region.Id);
    }

    private void Delve(SimContext ctx, Character delver, Artifact artifact)
    {
        var region = ctx.World.Regions[artifact.RegionId];
        var outcome = _arbiter.Resolve(Encounters.DelveIn(ctx.World, delver, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var delve = ctx.Emit(new EventDraft(outcome.AttackerWins ? "ArtifactFound" : "DelveFailed",
            Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = artifact.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: artifact.Provenance.Count > 0 ? new[] { new Cause(artifact.Provenance[^1].EventId, CauseKind.Enabler, 0.5) } : null,
            Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(20 + artifact.Fame / 3) : 15),
            Tags: new[] { "Characters", "Deeds", "Artifacts" }));
        if (outcome.AttackerWins)
        {
            Fates.MoveToHolder(artifact, delver, delve.Id, "found");
            artifact.Fame = Math.Min(100, artifact.Fame + 5);
            Glory(ctx, delver, 10);
        }
        Suffer(ctx, delver, outcome.AttackerFate, "a labyrinth's guardians", delve, lostIn: region.Id);
    }

    private static void Steal(SimContext ctx, Character thief, Artifact artifact)
    {
        var victim = ctx.World.Characters[artifact.HolderId];
        var guard = victim.Attributes.Intrigue + artifact.Bonus("intrigue") + 5 + (victim.Has(Traits.Paranoid) ? 5 : 0);
        var success = ctx.Rng.Chance(thief.Attributes.Intrigue / (double)(thief.Attributes.Intrigue + guard));
        var ev = ctx.Emit(new EventDraft(success ? "ArtifactStolen" : "TheftFoiled",
            Actors: new Dictionary<string, EntityRef> { ["thief"] = thief.Ref, ["victim"] = victim.Ref, ["artifact"] = artifact.Ref },
            Place: ctx.World.Settlements[thief.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(victim, 15), (int)(10 + artifact.Fame / 3)), Tags: new[] { "Characters", "Artifacts" }));
        Fates.Remember(victim, ev.Id, thief.Id, success ? -80 : -50, 80);
        if (success)
        {
            Fates.MoveToHolder(artifact, thief, ev.Id, "stolen");
            SwearVengeance(ctx, victim, thief);
        }
        else Shame(thief);
    }

    private void Forge(SimContext ctx, Character mage)
    {
        var world = ctx.World;
        var region = world.Regions[world.Settlements[mage.SettlementId].RegionId];
        var room = 1 - world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) / (double)ArtifactCap(world);
        if (!ctx.Rng.Chance(Math.Max(0, room) * (mage.Attributes.Lore - 12) / 30.0)) return;
        var artifactRef = new EntityRef(Artifact.Kind, world.Ids.Peek(Artifact.Kind));
        var forged = ctx.Emit(new EventDraft("ArtifactForged",
            Actors: new Dictionary<string, EntityRef> { ["maker"] = mage.Ref, ["artifact"] = artifactRef },
            Place: world.Settlements[mage.SettlementId].Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(mage, 35), Tags: new[] { "Characters", "Artifacts" }));
        var artifact = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(mage.CultureId)?.Style),
            forged.Id, rareMaterials: region.Resources.Keys.Where(ResourceKinds.IsRare).ToList());
        var powers = string.Join(", ", artifact.Powers.Select(p => p.Attribute is null ? p.Kind : $"+{p.Amount} {p.Attribute}"));
        var invention = _inventor.Invent(new InventionBrief("artifact", artifact.Ref.ToString(),
            $"A {artifact.Type.ToString().ToLowerInvariant()} of {artifact.Material}, granting {powers}{(artifact.Curse is null ? "" : $", bearing a hidden curse of {artifact.Curse}")}; "
            + $"forged by {mage.Name}, a {world.Cultures.GetValueOrDefault(mage.CultureId)?.Name ?? "nameless"} mage, at {world.Settlements[mage.SettlementId].Name} in {region.Name}.",
            artifact.Name));
        (artifact.Name, artifact.Lore) = (invention.Name, invention.Lore ?? "");
        world.Artifacts[artifact.Id] = artifact;
        Fates.MoveToHolder(artifact, mage, forged.Id, "forged");
        Glory(ctx, mage, 10);
    }

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var duel = ctx.Emit(new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key },
            Causes: grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : null,
            Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 30 : 15), Fates.Importance(defender, toTheDeath ? 30 : 15)),
            Tags: new[] { "Characters", "Deeds" }));
        var (winner, loser) = outcome.AttackerWins ? (challenger, defender) : (defender, challenger);
        Glory(ctx, winner, toTheDeath ? 8 : 4);
        Fates.Remember(loser, duel.Id, winner.Id, -60, 60);
        if (!toTheDeath && ctx.Rng.Chance(0.5)) Shame(loser);
        if (!toTheDeath && loser.Id != winner.Id) SwearVengeance(ctx, loser, winner);
        if (toTheDeath && outcome.AttackerWins && challenger.Ambition == Ambitions.Vengeance)
            challenger.Ambition = CharacterFactory.ChooseAmbition(challenger, ctx.Rng);
        Suffer(ctx, challenger, outcome.AttackerFate, "a duel", duel, outcome.AttackerFate == Fate.Dead ? defender : null);
        Suffer(ctx, defender, outcome.DefenderFate, "a duel", duel, outcome.DefenderFate == Fate.Dead ? challenger : null);
    }

    private static void Suffer(SimContext ctx, Character c, Fate fate, string cause, WorldEvent deed,
        Character? killer = null, long lostIn = 0)
    {
        if (fate == Fate.Dead)
        {
            if (c.IsAlive)
                Fates.Die(ctx, c, cause, new[] { new Cause(deed.Id, CauseKind.Trigger, 1.0) },
                    killer is { IsAlive: true } ? killer : null, lostIn);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
    }

    /// <summary>Renown for the deed; the artifacts they carry grow famous with them.</summary>
    private static void Glory(SimContext ctx, Character c, double renown)
    {
        c.Renown = Math.Min(100, c.Renown + renown);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
        foreach (var a in ctx.World.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id))
            a.Fame = Math.Min(100, a.Fame + renown / 2);
    }

    /// <summary>The wrathful and the cruel do not forgive a humiliation or a theft: half of them swear vengeance.</summary>
    private static void SwearVengeance(SimContext ctx, Character wronged, Character wrongdoer)
    {
        if (wronged.Has(Traits.Craven) || !(wronged.Has(Traits.Wrathful) || wronged.Has(Traits.Cruel))) return;
        if (wronged.Ambition == Ambitions.Vengeance || !ctx.Rng.Chance(0.5)) return;
        wronged.Ambition = Ambitions.Vengeance;
        wronged.AmbitionTarget = wrongdoer.Id;
    }

    private static void Shame(Character c)
    {
        if (c.Has(Traits.Humiliated)) return;
        c.Traits.Add(Traits.Humiliated);
        c.Traits.Sort(StringComparer.Ordinal);
    }

    private static IEnumerable<Region> Nearby(World world, long regionId) =>
        new[] { regionId }.Concat(world.Geography?.Graph.Neighbours(regionId) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]);
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new MonsterSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

`sim/FantasyWorld.Sim/Systems/MonsterSystem.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Monsters (sub-project 3 §3). Each year a named beast may rise in a dangerous land (more often after a ley surge),
/// of a breed that fits the land, mightier where the land is wilder; beasts grow with age, and very old ones may
/// slumber until danger or a surge wakes them. Each month a roaming beast may raid a settlement in or next to its
/// land: people, food and wealth are lost, unrest rises, and the beast's notoriety grows until it becomes a legend.
/// Heroes answer them (hunts in <see cref="DeedsSystem"/>).
/// </summary>
public sealed class MonsterSystem(IInventor? inventor = null) : ISimSystem
{
    public const double RiseDanger = 55;
    public const double RisePerYear = 0.05;
    public const double RaidPerMonth = 0.03;
    public const double LegendNotoriety = 60;
    public const int SlumberAgeYears = 120;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "monsters";

    public static int Cap(World world) => Math.Max(1, world.Regions.Count / 8);

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.State == MonsterState.Roaming).ToList())
        {
            var lair = world.Regions[beast.RegionId];
            lair.Danger = Math.Max(lair.Danger, beast.Might / 2);
            if (!ctx.Rng.Chance(RaidPerMonth * (0.5 + beast.Might / 100))) continue;
            Raid(ctx, beast, lair);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.Alive).ToList())
        {
            var age = (ctx.Date.Day - beast.BornDay) / WorldDateYears;
            if (beast.State == MonsterState.Roaming)
            {
                beast.Might = Math.Min(100, beast.Might + (age < 60 ? 1 : 0));
                if (age >= SlumberAgeYears && ctx.Rng.Chance(0.1))
                {
                    beast.State = MonsterState.Slumbering;
                    ctx.Emit(new EventDraft("MonsterSlumbered", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                        Place: world.Regions[beast.RegionId].Ref, Importance: 25, Tags: new[] { "Magic" }));
                }
            }
            else if (world.Regions[beast.RegionId].Conditions.TryGetValue(Conditions.LeySurge, out var surge) && ctx.Rng.Chance(0.3))
            {
                beast.State = MonsterState.Roaming;
                ctx.Emit(new EventDraft("MonsterWoke", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                    Place: world.Regions[beast.RegionId].Ref, Causes: new[] { new Cause(surge, CauseKind.Trigger, 0.9) },
                    Importance: 45, Tags: new[] { "Magic" }));
            }
        }
        Rise(ctx);
    }

    private const double WorldDateYears = Core.WorldDate.DaysPerYear;

    private void Rise(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Monsters.Values.Count(m => m.Alive) >= Cap(world)) return;
        var haunted = world.Monsters.Values.Where(m => m.Alive).Select(m => m.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.Danger >= RiseDanger && !haunted.Contains(r.Id)).ToList())
        {
            var surge = region.Conditions.TryGetValue(Conditions.LeySurge, out var surgeEvent);
            if (!ctx.Rng.Chance(RisePerYear * (surge ? 3 : 1) * region.Danger / 100)) continue;
            var breeds = Monster.BreedsOf(region.Biome);
            var breed = breeds[ctx.Rng.NextInt(0, breeds.Count)];
            var id = world.Ids.Take(Monster.Kind);
            var invention = _inventor.Invent(new InventionBrief("monster", $"{Monster.Kind}#{id}",
                $"A {breed} that rose in the wild land of {region.Name} ({region.Biome}) in year {ctx.Date.Year}.",
                $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var rose = ctx.Emit(new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["breed"] = breed },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.8), (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
                Importance: 40, Tags: new[] { "Magic" }));
            world.Monsters[id] = new Monster
            {
                Id = id, Name = invention.Name, Breed = breed, RegionId = region.Id, Might = Math.Clamp(20 + region.Danger / 2 + ctx.Rng.NextInt(0, 21), 1, 100),
                BornDay = ctx.Date.Day, State = MonsterState.Roaming, RoseEventId = rose.Id,
            };
            return; // at most one a year
        }
    }

    private static void Raid(SimContext ctx, Monster beast, Region lair)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        var nearby = new[] { lair.Id }.Concat(graph?.Neighbours(lair.Id) ?? Array.Empty<long>()).ToHashSet();
        var target = world.Settlements.Values.Where(s => !s.Abandoned && nearby.Contains(s.RegionId))
            .OrderBy(s => s.RegionId == lair.Id ? 0 : 1).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
        if (target is null) return;
        var share = beast.Might / 1000.0; // up to a tenth of the people
        var dead = (int)(target.Adults * share);
        target.Adults -= dead;
        world.Regions[target.RegionId].Food *= 1 - share;
        target.Wealth *= 1 - share;
        target.Unrest = Math.Min(100, target.Unrest + 15);
        beast.Notoriety = Math.Min(100, beast.Notoriety + 5 + beast.Might / 10);
        var previous = ctx.Log.Involving(beast.Ref).LastOrDefault();
        var raid = ctx.Emit(new EventDraft("MonsterRaid", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
            Place: target.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: previous is null ? null : new[] { new Cause(previous.Id, CauseKind.Enabler, 0.5) },
            Importance: (int)Math.Clamp(25 + beast.Might / 3, 25, 60), Tags: new[] { "Magic" }));
        // Merchants shun a land a beast has raided (the spec's raid that severs a road).
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open && r.Regions.Contains(target.RegionId)).ToList())
            TradeSystem.Sever(ctx, route, "raid", target.Ref, raid.Id);
        if (!beast.Legendary && beast.Notoriety >= LegendNotoriety)
        {
            beast.Legendary = true;
            ctx.Emit(new EventDraft("MonsterLegendary", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                Place: lair.Ref, Causes: new[] { new Cause(beast.RoseEventId, CauseKind.Enabler, 0.6) }, Importance: 60, Tags: new[] { "Magic" }));
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/TradeSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Trade routes (sub-project 3 §8). Towns and cities are markets; each year a few pairs within a journey's reach open
/// a road along the region graph when their realms are at peace with each other and the way is safe. A road carries
/// wealth both ways each year in proportion to the smaller market. Each month, a road that a war cuts (an occupied
/// region on the way, or its markets' realms at war with each other) or that grows too dangerous is severed, and it
/// reopens once the way is clear again; a road whose market is abandoned or shrinks below a town closes for good.
/// </summary>
public sealed class TradeSystem : ISimSystem
{
    public const double MaxTravelDays = 40;
    public const double OpenChance = 0.12;
    public const int MaxNewPerYear = 3;
    public const int MaxPerMarket = 3;
    public const double DangerLimit = 60;
    public const double FlowShare = 0.03;

    public string Name => "trade";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var route in world.Routes.Values.Where(r => r.State != RouteState.Closed).ToList())
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            if (!IsMarket(from) || !IsMarket(to))
            {
                // A market abandoned, or shrunk below a town, ends the road for good.
                route.State = RouteState.Closed;
                route.Volume = 0;
                var gone = !IsMarket(from) ? from : to;
                var why = ctx.Log.Involving(gone.Ref).LastOrDefault(e => e.Type is "SettlementAbandoned" or "SettlementDeclined");
                ctx.Emit(new EventDraft("RouteClosed", Actors: Ends(route, from, to), Place: gone.Ref,
                    Causes: why is null ? null : new[] { new Cause(why.Id, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
                continue;
            }
            var blocked = Blocked(ctx, route);
            if (route.State == RouteState.Open && blocked is { } cut)
                Sever(ctx, route, cut.Reason, cut.Place ?? from.Ref, cut.Cause);
            else if (route.State == RouteState.Severed && blocked is null && !RaidedLately(ctx, route))
            {
                route.State = RouteState.Open;
                // Recorded at a weight compaction keeps, so past maps see the road reopen.
                ctx.Emit(new EventDraft("RouteRestored", Actors: Ends(route, from, to),
                    Place: from.Ref, Causes: new[] { new Cause(route.SeveredEventId, CauseKind.Enabler, 0.5) },
                    Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
            }
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open))
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            route.Volume = FlowShare * Math.Min(from.Wealth, to.Wealth);
            from.Wealth += route.Volume;
            to.Wealth += route.Volume;
        }
        Open(ctx);
    }

    /// <summary>A road a beast's raid cut stays shunned this long.</summary>
    public const int RaidDays = 180;

    private static bool RaidedLately(SimContext ctx, TradeRoute route) =>
        ctx.Log.TryGet(route.SeveredEventId, out var cut) && cut.Payload.GetValueOrDefault("reason") == "raid" && ctx.Date.Day - cut.Date.Day < RaidDays;

    /// <summary>Cuts an open road, citing what cut it.</summary>
    public static void Sever(SimContext ctx, TradeRoute route, string reason, EntityRef place, long? cause)
    {
        var world = ctx.World;
        route.State = RouteState.Severed;
        route.Volume = 0;
        route.SeveredEventId = ctx.Emit(new EventDraft("RouteSevered", Actors: Ends(route, world.Settlements[route.FromId], world.Settlements[route.ToId]),
            Place: place, Payload: new Dictionary<string, string> { ["reason"] = reason },
            Causes: cause is { } c ? new[] { new Cause(c, CauseKind.Trigger, 0.9) } : null,
            Importance: 30, Tags: new[] { "Economy" })).Id;
    }

    private static Dictionary<string, EntityRef> Ends(TradeRoute route, Settlement from, Settlement to) =>
        new() { ["route"] = route.Ref, ["from"] = from.Ref, ["to"] = to.Ref };

    public static bool IsMarket(Settlement s) => !s.Abandoned && s.Tier >= SettlementTier.Town;

    private static void Open(SimContext ctx)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        if (graph is null) return;
        var markets = world.Settlements.Values.Where(IsMarket).ToList();
        int Roads(long id) => world.Routes.Values.Count(r => r.State != RouteState.Closed && (r.FromId == id || r.ToId == id));
        var linked = world.Routes.Values.Where(r => r.State != RouteState.Closed).Select(r => (r.FromId, r.ToId)).ToHashSet();
        var opened = 0;
        for (var i = 0; i < markets.Count && opened < MaxNewPerYear; i++)
            for (var j = i + 1; j < markets.Count && opened < MaxNewPerYear; j++)
            {
                var (a, b) = (markets[i], markets[j]);
                if (a.RegionId == b.RegionId || linked.Contains((a.Id, b.Id)) || Roads(a.Id) >= MaxPerMarket || Roads(b.Id) >= MaxPerMarket) continue;
                if (graph.ShortestPath(a.RegionId, b.RegionId) is not { } way || way.Days > MaxTravelDays) continue;
                if (AtWar(world, a, b) || way.Path.Any(r => world.Regions[r].OccupierRealmId != 0 || world.Regions[r].Danger >= DangerLimit)) continue;
                if (!ctx.Rng.Chance(OpenChance)) continue;
                var id = world.Ids.Take(TradeRoute.Kind);
                var road = ctx.Emit(new EventDraft("RouteOpened",
                    Actors: new Dictionary<string, EntityRef> { ["route"] = new(TradeRoute.Kind, id), ["from"] = a.Ref, ["to"] = b.Ref },
                    Place: a.Ref, Importance: 25, Tags: new[] { "Economy" }));
                var route = new TradeRoute
                {
                    Id = id, Name = $"The {a.Name}–{b.Name} Road", FromId = a.Id, ToId = b.Id,
                    Regions = way.Path.ToList(), State = RouteState.Open, OpenedEventId = road.Id,
                };
                world.Routes[route.Id] = route;
                linked.Add((a.Id, b.Id));
                opened++;
            }
    }

    /// <summary>Why a road cannot be used now (and the event to blame), or null when the way is clear.</summary>
    private static (string Reason, EntityRef? Place, long? Cause)? Blocked(SimContext ctx, TradeRoute route)
    {
        var world = ctx.World;
        var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
        if (AtWar(world, from, to))
        {
            var war = world.Wars.Values.Where(w => !w.Ended).FirstOrDefault(w => Opposed(w, Realms.SovereignOf(world, from)?.Id, Realms.SovereignOf(world, to)?.Id));
            return ("war", null, war?.DeclaredEventId);
        }
        foreach (var id in route.Regions)
        {
            var region = world.Regions[id];
            if (region.OccupierRealmId != 0)
                return ("occupation", region.Ref, ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type is "RegionOccupied" or "SiegeEnded")?.Id);
            if (region.Danger >= DangerLimit)
                return ("danger", region.Ref, region.Conditions.TryGetValue(Conditions.Dangerous, out var danger) ? danger : null);
        }
        return null;
    }

    private static bool AtWar(World world, Settlement a, Settlement b)
    {
        var (x, y) = (Realms.SovereignOf(world, a)?.Id, Realms.SovereignOf(world, b)?.Id);
        return x is not null && y is not null && x != y && world.Wars.Values.Any(w => !w.Ended && Opposed(w, x, y));
    }

    private static bool Opposed(War w, long? a, long? b) =>
        a is { } x && b is { } y && (w.Attackers.Contains(x) && w.Defenders.Contains(y) || w.Attackers.Contains(y) && w.Defenders.Contains(x));
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~MonsterSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`. Then `dotnet test sim` → `Passed: 523` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (70)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): raise named beasts that raid, grow into legends and are hunted by name

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Beasts on the map

Spec §9: lairs get a beast marker. `MapScreen` lists the beasts that had risen and were not yet slain at the date; the diorama stands a dark spiked token with glowing eyes in each lair, grey while it slumbers, with a turning gold ring for a legend. Clicking a beast explains its rising.

**Files:**
- Modify: `game/scripts/Diorama.cs`, `game/scripts/Main.cs`, `game/tests/HeadlessChecks.cs`, `sim/FantasyWorld.Sim/Views/MapScreen.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`

**Interfaces:**
- Consumes: Task 3; `Diorama`, `Main.Picked` (sub-project 4).
- Produces: `record MapMonster(Ref, Name, Breed, Might, Legendary, Slumbering, Position, RoseEventId)`; `MapScreen.Monsters`; `Diorama.Monsters` and beast nodes named `monster<id>` on the structure layer.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, one open labyrinth.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      }
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      }
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      }
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      }
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      }
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      }
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      }
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      }
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      }
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      }
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      }
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      }
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      }
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      }
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      }
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      }
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      }
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      }
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      }
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      }
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      }
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      }
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      }
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      }
    }
  ],
  "structures": [
    {
      "ref": "structure#1",
      "name": "The Deeps of Taramdale",
      "kind": "Labyrinth",
      "state": "Standing",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      }
    }
  ],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ],
  "armies": [],
  "battles": [],
  "routes": [
    {
      "ref": "route#1",
      "name": "The Greindrondcross–Niar Road",
      "from": "settlement#16",
      "to": "settlement#24",
      "state": "open",
      "volume": 76,
      "path": [
        {
          "x": 0.8828125,
          "y": 0.4296875
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    },
    {
      "ref": "route#2",
      "name": "The Drerithby–Greindrondcross Road",
      "from": "settlement#3",
      "to": "settlement#16",
      "state": "open",
      "volume": 106,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.4921875,
          "y": 0.4296875
        },
        {
          "x": 0.8828125,
          "y": 0.4296875
        }
      ]
    },
    {
      "ref": "route#3",
      "name": "The Drerithby–Niar Road",
      "from": "settlement#3",
      "to": "settlement#24",
      "state": "open",
      "volume": 78,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    }
  ],
  "monsters": []
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 1 failing.

- [x] **Step 3: Implement**

Replace `game/scripts/Diorama.cs` entirely with:
```csharp
using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The floating world block (sub-project 4 §3): terrain, sea, strata walls, markers, realm borders and digest pins,
/// seen through the orbit camera. Geography is built once; markers and borders are rebuilt when the map changes.
/// Drag to orbit, scroll to descend; clicking near a marker raises <see cref="Picked"/>.
/// </summary>
public partial class Diorama : Node3D
{
    public const float PickRadius = 16f;
    public const float FrameOffset = 0.08f;

    private Geography? _geography;
    private bool _dragging;
    private Vector2 _pressedAt;
    private readonly List<Marker> _markers = new();
    private readonly List<Pin> _pins = new();

    public OrbitRig Rig { get; } = new();
    public Camera3D Camera { get; private set; } = null!;
    public MeshInstance3D Block { get; private set; } = null!;
    public Node3D MarkerLayer { get; private set; } = null!;
    public Node3D SettlementLayer { get; private set; } = null!;
    public Node3D StructureLayer { get; private set; } = null!;
    public MapLayers Layers { get; private set; } = MapLayers.Default;
    public Node3D PinLayer { get; private set; } = null!;
    public WarLayer War { get; private set; } = null!;
    public MeshInstance3D BorderLines { get; private set; } = null!;
    public MeshInstance3D RoadLines { get; private set; } = null!;
    public IReadOnlyList<Road> Roads { get; private set; } = Array.Empty<Road>();
    public IReadOnlyList<MapMonster> Monsters { get; private set; } = Array.Empty<MapMonster>();
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary>A marker or pin was clicked.</summary>
    public event Action<EntityRef>? Picked;

    public override void _Ready()
    {
        Name = "Diorama";
        Camera = new Camera3D { Name = "Camera", Fov = 32, Near = 0.01f, Far = 50 };
        AddChild(Camera);
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 0.55f, ShadowEnabled = true });
        // A soft fill from the viewer's side keeps the strata walls readable.
        AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-15, 35, 0), LightEnergy = 0.25f });
        AddChild(new DirectionalLight3D { Name = "Back", RotationDegrees = new Vector3(-20, 215, 0), LightEnergy = 0.2f });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.C(Palette.Ground),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f,
            },
        });
        Block = new MeshInstance3D { Name = "Block" };
        AddChild(Block);
        BorderLines = new MeshInstance3D { Name = "Borders" };
        AddChild(BorderLines);
        RoadLines = new MeshInstance3D { Name = "Roads" };
        AddChild(RoadLines);
        MarkerLayer = new Node3D { Name = "Markers" };
        AddChild(MarkerLayer);
        SettlementLayer = new Node3D { Name = "Settlements" };
        StructureLayer = new Node3D { Name = "Structures" };
        MarkerLayer.AddChild(SettlementLayer);
        MarkerLayer.AddChild(StructureLayer);
        PinLayer = new Node3D { Name = "Pins" };
        AddChild(PinLayer);
        War = new WarLayer();
        AddChild(War);
        UpdateCamera();
    }

    /// <summary>
    /// Builds the terrain block, its walls, the sea, the woods and the block's shadow (once per world). Woods leave
    /// clearings around the given points (today's settlements).
    /// </summary>
    public void Build(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null) =>
        Build(geography, Prepare(geography, clearings, resolution));

    /// <summary>The block's geometry and woods, computed without Godot so it can run on a worker thread.</summary>
    public sealed record Parts(MeshData Top, MeshData Sides, MeshData Water, IReadOnlyList<Client.Tree> Trees);

    public static Parts Prepare(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null)
    {
        var grid = geography.Terrain;
        // About one and a half vertices per tile: smooth on small worlds, detailed on large ones.
        var detail = resolution ?? Math.Clamp(grid.Width * 3 / 2, 192, 384);
        return new Parts(BlockMesh.Top(grid, detail), BlockMesh.Sides(grid, detail), BlockMesh.Water(grid), Scatter.Trees(grid, clearings));
    }

    public void Build(Geography geography, Parts parts)
    {
        _geography = geography;
        var mesh = new ArrayMesh();
        AddSurface(mesh, parts.Top, VertexColours());
        AddSurface(mesh, parts.Sides, VertexColours());
        Block.Mesh = mesh;

        var water = new ArrayMesh();
        AddSurface(water, parts.Water, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.18f,
            MetallicSpecular = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        Child<MeshInstance3D>("Water").Mesh = water;

        var trees = parts.Trees;
        Child<MultiMeshInstance3D>("Broadleaves").Multimesh = Instances(
            new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 },
            trees.Where(t => t.Kind == TreeKind.Broadleaf).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.55f, 0), new Vector3(t.Height * 0.95f, t.Height, t.Height * 0.95f), 0), t.Colour)));
        Child<MultiMeshInstance3D>("Conifers").Multimesh = Instances(
            new CylinderMesh { TopRadius = 0, BottomRadius = 0.36f, Height = 1f, RadialSegments = 7, Rings = 1 },
            trees.Where(t => t.Kind == TreeKind.Conifer).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.5f, 0), new Vector3(t.Height, t.Height, t.Height), 0), t.Colour)));
        foreach (var name in new[] { "Broadleaves", "Conifers" }) Child<MultiMeshInstance3D>(name).MaterialOverride = Foliage();

        // A soft shadow on the ground beneath the floating block.
        Child<MeshInstance3D>("Shadow").Mesh = new PlaneMesh { Size = new Vector2(3.1f, 3.1f) };
        var shadow = Child<MeshInstance3D>("Shadow");
        shadow.Position = new Vector3(0.06f, -BlockMesh.BaseDepth - 0.12f, 0.06f);
        shadow.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = SoftShadow(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 1),
        };
    }

    public int TreeCount => Child<MultiMeshInstance3D>("Broadleaves").Multimesh?.InstanceCount + Child<MultiMeshInstance3D>("Conifers").Multimesh?.InstanceCount ?? 0;

    /// <summary>Places settlements, structures and borders for a map: today's, or the given past year's.</summary>
    public void Show(MapScreen map, int? pastYear = null)
    {
        if (_geography is null) return;
        Ui.Clear(SettlementLayer);
        Ui.Clear(StructureLayer);
        _markers.Clear();
        _markers.AddRange(FantasyWorld.Client.Markers.For(map, _geography));
        var houses = _markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, _geography.Terrain)).ToList();
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Walls", MaterialOverride = Painted(),
            Multimesh = Instances(new BoxMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Size * (h.Tower ? 0.75f : 0.35f), 0), h.Tower ? new Vector3(h.Size, h.Size * 1.5f, h.Size) : new Vector3(h.Size, h.Size * 0.7f, h.Size * 0.8f), h.Yaw),
                h.Tower ? Stone : Plaster))),
        });
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Roofs", MaterialOverride = Painted(),
            Multimesh = Instances(new PrismMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Tower ? h.Size * 1.85f : h.Size * 0.95f, 0), h.Tower ? new Vector3(h.Size * 1.15f, h.Size * 0.75f, h.Size * 1.15f) : new Vector3(h.Size * 1.1f, h.Size * 0.5f, h.Size * 0.9f), h.Yaw),
                Roof(h.Roof)))),
        });
        foreach (var marker in _markers.Where(m => m.Shape != MarkerShape.Settlement)) StructureLayer.AddChild(MarkerNode(marker));
        Monsters = map.Monsters ?? Array.Empty<MapMonster>();
        foreach (var beast in Monsters) StructureLayer.AddChild(BeastNode(beast, _geography));
        BorderLines.Mesh = DashedRibbons(FantasyWorld.Client.Markers.BorderLines(map, _geography), Palette.Ink);
        War.Show(map, _geography);
        Roads = RouteLayout.Roads(map, _geography);
        // Open roads are close-dotted in road brown; severed ones broken and reddened.
        var roads = new ArrayMesh();
        foreach (var (open, dash, gap, colour) in new[] { (true, 0.006f, 0.005f, BlockMesh.Hex(0x7a5a35)), (false, 0.004f, 0.012f, BlockMesh.Hex(0xa8483a)) })
        {
            var lines = Roads.Where(r => r.Open == open).Select(r => r.Line).ToList();
            if (lines.Count == 0) continue;
            var part = DashedRibbons(lines, colour, dash, gap, width: 0.0019f, halo: 0.0036f);
            for (var s = 0; s < part.GetSurfaceCount(); s++)
            {
                roads.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, part.SurfaceGetArrays(s));
                roads.SurfaceSetMaterial(roads.GetSurfaceCount() - 1, part.SurfaceGetMaterial(s));
            }
        }
        RoadLines.Mesh = roads;
        ShownYear = pastYear;
    }

    /// <summary>The past year the markers show, or null for today.</summary>
    public int? ShownYear { get; private set; }

    /// <summary>Shows or hides the borders, settlements and structures (news tags are the screen's).</summary>
    public void SetLayers(MapLayers layers)
    {
        Layers = layers;
        BorderLines.Visible = layers.HasFlag(MapLayers.Borders);
        SettlementLayer.Visible = layers.HasFlag(MapLayers.Settlements);
        StructureLayer.Visible = layers.HasFlag(MapLayers.Structures);
        War.Visible = layers.HasFlag(MapLayers.Armies);
        RoadLines.Visible = layers.HasFlag(MapLayers.TradeRoutes);
    }

    /// <summary>The digest's pins as stems over their places (the screen draws their numbered labels).</summary>
    public void ShowPins(IEnumerable<Pin> pins)
    {
        Ui.Clear(PinLayer);
        _pins.Clear();
        _pins.AddRange(pins);
        foreach (var pin in _pins)
        {
            var node = new Node3D { Name = $"Pin{pin.Number}", Position = ToGodot(pin.Position) };
            node.AddChild(new MeshInstance3D
            {
                Name = "Stem", Position = new Vector3(0, -Client.Markers.PinLift / 2, 0),
                Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = Client.Markers.PinLift },
                MaterialOverride = Flat(Palette.Ink),
            });
            node.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f }, MaterialOverride = Flat(Palette.Ink) });
            PinLayer.AddChild(node);
        }
    }

    /// <summary>Eases the camera to a place on the map.</summary>
    public void Focus(MapPoint point, Zoom? zoom = null)
    {
        if (_geography is null) return;
        var at = Client.Projection.ToBlock(_geography.Terrain, point, 0);
        Rig.Focus(at, zoom);
    }

    /// <summary>The marker or pin drawn nearest a screen point, within <see cref="PickRadius"/> pixels.</summary>
    public EntityRef? Pick(Vector2 screen)
    {
        // Banners pick as their host, battle marks as the battle (an "event" ref); both above the settlements they stand on.
        var war = !War.Visible ? Enumerable.Empty<(EntityRef, Rgb)>()
            : War.Armies.Select(a => (a.Ref, War.BannerAt(a.Ref) is { } b ? new Rgb(b.X, b.Y + WarLayer.BannerHeight * 0.7f, b.Z) : Rgb.Zero))
                .Concat(War.RecentBattles.Select(b => (new EntityRef("event", b.EventId), Client.Projection.ToBlock(_geography!.Terrain, b.Position, 0.018f))));
        var beasts = !StructureLayer.Visible ? Enumerable.Empty<(EntityRef, Rgb)>()
            : Monsters.Select(m => (m.Ref, Client.Projection.ToBlock(_geography!.Terrain, m.Position, 0.02f)));
        var candidates = war.Concat(beasts).Concat(_pins.Select(p => (p.Place, p.Position))).Concat(_markers
            .Where(m => m.Shape == MarkerShape.Settlement ? SettlementLayer.Visible : StructureLayer.Visible).Select(m => (m.Ref, m.Position)));
        EntityRef? best = null;
        var bestDistance = PickRadius;
        foreach (var (entity, position) in candidates)
        {
            var world = ToGodot(position);
            if (Camera.IsPositionBehind(world)) continue;
            var distance = Camera.UnprojectPosition(world).DistanceTo(screen);
            if (distance < bestDistance) (best, bestDistance) = (entity, distance);
        }
        return best;
    }

    /// <summary>Where a block-space point appears on screen.</summary>
    public Vector2 ScreenOf(Rgb position) => Camera.UnprojectPosition(ToGodot(position));

    public override void _Process(double delta)
    {
        Rig.Update((float)delta);
        UpdateCamera();
        // Beasts, like banners, keep a readable size at any zoom.
        foreach (var node in StructureLayer.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().StartsWith(Monster.Kind)))
            node.Scale = Vector3.One * WarLayer.ScaleFor(Camera.GlobalPosition.DistanceTo(node.GlobalPosition));
    }

    public override void _UnhandledInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) (_dragging, _pressedAt) = (true, button.Position);
                else
                {
                    _dragging = false;
                    if (button.Position.DistanceTo(_pressedAt) < 4 && Pick(button.Position) is { } picked) Picked?.Invoke(picked);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Rig.Scroll(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Rig.Scroll(-1);
                break;
            case InputEventMouseMotion motion when _dragging:
                Rig.Drag(motion.Relative.X, motion.Relative.Y);
                break;
        }
    }

    private void UpdateCamera()
    {
        if (Camera is null) return;
        var eye = Rig.Eye;
        // Never inside a mountain: keep the eye a little above the ground beneath it.
        if (_geography is not null && Math.Abs(eye.X) <= 1 && Math.Abs(eye.Z) <= 1)
            eye.Y = Math.Max(eye.Y, Math.Max(0, BlockMesh.HeightAt(_geography.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)) + 0.04f);
        Camera.Position = ToGodot(eye);
        Camera.LookAt(ToGodot(Rig.Target), Vector3.Up);
        // The screens' cards sit on the right, so the world is framed slightly left of centre.
        Camera.HOffset = Rig.Distance * FrameOffset;
    }

    private static readonly Rgb Plaster = BlockMesh.Hex(0xf1ebde);
    private static readonly Rgb Stone = BlockMesh.Hex(0xb9b1a2);

    /// <summary>Roofs take the holder's colour, softened toward terracotta so heraldry reads without shouting.</summary>
    private static Rgb Roof(Rgb heraldic)
    {
        var roof = Rgb.Lerp(heraldic, BlockMesh.Hex(0xb5654a), 0.3f);
        // Keep roofs mid-toned: sable (black) and argent (white) houses would read as holes and glare.
        var luma = 0.3f * roof.X + 0.59f * roof.Y + 0.11f * roof.Z;
        return Rgb.Clamp(roof * (Math.Clamp(luma, 0.32f, 0.62f) / Math.Max(0.01f, luma)), Rgb.Zero, Rgb.One);
    }

    /// <summary>A beast in its lair: a dark spiked token with glowing eyes; legends wear a turning gold ring; sleepers are grey.</summary>
    private static Node3D BeastNode(MapMonster beast, Geography geography)
    {
        var node = new Node3D
        {
            Name = $"monster{beast.Ref.Id}",
            Position = ToGodot(Client.Projection.ToBlock(geography.Terrain, beast.Position, 0)) + new Vector3(-0.03f, 0, 0.02f),
        };
        var size = 0.02f + 0.02f * beast.Might / 100f;
        var hide = new StandardMaterial3D { AlbedoColor = Ui.C(beast.Slumbering ? BlockMesh.Hex(0x77736b) : BlockMesh.Hex(0x2b2230)), Roughness = 0.7f };
        node.AddChild(new MeshInstance3D
        {
            Name = "Body", Position = new Vector3(0, size * 0.35f, 0),
            Mesh = new SphereMesh { Radius = size * 0.45f, Height = size * 0.7f, RadialSegments = 10, Rings = 5 }, MaterialOverride = hide,
        });
        for (var k = 0; k < 3; k++)
        {
            var angle = k * Mathf.Tau / 3;
            node.AddChild(new MeshInstance3D
            {
                Name = $"Spike{k}", Position = new Vector3(Mathf.Cos(angle) * size * 0.22f, size * 0.85f, Mathf.Sin(angle) * size * 0.22f),
                Rotation = new Vector3(Mathf.Sin(angle) * 0.4f, 0, -Mathf.Cos(angle) * 0.4f),
                Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = size * 0.12f, Height = size * 0.5f, RadialSegments = 5 }, MaterialOverride = hide,
            });
        }
        if (!beast.Slumbering)
            node.AddChild(new MeshInstance3D
            {
                Name = "Eyes", Position = new Vector3(0, size * 0.45f, size * 0.42f),
                Mesh = new BoxMesh { Size = new Vector3(size * 0.4f, size * 0.07f, size * 0.05f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xff8a3d)), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            });
        if (beast.Legendary)
        {
            var ring = new MeshInstance3D
            {
                Name = "Legend", Position = new Vector3(0, 0.002f, 0),
                Mesh = new TorusMesh { InnerRadius = size * 0.75f, OuterRadius = size * 0.85f, Rings = 24, RingSegments = 6 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xc9a227)), Metallic = 0.7f, Roughness = 0.3f },
            };
            node.AddChild(ring);
            var spin = ring.CreateTween().SetLoops();
            spin.TweenProperty(ring, "rotation:y", Mathf.Tau, 6.0).From(0f);
        }
        return node;
    }

    /// <summary>Landmarks are pale obelisks, labyrinths a ring of standing walls, ruins a few broken stones.</summary>
    private static Node3D MarkerNode(Marker marker)
    {
        var node = new Node3D { Name = $"{marker.Ref.Kind}{marker.Ref.Id}", Position = ToGodot(marker.Position) };
        var s = marker.Size;
        switch (marker.Shape)
        {
            case MarkerShape.Landmark:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.35f, s * 1.6f, s * 0.35f) }, Stone, s * 0.8f));
                node.AddChild(Solid(new PrismMesh { Size = new Vector3(s * 0.4f, s * 0.35f, s * 0.4f) }, marker.Colour, s * 1.78f));
                break;
            case MarkerShape.Labyrinth:
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * Mathf.Tau / 8;
                    var wall = Solid(new BoxMesh { Size = new Vector3(s * 0.38f, s * 0.35f, s * 0.08f) }, Stone, s * 0.17f);
                    wall.Position += new Vector3(Mathf.Cos(angle) * s * 0.5f, 0, Mathf.Sin(angle) * s * 0.5f);
                    wall.Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0);
                    node.AddChild(wall);
                }
                node.AddChild(Solid(new CylinderMesh { TopRadius = s * 0.16f, BottomRadius = s * 0.16f, Height = s * 0.06f }, Rgb.Lerp(marker.Colour, Stone, 0.45f), s * 0.03f));
                break;
            default:
                for (var k = 0; k < 3; k++)
                {
                    var stone = Solid(new BoxMesh { Size = new Vector3(s * 0.22f, s * (0.25f + 0.2f * k), s * 0.22f) }, marker.Colour, s * 0.15f);
                    stone.Position += new Vector3((k - 1) * s * 0.3f, 0, (k % 2) * s * 0.2f);
                    stone.Rotation = new Vector3(0.15f * k, 0.6f * k, 0);
                    node.AddChild(stone);
                }
                break;
        }
        return node;
    }

    private static MeshInstance3D Solid(Mesh mesh, Rgb colour, float lift) =>
        new() { Mesh = mesh, Position = new Vector3(0, lift, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(colour), Roughness = 0.8f } };

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    private static StandardMaterial3D VertexColours() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };

    /// <summary>
    /// Lines drawn at a constant thickness on screen whatever the zoom: each vertex carries the direction across its
    /// line (NORMAL) and which side it is on (UV.x), and the shader pushes it out in proportion to its distance from
    /// the camera. Width is in thousandths of the distance (about 1.6 px at 1440×900 per 0.001).
    /// </summary>
    internal const string LineShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform vec4 colour : source_color = vec4(0.1, 0.1, 0.09, 0.9);
        uniform float width = 0.0016;
        void vertex() {
            vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            float distance_to_eye = length(CAMERA_POSITION_WORLD - world);
            VERTEX += NORMAL * UV.x * width * distance_to_eye;
            VERTEX.y += 0.0008 * distance_to_eye;
        }
        void fragment() {
            ALBEDO = colour.rgb;
            ALPHA = colour.a;
        }
        """;

    /// <summary>Realm borders as dashed lines of constant screen width, ink over a pale halo so they read on any ground.</summary>
    internal static ArrayMesh DashedRibbons(IReadOnlyList<IReadOnlyList<Rgb>> lines, Rgb colour, float dash = 0.014f, float gap = 0.008f, float width = 0.0016f, float halo = 0.0034f)
    {
        var mesh = new ArrayMesh();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var line in lines)
        {
            var travelled = 0f;
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (ToGodot(line[i]), ToGodot(line[i + 1]));
                var length = a.DistanceTo(b);
                if (length < 1e-6f) continue;
                var across = new Vector3(-(b.Z - a.Z), 0, b.X - a.X).Normalized();
                for (var t = 0f; t < length;)
                {
                    var phase = (travelled + t) % (dash + gap);
                    var span = phase < dash ? Math.Min(dash - phase, length - t) : Math.Min(dash + gap - phase, length - t);
                    if (phase < dash)
                    {
                        var (p, q) = (a.Lerp(b, t / length), a.Lerp(b, (t + span) / length));
                        vertices.AddRange(new[] { p, q, p, q, q, p });
                        normals.AddRange(Enumerable.Repeat(across, 6));
                        uvs.AddRange(new[] { new Vector2(-1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 0) });
                    }
                    t += Math.Max(span, 1e-5f);
                }
                travelled += length;
            }
        }
        if (vertices.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        // The same dashes twice: a wide pale halo, then the ink line on top.
        foreach (var (tint, size, priority) in new[] { (Palette.Card, halo, 0), (colour, width, 1) })
        {
            if (size <= 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = new Shader { Code = LineShader }, RenderPriority = priority };
            material.SetShaderParameter("colour", Ui.C(tint, priority == 0 ? 0.75f : 0.95f));
            material.SetShaderParameter("width", size);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        return mesh;
    }

    /// <summary>Instances of one mesh with per-instance transforms and colours (colours given in sRGB).</summary>
    private static MultiMesh Instances(Mesh mesh, IEnumerable<(Transform3D Transform, Rgb Colour)> items)
    {
        var list = items.ToList();
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
        for (var i = 0; i < list.Count; i++)
        {
            multi.SetInstanceTransform(i, list[i].Transform);
            multi.SetInstanceColor(i, Ui.C(list[i].Colour).SrgbToLinear());
        }
        return multi;
    }

    private static Transform3D Place(Rgb at, Vector3 scale, float yaw) =>
        new(new Basis(Vector3.Up, yaw).Scaled(scale), ToGodot(at));

    private static StandardMaterial3D Foliage() => new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    private static StandardMaterial3D Painted() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.9f };

    /// <summary>A radial falloff from soft grey to clear, for the shadow under the block.</summary>
    private static ImageTexture SoftShadow(int size = 128)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // A rounded square: the block is square, its shadow a soft square.
                var dx = Math.Max(0, Math.Abs((x + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var dy = Math.Max(0, Math.Abs((y + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp(1 - d, 0, 1);
                image.SetPixel(x, y, new Color(0.1f, 0.1f, 0.09f, 0.22f * a * a));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private T Child<T>(string name) where T : Node, new()
    {
        if (GetNodeOrNull<T>(name) is { } existing) return existing;
        var child = new T { Name = name };
        AddChild(child);
        return child;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select((c, i) => Ui.C(c, data.Alphas?[i] ?? 1).SrgbToLinear()).ToArray();
        // Godot treats clockwise triangles as front faces; the client's indices are counter-clockwise.
        var indices = new int[data.Indices.Length];
        for (var i = 0; i < indices.Length; i += 3) (indices[i], indices[i + 1], indices[i + 2]) = (data.Indices[i], data.Indices[i + 2], data.Indices[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    public static Vector3 ToGodot(Rgb v) => new(v.X, v.Y, v.Z);
}
```

Replace `game/scripts/Main.cs` entirely with:
```csharp
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The root of the game (sub-project 4 §2): opens or creates the world, catches up on a worker thread with progress,
/// then keeps it live, and hosts the diorama and the screens. The world file comes from `-- --world &lt;path&gt;`, else
/// user://worlds/world.fw.db (created as a medium world with a century of prehistory on first launch).
/// </summary>
public partial class Main : Control
{
    public const double LiveInterval = 2.0;

    private Session? _session;
    private double _sinceStep;
    private bool _stepping;
    private bool _liveStopped;
    private readonly System.Threading.CancellationTokenSource _closing = new();

    public Session? Session => _session;

    public ScreenRouter Router { get; } = new();

    /// <summary>One line of state: creating, catching up, caught up.</summary>
    public Label Status { get; private set; } = null!;

    public bool CaughtUp { get; private set; }

    public Diorama Diorama { get; private set; } = null!;

    /// <summary>The layer the current screen's controls live in, above the diorama.</summary>
    public Control Screens { get; private set; } = null!;

    public Panel StatusLight { get; private set; } = null!;

    private ImageTexture? _miniMap;

    /// <summary>The painted world map for page insets, made once per world.</summary>
    public ImageTexture? MiniMap()
    {
        if (_miniMap is null && Geography is { } geography)
        {
            const int size = 192;
            _miniMap = ImageTexture.CreateFromImage(Image.CreateFromData(size, size, false, Image.Format.Rgba8, Client.MiniMap.Pixels(geography.Terrain, size)));
        }
        return _miniMap;
    }

    public Geography? Geography { get; private set; }

    /// <summary>The digest built when the world caught up (it is marked seen at once, so it is kept, not rebuilt).</summary>
    public DigestScreen? Digest { get; private set; }

    /// <summary>The control showing the current screen, if that screen has one yet.</summary>
    public Control? Current { get; private set; }

    /// <summary>Lets a test (or another host) supply an open session before the node enters the tree.</summary>
    public void Use(Session session) => _session = session;

    public override void _Ready()
    {
        Name = "Main";
        Theme = Ui.CreateTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // The diorama draws behind every card; the root lets clicks it does not use fall through to it.
        MouseFilter = MouseFilterEnum.Ignore;
        Diorama = new Diorama();
        AddChild(Diorama);
        // A soft wash of the ground colour behind the left-hand text, so titles stay legible over busy terrain.
        var wash = new Gradient();
        wash.SetColor(0, Ui.C(Palette.Ground, 0.9f));
        wash.SetColor(1, Ui.C(Palette.Ground, 0));
        wash.AddPoint(0.55f, Ui.C(Palette.Ground, 0.6f));
        var scrim = new TextureRect
        {
            Name = "Scrim", MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = new GradientTexture2D { Gradient = wash, Width = 256, Height = 4, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 0) },
        };
        scrim.AnchorBottom = 1;
        (scrim.OffsetLeft, scrim.OffsetRight) = (0, 560);
        AddChild(scrim);
        Screens = new Control { Name = "Screens", MouseFilter = MouseFilterEnum.Ignore };
        Screens.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Screens);
        var pill = Ui.Capsule("StatusPill", Ui.C(Palette.Card), Ui.C(Palette.Border), 14, 8);
        Ui.Place(pill, 1, 1, -40, -40);
        var row = Ui.Row("Row", 8);
        StatusLight = Ui.Dot(Palette.Muted, 8, "Light");
        row.AddChild(StatusLight);
        Status = Ui.Body("Opening…", "Status", Palette.Body, 12);
        Status.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(Status);
        pill.AddChild(row);
        AddChild(pill);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            if (_session is null)
            {
                var path = WorldPath();
                if (!System.IO.File.Exists(path))
                {
                    Status.Text = "Creating your world…";
                    _session = await Task.Run(() => Session.Create(path, (ulong)DateTimeOffset.UtcNow.UtcTicks, WorldSize.Medium, () => DateTimeOffset.UtcNow));
                }
                else _session = await Task.Run(() => Session.Open(path, () => DateTimeOffset.UtcNow));
            }
            var progress = new Progress<WorldDate>(date =>
            {
                if (IsInsideTree()) Status.Text = $"Catching up… {DigestScreen.StatusLine(date, 0)["Caught up · ".Length..]}";
            });
            // Catch-up comes in chunks of at most 30 world years; run them all before the world is shown.
            while ((await _session.CatchUpAsync(progress, _closing.Token)).DaysRemaining > 0) { }
            if (!IsInsideTree()) return;
            await OnCaughtUpAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (!IsInsideTree()) return;
            Status.Text = $"Could not open the world: {e.Message}";
            GD.PushError(e.ToString());
        }
    }

    /// <summary>
    /// The world is current: build the diorama and the digest on a worker (the digest may ask the LLM to narrate),
    /// then show them. Only Godot objects are made on the main thread.
    /// </summary>
    private async Task OnCaughtUpAsync()
    {
        var session = _session!;
        Status.Text = "Setting out the world…";
        var (geography, today, parts, digest) = await Task.Run(() =>
        {
            var geography = session.Read((w, _) => w.Geography);
            var today = session.Map();
            var parts = geography is null ? null : Diorama.Prepare(geography, today.Settlements.Where(s => s.Position is not null)
                .Select(s => Client.Projection.ToBlock(geography.Terrain, s.Position!, 0)).ToList());
            var digest = session.Digest();
            session.MarkSeen();
            return (geography, today, parts, digest);
        });
        if (!IsInsideTree()) return;
        Geography = geography;
        if (geography is not null && parts is not null)
        {
            Diorama.Build(geography, parts);
            Diorama.Show(today);
        }
        Digest = digest;
        Diorama.Picked += Picked;
        Router.Changed += Show;
        CaughtUp = true;
        UpdateStatus();
        Show(Router.Current);
    }

    /// <summary>A click on the diorama: a battle opens its why chain, a host its commander, anything else its page.</summary>
    private void Picked(FantasyWorld.Sim.Events.EntityRef picked)
    {
        if (picked.Kind == "event") Router.Explain(picked.Id);
        else if (picked.Kind == Monster.Kind)
        {
            if (Diorama.Monsters.FirstOrDefault(m => m.Ref == picked) is { } beast) Router.Explain(beast.RoseEventId);
        }
        else if (picked.Kind == Army.Kind)
        {
            if (Diorama.War.Armies.FirstOrDefault(a => a.Ref == picked)?.Commander is { } commander) Router.Open(commander.Ref);
        }
        else Router.Open(picked);
    }

    /// <summary>Replaces the current screen's controls (and the diorama's pins) for a screen.</summary>
    public void Show(Screen screen)
    {
        Ui.Clear(Screens);
        // Only the map travels in time; every other screen shows today's world.
        if (screen.Kind != ScreenKind.Map && Diorama.ShownYear is not null) Diorama.Show(_session!.Map());
        Diorama.ShowPins(screen.Kind == ScreenKind.Digest && Digest is not null && Geography is not null
            ? Markers.Pins(Digest, Geography) : Array.Empty<Pin>());
        try
        {
            Current = Build(screen);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // The page's subject is gone (an old minor event compacted away, a stale link): say so and stay put.
            GD.PushWarning($"Could not open {screen}: {e.Message}");
            Current = null;
            Toast("That page is no longer in the chronicle.", 0);
            if (Router.CanGoBack) Callable.From(() => Router.Back()).CallDeferred();
        }
        // Pages cover the diorama; there is no need to draw it underneath them.
        Diorama.Visible = screen.Kind is ScreenKind.Digest or ScreenKind.Map || Current is null;
        if (Current is not null) Screens.AddChild(Current);
    }

    private Control? Build(Screen screen) =>
        screen.Kind switch
        {
            ScreenKind.Digest when Digest is not null => new DigestView { Host = this, Digest = Digest },
            ScreenKind.Map => new MapHudView { Host = this, Focus = screen.Focus },
            ScreenKind.Character => new CharacterView { Host = this, Character = _session!.Character(screen.Id) },
            ScreenKind.Family => new FamilyView { Host = this, Tree = _session!.Family(screen.Id) },
            ScreenKind.Why => new WhyView { Host = this, Why = _session!.Why(screen.Id) },
            ScreenKind.Realm => new RealmView { Host = this, Realm = _session!.Realm(screen.Id) },
            ScreenKind.Artifact => new ArtifactView { Host = this, Artifact = _session!.Artifact(screen.Id) },
            ScreenKind.Chronicle => new ChronicleView { Host = this, Only = screen.Focus is { Kind: "war" } ? Category.War : null },
            _ => null,
        };

    public override void _Process(double delta)
    {
        if (!CaughtUp || _session is null) return;
        _sinceStep += delta;
        if (_sinceStep < LiveInterval) return;
        _sinceStep = 0;
        if (_stepping || _liveStopped) return;
        _ = StepLiveAsync();
    }

    /// <summary>
    /// One live step on a worker (never more than one at a time): the simulation, any LLM calls and the save stay off
    /// the main thread. Then the status, today's diorama and the map follow, and the day's big news is announced.
    /// </summary>
    private async Task StepLiveAsync()
    {
        var session = _session!;
        _stepping = true;
        try
        {
            var (days, map, news) = await Task.Run(() =>
            {
                var tick = session.Step();
                if (tick.Days == 0) return (0L, (MapScreen?)null, ((string, long)?)null);
                var top = tick.Events.Where(e => e.Importance >= LiveToastImportance).MaxBy(e => e.Importance);
                var line = top is null ? null : session.Read((w, _) => new FantasyWorld.Sim.Narration.TemplateNarrator(w).Sentence(top));
                return (tick.Days, session.Map(), top is null ? null : ((string, long)?)(line!, top.Id));
            });
            if (!IsInsideTree() || days == 0) return;
            UpdateStatus();
            // The world moved on: today's diorama follows it (a past map on the history slider stays put).
            if (map is not null && Diorama.ShownYear is null && Router.Current.Kind is ScreenKind.Digest or ScreenKind.Map)
            {
                Diorama.Show(map);
                (Current as MapHudView)?.Today(map);
            }
            if (news is { } n) Toast(n.Item1, n.Item2);
        }
        catch (Exception e)
        {
            // A save refused (another session changed the file) or a failing provider: stop living rather than lose work.
            _liveStopped = true;
            if (!IsInsideTree()) return;
            Status.Text = $"Live play paused: {e.Message}";
            StatusLight.AddThemeStyleboxOverride("panel", Ui.Round(Ui.C(Palette.Conflict), null));
            GD.PushError(e.ToString());
        }
        finally
        {
            _stepping = false;
        }
    }

    /// <summary>Events at least this important are announced as they happen while the game is open.</summary>
    public const int LiveToastImportance = 40;

    public PanelContainer? CurrentToast { get; private set; }

    /// <summary>A short-lived notice at the bottom of the screen; clicking it opens the event's why chain.</summary>
    public void Toast(string text, long eventId)
    {
        if (CurrentToast is { } old && IsInstanceValid(old)) old.QueueFree();
        var toast = Ui.Capsule("Toast", Ui.C(Palette.Ink), null, 18, 10);
        var row = Ui.Row("Row", 10);
        row.AddChild(Ui.Dot(Palette.Conflict, 8));
        row.AddChild(Ui.Line(text, "Text", Palette.Card, 13));
        toast.AddChild(row);
        toast.MouseDefaultCursorShape = CursorShape.PointingHand;
        toast.GuiInput += input =>
        {
            if (eventId != 0 && input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Router.Explain(eventId);
        };
        AddChild(toast);
        toast.AnchorLeft = toast.AnchorRight = 0.5f;
        toast.AnchorTop = toast.AnchorBottom = 1;
        toast.GrowHorizontal = GrowDirection.Both;
        toast.GrowVertical = GrowDirection.Begin;
        (toast.OffsetTop, toast.OffsetBottom) = (-40, -40);
        CurrentToast = toast;
        var fade = toast.CreateTween();
        fade.TweenInterval(6);
        fade.TweenProperty(toast, "modulate:a", 0f, 0.6f);
        fade.TweenCallback(Callable.From(() =>
        {
            if (CurrentToast == toast) CurrentToast = null;
            if (IsInstanceValid(toast)) toast.QueueFree();
        }));
    }

    /// <summary>Esc or Backspace goes back, H goes home to the digest, M opens the map.</summary>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!CaughtUp || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.CtrlPressed || key.AltPressed || key.MetaPressed || key.ShiftPressed) return;
        switch (key.Keycode)
        {
            case Key.Escape or Key.Backspace:
                Router.Back();
                break;
            case Key.H:
                Router.Home();
                break;
            case Key.M:
                Router.Go(new Screen(ScreenKind.Map));
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void UpdateStatus()
    {
        if (_session?.TryRead((w, _) => DigestScreen.StatusLine(w.Date, _session.DaysBehind)) is { } line)
        {
            Status.Text = line;
            StatusLight.AddThemeStyleboxOverride("panel", Ui.Round(Ui.C(_session.DaysBehind > 0 ? Palette.Conflict : Palette.Realm), null));
        }
    }

    /// <summary>Closing stops a catch-up in progress (the world keeps the days it simulated) and saves.</summary>
    public override void _ExitTree()
    {
        _closing.Cancel();
        try
        {
            _session?.Dispose();
        }
        catch (Exception e)
        {
            GD.PushError($"Could not save the world on exit: {e.Message}");
        }
    }

    private static string WorldPath()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--world");
        return at >= 0 && at + 1 < args.Length
            ? args[at + 1]
            : System.IO.Path.Combine(OS.GetUserDataDir(), "worlds", "world.fw.db");
    }
}
```

Replace `game/tests/HeadlessChecks.cs` entirely with:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// Runs the real scenes headless against a fresh tiny world and checks what they show (sub-project 4 §7). Exits 0 when
/// every check passes, 1 otherwise; CI runs it with `godot --headless --path game res://tests/HeadlessChecks.tscn`.
/// </summary>
public partial class HeadlessChecks : Node
{
    private readonly List<string> _failures = new();
    private int _checks;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "checks", $"check-{System.Guid.NewGuid():N}.fw.db");
        try
        {
            var session = await Task.Run(() => Session.Create(path, 3, WorldSize.Tiny, () => System.DateTimeOffset.UtcNow, prehistoryYears: 8));
            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            main.Use(session);
            AddChild(main);
            await Until(() => main.CaughtUp, "the world catches up");
            await RunChecks(main);
        }
        catch (System.Exception e)
        {
            Fail($"unexpected {e}");
        }
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GD.Print(_failures.Count == 0 ? $"HEADLESS CHECKS PASSED ({_checks})" : $"HEADLESS CHECKS FAILED: {string.Join("; ", _failures)}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>The checks for each screen; later tasks add theirs.</summary>
    private async Task RunChecks(Main main)
    {
        Check(main.Status.Text.StartsWith("Caught up · "), $"status reads caught up (\"{main.Status.Text}\")");
        await Frames(2);
        await DioramaChecks(main);
        await DigestChecks(main);
        await MapChecks(main);
        await PageChecks(main);
        await ReadingChecks(main);
        await WarChecks(main);
    }

    /// <summary>Stages a small war (a host marching across a border into land it occupies) and checks the war layer.</summary>
    private async Task WarChecks(Main main)
    {
        var session = main.Session!;
        var (armyRef, commander) = session.Read((world, log) =>
        {
            var link = world.Geography!.Graph.Links.First(l => world.Regions[l.A].OwnerRealmId != 0 && world.Regions[l.B].OwnerRealmId != 0
                                                             && world.Regions[l.A].OwnerRealmId != world.Regions[l.B].OwnerRealmId);
            var (home, target) = (world.Regions[link.A], world.Regions[link.B]);
            var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
            var war = new FantasyWorld.Sim.Model.War { Id = world.Ids.Take("war"), Name = "The Staged War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id } };
            world.Wars[war.Id] = war;
            var leader = world.Characters.Values.First(c => c.IsActive);
            var army = new FantasyWorld.Sim.Model.Army
            {
                Id = world.Ids.Take("army"), Name = $"The Host of {a.Name}", RealmId = a.Id, WarId = war.Id, CommanderId = leader.Id, Levies = 800,
                RegionId = home.Id, Path = { target.Id }, TargetRegionId = target.Id,
            };
            world.Armies[army.Id] = army;
            target.OccupierRealmId = a.Id;
            var beastId = world.Ids.Take("monster");
            var rose = log.Append(world.Date, new FantasyWorld.Sim.Events.EventDraft("MonsterRose",
                Actors: new Dictionary<string, FantasyWorld.Sim.Events.EntityRef> { ["monster"] = new("monster", beastId) }, Place: home.Ref));
            world.Monsters[beastId] = new FantasyWorld.Sim.Model.Monster
            {
                Id = beastId, Name = "The Wyrm of the Staged War", Breed = "wyrm", RegionId = home.Id, Might = 80, Legendary = true, RoseEventId = rose.Id,
            };
            return (army.Ref, leader.Ref);
        });
        main.Router.Go(new Screen(ScreenKind.Map));
        main.Diorama.Show(session.Map());
        (main.Current as MapHudView)?.Today(session.Map());
        main.Diorama.Rig.Reset();
        await Frames(90);
        var war = main.Diorama.War;
        Check(war.Banners.GetChildCount() == 1 && war.Arrows.GetChildCount() == 1, "a host on the march has a banner and a campaign arrow");
        Check(war.Hatch.Mesh is not null && war.Outline.Mesh is not null, "occupied land is hatched and outlined");
        Check(war.Fronts.Mesh is ArrayMesh { } fronts && fronts.GetSurfaceCount() >= 2, "the front between the sides is drawn with its teeth");
        var tag = (main.Current as MapHudView)?.TagLayer.GetChildren().OfType<ArmyTag>().FirstOrDefault();
        Check(tag is not null && tag.Army.Ref == armyRef, "the host is named on the map");
        Check(main.Diorama.Monsters.Count == 1 && main.Diorama.StructureLayer.GetNodeOrNull("monster" + main.Diorama.Monsters[0].Ref.Id) is not null,
            "a beast stands in its lair");
        await Shot("war");
        var banner = war.BannerAt(armyRef)!.Value;
        var screen = main.Diorama.Camera.UnprojectPosition(banner + new Godot.Vector3(0, WarLayer.BannerHeight * 0.7f, 0));
        Check(main.Diorama.Pick(screen) == armyRef, "clicking a banner picks its host");
        main.Router.Home();
        await Frames(2);
        main.Router.Open(commander);
        await Frames(2);
        Check(main.Current is CharacterView c && c.Character.Ref == commander, "a host's commander has a page");
        main.Router.Home();
    }

    private async Task ReadingChecks(Main main)
    {
        var session = main.Session!;
        var realm = session.Read((w, _) => w.Realms.Keys.First());
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("realm", realm));
        await Frames(2);
        Check(main.Current is RealmView r && r.GetNode<Label>("Left/Title").Text == $"{r.Realm.Title}.", "a realm link opens the realm's page");
        await Shot("realm");
        if (session.Read((w, _) => w.Artifacts.Keys.Cast<long?>().FirstOrDefault()) is { } artifact)
        {
            main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("artifact", artifact));
            await Frames(2);
            Check(main.Current is ArtifactView a && a.GetNode<Label>("Left/Title").Text == $"{a.Artifact.Name}.", "an artifact link opens its page");
        }
        main.Router.Go(new Screen(ScreenKind.Chronicle));
        await Frames(2);
        var chronicle = main.Current as ChronicleView;
        Check(chronicle is not null && chronicle.Page == chronicle.Chronicle.Pages && chronicle.Body.GetChildCount() == chronicle.Chronicle.Seasons.Count,
            "the full chronicle opens at its latest page, by season");
        if (chronicle is not null)
        {
            chronicle.Filter(FantasyWorld.Sim.Views.Category.War);
            Check(chronicle.Chronicle.Seasons.SelectMany(s => s.Rows).All(r => r.Category == FantasyWorld.Sim.Views.Category.War), "the chronicle filters by system");
            await Shot("chronicle");
        }

        main.Router.Home();
        main.Router.Go(new Screen(ScreenKind.Map));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView, "Esc goes back");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, Pressed = true });
        await Frames(2);
        Check(main.Current is MapHudView, "M opens the map");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView && !main.Router.CanGoBack, "H goes home");

        var news = session.Read((w, l) => l.All.Last(e => e.Importance >= Main.LiveToastImportance).Id);
        main.Toast("A test of the live notice.", news);
        await Frames(2);
        Check(main.CurrentToast is { Visible: true }, "notable events are announced while the game is open");
        main.CurrentToast!.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Check(main.Router.Current == new Screen(ScreenKind.Why, news), "and the notice opens its why chain");
        main.Router.Home();
    }

    private async Task PageChecks(Main main)
    {
        var session = main.Session!;
        var person = session.Read((w, l) => l.All.Reverse().SelectMany(e => e.Actors.Values).First(a => a.Kind == "char" && w.Characters.ContainsKey(a.Id)).Id);
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("char", person));
        await Frames(3);
        var character = main.Current as CharacterView;
        Check(character is not null && !main.Diorama.Visible, "a character link opens their page over the diorama");
        if (character is null) return;
        var screen = character.Character;
        Check(character.GetNode<Label>("Left/Who/Title").Text == $"{screen.Name}.", "the page is titled with their name");
        Check(character.GetNode("Left/Chips").GetChildCount() == screen.Traits.Count + (screen.Ambition.Length > 0 ? 1 : 0), "traits and ambition are chips");
        Check(character.Timeline.GetChildren().Count(c => c.Name.ToString().StartsWith("Event") || c.Name == "Highlight") == screen.Timeline.Count, "every deed is on the timeline");
        Check(character.GetNode("Ties/Column/List").GetChildCount() == Math.Max(1, screen.Ties.Count), "ties are listed");
        await Shot("character");
        var followed = session.IsFollowed(screen.Ref);
        character.ToggleFollow();
        Check(session.IsFollowed(screen.Ref) != followed && character.FollowButton.Text == (followed ? "Follow" : "Following"), "Follow follows");
        character.ToggleFollow();

        character.GetNode<Button>("Ties/Column/Actions/Family").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shown = (main.Current as FamilyView)?.FindChildren("Person*", "", true, false).Count ?? -1;
        Check(shown == session.Family(person).Nodes.Count, $"the family tree shows everyone ({shown} of {session.Family(person).Nodes.Count})");
        await Shot("family");
        main.Router.Back();
        await Frames(2);
        Check(main.Current is CharacterView, "Back returns to the character");

        var eventId = session.Read((w, l) => l.All.Last(e => e.Causes.Count > 0 && l.TryGet(e.Causes[0].EventId, out _)).Id);
        main.Router.Explain(eventId);
        await Frames(3);
        var why = main.Current as WhyView;
        Check(why is not null, "a why chain opens");
        if (why is null) return;
        Check(why.Graph.Cards.Count == 1 + why.Why.Layers.Sum(l => l.Count) && why.Canvas.FindChildren("Card*", "", false, false).Count == why.Graph.Cards.Count,
            "every cause is a card");
        Check(why.GetNode<Label>("Intro/Summary").Text == why.Why.Summary, "the chain is summarised");
        await Shot("why");
        Check(why.Canvas.FindChildren("Title", "RichTextLabel", true, false).Cast<Control>().All(t => t.MouseFilter == Control.MouseFilterEnum.Pass),
            "clicks on a card's words reach the card");
        var cause = why.Graph.Cards[1].Card.EventId;
        why.Canvas.GetNode<PanelContainer>($"Card{cause}").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(2);
        Check(main.Router.Current == new Screen(ScreenKind.Why, cause), "clicking a cause traces it further back");
        main.Router.Back();
        await Frames(2);
        var again = (WhyView)main.Current!;
        again.GetNode<Button>("Chain/Column/ActionsRow/Actions/TraceFurther").EmitSignal(BaseButton.SignalName.Pressed);
        Check(again.ShowingConsequences && again.Graph.Cards.Count == 1 + again.Why.Consequences.Count, "trace further shows what it led to");
        main.Router.Explain(long.MaxValue);
        await Frames(3);
        Check(main.Router.Current != new Screen(ScreenKind.Why, long.MaxValue) && main.Current is not null && main.CurrentToast is not null,
            "a page whose subject is gone says so and goes back instead of breaking");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.Visible && main.Current is DigestView, "home is the digest over the diorama");

        var rig = main.Diorama.Rig;
        var peak = main.Diorama.Markers.Count == 0 ? Godot.Vector3.Zero : Diorama.ToGodot(main.Diorama.Markers[0].Position);
        rig.Focus(new System.Numerics.Vector3(peak.X, 0, peak.Z), Zoom.Town);
        rig.Drag(0, 100_000);
        rig.Drag(0, -100_000);
        rig.Scroll(100);
        await Frames(120);
        var eye = main.Diorama.Camera.Position;
        Check(eye.Y > Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(main.Geography!.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)),
            "the camera never goes below the ground");
        rig.Reset();
    }

    private async Task MapChecks(Main main)
    {
        main.Diorama.Rig.Reset();
        main.Router.Go(new Screen(ScreenKind.Map));
        await Frames(30);
        var view = main.Current as MapHudView;
        Check(view is not null, "the map screen opens");
        if (view is null) return;
        var map = view.Map;
        Check(view.GetNode<Label>("Intro/Title").Text == $"{map.WorldName}.", "the map is titled with the world's name");
        Check(view.GetNode<Label>("Intro/Summary").Text == map.Summary, "and summarised");
        var tagged = map.Settlements.Count(s => MapLabels.Shows(s, view.Host.Diorama.Rig.NearestZoom, main.Diorama.Layers));
        Check(view.TagLayer.GetChildren().OfType<SettlementTag>().Count() == tagged, $"settlements big enough for the zoom are named ({tagged})");
        Check(view.GetNode("Legend").GetChildCount() == map.Legend.Count + (map.Armies is { Count: > 0 } ? 2 : 0), "the legend lists the realms");
        await Shot("map");

        var tags = view.TagLayer.GetChildren().OfType<SettlementTag>().ToList();
        if (tags.Count > 0)
        {
            view.ShowCard(tags[0]);
            Check(view.HoverCard.Visible && view.HoverCard.FindChildren("Title", "Label", true, false).OfType<Label>().Single().Text == tags[0].Settlement.Card.Title,
                "hovering a settlement shows who holds it");
            await Shot("map-card");
        }

        var borders = view.GetNode<CheckBox>("Card/Column/Layers/Borders");
        borders.ButtonPressed = false;
        Check(!main.Diorama.BorderLines.Visible && !main.Diorama.Layers.HasFlag(MapLayers.Borders), "the borders layer can be hidden");
        borders.ButtonPressed = true;
        var trade = view.GetNode<CheckBox>("Card/Column/Layers/TradeRoutes");
        trade.ButtonPressed = false;
        Check(!main.Diorama.RoadLines.Visible, "the trade routes layer can be hidden");
        trade.ButtonPressed = true;
        Check(main.Diorama.RoadLines.Visible && main.Diorama.Roads.Count == (map.Routes?.Count ?? 0), "every trade road is drawn");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = false;
        Check(!main.Diorama.SettlementLayer.Visible && view.TagLayer.GetChildren().OfType<SettlementTag>().All(t => t.Settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent),
            "hiding settlements leaves only places with news");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = true;

        view.History.Value = 2;
        await Frames(2);
        Check(view.Year == 2 && view.Map.Historical && view.Map.Year == 2 && main.Diorama.ShownYear == 2, "the history slider shows the map of Year 2");
        await Shot("map-year-2");
        view.GetNode<Button>("Card/Column/ActionsRow/Actions/Reset").EmitSignal(BaseButton.SignalName.Pressed);
        Check(view.Year == view.Now && main.Diorama.ShownYear is null && view.History.Value == view.Now, "reset view returns to today");

        view.GetNode<Button>("Card/Column/Zoom/Presets/Region").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(90);
        Check(main.Diorama.Rig.NearestZoom == Zoom.Region && view.GetNode<Label>("Intro/Kicker").Text.EndsWith("REGION VIEW"), "the zoom presets descend");

        view.History.Value = 2;
        var settlement = map.Settlements.First(s => s.Position is not null);
        main.Router.Open(settlement.Ref);
        await Frames(2);
        Check(main.Current is MapHudView { Focus: { } focus } && focus == settlement.Ref, "a linked place opens the map");
        Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Town)
              && Diorama.ToGodot(main.Diorama.Rig.GoalTarget).DistanceTo(Diorama.ToGodot(FantasyWorld.Client.Projection.ToBlock(main.Geography!.Terrain, settlement.Position!, 0))) < 0.01f,
            "and flies to it");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.ShownYear is null, "leaving the map returns the diorama to today");
    }

    private async Task DigestChecks(Main main)
    {
        main.Router.Home();
        main.Diorama.Rig.Reset();
        await Frames(60);
        var view = main.Current as DigestView;
        Check(view is not null, "the digest is the first screen");
        if (view is null) return;
        var digest = main.Digest!;
        Check(view.GetNode<Label>("Intro/Title").Text == digest.Title, "the digest shows the year");
        Check(view.GetNode<Label>("Intro/Subtitle").Text == digest.Subtitle, "and the time away");
        Check(main.Session!.Digest().Items.Count == 0 && main.Session.Digest().Headline is null, "showing the digest marks it seen");
        Check(digest.Headline is null || view.Content.GetNode<RichTextLabel>("Headline/HeadlineTitle").GetParsedText() == digest.Headline.Title.Text,
            "the headline is shown");
        Check(view.Content.GetNodeOrNull("Items")?.GetChildCount() == digest.Items.Count, $"every item is listed ({digest.Items.Count})");
        Check(view.PinLayer.GetChildCount() == digest.Pins.Count && main.Diorama.Pins.Count == digest.Pins.Count, $"one pin per place ({digest.Pins.Count})");
        await Shot("digest");

        if (digest.Pins.Count > 0)
        {
            var pin = view.PinLayer.GetChild<PinTag>(0);
            var expected = main.Diorama.ScreenOf(main.Diorama.Pins[0].Position) - new Godot.Vector2(11, 11);
            Check(pin.Position.DistanceTo(expected) < 1, "pins follow their places on screen");
            view.FlyTo(digest.Pins[0].EventId);
            await Frames(2);
            Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Region), "an item flies the camera to its place");
        }

        view.ShowChronicle(true);
        await Frames(2);
        Check(view.Content.GetNodeOrNull("Chronicle") is not null && view.ShowingChronicle, "the toggle shows the full chronicle");
        await Shot("digest-chronicle");
        view.ShowChronicle(false);

        if (digest.Headline is { CanExplain: true } headline)
        {
            view.Content.GetNode<Button>("Headline/Why").EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.Router.Current == new Screen(ScreenKind.Why, headline.EventId), "\"Why did this happen?\" opens the why chain");
            main.Router.Back();
            await Frames(2);
            Check(main.Current is DigestView, "Back returns to the digest");
        }
    }

    private async Task DioramaChecks(Main main)
    {
        var diorama = main.Diorama;
        var map = main.Session!.Map();
        Check(diorama.Block.Mesh is ArrayMesh { } block && block.GetSurfaceCount() == 2, "the block has a top and walls");
        Check(diorama.GetNodeOrNull<MeshInstance3D>("Water")?.Mesh is not null, "the sea is drawn");
        var settlements = map.Settlements.Count(s => s.Position is not null);
        Check(diorama.Markers.Count(m => m.Shape == MarkerShape.Settlement) == settlements
              && diorama.SettlementLayer.GetNode<MultiMeshInstance3D>("Roofs").Multimesh.InstanceCount >= 3 * settlements,
            "every settlement is a cluster of houses");
        Check(diorama.StructureLayer.GetChildren().Count(n => n.Name.ToString().StartsWith("structure")) == map.Structures.Count(s => s.Position is not null),
            "every landmark and labyrinth stands on the block");
        Check(diorama.TreeCount > 100, $"woods are planted ({diorama.TreeCount} trees)");
        Check(map.Borders.Count == 0 || diorama.BorderLines.Mesh is ArrayMesh { } lines && lines.GetSurfaceCount() == 2, "realm borders are drawn, ink over a halo");

        diorama.Rig.Reset();
        await Frames(90);
        await Shot("diorama");
        Check(diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Eye)) < 0.01f, "the camera follows the rig");
        var target = diorama.Markers[0];
        var onScreen = diorama.ScreenOf(target.Position);
        Check(diorama.Pick(onScreen) is { } picked && (picked == target.Ref || diorama.Markers.Any(m => m.Ref == picked && diorama.ScreenOf(m.Position).DistanceTo(onScreen) < 1)),
            "clicking a marker picks it");
        Check(diorama.Pick(new Godot.Vector2(-500, -500)) is null, "clicking empty space picks nothing");

        var settlement = map.Settlements.First(s => s.Position is not null);
        diorama.Focus(settlement.Position!, Zoom.Town);
        await Frames(120);
        await Shot("diorama-town");
        Check(diorama.Rig.NearestZoom == Zoom.Town && diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Target)) < 1f, "focusing descends to the town");
    }

    /// <summary>With `-- --shots &lt;dir&gt;` (and a real display), saves what the screen shows, for visual review.</summary>
    private async Task Shot(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = System.Array.IndexOf(args, "--shots");
        if (at < 0 || at + 1 >= args.Length || DisplayServer.GetName() == "headless") return;
        await Frames(4);
        System.IO.Directory.CreateDirectory(args[at + 1]);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(args[at + 1], name + ".png"));
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) Fail(what);
    }

    private void Fail(string what)
    {
        _failures.Add(what);
        GD.PrintErr("FAIL: " + what);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Until(System.Func<bool> condition, string what, int maxFrames = 600)
    {
        for (var i = 0; i < maxFrames && !condition(); i++) await Frames(1);
        Check(condition(), what);
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/MapScreen.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum PinMarker { Plain, Realm, RecentEvent, Conflict }

public sealed record MapRegion(EntityRef Ref, string Name, string Biome, MapPoint? Center, EntityRef? Owner, EntityRef? Occupier, string Colour);

public sealed record MapCard(string Kicker, string Title, string Line);

public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card);

public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// A host in the field: who leads it and who rides with it, its strength, what it is doing, and the road it means to
/// take (its region, then each region on its path, ending at its target).
/// </summary>
public sealed record MapArmy(
    EntityRef Ref, string Name, EntityRef Realm, string Colour, EntityRef War, string Side, Mention? Commander, IReadOnlyList<Mention> Knights,
    int Soldiers, string Objective, bool Besieging, MapPoint Position, IReadOnlyList<MapPoint> Route);

/// <summary>A trade road on the map: its markets, the way it runs (market, regions between, market) and its state.</summary>
public sealed record MapRoute(EntityRef Ref, string Name, EntityRef From, EntityRef To, string State, int Volume, IReadOnlyList<MapPoint> Path);

/// <summary>A beast in its lair: what it is, how mighty, whether it is a legend or asleep, and the event of its rising.</summary>
public sealed record MapMonster(EntityRef Ref, string Name, string Breed, int Might, bool Legendary, bool Slumbering, MapPoint Position, long RoseEventId);

/// <summary>A battle of the last year: where, who won, and the event to explain it.</summary>
public sealed record MapBattle(long EventId, int Year, MapPoint Position, EntityRef Victor, EntityRef Vanquished, EntityRef War);

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend,
    IReadOnlyList<MapArmy>? Armies = null, IReadOnlyList<MapBattle>? Battles = null, IReadOnlyList<MapRoute>? Routes = null,
    IReadOnlyList<MapMonster>? Monsters = null)
{
    /// <summary>Battles shown on the map: those of the last year.</summary>
    public const int BattleDays = 360;
    public static MapScreen Build(World world, EventLog log, WorldDate? asOf = null)
    {
        var requested = asOf is { } d && d < world.Date ? d : world.Date;
        var map = History.PoliticalMapAt(world, log, requested);
        var date = map.Historical ? requested : world.Date;
        var today = date == world.Date;
        var political = map.Regions;
        EntityRef? RealmRef(long id) => id != 0 && world.Realms.TryGetValue(id, out var r) ? r.Ref : null;
        string Colour(long id) => id != 0 && world.Realms.TryGetValue(map.TopOf(id), out var top) ? top.Colors[0] : "none";

        var regions = world.Regions.Values.Select(r => new MapRegion(r.Ref, r.Name, r.Biome.ToString(), MapPoint.Of(world, r.Ref),
            RealmRef(political[r.Id].OwnerRealmId), RealmRef(political[r.Id].OccupierRealmId), Colour(political[r.Id].OwnerRealmId))).ToList();

        var abandoned = log.All.Where(e => e.Type == "SettlementAbandoned" && e.Date <= date && e.Place is { Kind: Settlement.Kind })
            .Select(e => e.Place!.Value.Id).ToHashSet();
        var season = date.Day - 90;
        var settlements = new List<MapSettlement>();
        foreach (var s in world.Settlements.Values.Where(s => !abandoned.Contains(s.Id)))
        {
            var state = political[s.RegionId];
            var here = log.Involving(s.Ref).Where(e => e.Date <= date).ToList();
            var marker = state.OccupierRealmId != 0 ? PinMarker.Conflict
                : here.Any(e => e.Date.Day > season && e.Importance >= 30) ? PinMarker.RecentEvent
                : state.OwnerRealmId != 0 ? PinMarker.Realm : PinMarker.Plain;
            var holder = world.Realms.GetValueOrDefault(state.OwnerRealmId);
            var ceded = log.Involving(world.Regions[s.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded" && e.Date <= date);
            var title = holder is null ? "Held by no realm"
                : $"Held by the {Statecraft.Title(holder)} {(ceded is null ? "since the founding" : $"since Year {ceded.Date.Year}")}";
            if (state.OccupierRealmId != 0 && world.Realms.TryGetValue(state.OccupierRealmId, out var occupier))
                title += $", occupied by {occupier.Name}";
            var thisYear = here.Count(e => e.Date.Year == date.Year);
            var line = (today ? $"Pop. {s.Population.ToString("N0", CultureInfo.InvariantCulture)} · " : "")
                       + $"{thisYear} event{(thisYear == 1 ? "" : "s")} this year";
            settlements.Add(new MapSettlement(s.Ref, s.Name, s.Tier.ToString(), MapPoint.Of(world, s.Ref), holder?.Ref, marker,
                new MapCard($"{s.Name} · {s.Tier}", title, line)));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
            .ToList();

        var borders = world.Geography is { } geo
            ? geo.Graph.Links.Where(l => political[l.A].OwnerRealmId != political[l.B].OwnerRealmId)
                .Select(l => new Border(world.Regions[Math.Min(l.A, l.B)].Ref, world.Regions[Math.Max(l.A, l.B)].Ref)).Distinct().ToList()
            : new List<Border>();

        var conflicts = world.Wars.Values.Where(w => w.StartDay <= date.Day && (!w.Ended || log.TryGet(w.EndedEventId, out var end) && end.Date > date))
            .Select(w => (War: w, Sides: map.Wars.GetValueOrDefault(w.Id) ?? new WarSides(w.Attackers, w.Defenders)))
            .Select(x => new Conflict(x.War.Ref, x.War.Name, x.Sides.Attackers.Select(id => world.Realms[id].Ref).ToList(),
                x.Sides.Defenders.Select(id => world.Realms[id].Ref).ToList(),
                political.Values.Where(p => p.OccupyingWarId == x.War.Id).Select(p => world.Regions[p.RegionId].Ref).ToList()))
            .ToList();

        var independent = political.Values.Select(p => p.OwnerRealmId).Where(id => id != 0).Select(map.TopOf).Distinct()
            .Select(id => world.Realms[id]).OrderBy(r => r.Id).ToList();
        var legend = independent.Select(r => new LegendEntry(r.Name, r.Colors[0]))
            .Append(new LegendEntry("Active conflict", "conflict")).Append(new LegendEntry("Event this season", "event")).ToList();
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend, today ? ArmiesIn(world) : Array.Empty<MapArmy>(), BattlesBefore(world, log, date), RoutesAt(world, log, date, today), MonstersAt(world, log, date));
    }

    /// <summary>Hosts in the field today (armies are not kept in history, so past maps show none).</summary>
    private static IReadOnlyList<MapArmy> ArmiesIn(World world)
    {
        Mention? Of(long id) => world.Characters.TryGetValue(id, out var c) && c.IsActive ? new Mention(c.Ref, Names.Full(world, c)) : null;
        MapPoint? At(long region) => world.Regions.TryGetValue(region, out var r) ? MapPoint.Of(world, r.Ref) : null;
        var armies = new List<MapArmy>();
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded).OrderBy(a => a.Id))
        {
            if (At(army.RegionId) is not { } here || !world.Wars.TryGetValue(army.WarId, out var war) || !world.Realms.TryGetValue(army.RealmId, out var realm)) continue;
            var route = new List<MapPoint> { here };
            route.AddRange(army.Path.Select(At).OfType<MapPoint>());
            armies.Add(new MapArmy(army.Ref, army.Name, realm.Ref, realm.Colors[0], war.Ref, war.IsAttacker(realm.Id) ? "attacker" : "defender",
                Of(army.CommanderId), army.Knights.Select(Of).OfType<Mention>().ToList(), army.Soldiers, army.Objective.ToString().ToLowerInvariant(),
                world.Regions[army.RegionId].BesiegerArmyId == army.Id, here, route));
        }
        return armies;
    }

    /// <summary>Beasts that had risen by the date and were not yet slain then.</summary>
    private static IReadOnlyList<MapMonster> MonstersAt(World world, EventLog log, WorldDate date) =>
        world.Monsters.Values
            .Where(m => log.TryGet(m.RoseEventId, out var rose) && rose.Date <= date
                        && !(m.State == MonsterState.Slain && log.TryGet(m.SlainEventId, out var slain) && slain.Date <= date))
            .Select(m => (Beast: m, At: MapPoint.Of(world, world.Regions[m.RegionId].Ref)))
            .Where(x => x.At is not null)
            .Select(x => new MapMonster(x.Beast.Ref, x.Beast.Name, x.Beast.Breed, (int)Math.Round(x.Beast.Might), x.Beast.Legendary,
                x.Beast.State == MonsterState.Slumbering, x.At!, x.Beast.RoseEventId))
            .ToList();

    /// <summary>Roads opened by the date; their state then is replayed from their severing and restoring.</summary>
    private static IReadOnlyList<MapRoute> RoutesAt(World world, EventLog log, WorldDate date, bool today)
    {
        var routes = new List<MapRoute>();
        foreach (var route in world.Routes.Values)
        {
            if (!log.TryGet(route.OpenedEventId, out var opened) || opened.Date > date) continue;
            var state = route.State;
            if (!today)
            {
                var last = log.Involving(route.Ref).LastOrDefault(e => e.Date <= date && e.Type is "RouteSevered" or "RouteRestored");
                state = last?.Type == "RouteSevered" ? RouteState.Severed : RouteState.Open;
                // A road closes for good when a market is abandoned.
                if (new[] { route.FromId, route.ToId }.Any(id => log.Involving(world.Settlements[id].Ref).Any(e => e.Type == "SettlementAbandoned" && e.Date <= date)))
                    state = RouteState.Closed;
            }
            if (state == RouteState.Closed) continue;
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            var path = new List<MapPoint>();
            if (MapPoint.Of(world, from.Ref) is { } start) path.Add(start);
            path.AddRange(route.Regions.Skip(1).SkipLast(1).Select(r => MapPoint.Of(world, world.Regions[r].Ref)).OfType<MapPoint>());
            if (MapPoint.Of(world, to.Ref) is { } end) path.Add(end);
            if (path.Count < 2) continue;
            routes.Add(new MapRoute(route.Ref, route.Name, from.Ref, to.Ref, state.ToString().ToLowerInvariant(), (int)Math.Round(today ? route.Volume : 0), path));
        }
        return routes;
    }

    private static IReadOnlyList<MapBattle> BattlesBefore(World world, EventLog log, WorldDate date) =>
        log.All.Where(e => e.Type == "BattleFought" && e.Date <= date && e.Date.Day > date.Day - BattleDays && e.Place is { } p && MapPoint.Of(world, p) is not null)
            .Select(e => new MapBattle(e.Id, e.Date.Year, MapPoint.Of(world, e.Place!.Value)!, e.Actors["victor"], e.Actors["vanquished"], e.Actors["war"]))
            .ToList();

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type == StructureKind.Landmark && !(st.CompletedEventId != 0 && log.TryGet(st.CompletedEventId, out var done) && done.Date <= date))
            return st.State == Model.StructureState.Ruined && RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Building);
        if (st.Type == StructureKind.Landmark && st.State == Model.StructureState.Ruined)
            return RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Standing);
        return st.State.ToString();
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim` → `Passed: 523` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add game sim
git commit -m "feat(game): show beasts in their lairs on the diorama

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Faith spread, conversions and holy sites

Spec §4. Each year a peopled region may turn to the faith pressing hardest on it: its realm's faith and its neighbours' faiths, stronger where temples and shrines stand and for zealous faiths, weaker for ascetic or tolerant ones; its own temples hold it, and a people that turned keeps its faith for a generation. Conversions cite the realm's taking of the land or the neighbouring conversion that spread it. A realm whose lands have mostly turned takes up the new faith (its ruler too). A faith whose holy land falls to a realm of another faith, by cession or by the holder's conversion, grows zealous until it is regained. History replay also learns a missing event: an occupation that lapses when the land changes hands mid-war is now recorded (`OccupationLapsed`).

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/FaithSystem.cs`
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Model/Culture.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`, `sim/FantasyWorld.Sim/Systems/MilitarySystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`, `sim/FantasyWorld.Sim/Views/History.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/FaithSystemTests.cs`

**Interfaces:**
- Consumes: `World.Faiths`, `Region.FaithId`, `Realm.FaithId`, settlement buildings (Plans 2–4), `History` (sub-project 2).
- Produces: `Faith { Zeal; HolySiteLostEventId; Has(tenet) }`; `sealed class FaithSystem : ISimSystem { ConvertPerYear; SettleYears = 40; RealmShare = 2/3; LossZeal = 30; Devotion(world, region); Pressure(world, region); Odds(world, region) }` (after `TradeSystem`); events `RegionConverted`, `RealmConverted`, `HolySiteLost`, `HolySiteRegained`, `OccupationLapsed`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/FaithSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FaithSystemTests
{
    private static Faith AddFaith(World world, string name, long holyRegion, params string[] tenets)
    {
        var faith = new Faith { Id = world.Ids.Take(Faith.Kind), Name = name, Deity = name, HolyRegionId = holyRegion, Tenets = tenets.ToList() };
        world.Faiths[faith.Id] = faith;
        return faith;
    }

    private static Simulator Run(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new FaithSystem() }, checkInvariants: true);

    [Fact]
    public void ARealmsFaith_SpreadsToItsLands_BecauseTheRealmTookThem()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Place: world.Regions[1].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 200);
        var turned = sim.Log.All.First(e => e.Type == "RegionConverted" && e.Place == world.Regions[1].Ref);
        Assert.Equal(sun.Ref, turned.Actors["faith"]);
        Assert.Contains(turned.Causes, c => c.EventId == ceded.Id);
    }

    [Fact]
    public void APeopleThatTurned_KeepTheirNewFaithForAGeneration()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        world.Regions[2].FaithId = sun.Id; // each realm's people hold the other's faith, so both lands keep being pulled
        var sim = Run(world);
        sim.AdvanceDays(360 * 300);
        Assert.True(sim.Log.All.Count(e => e.Type == "RegionConverted") >= 3);
        foreach (var region in world.Regions.Values)
        {
            var turns = sim.Log.Involving(region.Ref).Where(e => e.Type == "RegionConverted").ToList();
            for (var i = 1; i < turns.Count; i++)
                Assert.True(turns[i].Date.Day - turns[i - 1].Date.Day >= FaithSystem.SettleYears * 360L);
        }
    }

    [Fact]
    public void Temples_HoldThePeopleToTheirFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var (faith, bare) = FaithSystem.Odds(world, world.Regions[1]);
        world.Settlements[1].Buildings.AddRange(new[] { "shrine", "temple" });
        var (_, kept) = FaithSystem.Odds(world, world.Regions[1]);
        Assert.Equal(sun.Id, faith);
        Assert.True(kept < bare);
    }

    [Fact]
    public void NeighbourPressure_IsWeakerForTolerantFaiths()
    {
        var world = TestPeople.World();
        var zealots = AddFaith(world, "The Iron Word", 2, "zealous");
        var tolerant = AddFaith(world, "The Open Hand", 2, "tolerant");
        world.Regions[2].FaithId = zealots.Id;
        var strong = FaithSystem.Pressure(world, world.Regions[1])[zealots.Id];
        world.Regions[2].FaithId = tolerant.Id;
        var weak = FaithSystem.Pressure(world, world.Regions[1])[tolerant.Id];
        Assert.True(weak < strong);
    }

    [Fact]
    public void ARealmWhoseLandsHaveTurned_TakesUpTheNewFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Regions[2].OwnerRealmId = 1; // realm 1 holds both lands
        world.Realms[2].Destroyed = true;
        world.Regions[1].FaithId = world.Regions[2].FaithId = sun.Id;
        var sim = Run(world);
        sim.AdvanceDays(360 * 30);
        var converted = sim.Log.All.First(e => e.Type == "RealmConverted");
        Assert.Equal(world.Realms[1].Ref, converted.Actors["realm"]);
        Assert.Equal(sun.Id, world.Realms[1].FaithId);
        Assert.Equal(sun.Id, Realms.RulerOf(world, world.Realms[1])!.FaithId);
    }

    [Fact]
    public void LosingTheHolyLand_RaisesZeal_AndRegainingItIsRecorded()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        world.Realms[2].FaithId = 1; // the holy land of the Sun Creed is held by the Way of Ohm, since it was ceded
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        var lost = sim.Log.All.Single(e => e.Type == "HolySiteLost");
        Assert.Equal(sun.Ref, lost.Actors["faith"]);
        Assert.Contains(lost.Causes, c => c.EventId == ceded.Id);
        Assert.Equal(lost.Id, sun.HolySiteLostEventId);
        Assert.True(sun.Zeal >= FaithSystem.LossZeal - 3);

        world.Realms[2].FaithId = sun.Id;
        sim.AdvanceDays(360);
        var regained = sim.Log.All.Single(e => e.Type == "HolySiteRegained");
        Assert.Contains(regained.Causes, c => c.EventId == lost.Id);
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandNeverHeld_IsNotALoss()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        var sim = Run(world);
        sim.AdvanceDays(360 * 3);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteLost");
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandLeftToNoRealm_IsNotRegained()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2);
        world.Realms[2].FaithId = 1;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        Assert.NotEqual(0, sun.HolySiteLostEventId);
        (world.Regions[2].OwnerRealmId, world.Realms[2].Destroyed) = (0, true); // its holder fell
        sim.AdvanceDays(360);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteRegained");
        Assert.NotEqual(0, sun.HolySiteLostEventId);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'FaithSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Engine/Invariants.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException(IReadOnlyList<string> errors)
        : base("World invariants violated:" + Environment.NewLine + string.Join(Environment.NewLine, errors)) =>
        Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Split into partial files per subject; each Check* hook is optional.</summary>
public static partial class Invariants
{
    /// <summary>Checks log events from startIndex onward, plus (unless entities is false) every entity. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0, bool entities = true)
    {
        var errors = new List<string>();
        CheckLog(world, log, startIndex, errors);
        if (!entities) return errors;
        CheckRegions(world, log, errors);
        CheckSettlements(world, log, errors);
        CheckCultures(world, errors);
        CheckPeople(world, log, errors);
        CheckArtifacts(world, log, errors);
        CheckRealms(world, log, errors);
        CheckStructures(world, log, errors);
        return errors;
    }

    static partial void CheckRealms(World world, EventLog log, List<string> errors);

    static partial void CheckStructures(World world, EventLog log, List<string> errors);

    static partial void CheckPeople(World world, EventLog log, List<string> errors);

    static partial void CheckArtifacts(World world, EventLog log, List<string> errors);

    private static void CheckCultures(World world, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.CultureId != 0 && !world.Cultures.ContainsKey(region.CultureId))
                errors.Add($"Region #{region.Id} has unknown culture #{region.CultureId}.");
            if (region.FaithId != 0 && !world.Faiths.ContainsKey(region.FaithId))
                errors.Add($"Region #{region.Id} has unknown faith #{region.FaithId}.");
        }
        foreach (var culture in world.Cultures.Values)
            if (!world.Faiths.ContainsKey(culture.FaithId))
                errors.Add($"Culture #{culture.Id} has unknown faith #{culture.FaithId}.");
        foreach (var faith in world.Faiths.Values)
            if (faith.Zeal is < 0 or > 100) errors.Add($"Faith #{faith.Id} has zeal {faith.Zeal} outside 0–100.");
        foreach (var realm in world.Realms.Values)
            if (realm.FaithId != 0 && !world.Faiths.ContainsKey(realm.FaithId))
                errors.Add($"Realm #{realm.Id} has unknown faith #{realm.FaithId}.");
    }

    private static void CheckLog(World world, EventLog log, int startIndex, List<string> errors)
    {
        var all = log.All;
        for (var i = Math.Max(0, startIndex); i < all.Count; i++)
        {
            var ev = all[i];
            if (i > 0)
            {
                var prev = all[i - 1];
                if (ev.Id <= prev.Id) errors.Add($"Event #{ev.Id} id does not increase after #{prev.Id}.");
                if (ev.Date < prev.Date) errors.Add($"Event #{ev.Id} is dated before #{prev.Id}.");
            }
            if (ev.Date > world.Date) errors.Add($"Event #{ev.Id} is dated in the future ({ev.Date} > {world.Date}).");
            if (ev.Causes.Count > EventLog.MaxCauses) errors.Add($"Event #{ev.Id} has {ev.Causes.Count} causes.");
            foreach (var cause in ev.Causes)
            {
                if (!log.TryGet(cause.EventId, out var c))
                    errors.Add($"Event #{ev.Id} cites missing cause #{cause.EventId}.");
                else if (c.Id >= ev.Id || c.Date > ev.Date)
                    errors.Add($"Event #{ev.Id} cites later event #{c.Id}.");
            }
        }
    }

    private static void CheckRegions(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, region) in world.Regions)
        {
            var name = $"Region #{id}";
            if (region.Id != id) errors.Add($"{name} is stored under the wrong key ({region.Id}).");
            Range(errors, name, "fertility", region.Fertility);
            Range(errors, name, "danger", region.Danger);
            Range(errors, name, "ley power", region.LeyPower);
            if (!(region.Food >= 0)) errors.Add($"{name} has invalid food stores {region.Food}.");
            foreach (var sid in region.Settlements)
                if (!world.Settlements.TryGetValue(sid, out var s) || s.RegionId != id)
                    errors.Add($"{name} lists settlement #{sid}, which is not in it.");
            Conditions(errors, log, name, region.Conditions);
        }
    }

    private static void CheckSettlements(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, s) in world.Settlements)
        {
            var name = $"Settlement #{id}";
            if (s.Id != id) errors.Add($"{name} is stored under the wrong key ({s.Id}).");
            if (!world.Regions.TryGetValue(s.RegionId, out var region) || !region.Settlements.Contains(id))
                errors.Add($"{name} is not listed by its region #{s.RegionId}.");
            if (s.Children < 0 || s.Adults < 0 || s.Elders < 0) errors.Add($"{name} has a negative population.");
            if (!(s.Wealth >= 0)) errors.Add($"{name} has invalid wealth {s.Wealth}.");
            Range(errors, name, "unrest", s.Unrest);
            Conditions(errors, log, name, s.Conditions);
        }
    }

    private static void Range(List<string> errors, string name, string what, double value)
    {
        if (!(value >= 0 && value <= 100)) errors.Add($"{name} has {what} {value} outside 0–100.");
    }

    private static void Conditions(List<string> errors, EventLog log, string name, SortedDictionary<string, long> conditions)
    {
        foreach (var (condition, eventId) in conditions)
            if (!log.TryGet(eventId, out _))
                errors.Add($"{name} condition '{condition}' cites missing event #{eventId}.");
    }
}
```

Replace `sim/FantasyWorld.Sim/Model/Culture.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Which parent's house children join, and where a married couple lives.</summary>
public enum Lineage { Patrilineal, Matrilineal }

public sealed class Culture
{
    public const string Kind = "culture";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public NameStyle Style { get; init; } = NameStyle.Default;
    public Lineage Lineage { get; init; }
    public long HomeRegionId { get; init; }
    public long FaithId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

public sealed class Faith
{
    public const string Kind = "faith";

    /// <summary>Tenet vocabulary. Plans 4–5 give them teeth (holy wars, magic law, decision weights).</summary>
    public static readonly IReadOnlyList<string> TenetPool =
        new[] { "warlike", "pacifist", "arcane", "ascetic", "mercantile", "ancestral", "zealous", "tolerant" };

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Deity { get; init; } = "";
    public List<string> Tenets { get; init; } = new();
    public long HolyRegionId { get; init; }

    /// <summary>0–100: how fiercely the faithful defend and spread it. Rises when the holy land is lost.</summary>
    public double Zeal { get; set; }

    /// <summary>The event that cost the faith its holy land (0 while a realm of the faith, or none, holds it).</summary>
    public long HolySiteLostEventId { get; set; }

    public bool Has(string tenet) => Tenets.Contains(tenet);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new FaithSystem(),
        new MonsterSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

`sim/FantasyWorld.Sim/Systems/FaithSystem.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Faiths (sub-project 3 §4). Each year a region's people may turn to the faith pressing hardest on them: their
/// realm's faith and their neighbours' faiths, stronger where temples and shrines stand, weaker for ascetic or
/// tolerant faiths; their own temples hold them. A realm whose lands have mostly turned takes up the new faith.
/// A faith whose holy land falls to a realm of another faith grows zealous until it is regained.
/// </summary>
public sealed class FaithSystem : ISimSystem
{
    public const double ConvertPerYear = 0.04;
    public const int SettleYears = 40;
    public const double RealmShare = 2.0 / 3;
    public const double LossZeal = 30;

    public string Name => "faith";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        Spread(ctx);
        RealmsConvert(ctx);
        HolySites(ctx);
    }

    /// <summary>Temples count fully, shrines by half.</summary>
    public static double Devotion(World world, Region region) =>
        world.LivingSettlements(region).Sum(s => (s.Buildings.Contains("temple") ? 1 : 0) + (s.Buildings.Contains("shrine") ? 0.5 : 0));

    /// <summary>Each other faith's pull on the region.</summary>
    public static SortedDictionary<long, double> Pressure(World world, Region region)
    {
        var pull = new SortedDictionary<long, double>();
        void Add(long faith, double weight)
        {
            if (faith == 0 || faith == region.FaithId || !world.Faiths.TryGetValue(faith, out var f)) return;
            if (f.Has("ascetic") || f.Has("tolerant")) weight *= 0.6;
            pull[faith] = pull.GetValueOrDefault(faith) + weight * (1 + f.Zeal / 100);
        }
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner)) Add(owner.FaithId, 3);
        foreach (var n in world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
        {
            var neighbour = world.Regions[n];
            Add(neighbour.FaithId, 1 + Devotion(world, neighbour) / 2);
        }
        return pull;
    }

    /// <summary>The faith pressing hardest on a peopled region and the yearly chance it wins them (0 if none).</summary>
    public static (long Faith, double Odds) Odds(World world, Region region)
    {
        if (region.FaithId == 0 || !world.LivingSettlements(region).Any()) return (0, 0);
        var (faith, pull) = Pressure(world, region).OrderByDescending(p => p.Value).ThenBy(p => p.Key).FirstOrDefault();
        return faith == 0 ? (0, 0) : (faith, ConvertPerYear * pull / (pull + 3 + Devotion(world, region)));
    }

    private static void Spread(SimContext ctx)
    {
        var world = ctx.World;
        var turned = new List<(Region Region, long Faith)>();
        foreach (var region in world.Regions.Values)
        {
            if (Settling(ctx, region)) continue;
            var (faith, odds) = Odds(world, region);
            if (faith != 0 && ctx.Rng.Chance(odds)) turned.Add((region, faith));
        }
        // Decided on last year's map, applied together, so the order of regions does not matter.
        foreach (var (region, faith) in turned) Convert(ctx, region, faith);
    }

    /// <summary>A people that turned within a generation keep their new faith a while.</summary>
    private static bool Settling(SimContext ctx, Region region) =>
        ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "RegionConverted") is { } turned
        && ctx.Date.Day - turned.Date.Day < SettleYears * (long)Core.WorldDate.DaysPerYear;

    private static void Convert(SimContext ctx, Region region, long faithId)
    {
        var world = ctx.World;
        var old = world.Faiths[region.FaithId];
        var faith = world.Faiths[faithId];
        var causes = new List<Cause>();
        var neighbours = world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>();
        var spread = neighbours.SelectMany(n => ctx.Log.Involving(world.Regions[n].Ref))
            .Where(e => e.Type == "RegionConverted" && e.Actors.GetValueOrDefault("faith") == faith.Ref)
            .OrderBy(e => e.Id).LastOrDefault();
        if (spread is not null) causes.Add(new Cause(spread.Id, CauseKind.Enabler, 0.6));
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner) && owner.FaithId == faithId)
        {
            var taken = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded").OrderBy(e => e.Id).LastOrDefault();
            causes.Add(new Cause(taken?.Id ?? owner.FoundedEventId, CauseKind.Enabler, 0.5));
        }
        region.FaithId = faithId;
        ctx.Emit(new EventDraft("RegionConverted", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["old"] = old.Ref },
            Place: region.Ref, Causes: causes.Where(c => c.EventId != 0).ToArray(), Importance: 25, Tags: new[] { "Faith" }));
    }

    private static void RealmsConvert(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            var domain = Realms.Domain(world, realm).ToList();
            if (domain.Count < 2) continue;
            var (faith, count) = domain.GroupBy(r => r.FaithId).Select(g => (g.Key, g.Count()))
                .OrderByDescending(g => g.Item2).ThenBy(g => g.Key).First();
            if (faith == realm.FaithId || faith == 0 || count < domain.Count * RealmShare || !ctx.Rng.Chance(0.2)) continue;
            var last = domain.SelectMany(r => ctx.Log.Involving(r.Ref))
                .Where(e => e.Type == "RegionConverted" && e.Actors["faith"].Id == faith).OrderBy(e => e.Id).LastOrDefault();
            realm.FaithId = faith;
            realm.Stability = Math.Max(0, realm.Stability - 10);
            if (Realms.RulerOf(world, realm) is { } ruler) ruler.FaithId = faith;
            ctx.Emit(new EventDraft("RealmConverted", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["faith"] = world.Faiths[faith].Ref },
                Place: world.Settlements[realm.CapitalId].Ref, Causes: last is null ? null : new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: 50, Tags: new[] { "Faith", "Politics" }));
        }
    }

    private static void HolySites(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values)
        {
            var calm = faith.Has("zealous") ? 40 : 10;
            faith.Zeal = Math.Max(Math.Min(faith.Zeal, calm), faith.Zeal - 3);
            if (!world.Regions.TryGetValue(faith.HolyRegionId, out var holy)) continue;
            var holder = Realms.SovereignOf(world, holy);
            var lost = holder is not null && holder.FaithId != faith.Id;
            if (lost && faith.HolySiteLostEventId == 0)
            {
                // Lost only by its cession to the holder or the holder's change of faith: a holy land never held is not a loss.
                var taken = ctx.Log.Involving(holy.Ref)
                    .Where(e => e.Type == "RegionCeded" && e.Actors.TryGetValue("to", out var to) && world.Realms.TryGetValue(to.Id, out var gainer)
                                && Realms.Top(world, gainer).Id == holder!.Id)
                    .Concat(ctx.Log.Involving(holder!.Ref).Where(e => e.Type == "RealmConverted"))
                    .OrderBy(e => e.Id).LastOrDefault();
                if (taken is null) continue;
                faith.Zeal = Math.Min(100, faith.Zeal + LossZeal);
                faith.HolySiteLostEventId = ctx.Emit(new EventDraft("HolySiteLost",
                    Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["realm"] = holder.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(taken.Id, CauseKind.Trigger, 0.8) }, Importance: 45, Tags: new[] { "Faith" })).Id;
            }
            else if (holder?.FaithId == faith.Id && faith.HolySiteLostEventId != 0)
            {
                var loss = faith.HolySiteLostEventId;
                faith.HolySiteLostEventId = 0;
                faith.Zeal = Math.Min(100, faith.Zeal + 10);
                ctx.Emit(new EventDraft("HolySiteRegained", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(loss, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            }
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/MilitarySystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Armies and campaigns (spec §6 "Campaign flow"), monthly: every realm at war raises an army (levies
/// from its settlements, men-at-arms its treasury can hire, its knights and mages, led by its marshal);
/// armies take orders (besiege the war's target or the nearest enemy holding; defend against invaders),
/// march along the region graph (30 travel days a month), forage or draw supply, pay upkeep, and suffer
/// attrition and desertion. Battles, sieges and peace live in the other parts of this class.
/// </summary>
public sealed partial class MilitarySystem : ISimSystem
{
    public const double LevyShare = 0.05;
    public const int MarchDaysPerMonth = 30;

    private readonly IArbiter _arbiter;
    private readonly IDecider _decider;

    public MilitarySystem(IArbiter? arbiter = null, IDecider? decider = null)
    {
        _arbiter = arbiter ?? new RulesArbiter();
        _decider = decider ?? new RulesDecider();
    }

    public string Name => "military";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var realmId in war.Attackers.Concat(war.Defenders).ToList())
                if (world.Realms[realmId] is { Destroyed: false } realm && ArmyOf(world, war, realm) is null && (ctx.Date.Month % 6 == 1 || war.StartDay >= ctx.Date.Day - 30))
                    Mobilise(ctx, war, realm);

        // Occupations lapse when occupier and sovereign are no longer at war (the land changed hands mid-war).
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
            if (Realms.SovereignOf(world, region) is not { } sovereign || !AtWarWith(world, region.OccupierRealmId, sovereign.Id))
            {
                // Recorded (and important enough to outlast compaction) so the history replay sees it, citing the change of hands or peace behind it.
                var occupier = world.Realms[region.OccupierRealmId];
                var ended = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded")
                    .Concat(ctx.Log.Involving(occupier.Ref).Where(e => e.Type is "PeaceSigned" or "IndependenceDeclared"))
                    .Select(e => e.Id).DefaultIfEmpty().Max();
                ctx.Emit(new EventDraft("OccupationLapsed", Actors: new Dictionary<string, EntityRef> { ["realm"] = occupier.Ref },
                    Place: region.Ref, Causes: ended == 0 ? null : new[] { new Cause(ended, CauseKind.Trigger, 0.8) },
                    Importance: Chronicle.Compactor.MinorBelow, Tags: new[] { "War" }));
                region.OccupierRealmId = 0;
            }

        foreach (var army in Active(world).ToList())
        {
            if (army.Disbanded) continue;
            if (!world.Characters.TryGetValue(army.CommanderId, out var leader) || !leader.IsActive)
            {
                var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
                var successor = Commander(world, world.Realms[army.RealmId], serving, ctx.Date);
                army.CommanderId = successor?.Id ?? 0;
                if (successor is not null) army.Knights.Remove(successor.Id);
            }
            GiveOrders(world, army);
            March(world, army);
            Provision(ctx, army);
        }
        FightBattles(ctx);
        Besiege(ctx);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) Score(ctx, war);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) ConsiderPeace(ctx, war);
    }

    partial void FightBattles(SimContext ctx);

    partial void Besiege(SimContext ctx);

    partial void Score(SimContext ctx, War war);

    partial void ConsiderPeace(SimContext ctx, War war);

    public static IEnumerable<Army> Active(World world) => world.Armies.Values.Where(a => !a.Disbanded);

    public static Army? ArmyOf(World world, War war, Realm realm) =>
        Active(world).FirstOrDefault(a => a.WarId == war.Id && a.RealmId == realm.Id);

    /// <summary>Raises the realm's host for this war at its capital.</summary>
    public static Army Mobilise(SimContext ctx, War war, Realm realm)
    {
        var world = ctx.World;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var raised = new SortedDictionary<long, int>();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)))
        {
            var levies = (int)(s.Adults * LevyShare);
            if (levies <= 0) continue;
            s.Adults -= levies;
            raised[s.Id] = levies;
        }
        var menAtArms = (int)Math.Clamp(realm.Treasury / 20, 0, 500);
        realm.Treasury -= menAtArms * 5;
        var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
        var knights = world.ActiveCharacters
            .Where(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId) && !serving.Contains(c.Id)
                        && c.Stage(ctx.Date) == LifeStage.Adult)
            .Select(c => c.Id).ToList();
        var mages = realm.MagicLaw == MagicLaw.Outlawed ? 0
            : world.ActiveCharacters.Count(c => c.Role == Role.Mage && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        var commander = Commander(world, realm, serving, ctx.Date);
        if (commander is not null) knights.Remove(commander.Id);

        var army = new Army
        {
            Id = world.Ids.Take(Army.Kind),
            Name = $"The Host of {realm.Name}",
            RealmId = realm.Id,
            WarId = war.Id,
            CommanderId = commander?.Id ?? 0,
            Levies = raised.Values.Sum(),
            MenAtArms = menAtArms,
            Knights = knights,
            Mages = mages,
            RegionId = world.Settlements[realm.CapitalId].RegionId,
            Objective = ArmyObjective.Defend,
            RaisedFrom = raised,
        };
        world.Armies[army.Id] = army;
        var actors = new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["war"] = war.Ref, ["army"] = army.Ref };
        if (commander is not null) actors["commander"] = commander.Ref;
        ctx.Emit(new EventDraft("ArmyRaised", Actors: actors, Place: world.Regions[army.RegionId].Ref,
            Payload: new Dictionary<string, string> { ["soldiers"] = army.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Trigger, 1.0) }, Importance: 25, Tags: new[] { "War" }));
        return army;
    }

    /// <summary>The marshal, else the adult ruler, else the most martial knight.</summary>
    private static Character? Commander(World world, Realm realm, HashSet<long> serving, Core.WorldDate today)
    {
        bool Free(Character c) => c.IsActive && !serving.Contains(c.Id) && c.Stage(today) != LifeStage.Child;
        if (realm.Council.TryGetValue(CouncilSeats.Marshal, out var marshal) && Free(world.Characters[marshal])) return world.Characters[marshal];
        if (Realms.RulerOf(world, realm) is { } ruler && Free(ruler)) return ruler;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        return world.ActiveCharacters.Where(c => c.Role == Role.Knight && Free(c) && territory.Contains(world.Settlements[c.SettlementId].RegionId))
            .OrderByDescending(c => c.Attributes.Martial).ThenBy(c => c.Id).FirstOrDefault();
    }

    /// <summary>
    /// Orders, in priority: a beaten army goes home (and rejoins the war once its morale recovers); anyone meets a host of
    /// this war standing in their side's land; then retakes their side's occupied land; attackers besiege the war's
    /// target, else the nearest enemy holding; strong defenders strike back.
    /// </summary>
    private static void GiveOrders(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var graph = world.Geography!.Graph;
        long target;
        var enemies = war.EnemiesOf(Side(war, realm));
        // Only hosts of this war: battles are fought between armies of the same war, so chasing another war's army would never end.
        var invader = Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId) && Friendly(world, war, realm, world.Regions[a.RegionId]))
            .OrderBy(a => graph.ShortestPath(army.RegionId, a.RegionId)?.Days ?? double.MaxValue).ThenBy(a => a.Id).FirstOrDefault();
        var lost = world.Regions.Values.Where(r => r.OccupierRealmId != 0 && !Hostile(world, war, realm, r)
                && !Friendly(world, war, realm, r) && Realms.SovereignOf(world, r) is { } owner && OurSide(war, realm).Contains(owner.Id))
            .OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
        var home = world.Settlements[realm.CapitalId].RegionId;
        if (army.Objective == ArmyObjective.Return && army.RegionId == home && army.Morale >= 50)
            army.Objective = ArmyObjective.Defend; // regrouped at home: back to the war
        if (army.Objective == ArmyObjective.Return)
            target = home;
        else if (invader is not null)
        {
            // Meet the invader before retaking what it took: chasing lost regions while it takes the next one makes
            // the two hosts trade places month after month without ever fighting.
            target = invader.RegionId;
            army.Objective = ArmyObjective.Defend;
        }
        else if (lost is not null)
        {
            target = lost.Id;
            army.Objective = ArmyObjective.Defend;
        }
        else
        {
            var goals = world.Regions.Values.Where(r => Hostile(world, war, realm, r) && world.LivingSettlements(r).Any()).ToList();
            var preferred = goals.FirstOrDefault(r => r.Id == war.TargetRegionId);
            var nearest = goals.OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
            var goal = war.IsAttacker(Side(war, realm)) ? preferred ?? nearest : invader is null && Strong(world, army) ? nearest : null;
            target = goal?.Id ?? world.Settlements[realm.CapitalId].RegionId;
            army.Objective = goal is null ? ArmyObjective.Defend : ArmyObjective.Siege;
        }
        if (target == army.TargetRegionId && (army.Path.Count > 0 || army.RegionId == target)) return;
        army.TargetRegionId = target;
        army.Path.Clear();
        army.DaysToNext = 0;
        if (graph.ShortestPath(army.RegionId, target) is { } route) army.Path.AddRange(route.Path.Skip(1));
    }

    private static void March(World world, Army army)
    {
        var budget = (double)MarchDaysPerMonth;
        var graph = world.Geography!.Graph;
        while (army.Path.Count > 0 && budget > 0)
        {
            if (army.DaysToNext <= 0) army.DaysToNext = graph.Between(army.RegionId, army.Path[0])?.TravelDays ?? 1;
            var step = Math.Min(budget, army.DaysToNext);
            army.DaysToNext -= step;
            budget -= step;
            if (army.DaysToNext > 0) break;
            army.RegionId = army.Path[0];
            army.Path.RemoveAt(0);
            if (EnemyArmyIn(world, army, army.RegionId)) break;
        }
    }

    /// <summary>Supply from friendly land is paid for; in enemy land the army forages and devastates.</summary>
    private static void Provision(SimContext ctx, Army army)
    {
        var world = ctx.World;
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var region = world.Regions[army.RegionId];
        if (Friendly(world, war, realm, region)) army.Supply = Math.Min(100, army.Supply + 20);
        else
        {
            var need = army.Soldiers * 0.5;
            var taken = Math.Min(region.Food, need);
            region.Food -= taken;
            region.Fertility = Math.Max(0, region.Fertility - 1);
            army.Supply = Math.Clamp(army.Supply + (taken >= need ? 10 : -15), 0, 100);
        }

        realm.Treasury -= army.MenAtArms * 0.5 + army.Knights.Count * 2 + army.Mages * 3;
        if (realm.Treasury < 0)
        {
            army.MenAtArms = (int)(army.MenAtArms * 0.9);
            army.Morale = Math.Max(0, army.Morale - 10);
        }
        if (army.Supply < 30)
        {
            army.Levies = (int)(army.Levies * 0.95);
            army.MenAtArms = (int)(army.MenAtArms * 0.97);
            army.Morale = Math.Max(0, army.Morale - 5);
        }
        else army.Morale = Math.Min(100, army.Morale + 2);
        foreach (var r in war.Attackers.Concat(war.Defenders)) world.Realms[r].WarWeariness = Math.Min(100, world.Realms[r].WarWeariness + 0.2);
    }

    public static IReadOnlyList<long> OurSide(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;

    /// <summary>The side leader a realm fights for in this war (its own id if it leads).</summary>
    public static long Side(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.AttackerId : war.DefenderId;

    /// <summary>Held (and not occupied) by our side, or occupied by our side.</summary>
    public static bool Friendly(World world, War war, Realm realm, Region region)
    {
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        if (region.OccupierRealmId != 0) return ours.Contains(region.OccupierRealmId);
        return Realms.SovereignOf(world, region) is { } owner && ours.Contains(owner.Id);
    }

    /// <summary>Held by the other side and not already occupied by ours.</summary>
    public static bool Hostile(World world, War war, Realm realm, Region region)
    {
        var theirs = war.IsAttacker(realm.Id) ? war.Defenders : war.Attackers;
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        return Realms.SovereignOf(world, region) is { } owner && theirs.Contains(owner.Id) && !ours.Contains(region.OccupierRealmId);
    }

    private static bool EnemyArmyIn(World world, Army army, long regionId)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.RegionId == regionId && a.WarId == war.Id && enemies.Contains(a.RealmId));
    }

    /// <summary>Two realms fight on opposite sides of some ongoing war.</summary>
    private static bool AtWarWith(World world, long a, long b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.Attackers.Contains(a) && w.Defenders.Contains(b) || w.Attackers.Contains(b) && w.Defenders.Contains(a)));

    private static bool Strong(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        var theirs = Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId)).Sum(a => a.Soldiers);
        return army.Soldiers >= theirs * 1.2;
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/History.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Views;

/// <summary>Who held and who occupied a region at some date, and for which war.</summary>
public sealed record RegionState(long RegionId, long OwnerRealmId, long OccupierRealmId, long OccupyingWarId = 0);

/// <summary>The sides of a war at some date.</summary>
public sealed record WarSides(IReadOnlyList<long> Attackers, IReadOnlyList<long> Defenders);

/// <summary>The political world at a date: regions, who is whose vassal, which realms have fallen, and the wars' sides.</summary>
public sealed record PoliticalMap(
    bool Historical, IReadOnlyDictionary<long, RegionState> Regions, IReadOnlyDictionary<long, long> Lieges,
    IReadOnlySet<long> Fallen, IReadOnlyDictionary<long, WarSides> Wars)
{
    /// <summary>The independent realm at the top of a realm's chain of lieges at that date.</summary>
    public long TopOf(long realm)
    {
        for (var guard = 0; guard < 64 && Lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }
}

/// <summary>
/// History at a date (sub-project 2 §5): the political world replayed from the founding baseline through cessions,
/// occupations, liberations, peaces (with their vassalage terms), declarations of independence, realms' falls and
/// allies joining wars. Today's answer is the live state. Worlds saved before the baseline existed cannot be replayed:
/// their map is the live state, marked not historical.
/// </summary>
public static class History
{
    public static PoliticalMap PoliticalMapAt(World world, EventLog log, WorldDate asOf)
    {
        if (asOf >= world.Date || world.FoundingOwners.Count == 0) return Live(world, historical: asOf >= world.Date);

        var owner = world.Regions.Keys.ToDictionary(id => id, id => world.FoundingOwners.GetValueOrDefault(id));
        var lieges = world.Realms.Keys.ToDictionary(id => id, id => world.FoundingLieges.GetValueOrDefault(id));
        var fallen = new HashSet<long>();
        var occupation = new Dictionary<long, (long Realm, long War)>();
        var sides = new Dictionary<long, (List<long> Attackers, List<long> Defenders)>();
        var over = new HashSet<long>();
        foreach (var ev in log.All)
        {
            if (ev.Date > asOf) break;
            var region = RegionOf(world, ev.Place);
            switch (ev.Type)
            {
                case "WarDeclared" when ev.Actors.TryGetValue("war", out var war):
                    sides[war.Id] = (new List<long> { ev.Actors["attacker"].Id }, new List<long> { ev.Actors["defender"].Id });
                    break;
                case "AllyJoinedWar" when ev.Actors.TryGetValue("war", out var joined) && sides.TryGetValue(joined.Id, out var s):
                    var ally = ev.Actors["ally"].Id;
                    var attacking = ev.Payload.TryGetValue("side", out var side)
                        ? side == "attackers"
                        : world.Wars.TryGetValue(joined.Id, out var known) && known.Attackers.Contains(ally);
                    (attacking ? s.Attackers : s.Defenders).Add(ally);
                    break;
                case "RegionCeded" when region != 0 && ev.Actors.TryGetValue("to", out var to):
                    owner[region] = to.Id;
                    occupation.Remove(region);
                    break;
                case "RegionOccupied" or "SiegeEnded" when region != 0 && ev.Actors.TryGetValue("realm", out var realm)
                                                           && ev.Actors.TryGetValue("war", out var at)
                                                           && (ev.Type == "RegionOccupied" || ev.Payload.GetValueOrDefault("outcome") == "fell"):
                    occupation[region] = (realm.Id, at.Id);
                    break;
                case "RegionLiberated" or "OccupationLapsed" when region != 0:
                    occupation.Remove(region);
                    break;
                case "PeaceSigned" when ev.Actors.TryGetValue("war", out var ended):
                    // As MilitarySystem.MakePeace: the war's occupations end, unless the occupier still fights the owner in another war.
                    over.Add(ended.Id);
                    var involved = sides.TryGetValue(ended.Id, out var e) ? e.Attackers.Concat(e.Defenders).ToHashSet() : new HashSet<long>();
                    foreach (var (held, occ) in occupation.ToList())
                    {
                        var holder = Top(lieges, owner[held]);
                        if (involved.Contains(occ.Realm) && (holder == 0 || involved.Contains(holder) || !AtWar(sides, over, occ.Realm, holder)))
                            occupation.Remove(held);
                    }
                    if (ev.Payload.GetValueOrDefault("terms") is { } terms && terms.Contains("vassalage", StringComparison.Ordinal)
                        && ev.Actors.TryGetValue("vanquished", out var vassal) && ev.Actors.TryGetValue("victor", out var overlord)
                        && !fallen.Contains(vassal.Id))
                        lieges[vassal.Id] = overlord.Id;
                    break;
                case "IndependenceDeclared" when ev.Actors.TryGetValue("realm", out var free):
                    lieges[free.Id] = 0;
                    break;
                case "RealmFell" when ev.Actors.TryGetValue("realm", out var gone):
                    fallen.Add(gone.Id);
                    foreach (var id in owner.Where(o => o.Value == gone.Id).Select(o => o.Key).ToList()) owner[id] = 0;
                    foreach (var id in occupation.Where(o => o.Value.Realm == gone.Id).Select(o => o.Key).ToList()) occupation.Remove(id);
                    foreach (var id in lieges.Where(l => l.Value == gone.Id).Select(l => l.Key).ToList()) lieges[id] = 0;
                    foreach (var (attackers, defenders) in sides.Values)
                    {
                        attackers.Remove(gone.Id);
                        defenders.Remove(gone.Id);
                    }
                    break;
            }
        }
        var regions = owner.ToDictionary(o => o.Key, o => occupation.TryGetValue(o.Key, out var occ)
            ? new RegionState(o.Key, o.Value, occ.Realm, occ.War) : new RegionState(o.Key, o.Value, 0));
        return new PoliticalMap(true, regions, lieges, fallen, sides.ToDictionary(s => s.Key, s => new WarSides(s.Value.Attackers, s.Value.Defenders)));
    }

    private static PoliticalMap Live(World world, bool historical) => new(historical,
        world.Regions.Values.ToDictionary(r => r.Id, r => new RegionState(r.Id, r.OwnerRealmId, r.OccupierRealmId,
            r.OccupierRealmId == 0 ? 0 : world.Wars.Values.Where(w => !w.Ended && w.Involves(r.OccupierRealmId)).Select(w => w.Id).FirstOrDefault())),
        world.Realms.Values.ToDictionary(r => r.Id, r => r.LiegeId),
        world.Realms.Values.Where(r => r.Destroyed).Select(r => r.Id).ToHashSet(),
        world.Wars.Values.ToDictionary(w => w.Id, w => new WarSides(w.Attackers.ToList(), w.Defenders.ToList())));

    private static long Top(Dictionary<long, long> lieges, long realm)
    {
        for (var guard = 0; guard < 64 && realm != 0 && lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }

    private static bool AtWar(Dictionary<long, (List<long> Attackers, List<long> Defenders)> sides, HashSet<long> over, long a, long b) =>
        sides.Any(w => !over.Contains(w.Key) && (w.Value.Attackers.Contains(a) && w.Value.Defenders.Contains(b) || w.Value.Attackers.Contains(b) && w.Value.Defenders.Contains(a)));

    private static long RegionOf(World world, EntityRef? place) => place switch
    {
        { Kind: Region.Kind } p => p.Id,
        { Kind: Settlement.Kind } p when world.Settlements.TryGetValue(p.Id, out var s) => s.RegionId,
        _ => 0,
    };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~FaithSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 531` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): spread faiths from realms and neighbours, and make holy lands matter

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Schisms, holy wars for the holy land, saints

Spec §4. A zealous faith under strain (its holy land lost, or a realm of its faith in decline) may split: its most learned priest founds a heresy of the same god with one tenet changed (the inventor names it), their land follows and nearby lands may. A realm whose faith lost its holy land to a neighbour gains a *holy war* casus belli against it, citing the loss, and the war is named for the land. A devout notable of great renown is declared a saint after death; their relics, kept in their town, hold its people to their faith.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.cs`, `sim/FantasyWorld.Sim/Model/Artifact.cs`, `sim/FantasyWorld.Sim/Model/Character.cs`, `sim/FantasyWorld.Sim/Model/Culture.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`, `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`, `sim/FantasyWorld.Sim/Systems/FaithSystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Systems/FaithSystemTests.cs`

**Interfaces:**
- Consumes: Task 5; `DiplomacySystem.CasusBelliFor`, `Fates.Die`, `ArtifactPlace.Treasury`.
- Produces: `Faith { ParentFaithId; FounderId; FoundedEventId }`; `Character.SaintEventId`; `FaithSystem(IInventor?) { SchismZeal; SchismPerYear; SaintRenown = 45; Cap(world) }`; artifact power `devotion`; events `SchismBegan`, `SaintDeclared` (with the `relic`).

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Systems/FaithSystemTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FaithSystemTests
{
    private static Faith AddFaith(World world, string name, long holyRegion, params string[] tenets)
    {
        var faith = new Faith { Id = world.Ids.Take(Faith.Kind), Name = name, Deity = name, HolyRegionId = holyRegion, Tenets = tenets.ToList() };
        world.Faiths[faith.Id] = faith;
        return faith;
    }

    private static Simulator Run(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new FaithSystem() }, checkInvariants: true);

    [Fact]
    public void ARealmsFaith_SpreadsToItsLands_BecauseTheRealmTookThem()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Place: world.Regions[1].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 200);
        var turned = sim.Log.All.First(e => e.Type == "RegionConverted" && e.Place == world.Regions[1].Ref);
        Assert.Equal(sun.Ref, turned.Actors["faith"]);
        Assert.Contains(turned.Causes, c => c.EventId == ceded.Id);
    }

    [Fact]
    public void APeopleThatTurned_KeepTheirNewFaithForAGeneration()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        world.Regions[2].FaithId = sun.Id; // each realm's people hold the other's faith, so both lands keep being pulled
        var sim = Run(world);
        sim.AdvanceDays(360 * 300);
        Assert.True(sim.Log.All.Count(e => e.Type == "RegionConverted") >= 3);
        foreach (var region in world.Regions.Values)
        {
            var turns = sim.Log.Involving(region.Ref).Where(e => e.Type == "RegionConverted").ToList();
            for (var i = 1; i < turns.Count; i++)
                Assert.True(turns[i].Date.Day - turns[i - 1].Date.Day >= FaithSystem.SettleYears * 360L);
        }
    }

    [Fact]
    public void Temples_HoldThePeopleToTheirFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var (faith, bare) = FaithSystem.Odds(world, world.Regions[1]);
        world.Settlements[1].Buildings.AddRange(new[] { "shrine", "temple" });
        var (_, kept) = FaithSystem.Odds(world, world.Regions[1]);
        Assert.Equal(sun.Id, faith);
        Assert.True(kept < bare);
    }

    [Fact]
    public void NeighbourPressure_IsWeakerForTolerantFaiths()
    {
        var world = TestPeople.World();
        var zealots = AddFaith(world, "The Iron Word", 2, "zealous");
        var tolerant = AddFaith(world, "The Open Hand", 2, "tolerant");
        world.Regions[2].FaithId = zealots.Id;
        var strong = FaithSystem.Pressure(world, world.Regions[1])[zealots.Id];
        world.Regions[2].FaithId = tolerant.Id;
        var weak = FaithSystem.Pressure(world, world.Regions[1])[tolerant.Id];
        Assert.True(weak < strong);
    }

    [Fact]
    public void ARealmWhoseLandsHaveTurned_TakesUpTheNewFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Regions[2].OwnerRealmId = 1; // realm 1 holds both lands
        world.Realms[2].Destroyed = true;
        world.Regions[1].FaithId = world.Regions[2].FaithId = sun.Id;
        var sim = Run(world);
        sim.AdvanceDays(360 * 30);
        var converted = sim.Log.All.First(e => e.Type == "RealmConverted");
        Assert.Equal(world.Realms[1].Ref, converted.Actors["realm"]);
        Assert.Equal(sun.Id, world.Realms[1].FaithId);
        Assert.Equal(sun.Id, Realms.RulerOf(world, world.Realms[1])!.FaithId);
    }

    [Fact]
    public void LosingTheHolyLand_RaisesZeal_AndRegainingItIsRecorded()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        world.Realms[2].FaithId = 1; // the holy land of the Sun Creed is held by the Way of Ohm, since it was ceded
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        var lost = sim.Log.All.Single(e => e.Type == "HolySiteLost");
        Assert.Equal(sun.Ref, lost.Actors["faith"]);
        Assert.Contains(lost.Causes, c => c.EventId == ceded.Id);
        Assert.Equal(lost.Id, sun.HolySiteLostEventId);
        Assert.True(sun.Zeal >= FaithSystem.LossZeal - 3);

        world.Realms[2].FaithId = sun.Id;
        sim.AdvanceDays(360);
        var regained = sim.Log.All.Single(e => e.Type == "HolySiteRegained");
        Assert.Contains(regained.Causes, c => c.EventId == lost.Id);
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandNeverHeld_IsNotALoss()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        var sim = Run(world);
        sim.AdvanceDays(360 * 3);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteLost");
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandLeftToNoRealm_IsNotRegained()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2);
        world.Realms[2].FaithId = 1;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        Assert.NotEqual(0, sun.HolySiteLostEventId);
        (world.Regions[2].OwnerRealmId, world.Realms[2].Destroyed) = (0, true); // its holder fell
        sim.AdvanceDays(360);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteRegained");
        Assert.NotEqual(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AZealousFaithThatLostItsHolyLand_SplitsWhenAPriestFoundsAHeresy()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var ohm = world.Faiths[1];
        ohm.Tenets.Add("zealous");
        world.Realms[1].FaithId = AddFaith(world, "The Sun Creed", 2).Id; // Ohm's holy land is held by a realm of the Sun
        var lost = log.Append(world.Date, new EventDraft("HolySiteLost", Place: world.Regions[1].Ref));
        (ohm.HolySiteLostEventId, ohm.Zeal) = (lost.Id, 90);
        var priest = TestPeople.Add(world, 2, 40, role: Role.Priest);
        priest.Attributes.Lore = 18;
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 40);

        var schism = sim.Log.All.First(e => e.Type == "SchismBegan");
        Assert.Equal(priest.Ref, schism.Actors["heresiarch"]);
        Assert.Contains(schism.Causes, c => c.EventId == lost.Id);
        var heresy = world.Faiths[schism.Actors["faith"].Id];
        Assert.Equal((ohm.Id, priest.Id, schism.Id, ohm.Deity), (heresy.ParentFaithId, heresy.FounderId, heresy.FoundedEventId, heresy.Deity));
        Assert.Equal("The True Way of Ohm", heresy.Name);
        Assert.NotEqual(ohm.Tenets.Order(), heresy.Tenets.Order());
        Assert.Equal(heresy.Id, priest.FaithId);
        Assert.Contains(sim.Log.All, e => e.Type == "RegionConverted" && e.Causes.Any(c => c.EventId == schism.Id) && e.Place == world.Regions[2].Ref);
    }

    [Fact]
    public void ADevoutNotableOfRenown_IsDeclaredASaint_AndTheirRelicsHoldTheTown()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var holy = TestPeople.Add(world, 1, 70, role: Role.Priest, traits: new[] { Traits.Devout });
        holy.Renown = FaithSystem.SaintRenown;
        var before = FaithSystem.Devotion(world, world.Regions[1]);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var died = Fates.Die(ctx, holy, "old age", Array.Empty<Cause>());
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 3);

        var saint = sim.Log.All.Single(e => e.Type == "SaintDeclared");
        Assert.Equal(holy.Ref, saint.Actors["saint"]);
        Assert.Contains(saint.Causes, c => c.EventId == died.Id);
        Assert.Equal(saint.Id, holy.SaintEventId);
        var relic = world.Artifacts[saint.Actors["relic"].Id];
        Assert.Equal((ArtifactType.Relic, ArtifactPlace.Treasury, 1L), (relic.Type, relic.Place, relic.SettlementId));
        Assert.True(FaithSystem.Devotion(world, world.Regions[1]) > before);
    }

    [Fact]
    public void ARealmWhoseFaithLostItsHolyLand_HasAHolyWarAgainstItsHolder()
    {
        var world = TestRealms.TwoKingdoms();
        world.Geography = world.Geography; // the two kingdoms border each other
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2);
        world.Realms[1].FaithId = sun.Id;
        var lost = log.Append(world.Date, new EventDraft("HolySiteLost", Place: world.Regions[2].Ref));
        (sun.HolySiteLostEventId, sun.Zeal) = (lost.Id, 50);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.HolyWar && o.EventId == lost.Id);
        Assert.Equal((2L, 2L), (cb.Against, cb.TargetRegionId));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'Faith' does not contain a definition for 'ParentFaithId' and no accessible extension method 'ParentFaithId' accepting a first argument of type 'Faith' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Engine/Invariants.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException(IReadOnlyList<string> errors)
        : base("World invariants violated:" + Environment.NewLine + string.Join(Environment.NewLine, errors)) =>
        Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Split into partial files per subject; each Check* hook is optional.</summary>
public static partial class Invariants
{
    /// <summary>Checks log events from startIndex onward, plus (unless entities is false) every entity. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0, bool entities = true)
    {
        var errors = new List<string>();
        CheckLog(world, log, startIndex, errors);
        if (!entities) return errors;
        CheckRegions(world, log, errors);
        CheckSettlements(world, log, errors);
        CheckCultures(world, errors);
        CheckPeople(world, log, errors);
        CheckArtifacts(world, log, errors);
        CheckRealms(world, log, errors);
        CheckStructures(world, log, errors);
        return errors;
    }

    static partial void CheckRealms(World world, EventLog log, List<string> errors);

    static partial void CheckStructures(World world, EventLog log, List<string> errors);

    static partial void CheckPeople(World world, EventLog log, List<string> errors);

    static partial void CheckArtifacts(World world, EventLog log, List<string> errors);

    private static void CheckCultures(World world, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.CultureId != 0 && !world.Cultures.ContainsKey(region.CultureId))
                errors.Add($"Region #{region.Id} has unknown culture #{region.CultureId}.");
            if (region.FaithId != 0 && !world.Faiths.ContainsKey(region.FaithId))
                errors.Add($"Region #{region.Id} has unknown faith #{region.FaithId}.");
        }
        foreach (var culture in world.Cultures.Values)
            if (!world.Faiths.ContainsKey(culture.FaithId))
                errors.Add($"Culture #{culture.Id} has unknown faith #{culture.FaithId}.");
        foreach (var faith in world.Faiths.Values)
        {
            if (faith.Zeal is < 0 or > 100) errors.Add($"Faith #{faith.Id} has zeal {faith.Zeal} outside 0–100.");
            if (faith.ParentFaithId != 0 && !world.Faiths.ContainsKey(faith.ParentFaithId))
                errors.Add($"Faith #{faith.Id} split from unknown faith #{faith.ParentFaithId}.");
        }
        foreach (var realm in world.Realms.Values)
            if (realm.FaithId != 0 && !world.Faiths.ContainsKey(realm.FaithId))
                errors.Add($"Realm #{realm.Id} has unknown faith #{realm.FaithId}.");
    }

    private static void CheckLog(World world, EventLog log, int startIndex, List<string> errors)
    {
        var all = log.All;
        for (var i = Math.Max(0, startIndex); i < all.Count; i++)
        {
            var ev = all[i];
            if (i > 0)
            {
                var prev = all[i - 1];
                if (ev.Id <= prev.Id) errors.Add($"Event #{ev.Id} id does not increase after #{prev.Id}.");
                if (ev.Date < prev.Date) errors.Add($"Event #{ev.Id} is dated before #{prev.Id}.");
            }
            if (ev.Date > world.Date) errors.Add($"Event #{ev.Id} is dated in the future ({ev.Date} > {world.Date}).");
            if (ev.Causes.Count > EventLog.MaxCauses) errors.Add($"Event #{ev.Id} has {ev.Causes.Count} causes.");
            foreach (var cause in ev.Causes)
            {
                if (!log.TryGet(cause.EventId, out var c))
                    errors.Add($"Event #{ev.Id} cites missing cause #{cause.EventId}.");
                else if (c.Id >= ev.Id || c.Date > ev.Date)
                    errors.Add($"Event #{ev.Id} cites later event #{c.Id}.");
            }
        }
    }

    private static void CheckRegions(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, region) in world.Regions)
        {
            var name = $"Region #{id}";
            if (region.Id != id) errors.Add($"{name} is stored under the wrong key ({region.Id}).");
            Range(errors, name, "fertility", region.Fertility);
            Range(errors, name, "danger", region.Danger);
            Range(errors, name, "ley power", region.LeyPower);
            if (!(region.Food >= 0)) errors.Add($"{name} has invalid food stores {region.Food}.");
            foreach (var sid in region.Settlements)
                if (!world.Settlements.TryGetValue(sid, out var s) || s.RegionId != id)
                    errors.Add($"{name} lists settlement #{sid}, which is not in it.");
            Conditions(errors, log, name, region.Conditions);
        }
    }

    private static void CheckSettlements(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, s) in world.Settlements)
        {
            var name = $"Settlement #{id}";
            if (s.Id != id) errors.Add($"{name} is stored under the wrong key ({s.Id}).");
            if (!world.Regions.TryGetValue(s.RegionId, out var region) || !region.Settlements.Contains(id))
                errors.Add($"{name} is not listed by its region #{s.RegionId}.");
            if (s.Children < 0 || s.Adults < 0 || s.Elders < 0) errors.Add($"{name} has a negative population.");
            if (!(s.Wealth >= 0)) errors.Add($"{name} has invalid wealth {s.Wealth}.");
            Range(errors, name, "unrest", s.Unrest);
            Conditions(errors, log, name, s.Conditions);
        }
    }

    private static void Range(List<string> errors, string name, string what, double value)
    {
        if (!(value >= 0 && value <= 100)) errors.Add($"{name} has {what} {value} outside 0–100.");
    }

    private static void Conditions(List<string> errors, EventLog log, string name, SortedDictionary<string, long> conditions)
    {
        foreach (var (condition, eventId) in conditions)
            if (!log.TryGet(eventId, out _))
                errors.Add($"{name} condition '{condition}' cites missing event #{eventId}.");
    }
}
```

Replace `sim/FantasyWorld.Sim/Model/Artifact.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum ArtifactType { Weapon, Armor, Crown, Tome, Relic, Trinket }

/// <summary>Held by a character, kept in a settlement's treasury, lost in a region, or destroyed.</summary>
public enum ArtifactPlace { Held, Treasury, Lost, Destroyed }

/// <summary>
/// A mechanical effect. Kind "attribute" adds Amount to Attribute while held; other kinds are tags
/// later systems read: "ward" (monsters), "truthsight" (intrigue), "harvest" (region food), "legitimacy" (Plan 4), "devotion" (a saint's relics: holds their town to its faith).
/// </summary>
public sealed record ArtifactPower(string Kind, string? Attribute, int Amount);

/// <summary>One change of hands. From/To are character ids (0 = none); How: forged, found, inherited, gifted, stolen, looted, lost.</summary>
public sealed record ProvenanceEntry(long EventId, long From, long To, string How);

/// <summary>A named object with history (spec §4).</summary>
public sealed class Artifact
{
    public const string Kind = "artifact";

    public long Id { get; init; }
    public string Name { get; set; } = "";

    /// <summary>Legend invented for it (Plan 5); empty when none was.</summary>
    public string Lore { get; set; } = "";

    public ArtifactType Type { get; init; }
    public string Material { get; init; } = "";

    /// <summary>The creating event, or 0 if it predates recorded history (prehistory arrives in Plan 6).</summary>
    public long OriginEventId { get; init; }

    public List<ArtifactPower> Powers { get; init; } = new();

    /// <summary>A hidden downside, e.g. "paranoia"; revealed over time.</summary>
    public string? Curse { get; init; }
    public bool CurseRevealed { get; set; }

    /// <summary>0–100. Grows with deeds done while held.</summary>
    public double Fame { get; set; }

    public List<ProvenanceEntry> Provenance { get; init; } = new();

    public ArtifactPlace Place { get; set; }
    public long HolderId { get; set; }
    public long SettlementId { get; set; }
    public long RegionId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>Total bonus this artifact gives to an attribute while held.</summary>
    public int Bonus(string attribute) =>
        Powers.Where(p => p.Kind == "attribute" && p.Attribute == attribute).Sum(p => p.Amount);
}
```

Replace `sim/FantasyWorld.Sim/Model/Character.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum Sex { Female, Male }

public enum LifeStage { Child, Adult, Elder, Dead }

/// <summary>A notable's place in society. Lords are the settlement's ruler (Settlement.LordId is the source of truth).</summary>
public enum Role { None, Lord, Knight, Mage, Priest, Merchant, Elder, Adventurer, Rebel }

/// <summary>The six attributes, each 0–20.</summary>
public sealed class Attributes
{
    public int Martial { get; set; }
    public int Diplomacy { get; set; }
    public int Stewardship { get; set; }
    public int Intrigue { get; set; }
    public int Lore { get; set; }
    public int Prowess { get; set; }

    public static readonly IReadOnlyList<string> Names =
        new[] { "martial", "diplomacy", "stewardship", "intrigue", "lore", "prowess" };

    public int Get(string name) => name switch
    {
        "martial" => Martial,
        "diplomacy" => Diplomacy,
        "stewardship" => Stewardship,
        "intrigue" => Intrigue,
        "lore" => Lore,
        "prowess" => Prowess,
        _ => throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name)),
    };

    public void Set(string name, int value)
    {
        value = Math.Clamp(value, 0, 20);
        switch (name)
        {
            case "martial": Martial = value; break;
            case "diplomacy": Diplomacy = value; break;
            case "stewardship": Stewardship = value; break;
            case "intrigue": Intrigue = value; break;
            case "lore": Lore = value; break;
            case "prowess": Prowess = value; break;
            default: throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name));
        }
    }
}

/// <summary>A remembered event. About is the character it concerns (0 for none); Feeling −100…100; Weight decays yearly.</summary>
public sealed record Memory(long EventId, long About, double Feeling, double Weight);

/// <summary>A named, individually simulated person (spec §3 "Notables").</summary>
public sealed class Character
{
    public const string Kind = "char";
    public const int AdultAge = 16;
    public const int ElderAge = 60;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long HouseId { get; set; }
    public Sex Sex { get; init; }
    public long BirthDay { get; init; }
    public WorldDate? Died { get; set; }
    public string? DeathCause { get; set; }

    public long CultureId { get; init; }
    public long FaithId { get; set; }

    /// <summary>The settlement the character lives in.</summary>
    public long SettlementId { get; set; }

    public long FatherId { get; init; }
    public long MotherId { get; init; }
    public long SpouseId { get; set; }

    /// <summary>The event that began the current marriage (cited by births).</summary>
    public long MarriageEventId { get; set; }

    public List<long> Children { get; init; } = new();

    public Attributes Attributes { get; init; } = new();
    public List<string> Traits { get; init; } = new();
    public string Ambition { get; set; } = Model.Ambitions.Family;

    /// <summary>Whom the ambition targets (a rival for vengeance), 0 for none.</summary>
    public long AmbitionTarget { get; set; }

    public Role Role { get; set; }
    public List<Memory> Memories { get; init; } = new();

    /// <summary>Lasting injuries, e.g. "sword arm".</summary>
    public List<string> Wounds { get; init; } = new();

    /// <summary>0–100. Grows with deeds; raises the importance of their events.</summary>
    public double Renown { get; set; }

    /// <summary>The event that declared them a saint after death (0 if not one).</summary>
    public long SaintEventId { get; set; }

    /// <summary>No longer individually followed: absorbed into the settlement's populace.</summary>
    public bool Faded { get; set; }

    [JsonIgnore] public bool IsAlive => Died is null;

    /// <summary>Alive and still a notable.</summary>
    [JsonIgnore] public bool IsActive => Died is null && !Faded;

    public int Age(WorldDate today) => (int)((today.Day - BirthDay) / WorldDate.DaysPerYear);

    public LifeStage Stage(WorldDate today) => Died is not null
        ? LifeStage.Dead
        : Age(today) switch { < AdultAge => LifeStage.Child, < ElderAge => LifeStage.Adult, _ => LifeStage.Elder };

    public bool Has(string trait) => Traits.Contains(trait);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

/// <summary>A family line (spec "house/dynasty").</summary>
public sealed class House
{
    public const string Kind = "house";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long FounderId { get; set; }
    public long SeatSettlementId { get; set; }
    public long CultureId { get; init; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Model/Culture.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Which parent's house children join, and where a married couple lives.</summary>
public enum Lineage { Patrilineal, Matrilineal }

public sealed class Culture
{
    public const string Kind = "culture";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public NameStyle Style { get; init; } = NameStyle.Default;
    public Lineage Lineage { get; init; }
    public long HomeRegionId { get; init; }
    public long FaithId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

public sealed class Faith
{
    public const string Kind = "faith";

    /// <summary>Tenet vocabulary. Plans 4–5 give them teeth (holy wars, magic law, decision weights).</summary>
    public static readonly IReadOnlyList<string> TenetPool =
        new[] { "warlike", "pacifist", "arcane", "ascetic", "mercantile", "ancestral", "zealous", "tolerant" };

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Deity { get; init; } = "";
    public List<string> Tenets { get; init; } = new();
    public long HolyRegionId { get; init; }

    /// <summary>0–100: how fiercely the faithful defend and spread it. Rises when the holy land is lost.</summary>
    public double Zeal { get; set; }

    /// <summary>The event that cost the faith its holy land (0 while a realm of the faith, or none, holds it).</summary>
    public long HolySiteLostEventId { get; set; }

    /// <summary>For a faith born of a schism: the faith it split from, its heresiarch and the schism (else 0).</summary>
    public long ParentFaithId { get; init; }

    public long FounderId { get; init; }
    public long FoundedEventId { get; init; }

    public bool Has(string tenet) => Tenets.Contains(tenet);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new FaithSystem(inventor),
        new MonsterSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

Replace `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>A reason to go to war, found in history: the event it cites and what it aims at.</summary>
public sealed record CasusBelliOption(string Kind, long Against, long EventId, long TargetRegionId, long TargetArtifactId, double Weight);

/// <summary>
/// Relations between independent realms (spec §6 "Diplomacy"), yearly. Realm opinion = the rulers'
/// personal opinion + realm memories + faith and culture + treaties. Friendly neighbours ally (sealed
/// by a royal marriage when kin allow), hostile ones declare rivalry. A ruler at peace weighs every
/// casus belli history offers against keeping the peace (a decision point); a declared war cites its
/// casus belli and draws in allies.
/// </summary>
public sealed class DiplomacySystem : ISimSystem
{
    private readonly IDecider _decider;
    private readonly IInventor _inventor;

    public DiplomacySystem(IDecider? decider = null, IInventor? inventor = null)
    {
        _decider = decider ?? new RulesDecider();
        _inventor = inventor ?? new RulesInventor();
    }

    public string Name => "diplomacy";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var realms = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var a in realms)
            foreach (var b in Realms.Neighbours(world, a).Where(b => b.Id > a.Id && b.LiegeId == 0))
                ReviewTreaties(ctx, a, b);

        foreach (var realm in realms)
        {
            if (realm.Destroyed || realm.LiegeId != 0 || AtWar(world, realm)) continue;
            var ruler = Realms.RulerOf(world, realm);
            if (ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var options = CasusBelliFor(ctx, realm);
            if (options.Count == 0) continue;
            var choices = options.Select((o, i) => new DecisionOption($"war:{i}", o.Weight * Aggression(world, realm, ruler)
                    * Math.Clamp(Strength(world, realm) / Math.Max(1, Strength(world, world.Realms[o.Against])), 0.3, 2.0),
                    $"{o.Kind} against {world.Realms[o.Against].Name}"))
                .Append(new DecisionOption("peace", 100 + realm.WarWeariness, "keep the peace"))
                .ToList();
            var choice = _decider.Decide(new DecisionBrief(ruler.Id, "war or peace", choices), ctx.Rng);
            if (choice.Key == "peace") continue;
            using (ctx.Annotate("motive", choice.Reasoning))
                Declare(ctx, realm, options[int.Parse(choice.Key[4..], CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>What realm a thinks of realm b, −100…100.</summary>
    public static double Opinion(World world, Realm a, Realm b)
    {
        var score = 0.0;
        if (Realms.RulerOf(world, a) is { } ra && Realms.RulerOf(world, b) is { } rb) score += Relations.Opinion(ra, rb) / 2;
        score += a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += a.FaithId == b.FaithId ? 15 : -15;
        if (a.CultureId == b.CultureId) score += 10;
        if (a.Has(TreatyKind.Alliance, b.Id)) score += 25;
        if (a.Has(TreatyKind.Rivalry, b.Id)) score -= 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Rough fighting power: levies from the territory, men-at-arms the treasury could pay, knights.</summary>
    public static double Strength(World world, Realm realm)
    {
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var levies = world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)).Sum(s => s.Adults) * 0.05;
        var knights = world.ActiveCharacters.Count(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        return levies + Math.Max(0, realm.Treasury) / 50 * 3 + knights * 20;
    }

    public static bool AtWar(World world, Realm realm) => world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id));

    private static double Aggression(World world, Realm realm, Character ruler)
    {
        var a = 1.0;
        if (ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave)) a *= 1.5;
        if (ruler.Has(Traits.Content) || ruler.Has(Traits.Craven) || ruler.Has(Traits.Kind)) a *= 0.5;
        var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
        if (tenets.Contains("pacifist")) a *= 0.3;
        if (tenets.Contains("warlike")) a *= 1.5;
        if (realm.Conditions.ContainsKey(Statecraft.StabilityCollapsed)) a *= 0.5;
        return a;
    }

    private static void ReviewTreaties(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        var ab = Opinion(world, a, b);
        var ba = Opinion(world, b, a);
        var allied = a.Has(TreatyKind.Alliance, b.Id);
        var rivals = a.Has(TreatyKind.Rivalry, b.Id);
        if (!allied && ab >= 40 && ba >= 40 && !AtWarWith(world, a, b))
        {
            var wedding = RoyalMarriage(ctx, a, b);
            var pact = Pact(ctx, a, b, TreatyKind.Alliance, "AllianceFormed", 40, wedding);
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == a.Id);
            Statecraft.Remember(a, pact.Id, b.Id, 40, 60);
            Statecraft.Remember(b, pact.Id, a.Id, 40, 60);
        }
        else if (allied && (ab < 0 || ba < 0))
        {
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
            var broken = ctx.Emit(new EventDraft("AllianceBroken", Actors: Pair(a, b), Place: world.Settlements[a.CapitalId].Ref,
                Importance: 40, Tags: new[] { "Politics", "Diplomacy" }));
            Statecraft.Remember(a, broken.Id, b.Id, -30, 60);
            Statecraft.Remember(b, broken.Id, a.Id, -30, 60);
        }
        else if (!rivals && ab <= -40 && ba <= -40)
            Pact(ctx, a, b, TreatyKind.Rivalry, "RivalryDeclared", 35, null);
    }

    private static WorldEvent Pact(SimContext ctx, Realm a, Realm b, TreatyKind kind, string type, int importance, WorldEvent? cause)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: Pair(a, b), Place: ctx.World.Settlements[a.CapitalId].Ref,
            Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Enabler, 0.8) },
            Importance: importance, Tags: new[] { "Politics", "Diplomacy" }));
        a.Treaties.Add(new Treaty(kind, b.Id, ev.Id, 0));
        b.Treaties.Add(new Treaty(kind, a.Id, ev.Id, 0));
        return ev;
    }

    /// <summary>Weds an unmarried adult child or sibling of one ruler to one of the other's, if any match.</summary>
    private static WorldEvent? RoyalMarriage(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        IEnumerable<Character> Kin(Realm realm) => Realms.RulerOf(world, realm) is { } ruler
            ? ruler.Children.Select(id => world.Characters[id]).Concat(Fates.Siblings(world, ruler)).Append(ruler)
                .Where(k => FamilySystem.Marriageable(k, ctx.Date))
            : Array.Empty<Character>();
        // Children first, then siblings, then the rulers themselves.
        foreach (var x in Kin(a))
            foreach (var y in Kin(b))
                if (x.Sex != y.Sex && !(x.Role == Role.Lord && y.Role == Role.Lord) && !Relations.AreKin(x, y))
                    return FamilySystem.Wed(ctx, x, y);
        return null;
    }

    /// <summary>Every casus belli this realm's history gives it against a neighbour.</summary>
    public static List<CasusBelliOption> CasusBelliFor(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var options = new List<CasusBelliOption>();
        var ruler = Realms.RulerOf(world, realm);
        foreach (var enemy in Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id)))
        {
            var theirs = Realms.Territory(world, enemy).ToList();
            var grudge = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling <= -50).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null)
            {
                var ev = ctx.Log.Get(grudge.EventId);
                var lost = ev.Type == "RegionCeded" && theirs.Any(r => ev.Place == r.Ref) ? ev.Place!.Value.Id : 0;
                if (ev.Type == "IndependenceDeclared")
                    options.Add(new(CasusBelli.Independence, enemy.Id, grudge.EventId, world.Settlements[enemy.CapitalId].RegionId, 0, 40));
                else if (lost != 0) options.Add(new(CasusBelli.Reconquest, enemy.Id, grudge.EventId, lost, 0, 40));
                else options.Add(new(CasusBelli.Revenge, enemy.Id, grudge.EventId, Border(world, realm, enemy), 0, 30));
            }

            var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
            if (realm.FaithId != enemy.FaithId && (tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, 0, Border(world, realm, enemy), 0, 20));
            // A faith that lost its holy land to this neighbour wants it back, the more so the more zealous it is.
            if (world.Faiths.GetValueOrDefault(realm.FaithId) is { HolySiteLostEventId: not 0 } mine && world.Regions.TryGetValue(mine.HolyRegionId, out var holy)
                && Realms.SovereignOf(world, holy)?.Id == enemy.Id && (mine.Zeal >= 40 || tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, mine.HolySiteLostEventId, holy.Id, 0, 40));

            var hungry = Realms.Domain(world, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
            var granary = theirs.Where(r => r.Fertility >= 60 && Touches(world, r, realm)).OrderByDescending(r => r.Fertility).ThenBy(r => r.Id).FirstOrDefault();
            if (hungry != 0 && granary is not null) options.Add(new(CasusBelli.ResourceNeed, enemy.Id, hungry, granary.Id, 0, 30));

            if (ruler is not null)
            {
                foreach (var artifact in world.Artifacts.Values.Where(x => HeldBy(world, x, enemy)))
                {
                    // Only a fresh wrong counts: the artifact's latest change of hands was a theft from the ruler's house.
                    var theft = artifact.Provenance.LastOrDefault();
                    if (theft is { How: "stolen" or "looted" } && ruler.HouseId != 0
                        && world.Characters.TryGetValue(theft.From, out var victim) && victim.HouseId == ruler.HouseId) options.Add(new(CasusBelli.ArtifactRecovery, enemy.Id, theft.EventId, world.Settlements[enemy.CapitalId].RegionId, artifact.Id, 25));
                }
                if (Realms.RulerOf(world, enemy) is { } theirRuler && world.Characters.TryGetValue(theirRuler.SpouseId, out var consort)
                    && (consort.HouseId == ruler.HouseId) && consort.MarriageEventId != 0)
                    options.Add(new(CasusBelli.Claim, enemy.Id, consort.MarriageEventId, world.Settlements[enemy.CapitalId].RegionId, 0, 20));
            }
        }
        return options;
    }

    private void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
    {
        var world = ctx.World;
        var defender = world.Realms[cb.Against];
        var cbEvent = cb.EventId;
        if (cb.Kind == CasusBelli.HolyWar)
            cbEvent = ctx.Emit(new EventDraft("HolyWarProclaimed", Actors: Pair(attacker, defender),
                Place: world.Settlements[attacker.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["faith"] = world.Faiths[attacker.FaithId].Name },
                Causes: cb.EventId != 0 ? new[] { new Cause(cb.EventId, CauseKind.Motive, 0.9) } : null,
                Importance: 50, Tags: new[] { "Politics", "Faith" })).Id;

        var target = world.Regions.GetValueOrDefault(cb.TargetRegionId);
        var name = cb.Kind switch
        {
            CasusBelli.HolyWar when target is not null && target.Id == world.Faiths[attacker.FaithId].HolyRegionId => $"The Holy War for {target.Name}",
            CasusBelli.HolyWar => $"The Holy War of {world.Faiths[attacker.FaithId].Deity}",
            CasusBelli.Independence => $"The {defender.Name} Rebellion",
            CasusBelli.Revenge => $"The War of {attacker.Name}'s Vengeance",
            _ => $"The {target?.Name ?? defender.Name} War",
        };
        var warId = world.Ids.Peek(War.Kind);
        var invention = _inventor.Invent(new InventionBrief("war", $"{War.Kind}#{warId}",
            $"A war of {cb.Kind} declared by {Statecraft.Title(attacker)} on {Statecraft.Title(defender)}"
            + (target is null ? "" : $" over {target.Name}") + $", in year {ctx.Date.Year}.", name));
        name = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = name,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            Attackers = { attacker.Id },
            Defenders = { defender.Id },
            CasusBelli = cb.Kind,
            CasusBelliEventId = cbEvent,
            TargetRegionId = cb.TargetRegionId,
            TargetArtifactId = cb.TargetArtifactId,
            StartDay = ctx.Date.Day,
        };
        var declared = ctx.Emit(new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = attacker.Ref, ["defender"] = defender.Ref, ["war"] = war.Ref },
            Place: world.Settlements[defender.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["casus belli"] = cb.Kind, ["war"] = name },
            Causes: WarCauses(ctx, attacker, defender, cbEvent),
            Importance: 70, Tags: new[] { "Politics", "War" }));
        war.DeclaredEventId = declared.Id;
        world.Wars[war.Id] = war;
        attacker.Treaties.RemoveAll(t => t.With == defender.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        defender.Treaties.RemoveAll(t => t.With == attacker.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        Statecraft.Remember(defender, declared.Id, attacker.Id, -50, 70);

        foreach (var (side, leader) in new[] { (war.Attackers, attacker), (war.Defenders, defender) })
            foreach (var ally in Realms.Independent(world).Where(r => r.Has(TreatyKind.Alliance, leader.Id) && !AtWar(world, r)).OrderBy(r => r.Id).ToList())
            {
                // An ally of both sides stays out rather than fight a friend.
                if (war.Involves(ally.Id) || ally.Has(TreatyKind.Alliance, attacker.Id) && ally.Has(TreatyKind.Alliance, defender.Id)
                    || !ctx.Rng.Chance(0.7)) continue;
                side.Add(ally.Id);
                ctx.Emit(new EventDraft("AllyJoinedWar",
                    Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                    Payload: new Dictionary<string, string> { ["side"] = side == war.Attackers ? "attackers" : "defenders" },
                    Place: world.Settlements[ally.CapitalId].Ref,
                    Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
            }
    }

    /// <summary>
    /// The casus belli is the motive; the realms' latest rivalry, their latest shared grievance (a peace, a ceded
    /// region, a declaration of independence) and the ruler's accession, when there are any, are enablers (at most
    /// three causes in all), so a war's why-chain reaches back past its pretext.
    /// </summary>
    private static List<Cause> WarCauses(SimContext ctx, Realm attacker, Realm defender, long cbEvent)
    {
        var causes = new List<Cause> { new(cbEvent, CauseKind.Motive, 0.9) };
        bool Cited(long id) => causes.Any(c => c.EventId == id);
        var shared = ctx.Log.Involving(attacker.Ref).Where(e => e.Actors.ContainsValue(defender.Ref)).ToList();
        var rivalry = shared.LastOrDefault(e => e.Type is "RivalryDeclared" or "AllianceBroken");
        if (rivalry is not null && !Cited(rivalry.Id)) causes.Add(new Cause(rivalry.Id, CauseKind.Enabler, 0.5));
        var grievance = shared.LastOrDefault(e => e.Type is "PeaceSigned" or "RegionCeded" or "IndependenceDeclared");
        if (grievance is not null && !Cited(grievance.Id)) causes.Add(new Cause(grievance.Id, CauseKind.Enabler, 0.4));
        if (causes.Count < 3 && Realms.RulerOf(ctx.World, attacker) is { } ruler
            && Fates.Accession(ctx.Log, ruler) is { } accession && !Cited(accession.Id))
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.3));
        return causes;
    }

    private static bool AtWarWith(World world, Realm a, Realm b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.IsAttacker(a.Id) && w.Defenders.Contains(b.Id) || w.IsAttacker(b.Id) && w.Defenders.Contains(a.Id)));

    private static bool HeldBy(World world, Artifact x, Realm realm) => x.Place switch
    {
        ArtifactPlace.Held => Realms.SovereignOf(world, world.Settlements[world.Characters[x.HolderId].SettlementId])?.Id == realm.Id,
        ArtifactPlace.Treasury => Realms.SovereignOf(world, world.Settlements[x.SettlementId])?.Id == realm.Id,
        _ => false,
    };

    /// <summary>The enemy's region nearest our territory: an enemy region adjacent to ours, lowest id.</summary>
    private static long Border(World world, Realm ours, Realm theirs) =>
        Realms.Territory(world, theirs).Where(r => Touches(world, r, ours)).Select(r => r.Id).DefaultIfEmpty(world.Settlements[theirs.CapitalId].RegionId).Min();

    private static bool Touches(World world, Region region, Realm realm) =>
        (world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).Any(n => Realms.SovereignOf(world, world.Regions[n])?.Id == realm.Id);

    private static Dictionary<string, EntityRef> Pair(Realm a, Realm b) => new() { ["a"] = a.Ref, ["b"] = b.Ref };
}
```

Replace `sim/FantasyWorld.Sim/Systems/FaithSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Faiths (sub-project 3 §4). Each year a region's people may turn to the faith pressing hardest on them: their
/// realm's faith and their neighbours' faiths, stronger where temples and shrines stand, weaker for ascetic or
/// tolerant faiths; their own temples hold them. A realm whose lands have mostly turned takes up the new faith.
/// A faith whose holy land falls to a realm of another faith grows zealous until it is regained; a zealous faith under
/// that strain, or with its realm in decline, may split when a priest founds a heresy, and nearby lands choose. Devout
/// notables of renown may be declared saints after death, and their relics, kept in their town, hold its people.
/// </summary>
public sealed class FaithSystem(IInventor? inventor = null) : ISimSystem
{
    public const double SchismZeal = 50;
    public const double SchismPerYear = 0.08;
    public const double SaintRenown = 45;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    /// <summary>At most one faith per ten regions (and at least four), so schisms stay rare.</summary>
    public static int Cap(World world) => Math.Max(4, world.Regions.Count / 10);

    public const double ConvertPerYear = 0.04;
    public const int SettleYears = 40;
    public const double RealmShare = 2.0 / 3;
    public const double LossZeal = 30;

    public string Name => "faith";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        Spread(ctx);
        RealmsConvert(ctx);
        HolySites(ctx);
        Schisms(ctx);
        Saints(ctx);
    }

    /// <summary>Temples and saints' relics count fully, shrines by half.</summary>
    public static double Devotion(World world, Region region)
    {
        var towns = world.LivingSettlements(region).ToList();
        var relics = world.Artifacts.Values.Count(a => a is { Type: ArtifactType.Relic, Place: ArtifactPlace.Treasury } && towns.Any(t => t.Id == a.SettlementId));
        return relics + towns.Sum(s => (s.Buildings.Contains("temple") ? 1 : 0) + (s.Buildings.Contains("shrine") ? 0.5 : 0));
    }

    /// <summary>Each other faith's pull on the region.</summary>
    public static SortedDictionary<long, double> Pressure(World world, Region region)
    {
        var pull = new SortedDictionary<long, double>();
        void Add(long faith, double weight)
        {
            if (faith == 0 || faith == region.FaithId || !world.Faiths.TryGetValue(faith, out var f)) return;
            if (f.Has("ascetic") || f.Has("tolerant")) weight *= 0.6;
            pull[faith] = pull.GetValueOrDefault(faith) + weight * (1 + f.Zeal / 100);
        }
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner)) Add(owner.FaithId, 3);
        foreach (var n in world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
        {
            var neighbour = world.Regions[n];
            Add(neighbour.FaithId, 1 + Devotion(world, neighbour) / 2);
        }
        return pull;
    }

    /// <summary>The faith pressing hardest on a peopled region and the yearly chance it wins them (0 if none).</summary>
    public static (long Faith, double Odds) Odds(World world, Region region)
    {
        if (region.FaithId == 0 || !world.LivingSettlements(region).Any()) return (0, 0);
        var (faith, pull) = Pressure(world, region).OrderByDescending(p => p.Value).ThenBy(p => p.Key).FirstOrDefault();
        return faith == 0 ? (0, 0) : (faith, ConvertPerYear * pull / (pull + 3 + Devotion(world, region)));
    }

    private static void Spread(SimContext ctx)
    {
        var world = ctx.World;
        var turned = new List<(Region Region, long Faith)>();
        foreach (var region in world.Regions.Values)
        {
            if (Settling(ctx, region)) continue;
            var (faith, odds) = Odds(world, region);
            if (faith != 0 && ctx.Rng.Chance(odds)) turned.Add((region, faith));
        }
        // Decided on last year's map, applied together, so the order of regions does not matter.
        foreach (var (region, faith) in turned) Convert(ctx, region, faith);
    }

    /// <summary>A people that turned within a generation keep their new faith a while.</summary>
    private static bool Settling(SimContext ctx, Region region) =>
        ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "RegionConverted") is { } turned
        && ctx.Date.Day - turned.Date.Day < SettleYears * (long)Core.WorldDate.DaysPerYear;

    private static void Convert(SimContext ctx, Region region, long faithId, Cause? because = null)
    {
        var world = ctx.World;
        var old = world.Faiths[region.FaithId];
        var faith = world.Faiths[faithId];
        var causes = new List<Cause>();
        if (because is { } b) causes.Add(b);
        var neighbours = world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>();
        var spread = neighbours.SelectMany(n => ctx.Log.Involving(world.Regions[n].Ref))
            .Where(e => e.Type == "RegionConverted" && e.Actors.GetValueOrDefault("faith") == faith.Ref)
            .OrderBy(e => e.Id).LastOrDefault();
        if (spread is not null) causes.Add(new Cause(spread.Id, CauseKind.Enabler, 0.6));
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner) && owner.FaithId == faithId)
        {
            var taken = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded").OrderBy(e => e.Id).LastOrDefault();
            causes.Add(new Cause(taken?.Id ?? owner.FoundedEventId, CauseKind.Enabler, 0.5));
        }
        region.FaithId = faithId;
        ctx.Emit(new EventDraft("RegionConverted", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["old"] = old.Ref },
            Place: region.Ref, Causes: causes.Where(c => c.EventId != 0).ToArray(), Importance: 25, Tags: new[] { "Faith" }));
    }

    private static void RealmsConvert(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            var domain = Realms.Domain(world, realm).ToList();
            if (domain.Count < 2) continue;
            var (faith, count) = domain.GroupBy(r => r.FaithId).Select(g => (g.Key, g.Count()))
                .OrderByDescending(g => g.Item2).ThenBy(g => g.Key).First();
            if (faith == realm.FaithId || faith == 0 || count < domain.Count * RealmShare || !ctx.Rng.Chance(0.2)) continue;
            var last = domain.SelectMany(r => ctx.Log.Involving(r.Ref))
                .Where(e => e.Type == "RegionConverted" && e.Actors["faith"].Id == faith).OrderBy(e => e.Id).LastOrDefault();
            realm.FaithId = faith;
            realm.Stability = Math.Max(0, realm.Stability - 10);
            if (Realms.RulerOf(world, realm) is { } ruler) ruler.FaithId = faith;
            ctx.Emit(new EventDraft("RealmConverted", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["faith"] = world.Faiths[faith].Ref },
                Place: world.Settlements[realm.CapitalId].Ref, Causes: last is null ? null : new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: 50, Tags: new[] { "Faith", "Politics" }));
        }
    }

    private static void HolySites(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values)
        {
            // Zeal cools toward the faith's temper, which runs hotter while its holy land is lost.
            var calm = (faith.Has("zealous") ? 40 : 10) + (faith.HolySiteLostEventId != 0 ? LossZeal : 0);
            faith.Zeal = Math.Max(Math.Min(faith.Zeal, calm), faith.Zeal - 3);
            if (!world.Regions.TryGetValue(faith.HolyRegionId, out var holy)) continue;
            var holder = Realms.SovereignOf(world, holy);
            var lost = holder is not null && holder.FaithId != faith.Id;
            if (lost && faith.HolySiteLostEventId == 0)
            {
                // Lost only by its cession to the holder or the holder's change of faith: a holy land never held is not a loss.
                var taken = ctx.Log.Involving(holy.Ref)
                    .Where(e => e.Type == "RegionCeded" && e.Actors.TryGetValue("to", out var to) && world.Realms.TryGetValue(to.Id, out var gainer)
                                && Realms.Top(world, gainer).Id == holder!.Id)
                    .Concat(ctx.Log.Involving(holder!.Ref).Where(e => e.Type == "RealmConverted"))
                    .OrderBy(e => e.Id).LastOrDefault();
                if (taken is null) continue;
                faith.Zeal = Math.Min(100, faith.Zeal + LossZeal);
                faith.HolySiteLostEventId = ctx.Emit(new EventDraft("HolySiteLost",
                    Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["realm"] = holder.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(taken.Id, CauseKind.Trigger, 0.8) }, Importance: 45, Tags: new[] { "Faith" })).Id;
            }
            else if (holder?.FaithId == faith.Id && faith.HolySiteLostEventId != 0)
            {
                var loss = faith.HolySiteLostEventId;
                faith.HolySiteLostEventId = 0;
                faith.Zeal = Math.Min(100, faith.Zeal + 10);
                ctx.Emit(new EventDraft("HolySiteRegained", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(loss, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            }
        }
    }

    private void Schisms(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values.Where(f => f.Zeal >= SchismZeal).ToList())
        {
            if (world.Faiths.Count >= Cap(world)) return;
            var strain = faith.HolySiteLostEventId != 0 ? faith.HolySiteLostEventId
                : world.Realms.Values.Where(r => !r.Destroyed && r.FaithId == faith.Id && r.Stability < 30)
                    .SelectMany(r => ctx.Log.Involving(r.Ref)).Where(e => e.Type == "RealmDiminished").Select(e => e.Id).DefaultIfEmpty().Max();
            if (strain == 0 || !ctx.Rng.Chance(SchismPerYear)) continue;
            var heresiarch = world.Characters.Values
                .Where(c => c.IsActive && c.Role == Role.Priest && c.FaithId == faith.Id && c.Stage(ctx.Date) != LifeStage.Child
                            && world.Settlements.TryGetValue(c.SettlementId, out var home) && !home.Abandoned)
                .OrderByDescending(c => c.Attributes.Lore).ThenBy(c => c.Id).FirstOrDefault();
            if (heresiarch is null) continue;
            Split(ctx, faith, heresiarch, strain);
        }
    }

    private void Split(SimContext ctx, Faith old, Character heresiarch, long strain)
    {
        var world = ctx.World;
        var home = world.Regions[world.Settlements[heresiarch.SettlementId].RegionId];
        var tenets = old.Tenets.ToList();
        if (tenets.Count > 0) tenets.RemoveAt(ctx.Rng.NextInt(0, tenets.Count));
        var fresh = Faith.TenetPool.Where(t => !old.Tenets.Contains(t)).ToList();
        if (fresh.Count > 0) tenets.Add(fresh[ctx.Rng.NextInt(0, fresh.Count)]);
        tenets.Sort(StringComparer.Ordinal);

        var id = world.Ids.Take(Faith.Kind);
        var fallback = old.Name.StartsWith("The True ", StringComparison.Ordinal) ? $"The Rite of {heresiarch.Name}"
            : "The True " + (old.Name.StartsWith("The ", StringComparison.Ordinal) ? old.Name[4..] : old.Name);
        var invention = _inventor.Invent(new InventionBrief("faith", $"{Faith.Kind}#{id}",
            $"A heresy of {old.Name} (worship of {old.Deity}) founded by the priest {heresiarch.Name} in {home.Name} in year {ctx.Date.Year}; tenets: {string.Join(", ", tenets)}.",
            fallback));
        using var lore = ctx.Annotate("lore", invention.Lore);
        var schism = ctx.Emit(new EventDraft("SchismBegan",
            Actors: new Dictionary<string, EntityRef> { ["heresiarch"] = heresiarch.Ref, ["faith"] = new(Faith.Kind, id), ["old"] = old.Ref },
            Place: world.Settlements[heresiarch.SettlementId].Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
            Causes: new[] { new Cause(strain, CauseKind.Motive, 0.8) }, Importance: 55, Tags: new[] { "Faith" }));
        world.Faiths[id] = new Faith
        {
            Id = id, Name = invention.Name, Deity = old.Deity, Tenets = tenets, HolyRegionId = home.Id, Zeal = 60,
            ParentFaithId = old.Id, FounderId = heresiarch.Id, FoundedEventId = schism.Id,
        };
        old.Zeal = Math.Max(0, old.Zeal - 20);
        heresiarch.FaithId = id;
        heresiarch.Renown = Math.Min(100, heresiarch.Renown + 10);

        var follow = new Cause(schism.Id, CauseKind.Trigger, 0.9);
        if (home.FaithId == old.Id) Convert(ctx, home, id, follow);
        foreach (var n in world.Geography?.Graph.Neighbours(home.Id) ?? Array.Empty<long>())
            if (world.Regions[n] is { } near && near.FaithId == old.Id && world.LivingSettlements(near).Any() && ctx.Rng.Chance(0.4))
                Convert(ctx, near, id, follow);
    }

    private static void Saints(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - Core.WorldDate.DaysPerYear;
        foreach (var c in world.Characters.Values.Where(c => c.Died is { } d && d.Day >= since && c.SaintEventId == 0 && c.Has(Traits.Devout)
                                                             && c.Renown >= SaintRenown && world.Faiths.ContainsKey(c.FaithId)).ToList())
        {
            var faith = world.Faiths[c.FaithId];
            var death = ctx.Log.Involving(c.Ref).LastOrDefault(e => e.Type == "CharacterDied");
            var town = world.Settlements.GetValueOrDefault(c.SettlementId) is { Abandoned: false } s ? s : null;
            var relicId = town is null ? 0 : world.Ids.Take(Artifact.Kind);
            var actors = new Dictionary<string, EntityRef> { ["saint"] = c.Ref, ["faith"] = faith.Ref };
            if (relicId != 0) actors["relic"] = new EntityRef(Artifact.Kind, relicId);
            var declared = ctx.Emit(new EventDraft("SaintDeclared", Actors: actors, Place: town?.Ref,
                Causes: death is null ? null : new[] { new Cause(death.Id, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            c.SaintEventId = declared.Id;
            faith.Zeal = Math.Min(100, faith.Zeal + 5);
            if (town is null) continue;
            world.Artifacts[relicId] = new Artifact
            {
                Id = relicId, Name = $"The Relics of Saint {c.Name}", Type = ArtifactType.Relic, Material = "bone", OriginEventId = declared.Id,
                Fame = Math.Min(100, c.Renown / 2), Place = ArtifactPlace.Treasury, SettlementId = town.Id,
                Powers = { new ArtifactPower("devotion", null, 1) },
                Provenance = { new ProvenanceEntry(declared.Id, c.Id, 0, "enshrined") },
            };
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["SchismBegan"] = "Schism",
        ["SaintDeclared"] = "A saint",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~FaithSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 11`. Then `dotnet test sim` → `Passed: 534` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): split zealous faiths, wage holy wars for holy lands and declare saints

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Ley tides, wild magic, mage towers and magic law

Spec §5. Each region's ley power follows a slow tide (48 years) around its base, so surges cluster at the peaks. Mage towers are built only where magic is not outlawed, and train mages from learned townsfolk. A surge in a land with a tower may go wild (a storm that kills, a blight on the fields, a portal that lets danger in), more readily where magic is embraced; realms that suffer it tighten their magic law, and realms of arcane faith or with a mage on the throne loosen it. Where magic is outlawed, mages flee to the nearest freer realm or are burned.

**Files:**
- Create: `sim/FantasyWorld.Sim/Systems/MagicSystem.cs`
- Modify: `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs`, `sim/FantasyWorld.Sim/Systems/RegionSystem.cs`, `sim/FantasyWorld.Sim/Systems/SettlementSystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/MagicSystemTests.cs`

**Interfaces:**
- Consumes: `RegionSystem` drift, `SettlementSystem` building, `MagicLaw`, `Realms`, `Fates.Die`.
- Produces: `sealed class MagicSystem : ISimSystem { TideYears = 48; TideAmplitude = 12; Tide(region, date); WildOdds(law); HasTower(s); TrainChance; MagesPerTower; FleeChance; PersecuteChance }` (after `FaithSystem`); events `WildMagic` (payload `kind`, `dead`), `MageTrained`, `MagicLawChanged` (payload `from`, `law`), `MageFled`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/MagicSystemTests.cs`:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class MagicSystemTests
{
    private static Simulator Run(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new MagicSystem() }, checkInvariants: true);

    /// <summary>Region 1's town gets a tower; a surge is under way there.</summary>
    private static (World World, EventLog Log, WorldEvent Surge) Surging(MagicLaw law)
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var realm in world.Realms.Values) realm.MagicLaw = law;
        world.Settlements[1].Buildings.Add("mage tower");
        var log = new EventLog();
        var surge = log.Append(world.Date, new EventDraft("LeySurge", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.LeySurge] = surge.Id;
        return (world, log, surge);
    }

    [Fact]
    public void LeyPower_RisesAndFallsInASlowTide()
    {
        var world = TestPeople.World();
        var region = world.Regions[1];
        var tides = Enumerable.Range(0, MagicSystem.TideYears).Select(y => MagicSystem.Tide(region, new WorldDate(y * 360L))).ToList();
        Assert.InRange(tides.Max() - tides.Min(), MagicSystem.TideAmplitude, MagicSystem.TideAmplitude * 2 + 0.01);
        Assert.Equal(MagicSystem.Tide(region, new WorldDate(0)), MagicSystem.Tide(region, new WorldDate(MagicSystem.TideYears * 360L)), 6);
        Assert.NotEqual(MagicSystem.Tide(world.Regions[1], new WorldDate(0)), MagicSystem.Tide(world.Regions[2], new WorldDate(0)));
    }

    [Fact]
    public void ASurgeAtATower_GoesWild_AndTheRealmTightensItsLaw()
    {
        var (world, log, surge) = Surging(MagicLaw.Embraced);
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 30);
        var wild = sim.Log.All.First(e => e.Type == "WildMagic");
        Assert.Equal(world.Settlements[1].Ref, wild.Place);
        Assert.Contains(wild.Causes, c => c.EventId == surge.Id);
        Assert.Contains(wild.Payload["kind"], new[] { "storm", "blight", "portal" });
        var law = sim.Log.All.First(e => e.Type == "MagicLawChanged");
        Assert.Contains(law.Causes, c => c.EventId == wild.Id || sim.Log.Get(c.EventId).Type == "WildMagic");
        Assert.NotEqual(MagicLaw.Embraced, world.Realms[1].MagicLaw);
    }

    [Fact]
    public void WildMagic_IsRarerWhereMagicIsLicensed()
    {
        Assert.True(MagicSystem.WildOdds(MagicLaw.Licensed) < MagicSystem.WildOdds(MagicLaw.Embraced));
        Assert.True(MagicSystem.WildOdds(MagicLaw.Outlawed) < MagicSystem.WildOdds(MagicLaw.Licensed));
    }

    [Fact]
    public void ATower_TrainsMages_WhereTheLawAllows()
    {
        var world = TestRealms.TwoKingdoms();
        world.Settlements[1].Buildings.Add("mage tower");
        var pupil = TestPeople.Add(world, 1, 25);
        pupil.Attributes.Lore = 12;
        var sim = Run(world);
        sim.AdvanceDays(360 * 20);
        var trained = sim.Log.All.First(e => e.Type == "MageTrained");
        Assert.Equal(pupil.Ref, trained.Actors["mage"]);
        Assert.Equal(Role.Mage, pupil.Role);

        var banned = TestRealms.TwoKingdoms();
        banned.Realms[1].MagicLaw = MagicLaw.Outlawed;
        banned.Settlements[1].Buildings.Add("mage tower");
        TestPeople.Add(banned, 1, 25).Attributes.Lore = 12;
        var none = Run(banned);
        none.AdvanceDays(360 * 20);
        Assert.DoesNotContain(none.Log.All, e => e.Type == "MageTrained");
    }

    [Fact]
    public void WhereMagicIsOutlawed_MagesFleeToAFreerRealm()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Realms[1].MagicLaw = MagicLaw.Outlawed;
        world.Realms[2].MagicLaw = MagicLaw.Licensed;
        var banned = log.Append(world.Date, new EventDraft("MagicLawChanged", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref }));
        var mage = TestPeople.Add(world, 1, 30, role: Role.Mage);
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 40);
        var fate = sim.Log.All.First(e => e.Type == "MageFled" || e.Type == "CharacterDied" && e.Actors["deceased"] == mage.Ref);
        Assert.Contains(fate.Causes, c => c.EventId == banned.Id);
        if (fate.Type == "MageFled") Assert.Equal(2, mage.SettlementId);
        else Assert.Equal("burned for sorcery", mage.DeathCause);
    }

    [Fact]
    public void TowersAreNotBuilt_WhereMagicIsOutlawed()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].MagicLaw = MagicLaw.Outlawed;
        var town = world.Settlements[1];
        (town.Tier, town.Wealth, town.Walls) = (SettlementTier.City, 1_000_000, 3);
        town.Buildings.AddRange(new[] { "granary", "guildhall", "market", "temple" });
        world.Regions[1].LeyPower = 90;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new SettlementSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 3);
        Assert.DoesNotContain("mage tower", town.Buildings);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0246: The type or namespace name 'MagicSystem' could not be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["WildMagic"] = "Wild magic broke loose at {@place}: a {=kind}.",
        ["MageTrained"] = "{mage} was trained in the mage tower of {@place}.",
        ["MagicLawChanged"] = "{realm} changed its law on magic from {=from} to {=law}.",
        ["MageFled"] = "{mage} fled the law against magic to {@place}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Systems/DefaultSystems.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new FaithSystem(inventor),
        new MagicSystem(),
        new MonsterSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}
```

`sim/FantasyWorld.Sim/Systems/MagicSystem.cs`:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Magic (sub-project 3 §5). Each region's ley power follows a slow tide around its base (see <see cref="Tide"/>, used by
/// <see cref="RegionSystem"/>), so surges cluster at the peaks. Mage towers train mages where the law allows. A surge in
/// a land with a tower may go wild (a storm, a blight or a portal that lets danger in), the more readily where magic is
/// embraced; realms that suffer it tighten their magic law, and arcane realms loosen it. Where magic is outlawed, mages
/// flee to freer realms or are put to death.
/// </summary>
public sealed class MagicSystem : ISimSystem
{
    public const int TideYears = 48;
    public const double TideAmplitude = 12;
    public const double TrainChance = 0.3;
    public const int MagesPerTower = 2;
    public const double FleeChance = 0.25;
    public const double PersecuteChance = 0.05;

    public string Name => "magic";

    /// <summary>Where a region's ley power is drawn at a date: its base, raised and lowered by a tide of its own phase.</summary>
    public static double Tide(Region region, WorldDate date)
    {
        var phase = region.Id * 7919 % TideYears;
        var years = date.Day / (double)WorldDate.DaysPerYear + phase;
        return Math.Clamp(region.BaseLey + TideAmplitude * Math.Sin(2 * Math.PI * years / TideYears), 0, 100);
    }

    /// <summary>Yearly odds that a surge goes wild in a land with a tower, by the law of the realm that holds it.</summary>
    public static double WildOdds(MagicLaw? law) => law switch
    {
        MagicLaw.Embraced => 0.25,
        MagicLaw.Licensed => 0.08,
        MagicLaw.Outlawed => 0.03,
        _ => 0.15,
    };

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        WildMagic(ctx);
        Train(ctx);
        Laws(ctx);
        Persecute(ctx);
    }

    public static bool HasTower(Settlement s) => !s.Abandoned && s.Buildings.Contains("mage tower");

    private static void WildMagic(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var region in world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.LeySurge)).ToList())
        {
            var tower = world.LivingSettlements(region).Where(HasTower).OrderBy(s => s.Id).FirstOrDefault();
            if (tower is null) continue;
            var law = Realms.SovereignOf(world, region)?.MagicLaw;
            if (!ctx.Rng.Chance(WildOdds(law))) continue;
            var kind = new[] { "storm", "blight", "portal" }[ctx.Rng.NextInt(0, 3)];
            var dead = 0;
            switch (kind)
            {
                case "storm":
                    foreach (var s in world.LivingSettlements(region))
                    {
                        var before = s.Population;
                        Demography.Kill(s, 0.03, ctx.Rng);
                        dead += before - s.Population;
                    }
                    region.Food *= 0.7;
                    break;
                case "blight":
                    region.Fertility = Math.Max(0, region.Fertility - 20);
                    break;
                default:
                    region.Danger = Math.Min(100, region.Danger + 25);
                    break;
            }
            var built = ctx.Log.Involving(tower.Ref).LastOrDefault(e => e.Type == "BuildingCompleted" && e.Payload.GetValueOrDefault("building") == "mage tower");
            var causes = new List<Cause> { new(region.Conditions[Conditions.LeySurge], CauseKind.Trigger, 0.9) };
            if (built is not null) causes.Add(new Cause(built.Id, CauseKind.Enabler, 0.5));
            ctx.Emit(new EventDraft("WildMagic", Place: tower.Ref,
                Payload: new Dictionary<string, string> { ["kind"] = kind, ["dead"] = dead.ToString(CultureInfo.InvariantCulture) },
                Causes: causes, Importance: 40, Tags: new[] { "Magic" }));
        }
    }

    private static void Train(SimContext ctx)
    {
        var world = ctx.World;
        var notables = world.ActiveCharacters.ToList();
        foreach (var tower in world.Settlements.Values.Where(HasTower).ToList())
        {
            if (Realms.SovereignOf(world, world.Regions[tower.RegionId])?.MagicLaw == MagicLaw.Outlawed) continue;
            var here = notables.Where(c => c.SettlementId == tower.Id).ToList();
            if (here.Count(c => c.Role == Role.Mage) >= MagesPerTower || !ctx.Rng.Chance(TrainChance)) continue;
            var pupil = here.Where(c => c.Role == Role.None && c.Stage(ctx.Date) == LifeStage.Adult && c.Attributes.Lore >= 8)
                .OrderByDescending(c => c.Attributes.Lore).ThenBy(c => c.Id).FirstOrDefault();
            if (pupil is null) continue;
            pupil.Role = Role.Mage;
            pupil.Attributes.Lore = Math.Min(20, pupil.Attributes.Lore + 3);
            var built = ctx.Log.Involving(tower.Ref).LastOrDefault(e => e.Type == "BuildingCompleted" && e.Payload.GetValueOrDefault("building") == "mage tower");
            ctx.Emit(new EventDraft("MageTrained", Actors: new Dictionary<string, EntityRef> { ["mage"] = pupil.Ref }, Place: tower.Ref,
                Causes: built is null ? null : new[] { new Cause(built.Id, CauseKind.Enabler, 0.6) },
                Importance: Fates.Importance(pupil, 15), Tags: new[] { "Magic", "Characters" }));
        }
    }

    private static void Laws(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        foreach (var realm in Realms.Independent(world).ToList())
        {
            // Wild magic breaks loose at towers, so the year's outbreaks are found through the realm's towers.
            var wild = Realms.Territory(world, realm).SelectMany(world.LivingSettlements).Where(HasTower)
                .Select(t => ctx.Log.Involving(t.Ref).LastOrDefault(e => e.Type == "WildMagic")?.Id ?? 0)
                .Where(id => id != 0 && ctx.Log.Get(id).Date.Day >= since).DefaultIfEmpty().Max();
            var arcane = world.Faiths.GetValueOrDefault(realm.FaithId)?.Has("arcane") == true || Realms.RulerOf(world, realm)?.Role == Role.Mage;
            if (wild != 0 && realm.MagicLaw != MagicLaw.Outlawed && !arcane && ctx.Rng.Chance(0.5))
                Change(ctx, realm, realm.MagicLaw + 1, new Cause(wild, CauseKind.Trigger, 0.9));
            else if (wild == 0 && arcane && realm.MagicLaw != MagicLaw.Embraced && ctx.Rng.Chance(0.05))
            {
                var why = ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type is "RealmConverted" or "SuccessionOccurred");
                Change(ctx, realm, realm.MagicLaw - 1, why is null ? null : new Cause(why.Id, CauseKind.Enabler, 0.6));
            }
        }
    }

    private static void Change(SimContext ctx, Realm realm, MagicLaw law, Cause? cause)
    {
        var from = realm.MagicLaw;
        realm.MagicLaw = law;
        ctx.Emit(new EventDraft("MagicLawChanged", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["from"] = from.ToString().ToLowerInvariant(), ["law"] = law.ToString().ToLowerInvariant() },
            Causes: cause is { } c ? new[] { c } : null, Importance: 35, Tags: new[] { "Magic", "Politics" }));
    }

    private static void Persecute(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var mage in world.ActiveCharacters.Where(c => c.Role == Role.Mage).ToList())
        {
            var home = world.Settlements[mage.SettlementId];
            var realm = Realms.SovereignOf(world, world.Regions[home.RegionId]);
            if (realm?.MagicLaw != MagicLaw.Outlawed) continue;
            var law = ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type == "MagicLawChanged");
            var because = law is null ? Array.Empty<Cause>() : new[] { new Cause(law.Id, CauseKind.Enabler, 0.8) };
            if (ctx.Rng.Chance(FleeChance) && Refuge(world, home) is { } refuge)
            {
                mage.SettlementId = refuge.Id;
                ctx.Emit(new EventDraft("MageFled", Actors: new Dictionary<string, EntityRef> { ["mage"] = mage.Ref, ["from"] = home.Ref },
                    Place: refuge.Ref, Causes: because, Importance: Fates.Importance(mage, 20), Tags: new[] { "Magic", "Characters" }));
            }
            else if (ctx.Rng.Chance(PersecuteChance))
                Fates.Die(ctx, mage, "burned for sorcery", because);
        }
    }

    /// <summary>The nearest living town in a realm that allows magic, by the region graph.</summary>
    private static Settlement? Refuge(World world, Settlement home)
    {
        var graph = world.Geography?.Graph;
        if (graph is null) return null;
        return world.Settlements.Values
            .Where(s => !s.Abandoned && Realms.SovereignOf(world, world.Regions[s.RegionId])?.MagicLaw is MagicLaw.Embraced or MagicLaw.Licensed)
            .Select(s => (Town: s, Way: s.RegionId == home.RegionId ? 0 : graph.ShortestPath(home.RegionId, s.RegionId)?.Days ?? double.MaxValue))
            .Where(x => x.Way <= 60)
            .OrderBy(x => x.Way).ThenByDescending(x => x.Town.Tier).ThenBy(x => x.Town.Id)
            .Select(x => x.Town).FirstOrDefault();
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/RegionSystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Yearly odds of each region event (rolled monthly as odds / 12). Plague spread odds are per month
/// per link. Tests pass custom odds to force or silence events.
/// </summary>
public sealed record RegionOdds(
    double Drought = 0.06,
    double Flood = 0.05,
    double Blight = 0.02,
    double Plague = 0.008,
    double LeySurge = 0.02,
    double Earthquake = 0.01,
    double Monsters = 0.08,
    double Discovery = 0.015,
    double PlagueSpreadLand = 0.04,
    double PlagueSpreadSea = 0.02)
{
    public static readonly RegionOdds None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The land itself: fertility, danger and ley power drift; droughts, floods, blights, plagues,
/// ley surges, earthquakes, monster migrations and discoveries happen, each citing what enabled it.
/// Also records each region's defining events in Region.Memory.
/// </summary>
public sealed class RegionSystem : ISimSystem
{
    public const string ConditionEndKind = "region.condition.end";
    public const string MemoryCursor = "regions.memoryCursor";
    public const int MemorySize = 12;
    public const int MemoryImportance = 35;
    public const int ImmunityDays = 10 * 360;

    private readonly RegionOdds _odds;

    public RegionSystem(RegionOdds? odds = null) => _odds = odds ?? new RegionOdds();

    public string Name => "regions";

    public IEnumerable<string> HandledKinds => new[] { ConditionEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var plagued = world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.Plague)).ToList();
        foreach (var region in world.Regions.Values)
        {
            var settlements = world.LivingSettlements(region).ToList();
            Drift(ctx, region, settlements);
            RollEvents(ctx, region, settlements);
            if (region.Conditions.ContainsKey(Conditions.Plague))
                foreach (var s in settlements) Demography.Kill(s, 0.015, ctx.Rng);
        }
        SpreadPlague(ctx, plagued);
        Remember(ctx);
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var region = ctx.World.Regions[long.Parse(action.Data["region"], CultureInfo.InvariantCulture)];
        var condition = action.Data["condition"];
        var began = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!region.Conditions.TryGetValue(condition, out var current) || current != began) return;
        region.Conditions.Remove(condition);
        if (action.Data["end"] is not { Length: > 0 } endType) return;
        var ended = ctx.Emit(new EventDraft(endType, Place: region.Ref,
            Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: 10, Tags: new[] { "Regions" }));
        if (condition == Conditions.Plague) Hold(ctx, region, Conditions.Immune, ended.Id, ImmunityDays);
    }

    private static void Drift(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var farmers = settlements.Sum(Demography.Farmers);
        var capacity = Demography.FarmCapacity(region);
        region.Fertility = capacity > 0 && farmers > capacity * 0.9
            ? Math.Max(0, region.Fertility - 0.2)
            : Math.Clamp(region.Fertility + (region.BaseFertility - region.Fertility) * 0.02, 0, 100);

        var population = settlements.Sum(s => s.Population);
        var dangerTarget = Math.Max(0, region.BaseDanger - Math.Min(20, population / 500.0));
        region.Danger = Math.Clamp(region.Danger + (dangerTarget - region.Danger) * 0.03, 0, 100);
        region.LeyPower = Math.Clamp(region.LeyPower + (MagicSystem.Tide(region, ctx.Date) - region.LeyPower) * 0.01, 0, 100);

        Threshold(ctx, region, Conditions.Exhausted,
            begins: region.Fertility < region.BaseFertility * 0.7, ends: region.Fertility >= region.BaseFertility * 0.85,
            "FertilityExhausted", "FertilityRecovered", importance: 25, Array.Empty<Cause>());
        Threshold(ctx, region, Conditions.Dangerous,
            begins: region.Danger >= 70, ends: region.Danger < 50,
            "DangerRose", "DangerSubsided", importance: 30,
            Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.7)));
    }

    /// <summary>Condition events for a value crossing a threshold, with hysteresis.</summary>
    private static void Threshold(SimContext ctx, Region region, string condition, bool begins, bool ends,
        string beginType, string endType, int importance, IReadOnlyList<Cause> causes)
    {
        var active = region.Conditions.TryGetValue(condition, out var began);
        if (!active && begins)
            region.Conditions[condition] = ctx.Emit(new EventDraft(beginType, Place: region.Ref, Causes: causes,
                Importance: importance, Tags: new[] { "Regions" })).Id;
        else if (active && ends)
        {
            ctx.Emit(new EventDraft(endType, Place: region.Ref, Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) },
                Importance: 10, Tags: new[] { "Regions" }));
            region.Conditions.Remove(condition);
        }
    }

    private void RollEvents(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var season = ctx.Date.Season;
        var c = region.Conditions;

        var droughtOdds = _odds.Drought
                          * region.Climate switch { ClimateBand.Hot => 1.5, ClimateBand.Cold => 0.5, _ => 1.0 }
                          * (region.Biome is Biome.Swamp or Biome.Jungle ? 0.3 : 1.0);
        if (season == Season.Summer && !c.ContainsKey(Conditions.Drought) && Roll(ctx, droughtOdds))
        {
            var autumnEnds = WorldDate.FromYmd(ctx.Date.Year, 10, 1);
            Begin(ctx, region, Conditions.Drought, "Drought", 35, autumnEnds.Day - ctx.Date.Day, "DroughtEnded");
        }

        if (season == Season.Spring && region.HasRiver && !c.ContainsKey(Conditions.Flood) && Roll(ctx, _odds.Flood))
        {
            region.Food *= 0.75;
            region.Fertility = Math.Min(100, region.Fertility + 3);
            foreach (var s in settlements) Demography.Kill(s, 0.005, ctx.Rng);
            Begin(ctx, region, Conditions.Flood, "Flood", 30, 120);
        }

        if (ctx.Date.Month is >= 2 and <= 8 && region.Farmland > 0 && !c.ContainsKey(Conditions.Blight) && Roll(ctx, _odds.Blight))
            Begin(ctx, region, Conditions.Blight, "Blight", 25, 240, "BlightEnded");

        var population = settlements.Sum(s => s.Population);
        var plagueOdds = _odds.Plague * (1 + population / 10_000.0) * (c.ContainsKey(Conditions.Shortage) ? 2 : 1);
        if (!c.ContainsKey(Conditions.Plague) && !c.ContainsKey(Conditions.Immune) && Roll(ctx, plagueOdds))
            Begin(ctx, region, Conditions.Plague, "PlagueOutbreak", 50, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                Conditions.Cite(c, (Conditions.Shortage, CauseKind.Enabler, 0.6)));

        if (!c.ContainsKey(Conditions.LeySurge) && Roll(ctx, _odds.LeySurge * region.LeyPower / 50))
        {
            region.LeyPower = Math.Min(100, region.LeyPower + 20);
            region.Danger = Math.Min(100, region.Danger + 10);
            Begin(ctx, region, Conditions.LeySurge, "LeySurge", 30, 720);
        }

        if (region.MountainShare >= 0.05) Earthquake(ctx, region, settlements);
        if (region.Danger >= 65 && Roll(ctx, _odds.Monsters)) Monsters(ctx, region);
        if (Roll(ctx, _odds.Discovery)) Discovery(ctx, region);
    }

    private void Earthquake(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var surge = new[] { region }.Concat(Neighbours(ctx, region))
            .Select(r => r.Conditions.TryGetValue(Conditions.LeySurge, out var id) ? id : 0)
            .FirstOrDefault(id => id > 0);
        if (!Roll(ctx, _odds.Earthquake * (surge > 0 ? 5 : 1))) return;
        foreach (var s in settlements)
        {
            s.Walls = Math.Max(0, s.Walls - 1);
            Demography.Kill(s, 0.01, ctx.Rng);
        }
        region.Food *= 0.9;
        ctx.Emit(new EventDraft("Earthquake", Place: region.Ref,
            Causes: surge > 0 ? new[] { new Cause(surge, CauseKind.Trigger, 0.8) } : null,
            Importance: 40, Tags: new[] { "Regions" }));
    }

    private static void Monsters(SimContext ctx, Region region)
    {
        var target = Neighbours(ctx, region, LinkKind.Land).OrderBy(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (target is null) return;
        region.Danger = Math.Max(0, region.Danger - 15);
        target.Danger = Math.Min(100, target.Danger + 20);
        ctx.Emit(new EventDraft("MonstersMigrated",
            Actors: new Dictionary<string, EntityRef> { ["from"] = region.Ref },
            Place: target.Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Trigger, 0.8)),
            Importance: 35, Tags: new[] { "Regions" }));
    }

    private static void Discovery(SimContext ctx, Region region)
    {
        string kind;
        double capacity;
        if (region.LeyPower >= 60 && ctx.Rng.Chance(0.3)) (kind, capacity) = (ResourceKinds.ArcaneCrystal, region.LeyPower / 10);
        else if (region.MountainShare >= 0.1 && ctx.Rng.Chance(0.2)) (kind, capacity) = (ResourceKinds.Mithril, 3);
        else
        {
            var common = region.Resources.Keys.Where(k => !ResourceKinds.IsRare(k) && k != ResourceKinds.Grain).ToList();
            kind = common.Count > 0 ? common[ctx.Rng.NextInt(0, common.Count)] : ResourceKinds.Stone;
            capacity = region.Resources.TryGetValue(kind, out var existing) ? existing.Capacity * 0.5 : 5;
        }
        var deposit = region.Resources.TryGetValue(kind, out var d) ? d : region.Resources[kind] = new ResourceDeposit();
        deposit.Capacity = Math.Round(deposit.Capacity + capacity, 1);
        deposit.Yield = Math.Round(deposit.Yield + capacity, 1);
        ctx.Emit(new EventDraft("ResourceDiscovered", Place: region.Ref,
            Payload: new Dictionary<string, string> { ["resource"] = kind },
            Importance: ResourceKinds.IsRare(kind) ? 45 : 20, Tags: new[] { "Regions", "Economy" }));
    }

    private void SpreadPlague(SimContext ctx, IReadOnlyList<Region> plagued)
    {
        foreach (var source in plagued)
        {
            if (!source.Conditions.TryGetValue(Conditions.Plague, out var outbreak)) continue;
            foreach (var link in ctx.World.Geography?.Graph.LinksOf(source.Id) ?? Array.Empty<RegionLink>())
            {
                var target = ctx.World.Regions[link.Other(source.Id)];
                var odds = link.Kind == LinkKind.Sea ? _odds.PlagueSpreadSea : _odds.PlagueSpreadLand;
                if (target.Conditions.ContainsKey(Conditions.Plague) || target.Conditions.ContainsKey(Conditions.Immune)
                    || !ctx.Rng.Chance(odds)) continue;
                Begin(ctx, target, Conditions.Plague, "PlagueSpread", 45, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                    new[] { new Cause(outbreak, CauseKind.Trigger, 1.0) },
                    new Dictionary<string, EntityRef> { ["from"] = source.Ref });
            }
        }
    }

    /// <summary>Appends recent important events that happened in a region to its memory.</summary>
    private static void Remember(SimContext ctx)
    {
        var cursor = ctx.World.Markers.GetValueOrDefault(MemoryCursor, 0);
        var all = ctx.Log.All;
        // Ids increase along the log, so binary-search the first unscanned event instead of walking history.
        int lo = 0, hi = all.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (all[mid].Id <= cursor) lo = mid + 1; else hi = mid;
        }
        for (var i = lo; i < all.Count; i++)
        {
            var ev = all[i];
            if (ev.Importance < MemoryImportance || ev.Place is not { Kind: Region.Kind } place) continue;
            if (!ctx.World.Regions.TryGetValue(place.Id, out var region)) continue;
            region.Memory.Add(ev.Id);
            if (region.Memory.Count > MemorySize) region.Memory.RemoveAt(0);
        }
        if (ctx.Log.Count > 0) ctx.World.Markers[MemoryCursor] = ctx.Log.NextId - 1;
    }

    private static WorldEvent Begin(SimContext ctx, Region region, string condition, string type, int importance,
        long days, string endType = "", IReadOnlyList<Cause>? causes = null, IReadOnlyDictionary<string, EntityRef>? actors = null)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: actors, Place: region.Ref, Causes: causes,
            Importance: importance, Tags: new[] { "Regions" }));
        Hold(ctx, region, condition, ev.Id, days, endType);
        return ev;
    }

    /// <summary>Sets a condition caused by eventId and schedules its end (with an end event if endType is set).</summary>
    private static void Hold(SimContext ctx, Region region, string condition, long eventId, long days, string endType = "")
    {
        region.Conditions[condition] = eventId;
        ctx.Queue.Schedule(ctx.Date.AddDays(days), ConditionEndKind, new Dictionary<string, string>
        {
            ["region"] = region.Id.ToString(CultureInfo.InvariantCulture),
            ["condition"] = condition,
            ["event"] = eventId.ToString(CultureInfo.InvariantCulture),
            ["end"] = endType,
        });
    }

    private static IEnumerable<Region> Neighbours(SimContext ctx, Region region, LinkKind? kind = null) =>
        (ctx.World.Geography?.Graph.LinksOf(region.Id) ?? Array.Empty<RegionLink>())
        .Where(l => kind is null || l.Kind == kind)
        .Select(l => ctx.World.Regions[l.Other(region.Id)]);

    private static bool Roll(SimContext ctx, double perYear) => perYear > 0 && ctx.Rng.Chance(perYear / 12);
}
```

Replace `sim/FantasyWorld.Sim/Systems/SettlementSystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The populace's mood and fortunes: unrest follows hunger, plague and danger; high unrest starts
/// revolts and drives people to calmer neighbours; settlements grow, decline, build and die out.
/// </summary>
public sealed class SettlementSystem : ISimSystem
{
    public const string RevoltEndKind = "settlement.revolt.end";
    public const double RevoltAt = 60;
    public const double EmigrateAt = 50;
    public const int AbandonBelow = 15;

    private static readonly (string Name, SettlementTier MinTier, double Cost)[] BuildOrder =
    {
        ("granary", SettlementTier.Village, 300),
        ("market", SettlementTier.Town, 1500),
        ("temple", SettlementTier.Town, 2500),
        ("guildhall", SettlementTier.City, 8000),
        ("mage tower", SettlementTier.City, 12_000),
    };

    /// <summary>Walls are a level, not a building: towns raise them to 2, cities to 3, and rebuild after quakes.</summary>
    public const double WallCost = 4000;

    private readonly double _revoltChance;

    public SettlementSystem(double revoltChancePerMonth = 0.35) => _revoltChance = revoltChancePerMonth;

    public string Name => "settlements";

    public IEnumerable<string> HandledKinds => new[] { RevoltEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                UpdateUnrest(ctx, region, s);
                if (s.Unrest >= RevoltAt && !s.Conditions.ContainsKey(Conditions.Revolt) && ctx.Rng.Chance(_revoltChance))
                    BeginRevolt(ctx, region, s);
                Build(ctx, region, s);
            }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                Emigrate(ctx, region, s);
                ChangeTier(ctx, region, s);
                if (s.Population < AbandonBelow) Abandon(ctx, region, s);
            }
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var s = ctx.World.Settlements[long.Parse(action.Data["settlement"], CultureInfo.InvariantCulture)];
        var revolt = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!s.Conditions.TryGetValue(Conditions.Revolt, out var current) || current != revolt) return;
        s.Conditions.Remove(Conditions.Revolt);
        if (s.Abandoned) return;
        Demography.Kill(s, 0.02, ctx.Rng);
        s.Wealth *= 0.7;
        s.Unrest = 30;
        ctx.Emit(new EventDraft("RevoltEnded", Place: s.Ref,
            Causes: new[] { new Cause(revolt, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "Politics" }));
    }

    private static void UpdateUnrest(SimContext ctx, Region region, Settlement s)
    {
        var target = 10 + region.Danger * 0.2;
        if (region.Conditions.ContainsKey(Conditions.Shortage)) target += 50;
        if (region.Conditions.ContainsKey(Conditions.Plague)) target += 15;
        if (s.Walls == 0 && region.Danger > 60) target += 5;
        s.Unrest = Math.Clamp(s.Unrest + (target - s.Unrest) * 0.25 + ctx.Rng.NextInt(-3, 4), 0, 100);
    }

    private static void BeginRevolt(SimContext ctx, Region region, Settlement s)
    {
        var revolt = ctx.Emit(new EventDraft("RevoltBegan",
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Place: s.Ref,
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.9),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.4)),
            Importance: s.Tier switch
            {
                SettlementTier.City => 60,
                SettlementTier.Town => 45,
                SettlementTier.Village => 30,
                _ => 20,
            },
            Tags: new[] { "Politics" }));
        s.Conditions[Conditions.Revolt] = revolt.Id;
        ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(60, 151)), RevoltEndKind, new Dictionary<string, string>
        {
            ["settlement"] = s.Id.ToString(CultureInfo.InvariantCulture),
            ["event"] = revolt.Id.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static void Build(SimContext ctx, Region region, Settlement s)
    {
        var maxWalls = s.Tier switch { SettlementTier.City => 3, SettlementTier.Town => 2, _ => 0 };
        if (s.Walls < maxWalls && s.Wealth >= WallCost)
        {
            s.Wealth -= WallCost;
            s.Walls++;
            ctx.Emit(new EventDraft("WallsRaised", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["level"] = s.Walls.ToString(CultureInfo.InvariantCulture) },
                Importance: 15, Tags: new[] { "Settlements" }));
            return;
        }
        foreach (var (name, minTier, cost) in BuildOrder)
        {
            if (s.Buildings.Contains(name) || s.Tier < minTier) continue;
            if (name == "mage tower" && (region.LeyPower < 50 || Realms.SovereignOf(ctx.World, region)?.MagicLaw == MagicLaw.Outlawed)) continue;
            if (s.Wealth < cost) return;
            s.Wealth -= cost;
            s.Buildings.Add(name);
            s.Buildings.Sort(StringComparer.Ordinal);
            ctx.Emit(new EventDraft("BuildingCompleted", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["building"] = name },
                Importance: name == "mage tower" ? 30 : 10, Tags: new[] { "Settlements" }));
            return;
        }
    }

    /// <summary>Unhappy or hungry settlements lose 4% of their families to the calmest settlement nearby.</summary>
    private static void Emigrate(SimContext ctx, Region region, Settlement s)
    {
        var hungry = region.Conditions.ContainsKey(Conditions.Shortage);
        if (s.Unrest < EmigrateAt && !hungry) return;

        var nearby = new[] { region.Id }.Concat(ctx.World.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>());
        var destination = nearby
            .Select(id => ctx.World.Regions[id])
            .Where(r => !r.Conditions.ContainsKey(Conditions.Shortage))
            .SelectMany(r => ctx.World.LivingSettlements(r))
            .Where(d => d.Id != s.Id && d.Unrest < s.Unrest - 10)
            .OrderBy(d => d.Unrest).ThenByDescending(d => d.Tier).ThenBy(d => d.Id)
            .FirstOrDefault();
        if (destination is null) return;

        var children = Demography.RoundRandom(s.Children * 0.04, ctx.Rng);
        var adults = Demography.RoundRandom(s.Adults * 0.04, ctx.Rng);
        s.Children -= children;
        s.Adults -= adults;
        destination.Children += children;
        destination.Adults += adults;
        if (children + adults < 100) return;

        ctx.Emit(new EventDraft("Exodus",
            Actors: new Dictionary<string, EntityRef> { ["to"] = destination.Ref },
            Place: s.Ref,
            Payload: new Dictionary<string, string> { ["people"] = (children + adults).ToString(CultureInfo.InvariantCulture) },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Shortage, CauseKind.Trigger, 0.8))
                .Concat(Conditions.Cite(s.Conditions, (Conditions.Revolt, CauseKind.Enabler, 0.6))).ToList(),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    /// <summary>Grow at the next tier's threshold; decline below 75% of the current tier's.</summary>
    private static void ChangeTier(SimContext ctx, Region region, Settlement s)
    {
        var grown = Demography.TierFor(s.Population);
        if (grown > s.Tier)
        {
            s.Tier = grown;
            ctx.Emit(new EventDraft("SettlementGrew", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["tier"] = grown.ToString() },
                Importance: grown switch { SettlementTier.City => 50, SettlementTier.Town => 30, _ => 15 },
                Tags: new[] { "Settlements" }));
            return;
        }
        if (s.Tier == SettlementTier.Hamlet || s.Population >= Threshold(s.Tier) * 0.75) return;
        s.Tier -= 1;
        ctx.Emit(new EventDraft("SettlementDeclined", Place: s.Ref,
            Payload: new Dictionary<string, string> { ["tier"] = s.Tier.ToString() },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Enabler, 0.7), (Conditions.Plague, CauseKind.Enabler, 0.6)),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    private static void Abandon(SimContext ctx, Region region, Settlement s)
    {
        s.Abandoned = true;
        s.Children = s.Adults = s.Elders = 0;
        ctx.Emit(new EventDraft("SettlementAbandoned", Place: s.Ref,
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.8),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
            Importance: 30, Tags: new[] { "Settlements" }));
    }

    private static int Threshold(SettlementTier tier) => tier switch
    {
        SettlementTier.City => 6000,
        SettlementTier.Town => 1500,
        SettlementTier.Village => 300,
        _ => 0,
    };
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["WildMagic"] = "Wild magic",
        ["MagicLawChanged"] = "Magic law",
        ["SchismBegan"] = "Schism",
        ["SaintDeclared"] = "A saint",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~MagicSystemTests"`
Expected: `Passed!  - Failed: 0, Passed: 6`. Then `dotnet test sim` → `Passed: 540` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): let ley power ebb in tides, magic go wild and realms make laws about it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Great works and wars of rivalry and glory

Spec §6. A landmark now has an architect (the town's most learned notable), is held up while war, plague or famine is upon the town (said once per hindrance), and goes faster but may collapse when an ambitious or wrathful patron rushes it (people die, half the work is lost, the architect is blamed); its completion brings renown to patron and architect and stability to the realm. Towns that have known siege raise fortresses, which add to their defence, and lords restore fallen works. With this history some worlds went a century without a war, because wars came only from grievances that never arose; two causes are added: declared rivals have a border dispute, and a ruler who craves renown seeks glory against a weaker neighbour (citing their accession).

**Files:**
- Modify: `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`, `sim/FantasyWorld.Sim/Model/Structure.cs`, `sim/FantasyWorld.Sim/Model/War.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`, `sim/FantasyWorld.Sim/Systems/MilitarySystem.Sieges.cs`, `sim/FantasyWorld.Sim/Systems/StructureSystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/GreatWorksTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Systems/DiplomacySystemTests.cs`

**Interfaces:**
- Consumes: `StructureSystem`, `MilitarySystem.Sieges`, `DiplomacySystem.CasusBelliFor`, `Fates.Accession`.
- Produces: `Structure { ArchitectId; Rushed; DelayedByEventId }`; `StructureSystem { RushedCollapse; FortressPerYear; FortressWealth; FortressDefence = 400; RestorePerYear; Fortified(world, s); Architect(world, seat, today) }`; `CasusBelli.BorderDispute`, `CasusBelli.Glory`; events `WorksDelayed`, `WorksCollapsed`, `FortificationBegun`, `FortificationCompleted`, `RestorationBegun`.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Systems/DiplomacySystemTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class DiplomacySystemTests
{
    private sealed class Choose(string prefix) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(prefix, StringComparison.Ordinal)) ?? brief.Options[^1];
    }

    private static Simulator Make(World world, EventLog log, string choice = "peace") =>
        new(world, log, new EventQueue(), new ISimSystem[] { new DiplomacySystem(new Choose(choice)) }, checkInvariants: true);

    private static WorldEvent Grudge(World world, EventLog log, long realm, long about, double feeling = -80)
    {
        var ev = log.Append(world.Date, new EventDraft("BorderRaid"));
        world.Realms[realm].Memories.Add(new RealmMemory(ev.Id, about, feeling, 90));
        return ev;
    }

    [Fact]
    public void Friends_Ally_WithARoyalMarriage()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var a = world.Characters[world.Settlements[1].LordId];
        var b = world.Characters[world.Settlements[2].LordId];
        var prince = TestPeople.Add(world, 1, 20, Sex.Male, fatherId: a.Id, houseId: a.HouseId);
        var princess = TestPeople.Add(world, 2, 20, Sex.Female, fatherId: b.Id, houseId: b.HouseId);
        var feast = log.Append(world.Date, new EventDraft("Feast"));
        a.Memories.Add(new Memory(feast.Id, b.Id, 100, 100));
        b.Memories.Add(new Memory(feast.Id, a.Id, 100, 100));
        foreach (var r in world.Realms.Values) r.Memories.Add(new RealmMemory(feast.Id, r.Id == 1 ? 2 : 1, 60, 60));
        var sim = Make(world, log);

        sim.AdvanceDays(360);

        Assert.True(world.Realms[1].Has(TreatyKind.Alliance, 2));
        var pact = sim.Log.All.Single(e => e.Type == "AllianceFormed");
        Assert.Equal("MarriageWed", sim.Log.Get(pact.Causes.Single().EventId).Type);
        Assert.Equal(princess.Id, prince.SpouseId);
    }

    [Fact]
    public void Enemies_DeclareRivalry()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Grudge(world, log, 2, 1);
        world.Realms[2].FaithId = 2;
        world.Faiths[2] = new Faith { Id = 2, Name = "Other", Deity = "Zed" };
        var sim = Make(world, log);
        sim.AdvanceDays(360);
        Assert.True(world.Realms[1].Has(TreatyKind.Rivalry, 2));
    }

    [Fact]
    public void Grudge_GivesACasusBelli_AndTheWarCitesIt()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var raid = Grudge(world, log, 1, 2);
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.Revenge, war.CasusBelli);
        Assert.Equal(raid.Id, war.CasusBelliEventId);
        var declared = sim.Log.Get(war.DeclaredEventId);
        Assert.Equal("WarDeclared", declared.Type);
        Assert.Equal(raid.Id, declared.Causes.Single().EventId);
        Assert.Contains(world.Realms[2].Memories, m => m.EventId == declared.Id && m.About == 1);
    }

    [Fact]
    public void Zealots_ProclaimHolyWar_AgainstOtherFaiths()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Faiths[1].Tenets.Add("zealous");
        world.Faiths[2] = new Faith { Id = 2, Name = "The Other Way", Deity = "Zed" };
        world.Realms[2].FaithId = 2;
        var sim = Make(world, log, "war");

        sim.AdvanceDays(360);

        var war = world.Wars.Values.Single();
        Assert.Equal(CasusBelli.HolyWar, war.CasusBelli);
        Assert.Equal("HolyWarProclaimed", sim.Log.Get(war.CasusBelliEventId).Type);
        Assert.StartsWith("The Holy War of", war.Name);
    }

    [Fact]
    public void Peaceful_Rulers_KeepThePeace()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        Grudge(world, log, 1, 2);
        Make(world, log, "peace").AdvanceDays(360 * 5);
        Assert.Empty(world.Wars);
    }

    [Fact]
    public void StolenHeirloom_GivesArtifactRecovery()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.ArtifactRecovery);
        Assert.Equal(theft.Id, cb.EventId);
        Assert.Equal(crown.Id, cb.TargetArtifactId);
    }

    [Fact]
    public void AnInheritedHeirloom_IsNoLongerACasusBelli()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var victim = world.Characters[world.Settlements[1].LordId];
        var thief = world.Characters[world.Settlements[2].LordId];
        var theft = log.Append(world.Date, new EventDraft("ArtifactStolen"));
        var inherited = log.Append(world.Date, new EventDraft("ArtifactInherited"));
        var crown = TestPeople.GiveArtifact(world, thief);
        crown.Provenance.Add(new ProvenanceEntry(theft.Id, victim.Id, thief.Id, "stolen"));
        crown.Provenance.Add(new ProvenanceEntry(inherited.Id, thief.Id, thief.Id, "inherited"));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        Assert.DoesNotContain(DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]), o => o.Kind == CasusBelli.ArtifactRecovery);
    }

    [Fact]
    public void DeclaredRivals_HaveABorderDispute()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var rivalry = log.Append(world.Date, new EventDraft("RivalryDeclared"));
        world.Realms[1].Treaties.Add(new Treaty(TreatyKind.Rivalry, 2, rivalry.Id, 0));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.BorderDispute);
        Assert.Equal((2L, rivalry.Id), (cb.Against, cb.EventId));
    }

    [Fact]
    public void ARulerWhoCravesRenown_SeeksGlory_OnlyAgainstTheWeaker()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var ruler = world.Characters[world.Settlements[1].LordId];
        ruler.Ambition = Ambitions.Renown;
        var crowned = log.Append(world.Date, new EventDraft("SuccessionOccurred", Actors: new Dictionary<string, EntityRef> { ["heir"] = ruler.Ref }));
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        Assert.DoesNotContain(DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]), o => o.Kind == CasusBelli.Glory);

        world.Realms[1].Treasury = 100_000;
        var cb = DiplomacySystem.CasusBelliFor(ctx, world.Realms[1]).Single(o => o.Kind == CasusBelli.Glory);
        Assert.Equal((2L, crowned.Id), (cb.Against, cb.EventId));
    }
}
```

`sim/FantasyWorld.Sim.Tests/Systems/GreatWorksTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class GreatWorksTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem() }, checkInvariants: true);

    /// <summary>Realm 1's capital as a rich town with a learned notable who is not its lord.</summary>
    private static (World World, Settlement Seat, Character Lord, Character Architect) RichTown(params string[] lordTraits)
    {
        var world = TestRealms.TwoKingdoms();
        var seat = world.Settlements[1];
        (seat.Tier, seat.Wealth) = (SettlementTier.Town, 1_000_000);
        var lord = world.Characters[seat.LordId];
        lord.Traits.AddRange(lordTraits);
        var architect = TestPeople.Add(world, 1, 35);
        architect.Attributes.Lore = architect.Attributes.Stewardship = 18;
        return (world, seat, lord, architect);
    }

    private static Structure Staged(World world, EventLog log, Settlement seat, Character lord, bool rushed = false, double progress = 0)
    {
        var begun = log.Append(world.Date, new EventDraft("LandmarkBegun", Place: seat.Ref));
        var work = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Spire", Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building, Rushed = rushed, Progress = progress,
        };
        world.Structures[work.Id] = work;
        return work;
    }

    [Fact]
    public void AGreatWork_HasAnArchitect_AndItsCompletionSteadiesTheRealm()
    {
        var (world, _, _, architect) = RichTown();
        world.Realms[1].Stability = 50;
        var sim = Make(world);
        sim.AdvanceDays(360 * 80);
        var done = sim.Log.All.First(e => e.Type == "LandmarkCompleted");
        Assert.Equal(architect.Ref, done.Actors["architect"]);
        Assert.Equal(architect.Id, world.Structures[done.Actors["structure"].Id].ArchitectId);
        Assert.True(architect.Renown >= 8);
        Assert.True(world.Realms[1].Stability > 50);
    }

    [Fact]
    public void Plague_HoldsTheWorkUp_AndSaysSoOnce()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var work = Staged(world, log, seat, lord, progress: 40);
        var plague = log.Append(world.Date, new EventDraft("PlagueOutbreak", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.Plague] = plague.Id;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        var delayed = sim.Log.All.Single(e => e.Type == "WorksDelayed");
        Assert.Contains(delayed.Causes, c => c.EventId == plague.Id);
        Assert.Equal(40, work.Progress);
        Assert.Equal(plague.Id, work.DelayedByEventId);
    }

    [Fact]
    public void ARushedWork_MayCollapse_AndTheArchitectIsBlamed()
    {
        var (world, seat, lord, architect) = RichTown(Traits.Ambitious);
        var log = new EventLog();
        architect.Renown = 30;
        for (var i = 0; i < 40; i++) Staged(world, log, seat, lord, rushed: true).ArchitectId = architect.Id;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 2);
        var fall = sim.Log.All.First(e => e.Type == "WorksCollapsed");
        var work = world.Structures[fall.Actors["structure"].Id];
        Assert.Contains(fall.Causes, c => c.EventId == work.StartedEventId);
        Assert.Equal(architect.Ref, fall.Actors["architect"]);
        Assert.True(architect.Renown < 30);
    }

    [Fact]
    public void ATownThatKnewSiege_RaisesAFortress_ThatStrengthensItsDefence()
    {
        var (world, seat, _, _) = RichTown();
        var log = new EventLog();
        var siege = log.Append(world.Date, new EventDraft("SiegeEnded", Place: seat.Ref));
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 150);
        var begun = sim.Log.All.First(e => e.Type == "FortificationBegun");
        Assert.Contains(begun.Causes, c => c.EventId == siege.Id);
        Assert.Contains(sim.Log.All, e => e.Type == "FortificationCompleted");
        Assert.True(StructureSystem.Fortified(world, seat));
    }

    [Fact]
    public void AFallenLandmark_IsRestored_ByAWealthyLord()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var work = Staged(world, log, seat, lord);
        var fell = log.Append(world.Date, new EventDraft("WorksCollapsed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref));
        work.State = StructureState.Ruined;
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 60);
        var restored = sim.Log.All.First(e => e.Type == "RestorationBegun");
        Assert.Equal(work.Ref, restored.Actors["structure"]);
        Assert.Contains(restored.Causes, c => c.EventId == fell.Id);
        Assert.NotEqual(StructureState.Ruined, work.State);
    }

    [Fact]
    public void AnUnfinishedFortress_FallsWithItsAbandonedTown()
    {
        var (world, seat, lord, _) = RichTown();
        var log = new EventLog();
        var begun = log.Append(world.Date, new EventDraft("FortificationBegun", Place: seat.Ref));
        var walls = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Walls", Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building, Progress = 90,
        };
        world.Structures[walls.Id] = walls;
        foreach (var c in world.Characters.Values.Where(c => c.SettlementId == seat.Id)) c.SettlementId = 2;
        (seat.LordId, seat.Abandoned, lord.Role) = (0, true, Role.None);
        var sim = Make(world, log);
        sim.AdvanceDays(360 * 3);
        Assert.Equal(StructureState.Ruined, walls.State);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "FortificationCompleted");
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0117: 'CasusBelli' does not contain a definition for 'BorderDispute'`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.ArchitectId != 0 && !world.Characters.ContainsKey(s.ArchitectId)) errors.Add($"Structure #{s.Id} has unknown architect #{s.ArchitectId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Fortification && s.CompletedEventId == 0)
                errors.Add($"Fortress #{s.Id} stands without a completion event.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }

        foreach (var r in world.Routes.Values)
        {
            if (!world.Settlements.ContainsKey(r.FromId) || !world.Settlements.ContainsKey(r.ToId)) errors.Add($"Route #{r.Id} joins an unknown market.");
            else if (r.Regions.Count == 0 || r.Regions[0] != world.Settlements[r.FromId].RegionId || r.Regions[^1] != world.Settlements[r.ToId].RegionId)
                errors.Add($"Route #{r.Id} does not run from market to market.");
            if (r.Regions.Any(id => !world.Regions.ContainsKey(id))) errors.Add($"Route #{r.Id} crosses an unknown region.");
            if (!log.TryGet(r.OpenedEventId, out _)) errors.Add($"Route #{r.Id} cites missing event #{r.OpenedEventId}.");
            if (r.State != RouteState.Open && r.Volume != 0) errors.Add($"Route #{r.Id} carries trade while {r.State}.");
        }

        foreach (var m in world.Monsters.Values)
        {
            if (!world.Regions.ContainsKey(m.RegionId)) errors.Add($"Monster #{m.Id} lairs in unknown region #{m.RegionId}.");
            if (m.Might is < 1 or > 100) errors.Add($"Monster #{m.Id} has might {m.Might}.");
            if (!log.TryGet(m.RoseEventId, out _)) errors.Add($"Monster #{m.Id} cites missing event #{m.RoseEventId}.");
            if (m.State == MonsterState.Slain && m.SlainEventId == 0) errors.Add($"Monster #{m.Id} is slain without a slaying.");
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Model/Structure.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum StructureKind { Landmark, Fortification, Labyrinth, Ruin }

public enum StructureState { Building, Standing, Ruined }

/// <summary>
/// A place-defining work (spec §5 "Structures", minimal hooks expanded in sub-project 3): a landmark raised by a
/// lord over years, a labyrinth that opens in a dangerous, ley-rich region and hides artifacts, or the ruin an
/// abandoned settlement leaves behind.
/// </summary>
public sealed class Structure
{
    public const string Kind = "structure";

    public long Id { get; init; }
    public string Name { get; set; } = "";
    public StructureKind Type { get; init; }
    public long RegionId { get; init; }

    /// <summary>The settlement it stands in, 0 for the wilds (labyrinths).</summary>
    public long SettlementId { get; init; }

    /// <summary>The character who commissioned it, 0 if none.</summary>
    public long BuilderId { get; init; }

    /// <summary>The character who designs and oversees a great work (0 if none).</summary>
    public long ArchitectId { get; set; }

    /// <summary>Pushed on by an impatient patron: faster, but it may collapse.</summary>
    public bool Rushed { get; init; }

    /// <summary>The event (war, plague, famine) holding the work up, 0 when it goes on.</summary>
    public long DelayedByEventId { get; set; }

    public long StartedEventId { get; init; }
    public long CompletedEventId { get; set; }

    /// <summary>0–100 while building.</summary>
    public double Progress { get; set; }

    public StructureState State { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Model/War.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Why a war is fought (spec §6). Every war cites the event that gave it its casus belli.</summary>
public static class CasusBelli
{
    public const string Claim = "claim";
    public const string HolyWar = "holy war";
    public const string Revenge = "revenge";
    public const string Reconquest = "reconquest";
    public const string ResourceNeed = "resource need";
    public const string ArtifactRecovery = "artifact recovery";
    public const string Independence = "independence";
    public const string BorderDispute = "border dispute";
    public const string Glory = "glory";
}

public sealed class War
{
    public const string Kind = "war";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long AttackerId { get; init; }
    public long DefenderId { get; init; }
    public List<long> Attackers { get; init; } = new();
    public List<long> Defenders { get; init; } = new();
    public string CasusBelli { get; init; } = "";
    public long CasusBelliEventId { get; init; }
    public long TargetRegionId { get; init; }
    public long TargetArtifactId { get; init; }
    public long DeclaredEventId { get; set; }
    public long StartDay { get; init; }

    /// <summary>−100…100, positive favours the attackers: occupations plus BattleScore.</summary>
    public double Score { get; set; }

    /// <summary>Accumulated battle results, positive favours the attackers.</summary>
    public double BattleScore { get; set; }

    public int Battles { get; set; }
    public bool Ended { get; set; }
    public long EndedEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool IsAttacker(long realm) => Attackers.Contains(realm);
    public bool Involves(long realm) => Attackers.Contains(realm) || Defenders.Contains(realm);
    public long LeaderOf(long realm) => IsAttacker(realm) ? AttackerId : DefenderId;
    public IReadOnlyList<long> EnemiesOf(long realm) => IsAttacker(realm) ? Defenders : Attackers;
}

public enum ArmyObjective { Defend, Siege, Return }

/// <summary>An army in the field (spec §6 "Armies"). Levies are borrowed from settlements and returned on disbanding.</summary>
public sealed class Army
{
    public const string Kind = "army";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long RealmId { get; init; }
    public long WarId { get; init; }
    public long CommanderId { get; set; }
    public int Levies { get; set; }
    public int MenAtArms { get; set; }
    public List<long> Knights { get; init; } = new();
    public int Mages { get; set; }
    public double Morale { get; set; } = 70;
    public double Supply { get; set; } = 100;
    public long RegionId { get; set; }
    public List<long> Path { get; init; } = new();

    /// <summary>Travel days left before reaching the next region on the path.</summary>
    public double DaysToNext { get; set; }

    public ArmyObjective Objective { get; set; }
    public long TargetRegionId { get; set; }

    /// <summary>Settlement id → levies raised there (returned when the army disbands).</summary>
    public SortedDictionary<long, int> RaisedFrom { get; init; } = new();

    public bool Disbanded { get; set; }

    [JsonIgnore] public int Soldiers => Levies + MenAtArms + Knights.Count + Mages;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["WildMagic"] = "Wild magic broke loose at {@place}: a {=kind}.",
        ["MageTrained"] = "{mage} was trained in the mage tower of {@place}.",
        ["MagicLawChanged"] = "{realm} changed its law on magic from {=from} to {=law}.",
        ["MageFled"] = "{mage} fled the law against magic to {@place}.",
        ["WorksDelayed"] = "Work on {structure} stopped.",
        ["WorksCollapsed"] = "{structure} collapsed while it was being built; {=dead} died.",
        ["FortificationBegun"] = "{builder} began {structure} at {@place}.",
        ["FortificationCompleted"] = "{structure} was completed at {@place}.",
        ["RestorationBegun"] = "{builder} began to restore {structure}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>A reason to go to war, found in history: the event it cites and what it aims at.</summary>
public sealed record CasusBelliOption(string Kind, long Against, long EventId, long TargetRegionId, long TargetArtifactId, double Weight);

/// <summary>
/// Relations between independent realms (spec §6 "Diplomacy"), yearly. Realm opinion = the rulers'
/// personal opinion + realm memories + faith and culture + treaties. Friendly neighbours ally (sealed
/// by a royal marriage when kin allow), hostile ones declare rivalry. A ruler at peace weighs every
/// casus belli history offers against keeping the peace (a decision point); a declared war cites its
/// casus belli and draws in allies.
/// </summary>
public sealed class DiplomacySystem : ISimSystem
{
    private readonly IDecider _decider;
    private readonly IInventor _inventor;

    public DiplomacySystem(IDecider? decider = null, IInventor? inventor = null)
    {
        _decider = decider ?? new RulesDecider();
        _inventor = inventor ?? new RulesInventor();
    }

    public string Name => "diplomacy";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var realms = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var a in realms)
            foreach (var b in Realms.Neighbours(world, a).Where(b => b.Id > a.Id && b.LiegeId == 0))
                ReviewTreaties(ctx, a, b);

        foreach (var realm in realms)
        {
            if (realm.Destroyed || realm.LiegeId != 0 || AtWar(world, realm)) continue;
            var ruler = Realms.RulerOf(world, realm);
            if (ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var options = CasusBelliFor(ctx, realm);
            if (options.Count == 0) continue;
            var choices = options.Select((o, i) => new DecisionOption($"war:{i}", o.Weight * Aggression(world, realm, ruler)
                    * Math.Clamp(Strength(world, realm) / Math.Max(1, Strength(world, world.Realms[o.Against])), 0.3, 2.0),
                    $"{o.Kind} against {world.Realms[o.Against].Name}"))
                .Append(new DecisionOption("peace", 100 + realm.WarWeariness, "keep the peace"))
                .ToList();
            var choice = _decider.Decide(new DecisionBrief(ruler.Id, "war or peace", choices), ctx.Rng);
            if (choice.Key == "peace") continue;
            using (ctx.Annotate("motive", choice.Reasoning))
                Declare(ctx, realm, options[int.Parse(choice.Key[4..], CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>What realm a thinks of realm b, −100…100.</summary>
    public static double Opinion(World world, Realm a, Realm b)
    {
        var score = 0.0;
        if (Realms.RulerOf(world, a) is { } ra && Realms.RulerOf(world, b) is { } rb) score += Relations.Opinion(ra, rb) / 2;
        score += a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += a.FaithId == b.FaithId ? 15 : -15;
        if (a.CultureId == b.CultureId) score += 10;
        if (a.Has(TreatyKind.Alliance, b.Id)) score += 25;
        if (a.Has(TreatyKind.Rivalry, b.Id)) score -= 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Rough fighting power: levies from the territory, men-at-arms the treasury could pay, knights.</summary>
    public static double Strength(World world, Realm realm)
    {
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var levies = world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)).Sum(s => s.Adults) * 0.05;
        var knights = world.ActiveCharacters.Count(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        return levies + Math.Max(0, realm.Treasury) / 50 * 3 + knights * 20;
    }

    public static bool AtWar(World world, Realm realm) => world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id));

    private static double Aggression(World world, Realm realm, Character ruler)
    {
        var a = 1.0;
        if (ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave)) a *= 1.5;
        if (ruler.Has(Traits.Content) || ruler.Has(Traits.Craven) || ruler.Has(Traits.Kind)) a *= 0.5;
        var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
        if (tenets.Contains("pacifist")) a *= 0.3;
        if (tenets.Contains("warlike")) a *= 1.5;
        if (realm.Conditions.ContainsKey(Statecraft.StabilityCollapsed)) a *= 0.5;
        return a;
    }

    private static void ReviewTreaties(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        var ab = Opinion(world, a, b);
        var ba = Opinion(world, b, a);
        var allied = a.Has(TreatyKind.Alliance, b.Id);
        var rivals = a.Has(TreatyKind.Rivalry, b.Id);
        if (!allied && ab >= 40 && ba >= 40 && !AtWarWith(world, a, b))
        {
            var wedding = RoyalMarriage(ctx, a, b);
            var pact = Pact(ctx, a, b, TreatyKind.Alliance, "AllianceFormed", 40, wedding);
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == a.Id);
            Statecraft.Remember(a, pact.Id, b.Id, 40, 60);
            Statecraft.Remember(b, pact.Id, a.Id, 40, 60);
        }
        else if (allied && (ab < 0 || ba < 0))
        {
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
            var broken = ctx.Emit(new EventDraft("AllianceBroken", Actors: Pair(a, b), Place: world.Settlements[a.CapitalId].Ref,
                Importance: 40, Tags: new[] { "Politics", "Diplomacy" }));
            Statecraft.Remember(a, broken.Id, b.Id, -30, 60);
            Statecraft.Remember(b, broken.Id, a.Id, -30, 60);
        }
        else if (!rivals && ab <= -40 && ba <= -40)
            Pact(ctx, a, b, TreatyKind.Rivalry, "RivalryDeclared", 35, null);
    }

    private static WorldEvent Pact(SimContext ctx, Realm a, Realm b, TreatyKind kind, string type, int importance, WorldEvent? cause)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: Pair(a, b), Place: ctx.World.Settlements[a.CapitalId].Ref,
            Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Enabler, 0.8) },
            Importance: importance, Tags: new[] { "Politics", "Diplomacy" }));
        a.Treaties.Add(new Treaty(kind, b.Id, ev.Id, 0));
        b.Treaties.Add(new Treaty(kind, a.Id, ev.Id, 0));
        return ev;
    }

    /// <summary>Weds an unmarried adult child or sibling of one ruler to one of the other's, if any match.</summary>
    private static WorldEvent? RoyalMarriage(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        IEnumerable<Character> Kin(Realm realm) => Realms.RulerOf(world, realm) is { } ruler
            ? ruler.Children.Select(id => world.Characters[id]).Concat(Fates.Siblings(world, ruler)).Append(ruler)
                .Where(k => FamilySystem.Marriageable(k, ctx.Date))
            : Array.Empty<Character>();
        // Children first, then siblings, then the rulers themselves.
        foreach (var x in Kin(a))
            foreach (var y in Kin(b))
                if (x.Sex != y.Sex && !(x.Role == Role.Lord && y.Role == Role.Lord) && !Relations.AreKin(x, y))
                    return FamilySystem.Wed(ctx, x, y);
        return null;
    }

    /// <summary>Every casus belli this realm's history gives it against a neighbour.</summary>
    public static List<CasusBelliOption> CasusBelliFor(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var options = new List<CasusBelliOption>();
        var ruler = Realms.RulerOf(world, realm);
        foreach (var enemy in Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id)))
        {
            var theirs = Realms.Territory(world, enemy).ToList();
            var grudge = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling <= -50).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null)
            {
                var ev = ctx.Log.Get(grudge.EventId);
                var lost = ev.Type == "RegionCeded" && theirs.Any(r => ev.Place == r.Ref) ? ev.Place!.Value.Id : 0;
                if (ev.Type == "IndependenceDeclared")
                    options.Add(new(CasusBelli.Independence, enemy.Id, grudge.EventId, world.Settlements[enemy.CapitalId].RegionId, 0, 40));
                else if (lost != 0) options.Add(new(CasusBelli.Reconquest, enemy.Id, grudge.EventId, lost, 0, 40));
                else options.Add(new(CasusBelli.Revenge, enemy.Id, grudge.EventId, Border(world, realm, enemy), 0, 30));
            }

            var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
            if (realm.FaithId != enemy.FaithId && (tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, 0, Border(world, realm, enemy), 0, 20));
            // A faith that lost its holy land to this neighbour wants it back, the more so the more zealous it is.
            if (world.Faiths.GetValueOrDefault(realm.FaithId) is { HolySiteLostEventId: not 0 } mine && world.Regions.TryGetValue(mine.HolyRegionId, out var holy)
                && Realms.SovereignOf(world, holy)?.Id == enemy.Id && (mine.Zeal >= 40 || tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, mine.HolySiteLostEventId, holy.Id, 0, 40));

            // Declared rivals quarrel over the land between them.
            if (realm.Treaties.FirstOrDefault(t => t.Kind == TreatyKind.Rivalry && t.With == enemy.Id) is { } rivalry)
                options.Add(new(CasusBelli.BorderDispute, enemy.Id, rivalry.SinceEventId, Border(world, realm, enemy), 0, 25));

            // A ruler who craves renown seeks it against a weaker neighbour (their temper decides how often).
            if (ruler is not null && ruler.Ambition == Ambitions.Renown
                && Strength(world, realm) > Strength(world, enemy) * 1.2 && Fates.Accession(ctx.Log, ruler) is { } crowned)
                options.Add(new(CasusBelli.Glory, enemy.Id, crowned.Id, Border(world, realm, enemy), 0, ruler.Has(Traits.Ambitious) ? 15 : 8));

            var hungry = Realms.Domain(world, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
            var granary = theirs.Where(r => r.Fertility >= 60 && Touches(world, r, realm)).OrderByDescending(r => r.Fertility).ThenBy(r => r.Id).FirstOrDefault();
            if (hungry != 0 && granary is not null) options.Add(new(CasusBelli.ResourceNeed, enemy.Id, hungry, granary.Id, 0, 30));

            if (ruler is not null)
            {
                foreach (var artifact in world.Artifacts.Values.Where(x => HeldBy(world, x, enemy)))
                {
                    // Only a fresh wrong counts: the artifact's latest change of hands was a theft from the ruler's house.
                    var theft = artifact.Provenance.LastOrDefault();
                    if (theft is { How: "stolen" or "looted" } && ruler.HouseId != 0
                        && world.Characters.TryGetValue(theft.From, out var victim) && victim.HouseId == ruler.HouseId) options.Add(new(CasusBelli.ArtifactRecovery, enemy.Id, theft.EventId, world.Settlements[enemy.CapitalId].RegionId, artifact.Id, 25));
                }
                if (Realms.RulerOf(world, enemy) is { } theirRuler && world.Characters.TryGetValue(theirRuler.SpouseId, out var consort)
                    && (consort.HouseId == ruler.HouseId) && consort.MarriageEventId != 0)
                    options.Add(new(CasusBelli.Claim, enemy.Id, consort.MarriageEventId, world.Settlements[enemy.CapitalId].RegionId, 0, 20));
            }
        }
        return options;
    }

    private void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
    {
        var world = ctx.World;
        var defender = world.Realms[cb.Against];
        var cbEvent = cb.EventId;
        if (cb.Kind == CasusBelli.HolyWar)
            cbEvent = ctx.Emit(new EventDraft("HolyWarProclaimed", Actors: Pair(attacker, defender),
                Place: world.Settlements[attacker.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["faith"] = world.Faiths[attacker.FaithId].Name },
                Causes: cb.EventId != 0 ? new[] { new Cause(cb.EventId, CauseKind.Motive, 0.9) } : null,
                Importance: 50, Tags: new[] { "Politics", "Faith" })).Id;

        var target = world.Regions.GetValueOrDefault(cb.TargetRegionId);
        var name = cb.Kind switch
        {
            CasusBelli.HolyWar when target is not null && target.Id == world.Faiths[attacker.FaithId].HolyRegionId => $"The Holy War for {target.Name}",
            CasusBelli.HolyWar => $"The Holy War of {world.Faiths[attacker.FaithId].Deity}",
            CasusBelli.Independence => $"The {defender.Name} Rebellion",
            CasusBelli.Revenge => $"The War of {attacker.Name}'s Vengeance",
            CasusBelli.Glory when Realms.RulerOf(world, attacker) is { } glorious => $"The War of {glorious.Name}'s Glory",
            _ => $"The {target?.Name ?? defender.Name} War",
        };
        var warId = world.Ids.Peek(War.Kind);
        var invention = _inventor.Invent(new InventionBrief("war", $"{War.Kind}#{warId}",
            $"A war of {cb.Kind} declared by {Statecraft.Title(attacker)} on {Statecraft.Title(defender)}"
            + (target is null ? "" : $" over {target.Name}") + $", in year {ctx.Date.Year}.", name));
        name = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = name,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            Attackers = { attacker.Id },
            Defenders = { defender.Id },
            CasusBelli = cb.Kind,
            CasusBelliEventId = cbEvent,
            TargetRegionId = cb.TargetRegionId,
            TargetArtifactId = cb.TargetArtifactId,
            StartDay = ctx.Date.Day,
        };
        var declared = ctx.Emit(new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = attacker.Ref, ["defender"] = defender.Ref, ["war"] = war.Ref },
            Place: world.Settlements[defender.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["casus belli"] = cb.Kind, ["war"] = name },
            Causes: WarCauses(ctx, attacker, defender, cbEvent),
            Importance: 70, Tags: new[] { "Politics", "War" }));
        war.DeclaredEventId = declared.Id;
        world.Wars[war.Id] = war;
        attacker.Treaties.RemoveAll(t => t.With == defender.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        defender.Treaties.RemoveAll(t => t.With == attacker.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        Statecraft.Remember(defender, declared.Id, attacker.Id, -50, 70);

        foreach (var (side, leader) in new[] { (war.Attackers, attacker), (war.Defenders, defender) })
            foreach (var ally in Realms.Independent(world).Where(r => r.Has(TreatyKind.Alliance, leader.Id) && !AtWar(world, r)).OrderBy(r => r.Id).ToList())
            {
                // An ally of both sides stays out rather than fight a friend.
                if (war.Involves(ally.Id) || ally.Has(TreatyKind.Alliance, attacker.Id) && ally.Has(TreatyKind.Alliance, defender.Id)
                    || !ctx.Rng.Chance(0.7)) continue;
                side.Add(ally.Id);
                ctx.Emit(new EventDraft("AllyJoinedWar",
                    Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                    Payload: new Dictionary<string, string> { ["side"] = side == war.Attackers ? "attackers" : "defenders" },
                    Place: world.Settlements[ally.CapitalId].Ref,
                    Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
            }
    }

    /// <summary>
    /// The casus belli is the motive; the realms' latest rivalry, their latest shared grievance (a peace, a ceded
    /// region, a declaration of independence) and the ruler's accession, when there are any, are enablers (at most
    /// three causes in all), so a war's why-chain reaches back past its pretext.
    /// </summary>
    private static List<Cause> WarCauses(SimContext ctx, Realm attacker, Realm defender, long cbEvent)
    {
        var causes = new List<Cause> { new(cbEvent, CauseKind.Motive, 0.9) };
        bool Cited(long id) => causes.Any(c => c.EventId == id);
        var shared = ctx.Log.Involving(attacker.Ref).Where(e => e.Actors.ContainsValue(defender.Ref)).ToList();
        var rivalry = shared.LastOrDefault(e => e.Type is "RivalryDeclared" or "AllianceBroken");
        if (rivalry is not null && !Cited(rivalry.Id)) causes.Add(new Cause(rivalry.Id, CauseKind.Enabler, 0.5));
        var grievance = shared.LastOrDefault(e => e.Type is "PeaceSigned" or "RegionCeded" or "IndependenceDeclared");
        if (grievance is not null && !Cited(grievance.Id)) causes.Add(new Cause(grievance.Id, CauseKind.Enabler, 0.4));
        if (causes.Count < 3 && Realms.RulerOf(ctx.World, attacker) is { } ruler
            && Fates.Accession(ctx.Log, ruler) is { } accession && !Cited(accession.Id))
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.3));
        return causes;
    }

    private static bool AtWarWith(World world, Realm a, Realm b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.IsAttacker(a.Id) && w.Defenders.Contains(b.Id) || w.IsAttacker(b.Id) && w.Defenders.Contains(a.Id)));

    private static bool HeldBy(World world, Artifact x, Realm realm) => x.Place switch
    {
        ArtifactPlace.Held => Realms.SovereignOf(world, world.Settlements[world.Characters[x.HolderId].SettlementId])?.Id == realm.Id,
        ArtifactPlace.Treasury => Realms.SovereignOf(world, world.Settlements[x.SettlementId])?.Id == realm.Id,
        _ => false,
    };

    /// <summary>The enemy's region nearest our territory: an enemy region adjacent to ours, lowest id.</summary>
    private static long Border(World world, Realm ours, Realm theirs) =>
        Realms.Territory(world, theirs).Where(r => Touches(world, r, ours)).Select(r => r.Id).DefaultIfEmpty(world.Settlements[theirs.CapitalId].RegionId).Min();

    private static bool Touches(World world, Region region, Realm realm) =>
        (world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).Any(n => Realms.SovereignOf(world, world.Regions[n])?.Id == realm.Id);

    private static Dictionary<string, EntityRef> Pair(Realm a, Realm b) => new() { ["a"] = a.Ref, ["b"] = b.Ref };
}
```

Replace `sim/FantasyWorld.Sim/Systems/MilitarySystem.Sieges.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Sieges and occupation (spec §6 steps 5–6). An army that reaches its target besieges the region's chief
/// settlement: undefended places fall at once, walled ones after months of pressure against walls and
/// garrison, and a cruel commander sacks what falls. Defenders who return to occupied land unopposed
/// liberate it. War score is occupation (enemy capitals count more) plus the tally of battles.
/// </summary>
public sealed partial class MilitarySystem
{
    partial void Besiege(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var region in world.Regions.Values.Where(r => r.BesiegerArmyId != 0).ToList())
        {
            var besieger = world.Armies[region.BesiegerArmyId];
            if (besieger.Disbanded || besieger.RegionId != region.Id || besieger.Objective != ArmyObjective.Siege)
            {
                region.BesiegerArmyId = 0;
                region.SiegeProgress = 0;
            }
        }

        foreach (var army in Active(world).Where(a => a.Objective == ArmyObjective.Siege && a.RegionId == a.TargetRegionId).ToList())
        {
            if (army.Disbanded) continue;
            var war = world.Wars[army.WarId];
            var realm = world.Realms[army.RealmId];
            var region = world.Regions[army.RegionId];
            if (!Hostile(world, war, realm, region) || Contested(world, war, army)) continue;
            var chief = world.LivingSettlements(region).OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
            var defense = chief is null ? 0 : chief.Walls * 400 + chief.Garrison * 3 + 50 + (StructureSystem.Fortified(world, chief) ? StructureSystem.FortressDefence : 0);
            if (defense <= 150)
            {
                Occupy(ctx, war, army, region, chief, "RegionOccupied", null);
                continue;
            }
            if (region.BesiegerArmyId != army.Id)
            {
                region.BesiegerArmyId = army.Id;
                region.SiegeProgress = 0;
                ctx.Emit(new EventDraft("SiegeBegan", Actors: Actors(world, army, war), Place: chief!.Ref,
                    Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.5) }, Importance: 35, Tags: new[] { "War" }));
            }
            region.SiegeProgress = Math.Min(100, region.SiegeProgress + Strength(world, army, region) / defense * 25);
            if (region.SiegeProgress >= 100) Occupy(ctx, war, army, region, chief, "SiegeEnded", chief);
        }

        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0).ToList())
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var liberator = Active(world).Where(a => a.RegionId == region.Id).OrderBy(a => a.Id).FirstOrDefault(a =>
            {
                var w = world.Wars[a.WarId];
                var ours = w.IsAttacker(a.RealmId) ? w.Attackers : w.Defenders;
                var theirs = w.IsAttacker(a.RealmId) ? w.Defenders : w.Attackers;
                return ours.Contains(owner.Id) && theirs.Contains(region.OccupierRealmId);
            });
            if (liberator is null || Contested(world, world.Wars[liberator.WarId], liberator)) continue;
            region.OccupierRealmId = 0;
            ctx.Emit(new EventDraft("RegionLiberated", Actors: Actors(world, liberator, world.Wars[liberator.WarId]), Place: region.Ref,
                Importance: 35, Tags: new[] { "War" }));
        }
    }

    partial void Score(SimContext ctx, War war)
    {
        var world = ctx.World;
        var occupation = 0.0;
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var weight = world.Realms.Values.Any(r => !r.Destroyed && world.Settlements[r.CapitalId].RegionId == region.Id && r.LiegeId == 0) ? 25 : 10;
            if (war.Attackers.Contains(region.OccupierRealmId) && war.Defenders.Contains(owner.Id)) occupation += weight;
            if (war.Defenders.Contains(region.OccupierRealmId) && war.Attackers.Contains(owner.Id)) occupation -= weight;
        }
        war.Score = Math.Clamp(occupation + war.BattleScore, -100, 100);
    }

    private static void Occupy(SimContext ctx, War war, Army army, Region region, Settlement? chief, string type, Settlement? sacked)
    {
        var world = ctx.World;
        region.OccupierRealmId = army.RealmId;
        region.BesiegerArmyId = 0;
        region.SiegeProgress = 0;
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        var began = ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
        if (type == "SiegeEnded" && chief is not null)
        {
            var siege = ctx.Log.Involving(chief.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
            if (siege is not null) causes.Insert(0, new Cause(siege.Id, CauseKind.Trigger, 1.0));
        }
        else if (began is not null) causes.Add(new Cause(began.Id, CauseKind.Enabler, 0.3));
        var fell = ctx.Emit(new EventDraft(type, Actors: Actors(world, army, war), Place: chief?.Ref ?? region.Ref,
            Payload: type == "SiegeEnded" ? new Dictionary<string, string> { ["outcome"] = "fell" } : null,
            Causes: causes, Importance: type == "SiegeEnded" ? 50 : 30, Tags: new[] { "War" }));
        if (chief is not null)
        {
            chief.Garrison = 0;
            chief.Walls = Math.Max(0, chief.Walls - 1);
        }
        Statecraft.Remember(Realms.SovereignOf(world, region)!, fell.Id, army.RealmId, -50, 70);

        if (sacked is not null && world.Characters.TryGetValue(army.CommanderId, out var commander) && commander.Has(Traits.Cruel))
        {
            var dead = Demography.Kill(sacked, 0.1, ctx.Rng);
            sacked.Wealth *= 0.5;
            var sack = ctx.Emit(new EventDraft("SettlementSacked",
                Actors: new Dictionary<string, EntityRef> { ["commander"] = commander.Ref, ["war"] = war.Ref },
                Place: sacked.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                Causes: new[] { new Cause(fell.Id, CauseKind.Trigger, 1.0) }, Importance: 60, Tags: new[] { "War" }));
            Statecraft.Remember(Realms.SovereignOf(world, region)!, sack.Id, army.RealmId, -90, 100);
        }
    }

    /// <summary>An enemy army stands in the same region.</summary>
    private static bool Contested(World world, War war, Army army)
    {
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.WarId == war.Id && a.RegionId == army.RegionId && enemies.Contains(a.RealmId));
    }

    private static Dictionary<string, EntityRef> Actors(World world, Army army, War war)
    {
        var actors = new Dictionary<string, EntityRef> { ["realm"] = world.Realms[army.RealmId].Ref, ["army"] = army.Ref, ["war"] = war.Ref };
        if (world.Characters.TryGetValue(army.CommanderId, out var commander)) actors["commander"] = commander.Ref;
        return actors;
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/StructureSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Structures (spec §5, minimal hooks for sub-project 3), yearly and last in the tick order. Wealthy lords commission
/// landmarks that take years to raise (paid from the settlement's wealth; completion brings renown). Labyrinths open
/// in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers. Abandoned settlements
/// leave ruins, and their unfinished or standing landmarks fall with them.
/// Great works (sub-project 3 §6): a landmark has an architect; war, plague or famine hold the work up; an impatient
/// patron rushes it, at the risk of collapse; on completion the patron gains renown and the realm stability. Towns that
/// have known siege raise fortresses, which strengthen their defence. A lord may restore a fallen landmark.
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

    public const double RushedCollapse = 0.08;
    public const double FortressPerYear = 0.03;
    public const double FortressWealth = 2500;
    public const int FortressDefence = 400;
    public const double RestorePerYear = 0.05;

    private static readonly string[] Works = { "Spire", "Great Hall", "Bridge", "Temple", "Library", "Colossus", "Lighthouse", "Garden" };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "structures";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        Ruin(ctx);
        Build(ctx);
        Commission(ctx);
        Fortify(ctx);
        Restore(ctx);
        OpenLabyrinths(ctx);
    }

    /// <summary>A standing fortress guards the town.</summary>
    public static bool Fortified(World world, Settlement s) =>
        world.Structures.Values.Any(x => x.Type == StructureKind.Fortification && x.State == StructureState.Standing && x.SettlementId == s.Id);

    /// <summary>The town's best designer: a grown notable, not its lord, by lore and stewardship.</summary>
    public static Character? Architect(World world, Settlement seat, Core.WorldDate today) =>
        world.ActiveCharacters.Where(c => c.SettlementId == seat.Id && c.Id != seat.LordId && c.Stage(today) != LifeStage.Child)
            .OrderByDescending(c => c.Attributes.Lore + c.Attributes.Stewardship).ThenBy(c => c.Id).FirstOrDefault();

    public static int LabyrinthCap(World world) => Math.Max(1, world.Regions.Count / 15);

    private void Commission(SimContext ctx)
    {
        var world = ctx.World;
        var busy = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= LandmarkWealth
                                                                 && !busy.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            if (!ctx.Rng.Chance(CommissionPerYear)) continue;
            var fallback = $"The {Works[ctx.Rng.NextInt(0, Works.Length)]} of {seat.Name}";
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("landmark", $"{Structure.Kind}#{id}",
                $"A landmark commissioned by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}.", fallback));
            seat.Wealth *= 0.8;
            var architect = Architect(world, seat, ctx.Date);
            var actors = new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) };
            if (architect is not null) actors["architect"] = architect.Ref;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun", Actors: actors,
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = architect?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
                Rushed = lord.Has(Traits.Ambitious) || lord.Has(Traits.Wrathful),
            };
        }
    }

    /// <summary>What holds a work up this year: war at the town, plague or famine in its land (the event that began it).</summary>
    private static long Hindrance(SimContext ctx, Settlement seat)
    {
        var world = ctx.World;
        var region = world.Regions[seat.RegionId];
        if (region.OccupierRealmId != 0 || region.BesiegerArmyId != 0)
            return ctx.Log.Involving(region.Ref).Concat(ctx.Log.Involving(seat.Ref))
                .Where(e => e.Type is "RegionOccupied" or "SiegeBegan" or "SiegeEnded").Select(e => e.Id).DefaultIfEmpty().Max();
        var plague = region.Conditions.GetValueOrDefault(Conditions.Plague);
        return plague != 0 ? plague : region.Conditions.GetValueOrDefault(Conditions.Shortage);
    }

    private void Fortify(SimContext ctx)
    {
        var world = ctx.World;
        var guarded = world.Structures.Values.Where(s => s.Type == StructureKind.Fortification && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= FortressWealth
                                                                 && !guarded.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            var region = world.Regions[seat.RegionId];
            var siege = ctx.Log.Involving(seat.Ref).Concat(ctx.Log.Involving(region.Ref))
                .Where(e => e.Type is "SiegeEnded" or "RegionOccupied").OrderBy(e => e.Id).LastOrDefault();
            if (siege is null || !ctx.Rng.Chance(FortressPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("fortress", $"{Structure.Kind}#{id}",
                $"A fortress raised by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}, after the town knew war.", $"The Walls of {seat.Name}"));
            seat.Wealth *= 0.8;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("FortificationBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: new[] { new Cause(siege.Id, CauseKind.Motive, 0.8) }, Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures", "War" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = Architect(world, seat, ctx.Date)?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Restore(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s is { Type: StructureKind.Landmark or StructureKind.Fortification, State: StructureState.Ruined }).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned || seat.Wealth < LandmarkWealth || !world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive) continue;
            if (!ctx.Rng.Chance(RestorePerYear)) continue;
            var fell = ctx.Log.Involving(work.Ref).LastOrDefault(e => e.Type is "WorksCollapsed" or "StructureRuined");
            seat.Wealth *= 0.85;
            work.State = StructureState.Building;
            work.Progress = 50;
            work.DelayedByEventId = 0;
            ctx.Emit(new EventDraft("RestorationBegun", Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = work.Ref },
                Place: seat.Ref, Causes: fell is null ? null : new[] { new Cause(fell.Id, CauseKind.Motive, 0.7) },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned) continue; // its ruin comes with the town's (Ruin)
            var hindrance = Hindrance(ctx, seat);
            if (hindrance != 0)
            {
                if (work.DelayedByEventId != hindrance)
                {
                    work.DelayedByEventId = hindrance;
                    ctx.Emit(new EventDraft("WorksDelayed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref,
                        Causes: new[] { new Cause(hindrance, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Structures" }));
                }
                continue;
            }
            work.DelayedByEventId = 0;
            var step = ctx.Rng.NextInt(7, 17) * (work.Rushed ? 1.6 : 1);
            work.Progress = Math.Min(100, work.Progress + step);
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Rushed && work.Progress < 100 && ctx.Rng.Chance(RushedCollapse))
            {
                Collapse(ctx, work, seat);
                continue;
            }
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var architect = world.Characters.GetValueOrDefault(work.ArchitectId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            if (architect is not null) actors["architect"] = architect.Ref;
            var fortress = work.Type == StructureKind.Fortification;
            var done = ctx.Emit(new EventDraft(fortress ? "FortificationCompleted" : "LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: fortress ? new[] { "Structures", "War" } : new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
            if (architect is { IsAlive: true }) architect.Renown = Math.Min(100, architect.Renown + 8);
            if (Realms.SovereignOf(world, world.Regions[seat.RegionId]) is { } realm) realm.Stability = Math.Min(100, realm.Stability + 5);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        var haunted = labyrinths.Select(s => s.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world))
            {
                var relic = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id, fame: 20);
                world.Artifacts[relic.Id] = relic;
                Fates.Lose(relic, region.Id, opened.Id, 0);
            }
            return; // at most one a year
        }
    }

    private static void Ruin(SimContext ctx)
    {
        var world = ctx.World;
        var ruined = world.Structures.Values.Where(s => s.Type == StructureKind.Ruin).Select(s => s.SettlementId).ToHashSet();
        foreach (var town in world.Settlements.Values.Where(s => s.Abandoned && !ruined.Contains(s.Id)).ToList())
        {
            var abandonment = ctx.Log.Involving(town.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
            var id = world.Ids.Take(Structure.Kind);
            var fell = ctx.Emit(new EventDraft("StructureRuined",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: town.Ref, Payload: new Dictionary<string, string> { ["name"] = $"The Ruins of {town.Name}" },
                Causes: abandonment is null ? null : new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) },
                Importance: 25, Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = $"The Ruins of {town.Name}", Type = StructureKind.Ruin, RegionId = town.RegionId, SettlementId = town.Id,
                StartedEventId = fell.Id, CompletedEventId = fell.Id, Progress = 100, State = StructureState.Ruined,
            };
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type is StructureKind.Landmark or StructureKind.Fortification && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }

    /// <summary>A rushed work falls: people die, half the work is lost, and the architect is blamed.</summary>
    private static void Collapse(SimContext ctx, Structure work, Settlement seat)
    {
        var before = seat.Population;
        Demography.Kill(seat, 0.005, ctx.Rng);
        work.Progress = Math.Max(0, work.Progress - 50);
        var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
        if (ctx.World.Characters.TryGetValue(work.ArchitectId, out var architect))
        {
            actors["architect"] = architect.Ref;
            architect.Renown = Math.Max(0, architect.Renown - 10);
        }
        var builder = ctx.World.Characters.GetValueOrDefault(work.BuilderId);
        ctx.Emit(new EventDraft("WorksCollapsed", Actors: actors, Place: seat.Ref,
            Payload: new Dictionary<string, string> { ["dead"] = (before - seat.Population).ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(work.StartedEventId, CauseKind.Enabler, 0.7) },
            Importance: builder is null ? 40 : Fates.Importance(builder, 40), Tags: new[] { "Structures" }));
        if (work.Progress <= 0) work.State = StructureState.Ruined;
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["WorksCollapsed"] = "A collapse",
        ["FortificationCompleted"] = "A fortress",
        ["RestorationBegun"] = "Restoration",
        ["WildMagic"] = "Wild magic",
        ["MagicLawChanged"] = "Magic law",
        ["SchismBegan"] = "Schism",
        ["SaintDeclared"] = "A saint",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~DiplomacySystemTests|FullyQualifiedName~GreatWorksTests"`
Expected: `Passed!  - Failed: 0, Passed: 15`. Then `dotnet test sim` → `Passed: 548` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): build great works over years, raise fortresses, and give rivals and the ambitious their wars

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Labyrinth levels, guardians and clearing

Spec §7. A labyrinth opens with three to seven levels and a guardian (a beast that lairs there and never raids) on its deepest. Delvers go down one level at a time, each harder than the last, and the depth reached is remembered; at the bottom they face the guardian, and past it lie the relics lost in that land. Slaying the guardian and taking the last relic clears the labyrinth: the land grows safer and it becomes a ruin, or a shrine if a devout hand cleared it. A cleared land does not open another. Saints' relics now respect the cap on artifacts.

**Files:**
- Modify: `sim/FantasyWorld.Sim/Agency/Encounters.cs`, `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs`, `sim/FantasyWorld.Sim/Model/Monster.cs`, `sim/FantasyWorld.Sim/Model/Structure.cs`, `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs`, `sim/FantasyWorld.Sim/Systems/DeedsSystem.cs`, `sim/FantasyWorld.Sim/Systems/FaithSystem.cs`, `sim/FantasyWorld.Sim/Systems/MonsterSystem.cs`, `sim/FantasyWorld.Sim/Systems/StructureSystem.cs`, `sim/FantasyWorld.Sim/Views/Category.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Systems/LabyrinthTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`

**Interfaces:**
- Consumes: Tasks 3, 6; `DeedsSystem`, `StructureSystem.OpenLabyrinths`, `Encounters`.
- Produces: `StructureKind.Shrine`; `Structure { Levels; Depth; GuardianId; ClearedEventId }` (`Type` now settable); `MonsterState.Lairing`; `Monster { LairStructureId; GuardianBreeds }`; `Encounters.DelveLevel(world, delver, region, level)`; deed option `maze:<id>`; events `LabyrinthDelved`, `LabyrinthDelveFailed`, `LabyrinthCleared` (payload `becomes`); `LabyrinthOpened` names its `guardian`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Systems/LabyrinthTests.cs`:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class LabyrinthTests
{
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    /// <summary>A three-level labyrinth in region 2, its guardian, and a relic lost on its deepest level.</summary>
    private static (Structure Maze, Monster Guardian, Artifact Relic) Stage(World world, EventLog log, Character hero)
    {
        var opened = log.Append(world.Date, new EventDraft("LabyrinthOpened", Place: world.Regions[2].Ref));
        var maze = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Deeps of Eastmoor", Type = StructureKind.Labyrinth, RegionId = 2,
            StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing, Levels = 3,
        };
        world.Structures[maze.Id] = maze;
        var guardian = new Monster
        {
            Id = world.Ids.Take(Monster.Kind), Name = "The Minotaur", Breed = "minotaur", RegionId = 2, Might = 50,
            State = MonsterState.Lairing, RoseEventId = opened.Id, LairStructureId = maze.Id,
        };
        world.Monsters[guardian.Id] = guardian;
        maze.GuardianId = guardian.Id;
        var relic = TestPeople.GiveArtifact(world, hero); // once theirs, lost below
        Fates.Lose(relic, 2, opened.Id, hero.Id);
        world.Regions[2].Danger = 60;
        return (maze, guardian, relic);
    }

    private static Character Delver(World world)
    {
        var hero = TestPeople.Add(world, 1, 25, role: Role.Adventurer, traits: new[] { Traits.Devout });
        hero.Ambition = Ambitions.Artifact;
        return hero;
    }

    [Fact]
    public void ALabyrinth_OpensWithLevels_AndAGuardianThatKeepsToIt()
    {
        var world = TestPeople.World();
        var wild = world.Regions[2];
        (wild.LeyPower, wild.Danger) = (90, 50);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem(), new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 150);
        var opened = sim.Log.All.Single(e => e.Type == "LabyrinthOpened");
        var maze = world.Structures[opened.Actors["structure"].Id];
        Assert.InRange(maze.Levels, 3, 7);
        var guardian = world.Monsters[maze.GuardianId];
        Assert.Equal(guardian.Ref, opened.Actors["guardian"]);
        Assert.Equal((MonsterState.Lairing, maze.Id, wild.Id), (guardian.State, guardian.LairStructureId, guardian.RegionId));
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "MonsterRaid" && e.Actors["monster"] == guardian.Ref);
        Assert.True(wild.Danger >= guardian.Might / 2);
    }

    [Fact]
    public void Delvers_GoDownLevelByLevel_SlayTheGuardian_AndClearIt()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var hero = Delver(world);
        var (maze, guardian, relic) = Stage(world, log, hero);
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 8);

        Assert.Equal(new[] { "1", "2", "3" }, sim.Log.All.Where(e => e.Type == "LabyrinthDelved").Select(e => e.Payload["level"]));
        var slain = sim.Log.All.Single(e => e.Type == "MonsterSlain");
        Assert.Equal(guardian.Ref, slain.Actors["monster"]);
        Assert.Equal(MonsterState.Slain, guardian.State);
        Assert.Equal((ArtifactPlace.Held, hero.Id), (relic.Place, relic.HolderId));
        var cleared = sim.Log.All.Single(e => e.Type == "LabyrinthCleared");
        Assert.Equal(cleared.Id, maze.ClearedEventId);
        Assert.Equal((StructureKind.Shrine, StructureState.Standing), (maze.Type, maze.State));
        Assert.True(world.Regions[2].Danger < 60);
    }

    [Fact]
    public void AFailedDelve_GoesNoDeeper()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var (maze, _, _) = Stage(world, log, Delver(world));
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("maze"), new FixedArbiter("failure_wounded")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 6);
        Assert.Contains(sim.Log.All, e => e.Type == "LabyrinthDelveFailed" && e.Payload["level"] == "1");
        Assert.Equal(0, maze.Depth);
        Assert.Equal(0, maze.ClearedEventId);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json` entirely with:
```json
{
  "ref": "char#1",
  "name": "Fath of Cunvei",
  "house": "house#1",
  "kicker": "Character · House Cunvei",
  "line": "Lord of Drerithby · age 66 · born before recorded history",
  "traits": [
    "calm",
    "devout",
    "lazy",
    "trusting"
  ],
  "ambition": "Ambition: mastery of magic",
  "location": {
    "ref": "settlement#3",
    "label": "Now at Drerithby",
    "position": {
      "x": 0.3515625,
      "y": 0.1953125
    }
  },
  "timeline": [
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted Keth of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 58,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 32,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted End of Gil in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 10,
            "entity": "char#15"
          },
          {
            "start": 53,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 33,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 45,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei knighted Teilbeith of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 20,
            "entity": "char#18"
          },
          {
            "start": 63,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 126,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei and Lond of Cunvei became rivals over calm vs wrathful.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 19,
            "length": 14,
            "entity": "char#3"
          }
        ]
      },
      "eventId": 131,
      "highlight": false
    },
    {
      "year": 18,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 139,
      "highlight": false
    }
  ],
  "folded": 0,
  "earlier": 0,
  "ties": [
    {
      "ref": "char#2",
      "name": "Farnfand of Cunvei",
      "relation": "Spouse",
      "qualifier": "",
      "tone": "warm"
    },
    {
      "ref": "char#3",
      "name": "Lond of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    },
    {
      "ref": "char#4",
      "name": "Broth of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    }
  ],
  "followed": false
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/digest.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "year": 21,
  "title": "Year 21.",
  "subtitle": "While you were away, 20 years passed in Dromothholt. The seasons turned, and 153 events were recorded.",
  "elapsedDays": 7200,
  "status": "Caught up · Year 21, day 1",
  "pins": [
    {
      "number": 1,
      "place": "settlement#3",
      "label": "Drerithby",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "eventId": 30
    },
    {
      "number": 3,
      "place": "region#7",
      "label": "Gisreach",
      "position": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "eventId": 151
    },
    {
      "number": 4,
      "place": "region#3",
      "label": "Thonae",
      "position": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "eventId": 64
    },
    {
      "number": 5,
      "place": "settlement#16",
      "label": "Greindrondcross",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "eventId": 32
    },
    {
      "number": 8,
      "place": "region#2",
      "label": "Taramdale",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "eventId": 150
    }
  ],
  "headline": {
    "number": 1,
    "category": "politics",
    "kicker": "A new era",
    "title": {
      "text": "The Banners Age of Thonae began.",
      "links": []
    },
    "body": {
      "text": "Spring of Year 2.",
      "links": []
    },
    "eventId": 30,
    "canExplain": false
  },
  "items": [
    {
      "number": 2,
      "category": "politics",
      "text": {
        "text": "The Age of Long Peace of Thonae began.",
        "links": []
      },
      "eventId": 46
    },
    {
      "number": 3,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia slew the monsters of Gisreach.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 37,
            "length": 8,
            "entity": "region#7"
          }
        ]
      },
      "eventId": 151
    },
    {
      "number": 4,
      "category": "nature",
      "text": {
        "text": "Plague broke out in Thonae.",
        "links": [
          {
            "start": 20,
            "length": 6,
            "entity": "region#3"
          }
        ]
      },
      "eventId": 64
    },
    {
      "number": 5,
      "category": "characters",
      "text": {
        "text": "Fath of Cunvei knighted Keth of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 58,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 32
    },
    {
      "number": 6,
      "category": "characters",
      "text": {
        "text": "Gral of Bandbia and Nasgre of Stel became friends.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#16"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#19"
          }
        ]
      },
      "eventId": 67
    },
    {
      "number": 7,
      "category": "economy",
      "text": {
        "text": "Merchants opened The Drerithby–Greindrondcross Road between Drerithby and Greindrondcross.",
        "links": [
          {
            "start": 17,
            "length": 34,
            "entity": "route#2"
          },
          {
            "start": 60,
            "length": 9,
            "entity": "settlement#3"
          },
          {
            "start": 74,
            "length": 15,
            "entity": "settlement#16"
          }
        ]
      },
      "eventId": 47
    },
    {
      "number": 8,
      "category": "structures",
      "text": {
        "text": "Rarun of Cathreindis was driven back from level 3 of The Deeps of Taramdale.",
        "links": [
          {
            "start": 0,
            "length": 20,
            "entity": "char#39"
          },
          {
            "start": 53,
            "length": 22,
            "entity": "structure#1"
          }
        ]
      },
      "eventId": 150
    }
  ]
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, one open labyrinth.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      }
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      }
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      }
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      }
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      }
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      }
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      }
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      }
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      }
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      }
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      }
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      }
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      }
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      }
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      }
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      }
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      }
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      }
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      }
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      }
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      }
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      }
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      }
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      }
    }
  ],
  "structures": [
    {
      "ref": "structure#1",
      "name": "The Deeps of Taramdale",
      "kind": "Labyrinth",
      "state": "Standing",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      }
    }
  ],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ],
  "armies": [],
  "battles": [],
  "routes": [
    {
      "ref": "route#1",
      "name": "The Greindrondcross–Niar Road",
      "from": "settlement#16",
      "to": "settlement#24",
      "state": "open",
      "volume": 76,
      "path": [
        {
          "x": 0.8828125,
          "y": 0.4296875
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    },
    {
      "ref": "route#2",
      "name": "The Drerithby–Greindrondcross Road",
      "from": "settlement#3",
      "to": "settlement#16",
      "state": "open",
      "volume": 106,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.4921875,
          "y": 0.4296875
        },
        {
          "x": 0.8828125,
          "y": 0.4296875
        }
      ]
    },
    {
      "ref": "route#3",
      "name": "The Drerithby–Niar Road",
      "from": "settlement#3",
      "to": "settlement#24",
      "state": "open",
      "volume": 78,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    }
  ],
  "monsters": [
    {
      "ref": "monster#1",
      "name": "The Lich of the Deeps of Taramdale",
      "breed": "lich",
      "might": 61,
      "legendary": false,
      "slumbering": false,
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "roseEventId": 120
    }
  ]
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS0117: 'Structure' does not contain a definition for 'Levels'`.

- [x] **Step 3: Implement**

Replace `sim/FantasyWorld.Sim/Agency/Encounters.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Agency;

public enum Fate { Unharmed, Wounded, Maimed, Dead }

/// <summary>A legal result of an encounter, with its base probability from the odds.</summary>
public sealed record EncounterOutcome(string Key, double Probability, bool AttackerWins, Fate AttackerFate, Fate DefenderFate)
{
    /// <summary>How it happened, when an LLM arbitrated (spec §8.3): stored as the event's account.</summary>
    public string? Account { get; init; }
}

/// <summary>
/// A dramatic personal conflict (spec §8.3). The engine sets the stage (strengths) and enumerates the
/// legal outcomes with probabilities summing to 1; an arbiter picks one. DefenderId 0 means the wilds
/// (monsters, a labyrinth) rather than a person.
/// </summary>
public sealed record Encounter(
    string Kind,
    long AttackerId,
    long DefenderId,
    double AttackerStrength,
    double DefenderStrength,
    bool ToTheDeath,
    IReadOnlyList<EncounterOutcome> Outcomes)
{
    /// <summary>Outcomes below this base probability are implausible; the LLM arbiter (Plan 5) may not choose them.</summary>
    public const double PlausibilityFloor = 0.05;

    public double AttackerOdds => AttackerStrength / (AttackerStrength + DefenderStrength);

    public bool IsPlausible(EncounterOutcome outcome) => outcome.Probability >= PlausibilityFloor;
}

public interface IArbiter
{
    EncounterOutcome Resolve(Encounter encounter, Rng rng);
}

/// <summary>Seeded roll on the base probabilities: the offline fallback and the test baseline.</summary>
public sealed class RulesArbiter : IArbiter
{
    public EncounterOutcome Resolve(Encounter encounter, Rng rng)
    {
        var roll = rng.NextDouble();
        foreach (var outcome in encounter.Outcomes)
        {
            roll -= outcome.Probability;
            if (roll < 0) return outcome;
        }
        return encounter.Outcomes[^1];
    }
}

/// <summary>Builds encounters: strengths from attributes, traits, wounds, renown and artifacts; legal outcome sets.</summary>
public static class Encounters
{
    public const string Duel = "duel";
    public const string Hunt = "hunt";
    public const string Delve = "delve";

    /// <summary>Fighting strength: prowess + martial/2 + artifact bonuses + traits − wounds + renown/20 (min 1).</summary>
    public static double Strength(World world, Character c)
    {
        var artifacts = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList();
        var strength = c.Attributes.Prowess + artifacts.Sum(a => a.Bonus("prowess"))
                       + (c.Attributes.Martial + artifacts.Sum(a => a.Bonus("martial"))) / 2.0
                       + c.Renown / 20;
        if (c.Has(Traits.Brave)) strength += 2;
        if (c.Has(Traits.Craven)) strength -= 3;
        strength -= 2 * Math.Min(3, c.Wounds.Count);
        var age = c.Age(world.Date);
        if (age < 18) strength -= 4;
        if (age >= 55) strength -= (age - 50) / 5;
        return Math.Max(1, strength);
    }

    public static Encounter DuelBetween(World world, Character attacker, Character defender, bool toTheDeath)
    {
        var a = Strength(world, attacker);
        var d = Strength(world, defender);
        var p = a / (a + d);
        var outcomes = toTheDeath
            ? new List<EncounterOutcome>
            {
                new("attacker_kills", p * 0.97 * 0.6, true, Fate.Unharmed, Fate.Dead),
                new("attacker_maims", p * 0.97 * 0.2, true, Fate.Unharmed, Fate.Maimed),
                new("attacker_wounds", p * 0.97 * 0.2, true, Fate.Wounded, Fate.Wounded),
                new("defender_kills", (1 - p) * 0.97 * 0.6, false, Fate.Dead, Fate.Unharmed),
                new("defender_maims", (1 - p) * 0.97 * 0.2, false, Fate.Maimed, Fate.Unharmed),
                new("defender_wounds", (1 - p) * 0.97 * 0.2, false, Fate.Wounded, Fate.Wounded),
                new("both_die", 0.03, false, Fate.Dead, Fate.Dead),
            }
            : new List<EncounterOutcome>
            {
                new("attacker_first_blood", p * 0.8, true, Fate.Unharmed, Fate.Wounded),
                new("attacker_yields_win", p * 0.2, true, Fate.Unharmed, Fate.Unharmed),
                new("defender_first_blood", (1 - p) * 0.8, false, Fate.Wounded, Fate.Unharmed),
                new("defender_yields_win", (1 - p) * 0.2, false, Fate.Unharmed, Fate.Unharmed),
            };
        return new Encounter(Duel, attacker.Id, defender.Id, a, d, toTheDeath, outcomes);
    }

    /// <summary>Hunting the monsters of a region: the wilds' strength is danger / 4.</summary>
    public static Encounter HuntIn(World world, Character hunter, Region region) =>
        Wilds(Hunt, world, hunter, Math.Max(1, region.Danger / 4), deathShare: 0.15);

    /// <summary>Hunting a named beast: its strength is half its might, and losing to it is often fatal.</summary>
    public static Encounter HuntBeast(World world, Character hunter, Monster beast) =>
        Wilds(Hunt, world, hunter, Math.Max(1, beast.Might / 2), deathShare: 0.25);

    /// <summary>Delving for a lost artifact: the wilds' strength is danger / 3; failure is deadlier.</summary>
    /// <summary>Delving one level of a labyrinth: each level down is harder than the land above it.</summary>
    public static Encounter DelveLevel(World world, Character delver, Region region, int level) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3 + level * 2), deathShare: 0.3);

    public static Encounter DelveIn(World world, Character delver, Region region) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3), deathShare: 0.3);

    private static Encounter Wilds(string kind, World world, Character attacker, double wilds, double deathShare)
    {
        var a = Strength(world, attacker);
        var p = a / (a + wilds);
        var outcomes = new List<EncounterOutcome>
        {
            new("success", p * 0.7, true, Fate.Unharmed, Fate.Unharmed),
            new("success_wounded", p * 0.3, true, Fate.Wounded, Fate.Unharmed),
            new("failure_wounded", (1 - p) * (1 - deathShare) * 0.7, false, Fate.Wounded, Fate.Unharmed),
            new("failure_maimed", (1 - p) * (1 - deathShare) * 0.3, false, Fate.Maimed, Fate.Unharmed),
            new("failure_death", (1 - p) * deathShare, false, Fate.Dead, Fate.Unharmed),
        };
        return new Encounter(kind, attacker.Id, 0, a, wilds, true, outcomes);
    }
}
```

Replace `sim/FantasyWorld.Sim/Engine/Invariants.Structures.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.ArchitectId != 0 && !world.Characters.ContainsKey(s.ArchitectId)) errors.Add($"Structure #{s.Id} has unknown architect #{s.ArchitectId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Fortification && s.CompletedEventId == 0)
                errors.Add($"Fortress #{s.Id} stands without a completion event.");
            if (s.Depth < 0 || s.Levels > 0 && s.Depth > s.Levels) errors.Add($"Labyrinth #{s.Id} is delved to level {s.Depth} of {s.Levels}.");
            if (s.GuardianId != 0 && !world.Monsters.ContainsKey(s.GuardianId)) errors.Add($"Labyrinth #{s.Id} has unknown guardian #{s.GuardianId}.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }

        foreach (var r in world.Routes.Values)
        {
            if (!world.Settlements.ContainsKey(r.FromId) || !world.Settlements.ContainsKey(r.ToId)) errors.Add($"Route #{r.Id} joins an unknown market.");
            else if (r.Regions.Count == 0 || r.Regions[0] != world.Settlements[r.FromId].RegionId || r.Regions[^1] != world.Settlements[r.ToId].RegionId)
                errors.Add($"Route #{r.Id} does not run from market to market.");
            if (r.Regions.Any(id => !world.Regions.ContainsKey(id))) errors.Add($"Route #{r.Id} crosses an unknown region.");
            if (!log.TryGet(r.OpenedEventId, out _)) errors.Add($"Route #{r.Id} cites missing event #{r.OpenedEventId}.");
            if (r.State != RouteState.Open && r.Volume != 0) errors.Add($"Route #{r.Id} carries trade while {r.State}.");
        }

        foreach (var m in world.Monsters.Values)
        {
            if (!world.Regions.ContainsKey(m.RegionId)) errors.Add($"Monster #{m.Id} lairs in unknown region #{m.RegionId}.");
            if (m.Might is < 1 or > 100) errors.Add($"Monster #{m.Id} has might {m.Might}.");
            if (!log.TryGet(m.RoseEventId, out _)) errors.Add($"Monster #{m.Id} cites missing event #{m.RoseEventId}.");
            if (m.State == MonsterState.Lairing && !world.Structures.ContainsKey(m.LairStructureId)) errors.Add($"Guardian #{m.Id} guards no labyrinth.");
            if (m.State == MonsterState.Slain && m.SlainEventId == 0) errors.Add($"Monster #{m.Id} is slain without a slaying.");
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Model/Monster.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

public enum MonsterState { Roaming, Slumbering, Slain, Lairing }

/// <summary>
/// A named beast (sub-project 3 §3): what breed it is, where it lairs, how mighty and how infamous it has grown, and
/// how its story ended. Danger in its region never falls below half its might while it lives.
/// </summary>
public sealed class Monster
{
    public const string Kind = "monster";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Breed { get; init; } = "";
    public long RegionId { get; init; }

    /// <summary>1–100: how hard it is to kill, and how hard it raids.</summary>
    public double Might { get; set; }

    /// <summary>0–100: how widely it is feared; legends lead the digest.</summary>
    public double Notoriety { get; set; }

    public bool Legendary { get; set; }
    public long BornDay { get; init; }
    public MonsterState State { get; set; }
    public long RoseEventId { get; init; }
    public long SlainEventId { get; set; }
    public long SlayerId { get; set; }

    /// <summary>For a labyrinth's guardian (state Lairing): the labyrinth it guards; it never leaves to raid.</summary>
    public long LairStructureId { get; init; }

    /// <summary>What guards a labyrinth's deepest level.</summary>
    public static readonly IReadOnlyList<string> GuardianBreeds = new[] { "minotaur", "lich", "golem", "dragon" };

    [JsonIgnore] public bool Alive => State != MonsterState.Slain;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>The breeds that haunt each kind of land.</summary>
    public static IReadOnlyList<string> BreedsOf(Biome biome) => biome switch
    {
        Biome.Mountain => new[] { "wyrm", "troll", "roc" },
        Biome.Hills => new[] { "troll", "ogre", "griffin" },
        Biome.Forest => new[] { "wolf-king", "basilisk", "wild hunt" },
        Biome.Jungle => new[] { "basilisk", "manticore", "serpent" },
        Biome.Swamp => new[] { "hydra", "bog wight", "serpent" },
        Biome.Desert => new[] { "sand wyrm", "manticore", "sphinx" },
        Biome.Tundra or Biome.Taiga => new[] { "frost giant", "wendigo", "white wolf" },
        Biome.Savanna or Biome.Grassland => new[] { "chimera", "griffin", "wild hunt" },
        _ => new[] { "kraken", "serpent" },
    };
}
```

Replace `sim/FantasyWorld.Sim/Model/Structure.cs` entirely with:
```csharp
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum StructureKind { Landmark, Fortification, Labyrinth, Ruin, Shrine }

public enum StructureState { Building, Standing, Ruined }

/// <summary>
/// A place-defining work (spec §5 "Structures", minimal hooks expanded in sub-project 3): a landmark raised by a
/// lord over years, a labyrinth that opens in a dangerous, ley-rich region and hides artifacts, or the ruin an
/// abandoned settlement leaves behind.
/// </summary>
public sealed class Structure
{
    public const string Kind = "structure";

    public long Id { get; init; }
    public string Name { get; set; } = "";
    /// <summary>A cleared labyrinth becomes a ruin or a shrine.</summary>
    public StructureKind Type { get; set; }
    public long RegionId { get; init; }

    /// <summary>The settlement it stands in, 0 for the wilds (labyrinths).</summary>
    public long SettlementId { get; init; }

    /// <summary>The character who commissioned it, 0 if none.</summary>
    public long BuilderId { get; init; }

    /// <summary>The character who designs and oversees a great work (0 if none).</summary>
    public long ArchitectId { get; set; }

    /// <summary>Pushed on by an impatient patron: faster, but it may collapse.</summary>
    public bool Rushed { get; init; }

    /// <summary>A labyrinth's levels (3–7, 0 for one opened before levels), the deepest reached, its guardian and its clearing.</summary>
    public int Levels { get; set; }

    public int Depth { get; set; }
    public long GuardianId { get; set; }
    public long ClearedEventId { get; set; }

    /// <summary>The event (war, plague, famine) holding the work up, 0 when it goes on.</summary>
    public long DelayedByEventId { get; set; }

    public long StartedEventId { get; init; }
    public long CompletedEventId { get; set; }

    /// <summary>0–100 while building.</summary>
    public double Progress { get; set; }

    public StructureState State { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}
```

Replace `sim/FantasyWorld.Sim/Narration/TemplateNarrator.cs` entirely with:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} of {realm} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut by {=reason}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["WildMagic"] = "Wild magic broke loose at {@place}: a {=kind}.",
        ["MageTrained"] = "{mage} was trained in the mage tower of {@place}.",
        ["MagicLawChanged"] = "{realm} changed its law on magic from {=from} to {=law}.",
        ["MageFled"] = "{mage} fled the law against magic to {@place}.",
        ["WorksDelayed"] = "Work on {structure} stopped.",
        ["WorksCollapsed"] = "{structure} collapsed while it was being built; {=dead} died.",
        ["FortificationBegun"] = "{builder} began {structure} at {@place}.",
        ["FortificationCompleted"] = "{structure} was completed at {@place}.",
        ["RestorationBegun"] = "{builder} began to restore {structure}.",
        ["LabyrinthDelved"] = "{delver} reached level {=level} of {structure}.",
        ["LabyrinthDelveFailed"] = "{delver} was driven back from level {=level} of {structure}.",
        ["LabyrinthCleared"] = "{delver} cleared {structure}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} died of {=cause} at {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended: {victor} over {vanquished} ({=terms}).",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} of {realm} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} of {realm} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} for {=casus belli}: {war}.",
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = Templates.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events were recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? _nameOf(place) : null,
                _ when key.StartsWith('=') => ev.Payload.GetValueOrDefault(key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? _nameOf(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => _nameOf(a.Value)).Where(n => n is not null).ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {string.Join(" and ", who)}";
        if (ev.Place is { } place && _nameOf(place) is { } where) text += $" at {where}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}
```

Replace `sim/FantasyWorld.Sim/Systems/DeedsSystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What notables do with their lives (spec §3 "Monthly decisions"). Each month some adults act on
/// their ambition: a decision point offering hunts, delves, duels, study, forging or theft (plus
/// "carry on"). Risky deeds are encounters resolved by the arbiter; outcomes wound, kill, move
/// artifacts, tame regions and build renown. Every deed is an event citing its motive.
/// </summary>
public sealed class DeedsSystem : ISimSystem
{
    public const double ActPerMonth = 0.04;

    /// <summary>Artifacts the world can hold before forging becomes impossible (spec: 20–60 per world).</summary>
    public static int ArtifactCap(World world) => Math.Max(10, world.Regions.Count / 2);

    private readonly IDecider _decider;
    private readonly IArbiter _arbiter;
    private readonly IInventor _inventor;

    public DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null)
    {
        _inventor = inventor ?? new RulesInventor();
        _decider = decider ?? new RulesDecider();
        _arbiter = arbiter ?? new RulesArbiter();
    }

    public string Name => "deeds";

    public void OnMonthlyTick(SimContext ctx)
    {
        var residents = ctx.World.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var c in residents.SelectMany(g => g).OrderBy(c => c.Id).ToList())
        {
            if (!c.IsActive || c.Stage(ctx.Date) == LifeStage.Child) continue;
            if (c.Ambition == Ambitions.Vengeance
                && !(ctx.World.Characters.TryGetValue(c.AmbitionTarget, out var target) && target.IsActive && target.Id != c.Id))
            {
                c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
                c.AmbitionTarget = 0;
            }
            var drive = c.Has(Traits.Diligent) ? 1.5 : c.Has(Traits.Lazy) ? 0.5 : 1.0;
            if (!ctx.Rng.Chance(ActPerMonth * drive)) continue;
            var options = Options(ctx, c, residents[c.SettlementId]);
            if (options.Count <= 1) continue;
            var choice = _decider.Decide(new DecisionBrief(c.Id, c.Ambition, options), ctx.Rng);
            using (ctx.Annotate("motive", choice.Reasoning)) Act(ctx, c, choice.Key);
        }
    }

    /// <summary>The legal deeds for this character now, weighted by ambition and traits. Keys: verb:target.</summary>
    public static List<DecisionOption> Options(SimContext ctx, Character c, IEnumerable<Character>? neighbours = null)
    {
        var world = ctx.World;
        var home = world.Settlements[c.SettlementId];
        var nearby = Nearby(world, home.RegionId).ToList();
        var options = new List<DecisionOption> { new("rest", 30, "carry on as before") };
        var bold = c.Has(Traits.Brave) ? 2.0 : c.Has(Traits.Craven) ? 0.2 : 1.0;

        var beast = world.Monsters.Values.Where(m => m.State == MonsterState.Roaming && nearby.Any(r => r.Id == m.RegionId))
            .OrderByDescending(m => m.Notoriety).ThenBy(m => m.Id).FirstOrDefault();
        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (beast is not null)
            options.Add(new($"hunt:{beast.RegionId}", (c.Ambition == Ambitions.Renown ? 50 : 6) * bold, $"hunt {beast.Name} in {world.Regions[beast.RegionId].Name}"));
        else if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        // A labyrinth is delved level by level; the relics lost in its land lie on its deepest level.
        var maze = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.ClearedEventId == 0 && nearby.Any(r => r.Id == s.RegionId))
            .OrderBy(s => s.Id).FirstOrDefault();
        if (maze is not null)
            options.Add(new($"maze:{maze.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 25 : 2) * bold,
                $"delve into {maze.Name}, level {Math.Min(Math.Max(maze.Levels, 3), maze.Depth + 1)}"));

        var mazes = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.ClearedEventId == 0).Select(s => s.RegionId).ToHashSet();
        var lost = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && nearby.Any(r => r.Id == a.RegionId) && !mazes.Contains(a.RegionId))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (lost is not null)
            options.Add(new($"delve:{lost.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 10 : 2) * bold,
                $"seek {lost.Name} in {world.Regions[lost.RegionId].Name}"));

        if (c.Ambition == Ambitions.Artifact && (c.Has(Traits.Deceitful) || c.Has(Traits.Greedy)))
        {
            var coveted = world.Artifacts.Values
                .Where(a => a.Place == ArtifactPlace.Held && a.HolderId != c.Id && world.Characters[a.HolderId].SettlementId == c.SettlementId)
                .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
            if (coveted is not null) options.Add(new($"steal:{coveted.Id}", 10, $"steal {coveted.Name}"));
        }

        if (c.Ambition == Ambitions.Magic)
        {
            options.Add(new("study", 40, "study the arcane"));
            if (c.Attributes.Lore >= 15 && world.Regions[home.RegionId].LeyPower >= 50) options.Add(new("forge", 20, "forge an artifact"));
        }

        if (c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive && foe.Id != c.Id
            && nearby.Any(r => r.Id == world.Settlements[foe.SettlementId].RegionId))
            options.Add(new($"avenge:{foe.Id}", 50 * bold, $"challenge {foe.Name} to the death"));

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        if (rival is not null && (c.Has(Traits.Wrathful) || c.Has(Traits.Brave)))
            options.Add(new($"duel:{rival.Id}", 25 * bold, $"challenge {rival.Name} to first blood"));
        return options;
    }

    private void Act(SimContext ctx, Character c, string key)
    {
        var parts = key.Split(':');
        var target = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        switch (parts[0])
        {
            case "hunt": Hunt(ctx, c, ctx.World.Regions[target]); break;
            case "delve": Delve(ctx, c, ctx.World.Artifacts[target]); break;
            case "maze": Maze(ctx, c, ctx.World.Structures[target]); break;
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            case "avenge" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        if (ctx.World.Monsters.Values.Where(m => m.State == MonsterState.Roaming && m.RegionId == region.Id).OrderBy(m => m.Id).FirstOrDefault() is { } beast)
        {
            HuntBeast(ctx, hunter, region, beast);
            return;
        }
        var outcome = _arbiter.Resolve(Encounters.HuntIn(ctx.World, hunter, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonstersSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? 25 : 15), Tags: new[] { "Characters", "Deeds" }));
        if (outcome.AttackerWins)
        {
            region.Danger = Math.Max(0, region.Danger - 15);
            Glory(ctx, hunter, 8);
        }
        Suffer(ctx, hunter, outcome.AttackerFate, "monsters", hunt, lostIn: region.Id);
    }

    /// <summary>
    /// A hunt for a named beast: the arbiter weighs the hunter against its might. Slaying it ends its story (fame, a
    /// safer land, and from a mighty beast a trophy); failing feeds its notoriety.
    /// </summary>
    private void HuntBeast(SimContext ctx, Character hunter, Region region, Monster beast)
    {
        var world = ctx.World;
        var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, hunter, beast), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref, ["monster"] = beast.Ref },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: new[] { new Cause(ctx.Log.Involving(beast.Ref).LastOrDefault()?.Id ?? beast.RoseEventId, CauseKind.Trigger, 0.8) },
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? (int)(30 + beast.Might / 3 + (beast.Legendary ? 15 : 0)) : 18),
            Tags: new[] { "Characters", "Deeds", "Magic" }));
        if (outcome.AttackerWins)
        {
            beast.State = MonsterState.Slain;
            beast.SlainEventId = hunt.Id;
            beast.SlayerId = hunter.Id;
            region.Danger = Math.Max(0, region.Danger - 25);
            Glory(ctx, hunter, 8 + beast.Might / 8);
            if (beast.Might >= 60 && world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < ArtifactCap(world))
            {
                var trophy = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), hunt.Id,
                    fame: 20 + beast.Notoriety / 2);
                world.Artifacts[trophy.Id] = trophy;
                Fates.MoveToHolder(trophy, hunter, hunt.Id, "won");
            }
        }
        else beast.Notoriety = Math.Min(100, beast.Notoriety + 8);
        Suffer(ctx, hunter, outcome.AttackerFate, beast.Name, hunt, lostIn: region.Id);
    }

    private void Delve(SimContext ctx, Character delver, Artifact artifact)
    {
        var region = ctx.World.Regions[artifact.RegionId];
        var outcome = _arbiter.Resolve(Encounters.DelveIn(ctx.World, delver, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var delve = ctx.Emit(new EventDraft(outcome.AttackerWins ? "ArtifactFound" : "DelveFailed",
            Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = artifact.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: artifact.Provenance.Count > 0 ? new[] { new Cause(artifact.Provenance[^1].EventId, CauseKind.Enabler, 0.5) } : null,
            Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(20 + artifact.Fame / 3) : 15),
            Tags: new[] { "Characters", "Deeds", "Artifacts" }));
        if (outcome.AttackerWins)
        {
            Fates.MoveToHolder(artifact, delver, delve.Id, "found");
            artifact.Fame = Math.Min(100, artifact.Fame + 5);
            Glory(ctx, delver, 10);
        }
        Suffer(ctx, delver, outcome.AttackerFate, "a labyrinth's guardians", delve, lostIn: region.Id);
    }

    /// <summary>
    /// A delve one level deeper into a labyrinth (the depth reached is shared: the way down is remembered). At the
    /// deepest level the guardian bars the way; past it lie the relics lost in the land. Slaying the guardian and taking
    /// the last relic clears the labyrinth: the land grows safer, and it becomes a ruin, or a shrine if a devout hand cleared it.
    /// </summary>
    private void Maze(SimContext ctx, Character delver, Structure maze)
    {
        var world = ctx.World;
        var region = world.Regions[maze.RegionId];
        if (maze.Levels == 0) maze.Levels = 3;
        var actors = new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["structure"] = maze.Ref };
        WorldEvent last;
        if (maze.Depth < maze.Levels)
        {
            var level = maze.Depth + 1;
            var outcome = _arbiter.Resolve(Encounters.DelveLevel(world, delver, region, level), ctx.Rng);
            using (ctx.Annotate("account", outcome.Account))
                last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "LabyrinthDelved" : "LabyrinthDelveFailed", Actors: actors, Place: region.Ref,
                    Payload: new Dictionary<string, string> { ["level"] = level.ToString(CultureInfo.InvariantCulture), ["outcome"] = outcome.Key },
                    Causes: new[] { new Cause(ctx.Log.Involving(maze.Ref).LastOrDefault()?.Id ?? maze.StartedEventId, CauseKind.Enabler, 0.5) },
                    Importance: Fates.Importance(delver, outcome.AttackerWins ? 15 + 3 * level : 15), Tags: new[] { "Characters", "Deeds", "Structures" }));
            Suffer(ctx, delver, outcome.AttackerFate, maze.Name, last, lostIn: region.Id);
            if (!outcome.AttackerWins || !delver.IsActive) return;
            maze.Depth = level;
            Glory(ctx, delver, 2 + level);
            if (maze.Depth < maze.Levels) return;
        }
        else last = ctx.Log.Involving(maze.Ref).LastOrDefault() ?? ctx.Log.Get(maze.StartedEventId);

        if (world.Monsters.GetValueOrDefault(maze.GuardianId) is { Alive: true } guardian)
        {
            var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, delver, guardian), ctx.Rng);
            using var account = ctx.Annotate("account", outcome.Account);
            last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
                Actors: new Dictionary<string, EntityRef> { ["hunter"] = delver.Ref, ["monster"] = guardian.Ref }, Place: region.Ref,
                Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key }, Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(30 + guardian.Might / 3) : 18), Tags: new[] { "Characters", "Deeds", "Magic" }));
            if (!outcome.AttackerWins)
            {
                guardian.Notoriety = Math.Min(100, guardian.Notoriety + 8);
                Suffer(ctx, delver, outcome.AttackerFate, guardian.Name, last, lostIn: region.Id);
                return;
            }
            (guardian.State, guardian.SlainEventId, guardian.SlayerId) = (MonsterState.Slain, last.Id, delver.Id);
            Glory(ctx, delver, 8 + guardian.Might / 8);
            Suffer(ctx, delver, outcome.AttackerFate, guardian.Name, last, lostIn: region.Id);
            if (!delver.IsActive) return;
        }

        var relic = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id)
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (relic is not null)
        {
            last = ctx.Emit(new EventDraft("ArtifactFound", Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = relic.Ref },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = "success" }, Causes: new[] { new Cause(last.Id, CauseKind.Enabler, 0.8) },
                Importance: Fates.Importance(delver, (int)(20 + relic.Fame / 3)), Tags: new[] { "Characters", "Deeds", "Artifacts" }));
            Fates.MoveToHolder(relic, delver, last.Id, "found");
            relic.Fame = Math.Min(100, relic.Fame + 5);
            Glory(ctx, delver, 10);
        }
        if (world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id)) return;

        var shrine = delver.Has(Traits.Devout);
        actors = new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["structure"] = maze.Ref };
        maze.ClearedEventId = ctx.Emit(new EventDraft("LabyrinthCleared", Actors: actors, Place: region.Ref,
            Payload: new Dictionary<string, string> { ["becomes"] = shrine ? "shrine" : "ruin" }, Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.9) },
            Importance: Fates.Importance(delver, 50), Tags: new[] { "Characters", "Deeds", "Structures" })).Id;
        (maze.Type, maze.State) = shrine ? (StructureKind.Shrine, StructureState.Standing) : (StructureKind.Ruin, StructureState.Ruined);
        region.Danger = Math.Max(0, region.Danger - 20);
        Glory(ctx, delver, 10);
    }

    private static void Steal(SimContext ctx, Character thief, Artifact artifact)
    {
        var victim = ctx.World.Characters[artifact.HolderId];
        var guard = victim.Attributes.Intrigue + artifact.Bonus("intrigue") + 5 + (victim.Has(Traits.Paranoid) ? 5 : 0);
        var success = ctx.Rng.Chance(thief.Attributes.Intrigue / (double)(thief.Attributes.Intrigue + guard));
        var ev = ctx.Emit(new EventDraft(success ? "ArtifactStolen" : "TheftFoiled",
            Actors: new Dictionary<string, EntityRef> { ["thief"] = thief.Ref, ["victim"] = victim.Ref, ["artifact"] = artifact.Ref },
            Place: ctx.World.Settlements[thief.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(victim, 15), (int)(10 + artifact.Fame / 3)), Tags: new[] { "Characters", "Artifacts" }));
        Fates.Remember(victim, ev.Id, thief.Id, success ? -80 : -50, 80);
        if (success)
        {
            Fates.MoveToHolder(artifact, thief, ev.Id, "stolen");
            SwearVengeance(ctx, victim, thief);
        }
        else Shame(thief);
    }

    private void Forge(SimContext ctx, Character mage)
    {
        var world = ctx.World;
        var region = world.Regions[world.Settlements[mage.SettlementId].RegionId];
        var room = 1 - world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) / (double)ArtifactCap(world);
        if (!ctx.Rng.Chance(Math.Max(0, room) * (mage.Attributes.Lore - 12) / 30.0)) return;
        var artifactRef = new EntityRef(Artifact.Kind, world.Ids.Peek(Artifact.Kind));
        var forged = ctx.Emit(new EventDraft("ArtifactForged",
            Actors: new Dictionary<string, EntityRef> { ["maker"] = mage.Ref, ["artifact"] = artifactRef },
            Place: world.Settlements[mage.SettlementId].Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(mage, 35), Tags: new[] { "Characters", "Artifacts" }));
        var artifact = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(mage.CultureId)?.Style),
            forged.Id, rareMaterials: region.Resources.Keys.Where(ResourceKinds.IsRare).ToList());
        var powers = string.Join(", ", artifact.Powers.Select(p => p.Attribute is null ? p.Kind : $"+{p.Amount} {p.Attribute}"));
        var invention = _inventor.Invent(new InventionBrief("artifact", artifact.Ref.ToString(),
            $"A {artifact.Type.ToString().ToLowerInvariant()} of {artifact.Material}, granting {powers}{(artifact.Curse is null ? "" : $", bearing a hidden curse of {artifact.Curse}")}; "
            + $"forged by {mage.Name}, a {world.Cultures.GetValueOrDefault(mage.CultureId)?.Name ?? "nameless"} mage, at {world.Settlements[mage.SettlementId].Name} in {region.Name}.",
            artifact.Name));
        (artifact.Name, artifact.Lore) = (invention.Name, invention.Lore ?? "");
        world.Artifacts[artifact.Id] = artifact;
        Fates.MoveToHolder(artifact, mage, forged.Id, "forged");
        Glory(ctx, mage, 10);
    }

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var duel = ctx.Emit(new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key },
            Causes: grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : null,
            Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 30 : 15), Fates.Importance(defender, toTheDeath ? 30 : 15)),
            Tags: new[] { "Characters", "Deeds" }));
        var (winner, loser) = outcome.AttackerWins ? (challenger, defender) : (defender, challenger);
        Glory(ctx, winner, toTheDeath ? 8 : 4);
        Fates.Remember(loser, duel.Id, winner.Id, -60, 60);
        if (!toTheDeath && ctx.Rng.Chance(0.5)) Shame(loser);
        if (!toTheDeath && loser.Id != winner.Id) SwearVengeance(ctx, loser, winner);
        if (toTheDeath && outcome.AttackerWins && challenger.Ambition == Ambitions.Vengeance)
            challenger.Ambition = CharacterFactory.ChooseAmbition(challenger, ctx.Rng);
        Suffer(ctx, challenger, outcome.AttackerFate, "a duel", duel, outcome.AttackerFate == Fate.Dead ? defender : null);
        Suffer(ctx, defender, outcome.DefenderFate, "a duel", duel, outcome.DefenderFate == Fate.Dead ? challenger : null);
    }

    private static void Suffer(SimContext ctx, Character c, Fate fate, string cause, WorldEvent deed,
        Character? killer = null, long lostIn = 0)
    {
        if (fate == Fate.Dead)
        {
            if (c.IsAlive)
                Fates.Die(ctx, c, cause, new[] { new Cause(deed.Id, CauseKind.Trigger, 1.0) },
                    killer is { IsAlive: true } ? killer : null, lostIn);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
    }

    /// <summary>Renown for the deed; the artifacts they carry grow famous with them.</summary>
    private static void Glory(SimContext ctx, Character c, double renown)
    {
        c.Renown = Math.Min(100, c.Renown + renown);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
        foreach (var a in ctx.World.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id))
            a.Fame = Math.Min(100, a.Fame + renown / 2);
    }

    /// <summary>The wrathful and the cruel do not forgive a humiliation or a theft: half of them swear vengeance.</summary>
    private static void SwearVengeance(SimContext ctx, Character wronged, Character wrongdoer)
    {
        if (wronged.Has(Traits.Craven) || !(wronged.Has(Traits.Wrathful) || wronged.Has(Traits.Cruel))) return;
        if (wronged.Ambition == Ambitions.Vengeance || !ctx.Rng.Chance(0.5)) return;
        wronged.Ambition = Ambitions.Vengeance;
        wronged.AmbitionTarget = wrongdoer.Id;
    }

    private static void Shame(Character c)
    {
        if (c.Has(Traits.Humiliated)) return;
        c.Traits.Add(Traits.Humiliated);
        c.Traits.Sort(StringComparer.Ordinal);
    }

    private static IEnumerable<Region> Nearby(World world, long regionId) =>
        new[] { regionId }.Concat(world.Geography?.Graph.Neighbours(regionId) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]);
}
```

Replace `sim/FantasyWorld.Sim/Systems/FaithSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Faiths (sub-project 3 §4). Each year a region's people may turn to the faith pressing hardest on them: their
/// realm's faith and their neighbours' faiths, stronger where temples and shrines stand, weaker for ascetic or
/// tolerant faiths; their own temples hold them. A realm whose lands have mostly turned takes up the new faith.
/// A faith whose holy land falls to a realm of another faith grows zealous until it is regained; a zealous faith under
/// that strain, or with its realm in decline, may split when a priest founds a heresy, and nearby lands choose. Devout
/// notables of renown may be declared saints after death, and their relics, kept in their town, hold its people.
/// </summary>
public sealed class FaithSystem(IInventor? inventor = null) : ISimSystem
{
    public const double SchismZeal = 50;
    public const double SchismPerYear = 0.08;
    public const double SaintRenown = 45;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    /// <summary>At most one faith per ten regions (and at least four), so schisms stay rare.</summary>
    public static int Cap(World world) => Math.Max(4, world.Regions.Count / 10);

    public const double ConvertPerYear = 0.04;
    public const int SettleYears = 40;
    public const double RealmShare = 2.0 / 3;
    public const double LossZeal = 30;

    public string Name => "faith";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        Spread(ctx);
        RealmsConvert(ctx);
        HolySites(ctx);
        Schisms(ctx);
        Saints(ctx);
    }

    /// <summary>Temples and saints' relics count fully, shrines by half.</summary>
    public static double Devotion(World world, Region region)
    {
        var towns = world.LivingSettlements(region).ToList();
        var relics = world.Artifacts.Values.Count(a => a is { Type: ArtifactType.Relic, Place: ArtifactPlace.Treasury } && towns.Any(t => t.Id == a.SettlementId));
        return relics + towns.Sum(s => (s.Buildings.Contains("temple") ? 1 : 0) + (s.Buildings.Contains("shrine") ? 0.5 : 0));
    }

    /// <summary>Each other faith's pull on the region.</summary>
    public static SortedDictionary<long, double> Pressure(World world, Region region)
    {
        var pull = new SortedDictionary<long, double>();
        void Add(long faith, double weight)
        {
            if (faith == 0 || faith == region.FaithId || !world.Faiths.TryGetValue(faith, out var f)) return;
            if (f.Has("ascetic") || f.Has("tolerant")) weight *= 0.6;
            pull[faith] = pull.GetValueOrDefault(faith) + weight * (1 + f.Zeal / 100);
        }
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner)) Add(owner.FaithId, 3);
        foreach (var n in world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
        {
            var neighbour = world.Regions[n];
            Add(neighbour.FaithId, 1 + Devotion(world, neighbour) / 2);
        }
        return pull;
    }

    /// <summary>The faith pressing hardest on a peopled region and the yearly chance it wins them (0 if none).</summary>
    public static (long Faith, double Odds) Odds(World world, Region region)
    {
        if (region.FaithId == 0 || !world.LivingSettlements(region).Any()) return (0, 0);
        var (faith, pull) = Pressure(world, region).OrderByDescending(p => p.Value).ThenBy(p => p.Key).FirstOrDefault();
        return faith == 0 ? (0, 0) : (faith, ConvertPerYear * pull / (pull + 3 + Devotion(world, region)));
    }

    private static void Spread(SimContext ctx)
    {
        var world = ctx.World;
        var turned = new List<(Region Region, long Faith)>();
        foreach (var region in world.Regions.Values)
        {
            if (Settling(ctx, region)) continue;
            var (faith, odds) = Odds(world, region);
            if (faith != 0 && ctx.Rng.Chance(odds)) turned.Add((region, faith));
        }
        // Decided on last year's map, applied together, so the order of regions does not matter.
        foreach (var (region, faith) in turned) Convert(ctx, region, faith);
    }

    /// <summary>A people that turned within a generation keep their new faith a while.</summary>
    private static bool Settling(SimContext ctx, Region region) =>
        ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "RegionConverted") is { } turned
        && ctx.Date.Day - turned.Date.Day < SettleYears * (long)Core.WorldDate.DaysPerYear;

    private static void Convert(SimContext ctx, Region region, long faithId, Cause? because = null)
    {
        var world = ctx.World;
        var old = world.Faiths[region.FaithId];
        var faith = world.Faiths[faithId];
        var causes = new List<Cause>();
        if (because is { } b) causes.Add(b);
        var neighbours = world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>();
        var spread = neighbours.SelectMany(n => ctx.Log.Involving(world.Regions[n].Ref))
            .Where(e => e.Type == "RegionConverted" && e.Actors.GetValueOrDefault("faith") == faith.Ref)
            .OrderBy(e => e.Id).LastOrDefault();
        if (spread is not null) causes.Add(new Cause(spread.Id, CauseKind.Enabler, 0.6));
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner) && owner.FaithId == faithId)
        {
            var taken = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded").OrderBy(e => e.Id).LastOrDefault();
            causes.Add(new Cause(taken?.Id ?? owner.FoundedEventId, CauseKind.Enabler, 0.5));
        }
        region.FaithId = faithId;
        ctx.Emit(new EventDraft("RegionConverted", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["old"] = old.Ref },
            Place: region.Ref, Causes: causes.Where(c => c.EventId != 0).ToArray(), Importance: 25, Tags: new[] { "Faith" }));
    }

    private static void RealmsConvert(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            var domain = Realms.Domain(world, realm).ToList();
            if (domain.Count < 2) continue;
            var (faith, count) = domain.GroupBy(r => r.FaithId).Select(g => (g.Key, g.Count()))
                .OrderByDescending(g => g.Item2).ThenBy(g => g.Key).First();
            if (faith == realm.FaithId || faith == 0 || count < domain.Count * RealmShare || !ctx.Rng.Chance(0.2)) continue;
            var last = domain.SelectMany(r => ctx.Log.Involving(r.Ref))
                .Where(e => e.Type == "RegionConverted" && e.Actors["faith"].Id == faith).OrderBy(e => e.Id).LastOrDefault();
            realm.FaithId = faith;
            realm.Stability = Math.Max(0, realm.Stability - 10);
            if (Realms.RulerOf(world, realm) is { } ruler) ruler.FaithId = faith;
            ctx.Emit(new EventDraft("RealmConverted", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["faith"] = world.Faiths[faith].Ref },
                Place: world.Settlements[realm.CapitalId].Ref, Causes: last is null ? null : new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: 50, Tags: new[] { "Faith", "Politics" }));
        }
    }

    private static void HolySites(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values)
        {
            // Zeal cools toward the faith's temper, which runs hotter while its holy land is lost.
            var calm = (faith.Has("zealous") ? 40 : 10) + (faith.HolySiteLostEventId != 0 ? LossZeal : 0);
            faith.Zeal = Math.Max(Math.Min(faith.Zeal, calm), faith.Zeal - 3);
            if (!world.Regions.TryGetValue(faith.HolyRegionId, out var holy)) continue;
            var holder = Realms.SovereignOf(world, holy);
            var lost = holder is not null && holder.FaithId != faith.Id;
            if (lost && faith.HolySiteLostEventId == 0)
            {
                // Lost only by its cession to the holder or the holder's change of faith: a holy land never held is not a loss.
                var taken = ctx.Log.Involving(holy.Ref)
                    .Where(e => e.Type == "RegionCeded" && e.Actors.TryGetValue("to", out var to) && world.Realms.TryGetValue(to.Id, out var gainer)
                                && Realms.Top(world, gainer).Id == holder!.Id)
                    .Concat(ctx.Log.Involving(holder!.Ref).Where(e => e.Type == "RealmConverted"))
                    .OrderBy(e => e.Id).LastOrDefault();
                if (taken is null) continue;
                faith.Zeal = Math.Min(100, faith.Zeal + LossZeal);
                faith.HolySiteLostEventId = ctx.Emit(new EventDraft("HolySiteLost",
                    Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["realm"] = holder.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(taken.Id, CauseKind.Trigger, 0.8) }, Importance: 45, Tags: new[] { "Faith" })).Id;
            }
            else if (holder?.FaithId == faith.Id && faith.HolySiteLostEventId != 0)
            {
                var loss = faith.HolySiteLostEventId;
                faith.HolySiteLostEventId = 0;
                faith.Zeal = Math.Min(100, faith.Zeal + 10);
                ctx.Emit(new EventDraft("HolySiteRegained", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(loss, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            }
        }
    }

    private void Schisms(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values.Where(f => f.Zeal >= SchismZeal).ToList())
        {
            if (world.Faiths.Count >= Cap(world)) return;
            var strain = faith.HolySiteLostEventId != 0 ? faith.HolySiteLostEventId
                : world.Realms.Values.Where(r => !r.Destroyed && r.FaithId == faith.Id && r.Stability < 30)
                    .SelectMany(r => ctx.Log.Involving(r.Ref)).Where(e => e.Type == "RealmDiminished").Select(e => e.Id).DefaultIfEmpty().Max();
            if (strain == 0 || !ctx.Rng.Chance(SchismPerYear)) continue;
            var heresiarch = world.Characters.Values
                .Where(c => c.IsActive && c.Role == Role.Priest && c.FaithId == faith.Id && c.Stage(ctx.Date) != LifeStage.Child
                            && world.Settlements.TryGetValue(c.SettlementId, out var home) && !home.Abandoned)
                .OrderByDescending(c => c.Attributes.Lore).ThenBy(c => c.Id).FirstOrDefault();
            if (heresiarch is null) continue;
            Split(ctx, faith, heresiarch, strain);
        }
    }

    private void Split(SimContext ctx, Faith old, Character heresiarch, long strain)
    {
        var world = ctx.World;
        var home = world.Regions[world.Settlements[heresiarch.SettlementId].RegionId];
        var tenets = old.Tenets.ToList();
        if (tenets.Count > 0) tenets.RemoveAt(ctx.Rng.NextInt(0, tenets.Count));
        var fresh = Faith.TenetPool.Where(t => !old.Tenets.Contains(t)).ToList();
        if (fresh.Count > 0) tenets.Add(fresh[ctx.Rng.NextInt(0, fresh.Count)]);
        tenets.Sort(StringComparer.Ordinal);

        var id = world.Ids.Take(Faith.Kind);
        var fallback = old.Name.StartsWith("The True ", StringComparison.Ordinal) ? $"The Rite of {heresiarch.Name}"
            : "The True " + (old.Name.StartsWith("The ", StringComparison.Ordinal) ? old.Name[4..] : old.Name);
        var invention = _inventor.Invent(new InventionBrief("faith", $"{Faith.Kind}#{id}",
            $"A heresy of {old.Name} (worship of {old.Deity}) founded by the priest {heresiarch.Name} in {home.Name} in year {ctx.Date.Year}; tenets: {string.Join(", ", tenets)}.",
            fallback));
        using var lore = ctx.Annotate("lore", invention.Lore);
        var schism = ctx.Emit(new EventDraft("SchismBegan",
            Actors: new Dictionary<string, EntityRef> { ["heresiarch"] = heresiarch.Ref, ["faith"] = new(Faith.Kind, id), ["old"] = old.Ref },
            Place: world.Settlements[heresiarch.SettlementId].Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
            Causes: new[] { new Cause(strain, CauseKind.Motive, 0.8) }, Importance: 55, Tags: new[] { "Faith" }));
        world.Faiths[id] = new Faith
        {
            Id = id, Name = invention.Name, Deity = old.Deity, Tenets = tenets, HolyRegionId = home.Id, Zeal = 60,
            ParentFaithId = old.Id, FounderId = heresiarch.Id, FoundedEventId = schism.Id,
        };
        old.Zeal = Math.Max(0, old.Zeal - 20);
        heresiarch.FaithId = id;
        heresiarch.Renown = Math.Min(100, heresiarch.Renown + 10);

        var follow = new Cause(schism.Id, CauseKind.Trigger, 0.9);
        if (home.FaithId == old.Id) Convert(ctx, home, id, follow);
        foreach (var n in world.Geography?.Graph.Neighbours(home.Id) ?? Array.Empty<long>())
            if (world.Regions[n] is { } near && near.FaithId == old.Id && world.LivingSettlements(near).Any() && ctx.Rng.Chance(0.4))
                Convert(ctx, near, id, follow);
    }

    private static void Saints(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - Core.WorldDate.DaysPerYear;
        foreach (var c in world.Characters.Values.Where(c => c.Died is { } d && d.Day >= since && c.SaintEventId == 0 && c.Has(Traits.Devout)
                                                             && c.Renown >= SaintRenown && world.Faiths.ContainsKey(c.FaithId)).ToList())
        {
            var faith = world.Faiths[c.FaithId];
            var death = ctx.Log.Involving(c.Ref).LastOrDefault(e => e.Type == "CharacterDied");
            var room = world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world);
            var town = room && world.Settlements.GetValueOrDefault(c.SettlementId) is { Abandoned: false } s ? s : null;
            var relicId = town is null ? 0 : world.Ids.Take(Artifact.Kind);
            var actors = new Dictionary<string, EntityRef> { ["saint"] = c.Ref, ["faith"] = faith.Ref };
            if (relicId != 0) actors["relic"] = new EntityRef(Artifact.Kind, relicId);
            var declared = ctx.Emit(new EventDraft("SaintDeclared", Actors: actors, Place: town?.Ref,
                Causes: death is null ? null : new[] { new Cause(death.Id, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            c.SaintEventId = declared.Id;
            faith.Zeal = Math.Min(100, faith.Zeal + 5);
            if (town is null) continue;
            world.Artifacts[relicId] = new Artifact
            {
                Id = relicId, Name = $"The Relics of Saint {c.Name}", Type = ArtifactType.Relic, Material = "bone", OriginEventId = declared.Id,
                Fame = Math.Min(100, c.Renown / 2), Place = ArtifactPlace.Treasury, SettlementId = town.Id,
                Powers = { new ArtifactPower("devotion", null, 1) },
                Provenance = { new ProvenanceEntry(declared.Id, c.Id, 0, "enshrined") },
            };
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/MonsterSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Monsters (sub-project 3 §3). Each year a named beast may rise in a dangerous land (more often after a ley surge),
/// of a breed that fits the land, mightier where the land is wilder; beasts grow with age, and very old ones may
/// slumber until danger or a surge wakes them. Each month a roaming beast may raid a settlement in or next to its
/// land: people, food and wealth are lost, unrest rises, and the beast's notoriety grows until it becomes a legend.
/// Heroes answer them (hunts in <see cref="DeedsSystem"/>).
/// </summary>
public sealed class MonsterSystem(IInventor? inventor = null) : ISimSystem
{
    public const double RiseDanger = 55;
    public const double RisePerYear = 0.05;
    public const double RaidPerMonth = 0.03;
    public const double LegendNotoriety = 60;
    public const int SlumberAgeYears = 120;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "monsters";

    public static int Cap(World world) => Math.Max(1, world.Regions.Count / 8);

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.State is MonsterState.Roaming or MonsterState.Lairing).ToList())
        {
            var lair = world.Regions[beast.RegionId];
            lair.Danger = Math.Max(lair.Danger, beast.Might / 2);
            if (beast.State == MonsterState.Lairing || !ctx.Rng.Chance(RaidPerMonth * (0.5 + beast.Might / 100))) continue;
            Raid(ctx, beast, lair);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.Alive).ToList())
        {
            var age = (ctx.Date.Day - beast.BornDay) / WorldDateYears;
            if (beast.State == MonsterState.Roaming)
            {
                beast.Might = Math.Min(100, beast.Might + (age < 60 ? 1 : 0));
                if (age >= SlumberAgeYears && ctx.Rng.Chance(0.1))
                {
                    beast.State = MonsterState.Slumbering;
                    ctx.Emit(new EventDraft("MonsterSlumbered", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                        Place: world.Regions[beast.RegionId].Ref, Importance: 25, Tags: new[] { "Magic" }));
                }
            }
            else if (beast.State == MonsterState.Slumbering && world.Regions[beast.RegionId].Conditions.TryGetValue(Conditions.LeySurge, out var surge)
                     && ctx.Rng.Chance(0.3))
            {
                beast.State = MonsterState.Roaming;
                ctx.Emit(new EventDraft("MonsterWoke", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                    Place: world.Regions[beast.RegionId].Ref, Causes: new[] { new Cause(surge, CauseKind.Trigger, 0.9) },
                    Importance: 45, Tags: new[] { "Magic" }));
            }
        }
        Rise(ctx);
    }

    private const double WorldDateYears = Core.WorldDate.DaysPerYear;

    private void Rise(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Monsters.Values.Count(m => m.State is MonsterState.Roaming or MonsterState.Slumbering) >= Cap(world)) return; // guardians aside
        var haunted = world.Monsters.Values.Where(m => m.Alive).Select(m => m.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.Danger >= RiseDanger && !haunted.Contains(r.Id)).ToList())
        {
            var surge = region.Conditions.TryGetValue(Conditions.LeySurge, out var surgeEvent);
            if (!ctx.Rng.Chance(RisePerYear * (surge ? 3 : 1) * region.Danger / 100)) continue;
            var breeds = Monster.BreedsOf(region.Biome);
            var breed = breeds[ctx.Rng.NextInt(0, breeds.Count)];
            var id = world.Ids.Take(Monster.Kind);
            var invention = _inventor.Invent(new InventionBrief("monster", $"{Monster.Kind}#{id}",
                $"A {breed} that rose in the wild land of {region.Name} ({region.Biome}) in year {ctx.Date.Year}.",
                $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var rose = ctx.Emit(new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["breed"] = breed },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.8), (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
                Importance: 40, Tags: new[] { "Magic" }));
            world.Monsters[id] = new Monster
            {
                Id = id, Name = invention.Name, Breed = breed, RegionId = region.Id, Might = Math.Clamp(20 + region.Danger / 2 + ctx.Rng.NextInt(0, 21), 1, 100),
                BornDay = ctx.Date.Day, State = MonsterState.Roaming, RoseEventId = rose.Id,
            };
            return; // at most one a year
        }
    }

    private static void Raid(SimContext ctx, Monster beast, Region lair)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        var nearby = new[] { lair.Id }.Concat(graph?.Neighbours(lair.Id) ?? Array.Empty<long>()).ToHashSet();
        var target = world.Settlements.Values.Where(s => !s.Abandoned && nearby.Contains(s.RegionId))
            .OrderBy(s => s.RegionId == lair.Id ? 0 : 1).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
        if (target is null) return;
        var share = beast.Might / 1000.0; // up to a tenth of the people
        var dead = (int)(target.Adults * share);
        target.Adults -= dead;
        world.Regions[target.RegionId].Food *= 1 - share;
        target.Wealth *= 1 - share;
        target.Unrest = Math.Min(100, target.Unrest + 15);
        beast.Notoriety = Math.Min(100, beast.Notoriety + 5 + beast.Might / 10);
        var previous = ctx.Log.Involving(beast.Ref).LastOrDefault();
        var raid = ctx.Emit(new EventDraft("MonsterRaid", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
            Place: target.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: previous is null ? null : new[] { new Cause(previous.Id, CauseKind.Enabler, 0.5) },
            Importance: (int)Math.Clamp(25 + beast.Might / 3, 25, 60), Tags: new[] { "Magic" }));
        // Merchants shun a land a beast has raided (the spec's raid that severs a road).
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open && r.Regions.Contains(target.RegionId)).ToList())
            TradeSystem.Sever(ctx, route, "raid", target.Ref, raid.Id);
        if (!beast.Legendary && beast.Notoriety >= LegendNotoriety)
        {
            beast.Legendary = true;
            ctx.Emit(new EventDraft("MonsterLegendary", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                Place: lair.Ref, Causes: new[] { new Cause(beast.RoseEventId, CauseKind.Enabler, 0.6) }, Importance: 60, Tags: new[] { "Magic" }));
        }
    }
}
```

Replace `sim/FantasyWorld.Sim/Systems/StructureSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Structures (spec §5, minimal hooks for sub-project 3), yearly and last in the tick order. Wealthy lords commission
/// landmarks that take years to raise (paid from the settlement's wealth; completion brings renown). Labyrinths open
/// in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers. Abandoned settlements
/// leave ruins, and their unfinished or standing landmarks fall with them.
/// Great works (sub-project 3 §6): a landmark has an architect; war, plague or famine hold the work up; an impatient
/// patron rushes it, at the risk of collapse; on completion the patron gains renown and the realm stability. Towns that
/// have known siege raise fortresses, which strengthen their defence. A lord may restore a fallen landmark.
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

    public const double RushedCollapse = 0.08;
    public const double FortressPerYear = 0.03;
    public const double FortressWealth = 2500;
    public const int FortressDefence = 400;
    public const double RestorePerYear = 0.05;

    private static readonly string[] Works = { "Spire", "Great Hall", "Bridge", "Temple", "Library", "Colossus", "Lighthouse", "Garden" };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "structures";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        Ruin(ctx);
        Build(ctx);
        Commission(ctx);
        Fortify(ctx);
        Restore(ctx);
        OpenLabyrinths(ctx);
    }

    /// <summary>A standing fortress guards the town.</summary>
    public static bool Fortified(World world, Settlement s) =>
        world.Structures.Values.Any(x => x.Type == StructureKind.Fortification && x.State == StructureState.Standing && x.SettlementId == s.Id);

    /// <summary>The town's best designer: a grown notable, not its lord, by lore and stewardship.</summary>
    public static Character? Architect(World world, Settlement seat, Core.WorldDate today) =>
        world.ActiveCharacters.Where(c => c.SettlementId == seat.Id && c.Id != seat.LordId && c.Stage(today) != LifeStage.Child)
            .OrderByDescending(c => c.Attributes.Lore + c.Attributes.Stewardship).ThenBy(c => c.Id).FirstOrDefault();

    public static int LabyrinthCap(World world) => Math.Max(1, world.Regions.Count / 15);

    private void Commission(SimContext ctx)
    {
        var world = ctx.World;
        var busy = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= LandmarkWealth
                                                                 && !busy.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            if (!ctx.Rng.Chance(CommissionPerYear)) continue;
            var fallback = $"The {Works[ctx.Rng.NextInt(0, Works.Length)]} of {seat.Name}";
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("landmark", $"{Structure.Kind}#{id}",
                $"A landmark commissioned by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}.", fallback));
            seat.Wealth *= 0.8;
            var architect = Architect(world, seat, ctx.Date);
            var actors = new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) };
            if (architect is not null) actors["architect"] = architect.Ref;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun", Actors: actors,
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = architect?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
                Rushed = lord.Has(Traits.Ambitious) || lord.Has(Traits.Wrathful),
            };
        }
    }

    /// <summary>What holds a work up this year: war at the town, plague or famine in its land (the event that began it).</summary>
    private static long Hindrance(SimContext ctx, Settlement seat)
    {
        var world = ctx.World;
        var region = world.Regions[seat.RegionId];
        if (region.OccupierRealmId != 0 || region.BesiegerArmyId != 0)
            return ctx.Log.Involving(region.Ref).Concat(ctx.Log.Involving(seat.Ref))
                .Where(e => e.Type is "RegionOccupied" or "SiegeBegan" or "SiegeEnded").Select(e => e.Id).DefaultIfEmpty().Max();
        var plague = region.Conditions.GetValueOrDefault(Conditions.Plague);
        return plague != 0 ? plague : region.Conditions.GetValueOrDefault(Conditions.Shortage);
    }

    private void Fortify(SimContext ctx)
    {
        var world = ctx.World;
        var guarded = world.Structures.Values.Where(s => s.Type == StructureKind.Fortification && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= FortressWealth
                                                                 && !guarded.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            var region = world.Regions[seat.RegionId];
            var siege = ctx.Log.Involving(seat.Ref).Concat(ctx.Log.Involving(region.Ref))
                .Where(e => e.Type is "SiegeEnded" or "RegionOccupied").OrderBy(e => e.Id).LastOrDefault();
            if (siege is null || !ctx.Rng.Chance(FortressPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("fortress", $"{Structure.Kind}#{id}",
                $"A fortress raised by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}, after the town knew war.", $"The Walls of {seat.Name}"));
            seat.Wealth *= 0.8;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("FortificationBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: new[] { new Cause(siege.Id, CauseKind.Motive, 0.8) }, Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures", "War" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = Architect(world, seat, ctx.Date)?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Restore(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s is { Type: StructureKind.Landmark or StructureKind.Fortification, State: StructureState.Ruined }).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned || seat.Wealth < LandmarkWealth || !world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive) continue;
            if (!ctx.Rng.Chance(RestorePerYear)) continue;
            var fell = ctx.Log.Involving(work.Ref).LastOrDefault(e => e.Type is "WorksCollapsed" or "StructureRuined");
            seat.Wealth *= 0.85;
            work.State = StructureState.Building;
            work.Progress = 50;
            work.DelayedByEventId = 0;
            ctx.Emit(new EventDraft("RestorationBegun", Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = work.Ref },
                Place: seat.Ref, Causes: fell is null ? null : new[] { new Cause(fell.Id, CauseKind.Motive, 0.7) },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned) continue; // its ruin comes with the town's (Ruin)
            var hindrance = Hindrance(ctx, seat);
            if (hindrance != 0)
            {
                if (work.DelayedByEventId != hindrance)
                {
                    work.DelayedByEventId = hindrance;
                    ctx.Emit(new EventDraft("WorksDelayed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref,
                        Causes: new[] { new Cause(hindrance, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Structures" }));
                }
                continue;
            }
            work.DelayedByEventId = 0;
            var step = ctx.Rng.NextInt(7, 17) * (work.Rushed ? 1.6 : 1);
            work.Progress = Math.Min(100, work.Progress + step);
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Rushed && work.Progress < 100 && ctx.Rng.Chance(RushedCollapse))
            {
                Collapse(ctx, work, seat);
                continue;
            }
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var architect = world.Characters.GetValueOrDefault(work.ArchitectId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            if (architect is not null) actors["architect"] = architect.Ref;
            var fortress = work.Type == StructureKind.Fortification;
            var done = ctx.Emit(new EventDraft(fortress ? "FortificationCompleted" : "LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: fortress ? new[] { "Structures", "War" } : new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
            if (architect is { IsAlive: true }) architect.Renown = Math.Min(100, architect.Renown + 8);
            if (Realms.SovereignOf(world, world.Regions[seat.RegionId]) is { } realm) realm.Stability = Math.Min(100, realm.Stability + 5);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        // A land whose labyrinth was cleared does not open another.
        var haunted = labyrinths.Select(s => s.RegionId).Concat(world.Structures.Values.Where(s => s.GuardianId != 0).Select(s => s.RegionId)).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            var guardianId = world.Ids.Take(Monster.Kind);
            var levels = ctx.Rng.NextInt(3, 8);
            var breed = Monster.GuardianBreeds[ctx.Rng.NextInt(0, Monster.GuardianBreeds.Count)];
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id), ["guardian"] = new(Monster.Kind, guardianId) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
                Levels = levels, GuardianId = guardianId,
            };
            var deeps = invention.Name.StartsWith("The ", StringComparison.Ordinal) ? "the " + invention.Name[4..] : invention.Name;
            world.Monsters[guardianId] = new Monster
            {
                Id = guardianId, Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {deeps}", Breed = breed, RegionId = region.Id,
                Might = Math.Clamp(30 + 8 * levels + ctx.Rng.NextInt(0, 11), 1, 100), BornDay = ctx.Date.Day, State = MonsterState.Lairing,
                RoseEventId = opened.Id, LairStructureId = id,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world))
            {
                var relic = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id, fame: 20);
                world.Artifacts[relic.Id] = relic;
                Fates.Lose(relic, region.Id, opened.Id, 0);
            }
            return; // at most one a year
        }
    }

    private static void Ruin(SimContext ctx)
    {
        var world = ctx.World;
        var ruined = world.Structures.Values.Where(s => s.Type == StructureKind.Ruin).Select(s => s.SettlementId).ToHashSet();
        foreach (var town in world.Settlements.Values.Where(s => s.Abandoned && !ruined.Contains(s.Id)).ToList())
        {
            var abandonment = ctx.Log.Involving(town.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
            var id = world.Ids.Take(Structure.Kind);
            var fell = ctx.Emit(new EventDraft("StructureRuined",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: town.Ref, Payload: new Dictionary<string, string> { ["name"] = $"The Ruins of {town.Name}" },
                Causes: abandonment is null ? null : new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) },
                Importance: 25, Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = $"The Ruins of {town.Name}", Type = StructureKind.Ruin, RegionId = town.RegionId, SettlementId = town.Id,
                StartedEventId = fell.Id, CompletedEventId = fell.Id, Progress = 100, State = StructureState.Ruined,
            };
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type is StructureKind.Landmark or StructureKind.Fortification && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }

    /// <summary>A rushed work falls: people die, half the work is lost, and the architect is blamed.</summary>
    private static void Collapse(SimContext ctx, Structure work, Settlement seat)
    {
        var before = seat.Population;
        Demography.Kill(seat, 0.005, ctx.Rng);
        work.Progress = Math.Max(0, work.Progress - 50);
        var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
        if (ctx.World.Characters.TryGetValue(work.ArchitectId, out var architect))
        {
            actors["architect"] = architect.Ref;
            architect.Renown = Math.Max(0, architect.Renown - 10);
        }
        var builder = ctx.World.Characters.GetValueOrDefault(work.BuilderId);
        ctx.Emit(new EventDraft("WorksCollapsed", Actors: actors, Place: seat.Ref,
            Payload: new Dictionary<string, string> { ["dead"] = (before - seat.Population).ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(work.StartedEventId, CauseKind.Enabler, 0.7) },
            Importance: builder is null ? 40 : Fates.Importance(builder, 40), Tags: new[] { "Structures" }));
        if (work.Progress <= 0) work.State = StructureState.Ruined;
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/Category.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["LabyrinthCleared"] = "A labyrinth cleared",
        ["WorksCollapsed"] = "A collapse",
        ["FortificationCompleted"] = "A fortress",
        ["RestorationBegun"] = "Restoration",
        ["WildMagic"] = "Wild magic",
        ["MagicLawChanged"] = "Magic law",
        ["SchismBegan"] = "Schism",
        ["SaintDeclared"] = "A saint",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~LabyrinthTests"`
Expected: `Passed!  - Failed: 0, Passed: 3`. Then `dotnet test sim` → `Passed: 551` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (71)` and exit code 0.

- [x] **Step 6: Commit**

```bash
git add sim
git commit -m "feat(sim): give labyrinths levels and a guardian, delved level by level until cleared

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Faith, magic and structures in the views and on the diorama

Spec §9. The realm page names its faith; the character page names theirs and calls a saint so. On the map, today's towns show their temples and mage towers, labyrinths say how deep they have been delved (and replay as labyrinths before they were cleared), and guardians are marked. The diorama gains a marker for each: a pale hall under a gilt roof for a temple, a slender violet spire for a mage tower, a curtain wall with corner towers for a fortress, a gilt dome for a shrine.

**Files:**
- Modify: `game/scripts/Diorama.cs`, `game/scripts/screens/CharacterView.cs`, `game/scripts/screens/RealmView.cs`, `game/tests/HeadlessChecks.cs`, `sim/FantasyWorld.Client/Markers.cs`, `sim/FantasyWorld.Client/Palette.cs`, `sim/FantasyWorld.Sim/Systems/StructureSystem.cs`, `sim/FantasyWorld.Sim/Views/CharacterScreen.cs`, `sim/FantasyWorld.Sim/Views/ChronicleScreens.cs`, `sim/FantasyWorld.Sim/Views/MapScreen.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Client/MarkersTests.cs`, `sim/FantasyWorld.Sim.Tests/Views/CharacterScreenTests.cs`, `sim/FantasyWorld.Sim.Tests/Views/ChronicleScreenTests.cs`, `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json`, `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json`, `sim/FantasyWorld.Sim.Tests/Views/MapScreenTests.cs`

**Interfaces:**
- Consumes: Tasks 5–9; `Markers`, `Diorama.MarkerNode`, `RealmView`, `CharacterView` (sub-project 4).
- Produces: `MapSettlement.Marks`, `MapScreen.Marked`; `MapStructure.Detail`; `MapMonster.Guarding`; `RealmScreen.Faith`; `CharacterScreen { Faith; Saint }`; `MarkerShape { Fortress, Shrine, Tower, Temple }`; `Palette.Gold`; marker nodes `temple<id>`, `tower<id>`.

- [x] **Step 1: Write the failing tests**

Replace `sim/FantasyWorld.Sim.Tests/Client/MarkersTests.cs` entirely with:
```csharp
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class MarkersTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void EverySettlementAndStructure_StandsOnTheBlock_SizedByTier()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var markers = Markers.For(map, Aged.World.Geography!);
        Assert.Equal(map.Settlements.Count + map.Structures.Count + map.Settlements.Sum(s => s.Marks?.Count ?? 0), markers.Count);
        Assert.All(markers, m => Assert.True(m.Position.X is >= -1.1f and <= 1.1f && m.Position.Z is >= -1.1f and <= 1.1f && m.Position.Y >= 0));
        var city = map.Settlements.MaxBy(s => Markers.SizeOf(s.Tier))!;
        Assert.Equal(Markers.SizeOf(city.Tier), markers.Single(m => m.Ref == city.Ref && m.Shape == MarkerShape.Settlement).Size);
        Assert.True(Markers.SizeOf("City") > Markers.SizeOf("Hamlet"));
        Assert.All(markers.Where(m => m.Kind == PinMarker.Conflict), m => Assert.Equal(Palette.Conflict, m.Colour));
    }

    [Fact]
    public void Borders_RunBetweenDifferentlyHeldRegions()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var borders = Markers.Borders(map, Aged.World.Geography!);
        Assert.Equal(map.Borders.Count > 0, borders.Count > 0);
        var grid = Aged.World.Geography!.Terrain;
        Assert.All(borders, b => Assert.InRange(Math.Abs(b.From.X - b.To.X) + Math.Abs(b.From.Z - b.To.Z), 0.5f / grid.Width, 2.5f / grid.Width));
    }

    [Fact]
    public void BorderLines_AreSmoothChains_CoveringEveryBorderEdge()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var lines = Markers.BorderLines(map, Aged.World.Geography!);
        Assert.Equal(map.Borders.Count > 0, lines.Count > 0);
        Assert.All(lines, l => Assert.True(l.Count >= 2));
        // Rounded corners: Chaikin cutting leaves no exact right-angle staircase of tile-length steps.
        var grid = Aged.World.Geography!.Terrain;
        Assert.True(lines.Sum(l => l.Count) > Markers.Borders(map, Aged.World.Geography!).Count);
        Assert.All(lines.SelectMany(l => l), p => Assert.True(p.Y > 0));
    }

    [Fact]
    public void Chains_WalkEdgesIntoLines_AndLoops()
    {
        TileCoord P(int x, int y) => new(x, y);
        var open = Markers.Chains(new[] { (P(0, 0), P(1, 0)), (P(1, 0), P(2, 0)), (P(2, 0), P(2, 1)) });
        Assert.Single(open);
        Assert.Equal(4, open[0].Count);
        var square = Markers.Chains(new[] { (P(0, 0), P(1, 0)), (P(1, 0), P(1, 1)), (P(1, 1), P(0, 1)), (P(0, 1), P(0, 0)) });
        Assert.Single(square);
        Assert.Equal(square[0][0], square[0][^1]);
    }

    [Fact]
    public void Labels_ThatWouldOverlap_GiveWayToMoreImportantOnes()
    {
        var labels = new[]
        {
            new LabelBox(10, 10, 80, 20, 0), new LabelBox(20, 15, 80, 20, 5), new LabelBox(200, 10, 80, 20, 0),
            new LabelBox(900, 10, 80, 20, 9), new LabelBox(500, 10, 80, 20, 1),
        };
        var visible = Declutter.Visible(labels, 1000, 800, new[] { new LabelBox(480, 0, 200, 200, int.MaxValue) });
        Assert.Equal(new[] { false, true, true, true, false }, visible);
    }

    [Fact]
    public void DigestPins_FloatAboveTheirPlaces_InOrder()
    {
        var digest = DigestScreen.Build(Aged.World, Aged.Log, FantasyWorld.Sim.Chronicle.Digest.Build(Aged.Log, Aged.World, 0, new HashSet<EntityRef>()), new WorldDate(0));
        var pins = Markers.Pins(digest, Aged.World.Geography!);
        Assert.Equal(digest.Pins.Count, pins.Count);
        Assert.Equal(pins.Select(p => p.Number).Order(), pins.Select(p => p.Number));
        Assert.All(pins, p => Assert.True(p.Position.Y >= Markers.PinLift));
    }

    [Fact]
    public void FortressesShrinesTemplesAndTowers_HaveTheirOwnMarkers()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var town = map.Settlements.First(s => s.Position is not null);
        var at = town.Position!;
        map = map with
        {
            Settlements = map.Settlements.Select(s => s.Ref == town.Ref ? s with { Marks = new[] { "temple", "mage tower" } } : s with { Marks = null }).ToList(),
            Structures = new[]
            {
                new MapStructure(new EntityRef("structure", 901), "The Walls", "Fortification", "Standing", at),
                new MapStructure(new EntityRef("structure", 902), "The Shrine", "Shrine", "Standing", at, "cleared"),
                new MapStructure(new EntityRef("structure", 903), "The Old Deeps", "Ruin", "Ruined", at, "cleared"),
            },
        };
        var markers = Markers.For(map, Aged.World.Geography!);
        Assert.Equal(MarkerShape.Fortress, markers.Single(m => m.Ref.Id == 901).Shape);
        Assert.Equal((MarkerShape.Shrine, Palette.Gold), (markers.Single(m => m.Ref.Id == 902).Shape, markers.Single(m => m.Ref.Id == 902).Colour));
        Assert.Equal(MarkerShape.Ruin, markers.Single(m => m.Ref.Id == 903).Shape);
        var beside = markers.Where(m => m.Ref == town.Ref && m.Shape != MarkerShape.Settlement).ToList();
        Assert.Equal(new[] { MarkerShape.Temple, MarkerShape.Tower }, beside.Select(m => m.Shape));
        var home = markers.Single(m => m.Ref == town.Ref && m.Shape == MarkerShape.Settlement);
        Assert.All(beside, m => Assert.InRange(System.Numerics.Vector3.Distance(m.Position with { Y = 0 }, home.Position with { Y = 0 }), 0.01f, 0.05f));
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/CharacterScreenTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class CharacterScreenTests
{
    [Fact]
    public void TheCharacterPage_ShowsWhoTheyAre_TheirLife_AndTheirTies()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new FantasyWorld.Sim.Core.WorldDate(360 * 40);
        var log = new EventLog();
        var father = TestPeople.Add(world, 1, 60);
        var knight = TestPeople.Add(world, 1, 27, role: Role.Knight, fatherId: father.Id);
        knight.Traits.AddRange(new[] { Traits.Brave, Traits.Wrathful });
        knight.Ambition = Ambitions.Renown;
        var rival = TestPeople.Add(world, 1, 30);
        var born = log.Append(new FantasyWorld.Sim.Core.WorldDate(knight.BirthDay), new EventDraft("CharacterBorn",
            Actors: new Dictionary<string, EntityRef> { ["child"] = knight.Ref, ["father"] = father.Ref }, Place: world.Settlements[1].Ref, Importance: 20));
        var duel = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = rival.Ref, ["defender"] = knight.Ref }, Place: world.Settlements[1].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = "to first blood" }, Importance: 40));
        log.Append(world.Date, new EventDraft("Gossip", Actors: new Dictionary<string, EntityRef> { ["subject"] = knight.Ref }, Importance: 5));
        knight.Memories.Add(new Memory(duel.Id, rival.Id, -90, 90));

        var page = CharacterScreen.Build(world, log, knight.Id, followed: true);

        Assert.Equal($"Knight of {world.Realms[1].Name} · age 27 · born Year {born.Date.Year} in {world.Settlements[1].Name}", page.Line);
        Assert.Equal(new[] { Traits.Brave, Traits.Wrathful }, page.Traits);
        Assert.Equal("Ambition: renown", page.Ambition);
        Assert.Equal($"Now at {world.Settlements[1].Name}", page.Location!.Label);
        Assert.Equal(new[] { born.Id, duel.Id }, page.Timeline.Select(r => r.EventId));
        Assert.True(page.Timeline.Single(r => r.EventId == duel.Id).Highlight);
        Assert.Equal(1, page.Folded);
        Assert.Contains(page.Ties, t => t.Relation == "Liege" && t.Ref == world.Characters[world.Settlements[1].LordId].Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Father" && t.Ref == father.Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Rival" && t.Ref == rival.Ref && t.Tone == Tone.Hostile);
        Assert.True(page.Followed);

        var tree = FamilyTree.Build(world, knight.Id);
        Assert.Contains(tree.Edges, e => e.From == father.Ref && e.To == knight.Ref && e.Kind == "parent");
    }

    [Fact]
    public void EveryCharacterOfARealHistory_HasAPage()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var nameOf = Names.Of(world);
        foreach (var c in world.Characters.Values)
        {
            var page = CharacterScreen.Build(world, sim.Log, c.Id);
            Assert.All(page.Ties, t => Assert.NotNull(nameOf(t.Ref)));
            foreach (var row in page.Timeline)
                Assert.All(row.Text.Links, l => Assert.Equal(nameOf(l.Entity), row.Text.Text.Substring(l.Start, l.Length)));
            FamilyTree.Build(world, c.Id);
        }
    }

    [Fact]
    public void ACharactersPage_NamesTheirFaith_AndASaintIsCalledSo()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var holy = TestPeople.Add(world, 1, 60, role: Role.Priest);
        var page = CharacterScreen.Build(world, log, holy.Id);
        Assert.Equal(("The Way of Ohm", false), (page.Faith!.Name, page.Saint));
        holy.SaintEventId = log.Append(world.Date, new EventDraft("SaintDeclared")).Id;
        page = CharacterScreen.Build(world, log, holy.Id);
        Assert.True(page.Saint);
        Assert.StartsWith("Saint · ", page.Line);
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/ChronicleScreenTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class ChronicleScreenTests
{
    private static Simulator History(int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * years);
        return sim;
    }

    [Fact]
    public void TheChronicle_IsNewestFirst_FilteredAndPaged()
    {
        var sim = History(12);
        var all = ChronicleScreen.Build(sim.World, sim.Log);
        Assert.Equal((4, 13), (all.FromYear, all.ToYear));
        Assert.Equal(sim.Log.All.Count(e => e.Date.Year >= 4), all.Total);
        var ids = all.Seasons.SelectMany(s => s.Rows).Select(r => r.EventId).ToList();
        Assert.Equal(ChronicleScreen.PageSize, ids.Count);
        Assert.Equal(ids.OrderByDescending(id => id), ids);
        Assert.Equal(all.Pages, ChronicleScreen.Build(sim.World, sim.Log, page: 999).Page);

        var wars = ChronicleScreen.Build(sim.World, sim.Log, 1, 13, Category.War, minImportance: 40);
        Assert.All(wars.Seasons.SelectMany(s => s.Rows), r => Assert.True(r.Category == Category.War && r.Importance >= 40));
    }

    [Fact]
    public void EveryRealmAndArtifact_HasAPage_WhoseNamesResolve()
    {
        var sim = History(30);
        var world = sim.World;
        var nameOf = Names.Of(world);
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed))
        {
            var page = RealmScreen.Build(world, sim.Log, realm.Id);
            Assert.Contains(realm.Name, page.Title);
            Assert.Equal(world.Faiths[realm.FaithId].Name, page.Faith!.Name);
            Assert.All(page.Vassals.Concat(page.Council.Select(c => c.Member)).Concat(page.Treaties.Select(t => t.With)),
                m => Assert.Equal(nameOf(m.Ref), m.Name));
            Assert.All(page.Eras, e => Assert.True(sim.Log.TryGet(e.EventId, out _)));
        }
        Assert.Contains(world.Realms.Values, r => RealmScreen.Build(world, sim.Log, r.Id).Wars.Count > 0);
        foreach (var artifact in world.Artifacts.Values)
        {
            var page = ArtifactScreen.Build(world, sim.Log, artifact.Id);
            Assert.NotEmpty(page.Powers);
            Assert.Equal(artifact.Provenance.Count, page.Provenance.Count);
            Assert.All(page.Made.Links, l => Assert.Equal(nameOf(l.Entity), page.Made.Text.Substring(l.Start, l.Length)));
        }
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/character.json` entirely with:
```json
{
  "ref": "char#1",
  "name": "Fath of Cunvei",
  "house": "house#1",
  "kicker": "Character · House Cunvei",
  "line": "Lord of Drerithby · age 66 · born before recorded history",
  "traits": [
    "calm",
    "devout",
    "lazy",
    "trusting"
  ],
  "ambition": "Ambition: mastery of magic",
  "location": {
    "ref": "settlement#3",
    "label": "Now at Drerithby",
    "position": {
      "x": 0.3515625,
      "y": 0.1953125
    }
  },
  "timeline": [
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted Keth of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 15,
            "entity": "char#17"
          },
          {
            "start": 58,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 32,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Fath of Cunvei knighted End of Gil in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 10,
            "entity": "char#15"
          },
          {
            "start": 53,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 33,
      "highlight": false
    },
    {
      "year": 2,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 45,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei knighted Teilbeith of Bandbia in the service of Kingdom of Thonae.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 24,
            "length": 20,
            "entity": "char#18"
          },
          {
            "start": 63,
            "length": 17,
            "entity": "realm#1"
          }
        ]
      },
      "eventId": 126,
      "highlight": false
    },
    {
      "year": 16,
      "text": {
        "text": "Fath of Cunvei and Lond of Cunvei became rivals over calm vs wrathful.",
        "links": [
          {
            "start": 0,
            "length": 14,
            "entity": "char#1"
          },
          {
            "start": 19,
            "length": 14,
            "entity": "char#3"
          }
        ]
      },
      "eventId": 131,
      "highlight": false
    },
    {
      "year": 18,
      "text": {
        "text": "Broth of Cunvei and Fath of Cunvei became rivals over diligent vs lazy.",
        "links": [
          {
            "start": 0,
            "length": 15,
            "entity": "char#4"
          },
          {
            "start": 20,
            "length": 14,
            "entity": "char#1"
          }
        ]
      },
      "eventId": 139,
      "highlight": false
    }
  ],
  "folded": 0,
  "earlier": 0,
  "ties": [
    {
      "ref": "char#2",
      "name": "Farnfand of Cunvei",
      "relation": "Spouse",
      "qualifier": "",
      "tone": "warm"
    },
    {
      "ref": "char#3",
      "name": "Lond of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    },
    {
      "ref": "char#4",
      "name": "Broth of Cunvei",
      "relation": "Child",
      "qualifier": "",
      "tone": "neutral"
    }
  ],
  "followed": false,
  "faith": {
    "ref": "faith#2",
    "name": "The Way of Ciscoun"
  },
  "saint": false
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/Golden/map.json` entirely with:
```json
{
  "worldName": "Dromothholt",
  "summary": "Two realms, 24 settlements, one open labyrinth.",
  "year": 21,
  "firstYear": 1,
  "historical": true,
  "regions": [
    {
      "ref": "region#1",
      "name": "Drannias",
      "biome": "Swamp",
      "center": {
        "x": 0.0859375,
        "y": 0.4609375
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#2",
      "name": "Taramdale",
      "biome": "Hills",
      "center": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#3",
      "name": "Thonae",
      "biome": "Grassland",
      "center": {
        "x": 0.3203125,
        "y": 0.3515625
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#4",
      "name": "Thounholt",
      "biome": "Tundra",
      "center": {
        "x": 0.4921875,
        "y": 0.1328125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#5",
      "name": "Dathfaelldale",
      "biome": "Grassland",
      "center": {
        "x": 0.2734375,
        "y": 0.5546875
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#6",
      "name": "Stirvale",
      "biome": "Hills",
      "center": {
        "x": 0.4140625,
        "y": 0.7421875
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#7",
      "name": "Gisreach",
      "biome": "Tundra",
      "center": {
        "x": 0.6796875,
        "y": 0.1953125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#8",
      "name": "Veirwold",
      "biome": "Grassland",
      "center": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "owner": "realm#3",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#9",
      "name": "Driall",
      "biome": "Forest",
      "center": {
        "x": 0.6171875,
        "y": 0.6953125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#10",
      "name": "Haerrouvale",
      "biome": "Grassland",
      "center": {
        "x": 0.8828125,
        "y": 0.5703125
      },
      "owner": "realm#1",
      "occupier": null,
      "colour": "white"
    },
    {
      "ref": "region#11",
      "name": "Lam",
      "biome": "Jungle",
      "center": {
        "x": 0.4765625,
        "y": 0.9140625
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    },
    {
      "ref": "region#12",
      "name": "Vamholt",
      "biome": "Savanna",
      "center": {
        "x": 0.2578125,
        "y": 0.8203125
      },
      "owner": "realm#2",
      "occupier": null,
      "colour": "crimson"
    }
  ],
  "settlements": [
    {
      "ref": "settlement#1",
      "name": "Stesford",
      "tier": "Hamlet",
      "position": {
        "x": 0.1171875,
        "y": 0.3828125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Stesford · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 138 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#2",
      "name": "Thanddind",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.4765625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thanddind · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 123 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#3",
      "name": "Drerithby",
      "tier": "Town",
      "position": {
        "x": 0.3515625,
        "y": 0.1953125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Drerithby · Town",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 2,727 · 0 events this year"
      },
      "marks": [
        "temple"
      ]
    },
    {
      "ref": "settlement#4",
      "name": "Wiall",
      "tier": "Hamlet",
      "position": {
        "x": 0.2890625,
        "y": 0.3203125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Wiall · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 193 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#5",
      "name": "Kaerhiand",
      "tier": "Village",
      "position": {
        "x": 0.1171875,
        "y": 0.3671875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Kaerhiand · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 452 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#6",
      "name": "Veim",
      "tier": "Hamlet",
      "position": {
        "x": 0.4296875,
        "y": 0.0390625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veim · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 89 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#7",
      "name": "Brousfiallcross",
      "tier": "Village",
      "position": {
        "x": 0.2109375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Brousfiallcross · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 973 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#8",
      "name": "Raembridge",
      "tier": "Village",
      "position": {
        "x": 0.3984375,
        "y": 0.5078125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Raembridge · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 951 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#9",
      "name": "Gresford",
      "tier": "Village",
      "position": {
        "x": 0.3359375,
        "y": 0.5546875
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gresford · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 844 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#10",
      "name": "Gerrouhaven",
      "tier": "Village",
      "position": {
        "x": 0.2421875,
        "y": 0.5703125
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Gerrouhaven · Village",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 988 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#11",
      "name": "Droumkarnbridge",
      "tier": "Village",
      "position": {
        "x": 0.3671875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Droumkarnbridge · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 338 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#12",
      "name": "Fouth",
      "tier": "Hamlet",
      "position": {
        "x": 0.3046875,
        "y": 0.7109375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Fouth · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 247 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#13",
      "name": "Veill",
      "tier": "Hamlet",
      "position": {
        "x": 0.7265625,
        "y": 0.1640625
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Veill · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 179 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#14",
      "name": "Toth",
      "tier": "Hamlet",
      "position": {
        "x": 0.8046875,
        "y": 0.2734375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Toth · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 244 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#15",
      "name": "Fasthar",
      "tier": "Village",
      "position": {
        "x": 0.7265625,
        "y": 0.3515625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Fasthar · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 410 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#16",
      "name": "Greindrondcross",
      "tier": "Town",
      "position": {
        "x": 0.8828125,
        "y": 0.4296875
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Greindrondcross · Town",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 1,925 · 0 events this year"
      },
      "marks": [
        "temple"
      ]
    },
    {
      "ref": "settlement#17",
      "name": "Braestilby",
      "tier": "Village",
      "position": {
        "x": 0.6953125,
        "y": 0.4609375
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Braestilby · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 479 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#18",
      "name": "Droukolford",
      "tier": "Village",
      "position": {
        "x": 0.8359375,
        "y": 0.4765625
      },
      "holder": "realm#3",
      "marker": "realm",
      "card": {
        "kicker": "Droukolford · Village",
        "title": "Held by the County of Veirwold since the founding",
        "line": "Pop. 679 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#19",
      "name": "Heith",
      "tier": "Village",
      "position": {
        "x": 0.7421875,
        "y": 0.6796875
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Heith · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 993 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#20",
      "name": "Outh",
      "tier": "Village",
      "position": {
        "x": 0.6171875,
        "y": 0.7734375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Outh · Village",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,252 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#21",
      "name": "Thurnstead",
      "tier": "Hamlet",
      "position": {
        "x": 0.8203125,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Thurnstead · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 266 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#22",
      "name": "Caethhouston",
      "tier": "Hamlet",
      "position": {
        "x": 0.9296875,
        "y": 0.6484375
      },
      "holder": "realm#1",
      "marker": "realm",
      "card": {
        "kicker": "Caethhouston · Hamlet",
        "title": "Held by the Kingdom of Thonae since the founding",
        "line": "Pop. 163 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#23",
      "name": "Grintiacross",
      "tier": "Hamlet",
      "position": {
        "x": 0.3671875,
        "y": 0.9609375
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Grintiacross · Hamlet",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 117 · 0 events this year"
      },
      "marks": []
    },
    {
      "ref": "settlement#24",
      "name": "Niar",
      "tier": "Town",
      "position": {
        "x": 0.2578125,
        "y": 0.7890625
      },
      "holder": "realm#2",
      "marker": "realm",
      "card": {
        "kicker": "Niar · Town",
        "title": "Held by the Duchy of Vamholt since the founding",
        "line": "Pop. 1,964 · 0 events this year"
      },
      "marks": [
        "temple"
      ]
    }
  ],
  "structures": [
    {
      "ref": "structure#1",
      "name": "The Deeps of Taramdale",
      "kind": "Labyrinth",
      "state": "Standing",
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "detail": "level 2 of 3"
    }
  ],
  "borders": [
    {
      "a": "region#2",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#6"
    },
    {
      "a": "region#5",
      "b": "region#8"
    },
    {
      "a": "region#5",
      "b": "region#12"
    },
    {
      "a": "region#6",
      "b": "region#8"
    },
    {
      "a": "region#7",
      "b": "region#8"
    },
    {
      "a": "region#8",
      "b": "region#9"
    },
    {
      "a": "region#8",
      "b": "region#10"
    },
    {
      "a": "region#9",
      "b": "region#10"
    }
  ],
  "conflicts": [],
  "legend": [
    {
      "label": "Thonae",
      "colour": "white"
    },
    {
      "label": "Vamholt",
      "colour": "crimson"
    },
    {
      "label": "Active conflict",
      "colour": "conflict"
    },
    {
      "label": "Event this season",
      "colour": "event"
    }
  ],
  "armies": [],
  "battles": [],
  "routes": [
    {
      "ref": "route#1",
      "name": "The Greindrondcross–Niar Road",
      "from": "settlement#16",
      "to": "settlement#24",
      "state": "open",
      "volume": 76,
      "path": [
        {
          "x": 0.8828125,
          "y": 0.4296875
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    },
    {
      "ref": "route#2",
      "name": "The Drerithby–Greindrondcross Road",
      "from": "settlement#3",
      "to": "settlement#16",
      "state": "open",
      "volume": 106,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.4921875,
          "y": 0.4296875
        },
        {
          "x": 0.8828125,
          "y": 0.4296875
        }
      ]
    },
    {
      "ref": "route#3",
      "name": "The Drerithby–Niar Road",
      "from": "settlement#3",
      "to": "settlement#24",
      "state": "open",
      "volume": 78,
      "path": [
        {
          "x": 0.3515625,
          "y": 0.1953125
        },
        {
          "x": 0.2734375,
          "y": 0.5546875
        },
        {
          "x": 0.2578125,
          "y": 0.7890625
        }
      ]
    }
  ],
  "monsters": [
    {
      "ref": "monster#1",
      "name": "The Lich of the Deeps of Taramdale",
      "breed": "lich",
      "might": 61,
      "legendary": false,
      "slumbering": false,
      "position": {
        "x": 0.4921875,
        "y": 0.4296875
      },
      "roseEventId": 120,
      "guarding": true
    }
  ]
}
```

Replace `sim/FantasyWorld.Sim.Tests/Views/MapScreenTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class MapScreenTests
{
    [Fact]
    public void TheMap_ShowsHoldersOccupiersAndConflicts()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var siege = log.Append(world.Date, new EventDraft("RegionOccupied", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref, ["war"] = war.Ref },
            Place: world.Settlements[2].Ref, Importance: 30, Tags: new[] { "War" }));
        world.Regions[2].OccupierRealmId = 1;

        var map = MapScreen.Build(world, log);

        var held = map.Settlements.Single(s => s.Ref == world.Settlements[2].Ref);
        Assert.Equal(PinMarker.Conflict, held.Marker);
        Assert.Equal($"Held by the {Statecraft.Title(world.Realms[2])} since the founding, occupied by {world.Realms[1].Name}", held.Card.Title);
        Assert.StartsWith("Pop. ", held.Card.Line);
        Assert.Equal(world.Realms[1].Ref, map.Regions.Single(r => r.Ref == world.Regions[2].Ref).Occupier);
        var conflict = Assert.Single(map.Conflicts);
        Assert.Equal(new[] { world.Regions[2].Ref }, conflict.Occupied);
        Assert.Contains(map.Legend, l => l.Label == world.Realms[1].Name);
    }

    [Fact]
    public void TheMapAtAPastDate_ShowsThePastState()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var ceded = sim.Log.All.First(e => e.Type == "RegionCeded");
        var before = MapScreen.Build(world, sim.Log, ceded.Date.AddDays(-1));
        var after = MapScreen.Build(world, sim.Log, ceded.Date);
        Assert.Equal(ceded.Actors["from"], before.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.Equal(ceded.Actors["to"], after.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.DoesNotContain("Pop.", before.Settlements[0].Card.Line);

        var now = MapScreen.Build(world, sim.Log);
        Assert.Equal(world.Date.Year, now.Year);
        Assert.Equal(world.Settlements.Values.Count(s => !s.Abandoned), now.Settlements.Count);
        Assert.All(now.Settlements, s => Assert.True(s.Position!.X is > 0 and < 1));
        Assert.All(now.Borders, b => Assert.NotEqual(now.Regions.Single(r => r.Ref == b.A).Owner, now.Regions.Single(r => r.Ref == b.B).Owner));
        Assert.Matches(@"^[A-Z][a-z]+ realms?, [a-z0-9]+ settlements?, [a-z0-9]+ open labyrinths?\.$", now.Summary);
    }

    [Fact]
    public void AWorldWithoutAFoundingRecord_ShowsTodaysMap_AtAnyDate()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new WorldDate(3600);
        var map = MapScreen.Build(world, new EventLog(), new WorldDate(10));
        Assert.False(map.Historical);
        Assert.Equal(world.Date.Year, map.Year);
    }

    [Fact]
    public void TemplesTowers_AndLabyrinthDepths_ShowToday_AndClearedLabyrinthsReplay()
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var r in world.Regions.Values) world.FoundingOwners[r.Id] = r.OwnerRealmId;
        var log = new EventLog();
        world.Settlements[1].Buildings.AddRange(new[] { "granary", "mage tower", "temple" });
        var opened = log.Append(world.Date, new EventDraft("LabyrinthOpened", Place: world.Regions[2].Ref));
        var maze = new Structure
        {
            Id = world.Ids.Take(Structure.Kind), Name = "The Deeps", Type = StructureKind.Labyrinth, RegionId = 2, StartedEventId = opened.Id,
            CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing, Levels = 5, Depth = 2,
        };
        world.Structures[maze.Id] = maze;
        var guardian = new Monster { Id = world.Ids.Take(Monster.Kind), Name = "The Lich", Breed = "lich", RegionId = 2, Might = 60,
            State = MonsterState.Lairing, RoseEventId = opened.Id, LairStructureId = maze.Id };
        world.Monsters[guardian.Id] = guardian;
        maze.GuardianId = guardian.Id;

        var map = MapScreen.Build(world, log);
        Assert.Equal(new[] { "temple", "mage tower" }, map.Settlements.Single(s => s.Ref.Id == 1).Marks);
        var deeps = map.Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal(("Labyrinth", "level 2 of 5"), (deeps.Kind, deeps.Detail));
        Assert.True(map.Monsters!.Single().Guarding);

        world.Date = world.Date.AddDays(3600);
        var cleared = log.Append(world.Date, new EventDraft("LabyrinthCleared", Place: world.Regions[2].Ref));
        (maze.ClearedEventId, maze.Type, guardian.State, guardian.SlainEventId) = (cleared.Id, StructureKind.Shrine, MonsterState.Slain, cleared.Id);
        world.Date = world.Date.AddDays(360);
        var now = MapScreen.Build(world, log).Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal(("Shrine", "cleared"), (now.Kind, now.Detail));
        var then = MapScreen.Build(world, log, new WorldDate(360)).Structures.Single(s => s.Ref == maze.Ref);
        Assert.Equal("Labyrinth", then.Kind);
        Assert.Empty(MapScreen.Build(world, log, new WorldDate(360)).Settlements.Single(s => s.Ref.Id == 1).Marks!);
    }

    [Fact]
    public void GreatWorks_ReplayTheirCollapses_AndFortressesTheirBuilding()
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var r in world.Regions.Values) world.FoundingOwners[r.Id] = r.OwnerRealmId;
        var log = new EventLog();
        Structure Work(StructureKind kind, string begun)
        {
            var started = log.Append(world.Date, new EventDraft(begun, Place: world.Settlements[1].Ref));
            var work = new Structure { Id = world.Ids.Take(Structure.Kind), Name = $"The {kind}", Type = kind, RegionId = 1, SettlementId = 1, StartedEventId = started.Id };
            world.Structures[work.Id] = work;
            return work;
        }
        var spire = Work(StructureKind.Landmark, "LandmarkBegun");
        var walls = Work(StructureKind.Fortification, "FortificationBegun");
        world.Date = new WorldDate(720);
        log.Append(world.Date, new EventDraft("WorksCollapsed", Actors: new Dictionary<string, EntityRef> { ["structure"] = spire.Ref },
            Payload: new Dictionary<string, string> { ["ruined"] = "yes" }));
        var done = log.Append(world.Date, new EventDraft("FortificationCompleted", Actors: new Dictionary<string, EntityRef> { ["structure"] = walls.Ref }));
        (spire.State, walls.State, walls.CompletedEventId) = (StructureState.Ruined, StructureState.Standing, done.Id);
        world.Date = new WorldDate(1440);

        string At(Structure st, WorldDate? when) => MapScreen.Build(world, log, when).Structures.Single(s => s.Ref == st.Ref).State;
        Assert.Equal(("Ruined", "Standing"), (At(spire, null), At(walls, null)));
        Assert.Equal(("Building", "Building"), (At(spire, new WorldDate(360)), At(walls, new WorldDate(360))));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: build FAILS with `CS1061: 'MapSettlement' does not contain a definition for 'Marks' and no accessible extension method 'Marks' accepting a first argument of type 'MapSettlement' could be found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 3: Implement**

Replace `game/scripts/Diorama.cs` entirely with:
```csharp
using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The floating world block (sub-project 4 §3): terrain, sea, strata walls, markers, realm borders and digest pins,
/// seen through the orbit camera. Geography is built once; markers and borders are rebuilt when the map changes.
/// Drag to orbit, scroll to descend; clicking near a marker raises <see cref="Picked"/>.
/// </summary>
public partial class Diorama : Node3D
{
    public const float PickRadius = 16f;
    public const float FrameOffset = 0.08f;

    private Geography? _geography;
    private bool _dragging;
    private Vector2 _pressedAt;
    private readonly List<Marker> _markers = new();
    private readonly List<Pin> _pins = new();

    public OrbitRig Rig { get; } = new();
    public Camera3D Camera { get; private set; } = null!;
    public MeshInstance3D Block { get; private set; } = null!;
    public Node3D MarkerLayer { get; private set; } = null!;
    public Node3D SettlementLayer { get; private set; } = null!;
    public Node3D StructureLayer { get; private set; } = null!;
    public MapLayers Layers { get; private set; } = MapLayers.Default;
    public Node3D PinLayer { get; private set; } = null!;
    public WarLayer War { get; private set; } = null!;
    public MeshInstance3D BorderLines { get; private set; } = null!;
    public MeshInstance3D RoadLines { get; private set; } = null!;
    public IReadOnlyList<Road> Roads { get; private set; } = Array.Empty<Road>();
    public IReadOnlyList<MapMonster> Monsters { get; private set; } = Array.Empty<MapMonster>();
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary>A marker or pin was clicked.</summary>
    public event Action<EntityRef>? Picked;

    public override void _Ready()
    {
        Name = "Diorama";
        Camera = new Camera3D { Name = "Camera", Fov = 32, Near = 0.01f, Far = 50 };
        AddChild(Camera);
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 0.55f, ShadowEnabled = true });
        // A soft fill from the viewer's side keeps the strata walls readable.
        AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-15, 35, 0), LightEnergy = 0.25f });
        AddChild(new DirectionalLight3D { Name = "Back", RotationDegrees = new Vector3(-20, 215, 0), LightEnergy = 0.2f });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.C(Palette.Ground),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f,
            },
        });
        Block = new MeshInstance3D { Name = "Block" };
        AddChild(Block);
        BorderLines = new MeshInstance3D { Name = "Borders" };
        AddChild(BorderLines);
        RoadLines = new MeshInstance3D { Name = "Roads" };
        AddChild(RoadLines);
        MarkerLayer = new Node3D { Name = "Markers" };
        AddChild(MarkerLayer);
        SettlementLayer = new Node3D { Name = "Settlements" };
        StructureLayer = new Node3D { Name = "Structures" };
        MarkerLayer.AddChild(SettlementLayer);
        MarkerLayer.AddChild(StructureLayer);
        PinLayer = new Node3D { Name = "Pins" };
        AddChild(PinLayer);
        War = new WarLayer();
        AddChild(War);
        UpdateCamera();
    }

    /// <summary>
    /// Builds the terrain block, its walls, the sea, the woods and the block's shadow (once per world). Woods leave
    /// clearings around the given points (today's settlements).
    /// </summary>
    public void Build(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null) =>
        Build(geography, Prepare(geography, clearings, resolution));

    /// <summary>The block's geometry and woods, computed without Godot so it can run on a worker thread.</summary>
    public sealed record Parts(MeshData Top, MeshData Sides, MeshData Water, IReadOnlyList<Client.Tree> Trees);

    public static Parts Prepare(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null)
    {
        var grid = geography.Terrain;
        // About one and a half vertices per tile: smooth on small worlds, detailed on large ones.
        var detail = resolution ?? Math.Clamp(grid.Width * 3 / 2, 192, 384);
        return new Parts(BlockMesh.Top(grid, detail), BlockMesh.Sides(grid, detail), BlockMesh.Water(grid), Scatter.Trees(grid, clearings));
    }

    public void Build(Geography geography, Parts parts)
    {
        _geography = geography;
        var mesh = new ArrayMesh();
        AddSurface(mesh, parts.Top, VertexColours());
        AddSurface(mesh, parts.Sides, VertexColours());
        Block.Mesh = mesh;

        var water = new ArrayMesh();
        AddSurface(water, parts.Water, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.18f,
            MetallicSpecular = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        Child<MeshInstance3D>("Water").Mesh = water;

        var trees = parts.Trees;
        Child<MultiMeshInstance3D>("Broadleaves").Multimesh = Instances(
            new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 },
            trees.Where(t => t.Kind == TreeKind.Broadleaf).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.55f, 0), new Vector3(t.Height * 0.95f, t.Height, t.Height * 0.95f), 0), t.Colour)));
        Child<MultiMeshInstance3D>("Conifers").Multimesh = Instances(
            new CylinderMesh { TopRadius = 0, BottomRadius = 0.36f, Height = 1f, RadialSegments = 7, Rings = 1 },
            trees.Where(t => t.Kind == TreeKind.Conifer).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.5f, 0), new Vector3(t.Height, t.Height, t.Height), 0), t.Colour)));
        foreach (var name in new[] { "Broadleaves", "Conifers" }) Child<MultiMeshInstance3D>(name).MaterialOverride = Foliage();

        // A soft shadow on the ground beneath the floating block.
        Child<MeshInstance3D>("Shadow").Mesh = new PlaneMesh { Size = new Vector2(3.1f, 3.1f) };
        var shadow = Child<MeshInstance3D>("Shadow");
        shadow.Position = new Vector3(0.06f, -BlockMesh.BaseDepth - 0.12f, 0.06f);
        shadow.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = SoftShadow(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 1),
        };
    }

    public int TreeCount => Child<MultiMeshInstance3D>("Broadleaves").Multimesh?.InstanceCount + Child<MultiMeshInstance3D>("Conifers").Multimesh?.InstanceCount ?? 0;

    /// <summary>Places settlements, structures and borders for a map: today's, or the given past year's.</summary>
    public void Show(MapScreen map, int? pastYear = null)
    {
        if (_geography is null) return;
        Ui.Clear(SettlementLayer);
        Ui.Clear(StructureLayer);
        _markers.Clear();
        _markers.AddRange(FantasyWorld.Client.Markers.For(map, _geography));
        var houses = _markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, _geography.Terrain)).ToList();
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Walls", MaterialOverride = Painted(),
            Multimesh = Instances(new BoxMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Size * (h.Tower ? 0.75f : 0.35f), 0), h.Tower ? new Vector3(h.Size, h.Size * 1.5f, h.Size) : new Vector3(h.Size, h.Size * 0.7f, h.Size * 0.8f), h.Yaw),
                h.Tower ? Stone : Plaster))),
        });
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Roofs", MaterialOverride = Painted(),
            Multimesh = Instances(new PrismMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Tower ? h.Size * 1.85f : h.Size * 0.95f, 0), h.Tower ? new Vector3(h.Size * 1.15f, h.Size * 0.75f, h.Size * 1.15f) : new Vector3(h.Size * 1.1f, h.Size * 0.5f, h.Size * 0.9f), h.Yaw),
                Roof(h.Roof)))),
        });
        foreach (var marker in _markers.Where(m => m.Shape != MarkerShape.Settlement)) StructureLayer.AddChild(MarkerNode(marker));
        Monsters = map.Monsters ?? Array.Empty<MapMonster>();
        foreach (var beast in Monsters) StructureLayer.AddChild(BeastNode(beast, _geography));
        BorderLines.Mesh = DashedRibbons(FantasyWorld.Client.Markers.BorderLines(map, _geography), Palette.Ink);
        War.Show(map, _geography);
        Roads = RouteLayout.Roads(map, _geography);
        // Open roads are close-dotted in road brown; severed ones broken and reddened.
        var roads = new ArrayMesh();
        foreach (var (open, dash, gap, colour) in new[] { (true, 0.006f, 0.005f, BlockMesh.Hex(0x7a5a35)), (false, 0.004f, 0.012f, BlockMesh.Hex(0xa8483a)) })
        {
            var lines = Roads.Where(r => r.Open == open).Select(r => r.Line).ToList();
            if (lines.Count == 0) continue;
            var part = DashedRibbons(lines, colour, dash, gap, width: 0.0019f, halo: 0.0036f);
            for (var s = 0; s < part.GetSurfaceCount(); s++)
            {
                roads.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, part.SurfaceGetArrays(s));
                roads.SurfaceSetMaterial(roads.GetSurfaceCount() - 1, part.SurfaceGetMaterial(s));
            }
        }
        RoadLines.Mesh = roads;
        ShownYear = pastYear;
    }

    /// <summary>The past year the markers show, or null for today.</summary>
    public int? ShownYear { get; private set; }

    /// <summary>Shows or hides the borders, settlements and structures (news tags are the screen's).</summary>
    public void SetLayers(MapLayers layers)
    {
        Layers = layers;
        BorderLines.Visible = layers.HasFlag(MapLayers.Borders);
        SettlementLayer.Visible = layers.HasFlag(MapLayers.Settlements);
        StructureLayer.Visible = layers.HasFlag(MapLayers.Structures);
        War.Visible = layers.HasFlag(MapLayers.Armies);
        RoadLines.Visible = layers.HasFlag(MapLayers.TradeRoutes);
    }

    /// <summary>The digest's pins as stems over their places (the screen draws their numbered labels).</summary>
    public void ShowPins(IEnumerable<Pin> pins)
    {
        Ui.Clear(PinLayer);
        _pins.Clear();
        _pins.AddRange(pins);
        foreach (var pin in _pins)
        {
            var node = new Node3D { Name = $"Pin{pin.Number}", Position = ToGodot(pin.Position) };
            node.AddChild(new MeshInstance3D
            {
                Name = "Stem", Position = new Vector3(0, -Client.Markers.PinLift / 2, 0),
                Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = Client.Markers.PinLift },
                MaterialOverride = Flat(Palette.Ink),
            });
            node.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f }, MaterialOverride = Flat(Palette.Ink) });
            PinLayer.AddChild(node);
        }
    }

    /// <summary>Eases the camera to a place on the map.</summary>
    public void Focus(MapPoint point, Zoom? zoom = null)
    {
        if (_geography is null) return;
        var at = Client.Projection.ToBlock(_geography.Terrain, point, 0);
        Rig.Focus(at, zoom);
    }

    /// <summary>The marker or pin drawn nearest a screen point, within <see cref="PickRadius"/> pixels.</summary>
    public EntityRef? Pick(Vector2 screen)
    {
        // Banners pick as their host, battle marks as the battle (an "event" ref); both above the settlements they stand on.
        var war = !War.Visible ? Enumerable.Empty<(EntityRef, Rgb)>()
            : War.Armies.Select(a => (a.Ref, War.BannerAt(a.Ref) is { } b ? new Rgb(b.X, b.Y + WarLayer.BannerHeight * 0.7f, b.Z) : Rgb.Zero))
                .Concat(War.RecentBattles.Select(b => (new EntityRef("event", b.EventId), Client.Projection.ToBlock(_geography!.Terrain, b.Position, 0.018f))));
        var beasts = !StructureLayer.Visible ? Enumerable.Empty<(EntityRef, Rgb)>()
            : Monsters.Select(m => (m.Ref, Client.Projection.ToBlock(_geography!.Terrain, m.Position, 0.02f)));
        var candidates = war.Concat(beasts).Concat(_pins.Select(p => (p.Place, p.Position))).Concat(_markers
            .Where(m => m.Shape == MarkerShape.Settlement ? SettlementLayer.Visible : StructureLayer.Visible).Select(m => (m.Ref, m.Position)));
        EntityRef? best = null;
        var bestDistance = PickRadius;
        foreach (var (entity, position) in candidates)
        {
            var world = ToGodot(position);
            if (Camera.IsPositionBehind(world)) continue;
            var distance = Camera.UnprojectPosition(world).DistanceTo(screen);
            if (distance < bestDistance) (best, bestDistance) = (entity, distance);
        }
        return best;
    }

    /// <summary>Where a block-space point appears on screen.</summary>
    public Vector2 ScreenOf(Rgb position) => Camera.UnprojectPosition(ToGodot(position));

    public override void _Process(double delta)
    {
        Rig.Update((float)delta);
        UpdateCamera();
        // Beasts, like banners, keep a readable size at any zoom.
        foreach (var node in StructureLayer.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().StartsWith(Monster.Kind)))
            node.Scale = Vector3.One * WarLayer.ScaleFor(Camera.GlobalPosition.DistanceTo(node.GlobalPosition));
    }

    public override void _UnhandledInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) (_dragging, _pressedAt) = (true, button.Position);
                else
                {
                    _dragging = false;
                    if (button.Position.DistanceTo(_pressedAt) < 4 && Pick(button.Position) is { } picked) Picked?.Invoke(picked);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Rig.Scroll(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Rig.Scroll(-1);
                break;
            case InputEventMouseMotion motion when _dragging:
                Rig.Drag(motion.Relative.X, motion.Relative.Y);
                break;
        }
    }

    private void UpdateCamera()
    {
        if (Camera is null) return;
        var eye = Rig.Eye;
        // Never inside a mountain: keep the eye a little above the ground beneath it.
        if (_geography is not null && Math.Abs(eye.X) <= 1 && Math.Abs(eye.Z) <= 1)
            eye.Y = Math.Max(eye.Y, Math.Max(0, BlockMesh.HeightAt(_geography.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)) + 0.04f);
        Camera.Position = ToGodot(eye);
        Camera.LookAt(ToGodot(Rig.Target), Vector3.Up);
        // The screens' cards sit on the right, so the world is framed slightly left of centre.
        Camera.HOffset = Rig.Distance * FrameOffset;
    }

    private static readonly Rgb Plaster = BlockMesh.Hex(0xf1ebde);
    private static readonly Rgb Stone = BlockMesh.Hex(0xb9b1a2);

    /// <summary>Roofs take the holder's colour, softened toward terracotta so heraldry reads without shouting.</summary>
    private static Rgb Roof(Rgb heraldic)
    {
        var roof = Rgb.Lerp(heraldic, BlockMesh.Hex(0xb5654a), 0.3f);
        // Keep roofs mid-toned: sable (black) and argent (white) houses would read as holes and glare.
        var luma = 0.3f * roof.X + 0.59f * roof.Y + 0.11f * roof.Z;
        return Rgb.Clamp(roof * (Math.Clamp(luma, 0.32f, 0.62f) / Math.Max(0.01f, luma)), Rgb.Zero, Rgb.One);
    }

    /// <summary>A beast in its lair: a dark spiked token with glowing eyes; legends wear a turning gold ring; sleepers are grey.</summary>
    private static Node3D BeastNode(MapMonster beast, Geography geography)
    {
        var node = new Node3D
        {
            Name = $"monster{beast.Ref.Id}",
            Position = ToGodot(Client.Projection.ToBlock(geography.Terrain, beast.Position, 0)) + new Vector3(-0.03f, 0, 0.02f),
        };
        var size = 0.02f + 0.02f * beast.Might / 100f;
        var hide = new StandardMaterial3D { AlbedoColor = Ui.C(beast.Slumbering ? BlockMesh.Hex(0x77736b) : BlockMesh.Hex(0x2b2230)), Roughness = 0.7f };
        node.AddChild(new MeshInstance3D
        {
            Name = "Body", Position = new Vector3(0, size * 0.35f, 0),
            Mesh = new SphereMesh { Radius = size * 0.45f, Height = size * 0.7f, RadialSegments = 10, Rings = 5 }, MaterialOverride = hide,
        });
        for (var k = 0; k < 3; k++)
        {
            var angle = k * Mathf.Tau / 3;
            node.AddChild(new MeshInstance3D
            {
                Name = $"Spike{k}", Position = new Vector3(Mathf.Cos(angle) * size * 0.22f, size * 0.85f, Mathf.Sin(angle) * size * 0.22f),
                Rotation = new Vector3(Mathf.Sin(angle) * 0.4f, 0, -Mathf.Cos(angle) * 0.4f),
                Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = size * 0.12f, Height = size * 0.5f, RadialSegments = 5 }, MaterialOverride = hide,
            });
        }
        if (!beast.Slumbering)
            node.AddChild(new MeshInstance3D
            {
                Name = "Eyes", Position = new Vector3(0, size * 0.45f, size * 0.42f),
                Mesh = new BoxMesh { Size = new Vector3(size * 0.4f, size * 0.07f, size * 0.05f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xff8a3d)), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            });
        if (beast.Legendary)
        {
            var ring = new MeshInstance3D
            {
                Name = "Legend", Position = new Vector3(0, 0.002f, 0),
                Mesh = new TorusMesh { InnerRadius = size * 0.75f, OuterRadius = size * 0.85f, Rings = 24, RingSegments = 6 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xc9a227)), Metallic = 0.7f, Roughness = 0.3f },
            };
            node.AddChild(ring);
            var spin = ring.CreateTween().SetLoops();
            spin.TweenProperty(ring, "rotation:y", Mathf.Tau, 6.0).From(0f);
        }
        return node;
    }

    /// <summary>
    /// Landmarks are pale obelisks, labyrinths a ring of standing walls, ruins a few broken stones; fortresses a squat
    /// curtain wall with corner towers, shrines a gilt dome on a plinth, mage towers a slender violet spire and temples
    /// a pale hall under a gilt roof.
    /// </summary>
    private static Node3D MarkerNode(Marker marker)
    {
        var name = marker.Shape is MarkerShape.Tower or MarkerShape.Temple ? $"{marker.Shape.ToString().ToLowerInvariant()}{marker.Ref.Id}" : $"{marker.Ref.Kind}{marker.Ref.Id}";
        var node = new Node3D { Name = name, Position = ToGodot(marker.Position) };
        var s = marker.Size;
        switch (marker.Shape)
        {
            case MarkerShape.Landmark:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.35f, s * 1.6f, s * 0.35f) }, Stone, s * 0.8f));
                node.AddChild(Solid(new PrismMesh { Size = new Vector3(s * 0.4f, s * 0.35f, s * 0.4f) }, marker.Colour, s * 1.78f));
                break;
            case MarkerShape.Labyrinth:
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * Mathf.Tau / 8;
                    var wall = Solid(new BoxMesh { Size = new Vector3(s * 0.38f, s * 0.35f, s * 0.08f) }, Stone, s * 0.17f);
                    wall.Position += new Vector3(Mathf.Cos(angle) * s * 0.5f, 0, Mathf.Sin(angle) * s * 0.5f);
                    wall.Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0);
                    node.AddChild(wall);
                }
                node.AddChild(Solid(new CylinderMesh { TopRadius = s * 0.16f, BottomRadius = s * 0.16f, Height = s * 0.06f }, Rgb.Lerp(marker.Colour, Stone, 0.45f), s * 0.03f));
                break;
            case MarkerShape.Fortress:
                for (var k = 0; k < 4; k++)
                {
                    var angle = k * Mathf.Tau / 4;
                    var wall = Solid(new BoxMesh { Size = new Vector3(s * 0.9f, s * 0.32f, s * 0.12f) }, Stone, s * 0.16f);
                    wall.Position += new Vector3(Mathf.Cos(angle) * s * 0.45f, 0, Mathf.Sin(angle) * s * 0.45f);
                    wall.Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0);
                    node.AddChild(wall);
                    var tower = Solid(new CylinderMesh { TopRadius = s * 0.13f, BottomRadius = s * 0.15f, Height = s * 0.55f, RadialSegments = 8 }, Stone, s * 0.27f);
                    tower.Position += new Vector3(Mathf.Cos(angle + Mathf.Pi / 4) * s * 0.64f, 0, Mathf.Sin(angle + Mathf.Pi / 4) * s * 0.64f);
                    node.AddChild(tower);
                }
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.3f, s * 0.7f, s * 0.3f) }, Rgb.Lerp(Stone, marker.Colour, 0.25f), s * 0.35f));
                break;
            case MarkerShape.Shrine:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.7f, s * 0.12f, s * 0.7f) }, Stone, s * 0.06f));
                node.AddChild(Solid(new SphereMesh { Radius = s * 0.28f, Height = s * 0.56f, RadialSegments = 12, Rings = 6, IsHemisphere = true }, marker.Colour, s * 0.12f));
                break;
            case MarkerShape.Tower:
                node.AddChild(Solid(new CylinderMesh { TopRadius = s * 0.16f, BottomRadius = s * 0.2f, Height = s * 1.8f, RadialSegments = 8 }, Stone, s * 0.9f));
                node.AddChild(Solid(new CylinderMesh { TopRadius = 0, BottomRadius = s * 0.26f, Height = s * 0.6f, RadialSegments = 8 }, marker.Colour, s * 2.1f));
                node.AddChild(new MeshInstance3D
                {
                    Name = "Glow", Position = new Vector3(0, s * 1.55f, 0), Mesh = new SphereMesh { Radius = s * 0.09f, Height = s * 0.18f },
                    MaterialOverride = Flat(Rgb.Lerp(marker.Colour, Rgb.One, 0.5f)),
                });
                break;
            case MarkerShape.Temple:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.9f, s * 0.55f, s * 0.6f) }, BlockMesh.Hex(0xe8e2d4), s * 0.28f));
                node.AddChild(Solid(new PrismMesh { Size = new Vector3(s * 1.0f, s * 0.35f, s * 0.68f) }, marker.Colour, s * 0.72f));
                break;
            default:
                for (var k = 0; k < 3; k++)
                {
                    var stone = Solid(new BoxMesh { Size = new Vector3(s * 0.22f, s * (0.25f + 0.2f * k), s * 0.22f) }, marker.Colour, s * 0.15f);
                    stone.Position += new Vector3((k - 1) * s * 0.3f, 0, (k % 2) * s * 0.2f);
                    stone.Rotation = new Vector3(0.15f * k, 0.6f * k, 0);
                    node.AddChild(stone);
                }
                break;
        }
        return node;
    }

    private static MeshInstance3D Solid(Mesh mesh, Rgb colour, float lift) =>
        new() { Mesh = mesh, Position = new Vector3(0, lift, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(colour), Roughness = 0.8f } };

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    private static StandardMaterial3D VertexColours() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };

    /// <summary>
    /// Lines drawn at a constant thickness on screen whatever the zoom: each vertex carries the direction across its
    /// line (NORMAL) and which side it is on (UV.x), and the shader pushes it out in proportion to its distance from
    /// the camera. Width is in thousandths of the distance (about 1.6 px at 1440×900 per 0.001).
    /// </summary>
    internal const string LineShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform vec4 colour : source_color = vec4(0.1, 0.1, 0.09, 0.9);
        uniform float width = 0.0016;
        void vertex() {
            vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            float distance_to_eye = length(CAMERA_POSITION_WORLD - world);
            VERTEX += NORMAL * UV.x * width * distance_to_eye;
            VERTEX.y += 0.0008 * distance_to_eye;
        }
        void fragment() {
            ALBEDO = colour.rgb;
            ALPHA = colour.a;
        }
        """;

    /// <summary>Realm borders as dashed lines of constant screen width, ink over a pale halo so they read on any ground.</summary>
    internal static ArrayMesh DashedRibbons(IReadOnlyList<IReadOnlyList<Rgb>> lines, Rgb colour, float dash = 0.014f, float gap = 0.008f, float width = 0.0016f, float halo = 0.0034f)
    {
        var mesh = new ArrayMesh();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var line in lines)
        {
            var travelled = 0f;
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (ToGodot(line[i]), ToGodot(line[i + 1]));
                var length = a.DistanceTo(b);
                if (length < 1e-6f) continue;
                var across = new Vector3(-(b.Z - a.Z), 0, b.X - a.X).Normalized();
                for (var t = 0f; t < length;)
                {
                    var phase = (travelled + t) % (dash + gap);
                    var span = phase < dash ? Math.Min(dash - phase, length - t) : Math.Min(dash + gap - phase, length - t);
                    if (phase < dash)
                    {
                        var (p, q) = (a.Lerp(b, t / length), a.Lerp(b, (t + span) / length));
                        vertices.AddRange(new[] { p, q, p, q, q, p });
                        normals.AddRange(Enumerable.Repeat(across, 6));
                        uvs.AddRange(new[] { new Vector2(-1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 0) });
                    }
                    t += Math.Max(span, 1e-5f);
                }
                travelled += length;
            }
        }
        if (vertices.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        // The same dashes twice: a wide pale halo, then the ink line on top.
        foreach (var (tint, size, priority) in new[] { (Palette.Card, halo, 0), (colour, width, 1) })
        {
            if (size <= 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = new Shader { Code = LineShader }, RenderPriority = priority };
            material.SetShaderParameter("colour", Ui.C(tint, priority == 0 ? 0.75f : 0.95f));
            material.SetShaderParameter("width", size);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        return mesh;
    }

    /// <summary>Instances of one mesh with per-instance transforms and colours (colours given in sRGB).</summary>
    private static MultiMesh Instances(Mesh mesh, IEnumerable<(Transform3D Transform, Rgb Colour)> items)
    {
        var list = items.ToList();
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
        for (var i = 0; i < list.Count; i++)
        {
            multi.SetInstanceTransform(i, list[i].Transform);
            multi.SetInstanceColor(i, Ui.C(list[i].Colour).SrgbToLinear());
        }
        return multi;
    }

    private static Transform3D Place(Rgb at, Vector3 scale, float yaw) =>
        new(new Basis(Vector3.Up, yaw).Scaled(scale), ToGodot(at));

    private static StandardMaterial3D Foliage() => new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    private static StandardMaterial3D Painted() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.9f };

    /// <summary>A radial falloff from soft grey to clear, for the shadow under the block.</summary>
    private static ImageTexture SoftShadow(int size = 128)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // A rounded square: the block is square, its shadow a soft square.
                var dx = Math.Max(0, Math.Abs((x + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var dy = Math.Max(0, Math.Abs((y + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp(1 - d, 0, 1);
                image.SetPixel(x, y, new Color(0.1f, 0.1f, 0.09f, 0.22f * a * a));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private T Child<T>(string name) where T : Node, new()
    {
        if (GetNodeOrNull<T>(name) is { } existing) return existing;
        var child = new T { Name = name };
        AddChild(child);
        return child;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select((c, i) => Ui.C(c, data.Alphas?[i] ?? 1).SrgbToLinear()).ToArray();
        // Godot treats clockwise triangles as front faces; the client's indices are counter-clockwise.
        var indices = new int[data.Indices.Length];
        for (var i = 0; i < indices.Length; i += 3) (indices[i], indices[i + 1], indices[i + 2]) = (data.Indices[i], data.Indices[i + 2], data.Indices[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    public static Vector3 ToGodot(Rgb v) => new(v.X, v.Y, v.Z);
}
```

Replace `game/scripts/screens/CharacterView.cs` entirely with:
```csharp
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Character page* (sub-project 4 §4): name, role line, trait and ambition chips and a painted inset map of
/// where they are on the left; their life and deeds down the middle with the latest great deed in a dark card; ties,
/// Follow and Family tree on the right. Every name is a link.
/// </summary>
public partial class CharacterView : Control
{
    public Main Host { get; init; } = null!;
    public CharacterScreen Character { get; init; } = null!;

    public VBoxContainer Timeline { get; private set; } = null!;
    public Button FollowButton { get; private set; } = null!;

    public override void _Ready()
    {
        Name = "CharacterScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Ui.Page());

        var left = Ui.Place(Ui.Column("Left", 24), 0, 0, 56, 40);
        left.CustomMinimumSize = new Vector2(380, 0);
        left.AddChild(Ui.BackLink(() => Host.Router.Back()));
        var who = Ui.Column("Who", 10);
        who.AddChild(Ui.Kicker(Character.Kicker));
        who.AddChild(Ui.FittedTitle($"{Character.Name}.", 76, 380));
        who.AddChild(Ui.Body(Character.Line, "Line", Palette.Body));
        left.AddChild(who);
        var chips = new HFlowContainer { Name = "Chips" };
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        if (Character.Saint) chips.AddChild(Ui.Chip("Saint", filled: true, name: "Saint", dot: Palette.Gold));
        if (Character.Faith is { } faith) chips.AddChild(Ui.Chip(faith.Name, name: "Faith", dot: Palette.Gold));
        foreach (var trait in Character.Traits) chips.AddChild(Ui.Chip(trait, name: "Trait"));
        if (Character.Ambition.Length > 0)
            chips.AddChild(Ui.Chip(Character.Ambition.StartsWith("Ambition", StringComparison.OrdinalIgnoreCase) ? Character.Ambition : $"Ambition: {Character.Ambition}",
                filled: true, name: "Ambition"));
        left.AddChild(chips);
        AddChild(left);
        AddChild(Inset());

        var middle = Ui.Place(Ui.Column("Middle", 18), 0, 0, 476, 92);
        middle.CustomMinimumSize = new Vector2(580, 0);
        middle.AddChild(Ui.Kicker("Life & deeds"));
        var scroll = new ScrollContainer { Name = "Scroll", CustomMinimumSize = new Vector2(580, 760), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Timeline = Ui.Column("Timeline", 0);
        Timeline.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (Character.Earlier > 0)
            Timeline.AddChild(Ui.Body($"{Character.Earlier} earlier deeds are in the full chronicle.", "Earlier", Palette.Muted, 12));
        foreach (var row in Character.Timeline) Timeline.AddChild(row.Highlight ? Highlight(row) : Row(row));
        if (Character.Folded > 0)
            Timeline.AddChild(Ui.Ruled("Folded", Ui.Body($"… and {Character.Folded} smaller moments.", "Text", Palette.Muted, 12), ruleBelow: true));
        scroll.AddChild(Timeline);
        middle.AddChild(scroll);
        AddChild(middle);
        // Lives read upward from the latest deed, as in the design: open at the bottom.
        Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();

        AddChild(TiesCard());
    }

    private Control Row(TimelineRow row)
    {
        var line = Ui.Row("Row", 0);
        var year = Ui.Line($"Year {row.Year}", "Year", Palette.Muted, 12);
        year.CustomMinimumSize = new Vector2(76, 0);
        line.AddChild(year);
        line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 14, Palette.Ink));
        var ruled = Ui.Ruled($"Event{row.EventId}", line);
        ((StyleBoxFlat)ruled.GetThemeStylebox("panel")).ContentMarginTop = 14;
        ((StyleBoxFlat)ruled.GetThemeStylebox("panel")).ContentMarginBottom = 14;
        return ruled;
    }

    /// <summary>The year's most important deed, in the design's dark card with a larger serif line.</summary>
    private Control Highlight(TimelineRow row)
    {
        var card = Ui.Card($"Event{row.EventId}", 0);
        var style = (StyleBoxFlat)card.GetThemeStylebox("panel").Duplicate();
        (style.BgColor, style.BorderColor) = (Ui.C(Palette.Ink), Ui.C(Palette.Ink));
        (style.CornerRadiusBottomLeft, style.CornerRadiusBottomRight, style.CornerRadiusTopLeft, style.CornerRadiusTopRight) = (12, 12, 12, 12);
        (style.ContentMarginTop, style.ContentMarginBottom, style.ContentMarginLeft, style.ContentMarginRight) = (18, 18, 20, 20);
        card.AddThemeStyleboxOverride("panel", style);
        var line = Ui.Row("Row", 0);
        var year = Ui.Line($"Year {row.Year}", "Year", new System.Numerics.Vector3(0.73f, 0.72f, 0.69f), 12);
        year.CustomMinimumSize = new Vector2(76, 0);
        line.AddChild(year);
        line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 22, Palette.Card, serif: true));
        card.AddChild(line);
        var spacer = new MarginContainer { Name = "Highlight" };
        spacer.AddThemeConstantOverride("margin_top", 4);
        spacer.AddChild(card);
        return spacer;
    }

    private PanelContainer TiesCard()
    {
        var card = Ui.Place(Ui.Card("Ties", 320), 1, 0, -40, 40);
        var column = Ui.Column("Column", 16);
        column.AddChild(Ui.Kicker("Ties"));
        var list = Ui.Column("List", 0);
        for (var i = 0; i < Character.Ties.Count; i++)
        {
            var tie = Character.Ties[i];
            var entry = Ui.Column("Entry", 2);
            var label = tie.Qualifier.Length > 0 ? $"{tie.Relation} · {tie.Qualifier}" : tie.Relation;
            var kicker = Ui.Body(label, "Relation", tie.Tone == Sim.Views.Tone.Hostile ? Palette.Conflict : Palette.Muted, 11);
            entry.AddChild(kicker);
            entry.AddChild(Ui.NameLink(new Mention(tie.Ref, tie.Name), "Name", Host.Router.Open));
            list.AddChild(Ui.Ruled($"Tie{i}", entry, ruleBelow: i == Character.Ties.Count - 1));
        }
        if (Character.Ties.Count == 0) list.AddChild(Ui.Body("No close ties are recorded.", "None", Palette.Muted, 12));
        column.AddChild(list);
        var actions = Ui.Row("Actions", 6);
        FollowButton = Ui.Button(Character.Followed ? "Following" : "Follow", "Follow", primary: !Character.Followed);
        FollowButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        FollowButton.Pressed += ToggleFollow;
        var family = Ui.Button("Family tree", "Family");
        family.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        family.Pressed += () => Host.Router.Go(new Screen(ScreenKind.Family, Character.Ref.Id));
        actions.AddChild(FollowButton);
        actions.AddChild(family);
        column.AddChild(actions);
        card.AddChild(column);
        return card;
    }

    /// <summary>Follows or unfollows: followed people lead the digest and are never compacted from memory.</summary>
    public void ToggleFollow()
    {
        var follow = !Host.Session!.IsFollowed(Character.Ref);
        Host.Session.Follow(Character.Ref, follow);
        FollowButton.Text = follow ? "Following" : "Follow";
        foreach (var (state, filled) in new[] { ("normal", !follow), ("hover", !follow) }) FollowButton.AddThemeStyleboxOverride(state, Ui.Pill(filled));
        FollowButton.AddThemeColorOverride("font_color", follow ? Ui.C(Palette.Ink) : Colors.White);
    }

    /// <summary>A painted map of the world with a pin where they are now (or were last seen).</summary>
    private Control Inset()
    {
        var frame = Ui.Place(new Control { Name = "Inset", CustomMinimumSize = new Vector2(320, 250), Size = new Vector2(320, 250) }, 0, 1, 56, -290);
        if (Host.MiniMap() is not { } texture) return frame;
        // The world square, framed like the design's cards.
        var border = new Panel { Name = "Frame", Size = new Vector2(252, 252), Position = new Vector2(-1, -1), MouseFilter = MouseFilterEnum.Ignore };
        border.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ui.C(Palette.Card), BorderColor = Ui.C(Palette.Border), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            ShadowColor = new Color(0.1f, 0.1f, 0.09f, 0.06f), ShadowSize = 16, ShadowOffset = new Vector2(0, 8),
        });
        frame.AddChild(border);
        var map = new TextureRect
        {
            Name = "Map", Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Size = new Vector2(250, 250), MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.AddChild(map);
        if (Character.Location is { Position: { } position } location)
        {
            var at = new Vector2((float)position.X * 250, (float)position.Y * 250);
            var pin = Ui.Row("Pin", 6);
            var dot = Ui.Capsule("Dot", Ui.C(Palette.Realm), Ui.C(Palette.Card), 0, 0);
            dot.CustomMinimumSize = new Vector2(18, 18);
            dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pin.AddChild(dot);
            var label = Ui.Capsule("Label", Ui.C(Palette.Card), Ui.C(Palette.Border));
            label.AddChild(Ui.Line(location.Label, "Text", Palette.Ink, 12));
            pin.AddChild(label);
            pin.Position = at - new Vector2(9, 12);
            frame.AddChild(pin);
            var open = Ui.Button("Show on map", "ShowOnMap");
            open.Position = new Vector2(0, 258);
            open.Pressed += () => Host.Router.Open(location.Ref);
            frame.AddChild(open);
        }
        return frame;
    }
}
```

Replace `game/scripts/screens/RealmView.cs` entirely with:
```csharp
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A realm's page: ruler, laws and standing in the facts card; council, vassals, treaties, wars and eras.</summary>
public partial class RealmView : DocumentPage
{
    public RealmScreen Realm { get; init; } = null!;

    public override void _Ready()
    {
        Frame("RealmScreen", $"Realm · {Realm.Rank}", $"{Realm.Title}.",
            $"{Realm.Government} · {Realm.Regions} region{(Realm.Regions == 1 ? "" : "s")} · {Realm.Phase.ToLowerInvariant()}");
        Facts.AddChild(Ui.Kicker("At a glance"));
        if (Realm.Ruler is { } ruler) Fact("Ruler", ruler.Name, ruler.Ref);
        Fact("Capital", Realm.Capital.Label, Realm.Capital.Ref);
        if (Realm.Liege is { } liege) Fact("Liege", liege.Name, liege.Ref);
        Fact("Succession", Realm.SuccessionLaw);
        if (Realm.Faith is { } faith) Fact("Faith", faith.Name);
        Fact("Magic", Realm.MagicLaw);
        Fact("Legitimacy · stability", $"{Realm.Legitimacy} · {Realm.Stability}");
        Fact("Treasury", $"{Realm.Treasury:N0} crowns");
        var show = Ui.Button("Show on map", "ShowOnMap", primary: true);
        show.Pressed += () => Host.Router.Open(Realm.Capital.Ref);
        var pad = new MarginContainer { Name = "Action" };
        pad.AddThemeConstantOverride("margin_top", 14);
        pad.AddChild(show);
        Facts.AddChild(pad);

        if (Realm.Council.Count > 0)
        {
            var council = Section("Council");
            for (var i = 0; i < Realm.Council.Count; i++)
                Row(council, char.ToUpperInvariant(Realm.Council[i].Seat[0]) + Realm.Council[i].Seat[1..], Named(Realm.Council[i].Member), i == Realm.Council.Count - 1);
        }
        if (Realm.Vassals.Count > 0)
        {
            var vassals = Section("Vassals");
            for (var i = 0; i < Realm.Vassals.Count; i++) Row(vassals, "Vassal", Named(Realm.Vassals[i]), i == Realm.Vassals.Count - 1);
        }
        if (Realm.Treaties.Count > 0)
        {
            var treaties = Section("Treaties");
            for (var i = 0; i < Realm.Treaties.Count; i++)
                Row(treaties, $"Year {Realm.Treaties[i].SinceYear}", Named(Realm.Treaties[i].With, $"{Realm.Treaties[i].Kind} with "), i == Realm.Treaties.Count - 1);
        }
        if (Realm.Wars.Count > 0)
        {
            var wars = Section("Wars");
            for (var i = 0; i < Realm.Wars.Count; i++)
            {
                var war = Realm.Wars[i];
                Row(wars, war.Ended ? "Ended" : "Ongoing", Named(new Mention(war.War, war.Name), after: $" · as {war.Side}"), i == Realm.Wars.Count - 1);
            }
        }
        if (Realm.Eras.Count > 0)
        {
            var eras = Section("Eras");
            for (var i = 0; i < Realm.Eras.Count; i++)
            {
                var era = Realm.Eras[i];
                var row = Row(eras, $"Year {era.Year}", LinkedText.Plain(era.Phase.Length > 0 ? $"{era.Name} · {era.Phase.ToLowerInvariant()}" : era.Name), i == Realm.Eras.Count - 1);
                var eventId = era.EventId;
                row.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Host.Router.Explain(eventId);
                };
            }
        }
    }
}
```

Replace `game/tests/HeadlessChecks.cs` entirely with:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// Runs the real scenes headless against a fresh tiny world and checks what they show (sub-project 4 §7). Exits 0 when
/// every check passes, 1 otherwise; CI runs it with `godot --headless --path game res://tests/HeadlessChecks.tscn`.
/// </summary>
public partial class HeadlessChecks : Node
{
    private readonly List<string> _failures = new();
    private int _checks;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "checks", $"check-{System.Guid.NewGuid():N}.fw.db");
        try
        {
            var session = await Task.Run(() => Session.Create(path, 3, WorldSize.Tiny, () => System.DateTimeOffset.UtcNow, prehistoryYears: 8));
            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            main.Use(session);
            AddChild(main);
            await Until(() => main.CaughtUp, "the world catches up");
            await RunChecks(main);
        }
        catch (System.Exception e)
        {
            Fail($"unexpected {e}");
        }
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GD.Print(_failures.Count == 0 ? $"HEADLESS CHECKS PASSED ({_checks})" : $"HEADLESS CHECKS FAILED: {string.Join("; ", _failures)}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>The checks for each screen; later tasks add theirs.</summary>
    private async Task RunChecks(Main main)
    {
        Check(main.Status.Text.StartsWith("Caught up · "), $"status reads caught up (\"{main.Status.Text}\")");
        await Frames(2);
        await DioramaChecks(main);
        await DigestChecks(main);
        await MapChecks(main);
        await PageChecks(main);
        await ReadingChecks(main);
        await WarChecks(main);
    }

    /// <summary>Stages a small war (a host marching across a border into land it occupies) and checks the war layer.</summary>
    private async Task WarChecks(Main main)
    {
        var session = main.Session!;
        var (armyRef, commander) = session.Read((world, log) =>
        {
            var link = world.Geography!.Graph.Links.First(l => world.Regions[l.A].OwnerRealmId != 0 && world.Regions[l.B].OwnerRealmId != 0
                                                             && world.Regions[l.A].OwnerRealmId != world.Regions[l.B].OwnerRealmId);
            var (home, target) = (world.Regions[link.A], world.Regions[link.B]);
            var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
            var war = new FantasyWorld.Sim.Model.War { Id = world.Ids.Take("war"), Name = "The Staged War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id } };
            world.Wars[war.Id] = war;
            var leader = world.Characters.Values.First(c => c.IsActive);
            var army = new FantasyWorld.Sim.Model.Army
            {
                Id = world.Ids.Take("army"), Name = $"The Host of {a.Name}", RealmId = a.Id, WarId = war.Id, CommanderId = leader.Id, Levies = 800,
                RegionId = home.Id, Path = { target.Id }, TargetRegionId = target.Id,
            };
            world.Armies[army.Id] = army;
            target.OccupierRealmId = a.Id;
            var beastId = world.Ids.Take("monster");
            var rose = log.Append(world.Date, new FantasyWorld.Sim.Events.EventDraft("MonsterRose",
                Actors: new Dictionary<string, FantasyWorld.Sim.Events.EntityRef> { ["monster"] = new("monster", beastId) }, Place: home.Ref));
            world.Monsters[beastId] = new FantasyWorld.Sim.Model.Monster
            {
                Id = beastId, Name = "The Wyrm of the Staged War", Breed = "wyrm", RegionId = home.Id, Might = 80, Legendary = true, RoseEventId = rose.Id,
            };
            return (army.Ref, leader.Ref);
        });
        main.Router.Go(new Screen(ScreenKind.Map));
        main.Diorama.Show(session.Map());
        (main.Current as MapHudView)?.Today(session.Map());
        main.Diorama.Rig.Reset();
        await Frames(90);
        var war = main.Diorama.War;
        Check(war.Banners.GetChildCount() == 1 && war.Arrows.GetChildCount() == 1, "a host on the march has a banner and a campaign arrow");
        Check(war.Hatch.Mesh is not null && war.Outline.Mesh is not null, "occupied land is hatched and outlined");
        Check(war.Fronts.Mesh is ArrayMesh { } fronts && fronts.GetSurfaceCount() >= 2, "the front between the sides is drawn with its teeth");
        var tag = (main.Current as MapHudView)?.TagLayer.GetChildren().OfType<ArmyTag>().FirstOrDefault();
        Check(tag is not null && tag.Army.Ref == armyRef, "the host is named on the map");
        Check(main.Diorama.Monsters.Count == 1 && main.Diorama.StructureLayer.GetNodeOrNull("monster" + main.Diorama.Monsters[0].Ref.Id) is not null,
            "a beast stands in its lair");
        await Shot("war");
        var banner = war.BannerAt(armyRef)!.Value;
        var screen = main.Diorama.Camera.UnprojectPosition(banner + new Godot.Vector3(0, WarLayer.BannerHeight * 0.7f, 0));
        Check(main.Diorama.Pick(screen) == armyRef, "clicking a banner picks its host");
        main.Router.Home();
        await Frames(2);
        main.Router.Open(commander);
        await Frames(2);
        Check(main.Current is CharacterView c && c.Character.Ref == commander, "a host's commander has a page");
        main.Router.Home();
    }

    private async Task ReadingChecks(Main main)
    {
        var session = main.Session!;
        var realm = session.Read((w, _) => w.Realms.Keys.First());
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("realm", realm));
        await Frames(2);
        Check(main.Current is RealmView r && r.GetNode<Label>("Left/Title").Text == $"{r.Realm.Title}.", "a realm link opens the realm's page");
        await Shot("realm");
        if (session.Read((w, _) => w.Artifacts.Keys.Cast<long?>().FirstOrDefault()) is { } artifact)
        {
            main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("artifact", artifact));
            await Frames(2);
            Check(main.Current is ArtifactView a && a.GetNode<Label>("Left/Title").Text == $"{a.Artifact.Name}.", "an artifact link opens its page");
        }
        main.Router.Go(new Screen(ScreenKind.Chronicle));
        await Frames(2);
        var chronicle = main.Current as ChronicleView;
        Check(chronicle is not null && chronicle.Page == chronicle.Chronicle.Pages && chronicle.Body.GetChildCount() == chronicle.Chronicle.Seasons.Count,
            "the full chronicle opens at its latest page, by season");
        if (chronicle is not null)
        {
            chronicle.Filter(FantasyWorld.Sim.Views.Category.War);
            Check(chronicle.Chronicle.Seasons.SelectMany(s => s.Rows).All(r => r.Category == FantasyWorld.Sim.Views.Category.War), "the chronicle filters by system");
            await Shot("chronicle");
        }

        main.Router.Home();
        main.Router.Go(new Screen(ScreenKind.Map));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView, "Esc goes back");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, Pressed = true });
        await Frames(2);
        Check(main.Current is MapHudView, "M opens the map");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView && !main.Router.CanGoBack, "H goes home");

        var news = session.Read((w, l) => l.All.Last(e => e.Importance >= Main.LiveToastImportance).Id);
        main.Toast("A test of the live notice.", news);
        await Frames(2);
        Check(main.CurrentToast is { Visible: true }, "notable events are announced while the game is open");
        main.CurrentToast!.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Check(main.Router.Current == new Screen(ScreenKind.Why, news), "and the notice opens its why chain");
        main.Router.Home();
    }

    private async Task PageChecks(Main main)
    {
        var session = main.Session!;
        var person = session.Read((w, l) => l.All.Reverse().SelectMany(e => e.Actors.Values).First(a => a.Kind == "char" && w.Characters.ContainsKey(a.Id)).Id);
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("char", person));
        await Frames(3);
        var character = main.Current as CharacterView;
        Check(character is not null && !main.Diorama.Visible, "a character link opens their page over the diorama");
        if (character is null) return;
        var screen = character.Character;
        Check(character.GetNode<Label>("Left/Who/Title").Text == $"{screen.Name}.", "the page is titled with their name");
        Check(character.GetNode("Left/Chips").GetChildCount() == screen.Traits.Count + (screen.Ambition.Length > 0 ? 1 : 0) + (screen.Faith is null ? 0 : 1) + (screen.Saint ? 1 : 0),
            "faith, traits and ambition are chips");
        Check(screen.Faith is null || character.GetNodeOrNull("Left/Chips/Faith") is not null, "their faith is named");
        Check(character.Timeline.GetChildren().Count(c => c.Name.ToString().StartsWith("Event") || c.Name == "Highlight") == screen.Timeline.Count, "every deed is on the timeline");
        Check(character.GetNode("Ties/Column/List").GetChildCount() == Math.Max(1, screen.Ties.Count), "ties are listed");
        await Shot("character");
        var followed = session.IsFollowed(screen.Ref);
        character.ToggleFollow();
        Check(session.IsFollowed(screen.Ref) != followed && character.FollowButton.Text == (followed ? "Follow" : "Following"), "Follow follows");
        character.ToggleFollow();

        character.GetNode<Button>("Ties/Column/Actions/Family").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shown = (main.Current as FamilyView)?.FindChildren("Person*", "", true, false).Count ?? -1;
        Check(shown == session.Family(person).Nodes.Count, $"the family tree shows everyone ({shown} of {session.Family(person).Nodes.Count})");
        await Shot("family");
        main.Router.Back();
        await Frames(2);
        Check(main.Current is CharacterView, "Back returns to the character");

        var eventId = session.Read((w, l) => l.All.Last(e => e.Causes.Count > 0 && l.TryGet(e.Causes[0].EventId, out _)).Id);
        main.Router.Explain(eventId);
        await Frames(3);
        var why = main.Current as WhyView;
        Check(why is not null, "a why chain opens");
        if (why is null) return;
        Check(why.Graph.Cards.Count == 1 + why.Why.Layers.Sum(l => l.Count) && why.Canvas.FindChildren("Card*", "", false, false).Count == why.Graph.Cards.Count,
            "every cause is a card");
        Check(why.GetNode<Label>("Intro/Summary").Text == why.Why.Summary, "the chain is summarised");
        await Shot("why");
        Check(why.Canvas.FindChildren("Title", "RichTextLabel", true, false).Cast<Control>().All(t => t.MouseFilter == Control.MouseFilterEnum.Pass),
            "clicks on a card's words reach the card");
        var cause = why.Graph.Cards[1].Card.EventId;
        why.Canvas.GetNode<PanelContainer>($"Card{cause}").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(2);
        Check(main.Router.Current == new Screen(ScreenKind.Why, cause), "clicking a cause traces it further back");
        main.Router.Back();
        await Frames(2);
        var again = (WhyView)main.Current!;
        again.GetNode<Button>("Chain/Column/ActionsRow/Actions/TraceFurther").EmitSignal(BaseButton.SignalName.Pressed);
        Check(again.ShowingConsequences && again.Graph.Cards.Count == 1 + again.Why.Consequences.Count, "trace further shows what it led to");
        main.Router.Explain(long.MaxValue);
        await Frames(3);
        Check(main.Router.Current != new Screen(ScreenKind.Why, long.MaxValue) && main.Current is not null && main.CurrentToast is not null,
            "a page whose subject is gone says so and goes back instead of breaking");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.Visible && main.Current is DigestView, "home is the digest over the diorama");

        var rig = main.Diorama.Rig;
        var peak = main.Diorama.Markers.Count == 0 ? Godot.Vector3.Zero : Diorama.ToGodot(main.Diorama.Markers[0].Position);
        rig.Focus(new System.Numerics.Vector3(peak.X, 0, peak.Z), Zoom.Town);
        rig.Drag(0, 100_000);
        rig.Drag(0, -100_000);
        rig.Scroll(100);
        await Frames(120);
        var eye = main.Diorama.Camera.Position;
        Check(eye.Y > Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(main.Geography!.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)),
            "the camera never goes below the ground");
        rig.Reset();
    }

    private async Task MapChecks(Main main)
    {
        main.Diorama.Rig.Reset();
        main.Router.Go(new Screen(ScreenKind.Map));
        await Frames(30);
        var view = main.Current as MapHudView;
        Check(view is not null, "the map screen opens");
        if (view is null) return;
        var map = view.Map;
        Check(view.GetNode<Label>("Intro/Title").Text == $"{map.WorldName}.", "the map is titled with the world's name");
        Check(view.GetNode<Label>("Intro/Summary").Text == map.Summary, "and summarised");
        var tagged = map.Settlements.Count(s => MapLabels.Shows(s, view.Host.Diorama.Rig.NearestZoom, main.Diorama.Layers));
        Check(view.TagLayer.GetChildren().OfType<SettlementTag>().Count() == tagged, $"settlements big enough for the zoom are named ({tagged})");
        Check(view.GetNode("Legend").GetChildCount() == map.Legend.Count + (map.Armies is { Count: > 0 } ? 2 : 0), "the legend lists the realms");
        await Shot("map");

        var tags = view.TagLayer.GetChildren().OfType<SettlementTag>().ToList();
        if (tags.Count > 0)
        {
            view.ShowCard(tags[0]);
            Check(view.HoverCard.Visible && view.HoverCard.FindChildren("Title", "Label", true, false).OfType<Label>().Single().Text == tags[0].Settlement.Card.Title,
                "hovering a settlement shows who holds it");
            await Shot("map-card");
        }

        var borders = view.GetNode<CheckBox>("Card/Column/Layers/Borders");
        borders.ButtonPressed = false;
        Check(!main.Diorama.BorderLines.Visible && !main.Diorama.Layers.HasFlag(MapLayers.Borders), "the borders layer can be hidden");
        borders.ButtonPressed = true;
        var trade = view.GetNode<CheckBox>("Card/Column/Layers/TradeRoutes");
        trade.ButtonPressed = false;
        Check(!main.Diorama.RoadLines.Visible, "the trade routes layer can be hidden");
        trade.ButtonPressed = true;
        Check(main.Diorama.RoadLines.Visible && main.Diorama.Roads.Count == (map.Routes?.Count ?? 0), "every trade road is drawn");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = false;
        Check(!main.Diorama.SettlementLayer.Visible && view.TagLayer.GetChildren().OfType<SettlementTag>().All(t => t.Settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent),
            "hiding settlements leaves only places with news");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = true;

        view.History.Value = 2;
        await Frames(2);
        Check(view.Year == 2 && view.Map.Historical && view.Map.Year == 2 && main.Diorama.ShownYear == 2, "the history slider shows the map of Year 2");
        await Shot("map-year-2");
        view.GetNode<Button>("Card/Column/ActionsRow/Actions/Reset").EmitSignal(BaseButton.SignalName.Pressed);
        Check(view.Year == view.Now && main.Diorama.ShownYear is null && view.History.Value == view.Now, "reset view returns to today");

        view.GetNode<Button>("Card/Column/Zoom/Presets/Region").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(90);
        Check(main.Diorama.Rig.NearestZoom == Zoom.Region && view.GetNode<Label>("Intro/Kicker").Text.EndsWith("REGION VIEW"), "the zoom presets descend");

        view.History.Value = 2;
        var settlement = map.Settlements.First(s => s.Position is not null);
        main.Router.Open(settlement.Ref);
        await Frames(2);
        Check(main.Current is MapHudView { Focus: { } focus } && focus == settlement.Ref, "a linked place opens the map");
        Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Town)
              && Diorama.ToGodot(main.Diorama.Rig.GoalTarget).DistanceTo(Diorama.ToGodot(FantasyWorld.Client.Projection.ToBlock(main.Geography!.Terrain, settlement.Position!, 0))) < 0.01f,
            "and flies to it");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.ShownYear is null, "leaving the map returns the diorama to today");
    }

    private async Task DigestChecks(Main main)
    {
        main.Router.Home();
        main.Diorama.Rig.Reset();
        await Frames(60);
        var view = main.Current as DigestView;
        Check(view is not null, "the digest is the first screen");
        if (view is null) return;
        var digest = main.Digest!;
        Check(view.GetNode<Label>("Intro/Title").Text == digest.Title, "the digest shows the year");
        Check(view.GetNode<Label>("Intro/Subtitle").Text == digest.Subtitle, "and the time away");
        Check(main.Session!.Digest().Items.Count == 0 && main.Session.Digest().Headline is null, "showing the digest marks it seen");
        Check(digest.Headline is null || view.Content.GetNode<RichTextLabel>("Headline/HeadlineTitle").GetParsedText() == digest.Headline.Title.Text,
            "the headline is shown");
        Check(view.Content.GetNodeOrNull("Items")?.GetChildCount() == digest.Items.Count, $"every item is listed ({digest.Items.Count})");
        Check(view.PinLayer.GetChildCount() == digest.Pins.Count && main.Diorama.Pins.Count == digest.Pins.Count, $"one pin per place ({digest.Pins.Count})");
        await Shot("digest");

        if (digest.Pins.Count > 0)
        {
            var pin = view.PinLayer.GetChild<PinTag>(0);
            var expected = main.Diorama.ScreenOf(main.Diorama.Pins[0].Position) - new Godot.Vector2(11, 11);
            Check(pin.Position.DistanceTo(expected) < 1, "pins follow their places on screen");
            view.FlyTo(digest.Pins[0].EventId);
            await Frames(2);
            Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Region), "an item flies the camera to its place");
        }

        view.ShowChronicle(true);
        await Frames(2);
        Check(view.Content.GetNodeOrNull("Chronicle") is not null && view.ShowingChronicle, "the toggle shows the full chronicle");
        await Shot("digest-chronicle");
        view.ShowChronicle(false);

        if (digest.Headline is { CanExplain: true } headline)
        {
            view.Content.GetNode<Button>("Headline/Why").EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.Router.Current == new Screen(ScreenKind.Why, headline.EventId), "\"Why did this happen?\" opens the why chain");
            main.Router.Back();
            await Frames(2);
            Check(main.Current is DigestView, "Back returns to the digest");
        }
    }

    private async Task DioramaChecks(Main main)
    {
        var diorama = main.Diorama;
        var map = main.Session!.Map();
        Check(diorama.Block.Mesh is ArrayMesh { } block && block.GetSurfaceCount() == 2, "the block has a top and walls");
        Check(diorama.GetNodeOrNull<MeshInstance3D>("Water")?.Mesh is not null, "the sea is drawn");
        var settlements = map.Settlements.Count(s => s.Position is not null);
        Check(diorama.Markers.Count(m => m.Shape == MarkerShape.Settlement) == settlements
              && diorama.SettlementLayer.GetNode<MultiMeshInstance3D>("Roofs").Multimesh.InstanceCount >= 3 * settlements,
            "every settlement is a cluster of houses");
        Check(diorama.StructureLayer.GetChildren().Count(n => n.Name.ToString().StartsWith("structure")) == map.Structures.Count(s => s.Position is not null),
            "every landmark and labyrinth stands on the block");
        Check(diorama.StructureLayer.GetChildren().Count(n => n.Name.ToString().StartsWith("temple") || n.Name.ToString().StartsWith("tower"))
              == map.Settlements.Where(s => s.Position is not null).Sum(s => s.Marks?.Count ?? 0),
            "temples and mage towers stand beside their towns");
        Check(diorama.TreeCount > 100, $"woods are planted ({diorama.TreeCount} trees)");
        Check(map.Borders.Count == 0 || diorama.BorderLines.Mesh is ArrayMesh { } lines && lines.GetSurfaceCount() == 2, "realm borders are drawn, ink over a halo");

        diorama.Rig.Reset();
        await Frames(90);
        await Shot("diorama");
        Check(diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Eye)) < 0.01f, "the camera follows the rig");
        var target = diorama.Markers[0];
        var onScreen = diorama.ScreenOf(target.Position);
        Check(diorama.Pick(onScreen) is { } picked && (picked == target.Ref || diorama.Markers.Any(m => m.Ref == picked && diorama.ScreenOf(m.Position).DistanceTo(onScreen) < 1)),
            "clicking a marker picks it");
        Check(diorama.Pick(new Godot.Vector2(-500, -500)) is null, "clicking empty space picks nothing");

        var settlement = map.Settlements.First(s => s.Position is not null);
        diorama.Focus(settlement.Position!, Zoom.Town);
        await Frames(120);
        await Shot("diorama-town");
        Check(diorama.Rig.NearestZoom == Zoom.Town && diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Target)) < 1f, "focusing descends to the town");
    }

    /// <summary>With `-- --shots &lt;dir&gt;` (and a real display), saves what the screen shows, for visual review.</summary>
    private async Task Shot(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = System.Array.IndexOf(args, "--shots");
        if (at < 0 || at + 1 >= args.Length || DisplayServer.GetName() == "headless") return;
        await Frames(4);
        System.IO.Directory.CreateDirectory(args[at + 1]);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(args[at + 1], name + ".png"));
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) Fail(what);
    }

    private void Fail(string what)
    {
        _failures.Add(what);
        GD.PrintErr("FAIL: " + what);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Until(System.Func<bool> condition, string what, int maxFrames = 600)
    {
        for (var i = 0; i < maxFrames && !condition(); i++) await Frames(1);
        Check(condition(), what);
    }
}
```

Replace `sim/FantasyWorld.Client/Markers.cs` entirely with:
```csharp
using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

public enum MarkerShape { Settlement, Landmark, Labyrinth, Ruin, Fortress, Shrine, Tower, Temple }

/// <summary>One thing standing on the block: where, what shape, how big and in what colour.</summary>
public sealed record Marker(EntityRef Ref, string Name, MarkerShape Shape, Vector3 Position, float Size, Vector3 Colour, PinMarker Kind);

/// <summary>A numbered digest pin floating above its place.</summary>
public sealed record Pin(int Number, EntityRef Place, string Label, Vector3 Position, long EventId);

/// <summary>A straight piece of a realm border on the block's surface.</summary>
public sealed record Segment(Vector3 From, Vector3 To);

/// <summary>
/// Lays the map screen out on the diorama (sub-project 4 §3): settlement blocks sized by tier and coloured by their
/// holder (orange when occupied), diamonds for landmarks, nested diamonds for labyrinths, numbered pins above digest
/// places, and realm borders along the tile edges where two differently held regions meet. Fortresses, shrines, and
/// the temples and mage towers of today's towns stand beside them (sub-project 3 §9).
/// </summary>
public static class Markers
{
    public const float PinLift = 0.14f;

    public static float SizeOf(string tier) => tier switch
    {
        nameof(SettlementTier.City) => 0.045f,
        nameof(SettlementTier.Town) => 0.034f,
        nameof(SettlementTier.Village) => 0.024f,
        _ => 0.016f,
    };

    public static IReadOnlyList<Marker> For(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var colourOf = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Colour);
        var markers = new List<Marker>();
        foreach (var s in map.Settlements.Where(s => s.Position is not null))
        {
            var region = RegionAt(geography, s.Position!);
            var colour = s.Marker == PinMarker.Conflict ? Palette.Conflict
                : colourOf.TryGetValue(region, out var name) && name != "none" ? Palette.Heraldic(name) : Palette.Muted;
            var at = Projection.ToBlock(grid, s.Position!, 0);
            markers.Add(new Marker(s.Ref, s.Name, MarkerShape.Settlement, at, SizeOf(s.Tier), colour, s.Marker));
            // Temples and towers stand on the far side from structures, one behind the other.
            var k = 0;
            foreach (var mark in s.Marks ?? Array.Empty<string>())
            {
                var beside = at + new Vector3(-0.028f, 0, -0.012f + 0.024f * k++);
                beside = beside with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (beside.X + 1) / 2, (beside.Z + 1) / 2)) };
                markers.Add(mark == "mage tower"
                    ? new Marker(s.Ref, $"{s.Name} mage tower", MarkerShape.Tower, beside, 0.02f, Palette.For(Category.FaithAndMagic), PinMarker.Plain)
                    : new Marker(s.Ref, $"{s.Name} temple", MarkerShape.Temple, beside, 0.018f, Palette.Gold, PinMarker.Plain));
            }
        }
        foreach (var st in map.Structures.Where(st => st.Position is not null))
        {
            var shape = st.State == nameof(StructureState.Ruined) ? MarkerShape.Ruin
                : st.Kind switch
                {
                    nameof(StructureKind.Labyrinth) => MarkerShape.Labyrinth,
                    nameof(StructureKind.Fortification) => MarkerShape.Fortress,
                    nameof(StructureKind.Shrine) => MarkerShape.Shrine,
                    _ => MarkerShape.Landmark,
                };
            var colour = shape switch
            {
                MarkerShape.Ruin => Palette.Muted,
                MarkerShape.Labyrinth => Palette.For(Category.FaithAndMagic),
                MarkerShape.Shrine => Palette.Gold,
                _ => Palette.Ink,
            };
            // Structures stand beside their settlement, not inside it.
            var at = Projection.ToBlock(grid, st.Position!, 0) + new Vector3(0.03f, 0, 0.03f);
            markers.Add(new Marker(st.Ref, st.Name, shape, at with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (at.X + 1) / 2, (at.Z + 1) / 2)) },
                0.022f, colour, PinMarker.Plain));
        }
        return markers;
    }

    /// <summary>The digest's numbered pins, lifted above the block so they read from any angle.</summary>
    public static IReadOnlyList<Pin> Pins(DigestScreen digest, Geography geography) =>
        digest.Pins.Select(p => new Pin(p.Number, p.Place, p.Label, Projection.ToBlock(geography.Terrain, p.Position, PinLift), p.EventId)).ToList();

    /// <summary>Tile edges between land regions whose holders differ, slightly above the surface.</summary>
    public static IReadOnlyList<Segment> Borders(MapScreen map, Geography geography, float lift = 0.006f)
    {
        var grid = geography.Terrain;
        return BorderEdges(map, geography).Select(e => new Segment(Lift(grid, e.A.X / (double)grid.Width, e.A.Y / (double)grid.Height, lift),
            Lift(grid, e.B.X / (double)grid.Width, e.B.Y / (double)grid.Height, lift))).ToList();
    }

    /// <summary>
    /// The borders as smooth lines: tile edges chained into polylines, their corners rounded (Chaikin, twice) so they
    /// read as drawn lines rather than staircases, then laid on the surface.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Vector3>> BorderLines(MapScreen map, Geography geography, float lift = 0.004f)
    {
        var grid = geography.Terrain;
        var lines = new List<IReadOnlyList<Vector3>>();
        foreach (var chain in Chains(BorderEdges(map, geography)))
        {
            var points = chain.Select(p => new Vector2(p.X / (float)grid.Width, p.Y / (float)grid.Height)).ToList();
            var closed = chain.Count > 2 && chain[0] == chain[^1];
            // Drop the staircase first (keep only corners that matter at under a tile), then round what is left.
            points = Simplify(points, 0.75f / grid.Width);
            for (var k = 0; k < 3; k++) points = Chaikin(points, closed);
            lines.Add(points.Select(p => Lift(grid, p.X, p.Y, lift)).ToList());
        }
        return lines;
    }

    private static Vector3 Lift(TerrainGrid grid, double u, double v, float lift) =>
        new((float)(u * 2 - 1), Math.Max(0, BlockMesh.HeightAt(grid, u, v)) + lift, (float)(v * 2 - 1));

    /// <summary>Border edges between tile corners (integer lattice points).</summary>
    private static List<(TileCoord A, TileCoord B)> BorderEdges(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var owner = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Owner?.Id ?? 0);
        long Owner(int region) => owner.GetValueOrDefault(region);
        var edges = new List<(TileCoord, TileCoord)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var here = geography.RegionOf[grid.Index(x, y)];
                if (here == 0) continue;
                if (x + 1 < grid.Width && geography.RegionOf[grid.Index(x + 1, y)] is var right and not 0 && right != here && Owner(right) != Owner(here))
                    edges.Add((new TileCoord(x + 1, y), new TileCoord(x + 1, y + 1)));
                if (y + 1 < grid.Height && geography.RegionOf[grid.Index(x, y + 1)] is var down and not 0 && down != here && Owner(down) != Owner(here))
                    edges.Add((new TileCoord(x, y + 1), new TileCoord(x + 1, y + 1)));
            }
        return edges;
    }

    /// <summary>Chains edges into polylines: walks from every end or junction, then around any loops left over.</summary>
    public static List<List<TileCoord>> Chains(IEnumerable<(TileCoord A, TileCoord B)> edges)
    {
        var links = new Dictionary<TileCoord, List<TileCoord>>();
        foreach (var (a, b) in edges)
        {
            (links.TryGetValue(a, out var la) ? la : links[a] = new()).Add(b);
            (links.TryGetValue(b, out var lb) ? lb : links[b] = new()).Add(a);
        }
        var used = new HashSet<(TileCoord, TileCoord)>();
        bool Use(TileCoord a, TileCoord b) => used.Add((a, b)) & used.Add((b, a));
        var chains = new List<List<TileCoord>>();
        void Walk(TileCoord start, TileCoord next)
        {
            if (!Use(start, next)) return;
            var chain = new List<TileCoord> { start, next };
            var at = next;
            while (links[at].Count == 2 && links[at].FirstOrDefault(n => !used.Contains((at, n))) is var step && links[at].Any(n => !used.Contains((at, n))))
            {
                Use(at, step);
                chain.Add(step);
                at = step;
            }
            chains.Add(chain);
        }
        foreach (var node in links.Keys.Where(n => links[n].Count != 2).OrderBy(n => n.Y).ThenBy(n => n.X).ToList())
            foreach (var next in links[node].ToList()) Walk(node, next);
        foreach (var node in links.Keys.OrderBy(n => n.Y).ThenBy(n => n.X).ToList())
            foreach (var next in links[node].ToList()) Walk(node, next);
        return chains;
    }

    /// <summary>Douglas–Peucker: keeps the points that stray more than <paramref name="tolerance"/> from a straight run.</summary>
    internal static List<Vector2> Simplify(List<Vector2> points, float tolerance)
    {
        if (points.Count < 3) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var (best, far) = (-1, tolerance);
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i], points[a], points[b]);
                if (d > far) (best, far) = (i, d);
            }
            if (best < 0) continue;
            keep[best] = true;
            stack.Push((a, best));
            stack.Push((best, b));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + t * ab);
    }

    /// <summary>One round of corner cutting; open lines keep their ends.</summary>
    internal static List<Vector2> Chaikin(List<Vector2> points, bool closed)
    {
        if (points.Count < 3) return points;
        var result = new List<Vector2>(points.Count * 2);
        if (!closed) result.Add(points[0]);
        for (var i = 0; i < points.Count - 1; i++)
        {
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
        }
        if (closed) result.Add(result[0]);
        else result.Add(points[^1]);
        return result;
    }

    private static long RegionAt(Geography geography, MapPoint point)
    {
        var grid = geography.Terrain;
        var x = (int)Math.Clamp(point.X * grid.Width, 0, grid.Width - 1);
        var y = (int)Math.Clamp(point.Y * grid.Height, 0, grid.Height - 1);
        return geography.RegionOf[grid.Index(x, y)];
    }
}
```

Replace `sim/FantasyWorld.Client/Palette.cs` entirely with:
```csharp
using System.Numerics;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>The design's tokens (sub-project 4 §5) as linear-ish RGB in 0–1, plus colours for categories and realms.</summary>
public static class Palette
{
    public static readonly Vector3 Ground = BlockMesh.Hex(0xecebe7);
    public static readonly Vector3 Card = BlockMesh.Hex(0xf7f6f3);
    public static readonly Vector3 Border = BlockMesh.Hex(0xdcdad4);
    public static readonly Vector3 Rule = BlockMesh.Hex(0xe3e1db);
    public static readonly Vector3 Ink = BlockMesh.Hex(0x1a1a18);
    public static readonly Vector3 Body = BlockMesh.Hex(0x4a4842);
    public static readonly Vector3 Muted = BlockMesh.Hex(0x66645d);
    public static readonly Vector3 Conflict = BlockMesh.Hex(0xc4621f);
    public static readonly Vector3 Realm = BlockMesh.Hex(0x3b6fb6);

    /// <summary>Holy things: temples, shrines, saints.</summary>
    public static readonly Vector3 Gold = BlockMesh.Hex(0xc9a227);

    public const string DisplayFont = "Instrument Serif";
    public const string UiFont = "Geist";

    public static Vector3 For(Category category) => category switch
    {
        Category.War => Conflict,
        Category.Politics => Realm,
        Category.Economy => BlockMesh.Hex(0xb58a3c),
        Category.FaithAndMagic => BlockMesh.Hex(0x7b5ea7),
        Category.Structures => Ink,
        Category.Nature => BlockMesh.Hex(0x5e8a4a),
        Category.Chronicle => Muted,
        _ => Body,
    };

    private static readonly Dictionary<string, Vector3> Heraldry = new(StringComparer.Ordinal)
    {
        ["crimson"] = BlockMesh.Hex(0xa8323a), ["azure"] = Realm, ["gold"] = BlockMesh.Hex(0xc9a227), ["silver"] = BlockMesh.Hex(0x9aa0a6),
        ["sable"] = Ink, ["vert"] = BlockMesh.Hex(0x3f7d4e), ["purpure"] = BlockMesh.Hex(0x6d4c8f), ["orange"] = Conflict,
        ["white"] = Card, ["russet"] = BlockMesh.Hex(0x8b4a2b), ["grey"] = Muted,
    };

    /// <summary>A realm's heraldic colour name (e.g. "azure") as a colour; unknown names are muted.</summary>
    public static Vector3 Heraldic(string name) => Heraldry.TryGetValue(name, out var colour) ? colour : Muted;
}
```

Replace `sim/FantasyWorld.Sim/Systems/StructureSystem.cs` entirely with:
```csharp
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Structures (spec §5, minimal hooks for sub-project 3), yearly and last in the tick order. Wealthy lords commission
/// landmarks that take years to raise (paid from the settlement's wealth; completion brings renown). Labyrinths open
/// in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers. Abandoned settlements
/// leave ruins, and their unfinished or standing landmarks fall with them.
/// Great works (sub-project 3 §6): a landmark has an architect; war, plague or famine hold the work up; an impatient
/// patron rushes it, at the risk of collapse; on completion the patron gains renown and the realm stability. Towns that
/// have known siege raise fortresses, which strengthen their defence. A lord may restore a fallen landmark.
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

    public const double RushedCollapse = 0.08;
    public const double FortressPerYear = 0.03;
    public const double FortressWealth = 2500;
    public const int FortressDefence = 400;
    public const double RestorePerYear = 0.05;

    private static readonly string[] Works = { "Spire", "Great Hall", "Bridge", "Temple", "Library", "Colossus", "Lighthouse", "Garden" };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "structures";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        Ruin(ctx);
        Build(ctx);
        Commission(ctx);
        Fortify(ctx);
        Restore(ctx);
        OpenLabyrinths(ctx);
    }

    /// <summary>A standing fortress guards the town.</summary>
    public static bool Fortified(World world, Settlement s) =>
        world.Structures.Values.Any(x => x.Type == StructureKind.Fortification && x.State == StructureState.Standing && x.SettlementId == s.Id);

    /// <summary>The town's best designer: a grown notable, not its lord, by lore and stewardship.</summary>
    public static Character? Architect(World world, Settlement seat, Core.WorldDate today) =>
        world.ActiveCharacters.Where(c => c.SettlementId == seat.Id && c.Id != seat.LordId && c.Stage(today) != LifeStage.Child)
            .OrderByDescending(c => c.Attributes.Lore + c.Attributes.Stewardship).ThenBy(c => c.Id).FirstOrDefault();

    public static int LabyrinthCap(World world) => Math.Max(1, world.Regions.Count / 15);

    private void Commission(SimContext ctx)
    {
        var world = ctx.World;
        var busy = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= LandmarkWealth
                                                                 && !busy.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            if (!ctx.Rng.Chance(CommissionPerYear)) continue;
            var fallback = $"The {Works[ctx.Rng.NextInt(0, Works.Length)]} of {seat.Name}";
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("landmark", $"{Structure.Kind}#{id}",
                $"A landmark commissioned by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}.", fallback));
            seat.Wealth *= 0.8;
            var architect = Architect(world, seat, ctx.Date);
            var actors = new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) };
            if (architect is not null) actors["architect"] = architect.Ref;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun", Actors: actors,
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = architect?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
                Rushed = lord.Has(Traits.Ambitious) || lord.Has(Traits.Wrathful),
            };
        }
    }

    /// <summary>What holds a work up this year: war at the town, plague or famine in its land (the event that began it).</summary>
    private static long Hindrance(SimContext ctx, Settlement seat)
    {
        var world = ctx.World;
        var region = world.Regions[seat.RegionId];
        if (region.OccupierRealmId != 0 || region.BesiegerArmyId != 0)
            return ctx.Log.Involving(region.Ref).Concat(ctx.Log.Involving(seat.Ref))
                .Where(e => e.Type is "RegionOccupied" or "SiegeBegan" or "SiegeEnded").Select(e => e.Id).DefaultIfEmpty().Max();
        var plague = region.Conditions.GetValueOrDefault(Conditions.Plague);
        return plague != 0 ? plague : region.Conditions.GetValueOrDefault(Conditions.Shortage);
    }

    private void Fortify(SimContext ctx)
    {
        var world = ctx.World;
        var guarded = world.Structures.Values.Where(s => s.Type == StructureKind.Fortification && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= FortressWealth
                                                                 && !guarded.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            var region = world.Regions[seat.RegionId];
            var siege = ctx.Log.Involving(seat.Ref).Concat(ctx.Log.Involving(region.Ref))
                .Where(e => e.Type is "SiegeEnded" or "RegionOccupied").OrderBy(e => e.Id).LastOrDefault();
            if (siege is null || !ctx.Rng.Chance(FortressPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("fortress", $"{Structure.Kind}#{id}",
                $"A fortress raised by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}, after the town knew war.", $"The Walls of {seat.Name}"));
            seat.Wealth *= 0.8;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("FortificationBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: new[] { new Cause(siege.Id, CauseKind.Motive, 0.8) }, Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures", "War" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = Architect(world, seat, ctx.Date)?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Restore(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s is { Type: StructureKind.Landmark or StructureKind.Fortification, State: StructureState.Ruined }).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned || seat.Wealth < LandmarkWealth || !world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive) continue;
            if (!ctx.Rng.Chance(RestorePerYear)) continue;
            var fell = ctx.Log.Involving(work.Ref).LastOrDefault(e => e.Type is "WorksCollapsed" or "StructureRuined");
            seat.Wealth *= 0.85;
            work.State = StructureState.Building;
            work.Progress = 50;
            work.DelayedByEventId = 0;
            ctx.Emit(new EventDraft("RestorationBegun", Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = work.Ref },
                Place: seat.Ref, Causes: fell is null ? null : new[] { new Cause(fell.Id, CauseKind.Motive, 0.7) },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned) continue; // its ruin comes with the town's (Ruin)
            var hindrance = Hindrance(ctx, seat);
            if (hindrance != 0)
            {
                if (work.DelayedByEventId != hindrance)
                {
                    work.DelayedByEventId = hindrance;
                    ctx.Emit(new EventDraft("WorksDelayed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref,
                        Causes: new[] { new Cause(hindrance, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Structures" }));
                }
                continue;
            }
            work.DelayedByEventId = 0;
            var step = ctx.Rng.NextInt(7, 17) * (work.Rushed ? 1.6 : 1);
            work.Progress = Math.Min(100, work.Progress + step);
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Rushed && work.Progress < 100 && ctx.Rng.Chance(RushedCollapse))
            {
                Collapse(ctx, work, seat);
                continue;
            }
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var architect = world.Characters.GetValueOrDefault(work.ArchitectId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            if (architect is not null) actors["architect"] = architect.Ref;
            var fortress = work.Type == StructureKind.Fortification;
            var done = ctx.Emit(new EventDraft(fortress ? "FortificationCompleted" : "LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: fortress ? new[] { "Structures", "War" } : new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
            if (architect is { IsAlive: true }) architect.Renown = Math.Min(100, architect.Renown + 8);
            if (Realms.SovereignOf(world, world.Regions[seat.RegionId]) is { } realm) realm.Stability = Math.Min(100, realm.Stability + 5);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        // A land whose labyrinth was cleared does not open another.
        var haunted = labyrinths.Select(s => s.RegionId).Concat(world.Structures.Values.Where(s => s.GuardianId != 0).Select(s => s.RegionId)).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            var guardianId = world.Ids.Take(Monster.Kind);
            var levels = ctx.Rng.NextInt(3, 8);
            var breed = Monster.GuardianBreeds[ctx.Rng.NextInt(0, Monster.GuardianBreeds.Count)];
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id), ["guardian"] = new(Monster.Kind, guardianId) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
                Levels = levels, GuardianId = guardianId,
            };
            var deeps = invention.Name.StartsWith("The ", StringComparison.Ordinal) ? "the " + invention.Name[4..] : invention.Name;
            world.Monsters[guardianId] = new Monster
            {
                Id = guardianId, Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {deeps}", Breed = breed, RegionId = region.Id,
                Might = Math.Clamp(30 + 8 * levels + ctx.Rng.NextInt(0, 11), 1, 100), BornDay = ctx.Date.Day, State = MonsterState.Lairing,
                RoseEventId = opened.Id, LairStructureId = id,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world))
            {
                var relic = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id, fame: 20);
                world.Artifacts[relic.Id] = relic;
                Fates.Lose(relic, region.Id, opened.Id, 0);
            }
            return; // at most one a year
        }
    }

    private static void Ruin(SimContext ctx)
    {
        var world = ctx.World;
        var ruined = world.Structures.Values.Where(s => s.Type == StructureKind.Ruin).Select(s => s.SettlementId).ToHashSet();
        foreach (var town in world.Settlements.Values.Where(s => s.Abandoned && !ruined.Contains(s.Id)).ToList())
        {
            var abandonment = ctx.Log.Involving(town.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
            var id = world.Ids.Take(Structure.Kind);
            var fell = ctx.Emit(new EventDraft("StructureRuined",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: town.Ref, Payload: new Dictionary<string, string> { ["name"] = $"The Ruins of {town.Name}" },
                Causes: abandonment is null ? null : new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) },
                Importance: 25, Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = $"The Ruins of {town.Name}", Type = StructureKind.Ruin, RegionId = town.RegionId, SettlementId = town.Id,
                StartedEventId = fell.Id, CompletedEventId = fell.Id, Progress = 100, State = StructureState.Ruined,
            };
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type is StructureKind.Landmark or StructureKind.Fortification && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }

    /// <summary>A rushed work falls: people die, half the work is lost, and the architect is blamed.</summary>
    private static void Collapse(SimContext ctx, Structure work, Settlement seat)
    {
        var before = seat.Population;
        Demography.Kill(seat, 0.005, ctx.Rng);
        work.Progress = Math.Max(0, work.Progress - 50);
        var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
        if (ctx.World.Characters.TryGetValue(work.ArchitectId, out var architect))
        {
            actors["architect"] = architect.Ref;
            architect.Renown = Math.Max(0, architect.Renown - 10);
        }
        var builder = ctx.World.Characters.GetValueOrDefault(work.BuilderId);
        ctx.Emit(new EventDraft("WorksCollapsed", Actors: actors, Place: seat.Ref,
            Payload: new Dictionary<string, string>
            {
                ["dead"] = (before - seat.Population).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ruined"] = work.Progress <= 0 ? "yes" : "no",
            },
            Causes: new[] { new Cause(work.StartedEventId, CauseKind.Enabler, 0.7) },
            Importance: builder is null ? 40 : Fates.Importance(builder, 40), Tags: new[] { "Structures" }));
        if (work.Progress <= 0) work.State = StructureState.Ruined;
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/CharacterScreen.cs` entirely with:
```csharp
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum Tone { Warm, Neutral, Hostile }

public sealed record Place(EntityRef Ref, string Label, MapPoint? Position);

public sealed record TimelineRow(int Year, LinkedText Text, long EventId, bool Highlight);

public sealed record Tie(EntityRef Ref, string Name, string Relation, string Qualifier, Tone Tone);

/// <summary>
/// The *Character page* (sub-project 2 §3.3): who they are, their traits and ambition, where they are, the timeline of
/// their life (minor events folded) and their ties — family, liege, and the people they love or hate most.
/// </summary>
public sealed record CharacterScreen(
    EntityRef Ref, string Name, EntityRef? House, string Kicker, string Line, IReadOnlyList<string> Traits, string Ambition,
    Place? Location, IReadOnlyList<TimelineRow> Timeline, int Folded, int Earlier, IReadOnlyList<Tie> Ties, bool Followed,
    Mention? Faith = null, bool Saint = false)
{
    public const int TimelineImportance = 20;

    /// <summary>A long life shows its most recent rows; the rest are counted in <c>Earlier</c>.</summary>
    public const int MaxTimeline = 150;

    public static CharacterScreen Build(World world, EventLog log, long characterId, bool followed = false)
    {
        var c = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var nameOf = Names.Of(world);
        var narrator = new TemplateNarrator(world);
        var house = world.Houses.GetValueOrDefault(c.HouseId);
        var life = log.Involving(c.Ref);
        var born = life.FirstOrDefault(e => e.Type == "CharacterBorn" && e.Actors.GetValueOrDefault("child") == c.Ref);
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);

        var line = new List<string> { (c.SaintEventId != 0 ? "Saint · " : "") + RoleLine(world, c) };
        var today = c.Died ?? world.Date;
        line.Add($"age {c.Age(today)}");
        var birthYear = new Core.WorldDate(c.BirthDay).Year;
        line.Add(born?.Place is { } birthplace && nameOf(birthplace) is { } bp
            ? $"born Year {birthYear} in {bp}"
            : birthYear < 1 ? "born before recorded history" : $"born Year {birthYear}");
        if (c.Died is { } died) line.Add($"died Year {died.Year}{(c.DeathCause is { } cause ? $" of {cause}" : "")}");

        var notable = life.Where(e => e.Importance >= TimelineImportance).ToList();
        var highlight = c.IsAlive
            ? notable.Where(e => world.Date.Day - e.Date.Day < Core.WorldDate.DaysPerYear).OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault()
            : notable.OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault();
        var earlier = Math.Max(0, notable.Count - MaxTimeline);
        var timeline = notable.Skip(earlier).Select(e => new TimelineRow(e.Date.Year,
            LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf), e.Id, e.Id == highlight?.Id)).ToList();

        Place? location = home is null ? null
            : new Place(home.Ref, c.IsActive ? $"Now at {home.Name}" : $"Last at {home.Name}", MapPoint.Of(world, home.Ref));
        return new CharacterScreen(c.Ref, Names.Full(world, c), house?.Ref, house is null ? "Character" : $"Character · House {house.Name}",
            string.Join(" · ", line), c.Traits.ToList(), AmbitionText(world, c), location, timeline, life.Count - notable.Count, earlier,
            TiesOf(world, c), followed,
            world.Faiths.TryGetValue(c.FaithId, out var faith) ? new Mention(faith.Ref, faith.Name) : null, c.SaintEventId != 0);
    }

    private static string RoleLine(World world, Character c)
    {
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);
        var realm = home is null ? null : Realms.SovereignOf(world, home);
        var role = c.Role switch
        {
            Role.Lord when home is not null => $"Lord of {home.Name}",
            Role.Knight when realm is not null => $"Knight of {realm.Name}",
            Role.None => home is null ? "Commoner" : $"Of {home.Name}",
            _ => home is null ? c.Role.ToString() : $"{c.Role} of {home.Name}",
        };
        return c.IsAlive || c.Role == Role.None ? role : "Once " + char.ToLowerInvariant(role[0]) + role[1..];
    }

    private static string AmbitionText(World world, Character c) => c.Ambition switch
    {
        Ambitions.Renown => "Ambition: renown",
        Ambitions.Artifact => "Ambition: a great artifact",
        Ambitions.Magic => "Ambition: mastery of magic",
        Ambitions.Wealth => "Ambition: wealth",
        Ambitions.Vengeance when world.Characters.TryGetValue(c.AmbitionTarget, out var foe) => $"Ambition: vengeance on {Names.Full(world, foe)}",
        Ambitions.Vengeance => "Ambition: vengeance",
        _ => "Ambition: a family of their own",
    };

    private static List<Tie> TiesOf(World world, Character c)
    {
        var ties = new List<Tie>();
        void Add(Character? other, string relation, string qualifier, Tone tone)
        {
            if (other is null || other.Id == c.Id || ties.Any(t => t.Ref == other.Ref)) return;
            var q = other.Died is { } d ? (qualifier.Length > 0 ? $"{qualifier}, died Year {d.Year}" : $"died Year {d.Year}") : qualifier;
            ties.Add(new Tie(other.Ref, Names.Full(world, other), relation, q, tone));
        }
        Tone ToneOf(Character other) => Relations.Opinion(c, other) is var o && o >= Relations.FriendAt ? Tone.Warm : o <= Relations.RivalAt ? Tone.Hostile : Tone.Neutral;
        string Feeling(Character other) => ToneOf(other) switch { Tone.Warm => "loyal", Tone.Hostile => "resentful", _ => "" };

        // Only the living have a liege today; the dead's lords are in their timeline.
        if (c.IsActive && world.Settlements.TryGetValue(c.SettlementId, out var home) && Realms.SovereignOf(world, home) is { } realm
            && Realms.RulerOf(world, realm) is { } liege)
            Add(liege, "Liege", Feeling(liege), ToneOf(liege));
        Add(world.Characters.GetValueOrDefault(c.SpouseId), "Spouse", "", Tone.Warm);
        Add(world.Characters.GetValueOrDefault(c.FatherId), "Father", "", Tone.Neutral);
        Add(world.Characters.GetValueOrDefault(c.MotherId), "Mother", "", Tone.Neutral);
        foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>())
            Add(child, "Child", "", Tone.Neutral);
        foreach (var other in c.Memories.Select(m => m.About).Distinct().Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()
                     .Select(o => (Other: o, Opinion: Relations.Opinion(c, o)))
                     .Where(x => x.Opinion >= Relations.FriendAt || x.Opinion <= Relations.RivalAt)
                     .OrderByDescending(x => Math.Abs(x.Opinion)).ThenBy(x => x.Other.Id).Take(3))
            Add(other.Other, other.Opinion > 0 ? "Friend" : "Rival", "", other.Opinion > 0 ? Tone.Warm : Tone.Hostile);
        return ties;
    }
}

public sealed record FamilyNode(EntityRef Ref, string Name, int Born, int? Died, EntityRef? House);

public sealed record FamilyEdge(EntityRef From, EntityRef To, string Kind);

/// <summary>A character's family over a few generations up and down: nodes, parent and spouse edges.</summary>
public sealed record FamilyTree(EntityRef Root, IReadOnlyList<FamilyNode> Nodes, IReadOnlyList<FamilyEdge> Edges)
{
    public static FamilyTree Build(World world, long characterId, int generations = 2)
    {
        var root = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var people = new SortedDictionary<long, Character>();
        void Up(Character c, int depth)
        {
            people[c.Id] = c;
            if (depth == 0) return;
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Up(parent, depth - 1);
        }
        void Down(Character c, int depth)
        {
            people[c.Id] = c;
            if (world.Characters.TryGetValue(c.SpouseId, out var spouse)) people[spouse.Id] = spouse;
            if (depth == 0) return;
            foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Down(child, depth - 1);
        }
        Up(root, generations);
        Down(root, generations);
        var nodes = people.Values.Select(c => new FamilyNode(c.Ref, Names.Full(world, c), new Core.WorldDate(c.BirthDay).Year, c.Died?.Year,
            world.Houses.TryGetValue(c.HouseId, out var h) ? h.Ref : null)).ToList();
        var edges = new List<FamilyEdge>();
        foreach (var c in people.Values)
        {
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Where(people.ContainsKey))
                edges.Add(new FamilyEdge(people[parent].Ref, c.Ref, "parent"));
            if (people.TryGetValue(c.SpouseId, out var spouse) && c.Id < spouse.Id) edges.Add(new FamilyEdge(c.Ref, spouse.Ref, "spouse"));
        }
        return new FamilyTree(root.Ref, nodes, edges);
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/ChronicleScreens.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public sealed record ChronicleRow(long EventId, Category Category, int Importance, LinkedText Text);

public sealed record ChronicleSeason(string Label, IReadOnlyList<ChronicleRow> Rows);

/// <summary>The "Full chronicle" view (sub-project 2 §3.5): every event in a span, newest season first, paged.</summary>
public sealed record ChronicleScreen(int FromYear, int ToYear, int Total, int Page, int Pages, IReadOnlyList<ChronicleSeason> Seasons)
{
    public const int PageSize = 50;

    public static ChronicleScreen Build(World world, EventLog log, int? fromYear = null, int? toYear = null,
        Category? only = null, int minImportance = 0, int page = 1)
    {
        var to = toYear ?? world.Date.Year;
        var from = fromYear ?? Math.Max(1, to - 9);
        var narrator = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        var events = log.All.Where(e => e.Date.Year >= from && e.Date.Year <= to && e.Importance >= minImportance
                                        && (only is null || Categories.Of(e) == only)).Reverse().ToList();
        var pages = Math.Max(1, (events.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        var seasons = events.Skip((page - 1) * PageSize).Take(PageSize)
            .GroupBy(e => Digest.SeasonLabel(e.Date))
            .Select(g => new ChronicleSeason(g.Key, g.Select(e => new ChronicleRow(e.Id, Categories.Of(e), e.Importance,
                LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf))).ToList()))
            .ToList();
        return new ChronicleScreen(from, to, events.Count, page, pages, seasons);
    }
}

public sealed record CouncilSeat(string Seat, Mention Member);

public sealed record TreatyRow(string Kind, Mention With, int SinceYear);

public sealed record WarRow(EntityRef War, string Name, string Side, bool Ended);

public sealed record EraRow(int Year, string Name, string Phase, long EventId);

/// <summary>A realm's page (sub-project 2 §3.5): who rules and by what law, its council, vassals, treaties, wars and eras.</summary>
public sealed record RealmScreen(
    EntityRef Ref, string Title, string Rank, string Government, string SuccessionLaw, string MagicLaw, Mention? Ruler, Place Capital,
    int Legitimacy, int Stability, int Treasury, string Phase, int Regions, Mention? Liege, IReadOnlyList<Mention> Vassals,
    IReadOnlyList<CouncilSeat> Council, IReadOnlyList<TreatyRow> Treaties, IReadOnlyList<WarRow> Wars, IReadOnlyList<EraRow> Eras, Mention? Faith = null)
{
    public static RealmScreen Build(World world, EventLog log, long realmId)
    {
        var realm = world.Realms.TryGetValue(realmId, out var found) ? found : throw new KeyNotFoundException($"No realm #{realmId}.");
        Mention Of(EntityRef r) => new(r, Names.Of(world)(r) ?? r.ToString());
        var capital = world.Settlements[realm.CapitalId];
        return new RealmScreen(realm.Ref, Statecraft.Title(realm), realm.Rank.ToString(), Words(realm.Government.ToString()),
            realm.SuccessionLaw.ToString(), realm.MagicLaw.ToString(),
            Realms.RulerOf(world, realm) is { } ruler ? Of(ruler.Ref) : null,
            new Place(capital.Ref, capital.Name, MapPoint.Of(world, capital.Ref)),
            (int)Math.Round(realm.Legitimacy), (int)Math.Round(realm.Stability), (int)Math.Round(realm.Treasury), realm.Phase.ToString(),
            Realms.Domain(world, realm).Count(),
            realm.LiegeId != 0 && world.Realms.TryGetValue(realm.LiegeId, out var liege) ? Of(liege.Ref) : null,
            Realms.VassalsOf(world, realm).Select(v => Of(v.Ref)).ToList(),
            realm.Council.Where(kv => world.Characters.ContainsKey(kv.Value)).Select(kv => new CouncilSeat(kv.Key, Of(world.Characters[kv.Value].Ref))).ToList(),
            realm.Treaties.Where(t => world.Realms.ContainsKey(t.With)).Select(t => new TreatyRow(t.Kind.ToString(), Of(world.Realms[t.With].Ref),
                log.TryGet(t.SinceEventId, out var since) ? since.Date.Year : 1)).ToList(),
            world.Wars.Values.Where(w => w.Involves(realm.Id)).OrderByDescending(w => w.Id)
                .Select(w => new WarRow(w.Ref, w.Name, w.IsAttacker(realm.Id) ? "attacker" : "defender", w.Ended)).ToList(),
            new HistoryQuery(log).Eras(realm.Ref).Select(e => new EraRow(e.Date.Year, e.Payload["era"], e.Payload.GetValueOrDefault("phase") ?? "", e.Id)).ToList(),
            world.Faiths.TryGetValue(realm.FaithId, out var faith) ? Of(faith.Ref) : null);
    }

    private static string Words(string pascal) => string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString()));
}

public sealed record ProvenanceRow(int Year, string How, Mention? From, Mention? To, long EventId);

/// <summary>An artifact's page (sub-project 2 §3.5): powers, a revealed curse, lore, where it is, how it was made and every change of hands.</summary>
public sealed record ArtifactScreen(
    EntityRef Ref, string Name, string Kind, string Material, int Fame, IReadOnlyList<string> Powers, string? Curse, string Lore,
    string Whereabouts, Place? Location, LinkedText Made, IReadOnlyList<ProvenanceRow> Provenance)
{
    public static ArtifactScreen Build(World world, EventLog log, long artifactId)
    {
        var x = world.Artifacts.TryGetValue(artifactId, out var found) ? found : throw new KeyNotFoundException($"No artifact #{artifactId}.");
        var nameOf = Names.Of(world);
        Mention? Of(long characterId) => world.Characters.TryGetValue(characterId, out var c) ? new Mention(c.Ref, Names.Full(world, c)) : null;
        var (whereabouts, location) = x.Place switch
        {
            ArtifactPlace.Held when world.Characters.TryGetValue(x.HolderId, out var holder) =>
                ($"Held by {Names.Full(world, holder)}", world.Settlements.TryGetValue(holder.SettlementId, out var s) ? new Place(s.Ref, s.Name, MapPoint.Of(world, s.Ref)) : null),
            ArtifactPlace.Treasury => ($"In the treasury of {world.Settlements[x.SettlementId].Name}",
                new Place(world.Settlements[x.SettlementId].Ref, world.Settlements[x.SettlementId].Name, MapPoint.Of(world, world.Settlements[x.SettlementId].Ref))),
            ArtifactPlace.Lost => ($"Lost in {world.Regions[x.RegionId].Name}",
                new Place(world.Regions[x.RegionId].Ref, world.Regions[x.RegionId].Name, MapPoint.Of(world, world.Regions[x.RegionId].Ref))),
            _ => ("Destroyed", (Place?)null),
        };
        var made = x.OriginEventId != 0 && log.TryGet(x.OriginEventId, out var origin)
            ? LinkedText.Link($"Year {origin.Date.Year}: {new TemplateNarrator(world).Sentence(origin)}", LinkedText.Mentions(origin), nameOf)
            : LinkedText.Plain("Made before recorded history.");
        return new ArtifactScreen(x.Ref, x.Name, x.Type.ToString(), x.Material, (int)Math.Round(x.Fame),
            x.Powers.Select(p => p.Kind == "attribute" ? $"+{p.Amount} {p.Attribute}" : p.Kind).ToList(),
            x.CurseRevealed ? x.Curse : null, x.Lore, whereabouts, location, made,
            x.Provenance.Select(p => new ProvenanceRow(log.TryGet(p.EventId, out var e) ? e.Date.Year : 0, p.How, Of(p.From), Of(p.To), p.EventId)).ToList());
    }
}
```

Replace `sim/FantasyWorld.Sim/Views/MapScreen.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum PinMarker { Plain, Realm, RecentEvent, Conflict }

public sealed record MapRegion(EntityRef Ref, string Name, string Biome, MapPoint? Center, EntityRef? Owner, EntityRef? Occupier, string Colour);

public sealed record MapCard(string Kicker, string Title, string Line);

/// <summary>A settlement pin; Marks are the buildings worth drawing beside it today ("temple", "mage tower").</summary>
public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card,
    IReadOnlyList<string>? Marks = null);

/// <summary>A structure as it was at the date; Detail says how deep a labyrinth has been delved ("level 2 of 5").</summary>
public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position, string? Detail = null);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// A host in the field: who leads it and who rides with it, its strength, what it is doing, and the road it means to
/// take (its region, then each region on its path, ending at its target).
/// </summary>
public sealed record MapArmy(
    EntityRef Ref, string Name, EntityRef Realm, string Colour, EntityRef War, string Side, Mention? Commander, IReadOnlyList<Mention> Knights,
    int Soldiers, string Objective, bool Besieging, MapPoint Position, IReadOnlyList<MapPoint> Route);

/// <summary>A trade road on the map: its markets, the way it runs (market, regions between, market) and its state.</summary>
public sealed record MapRoute(EntityRef Ref, string Name, EntityRef From, EntityRef To, string State, int Volume, IReadOnlyList<MapPoint> Path);

/// <summary>A beast in its lair: what it is, how mighty, whether it is a legend or asleep, and the event of its rising.</summary>
public sealed record MapMonster(EntityRef Ref, string Name, string Breed, int Might, bool Legendary, bool Slumbering, MapPoint Position, long RoseEventId,
    bool Guarding = false);

/// <summary>A battle of the last year: where, who won, and the event to explain it.</summary>
public sealed record MapBattle(long EventId, int Year, MapPoint Position, EntityRef Victor, EntityRef Vanquished, EntityRef War);

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend,
    IReadOnlyList<MapArmy>? Armies = null, IReadOnlyList<MapBattle>? Battles = null, IReadOnlyList<MapRoute>? Routes = null,
    IReadOnlyList<MapMonster>? Monsters = null)
{
    /// <summary>Battles shown on the map: those of the last year.</summary>
    public const int BattleDays = 360;
    public static MapScreen Build(World world, EventLog log, WorldDate? asOf = null)
    {
        var requested = asOf is { } d && d < world.Date ? d : world.Date;
        var map = History.PoliticalMapAt(world, log, requested);
        var date = map.Historical ? requested : world.Date;
        var today = date == world.Date;
        var political = map.Regions;
        EntityRef? RealmRef(long id) => id != 0 && world.Realms.TryGetValue(id, out var r) ? r.Ref : null;
        string Colour(long id) => id != 0 && world.Realms.TryGetValue(map.TopOf(id), out var top) ? top.Colors[0] : "none";

        var regions = world.Regions.Values.Select(r => new MapRegion(r.Ref, r.Name, r.Biome.ToString(), MapPoint.Of(world, r.Ref),
            RealmRef(political[r.Id].OwnerRealmId), RealmRef(political[r.Id].OccupierRealmId), Colour(political[r.Id].OwnerRealmId))).ToList();

        var abandoned = log.All.Where(e => e.Type == "SettlementAbandoned" && e.Date <= date && e.Place is { Kind: Settlement.Kind })
            .Select(e => e.Place!.Value.Id).ToHashSet();
        var season = date.Day - 90;
        var settlements = new List<MapSettlement>();
        foreach (var s in world.Settlements.Values.Where(s => !abandoned.Contains(s.Id)))
        {
            var state = political[s.RegionId];
            var here = log.Involving(s.Ref).Where(e => e.Date <= date).ToList();
            var marker = state.OccupierRealmId != 0 ? PinMarker.Conflict
                : here.Any(e => e.Date.Day > season && e.Importance >= 30) ? PinMarker.RecentEvent
                : state.OwnerRealmId != 0 ? PinMarker.Realm : PinMarker.Plain;
            var holder = world.Realms.GetValueOrDefault(state.OwnerRealmId);
            var ceded = log.Involving(world.Regions[s.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded" && e.Date <= date);
            var title = holder is null ? "Held by no realm"
                : $"Held by the {Statecraft.Title(holder)} {(ceded is null ? "since the founding" : $"since Year {ceded.Date.Year}")}";
            if (state.OccupierRealmId != 0 && world.Realms.TryGetValue(state.OccupierRealmId, out var occupier))
                title += $", occupied by {occupier.Name}";
            var thisYear = here.Count(e => e.Date.Year == date.Year);
            var line = (today ? $"Pop. {s.Population.ToString("N0", CultureInfo.InvariantCulture)} · " : "")
                       + $"{thisYear} event{(thisYear == 1 ? "" : "s")} this year";
            settlements.Add(new MapSettlement(s.Ref, s.Name, s.Tier.ToString(), MapPoint.Of(world, s.Ref), holder?.Ref, marker,
                new MapCard($"{s.Name} · {s.Tier}", title, line), today ? Marked.Where(s.Buildings.Contains).ToList() : Array.Empty<string>()));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => Cleared(log, st, date)
                ? new MapStructure(st.Ref, st.Name, st.Type.ToString(), st.State.ToString(), MapPoint.Of(world, st.Ref), "cleared")
                : st.GuardianId != 0 || st.Type == StructureKind.Labyrinth
                    ? new MapStructure(st.Ref, st.Name, nameof(StructureKind.Labyrinth), nameof(Model.StructureState.Standing), MapPoint.Of(world, st.Ref),
                        today && st.Levels > 0 ? $"level {st.Depth} of {st.Levels}" : null)
                    : new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
            .ToList();

        var borders = world.Geography is { } geo
            ? geo.Graph.Links.Where(l => political[l.A].OwnerRealmId != political[l.B].OwnerRealmId)
                .Select(l => new Border(world.Regions[Math.Min(l.A, l.B)].Ref, world.Regions[Math.Max(l.A, l.B)].Ref)).Distinct().ToList()
            : new List<Border>();

        var conflicts = world.Wars.Values.Where(w => w.StartDay <= date.Day && (!w.Ended || log.TryGet(w.EndedEventId, out var end) && end.Date > date))
            .Select(w => (War: w, Sides: map.Wars.GetValueOrDefault(w.Id) ?? new WarSides(w.Attackers, w.Defenders)))
            .Select(x => new Conflict(x.War.Ref, x.War.Name, x.Sides.Attackers.Select(id => world.Realms[id].Ref).ToList(),
                x.Sides.Defenders.Select(id => world.Realms[id].Ref).ToList(),
                political.Values.Where(p => p.OccupyingWarId == x.War.Id).Select(p => world.Regions[p.RegionId].Ref).ToList()))
            .ToList();

        var independent = political.Values.Select(p => p.OwnerRealmId).Where(id => id != 0).Select(map.TopOf).Distinct()
            .Select(id => world.Realms[id]).OrderBy(r => r.Id).ToList();
        var legend = independent.Select(r => new LegendEntry(r.Name, r.Colors[0]))
            .Append(new LegendEntry("Active conflict", "conflict")).Append(new LegendEntry("Event this season", "event")).ToList();
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend, today ? ArmiesIn(world) : Array.Empty<MapArmy>(), BattlesBefore(world, log, date), RoutesAt(world, log, date, today), MonstersAt(world, log, date));
    }

    /// <summary>Buildings drawn beside their settlement.</summary>
    public static readonly IReadOnlyList<string> Marked = new[] { "temple", "mage tower" };

    /// <summary>A labyrinth cleared by the date has become a ruin or a shrine.</summary>
    private static bool Cleared(EventLog log, Structure st, WorldDate date) =>
        st.ClearedEventId != 0 && log.TryGet(st.ClearedEventId, out var cleared) && cleared.Date <= date;

    /// <summary>Hosts in the field today (armies are not kept in history, so past maps show none).</summary>
    private static IReadOnlyList<MapArmy> ArmiesIn(World world)
    {
        Mention? Of(long id) => world.Characters.TryGetValue(id, out var c) && c.IsActive ? new Mention(c.Ref, Names.Full(world, c)) : null;
        MapPoint? At(long region) => world.Regions.TryGetValue(region, out var r) ? MapPoint.Of(world, r.Ref) : null;
        var armies = new List<MapArmy>();
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded).OrderBy(a => a.Id))
        {
            if (At(army.RegionId) is not { } here || !world.Wars.TryGetValue(army.WarId, out var war) || !world.Realms.TryGetValue(army.RealmId, out var realm)) continue;
            var route = new List<MapPoint> { here };
            route.AddRange(army.Path.Select(At).OfType<MapPoint>());
            armies.Add(new MapArmy(army.Ref, army.Name, realm.Ref, realm.Colors[0], war.Ref, war.IsAttacker(realm.Id) ? "attacker" : "defender",
                Of(army.CommanderId), army.Knights.Select(Of).OfType<Mention>().ToList(), army.Soldiers, army.Objective.ToString().ToLowerInvariant(),
                world.Regions[army.RegionId].BesiegerArmyId == army.Id, here, route));
        }
        return armies;
    }

    /// <summary>Beasts that had risen by the date and were not yet slain then.</summary>
    private static IReadOnlyList<MapMonster> MonstersAt(World world, EventLog log, WorldDate date) =>
        world.Monsters.Values
            .Where(m => log.TryGet(m.RoseEventId, out var rose) && rose.Date <= date
                        && !(m.State == MonsterState.Slain && log.TryGet(m.SlainEventId, out var slain) && slain.Date <= date))
            .Select(m => (Beast: m, At: MapPoint.Of(world, world.Regions[m.RegionId].Ref)))
            .Where(x => x.At is not null)
            .Select(x => new MapMonster(x.Beast.Ref, x.Beast.Name, x.Beast.Breed, (int)Math.Round(x.Beast.Might), x.Beast.Legendary,
                x.Beast.State == MonsterState.Slumbering, x.At!, x.Beast.RoseEventId, x.Beast.State == MonsterState.Lairing))
            .ToList();

    /// <summary>Roads opened by the date; their state then is replayed from their severing and restoring.</summary>
    private static IReadOnlyList<MapRoute> RoutesAt(World world, EventLog log, WorldDate date, bool today)
    {
        var routes = new List<MapRoute>();
        foreach (var route in world.Routes.Values)
        {
            if (!log.TryGet(route.OpenedEventId, out var opened) || opened.Date > date) continue;
            var state = route.State;
            if (!today)
            {
                var last = log.Involving(route.Ref).LastOrDefault(e => e.Date <= date && e.Type is "RouteSevered" or "RouteRestored");
                state = last?.Type == "RouteSevered" ? RouteState.Severed : RouteState.Open;
                // A road closes for good when a market is abandoned or shrinks below a town.
                if (log.Involving(route.Ref).Any(e => e.Type == "RouteClosed" && e.Date <= date))
                    state = RouteState.Closed;
            }
            if (state == RouteState.Closed) continue;
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            var path = new List<MapPoint>();
            if (MapPoint.Of(world, from.Ref) is { } start) path.Add(start);
            path.AddRange(route.Regions.Skip(1).SkipLast(1).Select(r => MapPoint.Of(world, world.Regions[r].Ref)).OfType<MapPoint>());
            if (MapPoint.Of(world, to.Ref) is { } end) path.Add(end);
            if (path.Count < 2) continue;
            routes.Add(new MapRoute(route.Ref, route.Name, from.Ref, to.Ref, state.ToString().ToLowerInvariant(), (int)Math.Round(today ? route.Volume : 0), path));
        }
        return routes;
    }

    private static IReadOnlyList<MapBattle> BattlesBefore(World world, EventLog log, WorldDate date) =>
        log.All.Where(e => e.Type == "BattleFought" && e.Date <= date && e.Date.Day > date.Day - BattleDays && e.Place is { } p && MapPoint.Of(world, p) is not null)
            .Select(e => new MapBattle(e.Id, e.Date.Year, MapPoint.Of(world, e.Place!.Value)!, e.Actors["victor"], e.Actors["vanquished"], e.Actors["war"]))
            .ToList();

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type is not (StructureKind.Landmark or StructureKind.Fortification)) return st.State.ToString();
        if (RuinedBy(world, log, st, date)) return nameof(Model.StructureState.Ruined);
        // A great work's state at a date is its latest turn by then: completed, restored, or a collapse that ruined it.
        var turn = log.Involving(st.Ref).LastOrDefault(e => e.Date <= date
            && e.Type is "LandmarkCompleted" or "FortificationCompleted" or "RestorationBegun" or "WorksCollapsed");
        return turn?.Type switch
        {
            "LandmarkCompleted" or "FortificationCompleted" => nameof(Model.StructureState.Standing),
            "WorksCollapsed" when turn.Payload.GetValueOrDefault("ruined") == "yes" => nameof(Model.StructureState.Ruined),
            _ => nameof(Model.StructureState.Building),
        };
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~CharacterScreenTests|FullyQualifiedName~ChronicleScreenTests|FullyQualifiedName~MapScreenTests|FullyQualifiedName~MarkersTests"`
Expected: `Passed!  - Failed: 0, Passed: 17`. Then `dotnet test sim` → `Passed: 555` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (73)` and exit code 0.

Look: in the showcase's town view (`4-town.png`), mage towers stand as violet spires and temples under gilt roofs beside their towns.

- [x] **Step 6: Commit**

```bash
git add game sim
git commit -m "feat(game): show faiths, saints, temples, towers, fortresses and labyrinth depths

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Myth criteria, a two-century soak and the README

Spec §10. The why-chain criterion is extended: every monster raid leads back to a rising (or the surge or danger behind it), and every holy war cites what stirred it (zealous faiths now cite the rival faith's latest gain). A two-century soak on a medium world with invariants checked every tick must see beasts rise, raid and fall, roads open and close, lands convert, saints declared, surges, great works and labyrinths. The README gains a section on the world's myths.

**Files:**
- Modify: `README.md`, `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs`
- Test (create): `sim/FantasyWorld.Sim.Tests/Engine/MythHealthTests.cs`
- Test (modify): `sim/FantasyWorld.Sim.Tests/Engine/SuccessCriteriaTests.cs`

**Interfaces:**
- Consumes: Tasks 1–10.
- Produces: `SuccessCriteriaTests.Criterion3_MythsTraceBackToTheirCauses`; `MythHealthTests`; README section "Myths, magic and structures" with `docs/images/town.png`.

- [x] **Step 1: Write the failing tests**

`sim/FantasyWorld.Sim.Tests/Engine/MythHealthTests.cs`:
```csharp
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Sub-project 3 §10: two centuries of a medium world with every myth system running, invariants checked each tick.</summary>
public class MythHealthTests
{
    [Fact]
    public void TwoCenturies_GrowMyths_AndHoldTheirInvariants()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(200 * 360);
        var seen = sim.Log.All.Select(e => e.Type).ToHashSet();

        foreach (var type in new[] { "MonsterRose", "MonsterRaid", "MonsterSlain", "RouteOpened", "RouteSevered", "RegionConverted", "SaintDeclared",
                     "LeySurge", "LandmarkCompleted", "LabyrinthOpened", "LabyrinthDelved", "WarDeclared" })
            Assert.Contains(type, seen);
        Assert.All(world.Monsters.Values.Where(m => m.Alive), m => Assert.True(world.Regions.ContainsKey(m.RegionId)));
        Assert.All(world.Routes.Values.Where(r => r.State != RouteState.Closed),
            r => Assert.True(TradeSystem.IsMarket(world.Settlements[r.FromId]) && TradeSystem.IsMarket(world.Settlements[r.ToId])));
        var holy = sim.Log.All.Where(e => e.Type == "HolyWarProclaimed").ToList();
        Assert.NotEmpty(holy);
        Assert.All(holy, h => Assert.NotEmpty(h.Causes));
        Assert.All(world.Regions.Values, r => Assert.True(r.FaithId == 0 || world.Faiths.ContainsKey(r.FaithId)));
        Assert.All(world.Structures.Values.Where(s => s.GuardianId != 0), s => Assert.InRange(s.Depth, 0, s.Levels));
    }
}
```

Replace `sim/FantasyWorld.Sim.Tests/Engine/SuccessCriteriaTests.cs` entirely with:
```csharp
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>A medium world after its century of prehistory, shared by the success-criteria checks.</summary>
public sealed class PrehistoricWorld
{
    public PrehistoricWorld()
    {
        World = WorldGenerator.Generate(new WorldGenOptions(42, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        Sim = Prehistory.Run(World, Prehistory.DefaultYears);
    }

    public World World { get; }
    public Simulator Sim { get; }

    /// <summary>An independent deep copy (snapshots share entity objects, so round-trip through storage).</summary>
    public Simulator Fork()
    {
        var snapshot = Packed.Unpack<WorldSnapshot>(Packed.Pack(WorldSnapshot.Capture(World, Sim.Queue)))!;
        var world = snapshot.ToWorld();
        world.Geography = World.Geography;
        var log = new EventLog();
        foreach (var ev in Sim.Log.All) log.Restore(ev);
        return new Simulator(world, log, snapshot.ToQueue(), DefaultSystems.Create());
    }
}

/// <summary>Spec §11, success criteria for the sim core, checked on a real medium world (criteria 2 and 5 offline).</summary>
public class SuccessCriteriaTests(PrehistoricWorld fixture) : IClassFixture<PrehistoricWorld>
{
    private static int ChainLength(ChainNode node) => 1 + (node.Causes.Count == 0 ? 0 : node.Causes.Max(ChainLength));

    [Fact]
    public void Criterion1_ANewWorldHasACenturyOfHistory_AndHoldsItsInvariants()
    {
        Assert.Equal(Prehistory.DefaultYears + 1, fixture.World.Date.Year);
        Assert.Empty(Invariants.Check(fixture.World, fixture.Sim.Log));
        Assert.Contains(fixture.Sim.Log.All, e => e.Type == "WarDeclared");
        Assert.Contains(fixture.World.Structures.Values, s => s.Type == StructureKind.Landmark);
        Assert.Contains(fixture.World.Artifacts.Values, a => a.OriginEventId != 0);
    }

    [Fact]
    public void Criterion3_WarsTraceBackThroughLinkedCausesToRoots()
    {
        var query = new HistoryQuery(fixture.Sim.Log);
        var wars = fixture.Sim.Log.All.Where(e => e.Type == "WarDeclared").ToList();
        var deep = wars.Count(w => ChainLength(query.WhyChain(w.Id, 10)) >= 3);
        Assert.True(deep * 10 >= wars.Count * 9, $"{deep} of {wars.Count} wars have a why-chain of 3+ events");
        Assert.All(wars, w => Assert.NotEmpty(query.RootCauses(w.Id)));
    }

    [Fact]
    public void Criterion3_MythsTraceBackToTheirCauses()
    {
        // Sub-project 3 §10: raids lead back to the beast's rising, holy wars to what stirred the faithful.
        var query = new HistoryQuery(fixture.Sim.Log);
        var raids = fixture.Sim.Log.All.Where(e => e.Type == "MonsterRaid").ToList();
        Assert.NotEmpty(raids);
        Assert.All(raids, r => Assert.Contains(query.RootCauses(r.Id), root => root.Type is "MonsterRose" or "LeySurge" or "DangerRose"));
        // Holy wars are checked on a longer history in MythHealthTests (a century may have none).
        Assert.All(fixture.Sim.Log.All.Where(e => e.Type == "HolyWarProclaimed"), h => Assert.NotEmpty(h.Causes));
    }

    [Fact]
    public void Criterion2_EightHoursAwayGiveADigestOfHeadlines()
    {
        var sim = fixture.Fork();
        var (copy, history) = (sim.World, sim.Log);
        var seen = history.NextId - 1;
        sim.AdvanceDays(120);

        var digest = Digest.Build(history, copy, seen, new HashSet<EntityRef>());
        Assert.InRange(digest.Entries.Count(), 3, 8);
        Assert.StartsWith("From ", new TemplateNarrator(copy).Digest(digest));
    }

    [Fact]
    public void Compaction_KeepsEveryWarExplainable()
    {
        var sim = fixture.Fork();
        var (world, log) = (sim.World, sim.Log);
        var before = log.All.Where(e => e.Type == "WarDeclared").ToDictionary(e => e.Id, e => ChainLength(new HistoryQuery(log).WhyChain(e.Id, 10)));

        Assert.NotEmpty(Compactor.Compact(world, log, sim.Queue, 1, world.Date.Year - Compactor.AgeYears - 1).Forgotten);

        Assert.Empty(Invariants.Check(world, log));
        foreach (var (id, length) in before)
            Assert.Equal(length, ChainLength(new HistoryQuery(log).WhyChain(id, 10)));
    }

    [Fact]
    public void Armies_MeetEachOther_RatherThanTradePlacesForever()
    {
        // A defender that only chased lost regions and an invader that only took the next one used to swap places
        // monthly for decades without a battle, freeing and retaking the same two regions.
        // A real campaign may take and lose a region several times, but each swing is fought for.
        var log = fixture.Sim.Log.All;
        var battles = log.Where(e => e.Type == "BattleFought" && e.Actors.ContainsKey("war")).GroupBy(e => e.Actors["war"]).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(log.Where(e => e.Type == "RegionLiberated" && e.Actors.ContainsKey("war")).GroupBy(e => e.Actors["war"]),
            war => Assert.True(war.Count() <= 2 * battles.GetValueOrDefault(war.Key) + 4,
                $"{war.Key}: {war.Count()} liberations but {battles.GetValueOrDefault(war.Key)} battles"));
        Assert.Contains(log, e => e.Type == "BattleFought");
    }

    [Fact]
    public void Battles_AreFoughtByPeople_WhoCarryThemIntoTheirLives()
    {
        var log = fixture.Sim.Log;
        var battles = log.All.Where(e => e.Type == "BattleFought").ToList();
        Assert.Contains(battles, b => b.Actors.Keys.Any(k => k.Contains("knight")));
        var glory = log.All.Where(e => e.Type == "DistinguishedInBattle").ToList();
        Assert.NotEmpty(glory);
        Assert.All(glory, g => Assert.Equal("BattleFought", log.Get(g.Causes[0].EventId).Type));
        Assert.All(log.All.Where(e => e.Type == "WoundedInBattle"), w => Assert.Contains(w.Payload["wound"], new[] { "wounded", "maimed" }));
        // A knight named on a battle finds it in their own history.
        var knight = battles.SelectMany(b => b.Actors.Where(a => a.Key.Contains("knight")).Select(a => (a.Value, b.Id))).First();
        Assert.Contains(log.Involving(knight.Value), e => e.Id == knight.Id);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test sim`
Expected: FAILS: 1 failing.

- [x] **Step 3: Implement**

Replace `README.md` entirely with:
````markdown
# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `sim/FantasyWorld.Client/` — the game's engine-free logic (session, terrain, layout), tested with the rest
- `game/` — the Godot 4.3 diorama client
- `docs/superpowers/specs/` — design specs
- `docs/superpowers/plans/` — implementation plans

## The game

![The morning digest over the diorama](docs/images/digest.png)

The world is a floating diorama: drag to orbit, scroll to descend from the whole realm to a single town. On launch
the world catches up on the time you were away and opens on the *morning digest*: the year, what changed, the
headline and *why did this happen?* Every name is a link: people open their life and ties, places fly the camera
there, realms and artifacts open their pages, and any event opens the chain of causes that led to it. The *map*
adds layers, a history slider back to Year 1, and a card for every settlement. While the game is open the world
lives on (one world day every four real minutes) and announces what matters.

![A region seen from above, with realm borders and settlements](docs/images/region.png)

War is drawn like a strategy map: each host's banner (in its realm's colours) marches from region to region along
a broad campaign arrow toward its target, attackers in red and defenders in blue; fronts carry teeth facing the
attacker, occupied land is hatched, and crossed swords mark this year's battles. Click a banner for its commander,
a battle for why it was fought. Knights ride with their realm's host, and battles can wound, kill or make them famous.

![A host on the march: campaign arrow, banner, front and occupied land](docs/images/war.png)
![Why a war began: the chain of causes](docs/images/why.png)

Run it with [Godot 4.3 .NET](https://godotengine.org/download/archive/4.3-stable/) and the .NET 8 SDK:

```bash
dotnet build game/FantasyWorld.Game.csproj
godot --path game                                   # first launch creates a world with a century of prehistory
godot --path game -- --world worlds/aerenmoor.fw.db # or open a world made with the CLI
```

Keys: **Esc** back · **H** home (the digest) · **M** the map. Worlds live in Godot's user folder unless given.

Checks: `godot --headless --path game res://tests/HeadlessChecks.tscn` drives every screen on a fresh world and
exits non-zero on failure (CI runs it). `godot --path game res://tests/Showcase.tscn -- --shots <dir>` renders a
full-size world from the angles above, and `godot --path game --write-movie war.avi --fixed-fps 30 res://tests/WarMovie.tscn`
films a host on the march (both need a display).

## Myths, magic and structures

The world grows its own legends, each with a cause you can follow:

- **Beasts** rise where the land is dangerous, more often after a ley surge: a wyrm in the mountains, a hydra in the
  swamp. They raid nearby towns until a hero hunts them by name; some become legends, very old ones slumber.
- **Trade roads** join markets at peace with each other; wars, occupations and danger sever them, and peace reopens
  them (the map's *Trade routes* layer).
- **Faiths** spread from realms and neighbours, held back by temples and saints' relics; a realm whose lands have
  turned takes up the new faith. A faith that loses its holy land grows zealous, may wage a holy war to take it
  back, and may split when a priest founds a heresy. Devout notables of renown become saints.
- **Magic** ebbs and flows in slow ley tides. Mage towers train mages where the law allows; a surge at a tower may go
  wild (a storm, a blight, a portal that lets danger in), and realms tighten their law after it. Where magic is
  outlawed, mages flee or burn.
- **Great works** take years: an architect, a patron, delays from war, plague and famine, collapses when rushed;
  towns that knew siege raise fortresses, and fallen works are restored.
- **Labyrinths** open in wild, ley-charged land with three to seven levels and a guardian on the deepest. Delvers go
  down level by level; slaying the guardian and taking the last relic clears it, leaving a ruin or a shrine.
- Rivals quarrel over their borders, and rulers who crave renown seek glory against weaker neighbours.

![A town with its temple and mage tower, a beast in the hills and a labyrinth's walls](docs/images/town.png)

## Run

```bash
dotnet test sim
dotnet run --project sim/FantasyWorld.Cli -- new worlds/aerenmoor.fw.db            # medium world (100 regions)
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db            # ASCII map (--layer regions)
dotnet run --project sim/FantasyWorld.Cli -- catchup worlds/aerenmoor.fw.db        # simulate real elapsed time
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 50y    # debug fast-forward
dotnet run --project sim/FantasyWorld.Cli -- regions worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- region worlds/aerenmoor.fw.db 12
dotnet run --project sim/FantasyWorld.Cli -- why worlds/aerenmoor.fw.db <event-id>
dotnet run --project sim/FantasyWorld.Cli -- characters worlds/aerenmoor.fw.db       # living notables
dotnet run --project sim/FantasyWorld.Cli -- life worlds/aerenmoor.fw.db <char-id>    # a biography
dotnet run --project sim/FantasyWorld.Cli -- artifacts worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- artifact worlds/aerenmoor.fw.db <id>     # powers and provenance
dotnet run --project sim/FantasyWorld.Cli -- realms worlds/aerenmoor.fw.db           # kingdoms and their vassals
dotnet run --project sim/FantasyWorld.Cli -- realm worlds/aerenmoor.fw.db <id>        # laws, council, treaties, eras
dotnet run --project sim/FantasyWorld.Cli -- wars worlds/aerenmoor.fw.db
dotnet run --project sim/FantasyWorld.Cli -- war worlds/aerenmoor.fw.db <id>          # casus belli and the course of the war
dotnet run --project sim/FantasyWorld.Cli -- map worlds/aerenmoor.fw.db --layer realms
dotnet run --project sim/FantasyWorld.Cli -- tell worlds/aerenmoor.fw.db <event-id>    # the why-chain as prose
dotnet run --project sim/FantasyWorld.Cli -- stats worlds/aerenmoor.fw.db             # world summary and LLM spend
```

## Story beats from Claude

The world runs fully offline by default: rules make every choice and templates tell the story. To let Claude
decide notables' choices, arbitrate duels, hunts and delves, name forged artifacts, wars and eras, and narrate
with `fw tell`:

```bash
export ANTHROPIC_API_KEY=...        # never stored in the world file
dotnet run --project sim/FantasyWorld.Cli -- llm worlds/aerenmoor.fw.db --provider anthropic --cap 5
dotnet run --project sim/FantasyWorld.Cli -- advance worlds/aerenmoor.fw.db 2y
```

Defaults: `claude-opus-5-5` for every job (`--model narrate=<model>` changes one), 60 story beats and 200
narrations per rolling day, and a $10 cap per 30 days; past it, or without credentials, the rules take over.
`--provider openrouter` uses `OPENROUTER_API_KEY` instead. Every answer is journaled in the world file, so
reloading or replaying a world never asks again, and `fw why` shows the motives and accounts it gave.

`--size tiny` makes a 64×64 world with 12 regions for quick experiments (and no prehistory; medium and large
worlds start with 100 years of it, `--prehistory <years>` to change).

## The chronicle

```bash
dotnet run --project sim/FantasyWorld.Cli -- digest worlds/aerenmoor.fw.db              # headlines since you last looked
dotnet run --project sim/FantasyWorld.Cli -- follow worlds/aerenmoor.fw.db char#12      # their deeds lead the digest
dotnet run --project sim/FantasyWorld.Cli -- live worlds/aerenmoor.fw.db --minutes 30   # watch it unfold in real time
dotnet run --project sim/FantasyWorld.Cli -- structures worlds/aerenmoor.fw.db          # landmarks, labyrinths and ruins
```

Every screen of the game also exists as data, for clients (the Godot diorama) and for checking without one:

```bash
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db digest            # the morning digest, as JSON
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db map --as-of 150   # the political map in Year 150
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db why <event-id>     # a why-chain as a layered graph
dotnet run --project sim/FantasyWorld.Cli -- view worlds/aerenmoor.fw.db character <id>     # also: family, chronicle, realm, artifact
```

`catchup` ends with the digest. Minor events older than 50 years are condensed into one summary per decade as
the world runs. Every command that changes a world first keeps a
backup of the last three sessions next to it (`.bak1`–`.bak3`). Benchmarks:
`dotnet run -c Release --project sim/FantasyWorld.Bench -- --filter '*'`.
````

Replace `sim/FantasyWorld.Sim/Systems/DiplomacySystem.cs` entirely with:
```csharp
using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>A reason to go to war, found in history: the event it cites and what it aims at.</summary>
public sealed record CasusBelliOption(string Kind, long Against, long EventId, long TargetRegionId, long TargetArtifactId, double Weight);

/// <summary>
/// Relations between independent realms (spec §6 "Diplomacy"), yearly. Realm opinion = the rulers'
/// personal opinion + realm memories + faith and culture + treaties. Friendly neighbours ally (sealed
/// by a royal marriage when kin allow), hostile ones declare rivalry. A ruler at peace weighs every
/// casus belli history offers against keeping the peace (a decision point); a declared war cites its
/// casus belli and draws in allies.
/// </summary>
public sealed class DiplomacySystem : ISimSystem
{
    private readonly IDecider _decider;
    private readonly IInventor _inventor;

    public DiplomacySystem(IDecider? decider = null, IInventor? inventor = null)
    {
        _decider = decider ?? new RulesDecider();
        _inventor = inventor ?? new RulesInventor();
    }

    public string Name => "diplomacy";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var realms = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var a in realms)
            foreach (var b in Realms.Neighbours(world, a).Where(b => b.Id > a.Id && b.LiegeId == 0))
                ReviewTreaties(ctx, a, b);

        foreach (var realm in realms)
        {
            if (realm.Destroyed || realm.LiegeId != 0 || AtWar(world, realm)) continue;
            var ruler = Realms.RulerOf(world, realm);
            if (ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var options = CasusBelliFor(ctx, realm);
            if (options.Count == 0) continue;
            var choices = options.Select((o, i) => new DecisionOption($"war:{i}", o.Weight * Aggression(world, realm, ruler)
                    * Math.Clamp(Strength(world, realm) / Math.Max(1, Strength(world, world.Realms[o.Against])), 0.3, 2.0),
                    $"{o.Kind} against {world.Realms[o.Against].Name}"))
                .Append(new DecisionOption("peace", 100 + realm.WarWeariness, "keep the peace"))
                .ToList();
            var choice = _decider.Decide(new DecisionBrief(ruler.Id, "war or peace", choices), ctx.Rng);
            if (choice.Key == "peace") continue;
            using (ctx.Annotate("motive", choice.Reasoning))
                Declare(ctx, realm, options[int.Parse(choice.Key[4..], CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>What realm a thinks of realm b, −100…100.</summary>
    public static double Opinion(World world, Realm a, Realm b)
    {
        var score = 0.0;
        if (Realms.RulerOf(world, a) is { } ra && Realms.RulerOf(world, b) is { } rb) score += Relations.Opinion(ra, rb) / 2;
        score += a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += a.FaithId == b.FaithId ? 15 : -15;
        if (a.CultureId == b.CultureId) score += 10;
        if (a.Has(TreatyKind.Alliance, b.Id)) score += 25;
        if (a.Has(TreatyKind.Rivalry, b.Id)) score -= 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Rough fighting power: levies from the territory, men-at-arms the treasury could pay, knights.</summary>
    public static double Strength(World world, Realm realm)
    {
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var levies = world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)).Sum(s => s.Adults) * 0.05;
        var knights = world.ActiveCharacters.Count(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        return levies + Math.Max(0, realm.Treasury) / 50 * 3 + knights * 20;
    }

    public static bool AtWar(World world, Realm realm) => world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id));

    private static double Aggression(World world, Realm realm, Character ruler)
    {
        var a = 1.0;
        if (ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave)) a *= 1.5;
        if (ruler.Has(Traits.Content) || ruler.Has(Traits.Craven) || ruler.Has(Traits.Kind)) a *= 0.5;
        var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
        if (tenets.Contains("pacifist")) a *= 0.3;
        if (tenets.Contains("warlike")) a *= 1.5;
        if (realm.Conditions.ContainsKey(Statecraft.StabilityCollapsed)) a *= 0.5;
        return a;
    }

    private static void ReviewTreaties(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        var ab = Opinion(world, a, b);
        var ba = Opinion(world, b, a);
        var allied = a.Has(TreatyKind.Alliance, b.Id);
        var rivals = a.Has(TreatyKind.Rivalry, b.Id);
        if (!allied && ab >= 40 && ba >= 40 && !AtWarWith(world, a, b))
        {
            var wedding = RoyalMarriage(ctx, a, b);
            var pact = Pact(ctx, a, b, TreatyKind.Alliance, "AllianceFormed", 40, wedding);
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == a.Id);
            Statecraft.Remember(a, pact.Id, b.Id, 40, 60);
            Statecraft.Remember(b, pact.Id, a.Id, 40, 60);
        }
        else if (allied && (ab < 0 || ba < 0))
        {
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
            var broken = ctx.Emit(new EventDraft("AllianceBroken", Actors: Pair(a, b), Place: world.Settlements[a.CapitalId].Ref,
                Importance: 40, Tags: new[] { "Politics", "Diplomacy" }));
            Statecraft.Remember(a, broken.Id, b.Id, -30, 60);
            Statecraft.Remember(b, broken.Id, a.Id, -30, 60);
        }
        else if (!rivals && ab <= -40 && ba <= -40)
            Pact(ctx, a, b, TreatyKind.Rivalry, "RivalryDeclared", 35, null);
    }

    private static WorldEvent Pact(SimContext ctx, Realm a, Realm b, TreatyKind kind, string type, int importance, WorldEvent? cause)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: Pair(a, b), Place: ctx.World.Settlements[a.CapitalId].Ref,
            Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Enabler, 0.8) },
            Importance: importance, Tags: new[] { "Politics", "Diplomacy" }));
        a.Treaties.Add(new Treaty(kind, b.Id, ev.Id, 0));
        b.Treaties.Add(new Treaty(kind, a.Id, ev.Id, 0));
        return ev;
    }

    /// <summary>Weds an unmarried adult child or sibling of one ruler to one of the other's, if any match.</summary>
    private static WorldEvent? RoyalMarriage(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        IEnumerable<Character> Kin(Realm realm) => Realms.RulerOf(world, realm) is { } ruler
            ? ruler.Children.Select(id => world.Characters[id]).Concat(Fates.Siblings(world, ruler)).Append(ruler)
                .Where(k => FamilySystem.Marriageable(k, ctx.Date))
            : Array.Empty<Character>();
        // Children first, then siblings, then the rulers themselves.
        foreach (var x in Kin(a))
            foreach (var y in Kin(b))
                if (x.Sex != y.Sex && !(x.Role == Role.Lord && y.Role == Role.Lord) && !Relations.AreKin(x, y))
                    return FamilySystem.Wed(ctx, x, y);
        return null;
    }

    /// <summary>Every casus belli this realm's history gives it against a neighbour.</summary>
    public static List<CasusBelliOption> CasusBelliFor(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var options = new List<CasusBelliOption>();
        var ruler = Realms.RulerOf(world, realm);
        foreach (var enemy in Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id)))
        {
            var theirs = Realms.Territory(world, enemy).ToList();
            var grudge = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling <= -50).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null)
            {
                var ev = ctx.Log.Get(grudge.EventId);
                var lost = ev.Type == "RegionCeded" && theirs.Any(r => ev.Place == r.Ref) ? ev.Place!.Value.Id : 0;
                if (ev.Type == "IndependenceDeclared")
                    options.Add(new(CasusBelli.Independence, enemy.Id, grudge.EventId, world.Settlements[enemy.CapitalId].RegionId, 0, 40));
                else if (lost != 0) options.Add(new(CasusBelli.Reconquest, enemy.Id, grudge.EventId, lost, 0, 40));
                else options.Add(new(CasusBelli.Revenge, enemy.Id, grudge.EventId, Border(world, realm, enemy), 0, 30));
            }

            var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
            if (realm.FaithId != enemy.FaithId && (tenets.Contains("zealous") || tenets.Contains("warlike")))
            {
                // Zeal is stirred by the rival faith's latest gain (a land turned, or a realm that took it up), else by the
                // latest wrong the rival did, else by the zealot's own coming to power, else by the rival's latest deed.
                var border = Border(world, realm, enemy);
                var stirred = ctx.Log.Involving(world.Regions[border].Ref).Concat(ctx.Log.Involving(enemy.Ref))
                    .Where(e => e.Type is "RegionConverted" or "RealmConverted" && e.Actors.GetValueOrDefault("faith").Id == enemy.FaithId)
                    .Select(e => e.Id).DefaultIfEmpty().Max();
                if (stirred == 0) stirred = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling < 0).Select(m => m.EventId).DefaultIfEmpty().Max();
                if (stirred == 0 && ruler is not null) stirred = Fates.Accession(ctx.Log, ruler)?.Id ?? 0;
                if (stirred == 0) stirred = ctx.Log.Involving(enemy.Ref).LastOrDefault()?.Id ?? 0;
                options.Add(new(CasusBelli.HolyWar, enemy.Id, stirred, border, 0, 20));
            }
            // A faith that lost its holy land to this neighbour wants it back, the more so the more zealous it is.
            if (world.Faiths.GetValueOrDefault(realm.FaithId) is { HolySiteLostEventId: not 0 } mine && world.Regions.TryGetValue(mine.HolyRegionId, out var holy)
                && Realms.SovereignOf(world, holy)?.Id == enemy.Id && (mine.Zeal >= 40 || tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, mine.HolySiteLostEventId, holy.Id, 0, 40));

            // Declared rivals quarrel over the land between them.
            if (realm.Treaties.FirstOrDefault(t => t.Kind == TreatyKind.Rivalry && t.With == enemy.Id) is { } rivalry)
                options.Add(new(CasusBelli.BorderDispute, enemy.Id, rivalry.SinceEventId, Border(world, realm, enemy), 0, 25));

            // A ruler who craves renown seeks it against a weaker neighbour (their temper decides how often).
            if (ruler is not null && ruler.Ambition == Ambitions.Renown
                && Strength(world, realm) > Strength(world, enemy) * 1.2 && Fates.Accession(ctx.Log, ruler) is { } crowned)
                options.Add(new(CasusBelli.Glory, enemy.Id, crowned.Id, Border(world, realm, enemy), 0, ruler.Has(Traits.Ambitious) ? 15 : 8));

            var hungry = Realms.Domain(world, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
            var granary = theirs.Where(r => r.Fertility >= 60 && Touches(world, r, realm)).OrderByDescending(r => r.Fertility).ThenBy(r => r.Id).FirstOrDefault();
            if (hungry != 0 && granary is not null) options.Add(new(CasusBelli.ResourceNeed, enemy.Id, hungry, granary.Id, 0, 30));

            if (ruler is not null)
            {
                foreach (var artifact in world.Artifacts.Values.Where(x => HeldBy(world, x, enemy)))
                {
                    // Only a fresh wrong counts: the artifact's latest change of hands was a theft from the ruler's house.
                    var theft = artifact.Provenance.LastOrDefault();
                    if (theft is { How: "stolen" or "looted" } && ruler.HouseId != 0
                        && world.Characters.TryGetValue(theft.From, out var victim) && victim.HouseId == ruler.HouseId) options.Add(new(CasusBelli.ArtifactRecovery, enemy.Id, theft.EventId, world.Settlements[enemy.CapitalId].RegionId, artifact.Id, 25));
                }
                if (Realms.RulerOf(world, enemy) is { } theirRuler && world.Characters.TryGetValue(theirRuler.SpouseId, out var consort)
                    && (consort.HouseId == ruler.HouseId) && consort.MarriageEventId != 0)
                    options.Add(new(CasusBelli.Claim, enemy.Id, consort.MarriageEventId, world.Settlements[enemy.CapitalId].RegionId, 0, 20));
            }
        }
        return options;
    }

    private void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
    {
        var world = ctx.World;
        var defender = world.Realms[cb.Against];
        var cbEvent = cb.EventId;
        if (cb.Kind == CasusBelli.HolyWar)
            cbEvent = ctx.Emit(new EventDraft("HolyWarProclaimed", Actors: Pair(attacker, defender),
                Place: world.Settlements[attacker.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["faith"] = world.Faiths[attacker.FaithId].Name },
                Causes: cb.EventId != 0 ? new[] { new Cause(cb.EventId, CauseKind.Motive, 0.9) } : null,
                Importance: 50, Tags: new[] { "Politics", "Faith" })).Id;

        var target = world.Regions.GetValueOrDefault(cb.TargetRegionId);
        var name = cb.Kind switch
        {
            CasusBelli.HolyWar when target is not null && target.Id == world.Faiths[attacker.FaithId].HolyRegionId => $"The Holy War for {target.Name}",
            CasusBelli.HolyWar => $"The Holy War of {world.Faiths[attacker.FaithId].Deity}",
            CasusBelli.Independence => $"The {defender.Name} Rebellion",
            CasusBelli.Revenge => $"The War of {attacker.Name}'s Vengeance",
            CasusBelli.Glory when Realms.RulerOf(world, attacker) is { } glorious => $"The War of {glorious.Name}'s Glory",
            _ => $"The {target?.Name ?? defender.Name} War",
        };
        var warId = world.Ids.Peek(War.Kind);
        var invention = _inventor.Invent(new InventionBrief("war", $"{War.Kind}#{warId}",
            $"A war of {cb.Kind} declared by {Statecraft.Title(attacker)} on {Statecraft.Title(defender)}"
            + (target is null ? "" : $" over {target.Name}") + $", in year {ctx.Date.Year}.", name));
        name = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = name,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            Attackers = { attacker.Id },
            Defenders = { defender.Id },
            CasusBelli = cb.Kind,
            CasusBelliEventId = cbEvent,
            TargetRegionId = cb.TargetRegionId,
            TargetArtifactId = cb.TargetArtifactId,
            StartDay = ctx.Date.Day,
        };
        var declared = ctx.Emit(new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = attacker.Ref, ["defender"] = defender.Ref, ["war"] = war.Ref },
            Place: world.Settlements[defender.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["casus belli"] = cb.Kind, ["war"] = name },
            Causes: WarCauses(ctx, attacker, defender, cbEvent),
            Importance: 70, Tags: new[] { "Politics", "War" }));
        war.DeclaredEventId = declared.Id;
        world.Wars[war.Id] = war;
        attacker.Treaties.RemoveAll(t => t.With == defender.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        defender.Treaties.RemoveAll(t => t.With == attacker.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        Statecraft.Remember(defender, declared.Id, attacker.Id, -50, 70);

        foreach (var (side, leader) in new[] { (war.Attackers, attacker), (war.Defenders, defender) })
            foreach (var ally in Realms.Independent(world).Where(r => r.Has(TreatyKind.Alliance, leader.Id) && !AtWar(world, r)).OrderBy(r => r.Id).ToList())
            {
                // An ally of both sides stays out rather than fight a friend.
                if (war.Involves(ally.Id) || ally.Has(TreatyKind.Alliance, attacker.Id) && ally.Has(TreatyKind.Alliance, defender.Id)
                    || !ctx.Rng.Chance(0.7)) continue;
                side.Add(ally.Id);
                ctx.Emit(new EventDraft("AllyJoinedWar",
                    Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                    Payload: new Dictionary<string, string> { ["side"] = side == war.Attackers ? "attackers" : "defenders" },
                    Place: world.Settlements[ally.CapitalId].Ref,
                    Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
            }
    }

    /// <summary>
    /// The casus belli is the motive; the realms' latest rivalry, their latest shared grievance (a peace, a ceded
    /// region, a declaration of independence) and the ruler's accession, when there are any, are enablers (at most
    /// three causes in all), so a war's why-chain reaches back past its pretext.
    /// </summary>
    private static List<Cause> WarCauses(SimContext ctx, Realm attacker, Realm defender, long cbEvent)
    {
        var causes = new List<Cause> { new(cbEvent, CauseKind.Motive, 0.9) };
        bool Cited(long id) => causes.Any(c => c.EventId == id);
        var shared = ctx.Log.Involving(attacker.Ref).Where(e => e.Actors.ContainsValue(defender.Ref)).ToList();
        var rivalry = shared.LastOrDefault(e => e.Type is "RivalryDeclared" or "AllianceBroken");
        if (rivalry is not null && !Cited(rivalry.Id)) causes.Add(new Cause(rivalry.Id, CauseKind.Enabler, 0.5));
        var grievance = shared.LastOrDefault(e => e.Type is "PeaceSigned" or "RegionCeded" or "IndependenceDeclared");
        if (grievance is not null && !Cited(grievance.Id)) causes.Add(new Cause(grievance.Id, CauseKind.Enabler, 0.4));
        if (causes.Count < 3 && Realms.RulerOf(ctx.World, attacker) is { } ruler
            && Fates.Accession(ctx.Log, ruler) is { } accession && !Cited(accession.Id))
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.3));
        return causes;
    }

    private static bool AtWarWith(World world, Realm a, Realm b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.IsAttacker(a.Id) && w.Defenders.Contains(b.Id) || w.IsAttacker(b.Id) && w.Defenders.Contains(a.Id)));

    private static bool HeldBy(World world, Artifact x, Realm realm) => x.Place switch
    {
        ArtifactPlace.Held => Realms.SovereignOf(world, world.Settlements[world.Characters[x.HolderId].SettlementId])?.Id == realm.Id,
        ArtifactPlace.Treasury => Realms.SovereignOf(world, world.Settlements[x.SettlementId])?.Id == realm.Id,
        _ => false,
    };

    /// <summary>The enemy's region nearest our territory: an enemy region adjacent to ours, lowest id.</summary>
    private static long Border(World world, Realm ours, Realm theirs) =>
        Realms.Territory(world, theirs).Where(r => Touches(world, r, ours)).Select(r => r.Id).DefaultIfEmpty(world.Settlements[theirs.CapitalId].RegionId).Min();

    private static bool Touches(World world, Region region, Realm realm) =>
        (world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).Any(n => Realms.SovereignOf(world, world.Regions[n])?.Id == realm.Id);

    private static Dictionary<string, EntityRef> Pair(Realm a, Realm b) => new() { ["a"] = a.Ref, ["b"] = b.Ref };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test sim --filter "FullyQualifiedName~MythHealthTests|FullyQualifiedName~SuccessCriteriaTests"`
Expected: `Passed!  - Failed: 0, Passed: 8`. Then `dotnet test sim` → `Passed: 557` in total.

- [x] **Step 5: Run the headless game checks**

Run (with `godot` the Godot 4.3 .NET binary): `dotnet build game/FantasyWorld.Game.csproj && godot --headless --path game --import && godot --headless --path game res://tests/HeadlessChecks.tscn`
Expected: `HEADLESS CHECKS PASSED (73)` and exit code 0.

- [x] **Step 6: Render the README screenshots**

On a machine with a display (or under `xvfb-run`):
```bash
godot --path game res://tests/Showcase.tscn -- --shots /tmp/showcase
cp /tmp/showcase/4-town.png docs/images/town.png
```

- [x] **Step 7: Commit**

```bash
git add README.md sim
git commit -m "test(sim): check that myths trace back to causes and hold for two centuries; document them

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage (this plan)

| Spec item | Task |
|---|---|
| §3 monsters: rising, aging, slumber, raids, legends | 3 |
| §3 hunts against named monsters, trophies | 3 |
| §4 faith spread, temples, conversions, holy sites | 5 |
| §4 schisms, holy war, saints and relics | 6 |
| §5 ley tides, surges, wild magic, mage towers, magic law | 7 |
| §6 great works: architects, delays, collapse, fortifications, restoration | 8 |
| §7 labyrinth levels, guardians, clearing | 9 |
| §8 trade routes: network, wealth flow, severing and restoring | 1 |
| §9 views and client: routes layer, lairs, towers, temples, faith on pages | 2, 4, 10 |
| §10 testing: unit tests per system, soak with invariants, performance, why-chain criterion | all; 11 |
| (added) wars of rivalry and glory | 8 |

Deferred: monsters migrating between regions as entities (the older "monsters migrated" region event stays), trade by sea, persecution of faiths, and LLM-written lore for new faiths beyond their names.
