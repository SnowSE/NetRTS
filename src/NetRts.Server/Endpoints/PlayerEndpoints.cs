using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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

        api.MapGet("/players/me", async (ClaimsPrincipal user, NetRtsDb db, CancellationToken ct) =>
                await Profile(db, user.PlayerId(), ct))
            .RequireAuthorization()
            .Produces<PlayerProfileDto>()
            .ProducesErrors()
            .WithSummary("Your profile, rating and record.");

        api.MapGet("/players/{playerId:guid}", async (Guid playerId, NetRtsDb db, CancellationToken ct) =>
                await Profile(db, playerId, ct))
            .Produces<PlayerProfileDto>()
            .ProducesErrors()
            .WithSummary("A player's public profile.");

        api.MapGet("/leaderboard", Leaderboard)
            .WithSummary("Players ranked by Elo rating (only player-vs-player 1v1 matches are rated).");

        api.MapGet("/rules", () => GameRules.ToDto())
            .WithSummary("Every unit, building and upgrade stat, plus the core rules.");

        api.MapGet("/bots", () => HouseBots.All)
            .WithSummary("House bots you can play against or watch.");
    }

    private static async Task<IResult> Register(RegisterPlayerRequest request, NetRtsDb db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
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
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/players/{player.Id}", new RegisterPlayerResponse { PlayerId = player.Id, Name = name, ApiKey = apiKey });
    }

    private static async Task<IResult> Profile(NetRtsDb db, Guid playerId, CancellationToken ct)
    {
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct);
        return p is null
            ? ApiErrors.NotFound("Player")
            : Results.Ok(new PlayerProfileDto
            {
                PlayerId = p.Id,
                Name = p.Name,
                IsHouseBot = p.IsHouseBot,
                Rating = p.Rating,
                Wins = p.Wins,
                Losses = p.Losses,
                Draws = p.Draws,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc)),
            });
    }

    private static async Task<IReadOnlyList<LeaderboardEntryDto>> Leaderboard(NetRtsDb db, CancellationToken ct, int limit = 50)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => p.IsHouseBot || p.Wins + p.Losses + p.Draws > 0)
            .OrderByDescending(p => p.Rating).ThenByDescending(p => p.Wins).ThenBy(p => p.Name)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return players.Select((p, i) => new LeaderboardEntryDto
        {
            Rank = i + 1,
            PlayerId = p.Id,
            Name = p.Name,
            IsHouseBot = p.IsHouseBot,
            Rating = p.Rating,
            Wins = p.Wins,
            Losses = p.Losses,
            Draws = p.Draws,
        }).ToList();
    }
}
