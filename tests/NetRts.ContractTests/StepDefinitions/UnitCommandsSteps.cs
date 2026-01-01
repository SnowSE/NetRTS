using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class UnitCommandsSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;
    private int _workerUnitId;
    private int _soldierUnitId;
    private int _player2UnitId;
    private int _resourceDepositId;
    private QueueCommandsResponse? _commandResponse;

    public UnitCommandsSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"a match with player 1 controlling worker units at position \((\d+),(\d+)\)")]
    [Given(@"a match with player 1 worker at position \((\d+),(\d+)\)")]
    public async Task GivenAMatchWithPlayer1WorkerAtPosition(int x, int y)
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
        _context.Player2Token = GenerateJwtToken(_context.Player2Id);
        _workerUnitId = 1;
    }

    [Given(@"a match with player 1 soldier at position \((\d+),(\d+)\)")]
    public async Task GivenAMatchWithPlayer1SoldierAtPosition(int x, int y)
    {
        _context.Player1Id = Guid.NewGuid();
        _context.Player2Id = Guid.NewGuid();

        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        var unitConfigs = new List<(int, int, int, UnitType, Guid)>
        {
            (1, x, y, UnitType.Soldier, _context.Player1Id)
        };

        var match = await builder.CreateMatchWithCustomUnits(
            _context.Player1Id,
            _context.Player2Id,
            unitConfigs);

        _context.MatchId = match.Id;
        _context.Player1Token = GenerateJwtToken(_context.Player1Id);
        _context.Player2Token = GenerateJwtToken(_context.Player2Id);
        _soldierUnitId = 1;
    }

    [Given(@"a resource deposit at position \((\d+),(\d+)\)")]
    public async Task GivenAResourceDepositAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        _resourceDepositId = await builder.AddResourceDeposit(_context.MatchId, x, y, 5000);
    }

    [Given(@"player 2 unit at position \((\d+),(\d+)\)")]
    public async Task GivenPlayer2UnitAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var builder = new MatchTestDataBuilder(factory.Services);

        _player2UnitId = await builder.AddUnit(_context.MatchId, _context.Player2Id, UnitType.Worker, x, y);
    }

    [Given(@"a match with player 1 and player 2")]
    public async Task GivenAMatchWithPlayer1AndPlayer2()
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

        // Get a player 2 unit ID
        _player2UnitId = units.First(u => u.OwnerId == _context.Player2Id).Id;
    }

    [Given(@"a match with player 1 units")]
    public async Task GivenAMatchWithPlayer1Units()
    {
        await GivenAMatchWithPlayer1WorkerAtPosition(10, 10);
    }

    [Given(@"player 1 has (\d+) commands already queued")]
    public async Task GivenPlayer1HasCommandsAlreadyQueued(int commandCount)
    {
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        // Queue the specified number of commands
        for (int i = 0; i < commandCount; i++)
        {
            var request = new QueueCommandsRequest
            {
                Commands = new[]
                {
                    new CommandDto
                    {
                        CommandType = "Move",
                        UnitIds = new[] { _workerUnitId },
                        TargetPosition = new PositionDto { X = 15 + (i % 10), Y = 15 + (i / 10) }
                    }
                }
            };

            var response = await _context.HttpClient.PostAsJsonAsync(
                $"/api/v1/matches/{_context.MatchId}/commands", request);

            // Stop if we hit the limit
            if (!response.IsSuccessStatusCode)
            {
                break;
            }
        }
    }

    [When(@"player 1 queues a move command to position \((-?\d+),(-?\d+)\)")]
    public async Task WhenPlayer1QueuesAMoveCommandToPosition(int x, int y)
    {
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Move",
                    UnitIds = new[] { _workerUnitId },
                    TargetPosition = new PositionDto { X = x, Y = y }
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands", request);

        _context.LastStatusCode = _context.LastResponse.StatusCode;
        _context.LastResponseBody = await _context.LastResponse.Content.ReadAsStringAsync();

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _commandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 queues a gather command targeting the resource deposit")]
    public async Task WhenPlayer1QueuesAGatherCommandTargetingTheResourceDeposit()
    {
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Gather",
                    UnitIds = new[] { _workerUnitId },
                    TargetResourceId = _resourceDepositId
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands", request);

        _context.LastStatusCode = _context.LastResponse.StatusCode;
        _context.LastResponseBody = await _context.LastResponse.Content.ReadAsStringAsync();

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _commandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 queues an attack command targeting player 2 unit")]
    public async Task WhenPlayer1QueuesAnAttackCommandTargetingPlayer2Unit()
    {
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Attack",
                    UnitIds = new[] { _soldierUnitId },
                    TargetUnitId = _player2UnitId
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands", request);

        _context.LastStatusCode = _context.LastResponse.StatusCode;
        _context.LastResponseBody = await _context.LastResponse.Content.ReadAsStringAsync();

        if (_context.LastResponse.IsSuccessStatusCode)
        {
            _commandResponse = await _context.LastResponse.Content
                .ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 attempts to queue a command for player 2 units")]
    public async Task WhenPlayer1AttemptsToQueueACommandForPlayer2Units()
    {
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    CommandType = "Move",
                    UnitIds = new[] { _player2UnitId },
                    TargetPosition = new PositionDto { X = 50, Y = 50 }
                }
            }
        };

        _context.LastResponse = await _context.HttpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_context.MatchId}/commands", request);

        _context.LastStatusCode = _context.LastResponse.StatusCode;
        _context.LastResponseBody = await _context.LastResponse.Content.ReadAsStringAsync();
    }

    [When(@"player 1 attempts to queue another command")]
    public async Task WhenPlayer1AttemptsToQueueAnotherCommand()
    {
        await WhenPlayer1QueuesAMoveCommandToPosition(20, 20);
    }

    [Then(@"the command is accepted and queued")]
    public void ThenTheCommandIsAcceptedAndQueued()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
        _commandResponse.Should().NotBeNull();
        _commandResponse!.QueuedCount.Should().BeGreaterThan(0);
    }

    [Then(@"after the next game tick the worker has moved toward \((\d+),(\d+)\)")]
    public async Task ThenAfterTheNextGameTickTheWorkerHasMovedToward(int targetX, int targetY)
    {
        // Check queue size before processing tick (diagnostic)
        var queueSizeBefore = await _context.GetQueueSizeAsync(_context.Player1Id);
        Console.WriteLine($"Queue size before tick: {queueSizeBefore}");

        // Manually process game tick instead of waiting for background service
        await _context.ProcessGameTickAsync();

        // Check queue size after processing tick (diagnostic)
        var queueSizeAfter = await _context.GetQueueSizeAsync(_context.Player1Id);
        Console.WriteLine($"Queue size after tick: {queueSizeAfter}");

        // Get updated game state
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var worker = gameState!.Units.FirstOrDefault(u => u.UnitId == _workerUnitId);
        worker.Should().NotBeNull();

        Console.WriteLine($"Worker status after tick: {worker!.CurrentState}, Position: ({worker.Position.X}, {worker.Position.Y})");

        // Worker should have moved closer to target or reached it
        var originalDistance = Math.Sqrt(Math.Pow(10 - targetX, 2) + Math.Pow(10 - targetY, 2));
        var currentDistance = Math.Sqrt(Math.Pow(worker!.Position.X - targetX, 2) + Math.Pow(worker.Position.Y - targetY, 2));

        currentDistance.Should().BeLessThan(originalDistance, "worker should have moved toward target");
    }

    [Then(@"after the next game tick the worker is moving toward the deposit")]
    public async Task ThenAfterTheNextGameTickTheWorkerIsMovingTowardTheDeposit()
    {
        // Manually process game tick instead of waiting for background service
        await _context.ProcessGameTickAsync();

        // Get updated game state
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var worker = gameState!.Units.FirstOrDefault(u => u.UnitId == _workerUnitId);
        worker.Should().NotBeNull();
        worker!.CurrentState.Should().BeOneOf("Moving", "Gathering");
    }

    [Then(@"after the next game tick the soldier is attacking the target")]
    public async Task ThenAfterTheNextGameTickTheSoldierIsAttackingTheTarget()
    {
        // Manually process game tick instead of waiting for background service
        await _context.ProcessGameTickAsync();

        // Get updated game state
        _context.SetAuthToken(_context.Player1Token);
        _context.HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
        _context.HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", _context.Player1Id.ToString());

        var response = await _context.HttpClient.GetAsync(
            $"/api/v1/matches/{_context.MatchId}/state");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var gameState = await response.Content.ReadFromJsonAsync<GameStateResponse>();

        gameState.Should().NotBeNull();
        var soldier = gameState!.Units.FirstOrDefault(u => u.UnitId == _soldierUnitId);
        soldier.Should().NotBeNull();
        soldier!.CurrentState.Should().BeOneOf("Moving", "Attacking");
    }

    [Then(@"the command is rejected with authorization error")]
    public void ThenTheCommandIsRejectedWithAuthorizationError()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);
    }

    [Then(@"the command is rejected with validation error")]
    public void ThenTheCommandIsRejectedWithValidationError()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Then(@"the command is rejected with queue full error")]
    public void ThenTheCommandIsRejectedWithQueueFullError()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests);
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
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
