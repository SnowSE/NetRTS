<h1 align="center">🦡 Badger Brawl</h1>

<p align="center"><strong>Write the bot. Command the badgers. Watch them brawl.</strong><br>
<sub>A Snow College bot-programming game · Ephraim, Utah · Go Badgers!</sub></p>

<p align="center">
  <a href="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml"><img src="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml/badge.svg" alt="CI status"></a>
  <a href="https://hub.docker.com/r/snowcollege/netrts"><img src="https://img.shields.io/docker/v/snowcollege/netrts?label=docker&sort=date" alt="Docker image"></a>
</p>

<p align="center">
  <img src="docs/images/battle.gif" width="520" alt="An 825-tick brawl replayed at speed: the orange Grub Hoarder digs in at the top-left, the Blue Badger at the bottom-right, and their badgers trade waves across the middle of the snowfield until orange breaks through.">
</p>

**Badger Brawl is a real-time strategy game where nobody touches a mouse.** You write a program,
in any language that speaks HTTP, and it runs a clan of badgers: digging up grubs, burrowing new
dens, cramming in the Lecture Hall for sharper claws, sniffing through the blizzard and sending
brawlers at the rival sett. Every second the world ticks forward; your bot sees what its badgers
can see and decides what to do next. Then you sit back and watch it win (or work out why it
didn't).

Built by and for Snow College software engineering students, for programming competitions,
classrooms and anyone who has ever wanted to settle "my strategy is better than yours" with code.

## Your code is the head badger

Orders are plain JSON, and they say what you mean. The API uses classic RTS names (a Digger is a
`Worker`, a Den is a `Barracks`), so every bot library and tutorial still applies:

```jsonc
{ "commands": [
  { "type": "Gather",  "units": "all", "unitType": "Worker", "targetId": 4 },          // diggers, go dig up those grubs
  { "type": "Build",   "unitIds": [31], "buildingType": "Barracks", "x": 12, "y": 9 }, // dig a den here
  { "type": "Produce", "buildingId": 77, "unitType": "Soldier", "count": 3 },          // three brawlers, please
  { "type": "Attack",  "units": "5-40", "x": 56, "y": 56 }                             // everyone: charge!
] }
```

## Meet the clan

| On the field | In the API | What it does |
|---|---|---|
| 🦡 **Digger** | `Worker` | Digs grubs, builds burrows. Claws for every job. |
| 🥊 **Brawler** | `Soldier` | Tough, close-up, and doesn't care. |
| ❄️ **Snowballer** | `Archer` | Hurls snowballs from four tiles away. |
| 👃 **Sniffer** | `Scout` | Twice as fast, and smells trouble furthest off. |
| 🏔️ **Sett** | `CommandCenter` | The home burrow. Lose your last one and you're out. |
| 🕳️ **Den** | `Barracks` | Where brawlers, snowballers and sniffers come from. |
| 🧺 **Larder** | `ResourceDepot` | A closer place to drop grubs, and more room to store them. |
| 🎓 **Lecture Hall** | `TechLab` | Badgers study here: Sharper Claws, Thicker Fur, Snowshoes and a Digging Degree. |
| ⛄ **Snow Fort** | `GuardTower` | Pelts anything that wanders too close. |

Four kinds of badger, five burrows, eight upgrades, and a rule set small enough to learn in an
afternoon, with plenty of strategy hiding in it: rush early, out-dig the opponent, turtle behind
snow forts, out-study them, or sniff out their sett and strike where they're weak.

## Zoom into the brawl

<p align="center">
  <img src="docs/images/close-up.gif" width="640" alt="A close-up on the blue sett at the end of the brawl: the orange badgers march in and flatten the den, the lecture hall and finally the sett.">
</p>

The live spectator page streams every brawl tick by tick. Scroll or pinch to zoom, and the map
switches from a strategic overview to an illustrated close-up on a snowy Sanpete field: claw-marked
diggers, striped badger brawlers, snowballs in flight, sniffer paw prints, setts flying the Snow
pennant, mortarboard lecture halls and snow forts, with health bars and dig progress. Hit
**Follow the brawl** and the camera chases the biggest scuffle on its own. It fills whatever screen
you give it, from a phone to a wall-sized monitor, in Snow College blue by day or by night.

<p align="center">
  <img src="docs/images/spectator.png" width="860" alt="The spectator page mid-brawl: the map on the left, each badger's grubs, income, score breakdown and upgrades on the right, and the play-by-play underneath.">
</p>

## A blizzard that's actually fair

<img src="docs/images/fog-of-war.png" width="300" align="right" alt="The same brawl through the blue badger's eyes: only the area around its own sett is clear; the orange clan waits just beyond the edge of the blizzard.">

Your bot sees only what its badgers see. The server enforces this; it isn't a polite request to
the client. Enemy orders are never revealed, ids of unseen badgers can't be probed, and burrows you
sniffed out earlier are remembered for you. Maps are generated from a seed with mirror symmetry, so
every starting sett is exactly as good as every other.

Spectators can flip to any player's view to see the brawl the way that bot saw it.

<br clear="right">

## Every brawl ends with a box score

<p align="center">
  <img src="docs/images/victory.png" width="860" alt="The end of the brawl: a winner banner, 825 ticks, each side's badgers trained, lost and bowled over, the final score breakdown and the play-by-play.">
</p>

Flatten the last enemy Sett, or have the higher score when the final buzzer sounds. Results break
down into destruction, economy and survival, feed an Elo ladder of Top Badgers, and come with a
full replay: the engine is deterministic, so a seed plus the command log reproduces any brawl
exactly.

## Four house badgers to scrimmage

| House badger | API name | Plays like |
|---|---|---|
| **Sleepy Sitter** | `sitter` | Digs grubs and never fights back. Your first win. |
| **Honey Badger** | `rusher` | Doesn't care. Two dens, brawlers early, charges before you're ready. |
| **Blue Badger** | `balanced` | Steady digging, mixed clan, upgrades, attacks in waves. |
| **Grub Hoarder** | `economist` | Out-digs everyone, hides behind snow forts, arrives late with an enormous clan. |

Beat the Sleepy Sitter in your first hour. Beating the Grub Hoarder takes a real plan.

## An API that documents itself

<p align="center">
  <img src="docs/images/api-playground.png" width="860" alt="The Scalar API reference open at the state endpoint: parameters, every response code, a ready-to-run C# sample and the full JSON response shape.">
</p>

Every endpoint, schema and error code is browsable at `/scalar`, with ready-made code samples and
a "Test Request" button that uses your API key. The docs walk you from first request to a bot that
wins, in **C# or Python**: pick a language on any example and the rest follow.

## Start in five minutes

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/SnowSE/NetRTS && cd NetRTS
dotnet run --project src/NetRts.Server
```

Or skip the SDK and run the image:

```bash
docker run -p 8080:8080 -v badger-data:/home/app snowcollege/netrts
```

Open **http://localhost:5080** (or :8080 for Docker) and start a scrimmage between two house
badgers. Then send your own bot into the snow from another terminal:

```bash
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name eph-the-badger --strategy rusher --vs sitter
python samples/python/bot.py --server http://localhost:5080 --name my-python-badger
```

With no database configured the server keeps players and results in a local SQLite file. With
Docker installed, `aspire run` starts it against PostgreSQL with the Aspire dashboard, and the same
setup deploys to Azure (App Service plus a private PostgreSQL) with `azd up`.

<p align="center">
  <img src="docs/images/home.png" width="760" alt="The home page: start a scrimmage between two house badgers, watch live brawls, recent box scores and the Top Badgers ladder.">
</p>

## Documentation

| For | Read |
|---|---|
| New badgers, first five minutes | [docs/getting-started.md](docs/getting-started.md): one page |
| New badgers, first bot | [docs/tutorial/](docs/tutorial/README.md): step by step in C# or Python, with screenshots |
| Reference (and the field guide of badger names) | [docs/bot-guide.md](docs/bot-guide.md): every command, rule, stat and error code |
| Teaching / contributors | [docs/how-it-works.md](docs/how-it-works.md): guided tour of the back end, with exercises |
| Hosting | [docs/deploying.md](docs/deploying.md): local Aspire and Azure deployment |
| Maintainers | [docs/architecture.md](docs/architecture.md), [docs/requirements-coverage.md](docs/requirements-coverage.md) |

## Under the burrow

The code still lives under its original `NetRts` project names.

| Path | What it is |
|---|---|
| `src/NetRts.Engine` | The game: map generation, rules, pathfinding, the tick simulation, fog-of-war views, replays. Pure, deterministic C#. |
| `src/NetRts.Protocol` | JSON contract shared by the server, SDK and bots. |
| `src/NetRts.Server` | ASP.NET Core host: API keys, matchmaking, tick loops, long-poll state, live spectator stream, Elo, the spectator site. |
| `src/NetRts.Server/wwwroot/lore.js` | The badger vocabulary: every name the spectator site shows. |
| `src/NetRts.Bots` | C# SDK (`NetRtsClient`, `BotLoop`) and the house badgers. |
| `src/NetRts.BotRunner` | Command-line runner for any house-badger strategy. |
| `src/NetRts.AppHost`, `src/NetRts.ServiceDefaults` | .NET Aspire: local orchestration, Azure deployment, telemetry. |
| `samples/python` | Standalone Python reference bot. |
| `tests/` | Engine and API tests, including whole bot-vs-bot brawls and replay determinism. |
| `specs/001-rts-game-engine` | The original specification; [docs/requirements-coverage.md](docs/requirements-coverage.md) maps it to the code. |

```bash
dotnet build
dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) to join the clan.

## License

MIT. Go Badgers! 🦡
