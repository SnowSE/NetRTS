using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Data;

/// <summary>
/// Brings data recorded before skills and per-heat league points up to date, once, at startup. The
/// SkillsAndHeatPoints migration adds <see cref="PlayerStatsRecord.Sigma"/> as 0 on existing rows, which
/// no rated player can have (a real sigma is always positive), so a 0 marks a database that still needs
/// this; afterwards there are none and it does nothing.
/// </summary>
public static class SkillBackfill
{
    public static async Task<bool> RunAsync(NetRtsDb db, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.PlayerStats.AnyAsync(s => s.Sigma <= 0, ct))
        {
            return false;
        }

        var heats = await FillLeagueHistoryAsync(db, ct);
        var players = await ReplaySkillsAsync(db, ct);
        logger.LogInformation("Backfilled skills for {Players} ladder rows and league points for {Rows} history rows", players, heats);
        return true;
    }

    /// <summary>
    /// League history rows get their match's points (<see cref="LeaguePoints.Award"/> over the registered
    /// bots in finishing order, as they were added to the ladder) and their heat's league match.
    /// Season totals are left as they are: until now nobody played two heats of one league match, so
    /// nothing would have been dropped.
    /// </summary>
    private static async Task<int> FillLeagueHistoryAsync(NetRtsDb db, CancellationToken ct)
    {
        var matches = await db.Matches.AsNoTracking().Where(m => m.Category != MatchCategory.Practice)
            .Select(m => new { m.Id, m.SummaryJson }).ToListAsync(ct);
        var parents = matches.ToDictionary(m => m.Id,
            m => JsonSerializer.Deserialize<MatchSummaryDto>(m.SummaryJson, NetRtsClient.JsonOptions)?.ParentMatchId);
        var rows = await db.MatchPlayers.Where(r => r.Category != MatchCategory.Practice).ToListAsync(ct);
        foreach (var match in rows.GroupBy(r => r.MatchId))
        {
            var ranked = match.OrderByDescending(r => r.Result == MatchResultKind.Win).ThenByDescending(r => r.Score).ToList();
            var points = match.All(r => r.IsHouseBot) ? new int[ranked.Count] : LeaguePoints.Award(ranked.Select(r => r.Score).ToList());
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].Points = points[i];
                ranked[i].LeagueMatchId = parents.GetValueOrDefault(match.Key);
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return rows.Count;
    }

    /// <summary>
    /// Every ladder's skills, replayed from the rated matches' history in the order they were played
    /// (each season and category on its own, as they are rated). Returns how many ladder rows it set.
    /// </summary>
    private static async Task<int> ReplaySkillsAsync(NetRtsDb db, CancellationToken ct)
    {
        var history = await db.MatchPlayers.AsNoTracking()
            .Join(db.Matches.Where(m => m.Rated), r => r.MatchId, m => m.Id,
                (r, m) => new { r.MatchId, r.PlayerId, r.Place, m.CompletedAt, m.Category, m.Season })
            .ToListAsync(ct);
        var skills = new Dictionary<(Guid Player, MatchCategory Category, string Season), SkillRating>();
        foreach (var match in history.GroupBy(r => r.MatchId).OrderBy(g => g.First().CompletedAt).ThenBy(g => g.Key))
        {
            var rows = match.OrderBy(r => r.Place).ThenBy(r => r.PlayerId).ToList();
            if (rows.Count < 2)
            {
                continue;
            }

            var keys = rows.Select(r => (r.PlayerId, r.Category, r.Season)).ToList();
            var rated = OpenSkill.Rate(keys.Select(k => skills.GetValueOrDefault(k, OpenSkill.Initial)).ToList(), rows.Select(r => r.Place).ToList());
            for (var i = 0; i < keys.Count; i++)
            {
                skills[keys[i]] = rated[i];
            }
        }

        var stats = await db.PlayerStats.Where(s => s.Sigma <= 0).ToListAsync(ct);
        foreach (var s in stats)
        {
            var skill = skills.GetValueOrDefault((s.PlayerId, s.Category, s.Season), OpenSkill.Initial);
            (s.Mu, s.Sigma) = (skill.Mu, skill.Sigma);
        }

        await db.SaveChangesAsync(ct);
        return stats.Count;
    }
}
