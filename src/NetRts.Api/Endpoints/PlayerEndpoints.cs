using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Commands.RegisterPlayer;
using NetRts.Application.Queries.GetPlayerProfile;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/players")
            .WithTags("Player");

        group.MapPost("/", RegisterPlayer)
            .AllowAnonymous()
            .Produces<PlayerResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/me", GetCurrentProfile)
            .AllowAnonymous() // TODO: Enable auth
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RegisterPlayer(
        [FromBody] RegisterPlayerRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new RegisterPlayerCommand(request);
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/players/{response.PlayerId}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetCurrentProfile(
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var playerId = GetPlayerId(httpContext);
        if (playerId == Guid.Empty) return Results.Unauthorized();

        var query = new GetPlayerProfileQuery(playerId);
        var response = await mediator.Send(query, cancellationToken);
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