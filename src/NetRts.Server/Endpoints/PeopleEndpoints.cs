using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Endpoints;

/// <summary>One row of the Trophy Room: a person and what their bots have earned between them.</summary>
public sealed record TrophyRoomEntryDto
{
    public int Rank { get; init; }
    public Guid AccountId { get; init; }

    /// <summary>Their profile's name: #/player/{slug}, GET /api/v1/people/{slug}.</summary>
    public string Slug { get; init; } = "";

    public string DisplayName { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public int Bots { get; init; }

    /// <summary>Different achievements earned by any of their bots (each counts once).</summary>
    public int Achievements { get; init; }

    public int Bronze { get; init; }
    public int Silver { get; init; }
    public int Gold { get; init; }
    public int Platinum { get; init; }

    /// <summary>Trophy points: 1 per Bronze, 2 per Silver, 4 per Gold, 8 per Platinum. The Trophy Room is ranked by these.</summary>
    public int Points { get; init; }

    public DateTimeOffset? LatestAt { get; init; }
}

/// <summary>A person's public profile: their bots, what those bots have earned, and the brawls they played.</summary>
public sealed record PersonProfileDto
{
    public Guid AccountId { get; init; }
    public string Slug { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public DateTimeOffset MemberSince { get; init; }
    public int Points { get; init; }
    public IReadOnlyList<PlayerProfileDto> Bots { get; init; } = [];

    /// <summary>Each achievement once, with the bots that earned it, newest first.</summary>
    public IReadOnlyList<EarnedAchievementDto> Achievements { get; init; } = [];

    /// <summary>Their bots' most recent matches, newest first.</summary>
    public IReadOnlyList<PersonMatchDto> Brawls { get; init; } = [];
}

/// <summary>One of a person's bots in one finished match.</summary>
public sealed record PersonMatchDto
{
    public Guid MatchId { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public MatchCategory Category { get; init; }
    public Guid BotId { get; init; }
    public string Bot { get; init; } = "";
    public string Result { get; init; } = "";
    public int Place { get; init; }
    public int PlayerCount { get; init; }
    public int Ticks { get; init; }
    public MatchEndReason Reason { get; init; }
    public int Score { get; init; }

    /// <summary>The other registered bots in the match (extra copies of a house bot aren't listed).</summary>
    public IReadOnlyList<string> Opponents { get; init; } = [];
}

/// <summary>
/// People, as the site shows them: the Trophy Room (everyone ranked by their bots' achievements) and
/// public profiles. Only Discord-verified people appear, as their names already do on the leaderboards.
/// </summary>
public static class PeopleEndpoints
{
    private static readonly Dictionary<AchievementTier, int> TierPoints = new()
    {
        [AchievementTier.Bronze] = 1,
        [AchievementTier.Silver] = 2,
        [AchievementTier.Gold] = 4,
        [AchievementTier.Platinum] = 8,
    };

    public static void MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/v1/people").WithTags("Players & info");

        people.MapGet("/", TrophyRoom)
            .Produces<IReadOnlyList<TrophyRoomEntryDto>>()
            .WithSummary("The Trophy Room: Discord-verified people ranked by what their bots have earned (trophy points: 1 Bronze, 2 Silver, 4 Gold, 8 Platinum).");

        people.MapGet("/{slug}", Profile)
            .Produces<PersonProfileDto>()
            .ProducesErrors()
            .WithSummary("A person's public profile, by the name in their profile URL (e.g. jordan): their bots, their bots' achievements and their recent brawls.");
    }

