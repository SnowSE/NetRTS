// NetRts tutorial, step 3: build a Barracks and train an army.
//
// Step 2's economy, plus: pick a build site from the map terrain, send one worker
// to build a Barracks, train Soldiers there, and report rejected commands and
// CommandFailed events. The soldiers just stand around for now.
//
//     dotnet run step3_army.cs -- --name my-bot --max-ticks 300
//
// This is a .NET 10 "file-based app": no project file is needed, and only the
// built-in .NET libraries are used.

// Run as ordinary .NET rather than native AOT, so System.Text.Json can fill in our records
// without extra setup.
#:property PublishAot=false

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

string server = "http://localhost:5080";
string? apiKey = null;  // filled in by GetApiKey()

// A long poll can be held open for up to 30 s, so allow a generous timeout.
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

// The API's JSON names are camelCase ("mapWidth"); our C# records use PascalCase (MapWidth).
// The Web defaults translate between the two. Values we leave as null aren't sent at all.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};


// --------------------------------------------------------------------------- HTTP helper

// Send one request to the server and return the JSON reply, read into a T.
// (ApiError and the records the JSON is read into are at the bottom of the file.)
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
        // Turn the {"code": ..., "message": ...} error body into an ApiError exception.
        ErrorBody? error = null;
        try { error = JsonSerializer.Deserialize<ErrorBody>(text, json); } catch (JsonException) { }
        throw new ApiError((int)response.StatusCode, error?.Code ?? "HTTP_ERROR",
                           error?.Message ?? response.ReasonPhrase ?? "");
    }
    return JsonSerializer.Deserialize<T>(text, json)!;
}


// --------------------------------------------------------------------------- setup

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

async Task<string> CreateMatch(string opponent, int? tickMs, int? maxTicks, int? seed)
{
    // Settings left as null aren't sent, so the server uses its defaults for them.
    var settings = new { tickIntervalMs = tickMs, maxTicks, seed };
    var match = await Api<MatchInfo>("POST", "/api/v1/matches", new { houseBots = new[] { opponent }, settings });
    Console.WriteLine($"Match {match.MatchId} created against '{opponent}'");
    Console.WriteLine($"Watch it at {server}/#/match/{match.MatchId}");
    return match.MatchId;
}

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


// --------------------------------------------------------------------------- strategy

const int TargetWorkers = 12;
const int WorkerCost = 50, SoldierCost = 100, BarracksCost = 150;  // GET /api/v1/rules has every cost

// These live outside any function, so they keep their values from one tick to the next.
List<string> terrain = [];      // the map's rows: '.' = open ground, '#' = rock (loaded once in Play())
int? barracksOrderedAt = null;  // tick when we last sent a worker to build a Barracks
int? barracksBuilderId = null;  // the worker we sent to build it

// Tiles between two points, counting diagonal steps as 1 (the game's own measure).
static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

// An open tile 3-6 tiles from the Command Center that doesn't get in the miners' way.
(int X, int Y)? FindBuildSite(Building commandCenter, GameState state)
{
    var taken = state.Buildings.Select(b => (b.X, b.Y)).ToHashSet();
    var ore = state.Resources.Select(d => (d.X, d.Y)).ToList();
    int cx = commandCenter.X, cy = commandCenter.Y;
    (int X, int Y)? best = null;
    int bestScore = 0;
    for (int y = cy - 6; y <= cy + 6; y++)
    {
        for (int x = cx - 6; x <= cx + 6; x++)
        {
            if (x < 0 || x >= state.MapWidth || y < 0 || y >= state.MapHeight)
                continue;  // off the map
            if (terrain[y][x] != '.' || taken.Contains((x, y)))
                continue;  // rock, or a building is already there
            int d = Distance(x, y, cx, cy);
            if (d < 3 || d > 6)
                continue;  // too close (blocks the CC) or too far
            int toOre = ore.Select(o => Distance(x, y, o.X, o.Y)).DefaultIfEmpty(99).Min();
            if (toOre <= 1)
                continue;  // on or right next to a deposit: workers need that space
            int score = d - Math.Min(toOre, 4);  // close to the CC, but away from the ore
            if (best is null || score < bestScore)
            {
                best = (x, y);
                bestScore = score;
            }
        }
    }
    return best;
}

