using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;
using NetRts.Infrastructure.Services;
using MediatR;
using NSubstitute;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using Xunit;

using Unit = NetRts.Domain.Entities.Unit;

namespace NetRts.UnitTests.Infrastructure.Services;

public class GameTickProcessorVictoryTests
{
    private readonly ILogger<GameTickProcessor> _logger;
    private readonly GameStateCache _gameStateCache;
    private readonly IMatchRepository _matchRepository;
    private readonly ICommandQueueManager _commandQueueManager;
    private readonly IGameUpdateBroadcaster _gameUpdateBroadcaster;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GameTickProcessor _processor;

    public GameTickProcessorVictoryTests()
    {
        _logger = Substitute.For<ILogger<GameTickProcessor>>();
        _gameStateCache = new GameStateCache();
        _matchRepository = Substitute.For<IMatchRepository>();
        _commandQueueManager = Substitute.For<ICommandQueueManager>();
        _commandQueueManager.DequeueCommandsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(new List<Command>());
        _gameUpdateBroadcaster = Substitute.For<IGameUpdateBroadcaster>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(IMatchRepository)).Returns(_matchRepository);
        scope.ServiceProvider.GetService(typeof(IMediator)).Returns(Substitute.For<IMediator>());
        _scopeFactory.CreateScope().Returns(scope);

        _processor = new GameTickProcessor(
            _logger,
            _gameStateCache,
            _commandQueueManager,
            _gameUpdateBroadcaster,
            _scopeFactory);
    }

    [Fact]
    public async Task ProcessMatchTickAsync_ShouldEndMatch_WhenCommandCenterDestroyed()
    {
        // Arrange
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var match = new Match(p1Id, p2Id, GameSettings.Default);
        match.Start();
        
        _gameStateCache.SetMatch(match.Id, match);
        
        var buildings = new List<Building>
        {
            new Building(1, match.Id, p1Id, BuildingType.CommandCenter, new Position(10, 10)),
            new Building(2, match.Id, p2Id, BuildingType.CommandCenter, new Position(90, 90))
        };
        buildings[0].AdvanceConstruction(100);
        buildings[1].AdvanceConstruction(100);
        
        // Destroy player 2's Command Center
        buildings[1].TakeDamage(buildings[1].MaxHealthPoints);
        
        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
        _gameStateCache.SetUnitsForMatch(match.Id, new List<Unit>());
        _gameStateCache.SetMapTilesForMatch(match.Id, new List<MapTile>());
        _gameStateCache.SetResourceDepositsForMatch(match.Id, new List<ResourceDeposit>());
        _gameStateCache.SetUpgradesForMatch(match.Id, new List<Upgrade>());

        // Act
        await _processor.ProcessMatchTickAsync(match.Id);

        // Assert
        match.Status.Should().Be(MatchStatus.Completed);
        match.WinnerId.Should().Be(p1Id);
        await _matchRepository.Received().UpdateAsync(match, Arg.Any<CancellationToken>());
        await _gameUpdateBroadcaster.Received().BroadcastMatchEndedAsync(match.Id, p1Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMatchTickAsync_ShouldEndMatch_WhenTimeLimitReached()
    {
        // Arrange
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        // Set small time limit (constrained by GameSettings to min 600)
        var settings = new GameSettings(100, 100, 600, 500); 
        var match = new Match(p1Id, p2Id, settings);
        match.Start();
        
        // Fast-forward to just before limit
        // CurrentTick will be 600 after this
        for (int i = 0; i < 600; i++) match.AdvanceTick();
        
        _gameStateCache.SetMatch(match.Id, match);
        
        // Player 1 has units, Player 2 has none
        var units = new List<Unit>
        {
            new Unit(1, match.Id, p1Id, UnitType.Worker, new Position(0, 0))
        };
        
        _gameStateCache.SetUnitsForMatch(match.Id, units);
        _gameStateCache.SetBuildingsForMatch(match.Id, new List<Building>());
        _gameStateCache.SetMapTilesForMatch(match.Id, new List<MapTile>());
        _gameStateCache.SetResourceDepositsForMatch(match.Id, new List<ResourceDeposit>());
        _gameStateCache.SetUpgradesForMatch(match.Id, new List<Upgrade>());

        // Act
        // This will advance tick to 601, which is >= 600
        await _processor.ProcessMatchTickAsync(match.Id);

        // Assert
        match.CurrentTick.Should().Be(601);
        match.Status.Should().Be(MatchStatus.Completed);
        match.WinnerId.Should().Be(p1Id);
        await _gameUpdateBroadcaster.Received().BroadcastMatchEndedAsync(match.Id, p1Id, Arg.Any<CancellationToken>());
    }
}