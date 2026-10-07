using System.Globalization;
using NetRts.Bots;

namespace NetRts.BotRunner;

/// <summary>How the runner gets into a match.</summary>
public enum MatchMode
{
    /// <summary>Create a match against a house bot (starts immediately).</summary>
    Versus,

    /// <summary>Create a waiting match and print its id for others to join.</summary>
    Create,

    /// <summary>Join an existing waiting match.</summary>
    Join,
}

/// <summary>Command-line options, parsed by hand (no external dependencies).</summary>
public sealed class RunnerOptions
{
    public const string Usage = """
        netrts-bot - play a NetRts match with one of the built-in strategies

        Usage:
          netrts-bot --server http://localhost:5080 --name mybot [--key <apiKey>] --strategy balanced
                     (--vs <houseBot> | --create [--players 2] | --join <matchId>)
                     [--tick-ms 1000] [--max-ticks 1800] [--seed N]

        Options:
          --server <url>       API base URL (default: $NETRTS_SERVER or http://localhost:5080)
          --name <name>        Bot name to register (3-32 chars: letters, digits, '_' or '-')
          --key <apiKey>       Existing API key (default: $NETRTS_API_KEY). If omitted, --name is registered.
          --strategy <name>    Strategy to play with: {0} (default: balanced)
          --vs <houseBot>      Create a match against a house bot; it starts right away
          --create             Create a waiting match and print its id for others to --join
          --players <n>        Seats in a --create match, 2-4 (default: 2)
          --join <matchId>     Join an existing waiting match
          --tick-ms <ms>       Tick interval for matches you create
          --max-ticks <n>      Tick limit for matches you create
          --seed <n>           Map seed for matches you create
          -h, --help           Show this help

        Examples:
          netrts-bot --name mybot --strategy rusher --vs sitter --tick-ms 200
          netrts-bot --key $KEY --create --players 2          (prints a match id)
          netrts-bot --name friend --strategy economist --join <matchId>
        """;

    public Uri Server { get; private set; } = new("http://localhost:5080/");
    public string? Name { get; private set; }
    public string? ApiKey { get; private set; }
    public string Strategy { get; private set; } = "balanced";
    public MatchMode Mode { get; private set; }
    public string? VersusBot { get; private set; }
    public Guid JoinMatchId { get; private set; }
    public int Players { get; private set; } = 2;
    public int? TickMs { get; private set; }
    public int? MaxTicks { get; private set; }
    public int? Seed { get; private set; }
    public bool ShowHelp { get; private set; }

    public static string HelpText => string.Format(CultureInfo.InvariantCulture, Usage, string.Join(", ", HouseBots.All.Select(b => b.Name)));

    /// <summary>Parses arguments; throws <see cref="ArgumentException"/> with a user-facing message on bad input.</summary>
    public static RunnerOptions Parse(IReadOnlyList<string> args, Func<string, string?> env)
    {
        var o = new RunnerOptions();
        var server = env("NETRTS_SERVER");
        o.ApiKey = NullIfEmpty(env("NETRTS_API_KEY"));
        var modes = new List<MatchMode>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Value()
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{arg} needs a value.");
                }

                return args[++i];
            }

            switch (arg)
            {
                case "-h" or "--help" or "-?" or "/?":
                    o.ShowHelp = true;
                    break;
                case "--server":
                    server = Value();
                    break;
                case "--name":
                    o.Name = Value();
                    break;
                case "--key":
                    o.ApiKey = Value();
                    break;
                case "--strategy":
                    o.Strategy = Value();
                    break;
                case "--vs":
                    o.VersusBot = Value();
                    modes.Add(MatchMode.Versus);
                    break;
                case "--create":
                    modes.Add(MatchMode.Create);
                    break;
                case "--join":
                    var id = Value();
                    o.JoinMatchId = Guid.TryParse(id, out var g) ? g : throw new ArgumentException($"'{id}' is not a valid match id.");
                    modes.Add(MatchMode.Join);
                    break;
                case "--players":
                    o.Players = Int(arg, Value(), 2, 4);
                    break;
                case "--tick-ms":
                    o.TickMs = Int(arg, Value(), 1, int.MaxValue);
                    break;
                case "--max-ticks":
                    o.MaxTicks = Int(arg, Value(), 1, int.MaxValue);
                    break;
                case "--seed":
                    o.Seed = Int(arg, Value(), int.MinValue, int.MaxValue);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (o.ShowHelp)
        {
            return o;
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            if (!server.EndsWith('/'))
            {
                server += "/";
            }

            o.Server = Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? uri
                : throw new ArgumentException($"'{server}' is not a valid http(s) URL.");
        }

        if (o.ApiKey is null && string.IsNullOrWhiteSpace(o.Name))
        {
            throw new ArgumentException("Pass --name to register a new bot, or --key (or NETRTS_API_KEY) to use an existing one.");
        }

        if (!HouseBots.Exists(o.Strategy))
        {
            throw new ArgumentException($"Unknown strategy '{o.Strategy}'. Choose one of: {string.Join(", ", HouseBots.All.Select(b => b.Name))}.");
        }

        if (modes.Count != 1)
        {
            throw new ArgumentException("Choose exactly one of --vs <houseBot>, --create or --join <matchId>.");
        }

        o.Mode = modes[0];
        if (o.Mode == MatchMode.Versus && !HouseBots.Exists(o.VersusBot!))
        {
            throw new ArgumentException($"Unknown house bot '{o.VersusBot}'. Choose one of: {string.Join(", ", HouseBots.All.Select(b => b.Name))}.");
        }

        return o;
    }

    private static int Int(string name, string value, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? n
            : throw new ArgumentException($"{name} expects an integer{(min > int.MinValue ? $" >= {min}" : "")}{(max < int.MaxValue ? $" and <= {max}" : "")}, got '{value}'.");

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