// Look at this tick's state and return a list of commands to send.
List<Command> Decide(GameState state)
{
    int me = state.You.Slot;
    int tick = state.Tick;
    int budget = state.You.Resources;  // ore we haven't promised to anything yet this tick
    var workers = state.Units.Where(u => u.Owner == me && u.Type == "Worker").ToList();
    var myBuildings = state.Buildings.Where(b => b.Owner == me).ToList();
    var commandCenter = myBuildings.FirstOrDefault(b => b.Type == "CommandCenter");
    var barracks = myBuildings.Where(b => b.Type == "Barracks").ToList();  // finished or under construction
    var commands = new List<Command>();

    // 1. Build one Barracks once 6 workers are mining. Until it exists, save ore for it.
    bool savingForBarracks = commandCenter is not null && barracks.Count == 0 && workers.Count >= 6;
    bool recentlyOrdered = barracksOrderedAt is not null && tick - barracksOrderedAt < 30;
    if (commandCenter is not null && savingForBarracks && !recentlyOrdered && budget >= BarracksCost)
    {
        if (FindBuildSite(commandCenter, state) is (int x, int y))
        {
            // The worker closest to the CC and not carrying ore makes the best builder.
            var builder = workers.MinBy(w => (w.Carrying, Distance(w.X, w.Y, commandCenter.X, commandCenter.Y)))!;
            commands.Add(new Command("Build", UnitIds: [builder.Id], BuildingType: "Barracks", X: x, Y: y));
            budget -= BarracksCost;
            barracksOrderedAt = tick;
            barracksBuilderId = builder.Id;
            workers.Remove(builder);  // don't send the builder off mining in step 2 below
            Console.WriteLine($"tick {tick}: worker {builder.Id} will build a Barracks at ({x}, {y})");
        }
    }

    // 2. Idle workers go mining. Count how many workers already mine each deposit and
    //    prefer close deposits with few miners, so the workers spread out.
    var deposits = state.Resources;  // only the deposits we can currently see
    if (deposits.Count > 0)
    {
        var miners = deposits.ToDictionary(d => d.Id, d => 0);
        foreach (var w in workers)
        {
            if (w.TargetId is int target && miners.ContainsKey(target))
                miners[target]++;
        }
        foreach (var w in workers)
        {
            if (w.Activity != "Idle")
                continue;
            var best = deposits.MinBy(d => Distance(w.X, w.Y, d.X, d.Y) + 3 * miners[d.Id])!;
            miners[best.Id]++;
            commands.Add(new Command("Gather", UnitIds: [w.Id], TargetId: best.Id));
        }
    }

    // 3. Train more workers, one or two at a time, until we have TargetWorkers
    //    (but not while we're saving up for the Barracks).
    if (commandCenter is { Completed: true } && !savingForBarracks)
    {
        int queued = commandCenter.Production.Count;
        if (workers.Count + queued < TargetWorkers && queued < 2 && budget >= WorkerCost)
        {
            commands.Add(new Command("Produce", BuildingId: commandCenter.Id, UnitType: "Worker"));
            budget -= WorkerCost;
        }
    }

    // 4. Every finished Barracks keeps two Soldiers in its queue.
    foreach (var b in barracks)
    {
        if (b.Completed && b.Production.Count < 2 && budget >= SoldierCost)
        {
            commands.Add(new Command("Produce", BuildingId: b.Id, UnitType: "Soldier"));
            budget -= SoldierCost;
        }
    }

    return commands;
}

// POST the commands and report any the server rejected straight away.
async Task Send(string matchId, List<Command> commands)
{
    var reply = await Api<CommandReply>("POST", $"/api/v1/matches/{matchId}/commands", new { commands });
    foreach (var result in reply.Results)
    {
        if (!result.Accepted)
        {
            var command = commands[result.Index];
            Console.WriteLine($"  rejected {command.Type}: {result.Error?.Code} - {result.Error?.Message}");
        }
    }
}

// React to what happened since the last tick.
void HandleEvents(GameState state)
{
    foreach (var ev in state.Events)
    {
        if (ev.Kind == "CommandFailed")
        {
            // Accepted earlier, but it couldn't be carried out when the time came.
            Console.WriteLine($"tick {ev.Tick}: command failed: {ev.Message}");
            if (ev.EntityId == barracksBuilderId)
                barracksOrderedAt = null;  // our builder gave up: try again
        }
        else if (ev.Kind is "BuildingStarted" or "BuildingCompleted" or "UnitLost")
        {
            Console.WriteLine($"tick {ev.Tick}: {ev.Message}");
        }
    }
}


// --------------------------------------------------------------------------- the loop

async Task Play(string matchId)
{
    var state = await WaitForStart(matchId);
    // The terrain never changes, so fetch it once. It only exists once the match has started.
    terrain = (await Api<MapInfo>("GET", $"/api/v1/matches/{matchId}/map")).Terrain;
    Console.WriteLine($"Started! We are slot {state.You.Slot} on a {state.MapWidth}x{state.MapHeight} map");

    while (state.Status != "Completed")
    {
        HandleEvents(state);
        var commands = Decide(state);
        if (commands.Count > 0)
            await Send(matchId, commands);

        if (state.Tick % 10 == 0)
        {
            int me = state.You.Slot;
            var mine = state.Units.Where(u => u.Owner == me).ToList();
            int workers = mine.Count(u => u.Type == "Worker");
            int soldiers = mine.Count(u => u.Type == "Soldier");
            Console.WriteLine($"tick {state.Tick,4}: ore {state.You.Resources,5}, " +
                              $"{workers} workers, {soldiers} soldiers");
        }

        // Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        int tick = state.Tick;
        state = await Api<GameState>("GET", $"/api/v1/matches/{matchId}/state?waitForTick={tick + 1}&sinceTick={tick}");
    }

    HandleEvents(state);  // the final tick's events
    Console.WriteLine($"Match over after {state.Outcome?.Ticks} ticks: {state.Outcome?.Reason}");
}


