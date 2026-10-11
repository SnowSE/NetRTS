using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;

namespace NetRts.Server.Matches;

/// <summary>Posts a message to the Discord channel webhook (see <see cref="DiscordOptions.ResultsWebhookUrl"/>).</summary>
public interface IDiscordWebhook
{
    /// <summary>Sends one webhook message; returns how long Discord asked us to wait before retrying, if it rate-limited us.</summary>
    Task<TimeSpan?> SendAsync(JsonObject message, CancellationToken ct);
}

public sealed class DiscordWebhook(HttpClient http, IOptions<DiscordOptions> options) : IDiscordWebhook
{
    public async Task<TimeSpan?> SendAsync(JsonObject message, CancellationToken ct)
    {
        using var content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(options.Value.ResultsWebhookUrl, content, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return TimeSpan.FromSeconds(body.RootElement.TryGetProperty("retry_after", out var s) ? s.GetDouble() : 5);
        }

        response.EnsureSuccessStatusCode();
        return null;
    }
}

/// <summary>
/// Announces finished matches in the coding-competition Discord channel, when a results webhook is
/// configured (the shared server; private servers leave it unset):
/// <list type="bullet">
/// <item>league matches: one message once every heat has finished, with each heat's top players and points;</item>
/// <item>practice between real bots: one short message each;</item>
/// <item>practice against house bots only: an hourly digest, so iterating on a bot doesn't flood the channel;</item>
/// <item>house-bot exhibitions: nothing.</item>
/// </list>
/// Posting happens in the background; a failure is logged and skipped, never holding up a match.
/// </summary>
public sealed class MatchAnnouncer(
    IServiceProvider services,
    IOptions<DiscordOptions> discord,
    IOptions<NetRtsOptions> options,
    TimeProvider time,
    ILogger<MatchAnnouncer> logger) : BackgroundService
{
    /// <summary>Discord allows about 30 webhook messages a minute; stay under it.</summary>
    private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(2.1);

    private readonly Channel<JsonObject> _outbox = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly List<MatchSummaryDto> _digest = [];
    private readonly HashSet<Guid> _announcedLeagues = [];
    private readonly object _gate = new();

    public bool Enabled => !string.IsNullOrWhiteSpace(discord.Value.ResultsWebhookUrl);

    /// <summary>Called once a finished match has been saved.</summary>
    public void MatchRecorded(MatchHost host)
    {
        if (!Enabled || host.ToSummary() is not { Outcome: not null } summary)
        {
            return;
        }

        if (host.ParentMatchId is { } parent)
        {
            AnnounceLeagueIfDone(parent);
        }
        else if (summary.Players.Count(p => !p.IsHouseBot) >= 2)
        {
            Post(PracticeMessage(summary));
        }
        else if (summary.Players.Any(p => !p.IsHouseBot))
        {
            lock (_gate)
            {
                _digest.Add(summary);
            }
        }
    }

    /// <summary>Posts the digest of practice games against house bots, if there were any.</summary>
    public void FlushDigest()
    {
        List<MatchSummaryDto> games;
        lock (_gate)
        {
            games = [.. _digest];
            _digest.Clear();
        }

        if (games.Count > 0)
        {
            Post(DigestMessage(games));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var digestEvery = TimeSpan.FromMinutes(Math.Max(1, discord.Value.ResultsDigestMinutes));
        _ = DigestLoopAsync(digestEvery, stoppingToken);
        var webhook = services.GetRequiredService<IDiscordWebhook>();
        await foreach (var message in _outbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (await webhook.SendAsync(message, stoppingToken) is { } retryAfter)
                {
                    await Task.Delay(retryAfter, time, stoppingToken);
                    await webhook.SendAsync(message, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not post a match result to Discord");
            }

            await Task.Delay(MinimumGap, time, stoppingToken);
        }
    }

    private async Task DigestLoopAsync(TimeSpan every, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(every, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                FlushDigest();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Post(JsonObject embed)
    {
        var message = new JsonObject
        {
            ["embeds"] = new JsonArray(embed),
            // Names are shown as text; never ping anyone.
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
        };
        _outbox.Writer.TryWrite(message);
    }

    private void AnnounceLeagueIfDone(Guid parentId)
    {
        var leagues = services.GetRequiredService<LeagueMatchManager>();
        if (leagues.Find(parentId) is not { } league)
        {
            return;
        }

        List<MatchHost> heats;
        lock (league.Gate)
        {
            heats = [.. league.Heats];
        }

        lock (_gate)
        {
            if (heats.Count == 0 || !heats.All(h => h.Recorded) || !_announcedLeagues.Add(parentId))
            {
                return;
            }
        }

        Post(LeagueMessage(league, heats.Select(LeagueMatchManager.StandingsOf).ToList()));
    }

    // ------------------------------------------------------------------ messages

    private JsonObject PracticeMessage(MatchSummaryDto m)
    {
        var winner = m.Players.FirstOrDefault(p => p.PlayerId == m.Outcome!.WinnerId);
        var title = winner is null
            ? $"Draw: {string.Join(" vs ", m.Players.Select(p => p.Name))}"
            : m.Players.Count == 2
                ? $"{winner.Name} beat {m.Players.First(p => p.PlayerId != winner.PlayerId).Name}"
                : $"{winner.Name} won a {m.Players.Count}-player match";
        return Embed(Truncate(title, 250), Link($"#/match/{m.MatchId}"),
            $"{HowItEnded(m.Outcome!)}\n{string.Join("\n", m.Players.Select(p => (p.PlayerId == winner?.PlayerId ? "🏆 " : "• ") + Who(p.Name, p.IsHouseBot, p.Verified, p.OwnerName)))}",
            m.Category, "Practice");
    }

    private JsonObject DigestMessage(List<MatchSummaryDto> games)
    {
        var lines = games.Take(10).Select(g =>
        {
            var bot = g.Players.First(p => !p.IsHouseBot);
            var won = g.Outcome!.WinnerId == bot.PlayerId;
            var houses = string.Join(", ", g.Players.Where(p => p.IsHouseBot).Select(p => HouseName(p.Name)));
            var verb = g.Outcome.WinnerId is null ? "drew with" : won ? "beat" : "lost to";
            return $"• {Who(bot.Name, false, bot.Verified, bot.OwnerName)} {verb} {houses} · [watch]({Link($"#/match/{g.MatchId}")})";
        }).ToList();
        if (games.Count > 10)
        {
            lines.Add($"…and {games.Count - 10} more");
        }

        var wins = games.Count(g => g.Players.Any(p => !p.IsHouseBot && p.PlayerId == g.Outcome!.WinnerId));
        return Embed($"Practice vs house bots: {games.Count} game{(games.Count == 1 ? "" : "s")}, {wins} won by players",
            Link("#/"), Truncate(string.Join("\n", lines), 4000), MatchCategory.Practice, null);
    }

    private JsonObject LeagueMessage(LeagueMatch league, List<HeatStandingsDto> heats)
    {
        var embed = Embed($"{league.Category}: {league.Name} is over",
            Link($"#/match/{league.Id}"),
            $"{heats.Sum(h => h.Players.Count)} bots in {heats.Count} heat{(heats.Count == 1 ? "" : "s")}. Points go to each heat's top four.",
            league.Category, null);
        var fields = new JsonArray();
        foreach (var heat in heats.Take(25))
        {
            var rows = heat.Players.Take(4).Select(p =>
                $"{p.Place}. {Who(p.Name, false, p.Verified, p.OwnerName)} · {p.Score.ToString("N0", CultureInfo.InvariantCulture)}" +
                (p.Points > 0 ? $" · **+{p.Points.ToString("N0", CultureInfo.InvariantCulture)} pts**" : "")).ToList();
            if (heat.Players.Count > 4)
            {
                rows.Add($"…and {heat.Players.Count - 4} more");
            }

            fields.Add(new JsonObject
            {
                ["name"] = Truncate(heat.Name, 250),
                ["value"] = Truncate(string.Join("\n", rows) + $"\n[details]({Link($"#/match/{heat.MatchId}")})", 1024),
                ["inline"] = false,
            });
        }

        embed["fields"] = fields;
        return embed;
    }

    private static JsonObject Embed(string title, string? url, string description, MatchCategory category, string? footer)
    {
        var embed = new JsonObject
        {
            ["title"] = title,
            ["description"] = description,
            ["color"] = category switch
            {
                MatchCategory.Season => 0x1A73C9,
                MatchCategory.Championship => 0xE69F00,
                MatchCategory.Preseason => 0x0F6B4F,
                _ => 0x5865F2,
            },
        };
        if (url is not null)
        {
            embed["url"] = url;
        }

        if (footer is not null)
        {
            embed["footer"] = new JsonObject { ["text"] = footer };
        }

        return embed;
    }

    /// <summary>A link into the website, or null when the public address isn't configured.</summary>
    private string? Link(string fragment) =>
        string.IsNullOrWhiteSpace(options.Value.PublicUrl) ? null : options.Value.PublicUrl.TrimEnd('/') + "/" + fragment;

    private static string HowItEnded(MatchOutcomeDto o) => o.Reason switch
    {
        MatchEndReason.Elimination => $"By elimination after {o.Ticks:N0} ticks",
        MatchEndReason.Surrender => $"By surrender after {o.Ticks:N0} ticks",
        _ => o.WinnerId is null ? $"Draw at the time limit ({o.Ticks:N0} ticks)" : $"On score at the time limit ({o.Ticks:N0} ticks)",
    };

    private static string Who(string name, bool houseBot, bool verified, string? owner) =>
        houseBot ? HouseName(name)
        : $"**{Escape(name)}**" + (verified ? " ✅" : "") + (verified && owner is not null ? $" ({Escape(owner)})" : "");

    /// <summary>"house-rusher 2" → "rusher 2 (house bot)".</summary>
    private static string HouseName(string name) => $"{Escape(name.StartsWith("house-", StringComparison.Ordinal) ? name[6..] : name)} (house bot)";

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '[' or ']' or '(' or ')' or '#' or '@')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
