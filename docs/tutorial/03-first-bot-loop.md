# 3. Your first bot: the loop

[← Previous: Talk to the API by hand](02-talk-to-the-api.md) · [Tutorial index](README.md) · [Next: Put your workers to work →](04-put-workers-to-work.md)

Time to write code. This first bot gives no orders at all. It just joins a match and follows it
tick by tick, which is the skeleton every later step builds on.

Make a folder for your bot and copy the step 1 file into it:
[`code/step1_loop.cs`](code/step1_loop.cs) for C# or [`code/step1_loop.py`](code/step1_loop.py)
for Python. The Python file is about 130 lines. The C# file is about 220, because its last 40 or
so lines describe the server's JSON as C# types. Here are the important parts.

## Talking HTTP with the built-in libraries

Both languages can send HTTP requests out of the box: C# with `HttpClient`, Python with
`urllib.request`. We wrap that in one small helper, so the rest of the bot can just say
`Api<GameState>("GET", "/some/path")` in C# or `api("GET", "/some/path")` in Python:

```csharp
// Send one request to the server and return the JSON reply, read into a T.
async Task<T> Api<T>(string method, string path, object? body = null)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), server + path);
    if (body is not null)
        request.Content = JsonContent.Create(body, options: json);
    if (apiKey is not null)
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    using var response = await http.SendAsync(request);
    string text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        ...  // turn the {"code": ..., "message": ...} error body into an ApiError exception
    }
    return JsonSerializer.Deserialize<T>(text, json)!;
}
```

```python
def api(method, path, body=None):
    """Send one request to the server and return the decoded JSON reply (or None)."""
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(SERVER + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if API_KEY:
        request.add_header("Authorization", f"Bearer {API_KEY}")
    try:
        # A long poll can be held open for up to 30 s, so allow a generous timeout.
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        ...  # turn the {"code": ..., "message": ...} error body into an ApiError exception
```

It does exactly what `curl` did on the previous page: JSON body in, `Authorization` header on,
JSON reply out. When the server answers with an error, the helper raises `ApiError`, which has
the error's `code`, so the bot can react to specific errors.

**C# only:** Python turns the JSON into dictionaries, so it reads `state["you"]["slot"]`. In C#
the JSON is read into small *records* (classes that just hold data) declared at the bottom of the
file, so the same value is `state.You.Slot`. The JSON's camelCase names (`mapWidth`) match the
records' PascalCase properties (`MapWidth`) automatically. Here are a few of them:

```csharp
// GET /state: everything you can see this tick.
record GameState(int Tick, string Status, int MapWidth, int MapHeight, Me You, List<Player> Players,
                 List<Unit> Units, List<Building> Buildings, List<Deposit> Resources,
                 List<GameEvent> Events, Outcome? Outcome);
record Me(int Slot, int Resources, int StorageCapacity);
record Unit(int Id, int Owner, string Type, int X, int Y, int Hp, int MaxHp,
            string Activity, int Carrying, int? TargetId);
```

Unit types, activities and other names stay plain strings (`"Worker"`, `"Idle"`), just as in the
JSON. A record only needs the fields you use; the server sends a few more, which are ignored.

## Registering once

The API key is shown only once, so the bot saves it to a file in the folder you run it from
(`my-first-bot.key`) and reuses it from then on:

```csharp
// Reuse the key saved in <name>.key, or register the name and save the new key.
async Task<string> GetApiKey(string name)
{
    string keyFile = $"{name}.key";
    if (File.Exists(keyFile))
        return File.ReadAllText(keyFile).Trim();
    var reply = await Api<Registration>("POST", "/api/v1/players", new { name });
    File.WriteAllText(keyFile, reply.ApiKey);
    Console.WriteLine($"Registered '{name}'. API key saved to {keyFile}");
    return reply.ApiKey;
}
```

```python
def get_api_key(name):
    """Reuse the key saved in <name>.key, or register the name and save the new key."""
    key_file = f"{name}.key"
    if os.path.exists(key_file):
        with open(key_file) as f:
            return f.read().strip()
    reply = api("POST", "/api/v1/players", {"name": name})
    with open(key_file, "w") as f:
        f.write(reply["apiKey"])
    print(f"Registered '{name}'. API key saved to {key_file}")
    return reply["apiKey"]
```

Treat the `.key` file like a password: don't commit it to git or share it.

## Waiting for the start

`CreateMatch()` (`create_match()` in Python) sends the same `POST /api/v1/matches` you sent by
hand. The match then needs a moment to warm up, so we ask for tick 0 with `waitForTick=0`. If the
match hasn't started within the long-poll time, the server answers `409 MATCH_NOT_STARTED` and we
simply ask again:

```csharp
// waitForTick=0 blocks until the match has started (409 MATCH_NOT_STARTED means: ask again).
async Task<GameState> WaitForStart(string matchId)
{
    while (true)
    {
        try
        {
            return await Api<GameState>("GET", $"/api/v1/matches/{matchId}/state?waitForTick=0");
        }
        catch (ApiError e) when (e.Code == "MATCH_NOT_STARTED")
        {
            // Not started yet: go round and ask again.
        }
    }
}
```

