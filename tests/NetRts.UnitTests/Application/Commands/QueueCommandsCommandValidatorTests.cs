using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;
using NetRts.Application.Commands.QueueCommands;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.UnitTests.Application.Commands;

public class QueueCommandsCommandValidatorTests
{
    private readonly IGameStateCache _gameStateCache;
    private readonly QueueCommandsCommandValidator _validator;

    public QueueCommandsCommandValidatorTests()
    {
        _gameStateCache = Substitute.For<IGameStateCache>();
        _validator = new QueueCommandsCommandValidator(_gameStateCache);
    }

    [Fact]
    public void Validate_BuildCommand_WithValidTile_PassesValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 500);

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10))
        };

        var buildings = new List<Building>(); // No existing buildings

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Build",
                    UnitIds = new[] { 1 },
                    BuildingType = "Barracks",
                    TargetPosition = new PositionDto { X = 30, Y = 30 }
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BuildCommand_OnOccupiedTile_FailsValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 500);

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10))
        };

        // Existing building at target position
        var buildings = new List<Building>
        {
            new Building(1, matchId, player2Id, BuildingType.Barracks, new Position(30, 30))
        };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Build",
                    UnitIds = new[] { 1 },
                    BuildingType = "Barracks",
                    TargetPosition = new PositionDto { X = 30, Y = 30 }
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("*occupied*");
    }

    [Fact]
    public void Validate_BuildCommand_WithInsufficientResources_FailsValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 50); // Not enough for Barracks (costs 200)

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10))
        };

        var buildings = new List<Building>();

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Build",
                    UnitIds = new[] { 1 },
                    BuildingType = "Barracks",
                    TargetPosition = new PositionDto { X = 30, Y = 30 }
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("*Insufficient resources*");
    }

    [Fact]
    public void Validate_BuildCommand_OutOfBounds_FailsValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 500);

        var units = new List<Unit>
        {
            new Unit(1, matchId, player1Id, UnitType.Worker, new Position(10, 10))
        };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(units);
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(new List<Building>());
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Build",
                    UnitIds = new[] { 1 },
                    BuildingType = "Barracks",
                    TargetPosition = new PositionDto { X = 300, Y = 300 } // Out of bounds
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("*out of bounds*");
    }

    [Fact]
    public void Validate_ProduceCommand_WithSufficientResources_PassesValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 200);

        var barracks = new Building(1, matchId, player1Id, BuildingType.Barracks, new Position(30, 30));
        barracks.CompleteConstruction();

        var buildings = new List<Building> { barracks };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(new List<Unit>());
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Produce",
                    BuildingId = barracks.Id,
                    UnitType = "Soldier"
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ProduceCommand_WithInsufficientResources_FailsValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 20); // Not enough for Soldier (costs 100)

        var barracks = new Building(1, matchId, player1Id, BuildingType.Barracks, new Position(30, 30));
        barracks.CompleteConstruction();

        var buildings = new List<Building> { barracks };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(new List<Unit>());
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Produce",
                    BuildingId = barracks.Id,
                    UnitType = "Soldier"
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("*Insufficient resources*");
    }

    [Fact]
    public void Validate_ProduceCommand_WithNonOperationalBuilding_FailsValidation()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var settings = new GameSettings(100, 100, 1800, 1000);
        var match = new Match(player1Id, player2Id, settings);
        match.SetPlayerResources(player1Id, 200);

        var barracks = new Building(1, matchId, player1Id, BuildingType.Barracks, new Position(30, 30));
        // Don't complete construction

        var buildings = new List<Building> { barracks };

        _gameStateCache.GetMatch(matchId).Returns(match);
        _gameStateCache.GetUnitsForMatch(matchId).Returns(new List<Unit>());
        _gameStateCache.GetBuildingsForMatch(matchId).Returns(buildings);
        _gameStateCache.GetResourceDepositsForMatch(matchId).Returns(new List<ResourceDeposit>());

        var command = new QueueCommandsCommand(
            matchId,
            player1Id,
            new[]
            {
                new CommandDto
                {
                    Type = "Produce",
                    BuildingId = barracks.Id,
                    UnitType = "Soldier"
                }
            });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("*not operational*");
    }
}
