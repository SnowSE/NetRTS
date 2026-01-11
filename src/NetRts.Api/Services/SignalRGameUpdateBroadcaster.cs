using Microsoft.AspNetCore.SignalR;
using NetRts.Api.Hubs;
using NetRts.Application.Services;

namespace NetRts.Api.Services;

/// <summary>
/// Broadcasts game state updates to connected clients via SignalR.
/// </summary>
public class SignalRGameUpdateBroadcaster : IGameUpdateBroadcaster
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SignalRGameUpdateBroadcaster> _logger;

    public SignalRGameUpdateBroadcaster(
        IHubContext<GameHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<SignalRGameUpdateBroadcaster> logger)
    {
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task BroadcastGameStateAsync(Guid matchId, Guid player1Id, Guid player2Id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create a scope to resolve scoped services
            using var scope = _scopeFactory.CreateScope();
            var gameStateService = scope.ServiceProvider.GetRequiredService<IGameStateService>();

            // Get game state for each player (with their fog of war applied)
            var player1State = await gameStateService.GetGameStateAsync(matchId, player1Id, cancellationToken);
            var player2State = await gameStateService.GetGameStateAsync(matchId, player2Id, cancellationToken);

            if (player1State != null)
            {
                // Send to player 1's personal connection (using player ID as additional group)
                await _hubContext.Clients.Group($"player-{player1Id}-match-{matchId}")
                    .SendAsync("GameStateUpdate", player1State, cancellationToken);
                
                _logger.LogDebug("Broadcast game state to player {PlayerId} for match {MatchId}, Tick: {Tick}", 
                    player1Id, matchId, player1State.CurrentTick);
            }

            if (player2State != null)
            {
                // Send to player 2's personal connection
                await _hubContext.Clients.Group($"player-{player2Id}-match-{matchId}")
                    .SendAsync("GameStateUpdate", player2State, cancellationToken);
                
                _logger.LogDebug("Broadcast game state to player {PlayerId} for match {MatchId}, Tick: {Tick}", 
                    player2Id, matchId, player2State.CurrentTick);
            }

            // Also broadcast to the general match group (spectators get player 1's view for now)
            if (player1State != null)
            {
                await _hubContext.Clients.Group(matchId.ToString())
                    .SendAsync("GameStateUpdate", player1State, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting game state for match {MatchId}", matchId);
        }
    }

    public async Task BroadcastMatchEndedAsync(Guid matchId, Guid? winnerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var matchEndedEvent = new
            {
                MatchId = matchId,
                WinnerId = winnerId,
                EndedAt = DateTime.UtcNow
            };

            await _hubContext.Clients.Group(matchId.ToString())
                .SendAsync("MatchEnded", matchEndedEvent, cancellationToken);

            _logger.LogInformation("Broadcast match ended for match {MatchId}, Winner: {WinnerId}", matchId, winnerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting match ended for match {MatchId}", matchId);
        }
    }

    public async Task BroadcastLobbyPlayerJoinedAsync(Guid lobbyId, Guid playerId, string username, int slot, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"lobby-{lobbyId}")
                .SendAsync("LobbyPlayerJoined", new { LobbyId = lobbyId, PlayerId = playerId, Username = username, Slot = slot }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting lobby player joined for lobby {LobbyId}", lobbyId);
        }
    }

    public async Task BroadcastLobbyPlayerLeftAsync(Guid lobbyId, Guid playerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"lobby-{lobbyId}")
                .SendAsync("LobbyPlayerLeft", new { LobbyId = lobbyId, PlayerId = playerId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting lobby player left for lobby {LobbyId}", lobbyId);
        }
    }

    public async Task BroadcastMatchStartedAsync(Guid lobbyId, Guid matchId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"lobby-{lobbyId}")
                .SendAsync("MatchStarted", new { LobbyId = lobbyId, MatchId = matchId }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting match started for lobby {LobbyId}", lobbyId);
        }
    }
}
