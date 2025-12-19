using FluentAssertions;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Services;

namespace NetRts.UnitTests.Infrastructure.Services;

public class FogOfWarCalculatorTests
{
    private readonly FogOfWarCalculator _calculator;

    public FogOfWarCalculatorTests()
    {
        _calculator = new FogOfWarCalculator();
    }

    [Fact]
    public void CalculateVisiblePositions_WithUnits_ReturnsPositionsWithinVisionRange()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var unitPosition = new Position(10, 10);

        var units = new List<Unit>
        {
            new Unit(1, matchId, playerId, UnitType.Worker, unitPosition)
        };

        var buildings = new List<Building>();

        // Act
        var visiblePositions = _calculator.CalculateVisiblePositions(playerId, units, buildings);

        // Assert
        visiblePositions.Should().NotBeEmpty();
        visiblePositions.Should().Contain(unitPosition);

        // Worker vision range is 5, so check some positions within range
        visiblePositions.Should().Contain(new Position(11, 11)); // Distance ~1.4
        visiblePositions.Should().Contain(new Position(12, 12)); // Distance ~2.8
    }

    [Fact]
    public void CalculateVisiblePositions_WithBuildings_ReturnsPositionsWithinVisionRange()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var buildingPosition = new Position(20, 20);

        var units = new List<Unit>();

        var buildings = new List<Building>
        {
            new Building(1, matchId, playerId, BuildingType.CommandCenter, buildingPosition)
        };
        buildings[0].AdvanceConstruction(100); // Mark as operational

        // Act
        var visiblePositions = _calculator.CalculateVisiblePositions(playerId, units, buildings);

        // Assert
        visiblePositions.Should().NotBeEmpty();
        visiblePositions.Should().Contain(buildingPosition);
    }

    [Fact]
    public void CalculateVisiblePositions_WithMultipleUnits_CombinesVisionRanges()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var units = new List<Unit>
        {
            new Unit(1, matchId, playerId, UnitType.Worker, new Position(10, 10)),
            new Unit(2, matchId, playerId, UnitType.Worker, new Position(30, 30))
        };

        var buildings = new List<Building>();

        // Act
        var visiblePositions = _calculator.CalculateVisiblePositions(playerId, units, buildings);

        // Assert
        visiblePositions.Should().NotBeEmpty();
        visiblePositions.Should().Contain(new Position(10, 10)); // First unit position
        visiblePositions.Should().Contain(new Position(30, 30)); // Second unit position
    }

    [Fact]
    public void CalculateVisiblePositions_IgnoresUnconstructedBuildings()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var buildingPosition = new Position(20, 20);

        var units = new List<Unit>();

        var buildings = new List<Building>
        {
            new Building(1, matchId, playerId, BuildingType.CommandCenter, buildingPosition)
        };
        // Don't mark as operational (construction not complete)

        // Act
        var visiblePositions = _calculator.CalculateVisiblePositions(playerId, units, buildings);

        // Assert
        visiblePositions.Should().BeEmpty();
    }

    [Fact]
    public void FilterVisibleUnits_ReturnsOwnUnitsAndVisibleEnemyUnits()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var allUnits = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10)),
            new Unit(2, matchId, player2Id, UnitType.Worker, new Position(11, 11)), // Visible
            new Unit(3, matchId, player2Id, UnitType.Worker, new Position(90, 90))  // Not visible
        };

        var visiblePositions = new HashSet<Position>
        {
            new Position(10, 10),
            new Position(11, 11)
        };

        // Act
        var visibleUnits = _calculator.FilterVisibleUnits(player1Id, allUnits, visiblePositions).ToList();

        // Assert
        visibleUnits.Should().HaveCount(2);
        visibleUnits.Should().Contain(u => u.Id == 1 && u.OwnerId == player1Id);
        visibleUnits.Should().Contain(u => u.Id == 2 && u.OwnerId == player2Id);
        visibleUnits.Should().NotContain(u => u.Id == 3);
    }

    [Fact]
    public void FilterVisibleBuildings_ReturnsOwnBuildingsAndVisibleEnemyBuildings()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var allBuildings = new List<Building>
        {
            new Building(1, matchId, player1Id, BuildingType.CommandCenter, new Position(10, 10)),
            new Building(2, matchId, player2Id, BuildingType.CommandCenter, new Position(11, 11)), // Visible
            new Building(3, matchId, player2Id, BuildingType.Barracks, new Position(90, 90))       // Not visible
        };

        var visiblePositions = new HashSet<Position>
        {
            new Position(10, 10),
            new Position(11, 11)
        };

        // Act
        var visibleBuildings = _calculator.FilterVisibleBuildings(player1Id, allBuildings, visiblePositions).ToList();

        // Assert
        visibleBuildings.Should().HaveCount(2);
        visibleBuildings.Should().Contain(b => b.Id == 1 && b.OwnerId == player1Id);
        visibleBuildings.Should().Contain(b => b.Id == 2 && b.OwnerId == player2Id);
        visibleBuildings.Should().NotContain(b => b.Id == 3);
    }
}