```python
def wait_for_start(match_id):
    """waitForTick=0 blocks until the match has started (409 MATCH_NOT_STARTED means: ask again)."""
    while True:
        try:
            return api("GET", f"/api/v1/matches/{match_id}/state?waitForTick=0")
        except ApiError as e:
            if e.code != "MATCH_NOT_STARTED":
                raise
```

## The loop

This is the heart of every Badger Brawl bot:

```csharp
async Task Play(string matchId)
{
    var state = await WaitForStart(matchId);
    Console.WriteLine($"Started! We are slot {state.You.Slot} on a {state.MapWidth}x{state.MapHeight} map");

    while (state.Status != "Completed")
    {
        int me = state.You.Slot;
        var myUnits = state.Units.Where(u => u.Owner == me).ToList();
        Console.WriteLine($"tick {state.Tick,4}: ore {state.You.Resources,5}, {myUnits.Count} units");

        // Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        int tick = state.Tick;
        state = await Api<GameState>("GET", $"/api/v1/matches/{matchId}/state?waitForTick={tick + 1}&sinceTick={tick}");
    }

    Console.WriteLine($"Match over after {state.Outcome?.Ticks} ticks: {state.Outcome?.Reason}");
}
```

```python
def play(match_id):
    state = wait_for_start(match_id)
    print(f"Started! We are slot {state['you']['slot']} on a {state['mapWidth']}x{state['mapHeight']} map")

    while state["status"] != "Completed":
        me = state["you"]["slot"]
        my_units = [u for u in state["units"] if u["owner"] == me]
        print(f"tick {state['tick']:4}: ore {state['you']['resources']:5}, {len(my_units)} units")

        # Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        tick = state["tick"]
        state = api("GET", f"/api/v1/matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")

    print(f"Match over after {state['outcome']['ticks']} ticks: {state['outcome']['reason']}")
```

**Look** at the state, (later: **decide** and **order**), then **wait** for the next tick with
`waitForTick=tick+1`. The long poll means the loop runs exactly once per tick and never wastes
time sleeping or hammering the server. `sinceTick` makes sure each event shows up once, which
will matter on page 5.

> **Why not just sleep for a second** (`Thread.Sleep(1000)` in C#, `time.sleep(1)` in Python)?
> Your bot's clock and the server's drift apart, and the tick length can be changed per match.
> `waitForTick` always wakes you at the right moment.

## Run it

From the folder you copied the file into:

```bash
dotnet run step1_loop.cs -- --name my-first-bot --max-ticks 30     # C#
python step1_loop.py --name my-first-bot --max-ticks 30            # Python
```

In the C# command, everything after `--` goes to your bot rather than to `dotnet run` itself. The
first `dotnet run` of a file compiles it, which takes a few seconds; later runs start straight
away. (For Python on Windows you might need `py` instead of `python`.) `--max-ticks 30` makes a
30-second match, since our bot doesn't do anything yet. You should see:

```text
Registered 'my-first-bot'. API key saved to my-first-bot.key
Match 928bcfd4-66d9-4849-bf1c-098922dc5b07 created against 'sitter'
Watch it at http://localhost:5080/#/match/928bcfd4-66d9-4849-bf1c-098922dc5b07
Started! We are slot 0 on a 64x64 map
tick    0: ore   500, 5 units
tick    1: ore   500, 5 units
tick    2: ore   500, 5 units
...
tick   29: ore   500, 5 units
Match over after 30 ticks: TimeLimit
```

Open the printed link to watch. Your five workers stand still while sitter's get busy mining, and
when time runs out sitter wins on score. Run the command again and it reuses the saved key
instead of registering. (Both versions use the same key file, so you can switch languages without
registering a new name.)

The bot has a few more options (`dotnet run step1_loop.cs -- --help` or
`python step1_loop.py --help`):

| Option | Meaning |
|---|---|
| `--vs BOT` | Which house bot to play (`sitter`, `rusher`, `economist`, `balanced`). |
| `--tick-ms N` | Milliseconds per tick. `200` makes testing five times faster (minimum `100`). |
| `--max-ticks N` | End the match after N ticks (default 1800). |
| `--seed N` | Use the same map again. |
| `--server URL` | A server other than `http://localhost:5080`. |

If you press Ctrl+C, the bot surrenders the match on the way out so it doesn't keep running
without you.

## Recap

- One helper function handles all the HTTP and JSON.
- Register once and keep the key.
- `waitForTick=0` waits for the start; `waitForTick=tick+1` waits for each next tick.
- The loop is: look, decide, order, wait. We've done *look* and *wait*.

[← Previous: Talk to the API by hand](02-talk-to-the-api.md) · [Tutorial index](README.md) · [Next: Put your workers to work →](04-put-workers-to-work.md)
