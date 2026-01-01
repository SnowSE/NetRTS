using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using System.Net;
using System.Net.Http.Json;

namespace NetRts.IntegrationTests;

public class MultiplayerLobbyTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public MultiplayerLobbyTests(CustomWebApplicationFactory factory)
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
    public async Task CreateLobby_ShouldCreateLobbyWithHost()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var request = new CreateLobbyRequest
        {
            Name = "Test Lobby",
            MapWidth = 100,
            MapHeight = 100,
            MaxTicks = 1800,
            StartingResources = 500
        };

        // Act
        var response = await PostAsJsonWithPlayer($"/api/v1/lobbies", request, playerId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var lobby = await response.Content.ReadFromJsonAsync<LobbyResponse>();
        lobby.Should().NotBeNull();
        lobby!.Name.Should().Be("Test Lobby");
        lobby.HostPlayerId.Should().Be(playerId);
        lobby.Status.Should().Be("Open");
        lobby.CurrentPlayerCount.Should().Be(1);
        lobby.MaxPlayers.Should().Be(2);
        lobby.Settings.MapWidth.Should().Be(100);
    }

    [Fact]
    public async Task GetLobbies_ShouldReturnOpenLobbies()
    {
        // Arrange - Create two lobbies
        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();

        await PostAsJsonWithPlayer("/api/v1/lobbies", new CreateLobbyRequest { Name = "Lobby 1" }, player1);
        await PostAsJsonWithPlayer("/api/v1/lobbies", new CreateLobbyRequest { Name = "Lobby 2" }, player2);

        // Act
        var response = await _client.GetAsync("/api/v1/lobbies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lobbies = await response.Content.ReadFromJsonAsync<List<LobbyResponse>>();
        lobbies.Should().NotBeNull();
        lobbies!.Count.Should().BeGreaterThanOrEqualTo(2);
        lobbies.Should().Contain(l => l.Name == "Lobby 1");
        lobbies.Should().Contain(l => l.Name == "Lobby 2");
    }

    [Fact]
    public async Task JoinLobby_ShouldAddSecondPlayer()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Test Lobby" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        // Act
        var joinResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, joinerId);

        // Assert
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedLobby = await joinResponse.Content.ReadFromJsonAsync<LobbyResponse>();
        updatedLobby.Should().NotBeNull();
        updatedLobby!.CurrentPlayerCount.Should().Be(2);
        updatedLobby.Status.Should().Be("Full");
        updatedLobby.Players.Should().HaveCount(2);
        updatedLobby.Players.Should().Contain(p => p.PlayerId == hostId);
        updatedLobby.Players.Should().Contain(p => p.PlayerId == joinerId);
    }

    [Fact]
    public async Task JoinLobby_WhenFull_ShouldReturnBadRequest()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var player3Id = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Full Lobby" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, player2Id);

        // Act - Try to join when full
        var response = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/join", new { }, player3Id);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LeaveLobby_AsNonHost_ShouldRemovePlayer()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Test Lobby" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, joinerId);

        // Act
        var leaveResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/leave", new { }, joinerId);

        // Assert
        leaveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/v1/lobbies/{lobby.Id}");
        var updatedLobby = await getResponse.Content.ReadFromJsonAsync<LobbyResponse>();
        updatedLobby!.CurrentPlayerCount.Should().Be(1);
        updatedLobby.Status.Should().Be("Open");
    }

    [Fact]
    public async Task LeaveLobby_AsHost_ShouldCloseLobby()
    {
        // Arrange
        var hostId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Host Test" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        // Act
        var leaveResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/leave", new { }, hostId);

        // Assert
        leaveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/v1/lobbies/{lobby.Id}");
        var updatedLobby = await getResponse.Content.ReadFromJsonAsync<LobbyResponse>();
        updatedLobby!.Status.Should().Be("Closed");
    }

    [Fact]
    public async Task UpdateSettings_AsHost_ShouldUpdateSettings()
    {
        // Arrange
        var hostId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Settings Test" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        var updateRequest = new UpdateLobbySettingsRequest
        {
            MapWidth = 150,
            MapHeight = 150,
            MaxTicks = 3600,
            StartingResources = 1000
        };

        // Act
        var response = await PutAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/settings", updateRequest, hostId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedLobby = await response.Content.ReadFromJsonAsync<LobbyResponse>();
        updatedLobby!.Settings.MapWidth.Should().Be(150);
        updatedLobby.Settings.MapHeight.Should().Be(150);
        updatedLobby.Settings.MaxTicks.Should().Be(3600);
        updatedLobby.Settings.StartingResources.Should().Be(1000);
    }

    [Fact]
    public async Task UpdateSettings_AsNonHost_ShouldReturnForbidden()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var otherPlayerId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Auth Test" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, otherPlayerId);

        var updateRequest = new UpdateLobbySettingsRequest { MapWidth = 200 };

        // Act
        var response = await PutAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/settings", updateRequest, otherPlayerId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartMatch_WithTwoPlayers_ShouldCreateMatch()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Start Test" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, player2Id);

        // Act
        var startResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/start", new { }, hostId);

        // Assert
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await startResponse.Content.ReadFromJsonAsync<StartMatchResponse>();
        result.Should().NotBeNull();
        result!.MatchId.Should().NotBeEmpty();

        var expectedPlayers = new[] { hostId, player2Id };
        expectedPlayers.Should().Contain(result.Player1Id);
        expectedPlayers.Should().Contain(result.Player2Id);
        result.Player1Id.Should().NotBe(result.Player2Id);

        // Verify lobby is closed
        var lobbyResponse = await _client.GetAsync($"/api/v1/lobbies/{lobby.Id}");
        var updatedLobby = await lobbyResponse.Content.ReadFromJsonAsync<LobbyResponse>();
        updatedLobby!.Status.Should().Be("Closed");

        // Verify match exists and can be accessed
        var gameStateResponse = await GetWithPlayer($"/api/v1/matches/{result.MatchId}/state", hostId);
        gameStateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartMatch_WithOnlyHost_ShouldReturnBadRequest()
    {
        // Arrange
        var hostId = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Single Player" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        // Act
        var startResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/start", new { }, hostId);

        // Assert
        startResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartMatch_AsNonHost_ShouldReturnForbidden()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest { Name = "Auth Start Test" }, hostId);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, player2Id);

        // Act
        var startResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/start", new { }, player2Id);

        // Assert
        startResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CompleteMultiplayerFlow_CreateJoinStartAndPlay()
    {
        // Arrange
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        // Step 1: Player 1 creates lobby
        var createResponse = await PostAsJsonWithPlayer("/api/v1/lobbies",
            new CreateLobbyRequest
            {
                Name = "Full Flow Test",
                MapWidth = 100,
                MapHeight = 100,
                MaxTicks = 1800,
                StartingResources = 500
            }, player1Id);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var lobby = await createResponse.Content.ReadFromJsonAsync<LobbyResponse>();

        // Step 2: Player 2 joins lobby
        var joinResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby!.Id}/join", new { }, player2Id);
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Host starts match
        var startResponse = await PostAsJsonWithPlayer($"/api/v1/lobbies/{lobby.Id}/start", new { }, player1Id);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var matchInfo = await startResponse.Content.ReadFromJsonAsync<StartMatchResponse>();

        // Step 4: Both players can access game state
        var p1StateResponse = await GetWithPlayer($"/api/v1/matches/{matchInfo!.MatchId}/state", player1Id);
        p1StateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var p1State = await p1StateResponse.Content.ReadFromJsonAsync<GameStateResponse>();
        p1State.Should().NotBeNull();
        p1State!.Units.Should().NotBeEmpty();

        var p2StateResponse = await GetWithPlayer($"/api/v1/matches/{matchInfo.MatchId}/state", player2Id);
        p2StateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var p2State = await p2StateResponse.Content.ReadFromJsonAsync<GameStateResponse>();
        p2State.Should().NotBeNull();
        p2State!.Units.Should().NotBeEmpty();

        // Step 5: Player 1 queues a command
        var commandRequest = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Move",
                    UnitIds = new[] { p1State.Units.First().UnitId },
                    TargetPosition = new PositionDto { X = 20, Y = 20 }
                }
            }
        };

        var commandResponse = await PostAsJsonWithPlayer($"/api/v1/matches/{matchInfo.MatchId}/commands",
            commandRequest, player1Id);
        commandResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify command was queued
        var cmdResult = await commandResponse.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        cmdResult.Should().NotBeNull();
        cmdResult!.QueuedCount.Should().Be(1);
    }

    private async Task<HttpResponseMessage> PostAsJsonWithPlayer<T>(string url, T content, Guid playerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Test-Player-Id", playerId.ToString());
        request.Content = JsonContent.Create(content);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutAsJsonWithPlayer<T>(string url, T content, Guid playerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
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
