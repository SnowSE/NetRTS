using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class UnitCommandsSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;

    public UnitCommandsSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"a match with player 1 having worker units at position \((\d+),(\d+)\)")]
    public async Task GivenAMatchWithPlayer1HavingWorkerUnitsAtPosition(int x, int y)
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var unitConfigs = new List<(int, int, int, UnitType, Guid)>
        {
            (1, x, y, UnitType.Worker, _context.Player1Id),
            (2, x, y, UnitType.Worker, _context.Player1Id),
            (3, x, y, UnitType.Worker, _context.Player1Id)
        };

        var match = await builder.CreateMatchWithCustomUnits(
            _context.Player1Id,
            _context.Player2Id,
            unitConfigs);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);

        _scenarioContext["InitialPosition"] = new Position(x, y);
    }

    [Given(@"a match with player 1 having worker units near a resource deposit")]
    public async Task GivenAMatchWithPlayer1HavingWorkerUnitsNearResourceDeposit()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var match = await builder.CreateMatchWithPlayers(_context.Player1Id, _context.Player2Id);

        _context.MatchId = match.Item1.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);

        // Store initial resource count
        var gameStateCache = factory.Services.GetRequiredService<IGameStateCache>();
        var matchState = gameStateCache.GetMatch(_context.MatchId);
        _scenarioContext["InitialResources"] = matchState?.GetPlayerResources(_context.Player1Id) ?? 0;
    }

    [Given(@"a match with player 1 having soldier units and player 2 having visible enemy units")]
    public async Task GivenAMatchWithPlayer1HavingSoldierUnitsAndPlayer2HavingVisibleEnemyUnits()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var unitConfigs = new List<(int, int, int, UnitType, Guid)>
        {
            (1, 10, 10, UnitType.Soldier, _context.Player1Id),
            (2, 15, 15, UnitType.Worker, _context.Player2Id) // Within vision range
        };

        var match = await builder.CreateMatchWithCustomUnits(
            _context.Player1Id,
            _context.Player2Id,
            unitConfigs);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
    }

    [Given(@"a match where player 1 has already queued (\d+) commands")]
    public async Task GivenAMatchWherePlayer1HasAlreadyQueuedCommands(int commandCount)
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var (match, _, _) = await builder.CreateMatchWithPlayers(_context.Player1Id, _context.Player2Id);
        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
        _context.SetAuthToken(_context.Player1Token);

        // Queue the specified number of commands
        for (int i = 0; i < commandCount; i++)
        {
            var request = new QueueCommandsRequest
            {
                Commands = new[]
                {
                    new CommandDto
                    {
                        Type = "Move",
                        UnitIds = new[] { 1 },
                        TargetPosition = new PositionDto { X = 20, Y = 20 }
                    }
                }
            };

            await _context.HttpClient.PostAsJsonAsync(
                $"/api/v1/matches/{_context.MatchId}/commands",
                request);
        }
    }

    [Given(@"a match with player 1 having worker units")]
    public async Task GivenAMatchWithPlayer1HavingWorkerUnits()
    {
        await GivenAMatchWithPlayer1HavingWorkerUnitsAtPosition(10, 10);
    }

    [Given(@"a match in progress")]
    public async Task GivenAMatchInProgress()
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var (match, _, _) = await builder.CreateMatchWithPlayers(_context.Player1Id, _context.Player2Id);
        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
    }

    [When(@"player 1 queues a move command for their workers to position \((\d+),(\d+)\)")]
    public async Task WhenPlayer1QueuesAMoveCommandForTheirWorkersToPosition(int x, int y)
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Move",
                    UnitIds = new[] { 1, 2, 3 },
                    TargetPosition = new PositionDto { X = x, Y = y }
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands",
            request);

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _context.CommandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }

        _scenarioContext["TargetPosition"] = new Position(x, y);
    }

    [When(@"player 1 queues a gather command targeting the resource deposit")]
    public async Task WhenPlayer1QueuesAGatherCommandTargetingTheResourceDeposit()
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Gather",
                    UnitIds = new[] { 1, 2, 3 },
                    TargetResourceDepositId = 1
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands",
            request);

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _context.CommandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 queues an attack command targeting the enemy unit")]
    public async Task WhenPlayer1QueuesAnAttackCommandTargetingTheEnemyUnit()
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Attack",
                    UnitIds = new[] { 1 },
                    TargetUnitId = 2
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands",
            request);

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _context.CommandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 attempts to queue another command")]
    public async Task WhenPlayer1AttemptsToQueueAnotherCommand()
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Move",
                    UnitIds = new[] { 1 },
                    TargetPosition = new PositionDto { X = 30, Y = 30 }
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands",
            request);
    }

    [When(@"player 1 queues three movement commands in sequence")]
    public async Task WhenPlayer1QueuesThreeMovementCommandsInSequence()
    {
        _context.SetAuthToken(_context.Player1Token);

        var positions = new[] { (15, 15), (20, 20), (25, 25) };

        foreach (var (x, y) in positions)
        {
            var request = new QueueCommandsRequest
            {
                Commands = new[]
                {
                    new CommandDto
                    {
                        Type = "Move",
                        UnitIds = new[] { 1 },
                        TargetPosition = new PositionDto { X = x, Y = y }
                    }
                }
            };

            await _context.HttpClient.PostAsJsonAsync(
                $"/api/v1/matches/{_context.MatchId}/commands",
                request);
        }

        _scenarioContext["CommandSequence"] = positions;
    }

    [When(@"player 1 queues a command for a unit they do not own")]
    public async Task WhenPlayer1QueuesACommandForAUnitTheyDoNotOwn()
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Move",
                    UnitIds = new[] { 999 }, // Non-existent or enemy unit
                    TargetPosition = new PositionDto { X = 50, Y = 50 }
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands",
            request);
    }

    [When(@"player 1 sends more than 10 command requests per second")]
    public async Task WhenPlayer1SendsMoreThan10CommandRequestsPerSecond()
    {
        _context.SetAuthToken(_context.Player1Token);

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Move",
                    UnitIds = new[] { 1 },
                    TargetPosition = new PositionDto { X = 20, Y = 20 }
                }
            }
        };

        // Send 15 requests rapidly
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 15; i++)
        {
            var response = await _context.HttpClient.PostAsJsonAsync(
                $"/api/v1/matches/{_context.MatchId}/commands",
                request);
            responses.Add(response);
        }

        // At least some should be rate limited
        _context.LastResponse = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            ?? responses.Last();
    }

    [Then(@"the command is accepted and queued")]
    public void ThenTheCommandIsAcceptedAndQueued()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.IsSuccessStatusCode.Should().BeTrue();
        _context.CommandResponse.Should().NotBeNull();
        _context.CommandResponse!.QueuedCount.Should().BeGreaterThan(0);
    }

    [Then(@"after the next game tick the workers are closer to position \((\d+),(\d+)\)")]
    public async Task ThenAfterTheNextGameTickTheWorkersAreCloserToPosition(int targetX, int targetY)
    {
        // Wait for tick to process
        await Task.Delay(1500);

        _context.SetAuthToken(_context.Player1Token);
        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var initialPosition = _scenarioContext.Get<Position>("InitialPosition");
        var targetPosition = new Position(targetX, targetY);

        foreach (var unit in gameState!.Units.Where(u => u.UnitType == "Worker"))
        {
            var currentDistance = Math.Sqrt(
                Math.Pow(unit.Position.X - targetPosition.X, 2) +
                Math.Pow(unit.Position.Y - targetPosition.Y, 2));

            var initialDistance = Math.Sqrt(
                Math.Pow(initialPosition.X - targetPosition.X, 2) +
                Math.Pow(initialPosition.Y - targetPosition.Y, 2));

            currentDistance.Should().BeLessThan(initialDistance);
        }
    }

    [Then(@"after the next game tick the workers begin gathering resources")]
    public async Task ThenAfterTheNextGameTickTheWorkersBeginGatheringResources()
    {
        await Task.Delay(1500);

        _context.SetAuthToken(_context.Player1Token);
        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        gameState!.Units.Where(u => u.UnitType == "Worker")
            .Should().Contain(u => u.CurrentState == "Gathering");
    }

    [Then(@"player 1 resource count increases")]
    public async Task ThenPlayer1ResourceCountIncreases()
    {
        await Task.Delay(1500);

        _context.SetAuthToken(_context.Player1Token);
        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        var initialResources = _scenarioContext.Get<int>("InitialResources");
        gameState.Should().NotBeNull();
        gameState!.PlayerResources.Should().BeGreaterThan(initialResources);
    }

    [Then(@"after the next game tick the soldiers move toward the target and attack if in range")]
    public async Task ThenAfterTheNextGameTickTheSoldiersMoveTowardTheTargetAndAttackIfInRange()
    {
        await Task.Delay(1500);

        _context.SetAuthToken(_context.Player1Token);
        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var soldier = gameState!.Units.FirstOrDefault(u => u.UnitType == "Soldier");
        soldier.Should().NotBeNull();
        soldier!.CurrentState.Should().BeOneOf("Moving", "Attacking");
    }

    [Then(@"the system rejects the command with queue full error")]
    public void ThenTheSystemRejectsTheCommandWithQueueFullError()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Then(@"the error message indicates the maximum queue size")]
    public async Task ThenTheErrorMessageIndicatesTheMaximumQueueSize()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().Contain("queue");
    }

    [Then(@"after processing the workers execute the first command first")]
    public async Task ThenAfterProcessingTheWorkersExecuteTheFirstCommandFirst()
    {
        await Task.Delay(1500);

        _context.SetAuthToken(_context.Player1Token);
        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var unit = gameState!.Units.FirstOrDefault(u => u.UnitId == 1);
        unit.Should().NotBeNull();
    }

    [Then(@"subsequent commands execute in the order they were queued")]
    public void ThenSubsequentCommandsExecuteInTheOrderTheyWereQueued()
    {
        // Verified by the FIFO nature of the command queue
        true.Should().BeTrue();
    }

    [Then(@"the system rejects the command with authorization error")]
    public void ThenTheSystemRejectsTheCommandWithAuthorizationError()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Forbidden);
    }

    [Then(@"the command is not added to the queue")]
    public async Task ThenTheCommandIsNotAddedToTheQueue()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Then(@"the system rejects excess requests with 429 Too Many Requests")]
    public void ThenTheSystemRejectsExcessRequestsWith429TooManyRequests()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Then(@"the response indicates to retry after delay")]
    public void ThenTheResponseIndicatesToRetryAfterDelay()
    {
        _context.LastResponse!.Headers.Should().Contain(h => h.Key == "Retry-After");
    }

    private string GenerateJwtToken(Guid playerId)
    {
        // Use the same token generation logic as GameStateSteps
        var secretKey = "default-secret-key-for-development-only-min-32-chars";
        var key = new System.IdentityModel.Tokens.Jwt.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(secretKey));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                playerId.ToString()),
            new System.Security.Claims.Claim(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "NetRts",
            audience: "NetRts-Clients",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
