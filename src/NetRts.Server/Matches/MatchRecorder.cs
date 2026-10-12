using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

/// <summary>
/// Persists finished matches (summary, result, replay, frames) and updates win/loss records and Elo
/// ratings. Runs off the tick loop so a slow database never stalls a game.
/// </summary>
public sealed class MatchRecorder(IServiceScopeFactory scopes, ILogger<MatchRecorder> logger, MatchMetrics metrics, MatchAnnouncer announcer) : BackgroundService
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

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();

        db.Matches.Add(new MatchRecord
        {
            Id = host.Id,
            CreatedAt = host.CreatedAt,
            CompletedAt = host.CompletedAt ?? DateTime.UtcNow,
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

        // Exhibitions between house bots are entertainment, not ladder games.
        if (seats.Any(s => !s.IsHouseBot))
        {
            // Second and later copies of a house bot have no player row, so they keep no record.
            var ids = seats.Select(s => s.PlayerId).ToList();
            var known = await db.Players.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            var stats = await db.PlayerStats.Where(s => known.Contains(s.PlayerId) && s.Category == host.Category && s.Season == Seasons.Current)
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
                var places = ranked.Select(p => 1 + ranked.Count(q =>
                    (q.Winner && !p.Winner) || (q.Winner == p.Winner && q.Score.Total > p.Score.Total))).ToList();
                var ratings = Elo.UpdateMany(ranked.Select(p => stats[p.PlayerId].Rating).ToList(), places);
                for (var i = 0; i < ranked.Count; i++)
                {
                    stats[ranked[i].PlayerId].Rating = ratings[i];
                }
            }

            if (host.Category != MatchCategory.Practice)
            {
                var points = LeaguePoints.Award(ranked.Select(p => p.Score.Total).ToList());
                for (var i = 0; i < ranked.Count; i++)
                {
                    stats[ranked[i].PlayerId].Points += points[i];
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded {Category} match {MatchId} ({Reason}, rated: {Rated})", host.Category, host.Id, result.Outcome.Reason, rated);
    }
}
