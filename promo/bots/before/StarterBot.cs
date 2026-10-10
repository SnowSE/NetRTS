#:package CommandLineParser@2.9.1
#:property Nullable=disable
#:property PublishAot=false

using System.Net.Http.Json;
using CommandLine;

// A starter bot for Badger Brawl (NetRts).
//
// Every tick, the server sends us everything we can see (the "state"), we decide what to do,
// and we send back a list of commands. Change the Decide method to make your bot smarter!
//
// Run it with:  dotnet run StarterBot.cs --player-name Frank                          (play a house bot)
//          or:  dotnet run StarterBot.cs --player-name Frank --create-match our-game  (start a match for a classmate)
//          or:  dotnet run StarterBot.cs --player-name Ann --join-match our-game      (join it, by name or by match id)
// Add --tick-ms 250 when starting a match to make it run faster (milliseconds per tick; 1000 = one tick a second).
// Every option and its default value is in the Options class at the bottom of this file.

var options = Parser.Default.ParseArguments<Options>(args).Value;
if (options == null)
{
    return;
}

AskForNameIfNeeded(options);

var http = new HttpClient { BaseAddress = new Uri(options.Server), Timeout = TimeSpan.FromMinutes(1) };
if (!await SignUp(options.PlayerName))
{
    return;
}

var match = await GetIntoMatch(options);
if (match == null)
{
    return;
}

var state = await WaitForStart(match);
await Play(match, state);
await PrintResult(match);

// ---------------------------------------------------------------------------------------------
// Step 5: play. Asking for state?waitForTick=N waits until the server has played tick N.
async Task Play(Match match, State state)
{
    var matchUrl = "/api/v1/matches/" + match.MatchId;
    while (state.Status != "Completed")
    {
        var commands = Decide(state);
        if (commands.Count > 0)
        {
            await http.PostAsJsonAsync(matchUrl + "/commands", new { commands });
        }

        Console.WriteLine($"Tick {state.Tick}: {state.You.Resources} ore, {MyUnits(state).Count} units");
        state = await http.GetFromJsonAsync<State>(matchUrl + "/state?waitForTick=" + (state.Tick + 1));
    }
}

// ---------------------------------------------------------------------------------------------
// Your strategy goes here. Look at the state, return a list of commands.
// Every command, unit and building is described in the bot guide:
// https://github.com/SnowSE/NetRTS/blob/main/docs/bot-guide.md
List<object> Decide(State s)
{
    var commands = new List<object>();
    var ore = s.You.Resources;  // what we can still spend this tick
    if (MyBuilding(s, "CommandCenter") == null)
    {
        return commands;  // we lost our headquarters :(
    }

    var gather = GatherOre(s);
    if (gather != null)
    {
        commands.Add(gather);
    }

    var worker = TrainWorker(s, ore);
    if (worker != null)
    {
        commands.Add(worker);
        ore -= 50;
    }

    var barracks = BuildBarracks(s, ore);
    if (barracks != null)
    {
        commands.Add(barracks);
        ore -= 150;
    }

    var soldier = TrainSoldier(s, ore);
    if (soldier != null)
    {
        commands.Add(soldier);
        ore -= 100;
    }

    var attack = Attack(s);
    if (attack != null)
    {
        commands.Add(attack);
    }

    return commands;
}

// 1. Idle workers dig ore at the deposit closest to our headquarters.
object GatherOre(State s)
{
    var hq = MyBuilding(s, "CommandCenter");
    var closest = s.Resources.OrderBy(r => Math.Abs(r.X - hq.X) + Math.Abs(r.Y - hq.Y)).FirstOrDefault();
    if (closest == null)
    {
        return null;
    }

    return new { type = "Gather", units = "idle", unitType = "Worker", targetId = closest.Id };
}

// 2. Train workers until we have 8 (a Worker costs 50 ore).
object TrainWorker(State s, int ore)
{
    var hq = MyBuilding(s, "CommandCenter");
    if (MyUnits(s, "Worker").Count >= 8 || hq.Production.Count > 0 || ore < 50)
    {
        return null;
    }

