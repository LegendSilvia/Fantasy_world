# Fantasy World

A persistent fantasy world simulation — the world keeps moving while you sleep.
1 real day = 1 world year. You are the chronicler: come back each morning to see who rose, who fell, and *why*.

## Layout

- `sim/` — headless .NET 8 simulation core, tests and the `fw` CLI
- `sim/FantasyWorld.Client/` — the game's engine-free logic (session, terrain, layout), tested with the rest
- `sim/FantasyWorld.Broadcast/` — the daily broadcast: R2 storage, the morning run and Discord (`broadcast.json`)
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

Each world has a chronicler (a name, a temperament and a diction, the same every time the world opens) whose voice
and house style every narration follows. Narration may use only the facts it is given: one that invents a name or a
number is rejected and journaled, and the templates speak instead. The template voice is a product in its own right
(articles, numbers in words, several phrasings per event chosen by its id, headlines with a body that adds what the
title leaves out, and the story of why as one paragraph); its structure is checked on every test run against a real
history and a golden narration.

Live quality evals grade the LLM voice with a judge model on fixed seeds, within a cost cap:

```bash
dotnet run --project sim/FantasyWorld.Cli -- eval narration --cap 2      # events and chains: faithful, clear, voice, varied
dotnet run --project sim/FantasyWorld.Cli -- eval decisions --draws 40   # choices in character; arbitrations against their odds
```

Each run prints the mean scores and saves a report under `evals/`; no call is made once a run has spent its cap (default $2).

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

## The daily broadcast

Every morning at 05:30 Bangkok time, GitHub Actions pulls the world from Cloudflare R2, lets it live the year that passed,
builds the episode (the script written by the world's chronicler, Claude, held to the facts; the template voice when it
cannot), has it read aloud by ElevenLabs, and posts it to Discord with the narration and the political map. A rerun the same day posts nothing new; with a public bucket it also keeps a podcast feed and an archive page. Setup:
[docs/broadcast-setup.md](docs/broadcast-setup.md).

```bash
dotnet run --project sim/FantasyWorld.Cli -- episode worlds/aerenmoor.fw.db --out ep/ --narrate   # episode.json and script.txt since the last one
dotnet run --project sim/FantasyWorld.Broadcast -- run --dry-run --create --local bucket/   # the whole morning, with a local folder for R2
dotnet run --project sim/FantasyWorld.Broadcast -- map worlds/aerenmoor.fw.db map.png     # the map as the broadcast posts it
```
