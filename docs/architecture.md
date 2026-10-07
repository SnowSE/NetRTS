# Architecture

```text
 bots (any language) ──HTTP──►┌──────────────────────── NetRts.Server ────────────────────────┐
 browsers ─────────SSE───────►│ API-key auth · rate limits · endpoints                         │
                              │        │                                                       │
                              │   MatchManager ──► MatchHost (one per match)                    │
                              │                     ├─ lock ─► GameSimulation (NetRts.Engine)   │
                              │                     ├─ tick loop (PeriodicTimer)                │
                              │                     ├─ house bots (NetRts.Bots)                 │
                              │                     └─ tick signal ─► long-polls, SSE streams   │
                              │   MatchRecorder ──► EF Core (PostgreSQL or SQLite)              │
                              └────────────────────────────────────────────────────────────────┘
```

## The engine is the product

`NetRts.Engine` is a plain library with no dependencies beyond the protocol DTOs. A
`GameSimulation` owns one match's entire state and exposes a handful of operations:
`Submit(slot, commands)`, `Step()`, `Surrender(slot)`, `GetPlayerView(slot)`,
`GetSpectatorView()`, `GetResult()`, `GetReplay()`.

It is **deterministic**: map generation uses its own SplitMix64 generator (not `System.Random`,
whose sequence can change between .NET versions), entities are always iterated in id order, all
tie-breaks are explicit, and nothing reads the clock. A replay is just the config, the seed, and the
accepted commands with the tick they were queued on; `GameSimulation.FromReplay` re-runs it and the
tests check the final state hash matches the original.

Because the engine is just data in and data out, nearly all game behaviour is tested directly and
quickly (`tests/NetRts.Engine.Tests`), including full matches between the house bots.

### One tick

1. Execute up to 100 queued commands per player (re-validated against the current state).
2. Advance production and research; spawn finished units.
3. Every unit acts in id order: follow its order, move (A* with movement points), mine, haul,
   construct, or pick an attack. Decisions use the visibility at the start of the tick.
4. Guard towers pick targets.
5. Apply every attack at once (simultaneous combat), then remove the dead and credit kills.
6. Eliminate players with no Command Center; end the match on elimination or the tick limit.

## The server is a thin host

- **`MatchHost`** wraps one `GameSimulation` behind a lock. API requests and the tick loop both go
  through it, so nobody ever observes a half-applied tick. After each tick it completes a
  `TaskCompletionSource` that wakes long-polling bots (`waitForTick`) and spectator streams.
- **House bots** run inside the host: just before each tick they read their fog-of-war view and
  submit through the same validation as remote bots. The house bots use the same `IBotStrategy`
  interface as the C# runner, so the reference bots are exercised both in-process and over HTTP.
- **Auth** is per-player API keys (only SHA-256 hashes are stored), which suits long-running bots
  better than short-lived JWTs. Every game endpoint requires a key and checks the caller is seated
  in that match.
- **Persistence** is deliberately small: players (name, key hash, rating, record) and finished
  matches (summary, result, replay as JSON). Live state is in memory; a match's record is written
  by `MatchRecorder` off the tick thread when it ends. PostgreSQL is used when a `netrtsdb`
  connection string is present (Aspire locally, Azure in production), SQLite otherwise. In Azure the
  server signs in to PostgreSQL with its managed identity, so there is no database password at all.
- **Spectators** get a server-sent-events stream with one omniscient snapshot per tick.

## Scaling

One process comfortably runs dozens of matches: a tick costs a few milliseconds even with armies of
100+ units (`LiveMatchTests` runs ten concurrently). Because live matches live in the memory of
the process hosting them, run **one server instance**. Scaling out would need routing by
match id (e.g. a consistent-hash ingress on the `/matches/{id}` path segment) plus moving the
"list matches" view to the database; neither is needed for a competition-sized event.

## What changed from the first implementation

The first version (still in git history, on the `001-rts-game-engine` branch) followed a Clean-Architecture/CQRS template but never reached a playable game:

- it didn't build (vulnerable package pins under warnings-as-errors; MediatR 13+ now needs a commercial licence);
- authentication was disabled on every game endpoint, and any caller could impersonate a player
  with an `X-Test-Player-Id` header;
- workers never constructed anything (buildings appeared instantly), gathering happened once per
  command instead of continuously, speed and armor stats were ignored, building kills were scored
  twice, combat was sequential, and command ids came from `DateTime.Ticks`;
- API threads and the tick thread mutated the same lists without synchronisation;
- there were no sample bots, which the spec's success criteria require.

The rewrite keeps the specification and the good ideas (Aspire, PostgreSQL, fog of war, the
command-queue model) and rebuilds the rest around a deterministic engine.
