using FluentAssertions;
using NSubstitute;
using NetRts.Application.Commands.QueueCommands;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.UnitTests.Application.Commands;

public class QueueCommandsCommandHandlerTests
{
    private readonly ICommandQueueManager _commandQueueManager;
    private readonly QueueCommandsCommandHandler _handler;

    public QueueCommandsCommandHandlerTests()
    {
        _commandQueueManager = Substitute.For<ICommandQueueManager>();
        _handler = new QueueCommandsCommandHandler(_commandQueueManager);
    }

    [Fact]
    public async Task Handle_WithValidMoveCommand_ReturnsSuccessResponse()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            CommandType = "Move",
            UnitIds = new[] { 1, 2, 3 },
            TargetPosition = new PositionDto { X = 50, Y = 50 }
        };

        _commandQueueManager.EnqueueCommandAsync(
            Arg.Any<Command>(),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.QueuedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        await _commandQueueManager.Received(1).EnqueueCommandAsync(
            Arg.Is<Command>(c => c.Type == CommandType.Move),
            500);
    }

    [Fact]
    public async Task Handle_WithValidGatherCommand_MapsFieldsCorrectly()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var resourceDepositId = 1;
        var commandDto = new CommandDto
        {
            CommandType = "Gather",
            UnitIds = new[] { 1 },
            TargetResourceId = resourceDepositId
        };

        Command? capturedCommand = null;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Do<Command>(c => capturedCommand = c),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Type.Should().Be(CommandType.Gather);
        capturedCommand.TargetResourceDepositId.Should().Be(resourceDepositId);
        capturedCommand.TargetUnitIds.Should().Equal(1);
    }

    [Fact]
    public async Task Handle_WithValidAttackCommand_MapsTargetEntityId()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var targetUnitId = 4;
        var commandDto = new CommandDto
        {
            CommandType = "Attack",
            UnitIds = new[] { 1, 2 },
            TargetUnitId = targetUnitId
        };

        Command? capturedCommand = null;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Do<Command>(c => capturedCommand = c),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Type.Should().Be(CommandType.Attack);
        capturedCommand.TargetEntityId.Should().Be(targetUnitId);
        capturedCommand.TargetUnitIds.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_WhenQueueIsFull_ReturnsFailureResponse()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            CommandType = "Move",
            UnitIds = new[] { 1 },
            TargetPosition = new PositionDto { X = 50, Y = 50 }
        };

        _commandQueueManager.EnqueueCommandAsync(
            Arg.Any<Command>(),
            Arg.Any<int>())
            .Returns(false);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.QueuedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Command queue is full"));
    }

    [Fact]
    public async Task Handle_WithMultipleCommands_ProcessesAllSuccessfully()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commands = new[]
        {
            new CommandDto
            {
                CommandType = "Move",
                UnitIds = new[] { 1 },
                TargetPosition = new PositionDto { X = 50, Y = 50 }
            },
            new CommandDto
            {
                CommandType = "Gather",
                UnitIds = new[] { 2 },
                TargetResourceId = 1
            },
            new CommandDto
            {
                CommandType = "Attack",
                UnitIds = new[] { 3 },
                TargetUnitId = 4
            }
        };

        _commandQueueManager.EnqueueCommandAsync(
            Arg.Any<Command>(),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, commands);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.QueuedCount.Should().Be(3);
        result.FailedCount.Should().Be(0);

        await _commandQueueManager.Received(3).EnqueueCommandAsync(
            Arg.Any<Command>(),
            500);
    }

    [Fact]
    public async Task Handle_WithPartialFailures_ReturnsCorrectCounts()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commands = new[]
        {
            new CommandDto
            {
                CommandType = "Move",
                UnitIds = new[] { 1 },
                TargetPosition = new PositionDto { X = 50, Y = 50 }
            },
            new CommandDto
            {
                CommandType = "Move",
                UnitIds = new[] { 2 },
                TargetPosition = new PositionDto { X = 60, Y = 60 }
            }
        };

        var callCount = 0;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Any<Command>(),
            Arg.Any<int>())
            .Returns(_ => callCount++ == 0); // First succeeds, second fails

        var command = new QueueCommandsCommand(matchId, playerId, commands);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.QueuedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().NotBeNull();
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithBuildCommand_MapsBuildingType()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            CommandType = "Build",
            UnitIds = new[] { 1 },
            BuildingType = "Barracks",
            TargetPosition = new PositionDto { X = 30, Y = 30 }
        };

        Command? capturedCommand = null;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Do<Command>(c => capturedCommand = c),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Type.Should().Be(CommandType.Build);
        capturedCommand.BuildingType.Should().Be(BuildingType.Barracks);
        capturedCommand.TargetPosition.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WithProduceCommand_MapsUnitTypeAndBuildingId()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var buildingId = 1;
        var commandDto = new CommandDto
        {
            CommandType = "Produce",
            BuildingId = buildingId,
            UnitType = "Soldier"
        };

        Command? capturedCommand = null;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Do<Command>(c => capturedCommand = c),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Type.Should().Be(CommandType.Produce);
        capturedCommand.UnitType.Should().Be(UnitType.Soldier);
        capturedCommand.TargetBuildingId.Should().Be(buildingId);
    }

    [Fact]
    public async Task Handle_WithResearchCommand_MapsUpgradeType()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var buildingId = 1;
        var commandDto = new CommandDto
        {
            CommandType = "Research",
            BuildingId = buildingId,
            UpgradeType = "WeaponDamage1"
        };

        Command? capturedCommand = null;
        _commandQueueManager.EnqueueCommandAsync(
            Arg.Do<Command>(c => capturedCommand = c),
            Arg.Any<int>())
            .Returns(true);

        var command = new QueueCommandsCommand(matchId, playerId, new[] { commandDto });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Type.Should().Be(CommandType.Research);
        capturedCommand.UpgradeType.Should().Be(UpgradeType.WeaponDamage1);
        capturedCommand.TargetBuildingId.Should().Be(buildingId);
    }
}
