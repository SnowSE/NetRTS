using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Enums;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class GameStateSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;

    public GameStateSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"a new match has started with two players")]
    public async Task GivenANewMatchHasStartedWithTwoPlayers()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var (match, units, buildings) = await builder.CreateMatchWithPlayers(
            _context.Player1Id,
            _context.Player2Id);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
        _context.Player2Token = GenerateJwtToken(_context.Player2Id);
    }

    [Given(@"a match with player 1 units at position \((\d+),(\d+)\) with vision range (\d+)")]
    public async Task GivenAMatchWithPlayer1UnitsAtPositionWithVisionRange(int x, int y, int visionRange)
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var unitConfigs = new List<(int, int, int, UnitType, Guid)>
        {
            (1, x, y, UnitType.Worker, _context.Player1Id)
        };

        var match = await builder.CreateMatchWithCustomUnits(
            _context.Player1Id,
            _context.Player2Id,
            unitConfigs);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
    }

    [Given(@"a match where player 2 units are outside player 1 vision range")]
    public async Task GivenAMatchWherePlayer2UnitsAreOutsidePlayer1VisionRange()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        // Player 1 unit at (10,10), Player 2 units far away at (90,90)
        // Workers have vision range 5, so player 2 should be invisible
        var unitConfigs = new List<(int, int, int, UnitType, Guid)>
        {
            (1, 10, 10, UnitType.Worker, _context.Player1Id),
            (2, 90, 90, UnitType.Worker, _context.Player2Id)
        };

        var match = await builder.CreateMatchWithCustomUnits(
            _context.Player1Id,
            _context.Player2Id,
            unitConfigs);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
    }

    [Given(@"a match is in progress")]
    public async Task GivenAMatchIsInProgress()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var (match, _, _) = await builder.CreateMatchWithPlayers(
            _context.Player1Id,
            _context.Player2Id);

        _context.MatchId = match.Id;
    }

    [When(@"player (\d+) requests game state")]
    [When(@"player (\d+) requests current game state")]
    public async Task WhenPlayerRequestsGameState(int playerNumber)
    {
        var token = playerNumber == 1 ? _context.Player1Token : _context.Player2Token;
        _context.SetAuthToken(token);

        _context.LastResponse = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _context.GameState = await _context.LastResponse.Content
                .ReadFromJsonAsync<GameStateResponse>();
        }
    }

    [When(@"a bot requests game state with invalid authentication")]
    public async Task WhenABotRequestsGameStateWithInvalidAuthentication()
    {
        _context.SetAuthToken("invalid-token-that-will-fail-validation");

        _context.LastResponse = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");
    }

    [Then(@"the response includes their starting units")]
    public void ThenTheResponseIncludesTheirStartingUnits()
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.Units.Should().NotBeEmpty();
        _context.GameState.Units.Should().OnlyContain(u => u.PlayerId == _context.Player1Id);
    }

    [Then(@"the response includes their resource amounts")]
    public void ThenTheResponseIncludesTheirResourceAmounts()
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.PlayerResources.Should().BeGreaterThan(0);
    }

    [Then(@"the response includes visible map tiles within vision range")]
    public void ThenTheResponseIncludesVisibleMapTilesWithinVisionRange()
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.VisibleTiles.Should().NotBeEmpty();
    }

    [Then(@"the response includes fog of war indicators")]
    public void ThenTheResponseIncludesFogOfWarIndicators()
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.VisibleTiles.Should().NotBeEmpty();
        _context.GameState.VisibleTiles.Should().Contain(t => t.IsVisible);
    }

    [Then(@"they see all tiles within (\d+) tiles of \((\d+),(\d+)\)")]
    public void ThenTheySeeAllTilesWithinTilesOf(int range, int x, int y)
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.VisibleTiles.Should().NotBeEmpty();

        foreach (var tile in _context.GameState.VisibleTiles.Where(t => t.IsVisible))
        {
            var distance = Math.Sqrt(Math.Pow(tile.Position.X - x, 2) + Math.Pow(tile.Position.Y - y, 2));
            distance.Should().BeLessOrEqualTo(range);
        }
    }

    [Then(@"they see all entities within (\d+) tiles of \((\d+),(\d+)\)")]
    public void ThenTheySeeAllEntitiesWithinTilesOf(int range, int x, int y)
    {
        _context.GameState.Should().NotBeNull();

        foreach (var unit in _context.GameState!.Units)
        {
            var distance = Math.Sqrt(Math.Pow(unit.Position.X - x, 2) + Math.Pow(unit.Position.Y - y, 2));
            distance.Should().BeLessOrEqualTo(range);
        }
    }

    [Then(@"they do not see tiles at distance (\d+) or greater from their units")]
    public void ThenTheyDoNotSeeTilesAtDistanceOrGreaterFromTheirUnits(int minDistance)
    {
        _context.GameState.Should().NotBeNull();

        var playerUnitPositions = _context.GameState!.Units
            .Where(u => u.PlayerId == _context.Player1Id)
            .Select(u => u.Position)
            .ToList();

        playerUnitPositions.Should().NotBeEmpty();

        foreach (var tile in _context.GameState.VisibleTiles.Where(t => t.IsVisible))
        {
            var minDistanceToAnyUnit = playerUnitPositions
                .Min(pos => Math.Sqrt(Math.Pow(tile.Position.X - pos.X, 2) + Math.Pow(tile.Position.Y - pos.Y, 2)));

            minDistanceToAnyUnit.Should().BeLessThan(minDistance);
        }
    }

    [Then(@"player (\d+) units are not included in the response")]
    public void ThenPlayerUnitsAreNotIncludedInTheResponse(int playerNumber)
    {
        var playerId = playerNumber == 1 ? _context.Player1Id : _context.Player2Id;

        _context.GameState.Should().NotBeNull();
        _context.GameState!.Units.Should().NotContain(u => u.PlayerId == playerId);
    }

    [Then(@"the response only includes player 1 units and visible neutral entities")]
    public void ThenTheResponseOnlyIncludesPlayer1UnitsAndVisibleNeutralEntities()
    {
        _context.GameState.Should().NotBeNull();
        _context.GameState!.Units.Should().OnlyContain(u => u.PlayerId == _context.Player1Id);
    }

    [Then(@"the system rejects the request with 401 Unauthorized")]
    public void ThenTheSystemRejectsTheRequestWith401Unauthorized()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Then(@"the error message indicates authentication failure")]
    public async Task ThenTheErrorMessageIndicatesAuthenticationFailure()
    {
        _context.LastResponse.Should().NotBeNull();
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();
    }

    private string GenerateJwtToken(Guid playerId)
    {
        var secretKey = "default-secret-key-for-development-only-min-32-chars";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "NetRts",
            audience: "NetRts-Clients",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
