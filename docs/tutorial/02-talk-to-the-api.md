# 2. Talk to the API by hand

[← Previous: Start the server](01-start-the-server.md) · [Tutorial index](README.md) · [Next: Your first bot →](03-first-bot-loop.md)

A bot is just a program that sends HTTP requests. Before writing one, let's send those requests
ourselves and read the answers. Everything here is exactly what your bot will do later.

Every example comes in two flavours:

- **bash** (Linux, macOS, Git Bash on Windows) using `curl`;
- **PowerShell** (Windows) using `Invoke-RestMethod`, which turns the JSON reply into objects for you.

> In Windows PowerShell, `curl` is a different command. Use `Invoke-RestMethod` as shown, or
> call the real one as `curl.exe`.

Keep the server from page 1 running.

## 1. Register a player

You register once and get an **API key**: your bot's password. The server shows it **only
once**, so copy it somewhere.

```bash
curl -s -X POST http://localhost:5080/api/v1/players \
     -H "Content-Type: application/json" \
     -d '{"name": "my-curl-bot"}'
```

```json
{"playerId":"a60ebd7b-e9b9-4e08-9d5a-df5950cd567f","name":"my-curl-bot","apiKey":"nrts_NqxI3iwCj7vyv-M46adB6Jj-WWdiGRzZfNB2dMBq6l8"}
```

Keep the key in a shell variable so you don't have to paste it into every command:

```bash
KEY=nrts_NqxI3iwCj7vyv-M46adB6Jj-WWdiGRzZfNB2dMBq6l8     # yours will differ
```

PowerShell:

```powershell
$reg = Invoke-RestMethod http://localhost:5080/api/v1/players -Method Post `
         -ContentType "application/json" -Body '{"name": "my-curl-bot"}'
$reg.apiKey
$h = @{ Authorization = "Bearer $($reg.apiKey)" }    # the header every other call needs
```

If the name is already taken you get an error instead: `{"code":"NAME_TAKEN", ...}`. Every
error from the API has this shape: a machine-readable `code` and a human `message`.

## 2. Start a match against `sitter`

`sitter` is the easiest house bot: it mines ore and never fights. Perfect for target practice.

```bash
curl -s -X POST http://localhost:5080/api/v1/matches \
     -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
     -d '{"houseBots": ["sitter"], "settings": {"maxTicks": 600}}'
```

```powershell
$match = Invoke-RestMethod http://localhost:5080/api/v1/matches -Method Post -Headers $h `
           -ContentType "application/json" -Body '{"houseBots": ["sitter"], "settings": {"maxTicks": 600}}'
$m = $match.matchId
```

```jsonc
{"matchId":"2d07d955-5aa9-4c53-83d8-18c7c51f6fb1","status":"Active","maxPlayers":2,"tick":0,
 "maxTicks":600,"tickIntervalMs":1000,"mapWidth":64,"mapHeight":64,"seed":880822769, ... }
```

The `Authorization: Bearer <key>` header tells the server who you are. Because the other seat is
taken by a house bot, the match starts right away. `maxTicks: 600` ends it after 10 minutes
instead of the default 30. In bash, save the id: `MATCH=2d07d955-...`.

Open `http://localhost:5080/#/match/<matchId>` in your browser to watch, or click the match in
the home page's *Live matches* list. You're orange, in the top-left corner.

## 3. Look at the game state

```bash
curl -s "http://localhost:5080/api/v1/matches/$MATCH/state" -H "Authorization: Bearer $KEY" | python -m json.tool
```

```powershell
$s = Invoke-RestMethod "http://localhost:5080/api/v1/matches/$m/state" -Headers $h
$s.you
$s.units | Format-Table id, owner, type, x, y, activity, carrying
```

(`python -m json.tool` just pretty-prints the JSON. Without Python, leave it off, or use `jq .`
instead.) Here's what comes back, trimmed:

```jsonc
{
  "tick": 2, "status": "Active", "maxTicks": 600, "mapWidth": 64, "mapHeight": 64,
  "you": { "slot": 0, "name": "my-curl-bot", "resources": 500, "storageCapacity": 1500, ... },
  "players": [
    { "slot": 0, "name": "my-curl-bot",  "startPosition": {"x": 7,  "y": 7},  "score": {...}, ... },
    { "slot": 1, "name": "house-sitter", "startPosition": {"x": 56, "y": 56}, "score": {...}, ... }
  ],
  "units": [
    { "id": 66, "owner": 0, "type": "Worker", "x": 6, "y": 6, "hp": 40, "maxHp": 40,
      "activity": "Idle", "carrying": 0, "targetId": null, "destination": null },
    ... four more workers ...
  ],
  "buildings": [
    { "id": 65, "owner": 0, "type": "CommandCenter", "x": 7, "y": 7, "hp": 1500, "maxHp": 1500,
      "completed": true, "constructionPercent": 100, "production": [], "research": null }
  ],
  "resources": [
    { "id": 1, "x": 5, "y": 2, "remaining": 1000 },
    { "id": 3, "x": 7, "y": 2, "remaining": 1000 },
    ... seven more deposits ...
  ],
  "visibility": [ "0000000000000000...", "0000001110000000...", "0001111111110000...", ... ],
  "events": [],
  "outcome": null
}
```

The important parts:

