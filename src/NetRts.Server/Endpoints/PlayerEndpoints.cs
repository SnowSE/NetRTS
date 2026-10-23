using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Endpoints;

public static partial class PlayerEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    private static partial Regex ValidName();

    public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Players & info");

        api.MapPost("/players", Register)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Registration)
            .Produces<RegisterPlayerResponse>(201)
            .ProducesErrors()
            .WithSummary("Register a bot and receive its API key (shown once).");

        api.MapGet("/players/me", async (ClaimsPrincipal user, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct) =>
                await Profile(db, discord.Value, user.PlayerId(), ct))
            .RequireAuthorization()
            .Produces<PlayerProfileDto>()
            .ProducesErrors()
            .WithSummary("Your profile, ratings and records in each category.");

        api.MapGet("/players/{playerId:guid}", async (Guid playerId, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct) =>
                await Profile(db, discord.Value, playerId, ct))
            .Produces<PlayerProfileDto>()
            .ProducesErrors()
            .WithSummary("A player's public profile.");

        api.MapGet("/leaderboard", Leaderboard)
            .WithSummary("One category's ladder: Practice (default; by Elo rating, only player-vs-player 1v1s move it), Preseason, Season or Championship (by league points, then skill). Add season=<label> for a past season.");

        api.MapGet("/rules", () => GameRules.ToDto())
            .WithSummary("Every unit, building and upgrade stat, plus the core rules.");

        api.MapGet("/bots", () => HouseBots.All)
            .WithSummary("House bots you can play against or watch.");
    }

    private static Task<IResult> Register(RegisterPlayerRequest request, NetRtsDb db, CancellationToken ct) =>
        CreatePlayerAsync(db, request.Name, ownerAccountId: null, ct);

    /// <summary>Registers a bot (optionally owned by a signed-in account) and returns its key, shown once.</summary>
    internal static async Task<IResult> CreatePlayerAsync(NetRtsDb db, string? requestedName, Guid? ownerAccountId, CancellationToken ct)
    {
        var name = requestedName?.Trim() ?? "";
        if (!ValidName().IsMatch(name))
        {
            return ApiErrors.Error(400, "INVALID_NAME", "Names are 3-32 characters: letters, digits, '_' or '-'.");
        }

        if (name.StartsWith("house-", StringComparison.OrdinalIgnoreCase))
        {
            return ApiErrors.Error(400, "INVALID_NAME", "Names starting with 'house-' are reserved for house bots.");
        }

        var lower = name.ToLowerInvariant();
        if (await db.Players.AnyAsync(p => p.Name.ToLower() == lower, ct))
        {
            return ApiErrors.Error(409, "NAME_TAKEN", $"The name '{name}' is already registered.");
        }

        var apiKey = ApiKeys.Generate();
        var player = new PlayerRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            ApiKeyHash = ApiKeys.Hash(apiKey),
            CreatedAt = DateTime.UtcNow,
            OwnerAccountId = ownerAccountId,
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/players/{player.Id}", new RegisterPlayerResponse { PlayerId = player.Id, Name = name, ApiKey = apiKey });
    }

    private static async Task<IResult> Profile(NetRtsDb db, DiscordOptions discord, Guid playerId, CancellationToken ct)
    {
        var profiles = await ProfilesAsync(db, discord, db.Players.Where(p => p.Id == playerId), ct);
        return profiles.Count == 0 ? ApiErrors.NotFound("Player") : Results.Ok(profiles[0]);
    }

    /// <summary>Profiles (with every category's record and the owner's badge) for the players a query selects.</summary>
    internal static async Task<IReadOnlyList<PlayerProfileDto>> ProfilesAsync(NetRtsDb db, DiscordOptions discord, IQueryable<PlayerRecord> query, CancellationToken ct)
    {
        var players = await query.AsNoTracking().Include(p => p.Owner).OrderBy(p => p.Name).ToListAsync(ct);
        var ids = players.Select(p => p.Id).ToList();
        var stats = (await db.PlayerStats.AsNoTracking().Where(s => ids.Contains(s.PlayerId) && s.Season == Seasons.Current).ToListAsync(ct))
            .ToLookup(s => s.PlayerId);
        var slugs = await ProfileSlugs.LoadAsync(db, discord, ct);

        return players.Select(p =>
        {
            var records = Enum.GetValues<MatchCategory>().Select(category =>
            {
                var s = stats[p.Id].FirstOrDefault(s => s.Category == category) ?? new PlayerStatsRecord { Category = category };
                var skill = OpenSkill.Of(s);
                return new CategoryRecordDto
                {
                    Category = category, Rating = s.Rating, Wins = s.Wins, Losses = s.Losses, Draws = s.Draws, Points = s.Points,
                    Skill = OpenSkill.Display(skill.Mu, skill.Sigma), Mu = skill.Mu, Sigma = skill.Sigma,
                };
            }).ToList();
            var practice = records[0];
            var verified = Accounts.IsVerified(p.Owner, discord);
            return new PlayerProfileDto
            {
                PlayerId = p.Id,
                Name = p.Name,
                IsHouseBot = p.IsHouseBot,
                Rating = practice.Rating,
                Wins = practice.Wins,
                Losses = practice.Losses,
                Draws = practice.Draws,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc)),
                Verified = verified,
                OwnerName = verified ? p.Owner!.DisplayName : null,
                OwnerSlug = verified ? slugs.Of(p.Owner!.Id) : null,
                Records = records,
                Skill = practice.Skill,
            };
        }).ToList();
    }

    internal static async Task<IReadOnlyList<LeaderboardEntryDto>> Leaderboard(
        NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct, MatchCategory category = MatchCategory.Practice, int limit = 50, string? season = null)
    {
        season ??= Seasons.Current;
        // Each category is its own ladder. House bots always appear on the practice ladder as yardsticks.
        // Practice is ranked by rating; league categories by points, then skill (its conservative estimate). ?season= shows a past season.
        var league = category != MatchCategory.Practice;
        var rows = await db.Players.AsNoTracking()
            .Select(p => new { Player = p, p.Owner, Stats = db.PlayerStats.FirstOrDefault(s => s.PlayerId == p.Id && s.Category == category && s.Season == season) })
            .Where(x => (x.Stats != null && x.Stats.Wins + x.Stats.Losses + x.Stats.Draws > 0)
                        || (category == MatchCategory.Practice && season == Seasons.Current && x.Player.IsHouseBot))
            .OrderByDescending(x => league && x.Stats != null ? x.Stats.Points : 0)
            .ThenByDescending(x => league && x.Stats != null ? x.Stats.Mu - 3 * x.Stats.Sigma : 0)
            .ThenByDescending(x => x.Stats == null ? Elo.InitialRating : x.Stats.Rating)
            .ThenByDescending(x => x.Stats == null ? 0 : x.Stats.Wins)
            .ThenBy(x => x.Player.Name)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        var slugs = await ProfileSlugs.LoadAsync(db, discord.Value, ct);
        return rows.Select((x, i) =>
        {
            var verified = Accounts.IsVerified(x.Owner, discord.Value);
            var skill = x.Stats is null ? OpenSkill.Initial : OpenSkill.Of(x.Stats);
            return new LeaderboardEntryDto
            {
                Rank = i + 1,
                PlayerId = x.Player.Id,
                Name = x.Player.Name,
                IsHouseBot = x.Player.IsHouseBot,
                Rating = x.Stats?.Rating ?? Elo.InitialRating,
                Wins = x.Stats?.Wins ?? 0,
                Losses = x.Stats?.Losses ?? 0,
                Draws = x.Stats?.Draws ?? 0,
                Points = x.Stats?.Points ?? 0,
                Skill = OpenSkill.Display(skill.Mu, skill.Sigma),
                Mu = skill.Mu,
                Sigma = skill.Sigma,
                Verified = verified,
                OwnerName = verified ? x.Owner!.DisplayName : null,
                OwnerSlug = verified ? slugs.Of(x.Owner!.Id) : null,
            };
        }).ToList();
    }
}
