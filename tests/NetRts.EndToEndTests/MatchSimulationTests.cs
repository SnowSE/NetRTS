using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Enums;

namespace NetRts.EndToEndTests;

public class MatchSimulationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public MatchSimulationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteMatchSimulation_ShouldDetermineWinner()
    {
        // 1. Register two players
        var p1Response = await _client.PostAsJsonAsync("/api/v1/players", new RegisterPlayerRequest { Username = "Bot1", Password = "Password123" });
        p1Response.StatusCode.Should().Be(HttpStatusCode.Created);
        var player1 = await p1Response.Content.ReadFromJsonAsync<PlayerResponse>();

        var p2Response = await _client.PostAsJsonAsync("/api/v1/players", new RegisterPlayerRequest { Username = "Bot2", Password = "Password123" });
        p2Response.StatusCode.Should().Be(HttpStatusCode.Created);
        var player2 = await p2Response.Content.ReadFromJsonAsync<PlayerResponse>();

        // 2. Create lobby (Host = Player 1)
        var createLobbyResponse = await PostAsJsonWithPlayer("/api/v1/lobbies", 
            new CreateLobbyRequest { Name = "E2E Lobby" }, player1!.PlayerId);
        createLobbyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var lobby = await createLobbyResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        // 3. Join lobby (Player 2)
        var joinResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, player2!.PlayerId);
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Start match
        var startResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/start", new { }, player1.PlayerId);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var matchInfo = await startResponse.Content.ReadFromJsonAsync<StartMatchResponse>();
        var matchId = matchInfo!.MatchId;

        // 5. Get initial state
        var stateResponse = await GetWithPlayer($"/api/v1/matches/{matchId}/state", player1.PlayerId);
        var state = await stateResponse.Content.ReadFromJsonAsync<GameStateResponse>();
        state!.Units.Should().NotBeEmpty();
        var p1Units = state.Units.Where(u => u.PlayerId == player1.PlayerId).ToList();
        
        // Enemy CC is at (90,90) based on CreateMatchCommandHandler logic
        var enemyCCPos = new PositionDto { X = 90, Y = 90 };

        // 6. Issue move command (Player 1 units move toward Player 2 base)
        var moveRequest = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Move",
                    UnitIds = p1Units.Select(u => u.UnitId).ToArray(),
                    TargetPosition = enemyCCPos
                }
            }
        };
        var moveResponse = await PostAsJsonWithPlayer($"/api/v1/matches/{matchId}/commands", moveRequest, player1.PlayerId);
        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Process ticks until they arrive and destroy the CC
        var tickProcessor = _factory.Services.GetRequiredService<IGameTickProcessor>();
        var attackIssued = false;
        
        for (int i = 0; i < 1000; i++)
        {
            await tickProcessor.ProcessTickAsync();
            
            var currentStateResponse = await GetWithPlayer($"/api/v1/matches/{matchId}/state", player1.PlayerId);
            if (currentStateResponse.StatusCode == HttpStatusCode.NotFound) break;
            
            var currentState = await currentStateResponse.Content.ReadFromJsonAsync<GameStateResponse>();
            
            // Check if enemy CC is now visible
            var visibleEnemyCC = currentState!.Buildings.FirstOrDefault(b => b.PlayerId == player2.PlayerId && b.BuildingType == "CommandCenter");
            
            if (visibleEnemyCC != null && !attackIssued)
            {
                // Once visible, issue attack command
                var attackRequest = new QueueCommandsRequest
                {
                    Commands = new[]
                    {
                        new CommandDto
                        {
                            CommandType = "Attack",
                            UnitIds = currentState.Units.Where(u => u.PlayerId == player1.PlayerId).Select(u => u.UnitId).ToArray(),
                            TargetBuildingId = visibleEnemyCC.BuildingId
                        }
                    }
                };
                await PostAsJsonWithPlayer($"/api/v1/matches/{matchId}/commands", attackRequest, player1.PlayerId);
                attackIssued = true;
            }

            if (currentState.MatchStatus == "Completed") break;
        }

        // 8. Verify match results
        var resultResponse = await _client.GetAsync($"/api/v1/matches/{matchId}/result");
        resultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await resultResponse.Content.ReadFromJsonAsync<MatchResultResponse>();
        result.Should().NotBeNull();
        result!.WinnerId.Should().Be(player1.PlayerId);
        result.Status.Should().Be("Completed");
    }

    private async Task<HttpResponseMessage> PostAsJsonWithPlayer<T>(string url, T content, Guid playerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Test-Player-Id", playerId.ToString());
        request.Content = JsonContent.Create(content);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetWithPlayer(string url, Guid playerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Player-Id", playerId.ToString());
        return await _client.SendAsync(request);
    }
}