| Field | What it tells you |
|---|---|
| `tick` | How far the match has got. |
| `you` | Your **slot** (player number) and your ore (`resources`). You start with 500 ore. |
| `players` | Everyone in the match, including where each one started: `startPosition`. The enemy's Command Center is there. |
| `units`, `buildings` | Everything **you can see**: all of yours, plus enemies inside your vision. `owner` is a slot number, so `owner == you.slot` means "mine". |
| `resources` | Ore deposits you can see. Workers mine these. |
| `visibility` | One string per map row: `'1'` means you can see that tile right now, `'0'` means fog. |
| `events` | Things that happened to you recently: units produced, buildings finished, commands that failed... |

Every unit, building and deposit has an `id`, and ids never repeat, so an id identifies exactly
one thing. You'll use them to give orders.

## 4. Give an order

Your five workers are standing around (`"activity": "Idle"`). Send them to mine deposit 3, the
one at (7, 2). (If your `resources` list looks different, use any deposit id from it.) Orders go
to the `commands` endpoint as a list, so let's also add a deliberately bad one to see what
happens:

```bash
curl -s -X POST "http://localhost:5080/api/v1/matches/$MATCH/commands" \
     -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
     -d '{"commands": [
           {"type": "Gather", "units": "all", "unitType": "Worker", "targetId": 3},
           {"type": "Move",   "units": "all", "x": 99, "y": 3}
         ]}'
```

```powershell
$body = '{"commands": [
  {"type": "Gather", "units": "all", "unitType": "Worker", "targetId": 3},
  {"type": "Move",   "units": "all", "x": 99, "y": 3}
]}'
Invoke-RestMethod "http://localhost:5080/api/v1/matches/$m/commands" -Method Post -Headers $h `
  -ContentType "application/json" -Body $body | ConvertTo-Json -Depth 5
```

```json
{"accepted":1,"rejected":1,
 "results":[{"index":0,"accepted":true,"commandId":8,"error":null},
            {"index":1,"accepted":false,"commandId":null,
             "error":{"code":"OUT_OF_BOUNDS","message":"(99,3) is outside the 64x64 map."}}],
 "queueSize":1,"queueCapacity":500}
```

Each command gets its own verdict. The `Gather` was **accepted** and queued; it runs at the start
of the next tick. The `Move` was **rejected** straight away because x = 99 is off the map. One bad
command never spoils the others.

`"units": "all", "unitType": "Worker"` means "all my workers". You can also pick units by id:
`"unitIds": [66, 67]`. The [bot guide](../bot-guide.md#commands) lists every command and
selector.

Look at the state again after a few seconds. The workers now say `"activity": "Gathering"` and
`"carrying"` goes up as they mine, and in the browser you'll see them walk to the ore.

## 5. Wait for a tick: long polling

Asking for the state over and over to see if a tick has passed would be wasteful. Instead, add
`waitForTick`:

```bash
curl -s "http://localhost:5080/api/v1/matches/$MATCH/state?waitForTick=60" -H "Authorization: Bearer $KEY"
```

The server **holds the request open** until tick 60 has happened, then answers. This is called a
*long poll*. Your bot will use it to wake up exactly once per tick. (A long poll gives up after
about 30 seconds, so ask for a tick that's coming soon.)

## 6. End the match

You can't win by typing, so concede:

```bash
curl -s -X POST "http://localhost:5080/api/v1/matches/$MATCH/surrender" -H "Authorization: Bearer $KEY"
```

```powershell
Invoke-RestMethod "http://localhost:5080/api/v1/matches/$m/surrender" -Method Post -Headers $h
```

Then `GET /api/v1/matches/$MATCH/result` shows who won and the score breakdown.

## The API reference page

Everything above, and more, is in the interactive API reference at <http://localhost:5080/scalar>
(the **API docs** link on the home page; the old `/swagger` address takes you there too):

![The Badger Brawl API reference page. The left sidebar lists the endpoints (start an exhibition, create a match, join, leave, map, state, commands and more), each marked GET, POST or DELETE. On the right are a Server box showing http://localhost:5080, an Authentication box with a Bearer Token field, and Client Libraries tabs for Shell, Ruby, Node.js, PHP and Python.](images/02-api-reference.png)

What you can do there:

- **Browse the endpoints** in the left sidebar, or search them with Ctrl+K. Each one shows its
  path and query parameters and the responses it can return, including the error codes.
- **See the schemas**: the exact JSON shape of every request body and reply, with example values.
  It's handy when you want to know every field `/state` returns.
- **Enter your API key** once in the **Authentication** box on the front page (paste the key into
  *Bearer Token*), and every request you send from the page will use it.
- **Send requests** with an endpoint's **Test Request** button: fill in the parameters (a
  `matchId`, say), send it, and read the server's reply. It's another way to do everything on
  this page without a terminal. Each endpoint also shows the same call as code to copy, in
  several languages.

For a friendlier explanation of each endpoint, see the [bot guide](../bot-guide.md).

## Recap

- `POST /api/v1/players` once to get an API key. Send it as `Authorization: Bearer <key>`.
- `POST /api/v1/matches` with `"houseBots": ["sitter"]` starts a game.
- `GET .../state` shows what you can see; `?waitForTick=N` waits for tick N.
- `POST .../commands` sends orders, and each one is accepted or rejected with a reason.

That's the whole game loop: **look, decide, order, wait**. Next, we'll write a program to do it.

[← Previous: Start the server](01-start-the-server.md) · [Tutorial index](README.md) · [Next: Your first bot →](03-first-bot-loop.md)
