# Fantasy World — Daily Broadcast Design (sub-project 6)

**Status:** draft for review · **Brief:** the owner's "Handoff: Daily Broadcast" · **Builds on:** the sim core (Plans 1–6),
the chronicle views (sub-project 2) and narration polish (sub-project 5)

## 1. Purpose

Turn the world into a daily narrated show. Every morning the world lives one more year, a narrator reads what happened
and why, and the episode is published for followers. It runs unattended in GitHub Actions; the world file lives in
Cloudflare R2. Nothing runs on the owner's computer.

Non-goals: changing the simulation's rules (this sub-project only reads the world and tells it), community features,
and anything on the owner's computer.

## 2. Decisions

### 2.1 Made by the owner

| Topic | Decision |
|---|---|
| Runner | GitHub Actions, scheduled daily |
| Storage | Cloudflare R2 for the world file, backups and published media |
| Script writer | Claude Sonnet (`claude-sonnet-5-5`) on the `narrate` tier |
| Narrator voice | ElevenLabs text-to-speech |
| Hosting | Nothing on the owner's computer; fully automated |

### 2.2 Proposed defaults (flagged for review)

| Topic | Proposed default | Why |
|---|---|---|
| Language | English (`"language": "en"` in config) | The template voice and `English` are English-only; Thai needs a translation step and a Thai-capable voice later |
| Episode length | 2–4 minutes (a script budget of 1,800–3,600 characters) | A morning listen |
| Episode key | The broadcast date in Asia/Bangkok (`2026-10-27`), not the world year | A rerun hours later advances the world and can cross into a new year; keying by year would publish twice. The world years covered are recorded in the episode, and the RSS GUID combines the world's id and the date |
| Publish time | 05:30 Asia/Bangkok (`30 22 * * *` UTC) | Owner's suggestion; GitHub may start late under load, which is fine |
| ElevenLabs model | `eleven_multilingual_v2` (long-form stability, 10,000 characters a request), fallback `eleven_flash_v2_5` (cheaper, 40,000) | From ElevenLabs' models page at the time of writing; both are config values, and chunking reads the limit from config, not from code |
| Pronunciation | Respelling ("alias") rules for invented names, sent as a pronunciation dictionary | ElevenLabs documents alias rules for all models; phoneme rules are model-specific |
| Voice | Owner's choice (`voiceId` in config); the chronicler's temperament is printed beside it as a hint | |
| Channels (v1) | Discord webhook, podcast RSS feed, archive page | YouTube is milestone 4 |
| New worlds | Created only by a manual run with `create: true` | If R2 is unreachable or a key is wrong, a scheduled run must fail, never quietly start a new world |
| Keep-alive | A `keepAlive` config switch: when on, the job commits a one-line heartbeat on the 1st of each month | GitHub turns off scheduled workflows in public repositories after 60 days without activity; a private repository needs no heartbeat |

## 3. Architecture

```
GitHub Actions  broadcast.yml (daily cron + manual run; concurrency group "broadcast", never cancels a run in progress)
  └─ dotnet run --project sim/FantasyWorld.Broadcast -- run [--dry-run] [--create]
       1. read broadcast.json and manifest.json (R2)
       2. pull world.fw.db from R2; copy it to backups/<date>.fw.db first; prune old backups
       3. catch up (the sim's CatchUp, no digest narration, the owner's reading place untouched)
       4. build the episode from events since the broadcast marker      → episode.json, script.txt
       5. push the world and the episode to R2; mark "episode" done      (only after 3–4 succeeded)
       6. (M2) map.png   7. (M3) narration.mp3, feed.xml, archive page   8. post to Discord
          each step marked in the manifest as it finishes; a rerun skips finished steps
```

- **`FantasyWorld.Broadcast`** (new .NET 8 console project) holds every network call and secret: the R2 store, ElevenLabs,
  Discord, and the orchestration. It references `FantasyWorld.Sim` (and `FantasyWorld.Llm` for the narrate tier), so the
  daily run is one process with one exit code. The simulation stays offline and secret-free.
- **Storage** is behind an `IObjectStore` (get, put, copy, list, delete). The R2 implementation uses the S3 API
  (`https://<account>.r2.cloudflarestorage.com`, region `auto`); a local-directory implementation serves tests and
  local dry runs.
- **The world file** is pulled to a temporary path, used, then uploaded as a consistent single-file copy made with SQLite's
  online backup (`WorldStore.BackupTo`), never as a live WAL database.

### 3.1 The broadcast marker

