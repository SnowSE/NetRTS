using FluentAssertions;
using NSubstitute;
using NetRts.Application.Commands.QueueCommands;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;

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
            Type = "Move",
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
        result.Failures.Should().BeNull();

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
        var resourceDepositId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            Type = "Gather",
            UnitIds = new[] { 1 },
            TargetResourceDepositId = resourceDepositId
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
        var targetUnitId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            Type = "Attack",
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
            Type = "Move",
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
        result.Failures.Should().Contain("Command queue is full (max 500)");
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
                Type = "Move",
                UnitIds = new[] { 1 },
                TargetPosition = new PositionDto { X = 50, Y = 50 }
            },
            new CommandDto
            {
                Type = "Gather",
                UnitIds = new[] { 2 },
                TargetResourceDepositId = Guid.NewGuid()
            },
            new CommandDto
            {
                Type = "Attack",
                UnitIds = new[] { 3 },
                TargetUnitId = Guid.NewGuid()
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
                Type = "Move",
                UnitIds = new[] { 1 },
                TargetPosition = new PositionDto { X = 50, Y = 50 }
            },
            new CommandDto
            {
                Type = "Move",
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
        result.Failures.Should().NotBeNull();
        result.Failures.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithBuildCommand_MapsBuildingType()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            Type = "Build",
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
        var buildingId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            Type = "Produce",
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
        capturedCommand.BuildingId.Should().Be(buildingId);
    }

    [Fact]
    public async Task Handle_WithResearchCommand_MapsUpgradeType()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var commandDto = new CommandDto
        {
            Type = "Research",
            BuildingId = buildingId,
            UpgradeType = "MeleeDamage"
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
        capturedCommand.UpgradeType.Should().Be(UpgradeType.MeleeDamage);
        capturedCommand.BuildingId.Should().Be(buildingId);
    }
}
