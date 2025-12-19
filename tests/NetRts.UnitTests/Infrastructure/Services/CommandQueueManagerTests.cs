using FluentAssertions;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Infrastructure.Services;

namespace NetRts.UnitTests.Infrastructure.Services;

public class CommandQueueManagerTests
{
    private readonly CommandQueueManager _manager;

    public CommandQueueManagerTests()
    {
        _manager = new CommandQueueManager();
    }

    [Fact]
    public async Task EnqueueCommandAsync_WithValidCommand_ReturnsTrue()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var command = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);

        // Act
        var result = await _manager.EnqueueCommandAsync(command, 100);

        // Assert
        result.Should().BeTrue();
        var queueSize = await _manager.GetQueueSizeAsync(matchId, playerId);
        queueSize.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueCommandAsync_WhenQueueIsFull_ReturnsFalse()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        const int maxQueueSize = 5;

        // Fill the queue
        for (int i = 0; i < maxQueueSize; i++)
        {
            var command = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);
            await _manager.EnqueueCommandAsync(command, maxQueueSize);
        }

        // Try to add one more
        var extraCommand = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);

        // Act
        var result = await _manager.EnqueueCommandAsync(extraCommand, maxQueueSize);

        // Assert
        result.Should().BeFalse();
        var queueSize = await _manager.GetQueueSizeAsync(matchId, playerId);
        queueSize.Should().Be(maxQueueSize);
    }

    [Fact]
    public async Task DequeueCommandsAsync_ReturnsCommandsInFIFOOrder()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var command1 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);
        var command2 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Gather, 0);
        var command3 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Attack, 0);

        await _manager.EnqueueCommandAsync(command1, 100);
        await _manager.EnqueueCommandAsync(command2, 100);
        await _manager.EnqueueCommandAsync(command3, 100);

        // Act
        var dequeuedCommands = await _manager.DequeueCommandsAsync(matchId, playerId, 10);

        // Assert
        dequeuedCommands.Should().HaveCount(3);
        dequeuedCommands[0].Type.Should().Be(CommandType.Move);
        dequeuedCommands[1].Type.Should().Be(CommandType.Gather);
        dequeuedCommands[2].Type.Should().Be(CommandType.Attack);
    }

    [Fact]
    public async Task DequeueCommandsAsync_RespectsLimit()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
        {
            var command = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);
            await _manager.EnqueueCommandAsync(command, 100);
        }

        // Act
        var dequeuedCommands = await _manager.DequeueCommandsAsync(matchId, playerId, 5);

        // Assert
        dequeuedCommands.Should().HaveCount(5);
        var queueSize = await _manager.GetQueueSizeAsync(matchId, playerId);
        queueSize.Should().Be(5); // 5 remaining
    }

    [Fact]
    public async Task DequeueCommandsAsync_WithEmptyQueue_ReturnsEmptyList()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Act
        var dequeuedCommands = await _manager.DequeueCommandsAsync(matchId, playerId, 10);

        // Assert
        dequeuedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetQueueSizeAsync_ReturnsCorrectCount()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        for (int i = 0; i < 7; i++)
        {
            var command = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);
            await _manager.EnqueueCommandAsync(command, 100);
        }

        // Act
        var queueSize = await _manager.GetQueueSizeAsync(matchId, playerId);

        // Assert
        queueSize.Should().Be(7);
    }

    [Fact]
    public async Task GetQueueSizeAsync_ForNonExistentQueue_ReturnsZero()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Act
        var queueSize = await _manager.GetQueueSizeAsync(matchId, playerId);

        // Assert
        queueSize.Should().Be(0);
    }

    [Fact]
    public async Task ClearMatchCommandsAsync_RemovesAllPlayerQueuesForMatch()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        // Add commands for both players
        await _manager.EnqueueCommandAsync(
            new Command(Guid.NewGuid(), matchId, player1Id, CommandType.Move, 0), 100);
        await _manager.EnqueueCommandAsync(
            new Command(Guid.NewGuid(), matchId, player2Id, CommandType.Move, 0), 100);

        // Act
        await _manager.ClearMatchCommandsAsync(matchId);

        // Assert
        var player1QueueSize = await _manager.GetQueueSizeAsync(matchId, player1Id);
        var player2QueueSize = await _manager.GetQueueSizeAsync(matchId, player2Id);
        player1QueueSize.Should().Be(0);
        player2QueueSize.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAndDequeue_MaintainsFIFOOrderAcrossMultipleCycles()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var command1 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Move, 0);
        var command2 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Gather, 0);
        var command3 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Attack, 0);
        var command4 = new Command(Guid.NewGuid(), matchId, playerId, CommandType.Build, 0);

        // Enqueue 3 commands
        await _manager.EnqueueCommandAsync(command1, 100);
        await _manager.EnqueueCommandAsync(command2, 100);
        await _manager.EnqueueCommandAsync(command3, 100);

        // Dequeue 2 commands
        var firstBatch = await _manager.DequeueCommandsAsync(matchId, playerId, 2);

        // Enqueue 1 more command
        await _manager.EnqueueCommandAsync(command4, 100);

        // Dequeue remaining commands
        var secondBatch = await _manager.DequeueCommandsAsync(matchId, playerId, 10);

        // Assert
        firstBatch.Should().HaveCount(2);
        firstBatch[0].Type.Should().Be(CommandType.Move);
        firstBatch[1].Type.Should().Be(CommandType.Gather);

        secondBatch.Should().HaveCount(2);
        secondBatch[0].Type.Should().Be(CommandType.Attack);
        secondBatch[1].Type.Should().Be(CommandType.Build);
    }

    [Fact]
    public async Task Queues_AreIsolatedByPlayer()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        var player1Command = new Command(Guid.NewGuid(), matchId, player1Id, CommandType.Move, 0);
        var player2Command = new Command(Guid.NewGuid(), matchId, player2Id, CommandType.Gather, 0);

        await _manager.EnqueueCommandAsync(player1Command, 100);
        await _manager.EnqueueCommandAsync(player2Command, 100);

        // Act
        var player1Commands = await _manager.DequeueCommandsAsync(matchId, player1Id, 10);
        var player2Commands = await _manager.DequeueCommandsAsync(matchId, player2Id, 10);

        // Assert
        player1Commands.Should().HaveCount(1);
        player1Commands[0].Type.Should().Be(CommandType.Move);

        player2Commands.Should().HaveCount(1);
        player2Commands[0].Type.Should().Be(CommandType.Gather);
    }

    [Fact]
    public async Task Queues_AreIsolatedByMatch()
    {
        // Arrange
        var match1Id = Guid.NewGuid();
        var match2Id = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var match1Command = new Command(Guid.NewGuid(), match1Id, playerId, CommandType.Move, 0);
        var match2Command = new Command(Guid.NewGuid(), match2Id, playerId, CommandType.Gather, 0);

        await _manager.EnqueueCommandAsync(match1Command, 100);
        await _manager.EnqueueCommandAsync(match2Command, 100);

        // Act
        var match1Commands = await _manager.DequeueCommandsAsync(match1Id, playerId, 10);
        var match2Commands = await _manager.DequeueCommandsAsync(match2Id, playerId, 10);

        // Assert
        match1Commands.Should().HaveCount(1);
        match1Commands[0].Type.Should().Be(CommandType.Move);

        match2Commands.Should().HaveCount(1);
        match2Commands[0].Type.Should().Be(CommandType.Gather);
    }
}
