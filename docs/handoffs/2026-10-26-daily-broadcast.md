# Handoff: Daily Broadcast (sub-project 6)

Turn Fantasy World into a **daily narrated show**. Every morning the world lives one more year, a narrator reads what happened and *why*, and the episode is published for followers. Everything runs unattended in **GitHub Actions**, with the world file stored in **Cloudflare R2**. Nothing runs on the owner's PC.

This brief is for Claude Code. Follow the repo's existing flow: turn this into a design spec in `docs/superpowers/specs/` and an implementation plan in `docs/superpowers/plans/` (same format as the earlier plans, including **Decisions made while drafting**), get them reviewed, then execute task by task.

---

## 1. Where the project stands

- The sim core (Plans 1–6) and sub-projects 2–5 (chronicle views, myth/magic/structures, Godot diorama client, narration polish and evals) are all **complete** (last commit: "all sub-projects complete").
- Relevant pieces that already exist, so reuse them instead of rebuilding:
  - `CatchUp.Run` fast-forwards by real time elapsed since `World.LastRealUtc` (1 real day = 1 world year), capped at `TimeScale.MaxCatchUpDays` (30 world years).
  - `Digest.Build` / `DigestScreen.ForReader` produce headlines and why-chains. `fw view <world> digest` emits JSON.
  - Narration layer: `TemplateNarrator` (offline voice), `Chronicler` (persona derived from the seed), `NarrationGuard` (facts-only check), `LlmNarrator`. LLM answers are journaled in the world file.
  - `fw llm <world> --model narrate=<model>` sets the model per tier. Spend caps already exist.
  - `ci.yml` already installs .NET 8 and Godot 4.3 .NET on `ubuntu-latest` and runs headless checks.

## 2. Decisions already made by the owner

| Topic | Decision |
|---|---|
| Runner | **GitHub Actions**, scheduled daily |
| Storage | **Cloudflare R2** for the world file, backups, and published media |
| Script writer | **Claude Sonnet** (`claude-sonnet-5-5`) on the `narrate` tier |
| Narrator voice | **ElevenLabs** text-to-speech |
| Hosting | Nothing on the owner's PC. Fully automated. |

## 3. Open decisions (propose a default, flag them in the plan for review)

| Topic | Suggested default | Notes |
|---|---|---|
| Broadcast language | **English** | The template voice and `English.cs` are English-only. Thai would need a translation step and a Thai-capable voice. Keep language a config value. |
| Episode length | 2–4 minutes of audio | Roughly 1,800–3,600 characters of script |
| ElevenLabs model | Expressive model for quality, faster/cheaper model as fallback | Check current model names and per-request character limits in the ElevenLabs API docs. Do not hard-code assumptions. |
| Voice | Chosen by the owner | `voice_id` is config. Match it to the world's chronicler temperament. |
| Publish time | 05:30 Asia/Bangkok (`30 22 * * *` UTC) | GitHub cron can start late under load. That's acceptable. |
| Channels for v1 | Discord webhook + podcast RSS feed + archive web page | YouTube is milestone 4 |

## 4. Architecture

```
GitHub Actions (daily cron, concurrency: broadcast, cancel-in-progress: false)
 ├─ 1. pull world.fw.db from R2 (+ write a dated backup to R2 first)
 ├─ 2. fw catchup               → advance real elapsed time
 ├─ 3. fw episode --out ep/     → episode.json (facts, headlines, why-chains) + script.txt
 ├─ 4. broadcast tts            → ep/narration.mp3 via ElevenLabs
 ├─ 5. broadcast media          → ep/map.png (+ ep/episode.mp4 in M4)
 ├─ 6. broadcast publish        → R2 media, RSS feed, archive page, Discord webhook
 └─ 7. push world.fw.db back to R2 (only after steps 2–3 succeeded)
```

