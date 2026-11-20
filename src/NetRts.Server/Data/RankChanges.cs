using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Data;

/// <summary>
/// Who moved on a league ladder: just before a league match's points go on (see <c>LeagueFinalizer</c>),
/// each bot's rank is kept in <see cref="PlayerStatsRecord.PreviousRank"/>, so the scoreboard can show
/// ▲ 3 or ▼ 1 since the last match. Bots playing their first match in the category weren't ranked
/// before it, so they have none (new).
/// </summary>
public static class RankChanges
{
    /// <summary>The migration that adds <see cref="PlayerStatsRecord.PreviousRank"/>; the backfill runs when it does.</summary>
    public const string Migration = "PreviousRanks";

    /// <summary>
    /// Keeps every bot's rank on <paramref name="category"/>'s ladder as it stands now, before the league
    /// match whose history rows are <paramref name="match"/> adds its points. Call before adding them;
    /// the caller saves.
    /// </summary>
    public static async Task RememberAsync(NetRtsDb db, IOptions<DiscordOptions> discord, MatchCategory category,
        IReadOnlyList<MatchPlayerRecord> match, CancellationToken ct)
    {
        // Wins and losses go on as each heat finishes; the points wait for the match to close. A bot whose
        // only games are this match's heats wasn't really on the ladder yet.
        var heats = match.GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.Count());
        var ladder = await PlayerEndpoints.Leaderboard(db, discord, ct, category, limit: 200);
        var before = ladder.Where(e => !(heats.TryGetValue(e.PlayerId, out var n) && e.Wins + e.Losses + e.Draws <= n))
            .Select(e => e.PlayerId).ToList();
        await SetAsync(db, category, before, ct);
    }

    /// <summary>
    /// For a database that has league matches but no previous ranks yet (the migration that adds them
    /// just ran): works out each league ladder as it stood before its latest league match, by taking that
    /// match's points back off.
    /// </summary>
    public static async Task BackfillAsync(NetRtsDb db, IOptions<DiscordOptions> discord, ILogger logger, CancellationToken ct = default)
    {
        foreach (var category in Enum.GetValues<MatchCategory>().Where(c => c != MatchCategory.Practice))
        {
            var last = await db.MatchPlayers.Where(r => r.Category == category && r.LeagueMatchId != null && r.PointsCounted)
                .OrderByDescending(r => r.CompletedAt).Select(r => r.LeagueMatchId).FirstOrDefaultAsync(ct);
            if (last is null)
            {
                continue;
            }

            var rows = await db.MatchPlayers.AsNoTracking().Where(r => r.LeagueMatchId == last).ToListAsync(ct);
            var added = rows.GroupBy(r => r.PlayerId)
                .ToDictionary(g => g.Key, g => LeaguePoints.Night([.. g.OrderBy(r => r.CompletedAt).Select(r => r.Points)]));
            var ids = added.Keys.ToList();
            var veterans = (await db.MatchPlayers.Where(r => r.Category == category && r.LeagueMatchId != last && ids.Contains(r.PlayerId))
                .Select(r => r.PlayerId).Distinct().ToListAsync(ct)).ToHashSet();
            var ladder = await PlayerEndpoints.Leaderboard(db, discord, ct, category, limit: 200);
            var before = ladder
                .Where(e => !added.ContainsKey(e.PlayerId) || veterans.Contains(e.PlayerId))
                .Select((e, i) => (e.PlayerId, Points: e.Points - added.GetValueOrDefault(e.PlayerId), Order: i))
                .OrderByDescending(x => x.Points).ThenBy(x => x.Order)
                .Select(x => x.PlayerId).ToList();
            await SetAsync(db, category, before, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Backfilled previous ranks on the {Category} ladder ({Bots} bots)", category, before.Count);
        }
    }

    /// <summary>Each current-season row in the category gets its place in <paramref name="before"/> (best first), or none.</summary>
    private static async Task SetAsync(NetRtsDb db, MatchCategory category, IReadOnlyList<Guid> before, CancellationToken ct)
    {
        var rank = before.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        foreach (var s in await db.PlayerStats.Where(s => s.Category == category && s.Season == Seasons.Current).ToListAsync(ct))
        {
            s.PreviousRank = rank.TryGetValue(s.PlayerId, out var r) ? r : null;
        }
    }
}