// --------------------------------------------------------------------------- main

const string Usage = """
    usage: dotnet run step3_army.cs -- --name NAME [--server URL] [--vs BOT]
                                       [--tick-ms N] [--max-ticks N] [--seed N]

      --name NAME      your bot's name (3-32 letters, digits, _ or -)
      --server URL     the server (default http://localhost:5080)
      --vs BOT         house bot to play against (default sitter)
      --tick-ms N      milliseconds per tick (default 1000, minimum 100)
      --max-ticks N    end the match after this many ticks (default 1800)
      --seed N         map seed, to replay the same map
    """;

// Read the command line: pairs of "--option value". Only --name is required.
string? botName = null;
string vs = "sitter";
int? tickMsOption = null, maxTicksOption = null, seedOption = null;
for (int i = 0; i < args.Length; i += 2)
{
    string? value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--name" when value is not null: botName = value; break;
        case "--server" when value is not null: server = value.TrimEnd('/'); break;
        case "--vs" when value is not null: vs = value; break;
        case "--tick-ms" when int.TryParse(value, out int n): tickMsOption = n; break;
        case "--max-ticks" when int.TryParse(value, out int n): maxTicksOption = n; break;
        case "--seed" when int.TryParse(value, out int n): seedOption = n; break;
        default:  // --help, or something we don't understand
            Console.WriteLine(Usage);
            return;
    }
}
if (botName is null)
{
    Console.WriteLine(Usage);
    return;
}

apiKey = await GetApiKey(botName);
string matchId = await CreateMatch(vs, tickMsOption, maxTicksOption, seedOption);

// If you press Ctrl+C, don't leave a half-played match running: concede it on the way out.
Console.CancelKeyPress += (_, _) =>
{
    Api<object>("POST", $"/api/v1/matches/{matchId}/surrender").Wait();
    Console.WriteLine("\nSurrendered.");
};
await Play(matchId);


// --------------------------------------------------------------------------- the API's JSON, as C# types
// Each record lists the JSON fields we use (there are more: see the bot guide).
// Enums such as unit types and activities arrive as strings: "Worker", "Idle", ...

// The server answered with an error, e.g. 409 {"code": "MATCH_NOT_STARTED", ...}.
class ApiError(int status, string code, string message) : Exception($"{status} {code}: {message}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

record ErrorBody(string Code, string Message);
record Registration(string PlayerId, string Name, string ApiKey);
record MatchInfo(string MatchId);
record MapInfo(int Width, int Height, List<string> Terrain);

// GET /state: everything you can see this tick.
record GameState(int Tick, string Status, int MapWidth, int MapHeight, Me You, List<Player> Players,
                 List<Unit> Units, List<Building> Buildings, List<Deposit> Resources,
                 List<GameEvent> Events, Outcome? Outcome);
record Me(int Slot, int Resources, int StorageCapacity);
record Player(int Slot, string Name, bool Eliminated, Position StartPosition, Score Score);
record Position(int X, int Y);
record Score(int Destruction, int Economy, int Survival, int Total);
record Unit(int Id, int Owner, string Type, int X, int Y, int Hp, int MaxHp,
            string Activity, int Carrying, int? TargetId);
record Building(int Id, int Owner, string Type, int X, int Y, int Hp, int MaxHp,
                bool Completed, List<QueuedUnit> Production);
record QueuedUnit(string UnitType, int Percent);
record Deposit(int Id, int X, int Y, int Remaining);
record GameEvent(int Tick, string Kind, string Message, int? EntityId, long? CommandId);
record Outcome(string Reason, int Ticks);

// POST /commands: fill in only the fields a command needs, e.g.
// new Command("Gather", UnitIds: [12], TargetId: 3)
record Command(string Type, int[]? UnitIds = null, int? TargetId = null, int? BuildingId = null,
               string? UnitType = null, string? BuildingType = null, int? X = null, int? Y = null);
record CommandReply(int Accepted, int Rejected, List<CommandResult> Results);
record CommandResult(int Index, bool Accepted, long? CommandId, ErrorBody? Error);

// GET /result
record MatchResult(Outcome Outcome, List<PlayerResult> Players);
record PlayerResult(string Name, bool Winner, Score Score, int UnitsProduced, int UnitsLost, int UnitsKilled);
