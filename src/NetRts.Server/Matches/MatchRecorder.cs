using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

/// <summary>
/// Persists finished matches (summary, result, replay, frames) and updates win/loss records, Elo
/// ratings, OpenSkill skills and league points. Runs off the tick loop so a slow database never stalls a game.
/// </summary>
public sealed class MatchRecorder(
    IServiceScopeFactory scopes, ILogger<MatchRecorder> logger, MatchMetrics metrics, MatchAnnouncer announcer, AchievementService achievements) : BackgroundService
{
    private readonly Channel<MatchHost> _queue = metrics.ObserveRecorderQueue(Channel.CreateUnbounded<MatchHost>());

    public void Enqueue(MatchHost host) => _queue.Writer.TryWrite(host);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var host in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var activity = MatchMetrics.ActivitySource.StartActivity("record match");
            activity?.SetTag("netrts.match.id", host.Id);
            var started = Stopwatch.GetTimestamp();
            try
            {
                await RecordAsync(host, stoppingToken);
                host.Recorded = true;
                achievements.MatchRecorded(host.Id);
                metrics.MatchRecorded(Stopwatch.GetElapsedTime(started));
                announcer.MatchRecorded(host);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to record match {MatchId}", host.Id);
                metrics.MatchRecordFailed();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
    }

    private async Task RecordAsync(MatchHost host, CancellationToken ct)
    {
        var result = host.GetResult();
        var replay = host.GetReplay();
        if (result is null || replay is null)
        {
            return;
        }

        var summary = host.ToSummary();
        var seats = host.Seats;
        // Each category (practice, season, championship) keeps its own ratings and records. Practice
        // ratings move only in player-vs-player 1v1s, since games against house bots could be farmed.
        // League matches are set up by admins, so every size is rated, by finishing place.
        var rated = host.Category == MatchCategory.Practice
            ? seats.Count == 2 && seats.All(s => !s.IsHouseBot)
            : seats.Count(s => !s.IsHouseBot) >= 2;

        var completedAt = host.CompletedAt ?? DateTime.UtcNow;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();

        db.Matches.Add(new MatchRecord
        {
            Id = host.Id,
            CreatedAt = host.CreatedAt,
            CompletedAt = completedAt,
            Seed = host.Settings.Seed,
            MapWidth = host.Settings.MapWidth,
            MapHeight = host.Settings.MapHeight,
            MaxTicks = host.Settings.MaxTicks,
            TickIntervalMs = host.Settings.TickIntervalMs,
            Ticks = result.Outcome.Ticks,
            Reason = result.Outcome.Reason.ToString(),
            WinnerId = result.Outcome.WinnerId,
            Rated = rated,
            Category = host.Category,
            SummaryJson = JsonSerializer.Serialize(summary, NetRtsClient.JsonOptions),
            ResultJson = JsonSerializer.Serialize(result, NetRtsClient.JsonOptions),
            ReplayJson = JsonSerializer.Serialize(replay, NetRtsClient.JsonOptions),
            RulesVersion = GameRules.Version,
        });
        if (host.Frames is { } frames)
        {
            ReplayFrameStore.Add(db, host.Id, frames, DateTime.UtcNow);
        }

        // Second and later copies of a house bot have no player row, so they keep no record.
        var ids = seats.Select(s => s.PlayerId).ToList();
        var known = await db.Players.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        Dictionary<Guid, (int Before, int After)> ratingChanges = [];
        Dictionary<Guid, PlayerStatsRecord> stats = [];

        // Exhibitions between house bots are entertainment, not ladder games.
        if (seats.Any(s => !s.IsHouseBot))
        {
            stats = await db.PlayerStats.Where(s => known.Contains(s.PlayerId) && s.Category == host.Category && s.Season == Seasons.Current)
                .ToDictionaryAsync(s => s.PlayerId, ct);
            foreach (var id in known.Where(id => !stats.ContainsKey(id)))
            {
                stats[id] = db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = id, Category = host.Category }).Entity;
            }

            var winner = result.Outcome.WinnerId;
            foreach (var seat in seats)
            {
                if (!stats.TryGetValue(seat.PlayerId, out var p))
                {
                    continue;
                }

                if (winner is null)
                {
                    p.Draws++;
                }
                else if (winner == seat.PlayerId)
                {
                    p.Wins++;
                }
                else
                {
                    p.Losses++;
                }
            }

            // Finishing order: the winner, then everyone else by final score (eliminated players have
            // no survival points, so they trail). Equal scores share a place.
            var ranked = result.Players.Where(p => stats.ContainsKey(p.PlayerId))
                .OrderByDescending(p => p.Winner).ThenByDescending(p => p.Score.Total).ToList();
            if (rated)
            {
                var places = OpenSkill.Places(ranked.Select(p => (p.Winner, p.Score.Total)).ToList());
                var ratings = Elo.UpdateMany(ranked.Select(p => stats[p.PlayerId].Rating).ToList(), places);
                var skills = OpenSkill.Rate(ranked.Select(p => OpenSkill.Of(stats[p.PlayerId])).ToList(), places);
                for (var i = 0; i < ranked.Count; i++)
                {
                    var s = stats[ranked[i].PlayerId];
                    ratingChanges[ranked[i].PlayerId] = (s.Rating, ratings[i]);
                    s.Rating = ratings[i];
                    (s.Mu, s.Sigma) = (skills[i].Mu, skills[i].Sigma);
                }
            }
        }

        var rows = MatchHistory.Rows(host.Id, completedAt, host.Category, result,
            seats.Select((s, slot) => new HistorySeat(s.PlayerId, slot, s.IsHouseBot, s.OwnerAccountId)).ToList(),
            known.ToHashSet(), rated ? ratingChanges : null, replay.Surrenders.Select(s => s.Slot).ToHashSet(), host.ParentMatchId);
        AddLeaguePoints(stats, rows);
        db.MatchPlayers.AddRange(rows);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded {Category} match {MatchId} ({Reason}, rated: {Rated})", host.Category, host.Id, result.Outcome.Reason, rated);
    }

    /// <summary>
    /// Adds a match's league points (already on its history <paramref name="rows"/>) to the season
    /// totals. A heat of a league match waits: its league match adds them when it is closed (see
    /// <see cref="LeagueFinalizer"/>), less each bot's lowest heat.
    /// </summary>
    internal static void AddLeaguePoints(IReadOnlyDictionary<Guid, PlayerStatsRecord> stats, IReadOnlyList<MatchPlayerRecord> rows)
    {
        foreach (var row in rows.Where(r => r.Category != MatchCategory.Practice && stats.ContainsKey(r.PlayerId)))
        {
            if (row.LeagueMatchId is not null)
            {
                row.PointsCounted = false;
                continue;
            }

            stats[row.PlayerId].Points += row.Points;
        }
    }
}
