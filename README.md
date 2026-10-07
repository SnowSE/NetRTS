# NetRts

A real-time strategy game built for **programming competitions**. Competitors don't click — they
write bots that play over a REST API. Every tick (1 s by default) the server simulates the world;
between ticks each bot reads its fog-of-war view and queues orders like *"workers 1-7 mine ore
deposit 4"*, *"units 5-10 attack building 334"* or *"workers 4-10 build a barracks at tile 392"*.
Matches end by elimination or on score at the time limit, and results feed an Elo ladder.

- Four unit types (Worker, Soldier, Archer, Scout), five buildings (Command Center, Barracks,
  Resource Depot, TechLab, Guard Tower) and eight tiered upgrades.
- Fog of war, A* pathfinding, simultaneous combat, symmetric procedurally generated maps.
- A deterministic engine: a match's seed plus its command log replays it exactly.
- Four house bots to practise against, a C# SDK and runner, and a dependency-free Python sample.
- A live spectator page in the browser.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet run --project src/NetRts.Server
```

Open http://localhost:5080: start an exhibition between two house bots and watch, or browse the
API, with a live playground, at http://localhost:5080/scalar. With no database configured the server uses a local SQLite
file (`netrts.db`).

Play your first match from another terminal:

```bash
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name my-first-bot --strategy rusher --vs sitter
python samples/python/bot.py --server http://localhost:5080 --name my-python-bot
```

With Docker installed, `aspire run` (or `dotnet run --project src/NetRts.AppHost`) starts the
server against a local PostgreSQL container with the Aspire dashboard. The same AppHost deploys to
Azure App Service + Azure Database for PostgreSQL with `azd up` — see
[docs/deploying.md](docs/deploying.md).

## Documentation

| For | Read |
|---|---|
| Participants, first five minutes | [docs/getting-started.md](docs/getting-started.md) — one page |
| Participants, first bot | [docs/tutorial/](docs/tutorial/README.md) — step by step, with screenshots |
| Participants, reference | [docs/bot-guide.md](docs/bot-guide.md) — every command, rule, stat and error code |
| Teaching / contributors | [docs/how-it-works.md](docs/how-it-works.md) — guided tour of the back end, with exercises |
| Hosting | [docs/deploying.md](docs/deploying.md) — local Aspire and Azure deployment |
| Maintainers | [docs/architecture.md](docs/architecture.md), [docs/requirements-coverage.md](docs/requirements-coverage.md) |

## Writing a bot

The short version:

```bash
# 1. Register (the key is shown once)
curl -X POST localhost:5080/api/v1/players -H 'content-type: application/json' -d '{"name":"my-bot"}'

# 2. Start a match against a house bot
curl -X POST localhost:5080/api/v1/matches -H "Authorization: Bearer $KEY" \
     -H 'content-type: application/json' -d '{"houseBots":["balanced"]}'

# 3. Loop: wait for the next tick, look, act
curl "localhost:5080/api/v1/matches/$MATCH/state?waitForTick=1" -H "Authorization: Bearer $KEY"
curl -X POST localhost:5080/api/v1/matches/$MATCH/commands -H "Authorization: Bearer $KEY" \
     -H 'content-type: application/json' \
     -d '{"commands":[{"type":"Gather","units":"all","unitType":"Worker","targetId":4}]}'
```

## Repository layout

| Path | What it is |
|---|---|
| `src/NetRts.Engine` | The game: map generation, rules, pathfinding, the tick simulation, fog-of-war views, replays. Pure C#, no framework dependencies. |
| `src/NetRts.Protocol` | JSON contract shared by the server, SDK and bots. |
| `src/NetRts.Server` | ASP.NET Core host: API-key auth, matchmaking, tick loops, long-poll state, SSE spectator stream, persistence, Elo, spectator web page (`wwwroot`). |
| `src/NetRts.Bots` | C# SDK (`NetRtsClient`, `BotLoop`) and the house bots. |
| `src/NetRts.BotRunner` | Command-line runner for any house-bot strategy against a server. |
| `src/NetRts.AppHost`, `src/NetRts.ServiceDefaults` | .NET Aspire: local orchestration, Azure deployment, OpenTelemetry defaults. |
| `samples/python` | Standalone Python reference bot. |
| `tests/NetRts.Engine.Tests` | Rules, combat, economy, victory, full bot-vs-bot matches, replay determinism. |
| `tests/NetRts.Server.Tests` | HTTP API end to end, including a bot playing a whole match over HTTP. |
| `specs/001-rts-game-engine` | The original specification. [`docs/requirements-coverage.md`](docs/requirements-coverage.md) maps each requirement to the code and tests that satisfy it. |

## Development

```bash
dotnet build
dotnet test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/architecture.md](docs/architecture.md).

## License

MIT
