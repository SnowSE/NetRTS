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
| Snow Mobile: the college's old Snap-on tool truck, with a cannon on the roof | `Trebuchet` | Siege: outranges the Bell Tower |
| HQ: Noyes Building, Greenwood Student Center, Eccles Center, Huntsman Library, ... (one per player slot) | `CommandCenter` | Lose your last one and you're out |
| Housing: Suites at Academy Square, Anderson Hall, Mary Nielson Hall, Snow Hall, ... (one per player slot) | `Barracks` | Trains soldiers, archers and scouts |
| Co-op Store | `ResourceDepot` | Closer drop-off for grubs, more storage |
| GRSC Makerspace | `TechLab` | Researches upgrades |
| The Bell Tower | `GuardTower` | Fires on anyone close |
| Washburn Welding Shop (Richfield campus) | `SiegeWorkshop` | Builds Snow Mobiles |
| Grubs, dug from grub patches | ore (`resources`, `Gather`) | What everything costs |
| Intro to Chemistry → Organic Chemistry | `Weapons1` → `Weapons2` | More damage |
| Unit Testing → Code Review | `Armor1` → `Armor2` | More armor |
| Algorithms → Parallel Computing | `Mobility1` → `Mobility2` | More speed |
| Intro to Biology → Entomology | `Harvesting1` → `Harvesting2` | More grubs per dig (know your insects) |
| Welding Technology | `SiegeEngineering` | Unlocks the Washburn Welding Shop |
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
limited to 25 per minute per IP address (a classroom shares one), so register once and reuse the key.

### Verify your bot with Discord

On the website, **Sign in with Discord** (top right) and either claim a bot by pasting its API key or
register a new one there. Claimed bots get a blue check, show your nickname from our Discord server
next to their name, and can play **league matches** (preseason, season and championship). You must be a member of
the Discord server to be verified. The account page also replaces a lost or leaked API key.
Bots never use Discord themselves: they keep sending their API key.

**Matches on the shared server are public.** Finished matches are announced in the
coding-competition Discord channel's #results thread: season and championship matches straight
away, practice and preseason games in an hourly roundup. To practise privately, run your own server
(`dotnet run --project src/NetRts.Server`).

## Matches

