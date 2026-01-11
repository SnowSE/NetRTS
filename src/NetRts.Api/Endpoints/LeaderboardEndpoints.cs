using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetRts.Application.Queries.GetLeaderboard;
using NetRts.Contracts.Responses;

namespace NetRts.Api.Endpoints;

public static class LeaderboardEndpoints
{
    public static void MapLeaderboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/leaderboard")
            .WithTags("Leaderboard");

        group.MapGet("/", GetLeaderboard)
            .AllowAnonymous()
            .Produces<LeaderboardResponse>(StatusCodes.Status200OK);

        group.MapGet("/player/{playerId:guid}", GetPlayerStats)
            .AllowAnonymous()
            .Produces<PlayerScoreDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetLeaderboard(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        IMediator mediator = default!,
        CancellationToken cancellationToken = default!)
    {
        var query = new GetLeaderboardQuery(page, pageSize);
        var response = await mediator.Send(query, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetPlayerStats(
        Guid playerId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // For simplicity, search in the first page of leaderboard
        var query = new GetLeaderboardQuery(1, 100);
        var response = await mediator.Send(query, cancellationToken);
        var player = response.Entries.FirstOrDefault(e => e.PlayerId == playerId);
        
        return player != null ? Results.Ok(player) : Results.NotFound();
    }
}
