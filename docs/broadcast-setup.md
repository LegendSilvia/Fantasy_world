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

One video a run, of the day's episode (or yesterday's, if it was missed): turning YouTube on never uploads a backlog.
An upload is recorded before it starts and finished, never begun again, on a later run if it was cut short, so a video
never goes up twice. Dry runs make and store the video (`staging/episodes/<date>/episode.mp4`) but never upload. An
episode without narration waits for it; a failed upload never holds up Discord.

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
| `llm.storyBeatsPerDay` | `0` | the sim's own LLM decisions while it catches up; off by default, so the daily cost is one script (a few cents) |
| `voice.speak` | `true` | ElevenLabs reads each episode; the MP3 is attached to the post and stored |
| `voice.voiceId`, `voice.model` | Michael C. Vincent (`uju3wxzG5OhpWcoi3SMy`), `eleven_v4_turbo` | the narrator's voice and model (the owner's choices) |
| `voice.maxCharactersPerRequest` | `5000` | a script longer than this is spoken in parts and joined (a day's script is at most 3,600) |
| `voice.monthlyCharacterBudget` | `150000` | characters spoken per calendar month (about 40 days of episodes); past it, episodes go out without narration |
| `youTube.upload`, `youTube.privacy`, `youTube.categoryId` | `true`, `public`, `24` | the daily video (with the three YouTube secrets); 24 is Entertainment |
| `publicBaseUrl` | unset | the bucket's public address; when set, the podcast feed, the archive page and the cover are published |
| `podcastEmail` | unset | the feed's contact address, which Apple Podcasts and Spotify use to confirm you own the feed |
| `voice.respell` | `true` | Claude respells each new invented name once, so the narrator says it the same way every day |
| `llm.narrationsPerDay`, `llm.monthlyCapUsd` | `10`, `5` | budget and spending cap over 30 days; past them, the template script ships |
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
