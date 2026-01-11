using System.Net.Http.Json;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Client.Services;

public class GameApiClient
{
    private readonly AuthenticatedHttpClient _httpClient;

    public GameApiClient(AuthenticatedHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LobbyListResponse?> GetLobbiesAsync(int page = 1, int pageSize = 10)
    {
        return await _httpClient.GetAsync<LobbyListResponse>($"/api/v1/lobbies?page={page}&pageSize={pageSize}");
    }

    public async Task<LobbyResponse?> CreateLobbyAsync(string name)
    {
        var request = new CreateLobbyRequest { Name = name };
        var response = await _httpClient.PostAsync("/api/v1/lobbies", request);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<LobbyResponse>();
        }
        return null;
    }

    public async Task<LobbyResponse?> JoinLobbyAsync(Guid lobbyId)
    {
        var response = await _httpClient.PostAsync($"/api/v1/lobbies/{lobbyId}/join", new { });
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<LobbyResponse>();
        }
        return null;
    }

    public async Task<Guid?> StartMatchAsync(Guid lobbyId)
    {
        var response = await _httpClient.PostAsync($"/api/v1/lobbies/{lobbyId}/start", new { });
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<Guid>();
        }
        return null;
    }

    public async Task<GameStateResponse?> GetGameStateAsync(Guid matchId)
    {
        return await _httpClient.GetAsync<GameStateResponse>($"/api/v1/matches/{matchId}/state");
    }

    public async Task<QueueCommandsResponse?> QueueCommandsAsync(Guid matchId, QueueCommandsRequest request)
    {
        var response = await _httpClient.PostAsync($"/api/v1/matches/{matchId}/commands", request);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }
        return null;
    }

    public async Task<MatchResultResponse?> GetMatchResultAsync(Guid matchId)
    {
        return await _httpClient.GetAsync<MatchResultResponse>($"/api/v1/matches/{matchId}/result");
    }

    public async Task<LeaderboardResponse?> GetLeaderboardAsync(int page = 1, int pageSize = 20)
    {
        return await _httpClient.GetAsync<LeaderboardResponse>($"/api/v1/leaderboard?page={page}&pageSize={pageSize}");
    }
}
