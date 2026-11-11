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
- `src/NetRts.Bots` — `NetRtsClient`, `BotLoop`, `PlanBot` + house bot plans, and `Blocks/` (Block Bot: rule programs the server plays for a player). Both server-played bots stand on `BotView` (one tick's queries), `BotMemory` (what a bot keeps between ticks), `BotTurn` (one tick's budget, reserved units and commands; `TryTrain`/`TryBuild`/`TryResearch`/`Order` return an `Attempt`). `BotCharacterizationTests` hash every command the house bots and Block Bot send: a refactor must leave the hashes alone.
- `docs/bot-guide.md` is the player-facing rules/API doc — keep it in sync with `GameRules` and the endpoints. `docs/requirements-coverage.md` maps the spec to code.
- `src/NetRts.Server/Achievements` — the 76 achievements (`AchievementCatalog`; ids are stored, never change one) and their evaluation; `Recaps` — nightly ladder snapshots and the recap brief.
- `recap/` — the nightly recap video job (Node: Azure OpenAI gpt-5.5 + Azure Speech in Foundry, Playwright + ffmpeg); `deploy/recap/setup.sh` + `main.bicep` create its Azure resources.
- `src/NetRts.AppHost` — Aspire: local dev and the Azure deployment (private network, see `docs/deploying.md`).

### Sources of truth (one owner per idea)

Before adding a list, name table, constant or helper, find its owner here and use it; if the other language needs it too, serve it from an endpoint or add a test that compares the two copies.

| Idea | Owner | Other side |
|---|---|---|
| Game balance (costs, stats, prerequisites) | `GameRules` | pages read `GET /api/v1/rules` |
| Snow College names (Housing, grubs, Snow Mobile, ...) | `wwwroot/lore.js` | `NetRts.Bots/SnowLore.cs`, kept equal by `SnowLoreTests` |
| Block Bot words, choices, limits, defaults | `BlockProgramValidator` (`Vocabulary`) | pages read `GET /api/v1/blocks/vocabulary` |
| Who plays a seat | `Seat` (`IsHouseBot`, `IsBlockBot`, `IsOwnCode`) | `players[]` in the match summary; `match_players` |
| Live match stream (SSE, backoff, gone, end) | `wwwroot/matchfeed.js` `openMatchFeed` | |
| 3D view on a page | `wwwroot/3d/mount.js` `mount3d` | |
| Clock, progress bar, building details | `wwwroot/matchui.js` | |
| Block Bot rule editing (sessions, saving, limits, moves, undo/redo) | `wwwroot/blocks/editor-core.js` `EditorCore` | the two styles extend it: cards `rule-editor.js`, drag-and-drop `block-editor.js`, picked in `editors.js` |
| Block Bot block words, inputs and choices | `wwwroot/blocks/editor-parts.js` | both editor styles draw from it |
| A strategy as code (the edit page's Python / C# download) | rules: `wwwroot/blocks/python.js`, `csharp.js`; bots: `wwwroot/blocks/export/*.txt` | `BlockExportTests`: every block kind has code, every call is on the bot, the bots parse/build |
| Page lifetime (timers, listeners, late replies) | `util.js` `mountScope` | |
| Browser storage | `wwwroot/storage.js` (API keys in sessionStorage unless the player opts in) | |
| Player names, ranking, `fmt`, `downloadJson`, `botLabel` | `wwwroot/util.js` | |

### Keeping it from turning into mud

- `DuplicationTests` fails when ten meaningful lines appear twice anywhere in `src`. Fix it by extracting what both places call, never by widening the window or adding an exception.
- The second feature that needs a piece (a stream, a 3D mount, a card) pulls it into a shared module and moves the first user onto it in the same change.
- New JS modules stay under about 800 lines; split along their seams (see `wwwroot/blocks/`). `3d/view.js`, `mapview.js` and `spectate.js` are already over: don't grow them, take pieces out when you touch them.
- New pages load with `import()` from `app.js` (`mountLazy`, as Block Bot does) instead of a static import every visitor parses, and every async callback in a page runs inside its `mountScope`.
<!-- MANUAL ADDITIONS END -->
