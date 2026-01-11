using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Commands.CreateLobby;
using NetRts.Application.Commands.CreateMatch;
using NetRts.Application.Commands.JoinLobby;
using NetRts.Application.Commands.LeaveLobby;
using NetRts.Application.Commands.UpdateLobbySettings;
using NetRts.Application.Queries.GetLobbies;
using NetRts.Application.Queries.GetLobby;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

public static class LobbyEndpoints
{
    public static void MapLobbyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/lobbies")
            .WithTags("Lobby");

        group.MapPost("/", CreateLobby)
            .AllowAnonymous()
            .Produces<LobbyResponse>(StatusCodes.Status201Created);

        group.MapGet("/", GetLobbies)
            .AllowAnonymous()
            .Produces<LobbyListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{lobbyId:guid}", GetLobby)
            .AllowAnonymous()
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{lobbyId:guid}/join", JoinLobby)
            .AllowAnonymous()
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{lobbyId:guid}/leave", LeaveLobby)
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{lobbyId:guid}/settings", UpdateSettings)
            .AllowAnonymous()
            .Produces<LobbyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{lobbyId:guid}/start", StartMatch)
            .AllowAnonymous()
            .Produces<StartMatchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> CreateLobby(
        [FromBody] CreateLobbyRequest request,
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var command = new CreateLobbyCommand(playerId, request);
        var response = await mediator.Send(command, cancellationToken);

        return Results.Created($"/api/v1/lobbies/{response.Id}", response);
    }

    private static async Task<IResult> GetLobbies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        IMediator mediator = default!,
        CancellationToken cancellationToken = default!)
    {
        var query = new GetLobbiesQuery(page, pageSize);
        var response = await mediator.Send(query, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetLobby(
        Guid lobbyId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetLobbyQuery(lobbyId);
        var response = await mediator.Send(query, cancellationToken);
        
        return Results.Ok(response);
    }

    private static async Task<IResult> JoinLobby(
        Guid lobbyId,
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var command = new JoinLobbyCommand(lobbyId, playerId);
        var response = await mediator.Send(command, cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> LeaveLobby(
        Guid lobbyId,
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var command = new LeaveLobbyCommand(lobbyId, playerId);
        await mediator.Send(command, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSettings(
        Guid lobbyId,
        [FromBody] UpdateLobbySettingsRequest request,
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var command = new UpdateLobbySettingsCommand(lobbyId, playerId, request);
        var response = await mediator.Send(command, cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> StartMatch(
        Guid lobbyId,
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var command = new CreateMatchCommand(lobbyId, playerId);
        var response = await mediator.Send(command, cancellationToken);

        return Results.Ok(response);
    }

    private static Guid GetPlayerId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Test-Player-Id", out var testPlayerId)
            && Guid.TryParse(testPlayerId, out var playerId))
        {
            return playerId;
        }

        var sub = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? context.User?.FindFirst("sub")?.Value;
            
        if (Guid.TryParse(sub, out var guid)) return guid;
        
        return Guid.Empty;
    }
}