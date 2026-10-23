using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Matches;

/// <summary>One bot's part in a closed league match: each heat's points, and what went on the ladder.</summary>
public sealed record LeagueMatchTotal(Guid PlayerId, string Name, bool Verified, string? OwnerName, IReadOnlyList<int> HeatPoints, int Points);

/// <summary>
/// Puts a league match's points on the season ladder once it's over. Heats record their points as they
/// finish, but the ladder only takes them when the admin closes the match: each bot's heats, less its
/// lowest when it played two or more (<see cref="LeaguePoints.Night"/>). Then the results and the
/// ladder are posted to Discord. A match nobody closed (the server restarted, say) is finalized
/// <see cref="NetRtsOptions.LeagueAutoFinalizeMinutes"/> after its last heat.
/// </summary>
public sealed class LeagueFinalizer(
    IServiceScopeFactory scopes,
    LeagueMatchManager leagues,
    MatchAnnouncer announcer,
    IOptions<NetRtsOptions> options,
    IOptions<DiscordOptions> discord,
    ILogger<LeagueFinalizer> logger)
{
    private const int LadderShown = 10;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Finalizes a closed league match once every heat has been recorded (waiting up to
    /// <paramref name="wait"/> for that). False if some heat still isn't recorded; the sweep retries.
    /// </summary>
    public async Task<bool> FinishAsync(LeagueMatch league, TimeSpan wait, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (!leagues.AllHeatsRecorded(league))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(100, ct);
        }

        var final = await FinalizeAsync(league.Id, ct);
        leagues.MarkFinalized(league);
        if (final is { } f)
        {
            await AnnounceAsync(league.Name, league.Id, f.Category, f.Totals, ct);
        }

        return true;
    }

    /// <summary>
    /// Adds the points of a league match's heats that aren't on the ladder yet. Returns its category and
    /// every bot's total, best first, or null if there was nothing left to add.
    /// </summary>
    public async Task<(MatchCategory Category, IReadOnlyList<LeagueMatchTotal> Totals)?> FinalizeAsync(Guid leagueMatchId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var rows = await db.MatchPlayers.Where(r => r.LeagueMatchId == leagueMatchId).ToListAsync(ct);
            if (rows.All(r => r.PointsCounted))
            {
                return null;
            }

            var category = rows[0].Category;
            var ids = rows.Select(r => r.PlayerId).Distinct().ToList();
            var stats = await db.PlayerStats.Where(s => ids.Contains(s.PlayerId) && s.Category == category && s.Season == Seasons.Current)
                .ToDictionaryAsync(s => s.PlayerId, ct);
            var players = await db.Players.AsNoTracking().Include(p => p.Owner).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

            var totals = new List<LeagueMatchTotal>();
            foreach (var heats in rows.GroupBy(r => r.PlayerId))
            {
                // House bots without a player row keep no record.
                if (!stats.TryGetValue(heats.Key, out var s) || !players.TryGetValue(heats.Key, out var player))
                {
                    continue;
                }

                var points = heats.OrderBy(r => r.CompletedAt).Select(r => r.Points).ToList();
                var counted = heats.Where(r => r.PointsCounted).Select(r => r.Points).ToList();
                var night = LeaguePoints.Night(points);
                s.Points += night - LeaguePoints.Night(counted);
                var verified = Accounts.IsVerified(player.Owner, discord.Value);
                totals.Add(new LeagueMatchTotal(player.Id, player.Name, verified, verified ? player.Owner!.DisplayName : null, points, night));
            }

            foreach (var row in rows)
            {
                row.PointsCounted = true;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Finalized league match {MatchId}: {Players} bots' points added to the {Category} ladder", leagueMatchId, totals.Count, category);
            return (category, [.. totals.OrderByDescending(t => t.Points).ThenBy(t => t.Name)]);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finalizes closed league matches still waiting for a heat to be recorded, and matches nobody
    /// closed whose last heat finished long enough ago (and that aren't still open in memory).
    /// </summary>
    public async Task SweepAsync(DateTime now, CancellationToken ct)
    {
        foreach (var league in leagues.All.Where(l => l.Closed && !l.Finalized))
        {
            await FinishAsync(league, TimeSpan.Zero, ct);
        }

        var cutoff = now - TimeSpan.FromMinutes(options.Value.LeagueAutoFinalizeMinutes);
        List<Guid> forgotten;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            forgotten = await db.MatchPlayers.Where(r => !r.PointsCounted && r.LeagueMatchId != null)
                .GroupBy(r => r.LeagueMatchId!.Value)
                .Where(g => g.Max(r => r.CompletedAt) < cutoff)
                .Select(g => g.Key)
                .ToListAsync(ct);
        }

        foreach (var id in forgotten.Where(id => leagues.Find(id) is null))
        {
            logger.LogInformation("League match {MatchId} was never closed; finalizing it", id);
            if (await FinalizeAsync(id, ct) is { } final)
            {
                await AnnounceAsync(null, null, final.Category, final.Totals, ct);
            }
        }
    }

    private async Task AnnounceAsync(string? name, Guid? leagueId, MatchCategory category, IReadOnlyList<LeagueMatchTotal> totals, CancellationToken ct)
    {
        if (!announcer.Enabled)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        var ladder = await PlayerEndpoints.Leaderboard(db, discord, ct, category, LadderShown);
        announcer.LeagueMatchFinal(name, leagueId, category, totals, ladder);
    }
}