`World.LastBroadcastEventId` (kept in the world's `Markers`, so no schema change) is where the audience left off. It is
separate from `LastSeenEventId`: the owner reading a digest must not change what the audience hears. The marker moves only
when an episode is built, in the same save as the world it describes.

### 3.2 Episodes

`Episode.Build(world, log, since, date)` (in the Sim, so the CLI and the broadcast share it) selects from events since the
marker using the digest's ranking:

- **Cold open:** the headline's title, and when more than a year passed, the gap ("Three years have passed in Aerenmoor").
- **Stories:** the next 3–6 events by importance, one sentence each, the headline told in full.
- **Why:** the headline's chain of causes told as a story (the why screen's story of why), if it has causes.
- **Sign-off:** a line from the chronicler.

The episode is data (`episode.json`: date, world, years covered, gap, headline, stories, why, event ids) plus a script
(`script.txt`). M1 writes the template script; M2 asks the LLM for the script (chronicler persona and house style, a
journaled `narrate|episode|<date>` call, the guard on the result) and falls back to the template script when the LLM
fails, is over its cap or is rejected. An episode always ships.

`fw episode <world> --out <dir> [--date <yyyy-mm-dd>]` writes the same files from the command line.

### 3.3 Manifest and idempotency

`manifest.json` in R2 records, per episode date, which steps finished (`episode`, `map`, `audio`, `feed`, `discord`, …)
and when. A step is marked as soon as it finishes and the manifest is written straight back, so a rerun skips it: a
rerun never posts to Discord twice or pays for speech twice. A failed step stays unmarked and is retried by the next run.

### 3.4 Reliability rules

- **One run at a time:** the workflow's `concurrency: broadcast` group, `cancel-in-progress: false`.
- **Back up before writing:** `backups/<date>.fw.db` before anything changes; keep the last 30 days and the first backup
  of each month.
- **Fail safe:** the LLM failing → the template script; speech failing → publish text and map, mark audio missing, retry
  next run; the simulation failing → publish nothing, exit non-zero.
- **Spend caps:** the LLM's existing monthly cap; a monthly ElevenLabs character budget in config, counted in the manifest;
  no call once it is spent.
- **Long gaps:** catch-up is capped at 30 world years a run; one episode tells the gap.
- **Dry run:** `--dry-run` (and the manual trigger's `dry-run` input) does every step against the `staging/` prefix and the
  test Discord webhook (`DISCORD_TEST_WEBHOOK_URL`).

### 3.5 Secrets and config

Secrets (GitHub Actions): `ANTHROPIC_API_KEY`, `ELEVENLABS_API_KEY`, `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`,
`R2_SECRET_ACCESS_KEY`, `R2_BUCKET`, `DISCORD_WEBHOOK_URL`, `DISCORD_TEST_WEBHOOK_URL`, later the YouTube three.
Non-secret config lives in the checked-in `broadcast.json`: world key and seed, timezone, language, voice id and models,
budgets, public base URL, keep-alive. No secret is ever written to R2, the world file, logs or the repository.

## 4. Milestones

| # | Milestone | Done when |
|---|---|---|
| M1 | Cloud world: R2 pull/backup/push, daily catch-up, the episode (template script), a text post to Discord | Three scheduled runs in a row with nothing on the owner's computer; a rerun the same day does not post again |
| M2 | Episode script by Sonnet with guard and template fallback; the political map as PNG (rendered in .NET from the map view) | The script reads well and invents nothing (guard and tests); with no API key the template episode ships |
| M3 | Narrator: ElevenLabs audio in chunks under the model's limit joined with ffmpeg, pronunciation dictionary from world names, podcast RSS and archive page on R2 | The feed validates; names sound the same every day; the budget stops calls |
| M4 | Video (optional): mp4 from audio and map, YouTube upload | One upload a day; a failure blocks no other channel |

M1 is built first and confirmed with three unattended daily runs before M2 starts. M2–M4 each get their own plan.

## 5. Testing

xUnit as now, with no real network calls: fakes for R2 (a local-directory store), Discord, ElevenLabs and YouTube (a
counting fake, like the LLM tests'). Covered: the broadcast marker, episode selection and gap telling, the template script,
manifest idempotency (a rerun posts nothing), backup retention, the fail-safe order (nothing pushed when the sim fails),
and later the guard on episode scripts, RSS generation and speech chunking. The workflow itself is exercised with a manual
dry run.

## 6. Owner setup (once)

1. Create an R2 bucket and an API token with read/write on it; add the R2 and Discord secrets to the repository.
2. Choose a voice in ElevenLabs (M3) and put its id in `broadcast.json`.
3. Merge to the default branch (scheduled workflows run only from it), then start the workflow by hand with `create: true`
   (and `dry-run: true` first, if wanted).
