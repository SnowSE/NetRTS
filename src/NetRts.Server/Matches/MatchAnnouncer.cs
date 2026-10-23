using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;

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
/// <item>league matches: one message once every heat of a round has finished, with each heat's top players and points, and
/// one when the match is closed, with each bot's total and the ladder;</item>
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
    private readonly Channel<JsonObject> _outbox = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly List<MatchSummaryDto> _digest = [];
    private readonly HashSet<(Guid League, int Round)> _announcedRounds = [];
    private const string SerumMilestone = "serum";

    private readonly object _gate = new();
    private readonly SemaphoreSlim _milestoneGate = new(1, 1);

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
            AnnounceRoundIfDone(parent, host.Id);
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

            await Task.Delay(TimeSpan.FromMilliseconds(discord.Value.MessageGapMs), time, stoppingToken);
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

    /// <summary>
    /// The first person to take the serum unlocks super soldiers for everyone to hear about; the next
    /// three are mentioned too. Each Discord account counts once.
    /// </summary>
    public async Task SerumTakenAsync(Seat seat, CancellationToken ct)
    {
        if (!Enabled || !seat.Verified || seat.OwnerAccountId is not { } accountId)
        {
            return;
        }

        await _milestoneGate.WaitAsync(ct);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            if (await db.AccountMilestones.AnyAsync(m => m.AccountId == accountId && m.Kind == SerumMilestone, ct))
            {
                return;
            }

            var before = await db.AccountMilestones.CountAsync(m => m.Kind == SerumMilestone, ct);
            if (before >= 4)
            {
                return;
            }

            db.AccountMilestones.Add(new AccountMilestoneRecord { AccountId = accountId, Kind = SerumMilestone, ReachedAt = time.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(ct);
            var who = $"**{Escape(seat.OwnerName ?? seat.Name)}**";
            Send(new JsonObject { ["content"] = before == 0 ? $"{who} unlocked super soldiers!" : $"{who} has super soldiers now too!" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record a milestone");
        }
        finally
        {
            _milestoneGate.Release();
        }
    }

    /// <summary>
    /// A league match is closed and its points are on the ladder: each bot's total for the match (its
    /// heats less its lowest) and the ladder's top as it now stands. <paramref name="name"/> is null for
    /// a match nobody closed, finalized after a restart.
    /// </summary>
    public void LeagueMatchFinal(string? name, Guid? leagueId, MatchCategory category,
        IReadOnlyList<LeagueMatchTotal> totals, IReadOnlyList<LeaderboardEntryDto> ladder)
    {
        if (!Enabled)
        {
            return;
        }

        var rows = totals.Take(15).Select((t, i) =>
        {
            var heats = t.HeatPoints.Count < 2 ? "" : $" ({string.Join(" + ", t.HeatPoints.Select(p => p.ToString("N0", CultureInfo.InvariantCulture)))}, lowest dropped)";
            return $"{i + 1}. {Who(t.Name, false, t.Verified, t.OwnerName)} · **{t.Points.ToString("N0", CultureInfo.InvariantCulture)} pts**{heats}";
        }).ToList();
        if (totals.Count > 15)
        {
            rows.Add($"…and {totals.Count - 15} more");
        }

        var embed = Embed(name is null ? $"{category}: a match is final" : $"{category}: {name} is final",
            leagueId is { } id ? Link($"#/match/{id}") : Link("#/leaderboard"),
            Truncate(string.Join("\n", rows), 4000), category, null);
        embed["fields"] = new JsonArray(new JsonObject
        {
            ["name"] = $"{category} ladder",
            ["value"] = Truncate(string.Join("\n", ladder.Select(e =>
                $"{e.Rank}. {Who(e.Name, e.IsHouseBot, e.Verified, e.OwnerName)} · {e.Points.ToString("N0", CultureInfo.InvariantCulture)} pts · skill {e.Skill.ToString("N0", CultureInfo.InvariantCulture)}")), 1024),
            ["inline"] = false,
        });
        Post(embed);
    }

    /// <summary>An admin has created a league match: say what it's called and where to join it.</summary>
    public void LeagueMatchOpened(LeagueMatch league)
    {
        if (!Enabled)
        {
            return;
        }

        var name = Escape(league.Name);
        Post(Embed($"{league.Category} match open: {league.Name}", Link($"#/match/{league.Id}"),
            $"Verified bots can join now. Point yours at **{name}**:\n" +
            $"• the bot runner: `--join {league.Name}`\n" +
            $"• the API: `GET /api/v1/matches/named/{league.Name}`, then `POST /api/v1/matches/{{id}}/join`\n" +
            "One bot per person. The admins split everyone into heats and start them together.",
            league.Category, null));
    }

    /// <summary>The day's new achievements, bot by bot (one message; nothing if there were none).</summary>
    public void AchievementDigest(DateOnly day, IReadOnlyList<(string Bot, bool Verified, string? Owner, IReadOnlyList<string> Achievements)> bots)
    {
        if (!Enabled || bots.Count == 0)
        {
            return;
        }

        var total = bots.Sum(b => b.Achievements.Count);
        var lines = bots.Select(b => $"• {Who(b.Bot, false, b.Verified, b.Owner)}: {string.Join(", ", b.Achievements.Select(Escape))}").ToList();
        var shown = new List<string>();
        foreach (var line in lines)
        {
            if (string.Join("\n", shown.Append(line)).Length > 3900)
            {
                shown.Add($"…and {lines.Count - shown.Count} more bots");
                break;
            }

            shown.Add(line);
        }

        var title = $"Achievements unlocked {day.ToString("dddd, MMMM d", CultureInfo.InvariantCulture)}: {total} by {bots.Count} bot{(bots.Count == 1 ? "" : "s")}";
        Post(Embed(title, Link("#/achievements"), string.Join("\n", shown), MatchCategory.Practice, null));
    }

    /// <summary>A bot is the first ever to earn some achievements (one message for all of them).</summary>
    public void FirstToEarn(string botName, bool verified, string? ownerName, IReadOnlyList<(string Name, string Description)> achievements)
    {
        if (Enabled && achievements.Count > 0)
        {
            var list = string.Join("\n", achievements.Select(a => $"🏅 **{Escape(a.Name)}**: {Escape(a.Description)}"));
            Send(new JsonObject { ["content"] = Truncate($"{Who(botName, false, verified, ownerName)} is the first ever to earn\n{list}", 2000) });
        }
    }

    /// <summary>A new recap episode is out: its title and summary, linking to it on the site.</summary>
    public void RecapReady(RecapEpisodeRecord episode)
    {
        if (!Enabled)
        {
            return;
        }

        var day = DateOnly.FromDateTime(episode.Date).ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        var embed = Embed(Truncate($"📺 {episode.Title}", 250), Link($"#/recaps/{episode.Id}"),
            Truncate($"Last night's recap is up: the highlights from {day}.\n{Escape(episode.Summary)}", 4000), MatchCategory.Practice, null);
        if (!string.IsNullOrEmpty(episode.Thumbnail) && Link($"recaps/{episode.Id}/thumbnail") is { } poster)
        {
            embed["image"] = new JsonObject { ["url"] = poster };
        }

        Post(embed);
    }

    private void Post(JsonObject embed) => Send(new JsonObject { ["embeds"] = new JsonArray(embed) });

    private void Send(JsonObject message)
    {
        // Names are shown as text; never ping anyone.
        message["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() };
        _outbox.Writer.TryWrite(message);
    }

    private void AnnounceRoundIfDone(Guid parentId, Guid heatId)
    {
        var leagues = services.GetRequiredService<LeagueMatchManager>();
        if (leagues.Find(parentId) is not { } league)
        {
            return;
        }

        List<MatchHost> heats;
        int round;
        bool onlyRound;
        lock (league.Gate)
        {
            if (league.RoundOf(heatId) is not { } r)
            {
                return;
            }

            (heats, round, onlyRound) = ([.. r.Heats], r.Number, league.Rounds.Count == 1);
        }

        lock (_gate)
        {
            if (heats.Count == 0 || !heats.All(h => h.Recorded) || !_announcedRounds.Add((parentId, round)))
            {
                return;
            }
        }

        Post(LeagueMessage(league, round == 1 && onlyRound ? null : round, heats.Select(h => LeagueMatchManager.StandingsOf(h, round)).ToList()));
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

    private JsonObject LeagueMessage(LeagueMatch league, int? round, List<HeatStandingsDto> heats)
    {
        var embed = Embed(round is null ? $"{league.Category}: {league.Name} is over" : $"{league.Category}: {league.Name}, round {round} is over",
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
            ["color"] = CategoryColour(category),
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

    /// <summary>The embed's side stripe: blue for season, gold for championship, green for preseason, Discord blurple for practice.</summary>
    private static int CategoryColour(MatchCategory category) => category switch
    {
        MatchCategory.Season => 0x1A73C9,
        MatchCategory.Championship => 0xE69F00,
        MatchCategory.Preseason => 0x0F6B4F,
        _ => 0x5865F2,
    };

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