### New code
- **`fw episode` (in the CLI + Sim):** builds one episode from events since a **new broadcast marker** (`World.LastBroadcastEventId`), not `LastSeenEventId`. `catchup` and `digest` already move `LastSeenEventId`, and the owner's own reading must not affect what the audience hears. Output is deterministic data plus a script.
- **Episode script = a new narration job, not a new system.** Use the chronicler persona and house style, run the LLM output through `NarrationGuard`, and journal the call. If the LLM fails, is over cap, or is rejected by the guard, **fall back to a template script** so an episode always ships. Script format: a short cold open (the headline), 3–6 stories, at least one "why" told as a chain of causes, then a sign-off line from the chronicler.
- **`FantasyWorld.Broadcast` (new .NET project, same toolchain):** `tts`, `media`, and `publish` commands. Keeps secrets and network calls out of the sim.
  - **TTS:** split the script on paragraph boundaries under the API's per-request limit, then concatenate with ffmpeg. Invented names will be mispronounced, so generate pronunciation hints from world names (ElevenLabs pronunciation dictionaries or respelling, whichever the current API supports).
  - **Map image (v1):** render the political map from `fw view map` JSON to PNG in .NET. No Godot needed. Godot diorama shots (via xvfb, `Showcase.tscn`) are a later upgrade.
  - **Publish:** upload media to a public R2 bucket (custom domain optional), regenerate `feed.xml` (valid podcast RSS with enclosure, duration, GUID = world year) and a static archive page, then post a Discord embed (headline, 2–3 line summary, map image, audio link).

### Reliability rules (put these in Global Constraints)
- **Idempotent per world year.** A `manifest.json` in R2 records which steps finished for each episode. A rerun skips completed steps and **never posts twice** or pays for TTS twice.
- **One run at a time.** Use a GitHub `concurrency` group so two runs can never touch the world file together.
- **Back up before writing.** Keep dated copies of `world.fw.db` in R2 (for example the last 30 days plus monthly).
- **Fail safe.** If the LLM fails, use the template script. If TTS fails, publish text and map only, mark audio as missing in the manifest, and retry TTS next run. If the sim fails, publish nothing and exit non-zero.
- **Spend caps.** Keep the existing LLM monthly cap. Add a TTS character budget per month in config, and stop calling ElevenLabs once it's spent.
- **Long gaps.** If the job didn't run for a while, catch-up is capped at 30 world years. Make one episode summarizing the gap rather than a flood of episodes.
- **Scheduled-workflow keep-alive.** GitHub disables scheduled workflows in public repos after 60 days without repo activity. Either keep the repo private or have the job commit a small log monthly.

### Secrets (GitHub Actions secrets)
`ANTHROPIC_API_KEY`, `ELEVENLABS_API_KEY`, `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`, `DISCORD_WEBHOOK_URL`, and later `YOUTUBE_CLIENT_ID` / `YOUTUBE_CLIENT_SECRET` / `YOUTUBE_REFRESH_TOKEN`. Non-secret config (voice id, language, budgets, public base URL) lives in a checked-in `broadcast.json`.

## 5. Milestones

| # | Milestone | Done when |
|---|---|---|
| M1 | **Cloud world:** workflow pulls/backs up/pushes the world file via R2, runs `catchup` daily, posts a text-only digest to Discord | Runs on schedule for 3 days with no PC involved; reruns don't double-post |
| M2 | **Episode:** `fw episode`, broadcast marker, Sonnet script with guard and template fallback, map PNG | Script reads well, invents no facts (guard + tests), and the template fallback works with no API key |
| M3 | **Narrator:** ElevenLabs audio, pronunciation hints, podcast RSS + archive page on R2 | Feed validates in a podcast validator; names are pronounced consistently; budget cap stops calls |
| M4 | **Video (optional):** mp4 from audio + map (+ Godot shots if feasible), YouTube upload | One automated upload per day; failures don't block the other channels |

## 6. Testing
- Unit tests (xUnit, as now) for: broadcast marker, episode selection and gap summary, script template fallback, guard on episode scripts, RSS generation, manifest idempotency, TTS chunking.
- **No real network calls in the test suite.** Use fakes for ElevenLabs, R2, Discord and YouTube, like the existing counting provider for LLMs.
- A `workflow_dispatch` trigger with a `dry-run` input that runs every step but publishes to a test Discord channel and a `staging/` prefix in R2.

## 7. Out of scope
- Accounts, comments, voting and other community features (later: letting followers vote on which notable the chronicle follows).
- Running anything on the owner's PC.
- Changing the simulation rules. This sub-project only reads the world and narrates it.

## 8. First steps
1. Read `README.md`, the narration-polish spec and plan, `CatchUp.cs`, `CliApp.Chronicle.cs` and `ci.yml`.
2. Write the spec and plan for this sub-project, including the open decisions in §3 with your proposed defaults.
3. After review, build M1 first and confirm three unattended daily runs before starting M2.

Commit messages end with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
