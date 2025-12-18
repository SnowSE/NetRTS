using NetRts.Domain.Entities;

namespace NetRts.Application.Interfaces;

public interface ILeaderboardRepository
{
    Task<List<PlayerScore>> GetTopPlayersAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<PlayerScore?> GetPlayerScoreAsync(Guid playerId, CancellationToken cancellationToken = default);
    Task UpsertPlayerScoreAsync(PlayerScore playerScore, CancellationToken cancellationToken = default);
    Task RecalculateRanksAsync(CancellationToken cancellationToken = default);
}
