using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class MatchScoringSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;

    public MatchScoringSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"player 2 has a ""(.*)"" at position \((\d+),\s*(\d+)\)")]
    public void GivenPlayerHasABuildingAtPosition(string buildingType, int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        // Replace existing building with ID 2 (CommandCenter) if needed, or just ensure it exists
        var building = buildings.FirstOrDefault(b => b.Id == 2 && b.OwnerId == _context.Player2Id);
        
        if (building == null)
        {
            building = new Building(2, _context.MatchId, _context.Player2Id, Enum.Parse<BuildingType>(buildingType), new Position(x, y));
            building.AdvanceConstruction(100);
            buildings.Add(building);
        }
        else
        {
            // Ensure it's the right type and operational
            // Building properties are private set, so we might need a new object if type differs
            if (building.Type.ToString() != buildingType)
            {
                buildings.Remove(building);
                building = new Building(2, _context.MatchId, _context.Player2Id, Enum.Parse<BuildingType>(buildingType), new Position(x, y));
                building.AdvanceConstruction(100);
                buildings.Add(building);
            }
        }
        
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);
    }

    [Given(@"a match has reached the maximum tick limit")]
    public async Task GivenAMatchHasReachedTheMaximumTickLimit()
    {
        await _scenarioContext.ScenarioContainer.Resolve<GameStateSteps>().GivenANewMatchHasStartedWithTwoPlayers();
        
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        
        var match = gameStateCache.GetMatch(_context.MatchId);
        
        // Fast-forward to max ticks
        for (int i = 0; i < match!.MaxTicksPerMatch; i++)
        {
            match.AdvanceTick();
        }
        
        gameStateCache.AddOrUpdate(match);
    }

    [Given(@"player 1 has a higher total score than player 2")]
    public void GivenPlayer1HasAHigherTotalScoreThanPlayer2()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        
        var match = gameStateCache.GetMatch(_context.MatchId);
        
        // Give player 1 some points
        match!.UpdateResources(_context.Player1Id, 1000); // 1 point per 10 resources = 100 points
        
        gameStateCache.AddOrUpdate(match);
    }

    [When(@"player 1's units destroy player 2's ""(.*)""")]
    public void WhenPlayerUnitsDestroyPlayerBuilding(string buildingType)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var target = buildings.FirstOrDefault(b => b.OwnerId == _context.Player2Id && b.Type.ToString() == buildingType);
        
        target.Should().NotBeNull();
        target!.TakeDamage(target.MaxHealthPoints); // Destroy it
        
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);
    }

    [When(@"player 1 requests the match result")]
    public async Task WhenPlayerRequestsTheMatchResult()
    {
        _context.SetAuthToken(_context.Player1Token, _context.Player1Id);
        
        _context.LastResponse = await _context.HttpClient.GetAsync($"/api/v1/matches/{_context.MatchId}/result");
    }

    [Then(@"the match status is ""(.*)""")]
    public void ThenTheMatchStatusIs(string expectedStatus)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        
        var match = gameStateCache.GetMatch(_context.MatchId);
        match.Should().NotBeNull();
        match!.Status.ToString().Should().Be(expectedStatus);
    }

    [Then(@"player 1 is the winner")]
    public void ThenPlayer1IsTheWinner()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        
        var match = gameStateCache.GetMatch(_context.MatchId);
        match!.WinnerId.Should().Be(_context.Player1Id);
    }

    [Then(@"the match result is available with a valid score")]
    public async Task ThenTheMatchResultIsAvailableWithAValidScore()
    {
        await WhenPlayerRequestsTheMatchResult();
        
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await _context.LastResponse.Content.ReadFromJsonAsync<MatchResultResponse>();
        
        result.Should().NotBeNull();
        result!.WinnerId.Should().Be(_context.Player1Id);
        result.PlayerResults.Should().HaveCount(2);
    }

    [Then(@"the match result shows player 1 has a higher score")]
    public async Task ThenTheMatchResultShowsPlayer1HasAHigherScore()
    {
        await WhenPlayerRequestsTheMatchResult();
        
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await _context.LastResponse.Content.ReadFromJsonAsync<MatchResultResponse>();
        
        var p1Score = result!.PlayerResults.First(p => p.PlayerId == _context.Player1Id).TotalScore;
        var p2Score = result!.PlayerResults.First(p => p.PlayerId == _context.Player2Id).TotalScore;
        
        p1Score.Should().BeGreaterThan(p2Score);
    }

    [Then(@"the request fails with status (\d+) Conflict")]
    public void ThenTheRequestFailsWithStatusConflict(int statusCode)
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be((HttpStatusCode)statusCode);
    }

    [Then(@"the error indicates ""(.*)""")]
    public async Task ThenTheErrorIndicates(string expectedError)
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        
        var errorCode = expectedError.Replace(" ", "_").ToUpperInvariant();
        
        if (!content.Contains(errorCode, StringComparison.OrdinalIgnoreCase) && 
            !content.Contains(expectedError, StringComparison.OrdinalIgnoreCase))
        {
             content.Should().ContainEquivalentOf(expectedError);
        }
    }
}
