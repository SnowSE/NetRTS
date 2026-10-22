# NetRts Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-12-10

## Active Technologies

- C# 12 with .NET 10 (LTS) (001-rts-game-engine)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# 12 with .NET 10 (LTS)

## Code Style

C# 12 with .NET 10 (LTS): Follow standard conventions

## Recent Changes

- 001-rts-game-engine: Added C# 12 with .NET 10 (LTS)

<!-- MANUAL ADDITIONS START -->
## NetRts specifics

RTS game for bot programming competitions: bots play over REST, the server ticks the simulation.

```bash
dotnet build                                   # warnings are errors
dotnet test                                    # Microsoft.Testing.Platform (see global.json)
dotnet test --project tests/NetRts.Engine.Tests
dotnet run --project src/NetRts.Server         # http://localhost:5080 (SQLite), /scalar for API docs
dotnet run --project src/NetRts.AppHost        # Aspire + PostgreSQL (needs Docker)
dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name x --strategy rusher --vs sitter
```

- `src/NetRts.Engine` — deterministic simulation (`GameSimulation` partials: core, Commands, Tick, Views, Testing), `GameRules`, `MapGenerator`, `Pathfinder`. No clocks, `System.Random` or unordered iteration; the replay test checks determinism.
- `src/NetRts.Protocol` — JSON DTOs shared with bots. Add fields; don't rename them.
- `src/NetRts.Server` — ASP.NET Core minimal APIs; `Matches/MatchHost` wraps one simulation behind a lock.
- `src/NetRts.Bots` — `NetRtsClient`, `BotLoop`, `PlanBot` + house bot plans.
- `docs/bot-guide.md` is the player-facing rules/API doc — keep it in sync with `GameRules` and the endpoints. `docs/requirements-coverage.md` maps the spec to code.
- `src/NetRts.Server/Achievements` — the 75 achievements (`AchievementCatalog`; ids are stored, never change one) and their evaluation; `Recaps` — nightly ladder snapshots and the recap brief.
- `recap/` — the nightly recap video job (Node: Azure OpenAI gpt-5.5 + Azure Speech in Foundry, Playwright + ffmpeg); `deploy/recap/setup.sh` + `main.bicep` create its Azure resources.
- `src/NetRts.AppHost` — Aspire: local dev and the Azure deployment (private network, see `docs/deploying.md`).
<!-- MANUAL ADDITIONS END -->
