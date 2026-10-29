# Daily broadcast: owner setup

The broadcast runs in GitHub Actions (`.github/workflows/broadcast.yml`) every morning at 05:30 Bangkok time. The world
lives in Cloudflare R2, and each episode is posted to Discord. Nothing runs on your computer. You set it up once.

## 1. Cloudflare R2

1. In the Cloudflare dashboard, open **R2** and create a bucket (for example `fantasy-world`). Keep it private: the
   world file and its backups are not for the public.
2. Under **R2 → Manage API tokens**, create a token with **Object Read & Write** on that bucket only. Note the
   **Access Key ID** and **Secret Access Key** (shown once), and your **Account ID** (on the R2 overview page).

## 2. Discord

In your server: **Channel settings → Integrations → Webhooks → New webhook**, then **Copy webhook URL**. Make one for the
real channel, and (recommended) one for a private test channel used by dry runs.

## 3. GitHub secrets

In the repository: **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|---|---|
| `R2_ACCOUNT_ID` | Cloudflare account id |
| `R2_BUCKET` | the bucket's name |
| `R2_ACCESS_KEY_ID` | the token's access key id |
| `R2_SECRET_ACCESS_KEY` | the token's secret access key |
| `DISCORD_WEBHOOK_URL` | the real channel's webhook |
| `DISCORD_TEST_WEBHOOK_URL` | the test channel's webhook (optional; without it a dry run posts nowhere) |
| `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET`, `YOUTUBE_REFRESH_TOKEN` | optional: the daily video upload (see section 5) |
| `ELEVENLABS_API_KEY` | from elevenlabs.io → your profile → API Keys, with the permissions **Text to Speech** and **Pronunciation Dictionaries: Write** (without the second, episodes are spoken without the name respellings); the narrator reads each episode with it (without it, the post goes out as text and map) |
| `ANTHROPIC_API_KEY` | from console.anthropic.com → API Keys; the chronicler writes each script with it (without it, the template script ships) |

Secrets are only ever read from the environment. They are never written to R2, the world file, the logs or the repository.

## 4. The podcast feed and archive page (optional)

The feed (`feed.xml`) and the archive page (`index.html`) are published only once the bucket can be read from the web:

1. Cloudflare → R2 → your bucket → **Settings → Public access → R2.dev subdomain → Allow** (or connect a custom domain).
2. Copy the public address (for example `https://pub-1234abcd.r2.dev`) into `broadcast.json` as `"publicBaseUrl"` (and your
   address as `"podcastEmail"`, if you will list the podcast in Apple Podcasts or Spotify), and merge.
3. The next morning, add `<address>/live/feed.xml` to a podcast app, or open `<address>/live/index.html`.

Everything in the bucket becomes readable by anyone with the address: the episodes, and also the world file and its
backups. Nothing secret is stored there, but if you would rather keep the world private, leave this off.

## 5. YouTube (optional)

Each morning's map and narration can also go to YouTube as a video. It needs three more secrets:

1. In the Google Cloud console, create a project and enable the **YouTube Data API v3**.
2. Set up the **OAuth consent screen** (External), add yourself as a test user, then **Publish app** (to production).
   While the app is in testing, Google lets a refresh token live only seven days, and the upload stops after a week.
3. Under **Credentials**, create an **OAuth client ID** of type *Web application*, with the authorised redirect URI
   `https://developers.google.com/oauthplayground`. Note its client id and secret.
4. Get a refresh token for the channel's Google account in the OAuth 2.0 Playground: gear icon → *Use your own OAuth
   credentials* (paste the id and secret), select the scope `https://www.googleapis.com/auth/youtube.upload`, *Authorize
   APIs*, sign in as the channel, then *Exchange authorization code for tokens* and copy the refresh token.
5. Add the secrets `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET` and `YOUTUBE_REFRESH_TOKEN`.

YouTube keeps videos uploaded through a Google app that has not passed its API audit **private**, whatever `privacy`
says (the run log warns when that happens); request the audit (the YouTube API Services form) to publish them.

Each video gets a thumbnail (the frame where the headline's picture is on screen, with the year and headline across its
foot, stored as `episodes/<date>/thumbnail.jpg`) and tags (fantasy, the world's name, and the people and realms who
face off in it). YouTube takes custom thumbnails only from a **verified channel**: verify it once by phone at
youtube.com/verify. Until then the run log warns and YouTube picks a frame itself; the video still goes up.

