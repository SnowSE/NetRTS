# Writing a Badger Brawl bot

A Badger Brawl bot is any program that can make HTTP requests. Each tick (1 second by default) the
server simulates the world; between ticks your bot reads what it can see and queues orders.
This guide covers everything a bot needs; live, typed API docs with a playground are at **`/scalar`**, and every
balance number is served by **`GET /api/v1/rules`**.

- [The loop](#the-loop)
- [Authentication](#authentication)
- [Matches](#matches)
- [Reading state](#reading-state)
- [Commands](#commands)
- [Game rules](#game-rules)
- [Scoring and victory](#scoring-and-victory)
- [Errors](#errors)
- [Reference bots](#reference-bots)
- [Badger field guide](#badger-field-guide)

## Badger field guide

The spectator site tells the story in Snow College terms. The API speaks plain RTS, so the names your
bot sends and reads never change. When the play-by-play says your Worker was bowled over outside
Anderson Hall, your bot saw a `Worker` die next to a `Barracks`.

Snow College is named for Lorenzo and Erastus Snow, not the weather. It opened in 1888 as Sanpete
Stake Academy, with its first classes held upstairs in Ephraim's Co-op Store. Every team gets its
own campus building and its own student housing.

| On the field | In the API | |
|---|---|---|
| Worker, Soldier, Archer, Scout | `Worker`, `Soldier`, `Archer`, `Scout` | Units keep their names in both themes |
| HQ: Noyes Building, Greenwood Student Center, Eccles Center, Huntsman Library, ... (one per player slot) | `CommandCenter` | Lose your last one and you're out |
| Housing: Suites at Academy Square, Anderson Hall, Mary Nielson Hall, Snow Hall, ... (one per player slot) | `Barracks` | Trains soldiers, archers and scouts |
| Co-op Store | `ResourceDepot` | Closer drop-off for grubs, more storage |
| GRSC Makerspace | `TechLab` | Researches upgrades |
| Guard Tower | `GuardTower` | Fires on anyone close |
| Grubs, dug from grub patches | ore (`resources`, `Gather`) | What everything costs |
| Intro to Chemistry → Organic Chemistry | `Weapons1` → `Weapons2` | More damage |
| Unit Testing → Code Review | `Armor1` → `Armor2` | More armor |
| Algorithms → Parallel Computing | `Mobility1` → `Mobility2` | More speed |
| Intro to Biology → Entomology | `Harvesting1` → `Harvesting2` | More grubs per dig (know your insects) |
| Sleepy Sitter, Honey Badger, Blue Badger, Grub Hoarder | `sitter`, `rusher`, `balanced`, `economist` | House badgers |

## The loop

```text
register once ─► create or join a match ─► GET state?waitForTick=0      (blocks until the match starts)
                                            │
                                            ▼
                         ┌──► decide ─► POST commands
                         │                  │
                         └── GET state?waitForTick=<tick+1>&sinceTick=<tick>   (blocks until the next tick)
```

`waitForTick` is a long-poll: the server holds the request until that tick has been simulated (or
the match ends, or ~30 s pass), so you never need to sleep or busy-poll. `sinceTick` returns every
event that happened after that tick, so you never miss one.

## Authentication

```http
POST /api/v1/players
{ "name": "my-bot" }

201 Created
{ "playerId": "…", "name": "my-bot", "apiKey": "nrts_…" }
```

The API key is shown **once**. Send it on every authenticated call:

```http
Authorization: Bearer nrts_…
```

(`X-Api-Key: nrts_…` also works.) Names are 3–32 letters, digits, `_` or `-`. Registration is
limited to 5 per minute per IP address, so register once and reuse the key.

## Matches

| Call | What it does |
|---|---|
| `POST /api/v1/matches` | Create a match; you take slot 0. Body (all optional): `{"name":"cs1400-lab", "maxPlayers":2, "houseBots":["balanced"], "settings":{"tickIntervalMs":1000,"maxTicks":1800,"mapWidth":64,"mapHeight":64,"seed":42}}`. It starts as soon as every seat is filled — with `houseBots` that is immediately. A `name` (3–32 letters, digits, `_` or `-`) lets others find the match; no two unfinished matches share one (`409 MATCH_NAME_TAKEN`). |
| `GET /api/v1/matches?status=Waiting` | Open matches you can join, with their `name` if they have one. |
| `GET /api/v1/matches/named/{name}` | The match with that name: the one waiting or running, otherwise the most recent. The spectator page takes names too: `/#/match/{name}`. |
| `POST /api/v1/matches/{id}/join` | Take a free seat. |
| `POST /api/v1/matches/{id}/leave` | Give up your seat before the match starts. |
| `GET /api/v1/matches/{id}/map` | Terrain and every start position (public knowledge). |
| `GET /api/v1/matches/{id}/state` | Your fog-of-war view (see below). |
| `POST /api/v1/matches/{id}/commands` | Queue commands. |
| `DELETE /api/v1/matches/{id}/commands` | Drop everything still waiting in your queue. |
| `POST /api/v1/matches/{id}/surrender` | Concede. |
| `GET /api/v1/matches/{id}/result` | Winner, end reason, score breakdown and stats (`409` until it ends). |
| `GET /api/v1/matches/{id}/replay` | Seed + every executed command — enough to re-simulate the match exactly. |
| `GET /api/v1/bots` | House bots: `rusher`, `economist`, `balanced`, `sitter`. |
| `GET /api/v1/leaderboard` | Elo ladder. Only player-vs-player 1v1 matches change ratings; games against house bots count toward your win/loss record but not your rating. |

When a match fills there is a short warm-up (3 s by default) before tick 1, so every bot can read
the opening state.

**Big matches.** `maxPlayers` goes from 2 to 16, and `houseBots` can fill every seat but yours.
Repeat a name for several copies of the same bot: `["rusher","rusher","sitter"]` seats
`house-rusher`, `house-rusher 2` and `house-sitter`. Only the first copy's results count toward that
bot's leaderboard record. Two to four players start in the map's corners. Five or more start
evenly spaced around a ring, each with the same ore seam behind their Command Center, an expansion
and a contested deposit in the gap to the next player, and the same rock outcrops in every gap. A
ring needs room, so the default map grows with the player count (120×120 for 16 players), and a map
that's too small is rejected with `INVALID_SETTINGS` saying the minimum size. `GET .../map` lists
every start position.

## Reading state

`GET /api/v1/matches/{id}/state?waitForTick=N&sinceTick=M`

```jsonc
{
  "tick": 42, "status": "Active", "maxTicks": 1800, "tickIntervalMs": 1000,
  "mapWidth": 64, "mapHeight": 64,
  "you": { "slot": 0, "resources": 380, "storageCapacity": 1500, "incomePerMinute": 240,
           "queuedCommands": 0, "queueCapacity": 500,
           "upgrades": ["Weapons1"], "researching": [{ "upgrade": "Armor1", "percent": 40, "buildingId": 77 }] },
  "players": [ { "slot": 0, "name": "my-bot", "eliminated": false, "startPosition": {"x":7,"y":7},
                 "score": {"destruction":0,"economy":260,"survival":650,"total":910} }, … ],
  "visibility": ["0000111110…", …],            // one string per row y; char x is '1' if you can see it
  "units":     [ { "id": 31, "owner": 0, "type": "Worker", "x": 9, "y": 6, "hp": 40, "maxHp": 40,
                   "activity": "Gathering", "carrying": 6, "targetId": 4, "destination": null }, … ],
  "buildings": [ { "id": 30, "owner": 0, "type": "CommandCenter", "x": 7, "y": 7, "hp": 1500, "maxHp": 1500,
                   "completed": true, "constructionPercent": 100,
                   "production": [{ "unitType": "Worker", "percent": 50 }], "research": null,
                   "rally": { "x": 2, "y": 6 } }, … ],
  "resources": [ { "id": 4, "x": 2, "y": 6, "remaining": 940 }, … ],
  "rememberedBuildings": [ { "id": 212, "owner": 1, "type": "Barracks", "x": 51, "y": 49,
                             "hp": 700, "maxHp": 700, "completed": true, "lastSeenTick": 31 }, … ],
  "events":    [ { "tick": 42, "kind": "UnitProduced", "message": "Worker 88 ready at (8,8)", "entityId": 88 }, … ],
  "outcome": null
}
```

- `owner` is a player **slot**; compare with `you.slot`.
- You always see all of your own units and buildings. Enemy units/buildings and resource deposits
  appear only while inside your vision. Enemy orders (`targetId`, `destination`) and enemy
  production/research are never revealed.
- `rememberedBuildings` lists enemy buildings you saw earlier that are now in the fog, as they were
  when last seen (`lastSeenTick`). An entry is dropped once you see the tile again and the building
  is gone. You can't `Attack` a remembered building by id until you see it again, but you can
  attack-move to its position.
- `you.incomePerMinute` is the ore you banked in the last 60 ticks (a minute at normal speed).
- Every unit, building and deposit shares one id space, so an id is unambiguous.
- Event kinds: `CommandFailed` (carries `commandId`), `UnitProduced`, `UnitLost`, `UnitKilled`,
  `BuildingStarted`, `BuildingCompleted`, `BuildingLost`, `BuildingDestroyed`, `ResearchCompleted`,
  `DepositDepleted`, `PlayerEliminated`, `MatchEnded`.

## Commands

```http
POST /api/v1/matches/{id}/commands
{ "commands": [ { "type": "Gather", "units": "all", "unitType": "Worker", "targetId": 4 }, … ] }
```

| `type` | Required fields | Effect |
|---|---|---|
| `Move` | units, position | Walk there, ignoring enemies. |
| `Attack` | units, `targetId` **or** position | With `targetId`: chase and attack that visible enemy until it dies or is lost in the fog. With a position: *attack-move* — walk there, fighting anything seen on the way. |
| `Gather` | units, `targetId` (a deposit) | Workers mine, haul ore to the nearest Command Center / Resource Depot, and repeat. When the deposit runs dry they move on to one nearby. Deposits never move, so any deposit you have seen can be targeted, even from the fog. |
| `Build` | units, `buildingType`, position | Workers walk next to the tile and break ground (the cost is paid then). Construction speed scales with the number of workers. Targeting your own unfinished building of the same type makes the workers help finish it. |
| `Produce` | `buildingId`, `unitType`, optional `count`, optional position | Queue units (max 5 queued per building). Paid immediately. A position also sets the building's rally point. |
| `Rally` | `buildingId`, position | Newly trained units walk to that tile. Workers rallied onto an ore deposit start mining it. |
| `Research` | `buildingId` (a TechLab), `upgrade` | One research at a time per TechLab. Paid immediately. |
| `Stop` | units | Drop the current order. |

**Selecting units** — any combination of:

- `"units": "5-10"`, `"units": "1-7,12,20-22"` — ids and inclusive ranges; only *your* units in the
  ranges are selected, so ranges may safely span ids you don't own.
- `"units": "all"` or `"units": "idle"`.
- `"unitIds": [5, 6, 9]` — explicit ids; each must be a unit you own or the command is rejected.
- `"unitType": "Worker"` — narrows the selection to that type. (For `Produce`, `unitType` is the
  unit to build instead.)

**Positions** — `"x": 12, "y": 9` or `"tile": 588` where `tile = y * mapWidth + x`.

**When commands run** — each command is validated immediately; the response lists, per command,
whether it was accepted (with a `commandId`) or rejected (with an error `code`). Accepted commands
wait in your FIFO queue (capacity 500) and the first 100 run at the start of the next tick. A
command is validated again when it runs; if things changed (you spent the ore, the target died) it
fails with a `CommandFailed` event carrying its `commandId`. A Build that fails later, when the
worker arrives (ore spent, site taken, unreachable), also reports `commandId` and the worker as
`entityId`. Every command in a batch is checked against your ore *as it is now*, so five Barracks
orders with 500 ore are all accepted; keep your own budget or the later ones will fail. A later command for the same unit
replaces the earlier order, so the most recent command wins.

```jsonc
{ "accepted": 1, "rejected": 1, "queueSize": 1, "queueCapacity": 500,
  "results": [ { "index": 0, "accepted": true, "commandId": 17 },
               { "index": 1, "accepted": false, "error": { "code": "OUT_OF_BOUNDS", "message": "(99,3) is outside the 64x64 map." } } ] }
```

Command error codes: `INVALID_COMMAND_TYPE`, `NO_UNITS_SPECIFIED`, `UNIT_NOT_OWNED`,
`INVALID_SELECTOR`, `NO_MATCHING_UNITS`, `MISSING_POSITION`, `OUT_OF_BOUNDS`, `MISSING_TARGET`,
`TARGET_NOT_FOUND`, `FRIENDLY_FIRE`, `RESOURCE_NOT_FOUND`, `MISSING_BUILDING_TYPE`, `INVALID_SITE`,
`POSITION_OCCUPIED`, `MISSING_PREREQUISITE`, `INSUFFICIENT_RESOURCES`, `MISSING_BUILDING`,
`BUILDING_NOT_FOUND`, `BUILDING_NOT_OPERATIONAL`, `MISSING_UNIT_TYPE`, `CANNOT_PRODUCE`,
`INVALID_COUNT`, `PRODUCTION_QUEUE_FULL`, `CANNOT_RALLY`, `CANNOT_RESEARCH`, `MISSING_UPGRADE`,
`ALREADY_RESEARCHED`, `ALREADY_RESEARCHING`, `BUILDING_BUSY`, `QUEUE_FULL`, `TOO_MANY_COMMANDS`,
`MATCH_NOT_ACTIVE`, `PLAYER_ELIMINATED`.

## Game rules

The map is a grid (64×64 by default) with four-way mirror symmetry, so every start is equally fair.
`(0,0)` is the top-left. Rock (`#`) blocks movement; so do buildings and ore deposits. Units may
share tiles. Each player starts with a Command Center, 5 workers and 500 ore, with a field of ore
about five tiles away; richer contested ore sits near the centre.

**Units**

| Unit | Cost | Build ticks | HP | Damage | Armor | Range | Cooldown | Vision | Speed | Made at |
|---|---|---|---|---|---|---|---|---|---|---|
| Worker | 50 | 8 | 40 | 4 | 0 | 1 | 1 | 5 | 10 | CommandCenter |
| Soldier | 100 | 12 | 110 | 9 | 1 | 1 | 1 | 5 | 10 | Barracks |
| Archer | 125 | 14 | 60 | 12 | 0 | 4 | 2 | 6 | 9 | Barracks |
| Scout | 75 | 8 | 55 | 5 | 0 | 1 | 1 | 8 | 20 | Barracks |

Speed is in tenths of a tile per tick (10 = one tile per tick). Range and adjacency use Chebyshev
distance (diagonals count as 1); vision uses Euclidean distance.

**Buildings**

| Building | Cost | Build work | HP | Armor | Vision | Notes |
|---|---|---|---|---|---|---|
| CommandCenter | 400 | 80 | 1500 | 2 | 6 | Trains workers. Ore drop-off. +1500 storage. Lose them all and you're out. |
| Barracks | 150 | 40 | 700 | 1 | 6 | Trains soldiers, archers, scouts. |
| ResourceDepot | 100 | 25 | 450 | 1 | 6 | Ore drop-off. +750 storage. |
| TechLab | 150 | 40 | 550 | 1 | 6 | Researches upgrades. Requires a Barracks. |
| GuardTower | 125 | 35 | 500 | 2 | 7 | Shoots enemy units: 10 damage, range 5. Requires a Barracks. |

Build work is in worker-ticks: two workers finish a Barracks in 20 ticks. Sites start at 10% HP and
gain the rest as they're built.

**Upgrades** (TechLab; each tier 2 needs its tier 1)

| Upgrade | Cost | Ticks | Effect |
|---|---|---|---|
| Weapons1 / Weapons2 | 150 / 250 | 40 / 60 | +2 damage each, all units and towers |
| Armor1 / Armor2 | 150 / 250 | 40 / 60 | +1 armor each, all units |
| Mobility1 / Mobility2 | 100 / 200 | 30 / 45 | +2 speed each, all units |
| Harvesting1 / Harvesting2 | 100 / 200 | 30 / 45 | +1 ore per mining tick each |

Upgrades apply instantly to existing and future units.

**Economy** — workers mine 2 ore per tick (carrying up to 10), then walk it to the nearest
drop-off. Ore beyond your storage capacity is lost.

**Combat** — damage dealt is `max(1, damage − armor)`. Every attack in a tick lands at the same
time, so two units can kill each other. Idle soldiers/archers/scouts fight back against enemies
already in range; workers never attack unless ordered.

**Each tick runs in this order:** your queued commands → production & research → unit behaviour
and movement → combat → deaths → elimination & victory checks.

## Scoring and victory

- **Elimination** — a player with no Command Center (finished or under construction) is out. The
  last player standing wins.
- **Time limit** — at `maxTicks` (1800 by default, 30 minutes) the highest total score wins;
  equal totals are a draw.
- **Surrender** — conceding eliminates you immediately.

Score = **destruction** (ore value of enemy units and buildings you destroyed) + **economy** (ore
banked) + **survival** (ore value of your living units and finished buildings).

## Errors

Every non-2xx response has the same body:

```json
{ "code": "NOT_A_PARTICIPANT", "message": "You are not playing in this match." }
```

Common ones: `401 UNAUTHORIZED`, `403 NOT_A_PARTICIPANT`, `404 MATCH_NOT_FOUND`,
`409 MATCH_NOT_STARTED` (keep long-polling with `waitForTick=0`), `409 MATCH_FULL`,
`400 INVALID_JSON`, `429 RATE_LIMITED` (by default 30 requests/second per bot in each match, and
120/second across all of a bot's matches).

## Reference bots

- **Python, no dependencies:** [`samples/python/bot.py`](../samples/python/bot.py).
- **C#:** `src/NetRts.Bots` has a typed client (`NetRtsClient`), the standard loop
  (`BotLoop.RunAsync`) and the house bots' `PlanBot` brain. The runner plays any of them against a
  server:

  ```bash
  dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name my-bot --strategy balanced --vs rusher
  ```

- **Replays:** `GET /result` + `GET /replay` give you everything to analyse a loss. In C#,
  `GameSimulation.FromReplay(replay)` re-runs the match tick for tick.
