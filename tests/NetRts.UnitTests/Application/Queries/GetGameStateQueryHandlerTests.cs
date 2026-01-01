using FluentAssertions;
using NSubstitute;
using NetRts.Application.Interfaces;
using NetRts.Application.Queries.GetGameState;
using NetRts.Application.Services;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.UnitTests.Application.Queries;

public class GetGameStateQueryHandlerTests
{
    private readonly IGameStateCache _gameStateCache;
    private readonly IFogOfWarCalculator _fogOfWarCalculator;
    private readonly IApplicationDbContext _dbContext;
    private readonly GetGameStateQueryHandler _handler;

    public GetGameStateQueryHandlerTests()
    {
        _gameStateCache = Substitute.For<IGameStateCache>();
        _fogOfWarCalculator = Substitute.For<IFogOfWarCalculator>();
        _dbContext = Substitute.For<IApplicationDbContext>();
        _handler = new GetGameStateQueryHandler(_gameStateCache, _fogOfWarCalculator, _dbContext);
    }

    [Fact]
    public async Task Handle_WithValidMatch_ReturnsGameStateResponse()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10))
        };

        var buildings = new List<Building>
        {
            new Building(1, matchId, player1Id, BuildingType.CommandCenter, new Position(10, 10))
        };

        var resourceDeposits = new List<ResourceDeposit>
        {
            new ResourceDeposit(1, matchId, new Position(50, 50), 5000)
        };

        var mapTiles = new List<MapTile>
        {
            new MapTile(matchId, new Position(10, 10), TerrainType.Passable)
        };

        var visiblePositions = new HashSet<Position>
        {
            new Position(10, 10),
            new Position(11, 11)
        };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(resourceDeposits);
        _gameStateCache.GetMapTilesForMatch(matchId).Returns(mapTiles);
        _fogOfWarCalculator.CalculateVisiblePositions(
            Arg.Any<Guid>(),
            Arg.Any<IEnumerable<Unit>>(),
            Arg.Any<IEnumerable<Building>>())
            .Returns(visiblePositions);

        var query = new GetGameStateQuery(matchId, player1Id);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MatchId.Should().Be(matchId);
        result.PlayerId.Should().Be(player1Id);
        result.Units.Should().NotBeEmpty();
        result.Buildings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithNonParticipant_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var nonParticipantId = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);

        _gameStateCache.GetMatch(matchId).Returns(match);

        var query = new GetGameStateQuery(matchId, nonParticipantId);

        // Act & Assert
        await FluentActions.Invoking(() => _handler.Handle(query, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage($"Player {nonParticipantId} is not a participant in match {matchId}");
    }

    [Fact]
    public async Task Handle_WithNonExistentMatch_ThrowsInvalidOperationException()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        _gameStateCache.GetMatch(matchId).Returns((Match?)null);

        var query = new GetGameStateQuery(matchId, playerId);

        // Act & Assert
        await FluentActions.Invoking(() => _handler.Handle(query, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Match {matchId} not found in cache");
    }

    [Fact]
    public async Task Handle_OnlyReturnsUnitsVisibleToPlayer()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10)),
            new Unit(2, matchId, player2Id, UnitType.Worker, new Position(90, 90)) // Outside vision
        };

        var buildings = new List<Building>();
        var resourceDeposits = new List<ResourceDeposit>();
        var mapTiles = new List<MapTile>();

        var visiblePositions = new HashSet<Position>
        {
            new Position(10, 10) // Only player 1's position is visible
        };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(resourceDeposits);
        _gameStateCache.GetMapTilesForMatch(matchId).Returns(mapTiles);
        _fogOfWarCalculator.CalculateVisiblePositions(
            Arg.Any<Guid>(),
            Arg.Any<IEnumerable<Unit>>(),
            Arg.Any<IEnumerable<Building>>())
            .Returns(visiblePositions);

        var query = new GetGameStateQuery(matchId, player1Id);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Units.Should().HaveCount(1);
        result.Units.Should().OnlyContain(u => u.PlayerId == player1Id);
    }
}