    return new { type = "Produce", buildingId = hq.Id, unitType = "Worker" };
}

// 3. Then build one Barracks (150 ore), 4 tiles from the headquarters toward the middle of the map.
object BuildBarracks(State s, int ore)
{
    var workers = MyUnits(s, "Worker");
    if (workers.Count < 8 || MyBuilding(s, "Barracks") != null || ore < 150)
    {
        return null;
    }

    var hq = MyBuilding(s, "CommandCenter");
    var x = hq.X < s.MapWidth / 2 ? hq.X + 4 : hq.X - 4;
    var y = hq.Y < s.MapHeight / 2 ? hq.Y + 4 : hq.Y - 4;
    return new { type = "Build", unitIds = new[] { workers[0].Id }, buildingType = "Barracks", x, y };
}

// 4. The Barracks trains soldiers non-stop (a Soldier costs 100 ore).
object TrainSoldier(State s, int ore)
{
    var barracks = MyBuilding(s, "Barracks");
    if (barracks == null || !barracks.Completed || barracks.Production.Count > 0 || ore < 100)
    {
        return null;
    }

    return new { type = "Produce", buildingId = barracks.Id, unitType = "Soldier" };
}

// 5. Once we have 5 soldiers, send every idle soldier to attack the enemy's starting corner.
object Attack(State s)
{
    var enemy = s.Players.FirstOrDefault(p => p.Slot != s.You.Slot && !p.Eliminated);
    if (MyUnits(s, "Soldier").Count < 5 || enemy == null)
    {
        return null;
    }

    return new { type = "Attack", units = "idle", unitType = "Soldier", x = enemy.StartPosition.X, y = enemy.StartPosition.Y };
}

// Helpers: our own units (of one type, or all of them) and our first building of a type.
List<Unit> MyUnits(State s, string type = null) =>
    s.Units.Where(u => u.Owner == s.You.Slot && (type == null || u.Type == type)).ToList();

Building MyBuilding(State s, string type) =>
    s.Buildings.FirstOrDefault(b => b.Owner == s.You.Slot && b.Type == type);

// ---------------------------------------------------------------------------------------------
// Setting up: the name, signing up, getting into a match and seeing who won.

// If nobody picked a name (on the command line or in the Options class), ask for one.
void AskForNameIfNeeded(Options options)
{
    if (options.PlayerName == "change-me")
    {
        Console.WriteLine("Tip: add --player-name YourName (or change the player-name default in StarterBot.cs) and you won't be asked again.");
        Console.Write("What is your bot's name? ");
        options.PlayerName = Console.ReadLine().Trim();
    }
}

// Step 2: sign up once. The server gives us a secret API key, which we save in a file and reuse.
async Task<bool> SignUp(string playerName)
{
    var keyFile = "apikey-" + playerName + ".txt";
    if (!File.Exists(keyFile))
    {
        var response = await http.PostAsJsonAsync("/api/v1/players", new { name = playerName });
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("Could not register: " + await response.Content.ReadAsStringAsync());
            return false;
        }

        var player = await response.Content.ReadFromJsonAsync<Player>();
        File.WriteAllText(keyFile, player.ApiKey);
    }

    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + File.ReadAllText(keyFile).Trim());
    return true;
}

// Step 3: get into a match: create a named one, join one, or play a house bot.
async Task<Match> GetIntoMatch(Options options)
{
    HttpResponseMessage response;
    if (options.CreateMatch != null)
    {
        response = await http.PostAsJsonAsync("/api/v1/matches",
            new { name = options.CreateMatch, settings = new { tickIntervalMs = options.TickMs } });
    }
    else if (options.JoinMatch != null)
    {
        var matchId = await FindMatchId(options.JoinMatch);
        if (matchId == null)
        {
            Console.WriteLine($"No match named '{options.JoinMatch}' is waiting for players.");
            return null;
        }

        response = await http.PostAsync("/api/v1/matches/" + matchId + "/join", null);
    }
    else
    {
        response = await http.PostAsJsonAsync("/api/v1/matches",
            new { houseBots = new[] { options.Opponent }, settings = new { tickIntervalMs = options.TickMs, seed = 2026 } });
    }

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine("Could not get into a match: " + await response.Content.ReadAsStringAsync());
        return null;
    }

    var match = await response.Content.ReadFromJsonAsync<Match>();
    File.WriteAllText("match.txt", match.MatchId);
    Console.WriteLine($"In match {match.Name ?? match.MatchId}. Watch it at {options.Server}/#/match/{match.Name ?? match.MatchId}");
    return match;
}

