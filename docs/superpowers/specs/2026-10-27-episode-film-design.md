# Fantasy World — Episode Film Design (sub-project 7)

**Status:** draft for review · **Asked by:** the owner, 2026-10-27 ("make the video into sections, video + narrated
action … video of the diorama, focus + narrated … character focus") · **Builds on:** the daily broadcast (sub-project 6,
M1–M4), the Godot diorama client (`game/`) and its chronicle views

## 1. Purpose

Today each episode's video is the political map as a still image with the narration over it. This sub-project makes it a
short film of the world: the camera flies over the diorama to where each story happened, the diorama shows what happened
there (hosts marching, sieges, banners changing hands), the narrator tells it, and one notable character gets a
spotlight with their portrait. The film is cut into sections that appear as YouTube chapters.

Non-goals: changing the simulation, interactive features, music, and anything on the owner's computer.

## 2. Decisions

### 2.1 Made by the owner (2026-10-27)

| Topic | Decision |
|---|---|
| Sections | One per story: an opening flyover, the headline, each other story, "how it came to this", a character spotlight, the closing. Each is a YouTube chapter |
| Action | The camera flies to each event's place and the diorama shows it with its live markers (hosts, sieges, ceded regions, fallen towns), with a caption naming who and where |
| Character focus | A spotlight section: one notable character per episode, the camera at their home and an on-screen card (portrait, name, house banner, age, traits, deeds this episode, kin) while the narrator tells their story |
| Portraits | Only for characters worth mentioning (those named in the episode's sections), from the owner's image API (§5) |
| Length | 3–5 minutes (about three times today's narration) |

### 2.2 Proposed defaults (flagged for review)

| Topic | Proposed default | Why |
|---|---|---|
| Resolution / frame rate | 1280×720 at 24 fps | Software rendering on GitHub's runners (no GPU); YouTube serves 720p well. Raised if the spike (§7) shows room |
| Renderer | Godot 4.3 (the game's own engine) on a virtual display (`xvfb-run`, OpenGL compatibility renderer, `--write-movie`) | The diorama, markers and banners already exist there; nothing is drawn twice |
| Fallback when Godot cannot render | The film is made from still frames of the political map per section with slow pans (ffmpeg), and the run goes on | A failed render never holds up the episode, Discord or the narration |
| Portrait store | `portraits/char-<id>.png` at the bucket root, made once and kept forever | A character looks the same in every episode; each portrait is paid for once |
| Portraits per day | At most 4 new | Bounds the image API's cost |
| Speech budget | Sections are narrated one by one; the monthly character budget (150k) and the reuse of stored speech stay as they are | ~3,500 characters a day is ~105k a month |

## 3. The film, section by section

Every section has narration, a camera plan, markers and a caption. Durations come from the narration (measured after
speech), plus a short lead-in and hold.

| Section | Narration | Camera | Markers / overlay |
|---|---|---|---|
| Opening | The episode's opening line ("The chronicle of … in Year …") | Slow orbit over the whole diorama | Realm banners; title card (world, years) |
| Headline | The headline told in full | Flies to the headline's place; settles at town or region zoom | That event's markers (§4); caption: who, where |
| Other stories (one each) | Each story briefly | Short flight from the previous place | That event's markers; caption |
| How it came to this | The chain of causes, earliest first (when the brief tells one) | Hops from cause to cause in order | Each cause's place pinned as it is told |
| Spotlight | The spotlighted character's story this episode | Their home settlement, close | Character card (§5) |
| Closing | The chronicler's closing line | Pulls back to the whole diorama | Chronicler's name; "until we meet again" card |

The YouTube description lists the sections with their start times (`0:00 Opening`, `0:12 The fall of …`), which YouTube
turns into chapters. The Discord post keeps its link (sub-project 6) and gains the chapter list.

## 4. Narrated action on the diorama

Each story is anchored to its event's place (`EventDraft.Place`, or the region of its main participant). The film shows
the diorama as it stood at that moment of the episode and uses the markers the game already draws:

- **War events** (battles, sieges, hosts raised or marching): the hosts on their roads (`WarLayer`), the siege ring.
- **Regions ceded / realms fallen**: the regions blink, then take the new owner's colour.
- **Deaths, births, marriages, duels**: a pin on the settlement with the person's name.
- **Artifacts, monsters, structures**: their existing map markers, highlighted.

The camera moves are eased flights (no cuts), so the viewer follows the geography from story to story.

## 5. Character spotlight and portraits

**Who is spotlighted:** the notable character most present in the episode's sections (the headline's main character
first; ties go to the living, then the higher-ranked). If none, the section is skipped.

**Who gets a portrait:** every character named in a section's caption or the spotlight, if they have none yet, up to 4 new
per day (spotlight first). A character without a portrait shows their house banner instead.

**The portrait prompt** is built from the character's facts only (sex, age, culture, appearance traits, profession, title,
faith, house colours) in one fixed art style kept in `broadcast.json`, so portraits across the world look alike. Names and
invented words enter the prompt only as data. A refused or failed image is retried on a later day; it never fails the
run.

**The card:** portrait, name, house banner, age (or "died aged …"), traits, ambition, this episode's deeds, closest kin.

**The image API (the owner's, 2026-10-27):** the owner's Cloudflare Worker `flux-playground` (FLUX.2 [klein] 4B on Workers
AI), `POST https://flux-playground.tanwa-tharathep.workers.dev/generate`, key in the header `x-access-key` from the
repository secret `FLUX_ACCESS_KEY`. JSON request `{ prompt, scenario: "custom", style: "hand-painted", width: 768,
height: 768, transparent: true, seed }`; JSON response `{ image: <base64>, mime, … }`; 429 when its daily budget (9,500
neurons, reset 00:00 UTC) is used, which the broadcast treats as "try tomorrow". The prompt asks for a head-and-shoulders
portrait (the `character` scenario frames a full figure, too small for a card); the seed is the character's id, so a
portrait made again looks the same. A 768×768 portrait costs about 105 neurons, so 4 a day is about 420. The endpoint,
style and size live in `broadcast.json` (`portraits`), so the API can be swapped through one small interface
(`IPortraitMaker.MakeAsync(prompt, seed) → PNG`).

## 6. Making the film

1. **Script (Sonnet):** the episode script becomes a list of sections, each with its narration, written by the chronicler
   and held by the guard to that section's own brief (as today, per section). A section that fails the guard falls back to
   the template's text for that section only.
2. **Shot list (rules, no LLM):** for each section, the camera target, zoom, markers, caption and card, as JSON
   (`episodes/<date>/shots.json`).
3. **Speech:** each section is narrated on its own (ElevenLabs, with the respellings); its length is measured from the MP3.
   Stored speech is reused, as today.
4. **Portraits:** made or reused (§5).
5. **Render:** Godot loads the day's world snapshot and the shot list, plays it at a fixed frame rate and writes the frames
   (`Film.tscn`, run under `xvfb-run`).
6. **Compose (ffmpeg):** the frames and the section narrations into one MP4 (H.264 + AAC), with the chapter list.
7. **Publish:** YouTube (chapters in the description), then Discord with the link (as today).

Every step after the script is optional to the run: if the render fails, the still-map film (today's) is made instead; if
portraits fail, banners stand in; if speech fails, the episode goes out as text and map, as today.

## 7. Risks and the spike that comes first

- **Render speed on GitHub's runners.** Software OpenGL may draw a frame in tenths of a second or in seconds. A 4-minute
  film at 24 fps is ~5,800 frames. **Spike (before any plan):** a `film-spike` workflow (dispatch only) installs Godot as
  CI already does, renders 10 seconds of the existing `Showcase` angles under `xvfb-run` with `--write-movie`, and reports
  seconds per frame and the output size. If too slow: lower frame rate (12 fps with motion blur off still reads as a slow
  flight), lower resolution, or render one still per camera position and let ffmpeg pan between them.
- **Godot in the broadcast job.** Installing Godot adds ~1 minute; the game's assets need `--import` (~1–2 minutes). The job
  limit is 45 minutes; the render gets a fixed budget (20 minutes) and is cut off past it, falling back to the still film.
- **Portrait consistency and content.** The fixed style and fact-only prompts keep portraits in one look; the API's own
  safety refusals are treated as "no portrait yet".

## 8. Milestones

- **F1 — Sections and the still film:** sectioned script and narration, chapters on YouTube and Discord, the film made of
  per-section stills of the political map with slow pans (no Godot). Proves timing, chapters and cost.
- **F2 — The diorama film:** the spike, then `Film.tscn` rendering the shot list on the runner; camera flights and markers.
- **F3 — Spotlight and portraits:** the spotlight pick, the card, portraits from the owner's API (§5).

Each milestone is planned, reviewed and executed as before, and is live only after a dry run on the runner.
