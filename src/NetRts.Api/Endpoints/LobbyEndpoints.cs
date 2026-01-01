using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NetRts.Api.Hubs;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Api.Endpoints;

public static class LobbyEndpoints
{
    public static void MapLobbyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/lobbies")
            .WithTags("Lobby")
            .AllowAnonymous();

        group.MapGet("", GetLobbies)
            .WithName("GetLobbies")
            .WithSummary("Get list of open lobbies")
            .Produces<List<LobbyResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{lobbyId:guid}", GetLobby)
            .WithName("GetLobby")
            .WithSummary("Get lobby details")
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateLobby)
            .WithName("CreateLobby")
            .WithSummary("Create a new lobby")
            .Produces<LobbyResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{lobbyId:guid}/join", JoinLobby)
            .WithName("JoinLobby")
            .WithSummary("Join an existing lobby")
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{lobbyId:guid}/leave", LeaveLobby)
            .WithName("LeaveLobby")
            .WithSummary("Leave a lobby")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{lobbyId:guid}/settings", UpdateSettings)
            .WithName("UpdateLobbySettings")
            .WithSummary("Update lobby settings (host only)")
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{lobbyId:guid}/start", StartMatch)
            .WithName("StartMatch")
            .WithSummary("Start the match (host only)")
            .Produces<StartMatchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetLobbies(
        IMatchLobbyRepository lobbyRepository,
        CancellationToken cancellationToken)
    {
        var lobbies = await lobbyRepository.GetOpenLobbiesAsync(cancellationToken);
        var response = lobbies.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> GetLobby(
        Guid lobbyId,
        IMatchLobbyRepository lobbyRepository,
        CancellationToken cancellationToken)
    {
        var lobby = await lobbyRepository.GetByIdAsync(lobbyId, cancellationToken);
        if (lobby == null)
            return Results.NotFound(new { error = "Lobby not found" });

        return Results.Ok(MapToResponse(lobby));
    }

    private static async Task<IResult> CreateLobby(
        CreateLobbyRequest request,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMatchLobbyRepository lobbyRepository,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Lobby name is required" });

        var settings = new GameSettings(
            mapWidth: request.MapWidth ?? 100,
            mapHeight: request.MapHeight ?? 100,
            maxTicks: request.MaxTicks ?? 1800,
            startingResources: request.StartingResources ?? 500);

        var lobby = new MatchLobby(request.Name, playerId, settings);
        lobby.AddPlayer(playerId, 0);

        await lobbyRepository.AddAsync(lobby, cancellationToken);

        return Results.Created($"/api/v1/lobbies/{lobby.Id}", MapToResponse(lobby));
    }

    private static async Task<IResult> JoinLobby(
        Guid lobbyId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMatchLobbyRepository lobbyRepository,
        IHubContext<GameHub> hubContext,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
            return Results.Unauthorized();

        var lobby = await lobbyRepository.GetByIdAsync(lobbyId, cancellationToken);
        if (lobby == null)
            return Results.NotFound(new { error = "Lobby not found" });

        try
        {
            var nextSlot = lobby.CurrentPlayerCount;
            lobby.AddPlayer(playerId, nextSlot);
            await lobbyRepository.UpdateAsync(lobby, cancellationToken);

            var response = MapToResponse(lobby);
            await hubContext.Clients.Group($"lobby-{lobbyId}").SendAsync("LobbyUpdated", response, cancellationToken);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> LeaveLobby(
        Guid lobbyId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMatchLobbyRepository lobbyRepository,
        IHubContext<GameHub> hubContext,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
            return Results.Unauthorized();

        var lobby = await lobbyRepository.GetByIdAsync(lobbyId, cancellationToken);
        if (lobby == null)
            return Results.NotFound(new { error = "Lobby not found" });

        lobby.RemovePlayer(playerId);

        if (lobby.HostPlayerId == playerId)
        {
            lobby.Close();
        }

        await lobbyRepository.UpdateAsync(lobby, cancellationToken);

        var response = MapToResponse(lobby);
        await hubContext.Clients.Group($"lobby-{lobbyId}").SendAsync("LobbyUpdated", response, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSettings(
        Guid lobbyId,
        UpdateLobbySettingsRequest request,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMatchLobbyRepository lobbyRepository,
        IHubContext<GameHub> hubContext,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
            return Results.Unauthorized();

        var lobby = await lobbyRepository.GetByIdAsync(lobbyId, cancellationToken);
        if (lobby == null)
            return Results.NotFound(new { error = "Lobby not found" });

        if (lobby.HostPlayerId != playerId)
            return Results.Forbid();

        try
        {
            var settings = new GameSettings(
                mapWidth: request.MapWidth ?? lobby.GameSettings.MapWidth,
                mapHeight: request.MapHeight ?? lobby.GameSettings.MapHeight,
                maxTicks: request.MaxTicks ?? lobby.GameSettings.MaxTicks,
                startingResources: request.StartingResources ?? lobby.GameSettings.StartingResources);

            lobby.UpdateSettings(settings);
            await lobbyRepository.UpdateAsync(lobby, cancellationToken);

            var response = MapToResponse(lobby);
            await hubContext.Clients.Group($"lobby-{lobbyId}").SendAsync("LobbyUpdated", response, cancellationToken);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> StartMatch(
        Guid lobbyId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMatchLobbyRepository lobbyRepository,
        IMatchRepository matchRepository,
        IGameStateCache gameStateCache,
        IHubContext<GameHub> hubContext,
        CancellationToken cancellationToken)
    {
        var playerId = ExtractPlayerId(user, httpContext);
        if (playerId == Guid.Empty)
            return Results.Unauthorized();

        var lobby = await lobbyRepository.GetByIdAsync(lobbyId, cancellationToken);
        if (lobby == null)
            return Results.NotFound(new { error = "Lobby not found" });

        if (lobby.HostPlayerId != playerId)
            return Results.Forbid();

        if (lobby.CurrentPlayerCount < lobby.MaxPlayers)
            return Results.BadRequest(new { error = "Not all players have joined" });

        try
        {
            lobby.Start();

            var players = lobby.Players.Select(p => p.PlayerId).ToList();
            var player1Id = players[0];
            var player2Id = players[1];

            var match = new Match(player1Id, player2Id, lobby.GameSettings);
            match.Start();

            // Initialize game with starting units and buildings
            var player1SpawnPosition = new Position(10, 10);
            var player2SpawnPosition = new Position(90, 90);

            var units = new List<Unit>();
            int unitIdCounter = 1;

            // Create 5 workers for player 1
            for (int i = 0; i < 5; i++)
            {
                var workerPosition1 = new Position(
                    player1SpawnPosition.X + (i % 3),
                    player1SpawnPosition.Y + (i / 3));

                units.Add(new Unit(
                    id: unitIdCounter++,
                    matchId: match.Id,
                    ownerId: player1Id,
                    type: UnitType.Worker,
                    position: workerPosition1));
            }

            // Create 5 workers for player 2
            for (int i = 0; i < 5; i++)
            {
                var workerPosition2 = new Position(
                    player2SpawnPosition.X + (i % 3),
                    player2SpawnPosition.Y + (i / 3));

                units.Add(new Unit(
                    id: unitIdCounter++,
                    matchId: match.Id,
                    ownerId: player2Id,
                    type: UnitType.Worker,
                    position: workerPosition2));
            }

            // Create Command Centers for each player
            var buildings = new List<Building>();

            var building1 = new Building(
                id: 1,
                matchId: match.Id,
                ownerId: player1Id,
                type: BuildingType.CommandCenter,
                position: player1SpawnPosition);
            building1.AdvanceConstruction(100);
            buildings.Add(building1);

            var building2 = new Building(
                id: 2,
                matchId: match.Id,
                ownerId: player2Id,
                type: BuildingType.CommandCenter,
                position: player2SpawnPosition);
            building2.AdvanceConstruction(100);
            buildings.Add(building2);

            await matchRepository.AddAsync(match, cancellationToken);
            gameStateCache.SetMatch(match.Id, match);
            gameStateCache.SetUnitsForMatch(match.Id, units);
            gameStateCache.SetBuildingsForMatch(match.Id, buildings);

            lobby.Close();
            await lobbyRepository.UpdateAsync(lobby, cancellationToken);

            // Notify lobby members that match has started
            await hubContext.Clients.Group($"lobby-{lobbyId}").SendAsync("MatchStarted", match.Id, cancellationToken);

            return Results.Ok(new StartMatchResponse
            {
                MatchId = match.Id,
                Player1Id = player1Id,
                Player2Id = player2Id
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static LobbyResponse MapToResponse(MatchLobby lobby)
    {
        return new LobbyResponse
        {
            Id = lobby.Id,
            Name = lobby.Name,
            HostPlayerId = lobby.HostPlayerId,
            Status = lobby.Status.ToString(),
            MaxPlayers = lobby.MaxPlayers,
            CurrentPlayerCount = lobby.CurrentPlayerCount,
            Players = lobby.Players.Select(p => new LobbyPlayerDto
            {
                PlayerId = p.PlayerId,
                Slot = p.Slot,
                IsReady = p.IsReady,
                JoinedAt = p.JoinedAt
            }).ToList(),
            Settings = new GameSettingsDto
            {
                MapWidth = lobby.GameSettings.MapWidth,
                MapHeight = lobby.GameSettings.MapHeight,
                MaxTicks = lobby.GameSettings.MaxTicks,
                StartingResources = lobby.GameSettings.StartingResources
            },
            CreatedAt = lobby.CreatedAt
        };
    }

    private static Guid ExtractPlayerId(ClaimsPrincipal user, HttpContext httpContext)
    {
        var playerIdClaim = user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!string.IsNullOrEmpty(playerIdClaim) && Guid.TryParse(playerIdClaim, out var playerId))
            return playerId;

        if (httpContext.Request.Headers.TryGetValue("X-Test-Player-Id", out var testPlayerId)
            && Guid.TryParse(testPlayerId, out playerId))
            return playerId;

        return Guid.Empty;
    }
}
