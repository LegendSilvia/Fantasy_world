# Fantasy World — Godot Diorama Client Design (sub-project 4)

**Status:** draft for review · **Builds on:** the sim core (Plans 1–6) and the chronicle views (sub-project 2)
**Design reference:** Claude Design canvas "Fantasy World UI" — *Morning digest*, *Diorama map HUD*, *Character page*, *Why chain*.

## 1. Purpose

The game the player opens: a god's-eye **diorama** of their world — a floating cross-section block of land
with its regions, rivers, settlements, landmarks and labyrinths — that catches up on launch, keeps living
while open (1 real day = 1 world year), and lets the player read *why* anything happened. Everything shown
comes from sub-project 2's view models; the client never queries the simulation directly.

## 2. Architecture

```
game/                                   Godot 4.3 .NET project (Godot.NET.Sdk 4.3.0, net8.0)
  project.godot, FantasyWorld.Game.csproj   references sim/FantasyWorld.Sim, sim/FantasyWorld.Llm, sim/FantasyWorld.Client
  scenes/Main.tscn                      root: Diorama (3D) + Screens (UI layer) + status pill
  scenes/Diorama.tscn                   WorldBlock mesh, water, markers, OrbitCamera
  scenes/screens/{Digest,MapHud,Character,Why}.tscn
  scripts/                              thin Godot nodes: build nodes from client models, forward input
  theme/diorama.tres, fonts/            Instrument Serif + Geist (OFL), palette from the design
  tests/HeadlessChecks.tscn             loads a tiny world, opens every screen, exits 0/1
sim/FantasyWorld.Client/                Godot-free client logic (net8 class library, xUnit-tested)
  Session.cs                            open/create a world, catch up on a background thread with progress,
                                        LiveHost stepping, save, digest marking, follow
  BlockMesh.cs                          terrain grid → top-surface vertices/colours + side strata (arrays)
  Projection.cs                         map point (0–1) ↔ block-space position; pin placement and decluttering
  OrbitRig.cs                           orbit/zoom/pan maths with limits (drag to orbit, scroll to descend)
  ScreenRouter.cs                       navigation state: digest → why → character → map, back stack
  Palette.cs                            categories/realms → colours (design tokens)
```

Rules: Godot nodes hold no game logic; everything testable lives in `FantasyWorld.Client` (xUnit), and the
headless Godot check proves the scenes load, bind and navigate. The sim runs on a worker thread; the UI
reads immutable view models produced after each step.

## 3. The diorama

- **World block:** the terrain grid (256×256 for medium) becomes a heightfield mesh, downsampled to 128×128
  quads, coloured by biome (the design's greens/sands/greys), water as a separate translucent plane, rivers
  as thin ribbons; the four sides show strata bands (grass, soil, rock) like the design's isometric block.
- **Markers:** settlements as small blocks scaled by tier; landmarks as diamonds; labyrinths as nested
  diamonds; realm borders as dashed lines along region edges (from `MapScreen.Borders`); conflict pins in
  the accent orange; numbered digest pins (1–5) above their places.
- **Camera:** orbit around the block (drag), zoom/descend (scroll) between *Realm*, *Region* and *Town*
  presets, reset view; smooth damping; never below the surface.
- **Time:** a thin progress line and "Caught up · Year 312, day 196" pill; live mode advances while open.

## 4. Screens (from the design)

- **Morning digest** (launch screen after catch-up): title "Year N.", subtitle, control card with
  Digest / Full chronicle toggle, the headline with "Why did this happen?", items 2–5 with linked names,
  numbered pins on the diorama. Clicking a pin or item focuses the camera there.
- **Map HUD:** world name and summary, zoom presets, layer toggles (borders, settlements, events,
  landmarks & labyrinths, trade routes — disabled until modelled), history slider (Year 1 … now) driving
  `MapScreen.Build(asOf)`, settlement hover card, legend.
- **Character page:** name, line, trait chips, ambition chip, timeline with highlight, ties card, Follow
  and Family tree buttons, a small diorama inset showing where they are.
- **Why chain:** outcome card, cause layers with "because" connectors, dashed root cards, side panel
  (people, places, systems), Show on map, Trace further (consequences).
- Every linked name navigates (character → Character page; place → Map focused there; war/realm →
  chronicle filtered); Back returns.
- **War** (added after review): drawn like a strategy map on the diorama — campaign arrows along each host's road
  (attackers red, defenders blue) that draw in and flow, banners in realm colours that wave and march as days pass,
  fronts with teeth facing the attacker, occupied land hatched, crossed swords over the year's battles; banners open
  their commander, battles their why chain. Knights ride with their hosts and battles touch their lives.

## 5. Style

Palette and type from the design: ground `#ecebe7`, cards `#f7f6f3` with `#dcdad4` borders and soft shadow,
ink `#1a1a18`, muted `#66645d`, accents `#c4621f` (conflict) and `#3b6fb6` (realm/followed); italic
**Instrument Serif** for display, **Geist** for UI; black pill buttons; floating control card top-right.

## 6. Performance

60 fps on a laptop iGPU at 1440×900: block mesh built once per world (static geography), markers updated
only when a view model changes; sim steps never on the main thread.

## 7. Testing

- xUnit for `FantasyWorld.Client` (mesh sizes and normals, projection round-trips, orbit limits,
  router navigation, session catch-up/progress/cancel with a fake clock).
- Headless Godot check scene run in CI: opens a generated tiny world, shows each screen, follows a link,
  scrubs the history slider, asserts node trees and labels, exits with a status code.
- Manual: visual review against the design canvas (screenshots from a desktop run).

## 8. Plan outline (~9 tasks)

1. `FantasyWorld.Client` library + Session (open/create, catch-up thread, live stepping, save).
2. BlockMesh + Projection (+ tests).
3. OrbitRig + ScreenRouter + Palette (+ tests).
4. Godot project skeleton, theme and fonts, headless check harness.
5. Diorama scene: block, water, markers, camera.
6. Digest screen.
7. Map HUD (layers, history slider, cards).
8. Character page and Why chain.
9. Navigation polish, status pill and live updates, CI job for the headless check, README.
