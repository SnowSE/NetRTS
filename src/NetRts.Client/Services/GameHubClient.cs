using Microsoft.AspNetCore.SignalR.Client;
using NetRts.Contracts.Responses;

namespace NetRts.Client.Services;

public class GameHubClient : IAsyncDisposable
{
    private readonly string _hubUrl;
    private HubConnection? _hubConnection;

    public event Action<GameStateResponse>? OnGameStateUpdate;
    public event Action<object>? OnMatchEnded; // Use object for now, or specific DTO
    public event Action<Guid, Guid, string, int>? OnLobbyPlayerJoined;
    public event Action<Guid, Guid>? OnLobbyPlayerLeft;
    public event Action<Guid, Guid>? OnMatchStarted;

    public GameHubClient(string hubUrl)
    {
        _hubUrl = hubUrl;
    }

    public async Task StartAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<GameStateResponse>("GameStateUpdate", (state) =>
        {
            OnGameStateUpdate?.Invoke(state);
        });

        _hubConnection.On<object>("MatchEnded", (matchEnded) =>
        {
            OnMatchEnded?.Invoke(matchEnded);
        });

        _hubConnection.On<Guid, Guid, string, int>("LobbyPlayerJoined", (lobbyId, playerId, username, slot) =>
        {
            OnLobbyPlayerJoined?.Invoke(lobbyId, playerId, username, slot);
        });

        _hubConnection.On<Guid, Guid>("LobbyPlayerLeft", (lobbyId, playerId) =>
        {
            OnLobbyPlayerLeft?.Invoke(lobbyId, playerId);
        });

        _hubConnection.On<Guid, Guid>("MatchStarted", (lobbyId, matchId) =>
        {
            OnMatchStarted?.Invoke(lobbyId, matchId);
        });

        await _hubConnection.StartAsync();
    }

    public async Task SubscribeToMatch(Guid matchId, Guid? playerId = null)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.SendAsync("SubscribeToMatch", matchId, playerId);
        }
    }

    public async Task SubscribeToLobby(Guid lobbyId)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.SendAsync("SubscribeToLobby", lobbyId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
