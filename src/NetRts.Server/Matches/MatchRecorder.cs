using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

/// <summary>
/// Persists finished matches (summary, result, replay) and updates win/loss records and Elo
/// ratings. Runs off the tick loop so a slow database never stalls a game.
/// </summary>
public sealed class MatchRecorder(IServiceScopeFactory scopes, ILogger<MatchRecorder> logger, MatchMetrics metrics) : BackgroundService
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
        // Only player-vs-player 1v1s move ratings; practice against house bots could otherwise be farmed.
        var rated = seats.Count == 2 && seats.All(s => !s.IsHouseBot);

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
            SummaryJson = JsonSerializer.Serialize(summary, NetRtsClient.JsonOptions),
            ResultJson = JsonSerializer.Serialize(result, NetRtsClient.JsonOptions),
            ReplayJson = JsonSerializer.Serialize(replay, NetRtsClient.JsonOptions),
        });

        // Exhibitions between house bots are entertainment, not ladder games.
        if (seats.Any(s => !s.IsHouseBot))
        {
            var ids = seats.Select(s => s.PlayerId).ToList();
            var players = await db.Players.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var winner = result.Outcome.WinnerId;
            foreach (var seat in seats)
            {
                if (!players.TryGetValue(seat.PlayerId, out var p))
                {
                    continue;
                }

                if (winner is null)
                {
                    p.Draws++;
                }
                else if (winner == p.Id)
                {
                    p.Wins++;
                }
                else
                {
                    p.Losses++;
                }
            }

            if (rated && players.TryGetValue(seats[0].PlayerId, out var a) && players.TryGetValue(seats[1].PlayerId, out var b))
            {
                var scoreA = winner is null ? 0.5 : winner == a.Id ? 1.0 : 0.0;
                (a.Rating, b.Rating) = Elo.Update(a.Rating, b.Rating, scoreA);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded match {MatchId} ({Reason}, rated: {Rated})", host.Id, result.Outcome.Reason, rated);
    }
}