    private static async Task<IReadOnlyList<TrophyRoomEntryDto>> TrophyRoom(NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct, int limit = 100)
    {
        var owned = await db.Players.AsNoTracking().Include(p => p.Owner)
            .Where(p => p.OwnerAccountId != null && !p.IsHouseBot)
            .ToListAsync(ct);
        var people = owned.Where(p => Accounts.IsVerified(p.Owner, discord.Value)).GroupBy(p => p.Owner!.Id).ToList();
        var slugs = await ProfileSlugs.LoadAsync(db, discord.Value, ct);
        var botIds = people.SelectMany(g => g.Select(p => p.Id)).ToList();
        var earned = (await db.BotAchievements.AsNoTracking().Where(a => botIds.Contains(a.PlayerId)).ToListAsync(ct))
            .Where(a => AchievementCatalog.ById.ContainsKey(a.AchievementId))
            .ToLookup(a => a.PlayerId);

        var rows = people.Select(g =>
            {
                var owner = g.First().Owner!;
                var mine = g.SelectMany(p => earned[p.Id]).ToList();
                var distinct = mine.GroupBy(a => a.AchievementId).Select(x => AchievementCatalog.ById[x.Key]).ToList();
                int Count(AchievementTier t) => distinct.Count(a => a.Tier == t);
                return new TrophyRoomEntryDto
                {
                    AccountId = owner.Id,
                    Slug = slugs.Of(owner.Id) ?? "",
                    DisplayName = owner.DisplayName,
                    AvatarUrl = Accounts.AvatarUrl(owner),
                    Bots = g.Count(),
                    Achievements = distinct.Count,
                    Bronze = Count(AchievementTier.Bronze),
                    Silver = Count(AchievementTier.Silver),
                    Gold = Count(AchievementTier.Gold),
                    Platinum = Count(AchievementTier.Platinum),
                    Points = distinct.Sum(a => TierPoints[a.Tier]),
                    LatestAt = mine.Count > 0 ? Utc(mine.Max(a => a.EarnedAt)) : null,
                };
            })
            .OrderByDescending(r => r.Points).ThenByDescending(r => r.Achievements).ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();
        return rows.Select((r, i) => r with { Rank = i + 1 }).ToList();
    }

    private static async Task<IResult> Profile(string slug, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct, int brawls = 30)
    {
        var slugs = await ProfileSlugs.LoadAsync(db, discord.Value, ct);
        if (slugs.Find(slug) is not { } accountId)
        {
            return ApiErrors.NotFound("Player");
        }

        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);

        var bots = await PlayerEndpoints.ProfilesAsync(db, discord.Value, db.Players.Where(p => p.OwnerAccountId == accountId), ct);
        var botIds = bots.Select(b => b.PlayerId).ToList();
        var achievements = await AchievementEndpoints.EarnedAsync(db, db.BotAchievements.Where(a => botIds.Contains(a.PlayerId)), listBots: true, ct);

        var rows = await db.MatchPlayers.AsNoTracking()
            .Where(r => botIds.Contains(r.PlayerId))
            .OrderByDescending(r => r.CompletedAt)
            .Take(Math.Clamp(brawls, 0, 200))
            .ToListAsync(ct);
        var matchIds = rows.Select(r => r.MatchId).Distinct().ToList();
        var others = await db.MatchPlayers.AsNoTracking()
            .Where(r => matchIds.Contains(r.MatchId))
            .Join(db.Players, r => r.PlayerId, p => p.Id, (r, p) => new { r.MatchId, r.PlayerId, p.Name })
            .ToListAsync(ct);
        var names = bots.ToDictionary(b => b.PlayerId, b => b.Name);

        return Results.Ok(new PersonProfileDto
        {
            AccountId = account.Id,
            Slug = slugs.Of(account.Id)!,
            DisplayName = account.DisplayName,
            AvatarUrl = Accounts.AvatarUrl(account),
            MemberSince = Utc(account.CreatedAt),
            Points = achievements.Sum(a => TierPoints[a.Tier]),
            Bots = bots,
            Achievements = achievements,
            Brawls = rows.Select(r => new PersonMatchDto
            {
                MatchId = r.MatchId,
                CompletedAt = Utc(r.CompletedAt),
                Category = r.Category,
                BotId = r.PlayerId,
                Bot = names.GetValueOrDefault(r.PlayerId, ""),
                Result = r.Result.ToString(),
                Place = r.Place,
                PlayerCount = r.PlayerCount,
                Ticks = r.Ticks,
                Reason = r.Reason,
                Score = r.Score,
                Opponents = others.Where(o => o.MatchId == r.MatchId && o.PlayerId != r.PlayerId).Select(o => o.Name).Order(StringComparer.Ordinal).ToList(),
            }).ToList(),
        });
    }

    private static DateTimeOffset Utc(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
