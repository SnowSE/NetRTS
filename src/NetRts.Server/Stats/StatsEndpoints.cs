using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Stats;

/// <summary>The Stats page's data: every finished match, compact, rebuilt at most every few minutes.</summary>
public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var stats = app.MapGroup("/api/v1/stats").WithTags("Stats");

        stats.MapGet("/matches", (MatchStatsCache cache, CancellationToken ct) => cache.GetAsync(ct))
            .Produces<MatchStatsDto>()
            .WithSummary("Every finished match in a compact form (bots listed once, matches oldest first), for the Stats page. Refreshed every few minutes.");

        stats.MapGet("/server", (ServerStatsService server) => server.Current is { } current
                ? Results.Ok(current)
                : ApiErrors.Error(404, "NOT_AVAILABLE", "The server's telemetry isn't available here yet."))
            .Produces<ServerStatsDto>()
            .ProducesErrors()
            .WithSummary("How the server held up (requests, ticks, errors, uptime checks), from its telemetry, rebuilt each morning through the end of yesterday.");
    }
}

/// <summary>Builds <see cref="MatchStatsDto"/> from the archive and keeps it for <see cref="MaxAge"/>.</summary>
public sealed class MatchStatsCache(IServiceScopeFactory scopes, TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private MatchStatsDto? _stats;

    public async Task<MatchStatsDto> GetAsync(CancellationToken ct)
    {
        if (Fresh(_stats) is { } stats)
        {
            return stats;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh(_stats) is { } built)
            {
                return built;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var rows = await db.Matches.AsNoTracking()
                .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                .Select(m => new { m.Id, m.CreatedAt, m.Reason, m.Ticks, m.TickIntervalMs, m.WinnerId, m.Category, m.SummaryJson })
                .ToListAsync(ct);

            var bots = new List<StatsBotDto>();
            var place = new Dictionary<Guid, int>();
            var matches = new List<StatsMatchDto>(rows.Count);
            foreach (var row in rows)
            {
                var players = JsonSerializer.Deserialize<MatchSummaryDto>(row.SummaryJson, NetRtsClient.JsonOptions)!.Players
                    .OrderBy(p => p.Slot).ToList();
                var ids = new List<int>(players.Count);
                foreach (var p in players)
                {
                    var bot = new StatsBotDto { Name = p.Name, Kind = p.IsHouseBot ? "house" : p.IsBlockBot ? "block" : "code" };
                    if (place.TryGetValue(p.PlayerId, out var at))
                    {
                        bots[at] = bot;   // a renamed bot shows its latest name
                    }
                    else
                    {
                        at = place[p.PlayerId] = bots.Count;
                        bots.Add(bot);
                    }

                    ids.Add(at);
                }

                matches.Add(new StatsMatchDto
                {
                    Id = row.Id,
                    At = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
                    Reason = row.Reason,
                    Ticks = row.Ticks,
                    TickMs = row.TickIntervalMs,
                    Winner = players.FindIndex(p => p.PlayerId == row.WinnerId),
                    Players = ids,
                    Category = row.Category,
                });
            }

            return _stats = new MatchStatsDto { GeneratedAt = time.GetUtcNow(), Bots = bots, Matches = matches };
        }
        finally
        {
            _gate.Release();
        }
    }

    private MatchStatsDto? Fresh(MatchStatsDto? stats) =>
        stats is not null && time.GetUtcNow() - stats.GeneratedAt < MaxAge ? stats : null;
}
