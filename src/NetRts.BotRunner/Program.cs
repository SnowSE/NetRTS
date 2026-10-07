using System.Globalization;
using System.Net;
using NetRts.BotRunner;
using NetRts.Bots;
using NetRts.Protocol;

RunnerOptions options;
try
{
    options = RunnerOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine("Run with --help for usage.");
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(RunnerOptions.HelpText);
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C: stop cleanly. A second one falls through and kills the process.
    if (!cts.IsCancellationRequested)
    {
        e.Cancel = true;
        Runner.Log("Ctrl+C: stopping...");
        cts.Cancel();
    }
};

try
{
    return await Runner.RunAsync(options, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Runner.Log("Cancelled.");
    return 130;
}
catch (NetRtsApiException ex)
{
    Console.Error.WriteLine($"API error {(int)ex.Status} {ex.Code}: {ex.Message.Split(": ", 2).Last()}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"error: could not reach {options.Server}: {ex.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine($"error: request to {options.Server} timed out.");
    return 1;
}

namespace NetRts.BotRunner
{
    internal static class Runner
    {
        public static void Log(string message) =>
            Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}");

        public static async Task<int> RunAsync(RunnerOptions o, CancellationToken ct)
        {
            using var client = new NetRtsClient(o.Server, o.ApiKey);

            if (o.ApiKey is null)
            {
                try
                {
                    var registered = await client.RegisterAsync(o.Name!, ct);
                    Log($"Registered '{registered.Name}' (player {registered.PlayerId}).");
                    Console.WriteLine();
                    Console.WriteLine($"  API key: {registered.ApiKey}");
                    Console.WriteLine("  Save it! It is shown only once. Next time pass --key <apiKey> or set NETRTS_API_KEY.");
                    Console.WriteLine();
                }
                catch (NetRtsApiException ex) when (ex.Code == "NAME_TAKEN")
                {
                    Console.Error.WriteLine($"error: the name '{o.Name}' is already registered. If it's yours, pass --key <apiKey> (or set NETRTS_API_KEY); otherwise pick another --name.");
                    return 1;
                }
            }
            else
            {
                try
                {
                    var me = await client.GetMeAsync(ct);
                    Log($"Authenticated as '{me.Name}' (rating {me.Rating}, {me.Wins}W/{me.Losses}L/{me.Draws}D).");
                }
                catch (NetRtsApiException ex) when (ex.Status == HttpStatusCode.Unauthorized)
                {
                    Console.Error.WriteLine("error: the API key was rejected. Check --key / NETRTS_API_KEY.");
                    return 1;
                }
            }

            var match = await EnterMatchAsync(client, o, ct);
            var matchId = match.MatchId;

            await WaitForStartAsync(client, matchId, ct);

            var strategy = new ProgressLogger(HouseBots.Create(o.Strategy), Log);
            Log($"Match started. Playing with strategy '{o.Strategy}'.");
            var result = await BotLoop.RunAsync(client, matchId, strategy, Log, ct);
            if (result is null)
            {
                ct.ThrowIfCancellationRequested();
                Console.Error.WriteLine("error: the bot loop ended before the match completed.");
                return 1;
            }

            PrintResult(result, strategy.Slot);
            return 0;
        }

        private static async Task<MatchSummaryDto> EnterMatchAsync(NetRtsClient client, RunnerOptions o, CancellationToken ct)
        {
            var settings = o.TickMs is null && o.MaxTicks is null && o.Seed is null
                ? null
                : new MatchSettingsDto { TickIntervalMs = o.TickMs, MaxTicks = o.MaxTicks, Seed = o.Seed };

            switch (o.Mode)
            {
                case MatchMode.Versus:
                {
                    var match = await client.CreateMatchAsync(new CreateMatchRequest { MaxPlayers = 2, HouseBots = [o.VersusBot!], Settings = settings }, ct);
                    Log($"Created match {match.MatchId} vs house bot '{o.VersusBot}' ({Describe(match)}).");
                    return match;
                }

                case MatchMode.Create:
                {
                    var match = await client.CreateMatchAsync(new CreateMatchRequest { MaxPlayers = o.Players, Settings = settings }, ct);
                    Log($"Created match {match.MatchId} with {match.MaxPlayers} seats ({Describe(match)}).");
                    Console.WriteLine();
                    Console.WriteLine($"  Match id: {match.MatchId}");
                    Console.WriteLine($"  Others join with: netrts-bot --server {o.Server} --name <theirbot> --strategy <strategy> --join {match.MatchId}");
                    Console.WriteLine();
                    return match;
                }

                default:
                {
                    var match = await client.JoinMatchAsync(o.JoinMatchId, ct);
                    Log($"Joined match {match.MatchId} ({match.Players.Count}/{match.MaxPlayers} seats taken, {Describe(match)}).");
                    return match;
                }
            }
        }

        private static string Describe(MatchSummaryDto m) =>
            $"{m.MapWidth}x{m.MapHeight} map, seed {m.Seed}, {m.TickIntervalMs} ms/tick, max {m.MaxTicks} ticks";

        /// <summary>Long-polls until the match has been created (it starts once every seat is filled).</summary>
        private static async Task WaitForStartAsync(NetRtsClient client, Guid matchId, CancellationToken ct)
        {
            var announced = false;
            while (true)
            {
                try
                {
                    await client.GetStateAsync(matchId, waitForTick: 0, ct: ct);
                    return;
                }
                catch (NetRtsApiException ex) when (ex.Code == "MATCH_NOT_STARTED")
                {
                    if (!announced)
                    {
                        Log("Waiting for the match to fill up... (Ctrl+C to quit)");
                        announced = true;
                    }
                }
            }
        }

        private static void PrintResult(MatchResultDto result, int? mySlot)
        {
            var winner = result.Players.FirstOrDefault(p => p.Winner);
            Console.WriteLine();
            Console.WriteLine($"=== Match {result.MatchId} finished after {result.Outcome.Ticks} ticks ({result.Outcome.Reason}) ===");
            Console.WriteLine(winner is null
                ? "Result: draw"
                : $"Winner: {winner.Name} (slot {winner.Slot}){(winner.Slot == mySlot ? "  <-- you" : "")}");
            Console.WriteLine();
            Console.WriteLine($"  {"slot",-4} {"player",-24} {"total",7} {"destr",7} {"econ",7} {"surv",7} {"made",5} {"lost",5} {"kills",5} {"bldLost",7} {"bldKill",7}");
            foreach (var p in result.Players.OrderBy(p => p.Slot))
            {
                var name = p.Name + (p.Slot == mySlot ? " (you)" : "") + (p.Winner ? " *" : "");
                Console.WriteLine(
                    $"  {p.Slot,-4} {name,-24} {p.Score.Total,7} {p.Score.Destruction,7} {p.Score.Economy,7} {p.Score.Survival,7} " +
                    $"{p.UnitsProduced,5} {p.UnitsLost,5} {p.UnitsKilled,5} {p.BuildingsLost,7} {p.BuildingsDestroyed,7}");
            }

            Console.WriteLine();
        }
    }

    /// <summary>Wraps a strategy to print a short status line every so often (BotLoop only logs failures).</summary>
    internal sealed class ProgressLogger(IBotStrategy inner, Action<string> log) : IBotStrategy
    {
        private DateTime _nextReport = DateTime.MinValue;

        public string Name => inner.Name;

        public int? Slot { get; private set; }

        public IReadOnlyList<CommandRequest> Decide(BotContext context)
        {
            var s = context.State;
            Slot = s.You.Slot;
            if (DateTime.UtcNow >= _nextReport)
            {
                _nextReport = DateTime.UtcNow.AddSeconds(5);
                var mine = s.Units.Where(u => u.Owner == s.You.Slot).ToList();
                var workers = mine.Count(u => u.Type == UnitType.Worker);
                var buildings = s.Buildings.Count(b => b.Owner == s.You.Slot);
                var scores = string.Join(", ", s.Players.Select(p => $"{p.Name}={p.Score.Total}{(p.Eliminated ? " (out)" : "")}"));
                log($"tick {s.Tick}/{s.MaxTicks}: ore {s.You.Resources}/{s.You.StorageCapacity}, " +
                    $"{workers} workers, {mine.Count - workers} army, {buildings} buildings | score {scores}");
            }

            return inner.Decide(context);
        }
    }
}
