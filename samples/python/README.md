# NetRts Python reference bot

A complete NetRts bot in one file (`bot.py`), using only the Python 3 standard library
(`urllib.request`, `json`, `argparse`). Read it top to bottom to see the whole protocol, then
port it to whatever language you like: it's just HTTP + JSON.

## Run it

You need Python 3.8+ and a running server:

```bash
dotnet run --project src/NetRts.Server          # serves http://localhost:5080
```

Then, from this folder:

```bash
# First run: registers the name and prints your API key. Save the key, it is shown only once.
python bot.py --name my-python-bot

# Later runs: reuse the key (or set NETRTS_API_KEY / NETRTS_SERVER in the environment).
python bot.py --key nrts_xxxxxxxx --vs sitter --tick-ms 200

# Play someone else: one side creates a match, the other joins it by id.
python bot.py --key $KEY --create                  # prints the match id
python bot.py --key $OTHER_KEY --join <match-id>   # or: netrts-bot ... --join <match-id>
```

| Option | Meaning |
|---|---|
| `--server URL` | API base URL (default `$NETRTS_SERVER` or `http://localhost:5080`) |
| `--name NAME` | Register this name (3-32 chars: letters, digits, `_`, `-`) |
| `--key KEY` | Use an existing API key (default `$NETRTS_API_KEY`) |
| `--vs BOT` | Play a house bot: `sitter` (default), `rusher`, `economist`, `balanced` |
| `--create` / `--players N` | Create a match with N seats (default 2) for others to join |
| `--join MATCH_ID` | Join a waiting match |
| `--tick-ms`, `--max-ticks`, `--seed` | Settings for a match you create (`--tick-ms 100` is the fastest allowed) |

The bot prints progress every 100 ticks and the final result (winner and score breakdown).

## What it does

The protocol, all in `play()`:

1. `POST /api/v1/players {name}` gives you `{playerId, apiKey}`. Send `Authorization: Bearer <apiKey>` on every other call.
2. `POST /api/v1/matches {houseBots: ["sitter"], settings: {...}}` (or `POST /api/v1/matches/{id}/join`). The match starts once every seat is taken.
3. `GET /api/v1/matches/{id}/state?waitForTick=0` waits until the match starts. While it is still waiting
   you get `409 MATCH_NOT_STARTED` after up to 30 s, so just ask again.
4. `GET /api/v1/rules` and `GET /api/v1/matches/{id}/map` return the static data (costs, terrain). The map only exists once the match has started.
5. Every tick: decide, then `POST .../commands {commands: [...]}`, then
   `GET .../state?waitForTick=<tick+1>&sinceTick=<tick>` blocks until the next tick has been simulated.
   `sinceTick` limits `events` to new ones.
6. When `status` is `Completed`, `GET .../result` has the winner and the scores.

The strategy, in `Bot.decide()`:

- **Economy:** idle workers mine the nearest visible deposit, with a penalty for deposits that already
  have miners so workers spread out. The Command Center trains workers up to 10.
- **Barracks:** with 6+ workers it saves up 150 ore and sends one worker to build a Barracks on an open
  tile 3-6 tiles from the Command Center. It checks the map terrain and avoids buildings and tiles next to ore.
- **Army:** every finished Barracks keeps 2 Soldiers queued.
- **Attack:** once there are 6 soldiers, idle soldiers attack the closest visible enemy. If no enemy is
  visible, they attack-move to the opponent's `startPosition`, where its Command Center is. Destroying
  every Command Center eliminates a player.

It beats `sitter` in about 230 ticks. `balanced` and `rusher` beat it, so improving on it is up to you. Some ideas:
add archers, research upgrades at a TechLab, build a second Barracks, retreat wounded units, or
defend against early rushes. `GET /api/v1/rules` has every number you need. `docs/bot-guide.md` covers the rules in full.

## Command cheat sheet

```json
{"type": "Gather",  "unitIds": [3, 4], "targetId": 12}
{"type": "Build",   "unitIds": [40], "buildingType": "Barracks", "x": 12, "y": 9}
{"type": "Produce", "buildingId": 31, "unitType": "Soldier", "count": 2}
{"type": "Attack",  "units": "all", "unitType": "Soldier", "x": 56, "y": 56}
{"type": "Attack",  "unitIds": [7, 8], "targetId": 99}
{"type": "Research","buildingId": 50, "upgrade": "Weapons1"}
{"type": "Move",    "unitIds": [1, 2], "tile": 392}
{"type": "Stop",    "units": "5-10"}
```

`units` also accepts `"all"`, `"idle"` and id lists or ranges like `"1-7,12"`. Commands are checked when
you submit them (rejections come back in `results`) and run on an upcoming tick. Failures at run time
show up as `CommandFailed` events.
