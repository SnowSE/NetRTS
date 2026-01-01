using NetRts.Contracts.Responses;

namespace NetRts.Application.Services;

/// <summary>
/// Interface for broadcasting game state updates to connected clients.
/// </summary>
public interface IGameUpdateBroadcaster
{
    /// <summary>
    /// Broadcasts the updated game state to all players in a match.
    /// Each player receives their own view with fog of war applied.
    /// </summary>
    /// <param name="matchId">The match identifier</param>
    /// <param name="player1Id">Player 1 identifier</param>
    /// <param name="player2Id">Player 2 identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastGameStateAsync(Guid matchId, Guid player1Id, Guid player2Id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts match ended event to all players in a match.
    /// </summary>
    /// <param name="matchId">The match identifier</param>
    /// <param name="winnerId">The winner's player identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastMatchEndedAsync(Guid matchId, Guid? winnerId, CancellationToken cancellationToken = default);
}
