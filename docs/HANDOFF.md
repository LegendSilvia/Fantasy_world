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
| Sub-project 2 (Chronicle & why UI data) | **Done** — spec `docs/superpowers/specs/2026-10-06-chronicle-views-design.md`, plan `docs/superpowers/plans/2026-10-06-chronicle-views.md`, Tasks 1–8; 478/478 tests pass; every screen well under a millisecond (budget 50 ms) |
| Sub-project 4 (Godot diorama client) | **Done** — spec `docs/superpowers/specs/2026-10-06-godot-diorama-client-design.md`, plan `docs/superpowers/plans/2026-10-06-godot-diorama-client.md`, Tasks 1–13; 512/512 tests pass; 69 headless Godot checks pass (also in CI); screenshots in `docs/images/` |
| Sub-project 3 (Myth, magic & structures) | **Done** — spec `docs/superpowers/specs/2026-10-06-myth-magic-structures-design.md`, plan `docs/superpowers/plans/2026-10-06-myth-magic-structures.md`, Tasks 1–11; 557/557 tests pass; 73 headless Godot checks pass; a two-century medium soak with invariants checked every tick |
| Sub-project 5 (Narration polish & evals) | **Done** — spec `docs/superpowers/specs/2026-10-06-narration-polish-design.md`, plan `docs/superpowers/plans/2026-10-06-narration-polish.md`, Tasks 1–7; 586/586 tests pass; 74 headless Godot checks pass |
| **All five sub-projects** | **Complete** |
| Sub-project 6 (Daily broadcast) | **M1 built** — brief `docs/superpowers/specs/2026-10-26-daily-broadcast-brief.md`, spec `docs/superpowers/specs/2026-10-26-daily-broadcast-design.md`, plan `docs/superpowers/plans/2026-10-26-daily-broadcast-m1.md`, Tasks 1–4; 617/617 tests pass; 74 headless Godot checks pass. **Live since 2026-10-26:** secrets set, merged to `main`, dry run (staging, test channel), `create` run (first episode posted), and a same-day rerun that posted nothing all passed in GitHub Actions. Waiting for three scheduled mornings: GitHub never started the first (2026-10-27, cron `30 22 * * *`, workflow active), so that episode was made by hand at 00:05 UTC (run 11: backup, 264 days caught up, posted once). The scheduled run did come, three hours late (01:32 UTC, run 12): it found the episode built and posted and touched nothing, and its keep-alive job committed the month's heartbeat to `main`. If the 05:30 slot keeps starting this late, an off-peak minute (e.g. `17 22 * * *`) may help; the owner then skipped the three-morning wait (2026-10-27): **M1 is done**, every step having been verified by hand and dry runs; the morning checks continue only as monitoring. **M2 built on the branch** (started early at the owner's request) — plan `docs/superpowers/plans/2026-10-26-daily-broadcast-m2.md`, Tasks 1–3; 629/629 tests pass: the chronicler (Claude Sonnet) writes the script, held to the template's facts, and posts carry the political map. **M3 built on the branch** — plan `docs/superpowers/plans/2026-10-26-daily-broadcast-m3.md`, Tasks 1–4; 650/650 tests pass: ElevenLabs v4 Turbo narration (voice Michael C. Vincent) attached to each post, respellings for invented names, a shared monthly character budget, and a podcast feed and archive page once `publicBaseUrl` is set. **M4 built on the branch** — plan `docs/superpowers/plans/2026-10-26-daily-broadcast-m4.md`, Tasks 1–3; 660/660 tests pass: each episode as a video (ffmpeg) uploaded to YouTube at most once a day, on once the three YouTube secrets exist (dry runs make the video without uploading). M2–M4 merged to `main` on 2026-10-27 on the owner's "merge" |

## Next step

1. The daily broadcast (M1–M4) is live on `main`: each morning at 05:30 Bangkok (GitHub may start it hours late) the world lives on, Sonnet writes the script, ElevenLabs narrates it with respellings, and Discord and YouTube (public) get the episode. Optional: turn on public access for the bucket and set `publicBaseUrl` (and `podcastEmail`) in `broadcast.json` for the podcast feed and archive page (`docs/broadcast-setup.md` §4). The throwaway prefixes `staging-check/`, `staging-check2/`, `staging-youtube/` and `staging-voice/` in the bucket can be deleted.
2. If the late starts matter, a Cloudflare Worker cron could dispatch the workflow at 05:30 exactly (needs a GitHub token as a Worker secret).
3. With an API key, run the live evals once (`fw eval narration --cap 2`, `fw eval decisions`) and read the reports in `evals/`; the LLM voice has only been exercised against scripted providers here.
4. Later work noted in the plans' Deferred lines: sound, settings and world management screens, desktop exports, sea trade, possessives and plural agreement in the template voice.

