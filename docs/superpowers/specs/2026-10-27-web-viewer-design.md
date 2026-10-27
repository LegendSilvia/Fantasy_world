# Fantasy World — The World in a Browser (sub-project 11)

**Status:** W1 in progress · **Asked by:** the owner, 2026-10-27 ("go with B") · **Builds on:** the sim's views
(`FantasyWorld.Sim.Views`), the renderer-free geometry (`FantasyWorld.Client`), the daily broadcast (sub-project 6)

## 1. Purpose

Viewers (and the owner, anywhere) should be able to open the world in a browser and inspect it: the diorama, the realms,
the people, the wars, the history, the chain of causes behind any event. The Godot game cannot go to the web (Godot 4's
C# projects have no web export), so the browser runs **the same C# code** that the game and the film use, compiled to
WebAssembly, on **the same data** the daily broadcast publishes. One source of truth: a fix to the sim or a view shows
in the game, the film and the site alike.

## 2. What exists today (facts from the code)

| Piece | Today |
|---|---|
| Views | `FantasyWorld.Sim.Views`: every screen's data (map, digest, character, realm, war, why, chronicle, family), pure C#, pinned by golden JSON |
| Geometry | `FantasyWorld.Client`: the terrain block, walls, water, trees, houses, borders, arrows, fronts, markers as plain meshes (`MeshData`); Godot only draws them |
| Data | The daily run uploads the day's world (`world.fw.db`, SQLite) to R2; the world itself is JSON inside (`WorldSnapshot`, `SimJson`) |
| Native code | Only `WorldStore` (SQLite, `Microsoft.Data.Sqlite`) is native; nothing else in Sim or Client is |
| Public hosting | None yet: `publicBaseUrl` is unset; the bucket is private |

## 3. Design

- **The bundle (W1).** The daily run also writes `web/world.json.gz`: the world snapshot, the event log and the
  geography, in the sim's own JSON (`SimJson`), gzipped. The browser never needs SQLite. `fw export-web <world> <out>`
  makes it; a round-trip test pins that a world loaded from the bundle yields the same screens (golden JSON) as one loaded
  from the file.
- **The app (W2).** `FantasyWorld.Web`, a standalone Blazor WebAssembly app (no .NET workloads needed, no native code),
  read-only: it loads the bundle and answers the page through JS interop: a screen's JSON (`Screen("map", year)`,
  `Screen("character", id)`, `Screen("why", eventId)`…) and meshes as typed arrays (`Mesh("block")`, `Mesh("war", year)`).
  It never simulates forward.
- **The page (W3).** One static page: three.js (an exact pinned version) draws the diorama from the meshes with the
  game's palette, the realms' standards and names, settlements, hosts and their current moves; an orbit camera; the
  history slider; panels for characters, realms, wars and "why" opened by clicking; links that can be shared
  (`#character/12`, `#year/40`).
- **Publishing (W4).** The workflow builds the app and publishes the page with the day's bundle; the episode's YouTube
  description and Discord post link to it.

## 4. Decisions

| Topic | Decision |
|---|---|
| Approach | B: the same C# in the browser (the owner, 2026-10-27) |
| Read-only | The site inspects the published day and its history; it does not simulate |
| Data path | A JSON bundle, not the SQLite file (no native code in the browser) |
| **Hosting (open)** | Cloudflare Pages (needs a Pages API token as a repository secret) or the R2 bucket made public (a custom domain or r2.dev, then `publicBaseUrl`). To be chosen by the owner before W4 |

## 5. Milestones

- **W1 — The bundle:** `export-web`, the round trip, the daily run writes it to the store.
- **W2 — The app:** the Blazor WebAssembly project, loading the bundle, screens and meshes over JS interop; tests on the
  interop answers.
- **W3 — The page:** three.js diorama, panels, slider, links; checked in a headless browser (Playwright) for the map
  drawing and a panel opening.
- **W4 — Published:** built and deployed by the workflow with each broadcast; linked from YouTube and Discord.
