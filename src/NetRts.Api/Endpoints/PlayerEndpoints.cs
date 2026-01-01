using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;

namespace NetRts.Api.Endpoints;

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/players")
            .WithTags("Players");

        group.MapPost("", RegisterPlayer)
            .WithName("RegisterPlayer")
            .WithSummary("Register a new player")
            .AllowAnonymous()
            .Produces<PlayerResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginPlayer)
            .WithName("LoginPlayer")
            .WithSummary("Login with username to get a new token")
            .AllowAnonymous()
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/me", GetCurrentPlayer)
            .WithName("GetCurrentPlayer")
            .WithSummary("Get current player profile")
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("", GetAllPlayers)
            .WithName("GetAllPlayers")
            .WithSummary("Get all players (leaderboard)")
            .AllowAnonymous()
            .Produces<List<PlayerResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{playerId:guid}", GetPlayer)
            .WithName("GetPlayer")
            .WithSummary("Get player by ID")
            .AllowAnonymous()
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RegisterPlayer(
        RegisterPlayerRequest request,
        IPlayerRepository playerRepository,
        IJwtTokenService jwtTokenService,
        CancellationToken cancellationToken)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.BadRequest(new { error = "Username is required" });
        }

        if (request.Username.Length < 3 || request.Username.Length > 20)
        {
            return Results.BadRequest(new { error = "Username must be 3-20 characters" });
        }

        if (!request.Username.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return Results.BadRequest(new { error = "Username must contain only letters, digits, and underscores" });
        }

        // Check if username already exists
        if (await playerRepository.UsernameExistsAsync(request.Username, cancellationToken))
        {
            return Results.Conflict(new { error = "Username already taken" });
        }

        // Create player
        var player = new Player(request.Username, request.Email, request.IsBot);
        await playerRepository.AddAsync(player, cancellationToken);

        // Generate JWT token
        var token = jwtTokenService.GenerateToken(player);

        var response = MapToResponse(player, token);
        return Results.Created($"/api/v1/players/{player.Id}", response);
    }

    private static async Task<IResult> LoginPlayer(
        LoginPlayerRequest request,
        IPlayerRepository playerRepository,
        IJwtTokenService jwtTokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.BadRequest(new { error = "Username is required" });
        }

        var player = await playerRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (player == null)
        {
            return Results.NotFound(new { error = "Player not found" });
        }

        // Record activity
        player.RecordActivity();
        await playerRepository.UpdateAsync(player, cancellationToken);

        // Generate new JWT token
        var token = jwtTokenService.GenerateToken(player);

        var response = MapToResponse(player, token);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetCurrentPlayer(
        ClaimsPrincipal user,
        HttpContext httpContext,
        IPlayerRepository playerRepository,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var player = await playerRepository.GetByIdAsync(playerId, cancellationToken);
        if (player == null)
        {
            return Results.NotFound(new { error = "Player not found" });
        }

        var response = MapToResponse(player);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetAllPlayers(
        IPlayerRepository playerRepository,
        CancellationToken cancellationToken)
    {
        var players = await playerRepository.GetAllAsync(cancellationToken);
        var response = players.Select(p => MapToResponse(p)).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetPlayer(
        Guid playerId,
        IPlayerRepository playerRepository,
        CancellationToken cancellationToken)
    {
        var player = await playerRepository.GetByIdAsync(playerId, cancellationToken);
        if (player == null)
        {
            return Results.NotFound(new { error = "Player not found" });
        }

        var response = MapToResponse(player);
        return Results.Ok(response);
    }

    private static PlayerResponse MapToResponse(Player player, string? token = null)
    {
        return new PlayerResponse
        {
            PlayerId = player.Id,
            Username = player.Username,
            Email = player.Email,
            IsBot = player.IsBot,
            Token = token,
            CreatedAt = player.CreatedAt,
            TotalMatches = player.TotalMatches,
            TotalWins = player.TotalWins,
            CurrentElo = player.CurrentElo,
            WinRate = player.GetWinRate()
        };
    }

    private static Guid ExtractPlayerId(ClaimsPrincipal user, HttpContext httpContext)
    {
        // Try JWT claim first
        var playerIdClaim = user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!string.IsNullOrEmpty(playerIdClaim) && Guid.TryParse(playerIdClaim, out var playerId))
        {
            return playerId;
        }

        // Fallback to test header for development
        if (httpContext.Request.Headers.TryGetValue("X-Test-Player-Id", out var testPlayerId)
            && Guid.TryParse(testPlayerId, out playerId))
        {
            return playerId;
        }

        return Guid.Empty;
    }
}
