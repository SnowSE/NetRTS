using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Queries.GetGameState;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

/// <summary>
/// API endpoints for game state and match operations.
/// </summary>
public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/matches")
            .WithTags("Game")
            .RequireAuthorization();

        group.MapGet("/{matchId:guid}/state", GetGameState)
            .WithName("GetGameState")
            .WithSummary("Retrieve current game state for a match")
            .WithDescription("Returns the current game state including units, buildings, resources, and visible map tiles with fog of war applied")
            .Produces<GameStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetGameState(
        [FromRoute] Guid matchId,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract player ID from JWT token claims
            var playerIdClaim = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(playerIdClaim) || !Guid.TryParse(playerIdClaim, out var playerId))
            {
                return Results.Unauthorized();
            }

            // Execute query via MediatR
            var query = new GetGameStateQuery(matchId, playerId);
            var response = await mediator.Send(query, cancellationToken);

            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error retrieving game state",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
