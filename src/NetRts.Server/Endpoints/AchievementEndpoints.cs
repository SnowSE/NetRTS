using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Endpoints;

public static class AchievementEndpoints
{
    private const string Secret = "???";

    public static void MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Players & info");

        api.MapGet("/achievements", Catalog)
            .WithSummary("Every achievement, how many bots have earned it, and who (first earners first; earners=N, default 12). Hidden ones read \"???\" until someone earns them.");

        api.MapGet("/players/{playerId:guid}/achievements", async (Guid playerId, NetRtsDb db, CancellationToken ct) =>
                await db.Players.AnyAsync(p => p.Id == playerId, ct)
                    ? Results.Ok(await EarnedAsync(db, db.BotAchievements.Where(a => a.PlayerId == playerId), listBots: false, ct))
                    : ApiErrors.NotFound("Player"))
            .Produces<IReadOnlyList<EarnedAchievementDto>>()
            .ProducesErrors()
            .WithSummary("The achievements a bot has earned, newest first.");
    }

    private static async Task<IReadOnlyList<AchievementDto>> Catalog(NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct, int earners = 12)
    {
        earners = Math.Clamp(earners, 0, 200);
        var rows = await db.BotAchievements.AsNoTracking()
            .Join(db.Players.Include(p => p.Owner), a => a.PlayerId, p => p.Id, (a, p) => new { a.AchievementId, a.EarnedAt, Player = p })
            .ToListAsync(ct);
        var byId = rows.GroupBy(r => r.AchievementId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.EarnedAt).ThenBy(r => r.Player.Name, StringComparer.Ordinal).ToList());

        return AchievementCatalog.All.Select(a =>
        {
            var earned = byId.GetValueOrDefault(a.Id) ?? [];
            var secret = a.Hidden && earned.Count == 0;
            var first = earned.FirstOrDefault();
            return new AchievementDto
            {
                Id = a.Id,
                Name = secret ? Secret : a.Name,
                Description = secret ? Secret : a.Description,
                Group = a.Group,
                Tier = a.Tier,
                Hidden = a.Hidden,
                HouseBotsCount = a.HouseBotsCount,
                EarnedBy = earned.Count,
                FirstEarnedBy = first?.Player.Name,
                FirstEarnedAt = first is null ? null : Utc(first.EarnedAt),
                Earners = earned.Take(earners).Select(r =>
                {
                    var verified = Accounts.IsVerified(r.Player.Owner, discord.Value);
                    return new AchievementEarnerDto
                    {
                        PlayerId = r.Player.Id,
                        Bot = r.Player.Name,
                        OwnerName = verified ? r.Player.Owner!.DisplayName : null,
                        OwnerId = verified ? r.Player.Owner!.Id : null,
                        EarnedAt = Utc(r.EarnedAt),
                    };
                }).ToList(),
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
