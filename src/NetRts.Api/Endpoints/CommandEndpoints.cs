using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Commands.QueueCommands;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

/// <summary>
/// API endpoints for command queuing operations.
/// </summary>
public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/matches/{matchId:guid}/commands")
            .WithTags("Commands")
            .RequireAuthorization();

        group.MapPost("", QueueCommands)
            .WithName("QueueCommands")
            .WithSummary("Queue commands for execution")
            .WithDescription("Queues one or more commands (move, attack, gather, build, produce, research) for execution on the next game tick. Maximum 100 commands per request, 500 commands in queue per player.")
            .Produces<QueueCommandsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> QueueCommands(
        [FromRoute] Guid matchId,
        [FromBody] QueueCommandsRequest request,
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

            // Execute command via MediatR
            var command = new QueueCommandsCommand(matchId, playerId, request.Commands);
            var response = await mediator.Send(command, cancellationToken);

            // Check if all commands failed
            if (response.QueuedCount == 0 && response.FailedCount > 0)
            {
                return Results.BadRequest(new
                {
                    error = "Failed to queue commands",
                    failures = response.Failures
                });
            }

            return Results.Ok(response);
        }
        catch (FluentValidation.ValidationException ex)
        {
            return Results.BadRequest(new
            {
                error = "Validation failed",
                failures = ex.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Error queuing commands",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
