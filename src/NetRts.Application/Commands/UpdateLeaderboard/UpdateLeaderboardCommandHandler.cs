using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;

namespace NetRts.Application.Commands.UpdateLeaderboard;

public class UpdateLeaderboardCommandHandler : IRequestHandler<UpdateLeaderboardCommand>
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ILeaderboardRepository _leaderboardRepository;

    public UpdateLeaderboardCommandHandler(
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        ILeaderboardRepository leaderboardRepository)
    {
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _leaderboardRepository = leaderboardRepository;
    }

    public async Task Handle(UpdateLeaderboardCommand request, CancellationToken cancellationToken)
    {
        var match = await _matchRepository.GetByIdAsync(request.MatchId, cancellationToken);
        if (match == null || match.Status != MatchStatus.Completed)
        {
            return;
        }

        // Update player 1
        await UpdatePlayerScore(match.Player1Id, match.Player1Score.TotalScore, match.WinnerId == match.Player1Id, cancellationToken);

        // Update player 2
        await UpdatePlayerScore(match.Player2Id, match.Player2Score.TotalScore, match.WinnerId == match.Player2Id, cancellationToken);

        // Recalculate all ranks
        await _leaderboardRepository.RecalculateRanksAsync(cancellationToken);
    }

    private async Task UpdatePlayerScore(Guid playerId, int matchScore, bool won, CancellationToken cancellationToken)
    {
        var playerScore = await _leaderboardRepository.GetPlayerScoreAsync(playerId, cancellationToken);
        if (playerScore == null)
        {
            var player = await _playerRepository.GetByIdAsync(playerId, cancellationToken);
            if (player == null) return;
            playerScore = new PlayerScore(playerId, player.Username);
        }

        playerScore.UpdateStats(matchScore, won);
        await _leaderboardRepository.UpsertPlayerScoreAsync(playerScore, cancellationToken);
    }
}
