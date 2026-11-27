using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Sim;

// netrts-sim [--variants classic,spoils50] [--bots rusher,economist,...] [--seeds 3] [--players 2] [--mirror] [--ticks 1800] [--out games.jsonl]
//
// Plays every pairing of the house bots (both seat orders; with --players 4, every group of four) on
// each seed, under each variant, and prints how the games went.
var options = Options.Parse(args);
var unknown = options.Variants.Where(v => !Variants.All.ContainsKey(v)).ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine($"Unknown variant(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", Variants.All.Keys)}");
    return 1;
}

var lineups = Lineups(options.Bots, options.Players, options.Mirror).ToList();
var jobs = (from variant in options.Variants
            from lineup in lineups
            from seed in Enumerable.Range(1, options.Seeds)
            select (variant, lineup, seed)).ToList();
Console.Error.WriteLine($"{jobs.Count} games: {options.Variants.Count} variants x {lineups.Count} lineups x {options.Seeds} seeds");

var clock = Stopwatch.StartNew();
var games = new ConcurrentBag<Game>();
var done = 0;
Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
{
    games.Add(Play(job.variant, Variants.All[job.variant], job.lineup, job.seed, options.Ticks));
    var n = Interlocked.Increment(ref done);
    if (n % 20 == 0 || n == jobs.Count)
    {
        Console.Error.WriteLine($"  {n}/{jobs.Count} ({clock.Elapsed:mm\\:ss})");
    }
});

if (options.Out is { } path)
{
    File.WriteAllLines(path, games.OrderBy(g => g.Variant).ThenBy(g => string.Join(",", g.Bots)).ThenBy(g => g.Seed)
        .Select(g => JsonSerializer.Serialize(g, NetRtsClient.JsonOptions)));
}

Console.WriteLine(Report.Render(options, games.ToList()));
return 0;

// 2 players: every ordered pair (with --mirror, each bot against itself too). More: every group of that
// size, in each rotation so every bot starts from every corner.
static IEnumerable<string[]> Lineups(IReadOnlyList<string> bots, int players, bool mirror) => players == 2
    ? from a in bots from b in bots where a != b || mirror select new[] { a, b }
    : from lineup in Combinations(bots, players).Select(c => c.ToArray())
      from r in Enumerable.Range(0, players)
      select lineup.Skip(r).Concat(lineup.Take(r)).ToArray();

static IEnumerable<IEnumerable<string>> Combinations(IReadOnlyList<string> items, int k) => k == 0
    ? [[]]
    : items.SelectMany((item, i) => Combinations(items.Skip(i + 1).ToList(), k - 1).Select(rest => rest.Prepend(item)));

static Game Play(string variant, RuleSet rules, string[] bots, int seed, int ticks)
{
    var seats = bots.Select((b, i) => new PlayerSeat(new Guid(seed, (short)i, 1, new byte[8]), b)).ToList();
    // Like the server: 64x64 up to four players, more room (the minimum plus some, to a multiple of 8) beyond.
    var size = bots.Length <= 4 ? 64 : Math.Max(64, (MapGenerator.MinSizeFor(bots.Length) + 16 + 7) / 8 * 8);
    var sim = new GameSimulation(new Guid(seed, 0, 0, new byte[8]),
        new GameConfig { Seed = seed, MaxTicks = ticks, Rules = rules, MapWidth = size, MapHeight = size }, seats);
    var strategies = bots.Select(HouseBots.Create).ToArray();
    var map = sim.Map.ToDto();
    var rulesDto = rules.ToDto();
    int? leaderAt300 = null;

    while (sim.Status == MatchStatus.Active)
    {
        for (var slot = 0; slot < strategies.Length; slot++)
        {
            var view = sim.GetPlayerView(slot);
            if (view.Players[slot].Eliminated)
            {
                continue;
            }

            var commands = strategies[slot].Decide(new BotContext(view, map, rulesDto));
            if (commands.Count > 0)
            {
                sim.Submit(slot, commands.Take(sim.Config.MaxCommandsPerSubmit).ToList());
            }
        }

        sim.Step();
        if (sim.Tick == 300)
        {
            leaderAt300 = sim.GetPlayerView(0).Players.MaxBy(p => p.Score.Total)!.Slot;
        }
    }

    var result = sim.GetResult()!;
    return new Game(variant, seed, bots, result.Outcome.Reason, result.Outcome.Ticks,
        result.Players.FirstOrDefault(p => p.Winner)?.Slot, leaderAt300,
        result.Players.Select(p => new Seat(p.Slot, p.Score, p.UnitsKilled, p.BuildingsDestroyed, p.Stats!)).ToList());
}

internal sealed record Seat(int Slot, ScoreDto Score, int UnitsKilled, int BuildingsDestroyed, PlayerMatchStatsDto Stats);

internal sealed record Game(string Variant, int Seed, string[] Bots, MatchEndReason Reason, int Ticks, int? Winner, int? LeaderAt300, IReadOnlyList<Seat> Seats);

internal sealed record Options(IReadOnlyList<string> Variants, IReadOnlyList<string> Bots, int Seeds, int Players, int Ticks, string? Out, bool Mirror)
{
    public static Options Parse(string[] args)
    {
        string? Get(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        IReadOnlyList<string>? List(string name) => Get(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Options(
            List("--variants") ?? ["classic"],
            List("--bots") ?? ["rusher", "economist", "balanced", "poky", "formics"],
            int.Parse(Get("--seeds") ?? "3", System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--players") ?? "2", System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--ticks") ?? "1800", System.Globalization.CultureInfo.InvariantCulture),
            Get("--out"),
            args.Contains("--mirror"));
    }
}
