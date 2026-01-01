using Microsoft.AspNetCore.SignalR;

namespace NetRts.Api.Hubs;

/// <summary>
/// SignalR hub for real-time game state updates.
/// </summary>
public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;

    public GameHub(ILogger<GameHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to updates for a specific match.
    /// Also subscribes to player-specific updates if playerId is provided.
    /// </summary>
    public async Task SubscribeToMatch(Guid matchId, Guid? playerId = null)
    {
        // Subscribe to the general match group (for spectators and general updates)
        await Groups.AddToGroupAsync(Context.ConnectionId, matchId.ToString());
        _logger.LogInformation("Connection {ConnectionId} subscribed to match {MatchId}", 
            Context.ConnectionId, matchId);

        // If playerId is provided, also subscribe to player-specific group for fog-of-war filtered updates
        if (playerId.HasValue)
        {
            var playerMatchGroup = $"player-{playerId.Value}-match-{matchId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, playerMatchGroup);
            _logger.LogInformation("Connection {ConnectionId} subscribed to player-specific group {Group}", 
                Context.ConnectionId, playerMatchGroup);
        }
    }

    /// <summary>
    /// Subscribe to match with player ID extracted from connection context.
    /// </summary>
    public async Task SubscribeToMatchAsPlayer(Guid matchId, Guid playerId)
    {
        // Subscribe to the general match group
        await Groups.AddToGroupAsync(Context.ConnectionId, matchId.ToString());
        
        // Subscribe to player-specific group for personalized fog-of-war updates
        var playerMatchGroup = $"player-{playerId}-match-{matchId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, playerMatchGroup);
        
        _logger.LogInformation("Player {PlayerId} subscribed to match {MatchId} (Connection: {ConnectionId})", 
            playerId, matchId, Context.ConnectionId);
    }

    /// <summary>
    /// Unsubscribe from match updates.
    /// </summary>
    public async Task UnsubscribeFromMatch(Guid matchId, Guid? playerId = null)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId.ToString());
        
        if (playerId.HasValue)
        {
            var playerMatchGroup = $"player-{playerId.Value}-match-{matchId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, playerMatchGroup);
        }
        
        _logger.LogInformation("Connection {ConnectionId} unsubscribed from match {MatchId}", 
            Context.ConnectionId, matchId);
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

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, Exception: {Exception}", 
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
