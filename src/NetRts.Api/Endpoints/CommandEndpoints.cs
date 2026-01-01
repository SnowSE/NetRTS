using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

/// <summary>
/// API endpoints for command queueing operations.
/// </summary>
public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/matches")
            .WithTags("Commands");

        group.MapPost("/{matchId:guid}/commands", QueueCommands)
            .AllowAnonymous() // TODO: Remove after fixing JWT
            .WithName("QueueCommands")
            .WithSummary("Queue commands for execution in the next game tick")
            .WithDescription("Queue one or more commands (move, attack, gather, build, produce, research) that will be executed on the next game tick")
            .Produces<QueueCommandsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> QueueCommands(
        Guid matchId,
        [FromBody] QueueCommandsRequest request,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ICommandQueueService commandQueueService,
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

            // Validate request
            if (request.Commands == null || request.Commands.Length == 0)
            {
                return Results.BadRequest(new { error = "No commands provided" });
            }

            // Queue commands via service
            var response = await commandQueueService.QueueCommandsAsync(matchId, playerId, request, cancellationToken);

            // Check for system-level errors (match not found, queue full, etc.)
            var systemErrors = response.Errors.Where(e => e.CommandIndex == -1).ToList();
            if (systemErrors.Any())
            {
                if (systemErrors.Any(e => e.ErrorCode == "UNAUTHORIZED" || e.ErrorCode == "MATCH_NOT_ACTIVE"))
                {
                    return Results.Forbid();
                }
                if (systemErrors.Any(e => e.ErrorCode == "QUEUE_FULL"))
                {
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }
                return Results.BadRequest(response);
            }

            // If all commands failed with authorization errors, return 403
            if (response.QueuedCount == 0 &&
                response.Errors.Any(e => e.ErrorCode == "UNAUTHORIZED" || e.ErrorCode == "UNAUTHORIZED_UNIT"))
            {
                return Results.Forbid();
            }

            // If all commands failed with validation errors, return 400
            if (response.QueuedCount == 0 && response.FailedCount > 0)
            {
                return Results.BadRequest(response);
            }

            // Return success with any partial failures
            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error queueing commands",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
