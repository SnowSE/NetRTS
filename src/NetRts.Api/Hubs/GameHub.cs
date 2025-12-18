using Microsoft.AspNetCore.SignalR;

namespace NetRts.Api.Hubs;

/// <summary>
/// SignalR hub for real-time game state updates.
/// </summary>
public class GameHub : Hub
{
    /// <summary>
    /// Subscribe to updates for a specific match.
    /// </summary>
    public async Task SubscribeToMatch(Guid matchId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, matchId.ToString());
    }

    /// <summary>
    /// Unsubscribe from match updates.
    /// </summary>
    public async Task UnsubscribeFromMatch(Guid matchId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId.ToString());
    }

    /// <summary>
    /// Subscribe to lobby updates.
    /// </summary>
    public async Task SubscribeToLobby(Guid lobbyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby-{lobbyId}");
    }

    /// <summary>
    /// Unsubscribe from lobby updates.
    /// </summary>
    public async Task UnsubscribeFromLobby(Guid lobbyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"lobby-{lobbyId}");
    }
}
