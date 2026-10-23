using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Achievements;

/// <summary>
/// Awards achievements. Each recorded match is queued here (<see cref="MatchRecorded"/>) and checked
/// for every bot in it, one match at a time. On startup, matches recorded before the match history
/// existed get their history rows and achievements first, in the order they were played, quietly.
/// </summary>
public sealed class AchievementService(
    IServiceScopeFactory scopes,
    HouseBotDirectory houseBots,
    MatchAnnouncer announcer,
    IOptions<NetRtsOptions> options,
    IOptions<DiscordOptions> discord,
    ILogger<AchievementService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private int _pending;

    /// <summary>Queues a just-recorded match (its history rows already saved) for its achievements.</summary>
    public void MatchRecorded(Guid matchId)
    {
        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite(matchId);
    }

    /// <summary>For tests: waits until every queued match has been checked.</summary>
    public async Task DrainAsync(CancellationToken ct)
    {
        while (Volatile.Read(ref _pending) > 0)
        {
            await Task.Delay(20, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _pending);
        try
        {
            await BackfillAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Achievement backfill failed");
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }

        await foreach (var matchId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await EvaluateAsync(matchId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not award achievements for match {MatchId}", matchId);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private async Task EvaluateAsync(Guid matchId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        var match = await db.Matches.AsNoTracking().Where(m => m.Id == matchId)
            .Select(m => new { m.CompletedAt, m.MaxTicks, m.ResultJson }).FirstOrDefaultAsync(ct);
        if (match is null)
        {
            return;
        }

        var result = JsonSerializer.Deserialize<MatchResultDto>(match.ResultJson, NetRtsClient.JsonOptions)!;
        var rows = await db.MatchPlayers.AsNoTracking().Where(r => r.MatchId == matchId && !r.IsHouseBot).ToListAsync(ct);
        foreach (var row in rows)
        {
            var bot = await db.Players.AsNoTracking().Include(p => p.Owner).FirstAsync(p => p.Id == row.PlayerId, ct);
            var history = await db.MatchPlayers.AsNoTracking()
                .Where(r => r.PlayerId == row.PlayerId && r.CompletedAt <= match.CompletedAt)
                .OrderBy(r => r.CompletedAt).ThenBy(r => r.MatchId)
                .ToListAsync(ct);
            var opponents = await db.MatchPlayers.AsNoTracking()
                .Where(o => o.PlayerId != row.PlayerId && db.MatchPlayers.Any(m => m.PlayerId == row.PlayerId && m.MatchId == o.MatchId))
                .ToListAsync(ct);
            var bestRating = await db.PlayerStats.Where(s => s.PlayerId == row.PlayerId && s.Season == Seasons.Current)
                .Select(s => (int?)s.Rating).MaxAsync(ct) ?? Elo.InitialRating;
            var ownerBots = bot.OwnerAccountId is { } owner
                ? await db.MatchPlayers.Where(r => r.Result == MatchResultKind.Win && r.RealOpponents > 0
                                                    && db.Players.Any(p => p.Id == r.PlayerId && p.OwnerAccountId == owner))
                    .Select(r => r.PlayerId).Distinct().CountAsync(ct)
                : 0;
            var verified = Accounts.IsVerified(bot.Owner, discord.Value);
            var context = new AchievementContext(row, result.Players.First(p => p.Slot == row.Slot), result.Players, history,
                opponents.ToLookup(o => o.MatchId), match.MaxTicks, houseBots.Ids, verified, bestRating, ownerBots, options.Value.Zone);
            await AwardAsync(db, context, match.CompletedAt, announce: (bot.Name, verified, verified ? bot.Owner!.DisplayName : null), ct);
        }
    }

    /// <summary>Checks every achievement for one bot in one match and stores the new ones.</summary>
    private async Task AwardAsync(NetRtsDb db, AchievementContext context, DateTime earnedAt, (string Bot, bool Verified, string? Owner)? announce, CancellationToken ct)
    {
        var have = await db.BotAchievements.Where(a => a.PlayerId == context.Row.PlayerId).Select(a => a.AchievementId).ToListAsync(ct);
        var earned = Earned(context).Where(a => !have.Contains(a.Id)).ToList();
        if (earned.Count == 0)
        {
            return;
        }

        var ids = earned.Select(a => a.Id).ToList();
        var firsts = announce is null
            ? []
            : ids.Except(await db.BotAchievements.Where(a => ids.Contains(a.AchievementId)).Select(a => a.AchievementId).Distinct().ToListAsync(ct)).ToList();
        foreach (var achievement in earned)
        {
            db.BotAchievements.Add(new BotAchievementRecord
            {
                PlayerId = context.Row.PlayerId,
                AchievementId = achievement.Id,
                EarnedAt = earnedAt,
                MatchId = context.Row.MatchId,
            });
        }

        await db.SaveChangesAsync(ct);
        // Discord hears straight away about the first bot ever to earn each achievement.
        if (announce is { } who && firsts.Count > 0)
        {
            announcer.FirstToEarn(who.Bot, who.Verified, who.Owner, firsts.Select(id => AchievementCatalog.ById[id]).Select(a => (a.Name, a.Description)).ToList());
        }
    }

    /// <summary>The achievements a bot's match earns (whether or not it already has them).</summary>
    public static IEnumerable<Achievement> Earned(AchievementContext context) =>
        AchievementCatalog.All.Where(a => a.Check is not null
                                          && (a.HouseBotsCount || context.Row.RealOpponents > 0)
                                          && a.Check(context));

    /// <summary>
    /// Builds history rows for every match that has none (matches recorded before the history existed),
    /// then awards those matches' achievements in the order they were played.
    /// </summary>
    internal async Task BackfillAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var missing = await db.Matches.AsNoTracking()
            .Where(m => !db.MatchPlayers.Any(r => r.MatchId == m.Id))
            .OrderBy(m => m.CompletedAt)
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (missing.Count == 0)
        {
            return;
        }

        logger.LogInformation("Backfilling match history and achievements for {Count} matches", missing.Count);
        var players = await db.Players.AsNoTracking().Include(p => p.Owner).ToDictionaryAsync(p => p.Id, ct);
        var known = players.Keys.ToHashSet();
        var results = new Dictionary<Guid, (MatchResultDto Result, int MaxTicks)>();
        foreach (var batch in missing.Chunk(200))
        {
            var matches = await db.Matches.AsNoTracking().Where(m => batch.Contains(m.Id))
                .Select(m => new { m.Id, m.CompletedAt, m.Category, m.MaxTicks, m.SummaryJson, m.ResultJson }).ToListAsync(ct);
            foreach (var m in matches)
            {
                var summary = JsonSerializer.Deserialize<MatchSummaryDto>(m.SummaryJson, NetRtsClient.JsonOptions)!;
                var result = JsonSerializer.Deserialize<MatchResultDto>(m.ResultJson, NetRtsClient.JsonOptions)!;
                results[m.Id] = (result, m.MaxTicks);
                var seats = summary.Players.Select(p => new HistorySeat(p.PlayerId, p.Slot, p.IsHouseBot,
                    players.GetValueOrDefault(p.PlayerId)?.OwnerAccountId)).ToList();
                // Who surrendered isn't in the summary; in a match that ended by surrender, the losers did.
                var surrendered = result.Outcome.Reason == MatchEndReason.Surrender
                    ? result.Players.Where(p => !p.Winner).Select(p => p.Slot).ToHashSet()
                    : null;
                db.MatchPlayers.AddRange(MatchHistory.Rows(m.Id, m.CompletedAt, m.Category, result, seats, known, surrenderedSlots: surrendered,
                    leagueMatchId: summary.ParentMatchId));
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        // Achievements, in the order the matches were played, from the history in memory.
        var all = await db.MatchPlayers.AsNoTracking().OrderBy(r => r.CompletedAt).ThenBy(r => r.MatchId).ToListAsync(ct);
        var byMatch = all.ToLookup(r => r.MatchId);
        var byPlayer = all.GroupBy(r => r.PlayerId).ToDictionary(g => g.Key, g => g.ToList());
        var ratings = await db.PlayerStats.AsNoTracking().Where(s => s.Season == Seasons.Current)
            .GroupBy(s => s.PlayerId).Select(g => new { g.Key, Best = g.Max(s => s.Rating) }).ToDictionaryAsync(x => x.Key, x => x.Best, ct);
        var have = (await db.BotAchievements.AsNoTracking().Select(a => new { a.PlayerId, a.AchievementId }).ToListAsync(ct))
            .Select(a => (a.PlayerId, a.AchievementId)).ToHashSet();
        var winners = new Dictionary<Guid, HashSet<Guid>>(); // owner -> their bots with a real win so far
        var awarded = 0;
        foreach (var matchId in missing)
        {
            if (!results.TryGetValue(matchId, out var match))
            {
                continue;
            }

            foreach (var row in byMatch[matchId].Where(r => !r.IsHouseBot))
            {
                var bot = players[row.PlayerId];
                var mine = byPlayer[row.PlayerId];
                var history = mine.Take(mine.IndexOf(row) + 1).ToList();
                var opponents = history.SelectMany(h => byMatch[h.MatchId]).Where(o => o.PlayerId != row.PlayerId).ToLookup(o => o.MatchId);
                if (bot.OwnerAccountId is { } owner && row.Result == MatchResultKind.Win && row.RealOpponents > 0)
                {
                    (winners.TryGetValue(owner, out var set) ? set : winners[owner] = []).Add(row.PlayerId);
                }

                var context = new AchievementContext(row, match.Result.Players.First(p => p.Slot == row.Slot), match.Result.Players, history,
                    opponents, match.MaxTicks, houseBots.Ids, Accounts.IsVerified(bot.Owner, discord.Value),
                    ratings.GetValueOrDefault(row.PlayerId, Elo.InitialRating),
                    bot.OwnerAccountId is { } o && winners.TryGetValue(o, out var bots) ? bots.Count : 0,
                    options.Value.Zone);
                foreach (var achievement in Earned(context).Where(a => have.Add((row.PlayerId, a.Id))))
                {
                    db.BotAchievements.Add(new BotAchievementRecord
                    {
                        PlayerId = row.PlayerId,
                        AchievementId = achievement.Id,
                        EarnedAt = row.CompletedAt,
                        MatchId = matchId,
                    });
                    awarded++;
                }
            }

            if (db.ChangeTracker.Entries().Count() >= 500)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Backfill done: {Rows} history rows, {Awarded} achievements", all.Count, awarded);
    }
}
