<h1 align="center">🦡 Badger Brawl</h1>

<p align="center"><strong>Write the bot. Coach the badgers. Watch them brawl.</strong><br>
<sub>A Snow College bot-programming game · Ephraim, Utah · Est. 1888 · Go Badgers!</sub></p>

<p align="center">
  <a href="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml"><img src="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml/badge.svg" alt="CI status"></a>
  <a href="https://hub.docker.com/r/snowcollege/netrts"><img src="https://img.shields.io/docker/v/snowcollege/netrts?label=docker&sort=date" alt="Docker image"></a>
</p>

<p align="center">
  <img src="docs/images/battle.gif" width="520" alt="An 825-tick brawl replayed at speed: the orange Egg Hoarder builds up in the top-left, the Blue Badger in the bottom-right, and their teams trade waves across the middle of the valley until orange breaks through.">
</p>

**Badger Brawl is a real-time strategy game where nobody touches a mouse.** You write a program,
in any language that speaks HTTP, and it coaches a team of Snow College Badgers: sending diggers
out for Sunday eggs, putting up Badger Stadium and the Graham Science Center, and running
linebackers and quarterbacks across the Sanpete Valley at the other team's Noyes Building. Every
second the world ticks forward; your bot sees what its badgers can see and calls the next play.
Then you sit back and watch it win (or work out why it didn't).

Built by and for Snow College Software Engineering students, for programming competitions,
classrooms and anyone who has ever wanted to settle "my strategy is better than yours" with code.

## Why eggs? Why Noyes?

Snow College is named for Lorenzo and Erastus Snow, [not the weather](https://snow.edu/offices/administration/historical-sketch.html).
It opened in 1888 as Sanpete Stake Academy, with its first 150 students meeting upstairs in
Ephraim's Co-op Store, and the town helped pay for it by selling its "Sunday eggs". In 1900
Newton E. Noyes went to Salt Lake to ask for help keeping the school open, and the school took the
Snow name; the Noyes Building, the oldest on campus, still carries his. So in Badger Brawl, eggs are
the currency, the Co-op Store is where you drop them off, and every team defends a campus landmark.

## Your code is the head coach

Orders are plain JSON, and they say what you mean. The API uses classic RTS names (a Digger is a
`Worker`, Badger Stadium is a `Barracks`), so every bot library and tutorial still applies:

```jsonc
{ "commands": [
  { "type": "Gather",  "units": "all", "unitType": "Worker", "targetId": 4 },          // diggers, collect those eggs
  { "type": "Build",   "unitIds": [31], "buildingType": "Barracks", "x": 12, "y": 9 }, // put Badger Stadium here
  { "type": "Produce", "buildingId": 77, "unitType": "Soldier", "count": 3 },          // three linebackers, please
  { "type": "Attack",  "units": "5-40", "x": 56, "y": 56 }                             // everyone: blitz!
] }
```

## Meet the team, see the campus

| On the field | In the API | What it does |
|---|---|---|
| 🦡 **Digger** | `Worker` | Gathers eggs and puts up buildings. |
| 🏈 **Linebacker** | `Soldier` | Tough, close-up, and hits hard. |
| 🎯 **Quarterback** | `Archer` | Throws from four tiles away. |
| 💨 **Wide Receiver** | `Scout` | Twice as fast, and sees furthest down the field. |
| 🏛️ **HQ** | `CommandCenter` | Your home landmark: the **Noyes Building**, **Greenwood Student Center**, **Eccles Center** or **Huntsman Library**, by seat. Lose your last one and you're out. |
| 🏟️ **Badger Stadium** | `Barracks` | Where linebackers, quarterbacks and receivers come from. |
| 🧺 **Co-op Store** | `ResourceDepot` | A closer place to drop eggs off, and room to store more. |
| 🧪 **Graham Science Center** | `TechLab` | Research: Strength & Conditioning, Thicker Fur, Track & Field and Ag Science. |
| 🏠 **Snow Hall** | `GuardTower` | A residence hall whose RAs don't let anyone wander in. |

Four positions, five buildings, eight upgrades, and a rule set small enough to learn in an
afternoon, with plenty of strategy hiding in it: blitz early, out-gather the opponent, turtle behind
residence halls, out-study them, or scout their campus and strike where they're weak.

## Zoom into the brawl

<p align="center">
  <img src="docs/images/close-up.gif" width="640" alt="A close-up on the blue campus at the end of the brawl: the orange team marches in and flattens Badger Stadium, the science center and finally the HQ.">
</p>

The live spectator page streams every brawl tick by tick. Scroll or pinch to zoom, and the map
switches from a strategic overview to an illustrated close-up of the Sanpete Valley: claw-marked
diggers, striped badger linebackers, footballs, receivers' paw prints, campus halls flying a
pennant, the badger over Badger Stadium, science flasks and brick residence halls, with health bars
and build progress. Hit **Follow the brawl** and the camera chases the biggest pile-up on its own.
It fills whatever screen you give it, from a phone to a wall-sized monitor, in Snow College blue,
white and orange, light or dark.

<p align="center">
  <img src="docs/images/spectator.png" width="860" alt="The spectator page mid-brawl: the map on the left, each team's eggs, income, home landmark, score breakdown and upgrades on the right, and the play-by-play underneath.">
</p>

## Fog of war that's actually fair

<img src="docs/images/fog-of-war.png" width="300" align="right" alt="The same brawl through the blue team's eyes: only the area around its own campus is lit; the orange team waits just beyond the edge of its sight.">

Your bot sees only what its badgers see. The server enforces this; it isn't a polite request to
the client. Enemy orders are never revealed, ids of unseen badgers can't be probed, and buildings
you scouted earlier are remembered for you. Maps are generated from a seed with mirror symmetry, so
every starting campus is exactly as good as every other.

Spectators can flip to any team's view to see the brawl the way that bot saw it.

<br clear="right">

## Every brawl ends with a box score

<p align="center">
  <img src="docs/images/victory.png" width="860" alt="The end of the brawl: a winner banner, 825 ticks, each side's badgers trained, lost and sacked, the final score breakdown and the play-by-play.">
</p>

Take the last enemy HQ, or have the higher score at the final whistle. Results break down into
destruction, economy and survival, feed an Elo ladder of Top Badgers, and come with a full replay:
the engine is deterministic, so a seed plus the command log reproduces any brawl exactly.

## Four house badgers to scrimmage

| House badger | API name | Plays like |
|---|---|---|
| **Sleepy Sitter** | `sitter` | Gathers eggs and never fights back. Your first win. |
| **Honey Badger** | `rusher` | Doesn't care. Two stadiums, linebackers early, blitzes before you're ready. |
| **Blue Badger** | `balanced` | Steady economy, mixed team, upgrades, attacks in waves. |
| **Egg Hoarder** | `economist` | Out-gathers everyone, hides behind residence halls, arrives late with an enormous team. |

Beat the Sleepy Sitter in your first hour. Beating the Egg Hoarder takes a real game plan.

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
badgers. Then put your own bot on the field from another terminal:

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
| New players, first five minutes | [docs/getting-started.md](docs/getting-started.md): one page |
| New players, first bot | [docs/tutorial/](docs/tutorial/README.md): step by step in C# or Python, with screenshots |
| Reference (and the field guide of campus names) | [docs/bot-guide.md](docs/bot-guide.md): every command, rule, stat and error code |
| Teaching / contributors | [docs/how-it-works.md](docs/how-it-works.md): guided tour of the back end, with exercises |
| Hosting | [docs/deploying.md](docs/deploying.md): local Aspire and Azure deployment |
| Maintainers | [docs/architecture.md](docs/architecture.md), [docs/requirements-coverage.md](docs/requirements-coverage.md) |

## Under the hood

The code still lives under its original `NetRts` project names.

| Path | What it is |
|---|---|
| `src/NetRts.Engine` | The game: map generation, rules, pathfinding, the tick simulation, fog-of-war views, replays. Pure, deterministic C#. |
| `src/NetRts.Protocol` | JSON contract shared by the server, SDK and bots. |
| `src/NetRts.Server` | ASP.NET Core host: API keys, matchmaking, tick loops, long-poll state, live spectator stream, Elo, the spectator site. |
| `src/NetRts.Server/wwwroot/lore.js` | The Snow College vocabulary: every name the spectator site shows. |
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

See [CONTRIBUTING.md](CONTRIBUTING.md) to join the team.

## License

MIT. Go Badgers! 🦡
