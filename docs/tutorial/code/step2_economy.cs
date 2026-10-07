// NetRts tutorial, step 2: an economy.
//
// Step 1's loop, plus a Decide() function that sends orders every tick:
// idle workers mine ore (spread over the deposits) and the Command Center
// keeps training workers up to TargetWorkers.
//
//     dotnet run step2_economy.cs -- --name my-bot --max-ticks 300
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
const int WorkerCost = 50;

// Tiles between two points, counting diagonal steps as 1 (the game's own measure).
static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

// Look at this tick's state and return a list of commands to send.
List<Command> Decide(GameState state)
{
    int me = state.You.Slot;
    int ore = state.You.Resources;
    var workers = state.Units.Where(u => u.Owner == me && u.Type == "Worker").ToList();
    var commandCenter = state.Buildings.FirstOrDefault(b => b.Owner == me && b.Type == "CommandCenter");
    var commands = new List<Command>();

    // 1. Idle workers go mining. Count how many workers already mine each deposit and
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

    // 2. Train more workers, one or two at a time, until we have TargetWorkers.
    if (commandCenter is { Completed: true })
    {
        int queued = commandCenter.Production.Count;
        if (workers.Count + queued < TargetWorkers && queued < 2 && ore >= WorkerCost)
            commands.Add(new Command("Produce", BuildingId: commandCenter.Id, UnitType: "Worker"));
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


// --------------------------------------------------------------------------- the loop

async Task Play(string matchId)
{
    var state = await WaitForStart(matchId);
    Console.WriteLine($"Started! We are slot {state.You.Slot} on a {state.MapWidth}x{state.MapHeight} map");

    while (state.Status != "Completed")
    {
        var commands = Decide(state);
        if (commands.Count > 0)
            await Send(matchId, commands);

        if (state.Tick % 10 == 0)
        {
            int me = state.You.Slot;
            var workers = state.Units.Where(u => u.Owner == me && u.Type == "Worker").ToList();
            int idle = workers.Count(w => w.Activity == "Idle");
            Console.WriteLine($"tick {state.Tick,4}: ore {state.You.Resources,5}, " +
                              $"{workers.Count} workers ({idle} idle)");
        }

        // Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        int tick = state.Tick;
        state = await Api<GameState>("GET", $"/api/v1/matches/{matchId}/state?waitForTick={tick + 1}&sinceTick={tick}");
    }

    Console.WriteLine($"Match over after {state.Outcome?.Ticks} ticks: {state.Outcome?.Reason}");
}


// --------------------------------------------------------------------------- main

const string Usage = """
    usage: dotnet run step2_economy.cs -- --name NAME [--server URL] [--vs BOT]
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
