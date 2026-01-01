using NetRts.Contracts.Responses;

namespace NetRts.Application.Services;

/// <summary>
/// Service for retrieving game state with fog of war applied.
/// </summary>
public interface IGameStateService
{
    /// <summary>
    /// Gets the current game state for a player with fog of war applied.
    /// </summary>
    /// <param name="matchId">The match identifier</param>
    /// <param name="playerId">The requesting player identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Game state response or null if match not found</returns>
    Task<GameStateResponse?> GetGameStateAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default);
}
