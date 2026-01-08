using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Services;
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
            .WithTags("Game");
            // TODO: Re-enable RequireAuthorization() after fixing JWT validation

        group.MapGet("/{matchId:guid}/state", GetGameState)
            .AllowAnonymous() // TODO: Remove after fixing JWT
            .WithName("GetGameState")
            .WithSummary("Retrieve current game state for a match")
            .WithDescription("Returns the current game state including units, buildings, resources, and visible map tiles with fog of war applied")
            .Produces<GameStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/result", GetMatchResult)
            .AllowAnonymous() // TODO: Remove after fixing JWT
            .WithName("GetMatchResult")
            .WithSummary("Retrieve final match results")
            .WithDescription("Returns the final results of a completed match including winner, scores, and duration")
            .Produces<MatchResultResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetGameState(
        Guid matchId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IGameStateService gameStateService,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract player ID from JWT token claims OR test header
            Guid playerId;

            var playerIdClaim = user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!string.IsNullOrEmpty(playerIdClaim) && Guid.TryParse(playerIdClaim, out playerId))
            {
                // Successfully got player ID from JWT
            }
            else if (httpContext.Request.Headers.TryGetValue("X-Test-Player-Id", out var testPlayerId)
                     && Guid.TryParse(testPlayerId, out playerId))
            {
                // Got player ID from test header (only in Testing environment)
            }
            else
            {
                return Results.Json(
                    new { error = "Authentication required. Please provide a valid authorization token." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // Get game state via service
            var response = await gameStateService.GetGameStateAsync(matchId, playerId, cancellationToken);

            if (response == null)
            {
                return Results.NotFound(new { error = "Match not found or player not authorized" });
            }

            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error retrieving game state",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetMatchResult(
        Guid matchId,
        MediatR.IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new Application.Queries.GetMatchResult.GetMatchResultQuery(matchId);
            var result = await mediator.Send(query, cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not completed"))
        {
            return Results.Conflict(new
            {
                title = "Match not completed",
                detail = ex.Message,
                matchId = matchId
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new
            {
                title = "Match not found",
                detail = ex.Message,
                matchId = matchId
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error retrieving match result",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