One video a run, of the day's episode (or yesterday's, if it was missed): turning YouTube on never uploads a backlog.
An upload is recorded before it starts and finished, never begun again, on a later run if it was cut short, so a video
never goes up twice. Dry runs make and store the video (`staging/episodes/<date>/episode.mp4`) but do not upload it, unless
the run is started with **upload-unlisted** ticked: the staging video then goes up unlisted, a link that can be shared but is not listed on the channel. An
episode without narration waits for it; a failed upload never holds up Discord. The upload comes first each morning, so
the Discord post carries the video's link (and Discord plays it) in place of the narration file, once the video is public.

## 5b. The world in a browser (optional)

Each morning the world can also go to a website where anyone can look around it: the diorama, the realms, the people,
any past year and the story behind any event (the same code as the game, run in the browser). It is hosted on
Cloudflare Pages:

1. In the Cloudflare dashboard (the account that holds the R2 bucket): **My Profile → API Tokens → Create Token →
   Custom token**, with the permission **Account · Cloudflare Pages · Edit** for your account. Copy the token.
2. Add it as the repository secret `CLOUDFLARE_API_TOKEN` (GitHub → Settings → Secrets and variables → Actions). Paste it
   there only, never anywhere else. The account id is the one `R2_ACCOUNT_ID` already holds.
3. Run the workflow once as a dry run. The **site** job creates the Pages project `site.project`
   (`fantasy-world-chronicle` by default; pages.dev names are shared by everyone, so pick another if it is taken) and
   deploys it to the project's `staging` preview; its last step prints the address.
4. Put the public address in `broadcast.json` as `site.url` (e.g. `https://fantasy-world-chronicle.pages.dev`). From then
   on the YouTube description and the Discord post link to the world, opened on the headline's story.

Dry runs deploy to the `staging` preview (`https://staging.<project>.pages.dev`), so the public site changes only with a
live broadcast. Without the token the site job is skipped; a failure in it never holds up the broadcast.

## 5c. Face-off images (optional)

When a duel or a war is declared, the film shows a face-off: the map blurs behind a card of both sides' figures, with a
full-figure portrait of each duellist, or each realm's war banner, standing either side. The images come from your FLUX
image API (`images.baseUrl`) and are made once, then kept in the bucket (`live/images/`) and reused whenever that person
or realm is shown again; a person is drawn anew in each new decade of age and after a major change (a new role, new
scars, new colours), a realm's banner when its rank, colours or government change.

1. Add the API's access key as the repository secret `FLUX_ACCESS_KEY` (GitHub → Settings → Secrets and variables →
   Actions). Paste it there only, never anywhere else.
2. That is all: the next episode with a face-off makes its images. If the API says its day is spent (429), no more are
   asked for that morning and nothing is retried; the card goes out without them.

