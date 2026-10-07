# 7. Where next

[← Previous: Attack and win](06-attack-and-win.md) · [Tutorial index](README.md)

Your bot beats `sitter`. Against a bot that fights back it's a different story: in our test games
it beat `rusher` and `economist` but lost badly to `balanced`, and other players' bots will be
tougher still. Here are ideas for where to take it next, roughly from easiest to hardest. Keep
your step 4 file ([`step4_attack.cs`](code/step4_attack.cs) or
[`step4_attack.py`](code/step4_attack.py)) as your starting point and the
[bot guide](../bot-guide.md) open for the rules.

## Play the other house bots

```bash
dotnet run step4_attack.cs -- --name my-first-bot --vs balanced --tick-ms 200     # C#
python step4_attack.py --name my-first-bot --vs balanced --tick-ms 200            # Python
```

| House bot | Plays like |
|---|---|
| `sitter` | Only mines. You've beaten it. |
| `rusher` | Few workers, two Barracks, attacks early with soldiers and scouts. |
| `economist` | Big economy, guard towers, upgrades, a large late push. |
| `balanced` | Steady economy, soldiers plus archers, upgrades, attacks in waves of ten. |

Watch the matches in the spectate page: seeing *how* you lose is the fastest way to improve. Use
`--seed` to replay the same map while you compare changes.

## Ideas to try

**Spend everything.** Look at the output on page 5: once the soldiers start coming, the bot's
ore still creeps upwards (320 at tick 200), because one Barracks can only train a soldier every
12 ticks. A second Barracks doubles your
army's growth.

**Mix your army.** Archers (125 ore) have 60 HP but range 4: they hit soldiers before they can
hit back. A few behind a wall of soldiers is a classic combination. Scouts (75 ore) are fast and
see far (vision 8), so they're good for finding the enemy and spotting attacks early.

**Defend your base.** `rusher` attacks early, while your own army may be away. Watch for
enemies inside your vision near your Command Center, or for `UnitLost` events, and pull your
soldiers home to fight. A **Guard Tower** (125 ore, needs a Barracks) shoots any enemy within 5
tiles.

**Don't attack into a losing fight.** Compare the number and health of enemies you can see
with your own army. Retreat (a `Move` order back home) when you're outnumbered, and regroup
before going in again.

**Upgrades.** A **TechLab** (needs a Barracks) researches upgrades with the `Research` command:
`Weapons1` (+2 damage for every unit), `Armor1` (+1 armor), `Mobility1` (+2 speed) and
`Harvesting1` (+1 ore per mining tick). See the [upgrade table](../bot-guide.md#game-rules).

**Expand.** Richer ore sits near the centre of the map. A **Resource Depot** (100 ore) next to a
far deposit gives your workers a closer drop-off point and adds 750 storage.

**Stop hard-coding numbers.** `GET /api/v1/rules` returns every cost, stat and build time. Read it
once at the start (as the Python sample bot [`samples/python/bot.py`](../../samples/python/bot.py)
does) and your bot keeps working if the balance changes.

**Play your friends.** The tutorial bot only plays house bots. The Python reference bot
[`samples/python/bot.py`](../../samples/python/bot.py) adds `--create` (make a match and print its
id) and `--join <matchId>`, so two people's bots can meet. It's worth reading even if you write
C#: it has the same structure as the tutorial bot, in one file, and adding the same two options
to your own bot is a good exercise. (`--join` uses `POST /api/v1/matches/{id}/join`, and `--create`
is a `POST /api/v1/matches` without `houseBots`.) Its [README](../../samples/python/README.md) has a
command cheat sheet.

## Learn from your matches

- `GET /api/v1/matches/{id}/result` (page 6) has units produced, lost and killed and buildings
  destroyed for each player. Compare wins with losses.
- `GET /api/v1/matches/{id}/replay` returns the map seed and every command each player sent,
  tick by tick, which is enough to re-run the whole match exactly. Ever wondered what `balanced`
  actually does? Its commands are in there.
- `GET /api/v1/leaderboard` is the Elo ladder shown on the home page.

## A sparring partner in C#

The server's house bots are also available as a command-line runner, so you can set up any
house-bot strategy as a *player* in a match:

```bash
dotnet run --project src/NetRts.BotRunner -- --help
```

```text
Usage:
  netrts-bot --server http://localhost:5080 --name mybot [--key <apiKey>] --strategy balanced
             (--vs <houseBot> | --create [--players 2] | --join <matchId>)
             [--tick-ms 1000] [--max-ticks 1800] [--seed N]
```

For example, the runner can `--create` a match with `--strategy rusher`, and once you've added
`--join` to your bot (or used the sample bot), you can play it. When your C# bot outgrows one
file, `src/NetRts.Bots` has a complete typed client (`NetRtsClient`), a standard bot loop and the
house bots' source code to learn from.

## More reading

- [Bot guide](../bot-guide.md): every endpoint, command, selector, error code and game rule.
- [Getting started](../getting-started.md): other ways to install and run NetRts.
- [How it works](../how-it-works.md): what happens inside the server each tick.
- <http://localhost:5080/scalar>: the live, interactive API reference (page 2 shows how to use it).

Good luck, and have fun out-thinking the other bots!

[← Previous: Attack and win](06-attack-and-win.md) · [Tutorial index](README.md)
