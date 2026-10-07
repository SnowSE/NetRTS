# Contributing to NetRts

## Ground rules

- **Game behaviour lives in `NetRts.Engine` and must stay deterministic.** No clocks, no
  `System.Random`, no iteration over unordered collections, explicit tie-breaks. If you add state,
  include it in `StateHash()` so the replay test keeps you honest.
- **Balance numbers live in `GameRules`** and reach bots through `GET /api/v1/rules`. Update
  `docs/bot-guide.md` when you change them.
- **The wire contract lives in `NetRts.Protocol`.** Changing a field name breaks every bot in every
  language; add fields rather than renaming, and bump the API version for breaking changes.
- **The server stays thin.** Anything that touches a `GameSimulation` goes through `MatchHost` so it
  happens under the match lock.

## Tests

Write the test first where you can.

- Rules and mechanics → `tests/NetRts.Engine.Tests`. Use `TestGames.New()` and the internal hooks in
  `GameSimulation.Testing.cs` to stage exact scenarios.
- API behaviour → `tests/NetRts.Server.Tests` (real HTTP pipeline, isolated SQLite file, manual
  ticks via `MatchHost.Advance`).
- If you change the house bots or balance, run `FullMatchTests` and make sure matches still end
  decisively.

```bash
dotnet build
dotnet test
```

## Pull requests

1. Branch from `main`.
2. `dotnet build` must be warning-free (warnings are errors) and `dotnet test` green.
3. Describe user-visible changes (API, rules, balance) in the PR.
