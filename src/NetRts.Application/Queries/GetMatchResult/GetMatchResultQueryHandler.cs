using MediatR;
using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.Enums;

namespace NetRts.Application.Queries.GetMatchResult;

/// <summary>
/// Handler for retrieving final match results.
/// </summary>
public class GetMatchResultQueryHandler : IRequestHandler<GetMatchResultQuery, MatchResultResponse>
{
    private readonly IApplicationDbContext _context;

    public GetMatchResultQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MatchResultResponse> Handle(GetMatchResultQuery request, CancellationToken cancellationToken)
    {
        var match = await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == request.MatchId, cancellationToken);

        if (match == null)
        {
            throw new InvalidOperationException($"Match {request.MatchId} not found");
        }

        if (match.Status != MatchStatus.Completed)
        {
            throw new InvalidOperationException($"Match {request.MatchId} is not completed (status: {match.Status})");
        }

        // Calculate duration in seconds (assuming 1 tick = ~100ms, 10 ticks per second)
        var durationSeconds = match.CurrentTick / 10;

        var response = new MatchResultResponse
        {
            MatchId = match.Id,
            Status = match.Status.ToString(),
            WinnerId = match.WinnerId ?? Guid.Empty,
            DurationTicks = match.CurrentTick,
            DurationSeconds = durationSeconds,
            EndReason = DetermineEndReason(match),
            Player1Result = MapPlayerResult(match.Player1Id, match.Player1Score, match.WinnerId),
            Player2Result = MapPlayerResult(match.Player2Id, match.Player2Score, match.WinnerId)
        };

        return response;
    }

    private static string DetermineEndReason(Domain.Entities.Match match)
    {
        // Check if time limit was reached
        if (match.CurrentTick >= match.MaxTicksPerMatch)
        {
            return "TimeLimit";
        }

        // Check for elimination (one player has no Command Center)
        // This would need to be tracked in match state, for now return generic
        return "Victory";
    }

    private static PlayerMatchResult MapPlayerResult(Guid playerId, Domain.ValueObjects.Score score, Guid? winnerId)
    {
        return new PlayerMatchResult
        {
            PlayerId = playerId,
            UnitsDestroyedScore = score.UnitsDestroyed * 10,
            BuildingsDestroyedScore = score.BuildingsDestroyed * 50,
            ResourcesGatheredScore = score.ResourcesGathered / 10,
            UnitsRemainingScore = score.UnitsRemaining * 5,
            BuildingsRemainingScore = score.BuildingsRemaining * 25,
            TotalScore = score.TotalScore,
            IsWinner = winnerId.HasValue && winnerId.Value == playerId
        };
    }
}