// Match ids look like 1c042d43-d650-4381-a5b1-c41933e82069. Anything else is a name, so look it up.
async Task<string> FindMatchId(string idOrName)
{
    if (Guid.TryParse(idOrName, out _))
    {
        return idOrName;
    }

    var waiting = await http.GetFromJsonAsync<List<Match>>("/api/v1/matches?status=Waiting");
    var named = waiting.FirstOrDefault(m => string.Equals(m.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    return named?.MatchId;
}

// Step 4: wait for every seat to fill. The server holds each request for up to 30 seconds, then
// answers with an error if the match still hasn't started, so we just ask again.
async Task<State> WaitForStart(Match match)
{
    while (true)
    {
        var reply = await http.GetAsync("/api/v1/matches/" + match.MatchId + "/state?waitForTick=0");
        if (reply.IsSuccessStatusCode)
        {
            return await reply.Content.ReadFromJsonAsync<State>();
        }

        Console.WriteLine("Waiting for the match to start...");
    }
}

// Step 6: who won?
async Task PrintResult(Match match)
{
    var result = await http.GetFromJsonAsync<Result>("/api/v1/matches/" + match.MatchId + "/result");
    foreach (var p in result.Players)
    {
        Console.WriteLine($"{p.Name}: {p.Score.Total} points" + (p.Winner ? "  <-- WINNER" : ""));
    }
}

// ---------------------------------------------------------------------------------------------
// The command-line options. Each [Option] names the switch to type, like --player-name Frank,
// and its Default is what you get when you leave it out. Change a Default to change the setting.
class Options
{
    [Option("player-name", Default = "change-me", HelpText = "Your bot's name: 3-32 letters, digits, - or _. It must not be taken already.")]
    public string PlayerName { get; set; }

    [Option("opponent", Default = "sitter", HelpText = "The house bot to play: sitter (easiest), economist, balanced or rusher (hardest).")]
    public string Opponent { get; set; }

    [Option("tick-ms", Default = 1000, HelpText = "Milliseconds per tick for a match you start (100-10000; 1000 = one tick a second).")]
    public int TickMs { get; set; }

    [Option("server", Default = "https://netrts.snowse.io", HelpText = "The Badger Brawl server to play on.")]
    public string Server { get; set; }

    [Option("create-match", SetName = "create", HelpText = "Start a match with this name for a classmate to join.")]
    public string CreateMatch { get; set; }

    [Option("join-match", SetName = "join", HelpText = "Join a waiting match, by its name or its id.")]
    public string JoinMatch { get; set; }
}

// The shapes of the JSON the server sends us. We only list the fields this bot uses;
// the bot guide lists them all, so add more here when you need them.
record Player(string ApiKey);
record Match(string MatchId, string Name);
record State(int Tick, string Status, int MapWidth, int MapHeight, Me You,
             List<Unit> Units, List<Building> Buildings, List<Ore> Resources, List<PlayerInfo> Players);
record Me(int Slot, int Resources);
record Unit(int Id, int Owner, string Type, int X, int Y, string Activity);
record Building(int Id, int Owner, string Type, int X, int Y, bool Completed, List<object> Production);
record Ore(int Id, int X, int Y);
record PlayerInfo(int Slot, string Name, bool Eliminated, Spot StartPosition);
record Spot(int X, int Y);
record Result(List<ResultPlayer> Players);
record ResultPlayer(string Name, bool Winner, Score Score);
record Score(int Total);
