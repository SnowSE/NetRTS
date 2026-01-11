using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetLeaderboard;

public class GetLeaderboardQueryHandler : IRequestHandler<GetLeaderboardQuery, LeaderboardResponse>
{
    private readonly ILeaderboardRepository _leaderboardRepository;

    public GetLeaderboardQueryHandler(ILeaderboardRepository leaderboardRepository)
    {
        _leaderboardRepository = leaderboardRepository;
    }

    public async Task<LeaderboardResponse> Handle(GetLeaderboardQuery request, CancellationToken cancellationToken)
    {
        // For now, ILeaderboardRepository only has GetTopPlayersAsync which doesn't support pagination.
        // We'll use it and handle pagination in memory for now, or update the repo.
        var players = await _leaderboardRepository.GetTopPlayersAsync(100, cancellationToken);
        
        var totalCount = players.Count;
        var pagedPlayers = players
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new LeaderboardResponse
        {
            Entries = pagedPlayers.Select((p, index) => new PlayerScoreDto
            {
                Rank = ((request.Page - 1) * request.PageSize) + index + 1,
                PlayerId = p.PlayerId,
                Username = p.Username,
                TotalScore = p.TotalScore,
                MatchesPlayed = p.MatchesPlayed,
                TotalWins = p.MatchesWon,
                WinRate = p.MatchesPlayed > 0 ? (double)p.MatchesWon / p.MatchesPlayed : 0
            }).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