| Call | What it does |
|---|---|
| `POST /api/v1/matches` | Create a practice match. Body (all optional): `{"name":"cs1400-lab", "maxPlayers":2, "houseBots":["balanced"], "settings":{"tickIntervalMs":1000,"maxTicks":1800,"mapWidth":64,"mapHeight":64,"seed":42}}`. It starts as soon as every seat is filled — with `houseBots` that is immediately. A `name` (3–32 letters, digits, `_` or `-`) lets others find the match; no two unfinished matches share one (`409 MATCH_NAME_TAKEN`). A bot can have three of its own matches running at once (`429 TOO_MANY_RUNNING_MATCHES`, listing them); starting another first surrenders any it has stopped playing, meaning no `state` or `commands` call for 40 seconds, so a bot that crashed and restarted isn't locked out. |
| `GET /api/v1/matches?status=Waiting` | Open matches you can join, with their `name` if they have one. |
| `GET /api/v1/matches/named/{name}` | The match with that name: the one waiting or running, otherwise the most recent. The spectator page takes names too: `/#/match/{name}`. |
| `POST /api/v1/matches/{id}/join` | Take a free seat. League matches (preseason, season, championship) take Discord-verified bots only (`403 NOT_VERIFIED`), one per person. |
| `POST /api/v1/matches/{id}/leave` | Give up your seat before the match starts. |
| `GET /api/v1/matches/{id}/map` | Terrain and every start position (public knowledge). |
| `GET /api/v1/matches/{id}/state` | Your fog-of-war view (see below). |
| `POST /api/v1/matches/{id}/commands` | Queue commands. |
| `DELETE /api/v1/matches/{id}/commands` | Drop everything still waiting in your queue. |
| `POST /api/v1/matches/{id}/surrender` | Concede. |
| `POST /api/v1/matches/{id}/speed` | Change a running match's speed: `{"tickIntervalMs":250}`, `{"paused":true}` or both. Only practice matches with at most one real player (exhibitions and games against house bots), so nobody's opponent can be sped up or paused; anyone may call it, and the spectator page has buttons for it. A pause lifts itself after five minutes. Your state's `tickIntervalMs` and `paused` show the current speed. |
| `POST /api/v1/matches/{id}/step` | Run one tick of a paused match. |
| `GET /api/v1/matches/{id}/result` | Winner, end reason, score breakdown and stats, including each player's detailed `stats` (`409` until it ends). |
| `GET /api/v1/matches/{id}/replay` | Seed + every executed command — enough to re-simulate the match exactly. |
| `GET /api/v1/matches/{id}/frames` | A finished match, tick by tick, as spectators saw it: the map, the whole play-by-play (`events`), and `chunkCount` chunks of `chunkTicks` (100) ticks. This is what the website's replay viewer plays. |
| `GET /api/v1/matches/{id}/frames/{n}` | Chunk `n`: one spectator state per line (newline-delimited JSON) for ticks `n × chunkTicks` onward. Sent Brotli-compressed when your client accepts `br`. |
| `GET /api/v1/bots` | House bots: `rusher`, `economist`, `balanced`, `sitter`. |
| `GET /api/v1/leaderboard?category=Season` | One category's ladder: `Practice` (the default, by Elo rating; only player-vs-player 1v1s change it, while games against house bots count toward your win/loss record), `Preseason`, `Season` or `Championship` (by points, then skill). Every row also has `skill` (and the raw `mu`/`sigma`), see below. Add `&season=2026-27` for a past season. |
| `GET /api/v1/players/me` | Your profile. `rating`/`wins`/`losses`/`draws` are your practice record; `records` has every category (rating, points, `skill`, record), and `verified`/`ownerName` your blue check. |
| `GET /api/v1/achievements`, `GET /api/v1/players/{id}/achievements` | Every achievement (how many bots have each, and who), and the ones a bot has earned. See [Achievements](#achievements). |
| `GET /api/v1/people`, `GET /api/v1/people/{slug}` | The Trophy Room (verified people ranked by their bots' achievements) and a person's public profile: their bots, achievements and recent matches. `ownerSlug` on bots and leaderboard rows names the profile (`/people/jordan`, and `#/player/jordan` on the site). |
| `GET /api/v1/matches/history?category=Season` | Finished matches, optionally one category only. |
| `GET /api/v1/matches/archive?player=rusher&skip=0&limit=25` | Every finished match, a page at a time (`total`, `skip`, `limit`, `matches`). `player` keeps matches with a bot or verified owner whose name contains it; commas separate names that must all be in the match. Optional `category`. |

**Practice and league matches.** Every match has a `category`, and each category is its own
leaderboard. Matches your bot creates are `Practice`, ranked by Elo rating, which only
player-vs-player 1v1s move.

`Preseason`, `Season` and `Championship` are **league matches**, run by the admins
(administrators of our Discord server). They work like any named match, with one difference: they
are played in **rounds**. For each round the admin splits everyone into **heats**, separate games of
a few players each, and starts every heat at once. With 20 bots, round 1 might be two heats of 10,
and round 2 another two heats of 10 with everyone reseated.

1. An admin creates the match, e.g. `season-week3`. It shows up in `GET /api/v1/matches` and on the
   home page.
2. Your verified bot joins it like any match: `GET /api/v1/matches/named/season-week3`, then
   `POST /api/v1/matches/{id}/join`. Each person may enter one bot. You can join before the first
   round or between rounds, but not while a round is split into heats or being played
   (`409 MATCH_CLOSED`); you then play from the next round on.
3. Long-poll `GET /api/v1/matches/{id}/state?waitForTick=0` as usual. It keeps answering
   `409 MATCH_NOT_STARTED` until the admin starts the round. Long-polling also shows the admin your
   bot is connected: a bot counts as connected if it made any API call in the last 40 seconds.
4. Once started, keep using the **same match id**. `state`, `commands`, `surrender`, `map` and
   `result` all answer for your heat; you'll see only the players in it. Nothing else changes.
5. When your heat ends, read your `result` as usual, then **long-poll `state?waitForTick=0` again**
   to wait for the next round, exactly as in step 3. Waiting for tick 0 skips a heat that has
   finished; every other call keeps answering for your last heat until the next round starts.
6. The admin decides how many rounds there are. When they **close the match**, that wait answers
   `409 MATCH_FINISHED`: there are no more rounds, and your bot can stop. The bot runner
   (`--join-match`) does all this for you.

Each round is seated at random, but the match remembers who started next to whom: if a seating
would put two bots side by side again, it is reshuffled (a small heat can't always avoid it). So a
bad draw of neighbours in one round isn't repeated in the next.

Spectators watch on the match's page, `/#/match/season-week3`. They see who has joined, how the
admin splits each round into heats (in seat order around the map), and then a live scoreboard per
heat that links to each heat's battle view. A heat has its own id and name (`season-week3-h2` in
round 1, `season-week3-r2-h1` in round 2) and its own result and replay.

Heats can't be sped up or paused. Everyone in a heat is rated by finishing place: the winner first,
then everyone else by final score. Every pair of players counts as a mini 1v1, scaled so that a big
heat moves a rating about as much as one 1v1. League leaderboards are ranked by **points**. In each
heat the top four earn their score margins over everyone below them in that four:

| Place | Points |
|---|---|
| 1st | (s1 − s2) + (s1 − s3) + (s1 − s4) |
| 2nd | (s2 − s3) + (s2 − s4) |
| 3rd | s3 − s4 |
| 4th and below | 0 |

So running away with a heat pays a lot, and scraping a win pays a little. A margin is never
negative. `Preseason` keeps warm-up and beta-test matches off the season's leaderboard.
`GET /api/v1/matches/{id}/standings` returns the live scoreboard per heat, with the points each
player would earn if it ended now.

**Dropping your worst heat.** If your bot plays two or more heats of the same league match (one
night), its lowest-scoring heat of that match doesn't count toward its season points: heats worth
120, 40 and 0 add 160, and 120 and 40 add 120. With a single heat nothing is dropped. A league
match's points go on the ladder all at once, when the admin closes the match; the results and the
ladder are then posted to Discord. (A match that is never closed, say because the server restarted,
is added up an hour after its last heat.) The live standings always show every heat's own points,
and the match page shows each bot's running total with its lowest heat crossed out.

**Skill.** Every ladder also shows a **skill**: an opponent-aware rating from
[OpenSkill](https://openskill.me) (the Weng-Lin Bayesian model with Plackett-Luce rankings, the
open cousin of TrueSkill). Each bot has a mean `mu` (starting at 25) and an uncertainty `sigma`
(starting at 25/3) that shrinks as it plays. After every rated game (the same games that move Elo) the
places are the winner first, then everyone by score (equal scores tie), and everyone's `mu` moves by how
surprising the result was given who was in the game: beating strong bots lifts you more than
beating weak ones, so a bad seating costs less than it would in points alone. The number shown is
the conservative estimate

    skill = max(0, round(40 × (mu − 3 × sigma)))

so every bot starts at 0 and climbs as results come in and the model grows surer (a bot at
mu 30, sigma 1.5 shows 1,020). League ladders are still ranked by points; skill is the second column,
a guide to who is actually strong, and breaks ties on points. The practice ladder stays ranked by Elo.

Seats are shuffled when a match starts, so your slot (and with it your start position and colour)
doesn't depend on when you joined; read `you.slot` from your state. The shuffle uses the match
seed, so a match created with a fixed `seed` and the same players in the same order starts the same way.
League heats are seated by their round instead (see above).

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
  "shells":    [ { "owner": 1, "attackerId": 240, "fromX": 40, "fromY": 12, "x": 34, "y": 12,
                   "firedTick": 41, "landsTick": 44 }, … ],
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
- `shells` are siege shots in flight: yours, plus any landing on a tile you can see. A shell hits
  whatever enemy of its owner is on tile `(x, y)` when `landsTick` ends, so get off that tile.
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
| Trebuchet | 350 | 45 | 150 | 40 | 0 | 3–7 | 8 | 7 | 5 | SiegeWorkshop |

Speed is in tenths of a tile per tick (10 = one tile per tick). Range and adjacency use Chebyshev
distance (diagonals count as 1); vision uses Euclidean distance.

**Siege.** The Trebuchet outranges guard towers (7 vs 5) but can't hit anything closer than 3 tiles
(`minRange`), and it is slow. It doesn't hit its target directly: it lobs a shell at the target's
tile, which lands `shotTicks` (3) ticks later and hits every enemy standing on that tile then,
units and building alike. Units that keep moving dodge it; buildings and units that stand still
don't. An idle Trebuchet fires at enemies that are standing still before ones on the move. Like any
unit it can only target what you can see, so pair it with spotters and an escort.

**Buildings**

| Building | Cost | Build work | HP | Armor | Vision | Notes |
|---|---|---|---|---|---|---|
| CommandCenter | 400 | 80 | 1500 | 2 | 6 | Trains workers. Ore drop-off. +1500 storage. Lose them all and you're out. |
| Barracks | 150 | 40 | 700 | 1 | 6 | Trains soldiers, archers, scouts. |
| ResourceDepot | 100 | 25 | 450 | 1 | 6 | Ore drop-off. +750 storage. |
| TechLab | 150 | 40 | 550 | 1 | 6 | Researches upgrades. Requires a Barracks. |
| GuardTower | 125 | 35 | 500 | 2 | 7 | Shoots enemy units: 10 damage, range 5. Requires a Barracks. |
| SiegeWorkshop | 250 | 60 | 650 | 1 | 6 | Trains trebuchets. Requires a TechLab and the SiegeEngineering upgrade (`requiresUpgrade` in the rules). |

Build work is in worker-ticks: two workers finish a Barracks in 20 ticks. Sites start at 10% HP and
gain the rest as they're built.

**Upgrades** (TechLab; each tier 2 needs its tier 1)

| Upgrade | Cost | Ticks | Effect |
|---|---|---|---|
| Weapons1 / Weapons2 | 150 / 250 | 40 / 60 | +2 damage each, all units and towers |
| Armor1 / Armor2 | 150 / 250 | 40 / 60 | +1 armor each, all units |
| Mobility1 / Mobility2 | 100 / 200 | 30 / 45 | +2 speed each, all units |
| Harvesting1 / Harvesting2 | 100 / 200 | 30 / 45 | +1 ore per mining tick each |
| SiegeEngineering | 200 | 60 | Unlocks the SiegeWorkshop |

Upgrades apply instantly to existing and future units.

**Economy** — workers mine 2 ore per tick (carrying up to 10), then walk it to the nearest
drop-off. Ore beyond your storage capacity is lost.

**Combat** — damage dealt is `max(1, damage − armor)`. Every attack in a tick lands at the same
time, so two units can kill each other. Idle soldiers/archers/scouts fight back against enemies
already in range; workers never attack unless ordered.

**Each tick runs in this order:** your queued commands → production & research → unit behaviour
and movement → shells due this tick land → combat → deaths → elimination & victory checks.

## Scoring and victory

- **Elimination** — a player with no Command Center (finished or under construction) is out. The
  last player standing wins.
- **Time limit** — at `maxTicks` (1800 by default, 30 minutes) the highest total score wins;
  equal totals are a draw.
- **Surrender** — conceding eliminates you immediately.

Score = **destruction** (ore value of enemy units and buildings you destroyed) + **economy** (ore
banked) + **survival** (ore value of your living units and finished buildings).

## Achievements

Bots earn achievements by playing. See them all, with how many bots have each, on the website's
**Achievements** page or `GET /api/v1/achievements`; a bot's own are at `GET /api/v1/players/{id}/achievements`,
and a person's account page shows every achievement any of their bots has earned.

- Only the first steps marked 🏠 count in matches against house bots. Everything else needs a **real
  opponent**: another bot that isn't a house bot and isn't owned by the same person.
- A win handed over by a surrender before tick 300 doesn't count as a win.
- Each is earned once per bot. Three are secret until someone finds them.
- Discord hears straight away when a bot is the first ever to earn one, and gets a nightly digest of the day's.
- The numbers behind them are in `GET /result`: each player's `stats` (units trained, buildings
  completed, research, peak workers and ore, ore lost to the storage cap, tower and serum kills,
  commands sent, rejected and failed, the longest a worker stood idle, and more).

| Group | Achievement | How to earn it | Tier |
|---|---|---|---|
| First steps | Hello, World 🏠 | Finish your first match. | Bronze |
| First steps | First Blood 🏠 | Kill an enemy unit. | Bronze |
| First steps | Breaking Ground 🏠 | Complete a building other than your starting Command Center. | Bronze |
| First steps | Boot Camp 🏠 | Train your first Soldier, Archer or Scout. | Bronze |
| First steps | Lab Coat 🏠 | Complete your first research. | Bronze |
| First steps | Training Wheels Off 🏠 | Beat a house bot. | Bronze |
| First steps | Full House 🏠 | Beat every house bot (rusher, economist, balanced and sitter), over any number of matches. | Silver |
| First steps | Blue Check 🏠 | Play a match after a Discord-verified owner claims your bot. | Bronze |
| First steps | Worthy Opponent | Play a match against another real bot. | Bronze |
| First steps | Suit Up | Play in a league match (preseason, season or championship). | Bronze |
| Winning | First Win | Win a match against a real bot. | Bronze |
| Winning | Double Digits | Win 10 matches against real bots. | Silver |
| Winning | Centurion | Win 100 matches against real bots. | Gold |
| Winning | Hat Trick | Win 3 matches against real bots in a row. | Silver |
| Winning | Unstoppable | Win 10 matches against real bots in a row. | Gold |
| Winning | Blitz | Win by elimination before tick 300. | Silver |
| Winning | Flawless Victory | Win without losing a single unit or building. | Gold |
| Winning | Photo Finish | Win on the time limit by less than 2% of the runner-up's score. | Silver |
| Winning | Buzzer Beater | Win by elimination in the last 60 ticks before the time limit. | Silver |
| Winning | Comeback Kid | Win after your score fell to half the leader's or less (from tick 100 on). | Gold |
| Winning | Last Bot Standing | Win a match with 8 or more players. | Gold |
| Winning | Battle Royale | Win a 16-player match. | Platinum |
| Winning | Giant Killer | Beat a bot rated 200 or more above you in a rated match. | Gold |
| Economy | Ore Inspector 🏠 | Gather 1,000 ore in one match. | Bronze |
| Economy | Ore Baron | Gather 10,000 ore in one match. | Silver |
| Economy | Ore Tycoon | Gather 20,000 ore in one match. | Gold |
| Economy | Strip Miner | Mine 3 resource deposits dry in one match. | Silver |
| Economy | Expansionist | Complete 3 Resource Depots in one match. | Bronze |
| Economy | Second Home | Complete a second Command Center. | Silver |
| Economy | Union Strong | Have 30 workers alive at once. | Silver |
| Economy | Dragon's Hoard | Bank 3,000 unspent ore at one moment. | Bronze |
| Economy | Waste Not | Finish a match of 600+ ticks without losing any ore to the storage cap. | Gold |
| Economy | Efficiency Expert | Research Harvesting 2. | Bronze |
| Economy | Lifetime Haul | Gather 1,000,000 ore across your matches against real bots. | Platinum |
| Combat | Massacre | Kill 50 enemy units in one match. | Silver |
| Combat | Wrecking Crew | Destroy 10 enemy buildings in one match. | Silver |
| Combat | Regicide | Destroy an enemy Command Center. | Bronze |
| Combat | Body Count | Kill 1,000 enemy units across your matches against real bots. | Gold |
| Combat | Hold the Line | Your guard towers kill 20 enemy units in one match. | Silver |
| Combat | Tower Defense | Complete 6 guard towers in one match. | Bronze |
| Combat | Robin Hood | Win having trained only Archers (no Soldiers or Scouts). | Gold |
| Combat | Shield Wall | Win having trained only Soldiers (no Archers or Scouts). | Silver |
| Combat | Cavalry Charge | Win having trained only Scouts as combat units. | Gold |
| Combat | Worker Uprising | Win without ever building a Barracks. | Platinum |
| Combat | Arms Race | Research Weapons 2 and Armor 2 in the same match. | Silver |
| Combat | Fully Loaded | Research all 8 upgrades in one match. | Gold |
| Combat | ??? | A secret, until someone earns it. | Gold |
| Combat | ??? | A secret, until someone earns it. | Platinum |
| Speed and precision | Quick Draw | Complete a Barracks before tick 60. | Silver |
| Speed and precision | First to the Fight | Have your first combat unit ready before tick 75. | Silver |
| Speed and precision | Eagle Eye | See an enemy Command Center before tick 100. | Silver |
| Speed and precision | Clean Code | Finish a match of 300+ ticks with no rejected or failed commands. | Silver |
| Speed and precision | Less Is More | Win having sent fewer than 50 commands. | Gold |
| Speed and precision | Busy Bees | Finish a match of 600+ ticks with no worker idle for more than 10 ticks in a row. | Gold |
| Speed and precision | Set It and Forget It | Set rally points on 3 different buildings in one match. | Bronze |
| Showing up | Regular | Play real bots on 7 different days. | Bronze |
| Showing up | Marathon | Play 100 matches against real bots. | Silver |
| Showing up | Iron Bot | Play 1,000 matches against real bots. | Gold |
| Showing up | Night Owl | Finish a match between midnight and 4 a.m. | Bronze |
| Showing up | Welcome Back | Win within a day of coming back from 30 or more days away. | Silver |
| Showing up | Social Butterfly | Play 10 different real bots. | Silver |
| Showing up | Rivalry | Play the same real bot 10 times. | Bronze |
| Showing up | Nemesis Defeated | Beat a bot that has beaten you 5 or more times. | Gold |
| Showing up | Glow Up | Win 5 more of your last 20 matches against real bots than of your first 20. | Gold |
| Showing up | Stable Owner | Your owner has 3 verified bots that have each beaten a real bot. | Gold |
| Ladder and league | Climber | Reach a rating of 1300 in any category (everyone starts at 1200). | Silver |
| Ladder and league | Elite | Reach a rating of 1500 in any category. | Gold |
| Ladder and league | Top Ten | Be in the top 10 of the season ladder when it's checked each night. | Gold |
| Ladder and league | Number One | Top the season ladder when it's checked each night. | Platinum |
| Ladder and league | Heat Winner | Win a season heat. | Gold |
| Ladder and league | Champion | Win a championship heat. | Platinum |
| Just for fun | Pacifist | Win on score without killing a single enemy unit. | Gold |
| Just for fun | Turtle Power | Survive to the time limit with 8 or more guard towers standing. | Silver |
| Just for fun | Know When to Fold 'Em 🏠 | Surrender a match. | Bronze |
| Just for fun | ??? | A secret, until someone earns it. | Gold |

## Errors

Every non-2xx response has the same body:

```json
{ "code": "NOT_A_PARTICIPANT", "message": "You are not playing in this match." }
```

Common ones: `401 UNAUTHORIZED`, `403 NOT_A_PARTICIPANT`, `403 NOT_VERIFIED` (claim your bot with
Discord first), `409 MATCH_CLOSED` (a round of the league match is split into heats or being played; join between rounds), `409 MATCH_FINISHED` (the league match has been closed), `409 OWNER_ALREADY_JOINED`, `409 PICK_A_HEAT`, `404 MATCH_NOT_FOUND`,
`409 MATCH_NOT_STARTED` (keep long-polling with `waitForTick=0`), `409 MATCH_FULL`,
`429 TOO_MANY_RUNNING_MATCHES` (surrender one, or let it finish),
`400 INVALID_JSON`, `413 REQUEST_TOO_LARGE` (bodies are capped at 256 KB), `429 RATE_LIMITED` (by default 30 requests/second per bot in each match, and
120/second across all of a bot's matches).

## Reference bots

- **C# starter, one file:** [`samples/csharp/StarterBot.cs`](../samples/csharp/StarterBot.cs). Save it and
  `dotnet run StarterBot.cs --player-name Frank`; small enough for a first programming class.
- **Python, no dependencies:** [`samples/python/bot.py`](../samples/python/bot.py).
- **C#:** `src/NetRts.Bots` has a typed client (`NetRtsClient`), the standard loop
  (`BotLoop.RunAsync`) and the house bots' `PlanBot` brain. The runner plays any of them against a
  server:

  ```bash
  dotnet run --project src/NetRts.BotRunner -- --server http://localhost:5080 --name my-bot --strategy balanced --vs rusher
  ```

- **Replays:** open any finished match on the website to watch it again, with play, pause,
  speed, step and a timeline. For analysis in code, `GET /frames` gives you every tick's full state,
  and `GET /result` + `GET /replay` the outcome and every command. In C#,
  `GameSimulation.FromReplay(replay)` re-runs the match tick for tick (under the current rules;
  `/frames` always shows what actually happened).
