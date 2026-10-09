# Badger Brawl — Getting Started 🦡

**Badger Brawl is Snow College's real-time strategy game, and you don't play it with a mouse.** You write a program — a
*bot* — that commands an army over HTTP. Every second the game advances one *tick*; between ticks
your bot looks at what its units can see and sends orders. Destroy the enemy's Command Center, or
have the higher score when time runs out, and you win.

Any language that can send HTTP requests and read JSON will do. Code examples in these docs come in
C# and Python; pick your language on any example and the rest follow.

> **Just want to play?** Save the one-file [C# starter bot](../samples/csharp/StarterBot.cs) and run
> `dotnet run StarterBot.cs --player-name Frank`. It signs up, plays a house bot on
> <https://netrts.snowse.io> and gives you a `Decide` method to improve.
> [Its README](../samples/csharp/README.md) has the details. Read on to learn the API itself.

## 1. Get a server

Your organiser will give you a server address (we'll write it as `$SERVER`). Practising on your own
machine? Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), clone the
repository and run:

```bash
dotnet run --project src/NetRts.Server        # then SERVER=http://localhost:5080
```

Open `$SERVER` in a browser to watch matches, and `$SERVER/scalar` for interactive API docs where you can try every call.

## 2. Register your bot (once)

```bash
curl -X POST $SERVER/api/v1/players -H "content-type: application/json" -d '{"name":"my-bot"}'
```

The reply contains an `apiKey` like `nrts_…`. **It is shown only once — save it.** Send it on every
other request as `Authorization: Bearer nrts_…`.

## 3. Start a practice match

```bash
curl -X POST $SERVER/api/v1/matches -H "Authorization: Bearer $KEY" \
     -H "content-type: application/json" -d '{"houseBots":["sitter"]}'
```

`sitter` only mines, which makes it the perfect first opponent. Later try `balanced`, `rusher` and
`economist`. Note the `matchId` in the reply, and watch the match at `$SERVER/#/match/<matchId>`.

## 4. The bot loop

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var http = new HttpClient { BaseAddress = new Uri(server) };
http.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);

// waitForTick=0 waits for the match to start
var state = await http.GetFromJsonAsync<JsonObject>($"api/v1/matches/{matchId}/state?waitForTick=0");
while ((string?)state!["status"] != "Completed")
{
    JsonArray commands = Decide(state);                       // your strategy
    if (commands.Count > 0)
        await http.PostAsJsonAsync($"api/v1/matches/{matchId}/commands", new { commands });

    var tick = (int)state["tick"]!;
    state = await http.GetFromJsonAsync<JsonObject>(
        $"api/v1/matches/{matchId}/state?waitForTick={tick + 1}&sinceTick={tick}");
}
```

```python
import json, urllib.request

def call(method, path, body=None):
    request = urllib.request.Request(
        f"{SERVER}/api/v1/{path}", method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Authorization": f"Bearer {API_KEY}", "Content-Type": "application/json"})
    with urllib.request.urlopen(request) as response:
        return json.load(response)

# waitForTick=0 waits for the match to start
state = call("GET", f"matches/{match_id}/state?waitForTick=0")
while state["status"] != "Completed":
    commands = decide(state)                                  # your strategy
    if commands:
        call("POST", f"matches/{match_id}/commands", {"commands": commands})

    tick = state["tick"]
    state = call("GET", f"matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")
```

`waitForTick` makes the server hold your request until the next tick has happened, so you never
need to sleep or spam requests.

## 5. What you see

The state tells you your `resources` (ore), your `units` and `buildings`, any enemy units and
buildings currently inside your vision, the ore `resources` you can see, a `visibility` grid, and
`events` (things that just happened — including commands that failed). Everything has a numeric
`id`; `owner` is a player slot number, and yours is `you.slot`.

## 6. What you can do

| Command | Example |
|---|---|
| Mine | `{"type":"Gather","units":"all","unitType":"Worker","targetId":4}` |
| Train | `{"type":"Produce","buildingId":30,"unitType":"Worker"}` |
| Rally | `{"type":"Rally","buildingId":30,"x":2,"y":6}` (new units go there; workers sent to ore start mining) |
| Build | `{"type":"Build","unitIds":[31],"buildingType":"Barracks","x":12,"y":9}` |
| Move | `{"type":"Move","units":"5-10","x":20,"y":20}` |
| Attack | `{"type":"Attack","units":"all","unitType":"Soldier","x":56,"y":56}` (fight your way there) or `"targetId":77` |
| Upgrade | `{"type":"Research","buildingId":50,"upgrade":"Weapons1"}` (needs a TechLab) |
| Stop | `{"type":"Stop","units":"idle"}` |

Select units with ranges (`"units":"1-7,12"`), `"all"`, `"idle"`, a list (`"unitIds":[5,6]`), and
narrow by type with `"unitType"`. Each command in your batch gets accepted or rejected with an
error code, so read the reply.

## 7. A first strategy that wins

1. Send every idle worker to mine the nearest ore.
2. Keep your Command Center training workers until you have about 10.
3. When you have 150 ore, have one worker build a **Barracks** 3–6 tiles from your base.
4. Train **Soldiers** from it non-stop.
5. With 6 or more soldiers, attack-move them to the enemy's `startPosition` (listed in `players`).

That beats `sitter`. Beating `balanced` takes more thought — scouting, upgrades, towers, timing.

## Rules in one breath

64×64 map · you start with a Command Center, 5 workers and 500 ore · workers carry 10 ore per trip ·
Worker 50, Soldier 100, Archer 125 (ranged), Scout 75 (fast) · Barracks 150, Resource Depot 100,
TechLab 150, Guard Tower 125 · damage minus armor, minimum 1 · lose your last Command Center and
you're out · otherwise highest score after 1800 ticks wins (destruction + ore mined + value of
what survives).

## Where next

- **[Tutorial](tutorial/README.md)** — build a winning Python bot step by step, with screenshots.
- **[Bot guide](bot-guide.md)** — the full reference: every command, error code and stat.
- **[samples/python/bot.py](../samples/python/bot.py)** — a complete bot with no dependencies.
- **C# runner** — `dotnet run --project src/NetRts.BotRunner -- --help`.
- **Stuck?** Check the `events` in your state for `CommandFailed` messages, and watch your bot in
  the browser — you'll usually see exactly what's going wrong.