The same key paints **still images for the storytelling**, four an episode at most: each fight told first (a duel fought,
an army clash), then the scene of other stories. The chronicler (Claude) describes each picture as it writes the script
(no names, never read aloud), or the facts do when it cannot (a fight's clash; otherwise the town and land where it
happened), and the film holds the still over the blurred map while that section is told (for a battle that also tells its
war's moves, the still first, then the moves on the map). Stills are kept as `live/images/scene-<event>.png`.

Without the key, no image is made and the cards show only images already kept.

## 6. Start it

1. Merge to the default branch. GitHub runs scheduled workflows only from there, and a run that is not a dry run is
   refused on any other branch.
2. **Actions → broadcast → Run workflow** with `dry-run` ticked and `create` ticked. This makes a staging world under
   `staging/` and posts to the test channel. Read the episode there.
3. Run it again with `dry-run` unticked and `create` ticked. This begins the real world under `live/` and posts the first
   episode. From then on the schedule takes over; never tick `create` again unless you want to start over after
   deleting `live/world.fw.db`.
4. Watch three mornings in a row. Each should post one episode. Re-running the workflow the same day posts nothing new.

## Settings (`broadcast.json`)

| Field | Default | Meaning |
|---|---|---|
| `worldKey` | `world.fw.db` | the world's name in the bucket, under the prefix |
| `seed`, `size` | `20261027`, `medium` | the world a `create` run begins |
| `timeZone` | `Asia/Bangkok` | episodes are dated in this zone |
| `language` | `en` | only English exists so far |
| `livePrefix`, `stagingPrefix` | `live/`, `staging/` | where real and dry runs keep their files |
| `backupDays` | `30` | daily backups kept; the first backup of each month is kept for good |
| `stories`, `maxCharacters` | `5`, `3600` | episode length (about 2–4 minutes read aloud) |
| `discordName` | `The Chronicle` | the name posts appear under |
| `llm.narrate` | `true` | the chronicler (Claude) writes the script; off, the template script ships |
| `llm.narrateModel` | `claude-sonnet-5-5` | the model that writes it |
| `llm.storyBeatsPerDay` | `120` | the sim's own LLM decisions while it catches up (sub-project 8): the characters' choices and the fights, on Claude Opus; `0` turns them off, so the daily cost is one script (a few cents) |
| `llm.fightBeatsPerDay` | `40` | of the story beats, the ones only fights (duels, battles, hunts, delves) may use, so choices never starve them; past the budget the rules decide as before |
| `voice.speak` | `true` | ElevenLabs reads each episode; the MP3 is attached to the post and stored |
| `voice.voiceId`, `voice.model` | Michael C. Vincent (`uju3wxzG5OhpWcoi3SMy`), `eleven_v4_turbo` | the narrator's voice and model (the owner's choices) |
| `voice.maxCharactersPerRequest` | `5000` | a script longer than this is spoken in parts and joined (a day's script is at most 3,600) |
| `voice.monthlyCharacterBudget` | `150000` | characters spoken per calendar month (about 40 days of episodes); past it, episodes go out without narration |
| `youTube.upload`, `youTube.privacy`, `youTube.categoryId` | `true`, `public`, `24` | the daily video (with the three YouTube secrets); 24 is Entertainment |
| `publicBaseUrl` | unset | the bucket's public address; when set, the podcast feed, the archive page and the cover are published |
| `podcastEmail` | unset | the feed's contact address, which Apple Podcasts and Spotify use to confirm you own the feed |
| `voice.respell` | `true` | Claude respells each new invented name once, so the narrator says it the same way every day |
| `llm.narrationsPerDay`, `llm.monthlyCapUsd` | `10`, `25` | budget and spending cap over 30 days (narration, choices and fights together); past them, the template script ships and the rules decide |
| `storage.maxGigabytes` | `10` | the whole bucket's limit; past 90% of it, old film stretches, dry runs' media, live videos older than a week, the longest-unused face-off images, then old maps and narrations and old backups are deleted until it is at 80%. The world, manifests, feed, scripts and website bundle are never deleted |
| `images.make`, `images.baseUrl`, `images.style` | `true`, the owner's FLUX Worker, `hand-painted` | the face-offs' portraits and banners (with the `FLUX_ACCESS_KEY` secret) |
| `keepAlive` | `true` | a monthly heartbeat commit, so GitHub keeps the schedule of a public repository; turn it off for a private one |

## What is in the bucket

```
live/world.fw.db                       the world (a consistent single-file copy)
live/manifest.json                     per date, which steps finished: a rerun repeats none of them
live/episodes/2026-10-27/episode.json  the episode as data (years, headline, stories, why)
live/episodes/2026-10-27/script.txt    the script as posted
live/episodes/2026-10-27/map.png       the political map that morning, posted as the embed's image
live/episodes/2026-10-27/narration.mp3 the episode read aloud, attached to the post
live/feed.xml, live/index.html         the podcast feed and the archive page (with publicBaseUrl)
live/cover.png                         the latest map, the podcast's cover
live/episodes/2026-10-27/episode.mp4   the video (with YouTube), and youtube.json, where its upload stands
live/backups/2026-10-27.fw.db          the world as it was before that morning's run
live/web/world.json.gz                 the day's world for the website (no LLM journal or settings)
```

## When something goes wrong

- **A red run with "There is no world"**: the bucket is empty or the R2 secrets point somewhere else. A scheduled run never
  begins a new world, so check the secrets.
- **"Discord answered HTTP …"**: the episode was saved and the world moved on. The post is made by the next run, before
  that morning's own (re-run the workflow to post it now); posts missed for up to a week are made up, oldest first. A
  post is never made twice, except when Discord took it but timed out before answering.
- **"could not be narrated"**: the post went out as text and map; the narration is made by the next run (and kept for the
  feed). Check the ElevenLabs key, credits and the voice id. **"speech budget"**: raise `voice.monthlyCharacterBudget`.
- **A run stopped part-way** (a timeout, an R2 hiccup): re-run it. Each step it finished is recorded, and the stored world
  says which day's episode it holds, so nothing is told twice or skipped.
- **The world misbehaves**: copy a file from `live/backups/` over `live/world.fw.db` and leave `live/manifest.json` alone.
  The next run carries on from the restored world. Never delete the manifest: it is what stops a day being posted twice.
