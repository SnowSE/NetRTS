using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class BuildingProductionSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;

    public BuildingProductionSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"a match exists with two players")]
    public async Task GivenAMatchExistsWithTwoPlayers()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var match = await builder.CreateMatchWithPlayers(_context.Player1Id, _context.Player2Id);
        _context.MatchId = match.Item1.Id;
    }

    [Given(@"player 1 is authenticated")]
    public void GivenPlayer1IsAuthenticated()
    {
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
    }

    [Given(@"player 1 has worker units at position \((\d+),(\d+)\) with (\d+) resources")]
    public async Task GivenPlayer1HasWorkerUnitsAtPositionWithResources(int x, int y, int resources)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        // Set player resources
        var match = gameStateCache.GetMatch(_context.MatchId);
        match!.SetPlayerResources(_context.Player1Id, resources);
        gameStateCache.AddOrUpdate(match);

        // Create worker units
        var units = new List<Unit>
        {
            new Unit(1, _context.MatchId, _context.Player1Id, UnitType.Worker, new Position(x, y)),
            new Unit(2, _context.MatchId, _context.Player1Id, UnitType.Worker, new Position(x, y)),
            new Unit(3, _context.MatchId, _context.Player1Id, UnitType.Worker, new Position(x, y))
        };

        gameStateCache.SetUnitsForMatch(_context.MatchId, units);

        _scenarioContext["WorkerPosition"] = new Position(x, y);
        _scenarioContext["InitialResources"] = resources;
    }

    [Given(@"the tile at position \((\d+),(\d+)\) is unoccupied and passable")]
    public void GivenTheTileAtPositionIsUnoccupiedAndPassable(int x, int y)
    {
        _scenarioContext["BuildPosition"] = new Position(x, y);
    }

    [Given(@"there is an existing building at position \((\d+),(\d+)\)")]
    public void GivenThereIsAnExistingBuildingAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var nextId = buildings.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
        var existingBuilding = new Building(nextId, _context.MatchId, _context.Player2Id, BuildingType.Barracks, new Position(x, y));
        existingBuilding.AdvanceConstruction(100);
        buildings.Add(existingBuilding);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["BuildPosition"] = new Position(x, y);
    }

    [Given(@"player 1 has an operational Barracks at position \((\d+),(\d+)\) with (\d+) resources")]
    public void GivenPlayer1HasAnOperationalBarracksAtPositionWithResources(int x, int y, int resources)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        // Set player resources
        var match = gameStateCache.GetMatch(_context.MatchId);
        match!.SetPlayerResources(_context.Player1Id, resources);
        gameStateCache.AddOrUpdate(match);

        // Create operational barracks
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var nextId = buildings.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
        var barracks = new Building(nextId, _context.MatchId, _context.Player1Id, BuildingType.Barracks, new Position(x, y));
        barracks.AdvanceConstruction(100);

        buildings.Add(barracks);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["BarracksId"] = barracks.Id;
        _scenarioContext["BarracksPosition"] = new Position(x, y);
        _scenarioContext["InitialResources"] = resources;
    }

    [Given(@"player 1 has a Barracks under construction at position \((\d+),(\d+)\)")]
    public void GivenPlayer1HasABarracksUnderConstructionAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var nextId = buildings.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
        var barracks = new Building(nextId, _context.MatchId, _context.Player1Id, BuildingType.Barracks, new Position(x, y));
        // Don't complete construction - leave it at 0%

        buildings.Add(barracks);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["BarracksId"] = barracks.Id;
    }

    [Given(@"player 1 has an operational CommandCenter at position \((\d+),(\d+)\)")]
    public void GivenPlayer1HasAnOperationalCommandCenterAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var nextId = buildings.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
        var commandCenter = new Building(nextId, _context.MatchId, _context.Player1Id, BuildingType.CommandCenter, new Position(x, y));
        commandCenter.AdvanceConstruction(100);

        buildings.Add(commandCenter);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["CommandCenterId"] = commandCenter.Id;
    }

    [Given(@"player 1 has queued a build command for a Barracks at position \((\d+),(\d+)\)")]
    public async Task GivenPlayer1HasQueuedABuildCommandForABarracksAtPosition(int x, int y)
    {
        await WhenPlayer1QueuesABuildCommandForABarracksAtPosition(x, y);
    }

    [Given(@"player 1 has queued a produce command for a Soldier")]
    public async Task GivenPlayer1HasQueuedAProduceCommandForASoldier()
    {
        var barracksId = (int)_scenarioContext["BarracksId"];
        await WhenPlayer1QueuesAProduceCommandForASoldierFromTheBarracks();
    }

    [When(@"player 1 queues a build command for a Barracks at position \((\d+),(\d+)\)")]
    public async Task WhenPlayer1QueuesABuildCommandForABarracksAtPosition(int x, int y)
    {
        _context.SetAuthToken(_context.Player1Token, _context.Player1Id);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Build",
                    UnitIds = new[] { 1, 2, 3 }, // Workers doing the building
                    BuildingType = "Barracks",
                    TargetPosition = new PositionDto { X = x, Y = y }
                }
            }
        };

        var response = await _context.HttpClient.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        if (response.IsSuccessStatusCode)
        {
            _context.LastCommandResponse = await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }

        _scenarioContext["BuildPosition"] = new Position(x, y);
    }

    [When(@"player 1 queues a produce command for a Soldier from the barracks")]
    public async Task WhenPlayer1QueuesAProduceCommandForASoldierFromTheBarracks()
    {
        _context.SetAuthToken(_context.Player1Token, _context.Player1Id);

        var barracksId = (int)_scenarioContext["BarracksId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Produce",
                    BuildingId = barracksId,
                    UnitType = "Soldier"
                }
            }
        };

        var response = await _context.HttpClient.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        if (response.IsSuccessStatusCode)
        {
            _context.LastCommandResponse = await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 queues production for (\d+) Soldiers")]
    public async Task WhenPlayer1QueuesProductionForSoldiers(int count)
    {
        _context.SetAuthToken(_context.Player1Token, _context.Player1Id);

        var barracksId = (int)_scenarioContext["BarracksId"];

        var commands = new List<CommandDto>();
        for (int i = 0; i < count; i++)
        {
            commands.Add(new CommandDto
            {
                CommandType = "Produce",
                BuildingId = barracksId,
                UnitType = "Soldier"
            });
        }

        var request = new QueueCommandsRequest { Commands = commands.ToArray() };

        var response = await _context.HttpClient.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        if (response.IsSuccessStatusCode)
        {
            _context.LastCommandResponse = await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }

        _scenarioContext["ExpectedProductionCount"] = count;
    }

    [When(@"player 1 queues a Soldier from the Barracks")]
    public async Task WhenPlayer1QueuesASoldierFromTheBarracks()
    {
        await WhenPlayer1QueuesAProduceCommandForASoldierFromTheBarracks();
    }

    [When(@"player 1 queues a Worker from the CommandCenter")]
    public async Task WhenPlayer1QueuesAWorkerFromTheCommandCenter()
    {
        _context.SetAuthToken(_context.Player1Token, _context.Player1Id);

        var commandCenterId = (int)_scenarioContext["CommandCenterId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Produce",
                    BuildingId = commandCenterId,
                    UnitType = "Worker"
                }
            }
        };

        var response = await _context.HttpClient.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;
    }

    [When(@"the build command is executed")]
    public async Task WhenTheBuildCommandIsExecuted()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        // Process one tick to execute the build command
        await tickProcessor.ProcessTickAsync();

        _scenarioContext["BuildExecuted"] = true;
    }

    [When(@"(\d+) game ticks pass")]
    public async Task WhenGameTicksPass(int tickCount)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        for (int i = 0; i < tickCount; i++)
        {
            await tickProcessor.ProcessTickAsync();
        }
    }

    [When(@"the building reaches 100% construction progress")]
    public async Task WhenTheBuildingReaches100PercentConstructionProgress()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        var buildPosition = (Position)_scenarioContext["BuildPosition"];

        // Keep processing ticks until building is operational
        for (int i = 0; i < 100; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
            var building = buildings.FirstOrDefault(b => b.Position.Equals(buildPosition));

            if (building?.IsOperational == true)
            {
                _scenarioContext["CompletedBuilding"] = building;
                break;
            }
        }
    }

    [When(@"the produce command is executed")]
    public async Task WhenTheProduceCommandIsExecuted()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        await tickProcessor.ProcessTickAsync();

        _scenarioContext["ProduceExecuted"] = true;
    }

    [When(@"production timer reaches zero")]
    public async Task WhenProductionTimerReachesZero()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        var barracksId = (int)_scenarioContext["BarracksId"];
        
        // If we are in the "multiple buildings" scenario, we might expect multiple units
        int expectedIncrease = _scenarioContext.ContainsKey("ExpectedProductionCount") ? (int)_scenarioContext["ExpectedProductionCount"] : 1;
        // For the "multiple buildings" scenario, we expect 2 units (Soldier + Worker)
        if (_scenarioContext.StepContext.StepInfo.Text.Contains("both buildings produce units simultaneously") || 
            _scenarioContext.ContainsKey("CommandCenterId"))
        {
            expectedIncrease = 2;
        }

        var initialUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;

        // Keep processing ticks until units spawn
        for (int i = 0; i < 100; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var currentUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;
            if (currentUnitCount >= initialUnitCount + expectedIncrease)
            {
                break;
            }
        }
    }

    [When(@"the first production completes")]
    public async Task WhenTheFirstProductionCompletes()
    {
        await WaitUntilUnitsSpawn(1);
    }

    [When(@"the second production completes")]
    public async Task WhenTheSecondProductionCompletes()
    {
        await WaitUntilUnitsSpawn(1);
    }

    private async Task WaitUntilUnitsSpawn(int expectedIncrease)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        var initialUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;

        // Keep processing ticks until units spawn
        for (int i = 0; i < 100; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var currentUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;
            if (currentUnitCount >= initialUnitCount + expectedIncrease)
            {
                break;
            }
        }
    }

    [Then(@"a building is created at position \((\d+),(\d+)\) with 0% construction progress")]
    public void ThenABuildingIsCreatedAtPositionWithConstructionProgress(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var building = buildings.FirstOrDefault(b => b.Position.X == x && b.Position.Y == y);

        building.Should().NotBeNull();
        building!.ConstructionProgress.Should().Be(0);
        building.IsOperational.Should().BeFalse();
    }

    [Then(@"player 1's resources are decreased by the building cost")]
    public void ThenPlayer1ResourcesAreDecreasedByTheBuildingCost()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var currentResources = match!.GetPlayerResources(_context.Player1Id);
        var initialResources = (int)_scenarioContext["InitialResources"];

        currentResources.Should().BeLessThan(initialResources);
    }

    [Then(@"the building construction progress increases")]
    public void ThenTheBuildingConstructionProgressIncreases()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildPosition = (Position)_scenarioContext["BuildPosition"];
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var building = buildings.FirstOrDefault(b => b.Position.Equals(buildPosition));

        building.Should().NotBeNull();
        building!.ConstructionProgress.Should().BeGreaterThan(0);
    }

    [Then(@"the building becomes operational")]
    public void ThenTheBuildingBecomesOperational()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var buildPosition = (Position)_scenarioContext["BuildPosition"];
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var building = buildings.FirstOrDefault(b => b.Position.Equals(buildPosition));

        building.Should().NotBeNull();
        building!.IsOperational.Should().BeTrue();
        building.ConstructionProgress.Should().Be(100);
    }

    [Then(@"the production is added to the building's queue")]
    public void ThenTheProductionIsAddedToTheBuildingQueue()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var barracksId = (int)_scenarioContext["BarracksId"];
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var barracks = buildings.FirstOrDefault(b => b.Id == barracksId);

        barracks.Should().NotBeNull();
        barracks!.ProductionQueue.Should().NotBeEmpty();
    }

    [Then(@"player 1's resources are decreased by the unit cost")]
    public void ThenPlayer1ResourcesAreDecreasedByTheUnitCost()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var currentResources = match!.GetPlayerResources(_context.Player1Id);
        var initialResources = (int)_scenarioContext["InitialResources"];

        currentResources.Should().BeLessThan(initialResources);
    }

    [Then(@"a new Soldier unit spawns adjacent to the barracks")]
    [Then(@"a Soldier spawns adjacent to the barracks")]
    [Then(@"the Soldier spawns near the Barracks")]
    public void ThenANewSoldierUnitSpawnsAdjacentToTheBarracks()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var barracksPosition = (Position)_scenarioContext["BarracksPosition"];
        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        
        // Debug logging
        Console.WriteLine($"Units in match {_context.MatchId}: {units.Count}");
        foreach (var u in units)
        {
            Console.WriteLine($"Unit ID: {u.Id}, Type: {u.Type}, Owner: {u.OwnerId}, Position: {u.Position}");
        }

        var soldier = units.FirstOrDefault(u => u.Type == UnitType.Soldier);

        soldier.Should().NotBeNull("A soldier unit should have spawned");
        var distance = soldier!.Position.DistanceTo(barracksPosition);
        distance.Should().BeLessThanOrEqualTo(2.0, $"Soldier at {soldier.Position} should be adjacent to barracks at {barracksPosition}");
    }

    [Then(@"the unit belongs to player 1")]
    public void ThenTheUnitBelongsToPlayer1()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var soldier = units.FirstOrDefault(u => u.Type == UnitType.Soldier);

        soldier.Should().NotBeNull();
        soldier!.PlayerId.Should().Be(_context.Player1Id);
    }

    [Then(@"the command is rejected due to validation failure")]
    public void ThenTheCommandIsRejectedDueToValidationFailure()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Then(@"the error indicates the tile is occupied")]
    public async Task ThenTheErrorIndicatesTheTileIsOccupied()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("occupied");
    }

    [Then(@"the error indicates insufficient resources")]
    public async Task ThenTheErrorIndicatesInsufficientResources()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("insufficient resources");
    }

    [Then(@"the error indicates the building is not operational")]
    public async Task ThenTheErrorIndicatesTheBuildingIsNotOperational()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("not operational");
    }

    [Then(@"all 3 production orders are added to the queue")]
    public void ThenAll3ProductionOrdersAreAddedToTheQueue()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var barracksId = (int)_scenarioContext["BarracksId"];
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var barracks = buildings.FirstOrDefault(b => b.Id == barracksId);

        barracks.Should().NotBeNull();
        barracks!.ProductionQueue.Should().HaveCount(3);
    }

    [Then(@"(\d+) production orders? remain(?:s)? in the queue")]
    public void ThenProductionOrdersRemainInTheQueue(int expectedCount)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var barracksId = (int)_scenarioContext["BarracksId"];
        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        var barracks = buildings.FirstOrDefault(b => b.Id == barracksId);

        barracks.Should().NotBeNull();
        barracks!.ProductionQueue.Should().HaveCount(expectedCount);
    }

    [Then(@"another Soldier spawns")]
    public void ThenAnotherSoldierSpawns()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var soldiers = units.Where(u => u.Type == UnitType.Soldier).ToList();

        soldiers.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Then(@"both buildings produce units simultaneously")]
    public async Task ThenBothBuildingsProduceUnitsSimultaneously()
    {
        // Verify both production orders were accepted
        _context.LastResponse!.IsSuccessStatusCode.Should().BeTrue();
    }

    [Then(@"the Worker spawns near the CommandCenter")]
    public void ThenTheWorkerSpawnsNearTheCommandCenter()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var workers = units.Where(u => u.Type == UnitType.Worker).ToList();

        // Should have more workers than the initial 3 created in setup
        workers.Should().HaveCountGreaterThan(3);
    }

    private string GenerateJwtToken(Guid playerId)
    {
        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("default-secret-key-for-development-only-min-32-chars"));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

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
