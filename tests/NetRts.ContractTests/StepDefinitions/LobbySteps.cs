using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class LobbySteps
{
    private readonly TestContext _context;
    private readonly Dictionary<string, (Guid Id, string Token)> _players = new();

    public LobbySteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"a player ""([^""]*)"" is registered")]
    public async Task GivenAPlayerIsRegistered(string playerName)
    {
        // Add random suffix to avoid "username taken" errors between scenarios
        var uniqueName = $"{playerName}_{Guid.NewGuid().ToString("N")[..6]}";
        
        var request = new RegisterPlayerRequest 
        { 
            Username = uniqueName, 
            Email = $"{uniqueName.ToLower()}@example.com", 
            IsBot = true 
        };
        var response = await _context.HttpClient.PostAsJsonAsync("/api/v1/players", request);
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PlayerResponse>();
        
        result.Should().NotBeNull();
        _players[playerName] = (result!.PlayerId, result.Token!);
    }

    [Given(@"I am authenticated as ""([^""]*)""")]
    public void GivenIAmAuthenticatedAs(string playerName)
    {
        var player = _players[playerName];
        _context.SetAuthToken(player.Token, player.Id);
    }

    [Given(@"""([^""]*)"" has created a lobby named ""([^""]*)""")]
    public async Task GivenHasCreatedALobbyNamed(string playerName, string lobbyName)
    {
        GivenIAmAuthenticatedAs(playerName);
        await WhenICreateALobbyNamed(lobbyName);
        ThenTheLobbyIsCreatedSuccessfully();
    }

    [Given(@"""([^""]*)"" has joined the lobby named ""([^""]*)""")]
    public async Task GivenHasJoinedTheLobbyNamed(string playerName, string lobbyName)
    {
        GivenIAmAuthenticatedAs(playerName);
        await WhenIJoinTheLobbyNamed(lobbyName);
        ThenIAmAddedToTheLobbySuccessfully();
    }

    [Given(@"I have created a lobby named ""([^""]*)""")]
    public async Task GivenIHaveCreatedALobbyNamed(string lobbyName)
    {
        await WhenICreateALobbyNamed(lobbyName);
    }

    [Given(@"I have joined the lobby named ""([^""]*)""")]
    public async Task GivenIHaveJoinedTheLobbyNamed(string lobbyName)
    {
        await WhenIJoinTheLobbyNamed(lobbyName);
    }

    [When(@"I create a lobby named ""([^""]*)""")]
    public async Task WhenICreateALobbyNamed(string lobbyName)
    {
        var request = new CreateLobbyRequest
        {
            Name = lobbyName,
            Settings = new GameSettingsDto
            {
                MapWidth = 100,
                MapHeight = 100,
                MaxTicks = 1800,
                TickIntervalMs = 1000,
                CommandQueueSize = 500,
                CommandsPerTick = 100,
                StartingResources = 500
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync("/api/v1/lobbies", request);
        
        if (_context.LastResponse.IsSuccessStatusCode)
        {
            var result = await _context.LastResponse.Content.ReadFromJsonAsync<LobbyResponse>();
            _context.LobbyId = result!.Id;
        }
    }

    [When(@"I join the lobby named ""([^""]*)""")]
    public async Task WhenIJoinTheLobbyNamed(string lobbyName)
    {
        // For simplicity, we assume the LobbyId is already set in _context from a previous step
        // or we would need to find it by name. Since we only have one active lobby usually:
        _context.LastResponse = await _context.HttpClient.PostAsync($"/api/v1/lobbies/{_context.LobbyId}/join", null);
    }

    [When(@"I attempt to join the lobby named ""([^""]*)""")]
    public async Task WhenIAttemptToJoinTheLobbyNamed(string lobbyName)
    {
        await WhenIJoinTheLobbyNamed(lobbyName);
    }

    [When(@"I update the lobby settings to map size (\d+)x(\d+)")]
    public async Task WhenIUpdateTheLobbySettingsToMapSizeX(int width, int height)
    {
        var request = new UpdateLobbySettingsRequest { MapWidth = width, MapHeight = height };
        _context.LastResponse = await _context.HttpClient.PutAsJsonAsync($"/api/v1/lobbies/{_context.LobbyId}/settings", request);
    }

    [When(@"I start the match for lobby ""([^""]*)""")]
    public async Task WhenIStartTheMatchForLobby(string lobbyName)
    {
        _context.LastResponse = await _context.HttpClient.PostAsync($"/api/v1/lobbies/{_context.LobbyId}/start", null);
        
        if (_context.LastResponse.IsSuccessStatusCode)
        {
            var result = await _context.LastResponse.Content.ReadFromJsonAsync<StartMatchResponse>();
            _context.MatchId = result!.MatchId;
        }
    }

    [When(@"I leave the lobby ""([^""]*)""")]
    public async Task WhenILeaveTheLobby(string lobbyName)
    {
        _context.LastResponse = await _context.HttpClient.PostAsync($"/api/v1/lobbies/{_context.LobbyId}/leave", null);
    }

    [Then(@"the lobby is created successfully")]
    public void ThenTheLobbyIsCreatedSuccessfully()
    {
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Then(@"I am the host of the lobby")]
    public async Task ThenIAmTheHostOfTheLobby()
    {
        var response = await _context.HttpClient.GetAsync($"/api/v1/lobbies/{_context.LobbyId}");
        var lobby = await response.Content.ReadFromJsonAsync<LobbyResponse>();
        lobby!.HostPlayerId.Should().NotBeEmpty();
    }

    [Then(@"I am added to the lobby successfully")]
    public void ThenIAmAddedToTheLobbySuccessfully()
    {
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Then(@"the lobby now has (\d+) players?")]
    public async Task ThenTheLobbyNowHasPlayers(int count)
    {
        var response = await _context.HttpClient.GetAsync($"/api/v1/lobbies/{_context.LobbyId}");
        var lobby = await response.Content.ReadFromJsonAsync<LobbyResponse>();
        lobby!.Players.Should().HaveCount(count);
    }

    [Then(@"I receive an error ""([^""]*)""")]
    public async Task ThenIReceiveAnError(string errorMessage)
    {
        _context.LastResponse!.IsSuccessStatusCode.Should().BeFalse();
        var body = await _context.LastResponse.Content.ReadAsStringAsync();
        // The error might be in ProblemDetails or just a string
        body.Should().Contain(errorMessage);
    }

    [Then(@"the lobby settings are updated successfully")]
    public void ThenTheLobbySettingsAreUpdatedSuccessfully()
    {
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Then(@"the match is started successfully")]
    public void ThenTheMatchIsStartedSuccessfully()
    {
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
        _context.MatchId.Should().NotBeEmpty();
    }

    [Then(@"the lobby is closed")]
    public async Task ThenTheLobbyIsClosed()
    {
        var response = await _context.HttpClient.GetAsync($"/api/v1/lobbies/{_context.LobbyId}");
        var lobby = await response.Content.ReadFromJsonAsync<LobbyResponse>();
        lobby!.Status.Should().Be("Closed");
    }

    [Then(@"I am no longer in the lobby")]
    public void ThenIAmNoLongerInTheLobby()
    {
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
