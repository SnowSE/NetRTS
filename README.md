<h1 align="center">NetRts</h1>

<p align="center"><strong>Write the bot. Command the army. Watch it win.</strong></p>

<p align="center">
  <a href="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml"><img src="https://github.com/SnowSE/NetRTS/actions/workflows/ci.yaml/badge.svg" alt="CI status"></a>
</p>

<p align="center">
  <img src="docs/images/battle.gif" width="520" alt="An 825-tick match replayed at speed: the orange economist builds up in the top-left, the blue balanced bot in the bottom-right, and their armies trade waves across the middle of the map until orange breaks through.">
</p>

**NetRts is a real-time strategy game where nobody touches a mouse.** You write a program, in any
language that speaks HTTP, and it runs an army: mining ore, raising barracks, researching weapons,
scouting through the fog and throwing soldiers at the enemy base. Every second the world ticks
forward; your bot sees what its units can see and decides what to do next. Then you sit back and
watch it win (or work out why it didn't).

Built for programming competitions, classrooms and anyone who has ever wanted to settle
"my strategy is better than yours" with code.

## Your code is the commander

Orders are plain JSON, and they say what you mean:

```jsonc
{ "commands": [
  { "type": "Gather",  "units": "all", "unitType": "Worker", "targetId": 4 },          // workers, mine that ore
  { "type": "Build",   "unitIds": [31], "buildingType": "Barracks", "x": 12, "y": 9 }, // put a barracks here
  { "type": "Produce", "buildingId": 77, "unitType": "Soldier", "count": 3 },          // three soldiers, please
  { "type": "Attack",  "units": "5-40", "x": 56, "y": 56 }                             // everyone: fight your way there
] }
```

Four unit types, five buildings, eight upgrades, and a rule set small enough to learn in an
afternoon, with plenty of strategy hiding in it: rush early, out-mine the opponent, turtle behind
guard towers, out-tech them, or scout their base and strike where they're weak.

## Zoom into the fight

<p align="center">
  <img src="docs/images/close-up.gif" width="640" alt="A close-up on the blue base at the end of the match: the orange army of archers and shield-bearing soldiers marches in and tears down the barracks, the lab and finally the keep.">
</p>

The live spectator page streams every match tick by tick. Scroll or pinch to zoom, and the map
switches from a strategic overview to an illustrated close-up: workers with pickaxes, soldiers
with shields, archers, scouts, keeps, towers, labs, health bars and build progress. Hit
**Follow the fight** and the camera chases the biggest battle on its own. It fills whatever screen
you give it, from a phone to a wall-sized monitor, in light or dark.

<p align="center">
  <img src="docs/images/spectator.png" width="860" alt="The spectator page mid-match: the map on the left, each player's ore, income, score breakdown and researched upgrades on the right, and a live battle log underneath.">
</p>

## Fog of war that's actually fair

<img src="docs/images/fog-of-war.png" width="300" align="right" alt="The same match through the blue player's eyes: only the area around its own base is lit; the orange army waits just beyond the edge of its sight.">

Your bot sees only what its units see. The server enforces this; it isn't a polite request to the
client. Enemy orders are never revealed, ids of unseen units can't be probed, and buildings you
scouted earlier are remembered for you. Maps are generated from a seed with mirror symmetry, so
every start position is exactly as good as every other.

Spectators can flip to any player's view to see the battle the way that bot saw it.

<br clear="right">

## Every match ends with a story

<p align="center">
  <img src="docs/images/victory.png" width="860" alt="The end of the match: an 'economist wins' banner, 825 ticks, each side's units built, lost and killed, the final score breakdown and the battle log.">
</p>

Destroy the last enemy Command Center, or have the higher score when the clock runs out. Results
break down into destruction, economy and survival, feed an Elo leaderboard, and come with a full
replay: the engine is deterministic, so a seed plus the command log reproduces any match exactly.

## Four sparring partners included

| House bot | Plays like |
|---|---|
| `sitter` | Mines and never fights back. Your first win. |
| `rusher` | Two barracks, soldiers early, attacks before you're ready. |
| `balanced` | Steady economy, mixed army, upgrades, attacks in waves. |
| `economist` | Out-mines everyone, turtles behind towers, arrives late with an enormous army. |

Beat `sitter` in your first hour. Beating `economist` takes a real plan.

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

Open **http://localhost:5080** and start an exhibition between two house bots. Then play your
first match from another terminal:

```bash
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name my-first-bot --strategy rusher --vs sitter
python samples/python/bot.py --server http://localhost:5080 --name my-python-bot
```

With no database configured the server keeps players and results in a local SQLite file. With
Docker installed, `aspire run` starts it against PostgreSQL with the Aspire dashboard, and the same
setup deploys to Azure (App Service plus a private PostgreSQL) with `azd up`.

<p align="center">
  <img src="docs/images/home.png" width="760" alt="The home page: start an exhibition between two house bots, watch live matches, recent results and the leaderboard.">
</p>

## Documentation

| For | Read |
|---|---|
| Participants, first five minutes | [docs/getting-started.md](docs/getting-started.md): one page |
| Participants, first bot | [docs/tutorial/](docs/tutorial/README.md): step by step in C# or Python, with screenshots |
| Participants, reference | [docs/bot-guide.md](docs/bot-guide.md): every command, rule, stat and error code |
| Teaching / contributors | [docs/how-it-works.md](docs/how-it-works.md): guided tour of the back end, with exercises |
| Hosting | [docs/deploying.md](docs/deploying.md): local Aspire and Azure deployment |
| Maintainers | [docs/architecture.md](docs/architecture.md), [docs/requirements-coverage.md](docs/requirements-coverage.md) |

## Under the hood

| Path | What it is |
|---|---|
| `src/NetRts.Engine` | The game: map generation, rules, pathfinding, the tick simulation, fog-of-war views, replays. Pure, deterministic C#. |
| `src/NetRts.Protocol` | JSON contract shared by the server, SDK and bots. |
| `src/NetRts.Server` | ASP.NET Core host: API keys, matchmaking, tick loops, long-poll state, live spectator stream, Elo, the spectator site. |
| `src/NetRts.Bots` | C# SDK (`NetRtsClient`, `BotLoop`) and the house bots. |
| `src/NetRts.BotRunner` | Command-line runner for any house-bot strategy. |
| `src/NetRts.AppHost`, `src/NetRts.ServiceDefaults` | .NET Aspire: local orchestration, Azure deployment, telemetry. |
| `samples/python` | Standalone Python reference bot. |
| `tests/` | Engine and API tests, including whole bot-vs-bot matches and replay determinism. |
| `specs/001-rts-game-engine` | The original specification; [docs/requirements-coverage.md](docs/requirements-coverage.md) maps it to the code. |

```bash
dotnet build
dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) to get involved.

## License

MIT
