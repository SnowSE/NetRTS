using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Achievements;

/// <summary>Everything an achievement's check can look at: one bot, one finished match, and the bot's history up to it.</summary>
public sealed class AchievementContext
{
    private readonly ILookup<Guid, MatchPlayerRecord> _opponentsByMatch;
    private readonly TimeZoneInfo _zone;

    /// <param name="row">The bot's row for this match.</param>
    /// <param name="result">The bot's result (its <c>Stats</c> are null for matches recorded before they were kept).</param>
    /// <param name="results">Everyone's results in this match.</param>
    /// <param name="history">The bot's rows, oldest first, up to and including this match.</param>
    /// <param name="opponentsByMatch">The other bots' rows in the bot's matches, by match.</param>
    /// <param name="maxTicks">This match's tick limit.</param>
    /// <param name="houseBotIds">The house bots' player ids.</param>
    /// <param name="verified">Whether a Discord-verified owner has claimed the bot.</param>
    /// <param name="bestRating">The bot's best rating in any category, now.</param>
    /// <param name="ownerBotsWithRealWins">How many of the owner's verified bots have beaten a real bot.</param>
    /// <param name="zone">The server's local time zone.</param>
    public AchievementContext(
        MatchPlayerRecord row,
        PlayerResultDto result,
        IReadOnlyList<PlayerResultDto> results,
        IReadOnlyList<MatchPlayerRecord> history,
        ILookup<Guid, MatchPlayerRecord> opponentsByMatch,
        int maxTicks,
        IReadOnlyCollection<Guid> houseBotIds,
        bool verified,
        int bestRating,
        int ownerBotsWithRealWins,
        TimeZoneInfo zone)
    {
        Row = row;
        Result = result;
        History = history;
        _opponentsByMatch = opponentsByMatch;
        _zone = zone;
        MaxTicks = maxTicks;
        HouseBotIds = houseBotIds;
        Verified = verified;
        BestRating = bestRating;
        OwnerBotsWithRealWins = ownerBotsWithRealWins;
        Others = opponentsByMatch[row.MatchId].ToList();
        RunnerUpScore = results.Where(r => r.Slot != row.Slot).Select(r => (int?)r.Score.Total).Max();
        Real = history.Where(h => h.RealOpponents > 0).ToList();
        CurrentRealOpponents = Others.Where(IsRealOpponent).Select(o => o.PlayerId).ToList();
    }

    public MatchPlayerRecord Row { get; }
    public PlayerResultDto Result { get; }
    public PlayerMatchStatsDto? Stats => Result.Stats;
    public IReadOnlyList<MatchPlayerRecord> History { get; }

    /// <summary>The other registered bots in this match.</summary>
    public IReadOnlyList<MatchPlayerRecord> Others { get; }

    public int MaxTicks { get; }
    public IReadOnlyCollection<Guid> HouseBotIds { get; }
    public bool Verified { get; }
    public int BestRating { get; }
    public int OwnerBotsWithRealWins { get; }
    /// <summary>A win that counts: not one handed over by a surrender before tick 300, which could be arranged.</summary>
    public bool Won => Row.Result == MatchResultKind.Win && (Row.Reason != MatchEndReason.Surrender || Row.Ticks >= 300);

    /// <summary>The best score of anyone else in the match.</summary>
    public int? RunnerUpScore { get; }

    /// <summary>The bot's matches against real bots, oldest first.</summary>
    public IReadOnlyList<MatchPlayerRecord> Real { get; }

    /// <summary>The real bots in this match.</summary>
    public IReadOnlyList<Guid> CurrentRealOpponents { get; }

    public int RealWins => Real.Count(r => r.Result == MatchResultKind.Win);
    public long RealGathered => Real.Sum(r => (long)r.Gathered);
    public long RealKills => Real.Sum(r => (long)r.UnitsKilled);
    public int RealDays => Real.Select(r => LocalTime(r.CompletedAt).Date).Distinct().Count();

    /// <summary>Wins in a row against real bots, ending with this match.</summary>
    public int WinStreak
    {
        get
        {
            var streak = 0;
            for (var i = Real.Count - 1; i >= 0 && Real[i].Result == MatchResultKind.Win; i--)
            {
                streak++;
            }

            return streak;
        }
    }

    /// <summary>Wins in the last 20 real matches minus wins in the first 20; 0 until there are 40.</summary>
    public int GlowUp =>
        Real.Count < 40 ? 0 : Real.TakeLast(20).Count(r => r.Result == MatchResultKind.Win) - Real.Take(20).Count(r => r.Result == MatchResultKind.Win);

    /// <summary>This match is within a day of the bot's first real match after 30 or more days without one.</summary>
    public bool JustCameBack
    {
        get
        {
            for (var i = Real.Count - 1; i > 0; i--)
            {
                if (Real[i].CompletedAt - Real[i - 1].CompletedAt >= TimeSpan.FromDays(30))
                {
                    return Row.CompletedAt - Real[i].CompletedAt <= TimeSpan.FromDays(1);
                }
            }

            return false;
        }
    }

    public int DistinctRealOpponents =>
        Real.SelectMany(r => _opponentsByMatch[r.MatchId]).Where(IsRealOpponent).Select(o => o.PlayerId).Distinct().Count();

    public int TimesPlayed(Guid opponent) => History.Count(h => _opponentsByMatch[h.MatchId].Any(o => o.PlayerId == opponent));

    /// <summary>Earlier matches the opponent won with this bot in them.</summary>
    public int TimesBeatenBy(Guid opponent) =>
        History.Count(h => h.MatchId != Row.MatchId && _opponentsByMatch[h.MatchId].Any(o => o.PlayerId == opponent && o.Result == MatchResultKind.Win));

    /// <summary>The bot has won a match the given bot played in.</summary>
    public bool HasBeaten(Guid opponent) =>
        History.Any(h => h.Result == MatchResultKind.Win && _opponentsByMatch[h.MatchId].Any(o => o.PlayerId == opponent));

    public int Completed(BuildingType type) => Stats?.BuildingsCompleted.GetValueOrDefault(type) ?? 0;

    public bool Researched(UpgradeType upgrade) => Stats?.Upgrades.Contains(upgrade) == true;

    /// <summary>Trained at least one unit of this combat type and no other combat type.</summary>
    public bool OnlyTrained(UnitType type) =>
        Stats is { } s
        && s.UnitsTrained.GetValueOrDefault(type) > 0
        && s.UnitsTrained.Where(kv => kv.Key != UnitType.Worker && kv.Key != type).All(kv => kv.Value == 0);

    public DateTime LocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    private bool IsRealOpponent(MatchPlayerRecord other) => MatchHistory.IsRealOpponent(Row, other);
}
