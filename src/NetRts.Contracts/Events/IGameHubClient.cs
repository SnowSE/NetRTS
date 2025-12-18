namespace NetRts.Contracts.Events;

/// <summary>
/// Typed interface for SignalR hub client methods.
/// </summary>
public interface IGameHubClient
{
    /// <summary>
    /// Receive game state update.
    /// </summary>
    Task GameStateUpdated(object gameState);

    /// <summary>
    /// Receive match ended notification.
    /// </summary>
    Task MatchEnded(Guid matchId, Guid winnerId);

    /// <summary>
    /// Receive player joined lobby notification.
    /// </summary>
    Task PlayerJoinedLobby(Guid lobbyId, Guid playerId);

    /// <summary>
    /// Receive player left lobby notification.
    /// </summary>
    Task PlayerLeftLobby(Guid lobbyId, Guid playerId);

    /// <summary>
    /// Receive match started notification.
    /// </summary>
    Task MatchStarted(Guid matchId);
}