Notes from the first M2–M4 dry runs on GitHub Actions (2026-10-26, branch):
- ffmpeg is not on the runner and apt's man-db rebuild hung the install; fixed (installed only for a video, man-db and needrestart skipped, 10-minute limit).
- Sonnet respelled the staging world's names (24 asked, about $0.0066). ElevenLabs then refused the dictionary (HTTP 401: the key likely lacks "Pronunciation Dictionaries: Write") and the speech itself (**HTTP 402, Payment Required**): the ElevenLabs plan cannot use the chosen Voice Library voice through the API, or has no credits. Episodes go out as text and map until the owner upgrades the plan (or picks a premade voice) and gives the key both permissions. A dictionary refusal no longer stops the narration.
- After the owner fixed the plan, the narration worked (1:40, then 1:22 for 2026-10-27). The full dry run for 2026-10-27 (run 8) passed every step: 160 days caught up, map, respellings (15 asked, $0.0045), narration, test-channel post, and a 1.9 MB video stored. But both of Sonnet's scripts ($0.0175) were rejected and the template went out: the guard took spoken openers ("Welcome", "Our", "Farewell") for invented names. Fixed: the guard accepts the common words a script read aloud begins sentences with (invented names are still caught), the prompt asks for no unnamed names or numbers, and a rejected answer's reasons are kept in the journal and printed under the LLM line. The dictionary is still refused (HTTP 401).
- Two more dry runs the same night under throwaway prefixes (`staging-check/`, `staging-check2/`, copies of the live world) tried the fix at once. The first showed the new reasons (a span of years worked out by Sonnet, a word not in the brief); the retry is now told them. The second passed: **1,392 characters by the chronicler** ($0.0190 with one rejected try), narrated 1:38, posted to the test channel, 2.4 MB video stored. The two throwaway prefixes in the bucket can be deleted.
- 2026-10-27, after the owner added the three YouTube secrets: a dry run with the new **upload-private** input (run 13, throwaway prefix `staging-youtube/`) made the episode by the chronicler (1,137 characters), narrated it (1:17), posted to the test channel and **uploaded the video to YouTube as private** (https://youtu.be/x4rY7XUvx5s); YouTube kept it private as asked. `staging-youtube/` can be deleted too.
- Same day, after the owner gave the ElevenLabs key the Pronunciation Dictionaries permission: run 14 (throwaway `staging-voice/`) stored the respellings (no 401), the chronicler's script passed first time, narration 1:14. M2–M4 merged to `main` on the owner's "merge". The ffmpeg install hit its 5-minute limit again (ffmpeg was installed in time): needrestart is now suspended and the limit is 10 minutes. `staging-voice/` can be deleted.

Notes from sub-project 6 (M1) execution:
- Try it locally: `dotnet run --project sim/FantasyWorld.Broadcast -- run --dry-run --create --local bucket/` (a folder stands in for R2); `fw episode <world> --out ep/` writes one episode by hand.
- The stored world records which day's episode it holds (`World.LastBroadcastDate`), so a run stopped anywhere is picked up; failed posts are made up by the next run, oldest first, for up to a week.
- Found and fixed a sim bug: a fallen realm fell again for each region it ceded (triple headlines).
- Owner decisions after the spec: ElevenLabs model `eleven_v4_turbo`; proposed voice "Stephen" (`RbNTU8eTHcsao6T0f1ve`), to confirm in M3.

Notes from sub-project 5 execution:
- Try it: `fw tell <world> <event-id>` and `fw digest <world>` read in the new voice; the why page shows the story of why.
- Narration goldens: `sim/FantasyWorld.Sim.Tests/Narration/Golden/narration.txt`; regenerate with `UPDATE_GOLDEN=1 dotnet test sim --filter NarrationEvalTests` and read the diff.
- The guard rejects LLM narration whose names or numbers are not in its brief (including an unknown word opening a sentence); a false alarm only costs the template prose.

Notes from sub-project 3 execution:
- Try it: `fw new worlds/w.fw.db --seed 7 --size medium --prehistory 200`, then `fw log … | grep -E "Monster|Holy|Schism|Saint|Wild|Labyrinth|Route"`; the map's Trade routes layer and the town view show roads, beasts, temples and mage towers.
- Four systems joined `DefaultSystems` (trade, monsters, faith, magic); snapshot version 6 adds routes and monsters. Wars of rivalry and glory were added because some worlds went a century without a war.
- History replays from events of importance ≥ 30 (compaction keeps them): roads reopening and closing, occupations lapsing and works collapsing are recorded at that weight.

Notes from sub-project 4 execution:
- Try it: install Godot 4.3 .NET, `dotnet build game/FantasyWorld.Game.csproj`, then open `game/` in Godot (or run it with `--path game`); it opens `worlds/default.fw.db`, catching up first.
- Headless checks: `godot --headless --path game res://tests/HeadlessChecks.tscn` (prints `HEADLESS CHECKS PASSED (n)`, exit 0/1). Screenshots: add `-- --shots <dir>` under a display (xvfb works with `--rendering-driver opengl3`).
- The sim never runs on the main thread: catch-up, live steps and digests run on workers; LLM providers use `ConfigureAwait(false)` for this reason.
- Godot C# needs one node class per file, named after the file; vertex colours are converted from sRGB to linear.

Notes from sub-project 2 execution:
- Try it: `fw view worlds/w.fw.db digest`, `… map --as-of 50`, `… character char#12`, `… why <event-id>`, `… chronicle --category war`, `… realm 3`, `… artifact 2` (JSON for clients).
- Past maps are replayed from founding owners recorded at world creation; worlds made before this show today's map at any date (`historical: false`).
- Goldens live in `sim/FantasyWorld.Sim.Tests/Views/Golden/`; regenerate deliberately with `UPDATE_GOLDEN=1 dotnet test sim --filter ViewJsonTests` and review the diff.

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
6. Daily broadcast (GitHub Actions, R2, Discord; later Sonnet scripts, ElevenLabs narration, RSS, YouTube)
