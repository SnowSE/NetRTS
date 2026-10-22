using Microsoft.EntityFrameworkCore;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Data;

namespace NetRts.Server.Endpoints;

public static class AchievementEndpoints
{
    private const string Secret = "???";

    public static void MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Players & info");

        api.MapGet("/achievements", Catalog)
            .WithSummary("Every achievement, how many bots have earned it, and who was first. Hidden ones read \"???\" until someone earns them.");

        api.MapGet("/players/{playerId:guid}/achievements", async (Guid playerId, NetRtsDb db, CancellationToken ct) =>
                await db.Players.AnyAsync(p => p.Id == playerId, ct)
                    ? Results.Ok(await EarnedAsync(db, db.BotAchievements.Where(a => a.PlayerId == playerId), listBots: false, ct))
                    : ApiErrors.NotFound("Player"))
            .Produces<IReadOnlyList<EarnedAchievementDto>>()
            .ProducesErrors()
            .WithSummary("The achievements a bot has earned, newest first.");
    }

    private static async Task<IReadOnlyList<AchievementDto>> Catalog(NetRtsDb db, CancellationToken ct)
    {
        var counts = await db.BotAchievements.GroupBy(a => a.AchievementId)
            .Select(g => new { Id = g.Key, Count = g.Count(), First = g.Min(a => a.EarnedAt) })
            .ToListAsync(ct);
        var ids = counts.Select(c => c.Id).ToList();
        var firsts = (await db.BotAchievements.AsNoTracking()
                .Where(a => ids.Contains(a.AchievementId))
                .Join(db.Players, a => a.PlayerId, p => p.Id, (a, p) => new { a.AchievementId, a.EarnedAt, p.Name })
                .ToListAsync(ct))
            .GroupBy(a => a.AchievementId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.EarnedAt).ThenBy(a => a.Name, StringComparer.Ordinal).First());
        var byId = counts.ToDictionary(c => c.Id);

        return AchievementCatalog.All.Select(a =>
        {
            var earned = byId.GetValueOrDefault(a.Id);
            var secret = a.Hidden && earned is null;
            var first = firsts.GetValueOrDefault(a.Id);
            return new AchievementDto
            {
                Id = a.Id,
                Name = secret ? Secret : a.Name,
                Description = secret ? Secret : a.Description,
                Group = a.Group,
                Tier = a.Tier,
                Hidden = a.Hidden,
                HouseBotsCount = a.HouseBotsCount,
                EarnedBy = earned?.Count ?? 0,
                FirstEarnedBy = first?.Name,
                FirstEarnedAt = first is null ? null : Utc(first.EarnedAt),
            };
        }).ToList();
    }

    /// <summary>
    /// Earned achievements, newest first, each once (when it was first earned). With
    /// <paramref name="listBots"/> (a person's bots together) each lists the bots that earned it.
    /// </summary>
    internal static async Task<IReadOnlyList<EarnedAchievementDto>> EarnedAsync(NetRtsDb db, IQueryable<BotAchievementRecord> query, bool listBots, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Join(db.Players, a => a.PlayerId, p => p.Id, (a, p) => new { a.AchievementId, a.EarnedAt, a.MatchId, p.Name })
            .ToListAsync(ct);
        return rows
            .Where(r => AchievementCatalog.ById.ContainsKey(r.AchievementId))
            .GroupBy(r => r.AchievementId)
            .Select(g =>
            {
                var a = AchievementCatalog.ById[g.Key];
                var first = g.OrderBy(r => r.EarnedAt).First();
                return new EarnedAchievementDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Description = a.Description,
                    Group = a.Group,
                    Tier = a.Tier,
                    EarnedAt = Utc(first.EarnedAt),
                    MatchId = first.MatchId,
                    Bots = listBots ? g.OrderBy(r => r.EarnedAt).Select(r => r.Name).ToList() : [],
                };
            })
            .OrderByDescending(e => e.EarnedAt)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static DateTimeOffset Utc(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
