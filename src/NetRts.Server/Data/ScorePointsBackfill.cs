using Microsoft.EntityFrameworkCore;
using NetRts.Protocol;

namespace NetRts.Server.Data;

/// <summary>
/// Recounts the league ladders once, when league points became each heat's whole score (see
/// <see cref="LeaguePoints"/>): every league history row gets its new points, and every ladder total is
/// added up again from the rows already counted in it (each league match's heats less the lowest, as
/// before). Heats still waiting for their league match to close are left to <c>LeagueFinalizer</c>.
/// It runs when the ScorePoints migration is applied.
/// </summary>
public static class ScorePointsBackfill
{
    public const string Migration = "ScorePoints";

    public static async Task<int> RunAsync(NetRtsDb db, ILogger logger, CancellationToken ct = default)
    {
        var rows = await db.MatchPlayers.Where(r => r.Category != MatchCategory.Practice).ToListAsync(ct);
        foreach (var match in rows.GroupBy(r => r.MatchId))
        {
            // As before: a match of house bots alone earns nothing.
            var points = match.All(r => r.IsHouseBot) ? null : LeaguePoints.Award(match.Select(r => r.Score).ToList());
            foreach (var (row, i) in match.Select((r, i) => (r, i)))
            {
                row.Points = points?[i] ?? 0;
            }
        }

        var matchIds = rows.Select(r => r.MatchId).Distinct().ToList();
        var seasons = await db.Matches.AsNoTracking().Where(m => matchIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Season, ct);
        var totals = rows.Where(r => r.PointsCounted && seasons.ContainsKey(r.MatchId))
            .GroupBy(r => (r.PlayerId, r.Category, Season: seasons[r.MatchId]))
            .ToDictionary(g => g.Key, g =>
                g.Where(r => r.LeagueMatchId is null).Sum(r => r.Points)
                + g.Where(r => r.LeagueMatchId is not null).GroupBy(r => r.LeagueMatchId)
                    .Sum(night => LeaguePoints.Night(night.OrderBy(r => r.CompletedAt).Select(r => r.Points).ToList())));

        var stats = await db.PlayerStats.Where(s => s.Category != MatchCategory.Practice).ToListAsync(ct);
        foreach (var s in stats)
        {
            s.Points = totals.GetValueOrDefault((s.PlayerId, s.Category, s.Season));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("League points are now whole scores: recounted {Rows} history rows and {Ladders} ladder rows", rows.Count, stats.Count);
        return stats.Count;
    }
}
