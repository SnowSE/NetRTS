using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NetRts.Application.Services;
using NetRts.Infrastructure.BackgroundServices;

namespace NetRts.UnitTests.Infrastructure.BackgroundServices;

public class GameTickServiceTests
{
    private readonly ILogger<GameTickService> _logger;
    private readonly IGameTickProcessor _tickProcessor;
    private readonly GameTickService _service;

    public GameTickServiceTests()
    {
        _logger = Substitute.For<ILogger<GameTickService>>();
        _tickProcessor = Substitute.For<IGameTickProcessor>();
        _service = new GameTickService(_logger, _tickProcessor);
    }

    [Fact]
    public async Task ExecuteAsync_CallsTickProcessorOnEachInterval()
    {
        // Arrange
        var callCount = 0;
        var cts = new CancellationTokenSource();

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callCount++;
                if (callCount >= 3)
                {
                    await cts.CancelAsync();
                }
            });

        // Act
        try
        {
            await _service.StartAsync(cts.Token);
            await Task.Delay(3500, CancellationToken.None); // Wait for ~3 ticks
            await cts.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation occurs
        }

        // Assert
        callCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesProcessingAfterException()
    {
        // Arrange
        var callCount = 0;
        var cts = new CancellationTokenSource();

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callCount++;
                if (callCount == 2)
                {
                    throw new InvalidOperationException("Simulated error");
                }
                if (callCount >= 4)
                {
                    await cts.CancelAsync();
                }
            });

        // Act
        try
        {
            await _service.StartAsync(cts.Token);
            await Task.Delay(5000, CancellationToken.None); // Wait for ~5 ticks
            await cts.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation occurs
        }

        // Assert - Should have continued processing after the exception
        callCount.Should().BeGreaterOrEqualTo(4);
    }

    [Fact]
    public async Task ExecuteAsync_LogsErrorWhenProcessingFails()
    {
        // Arrange
        var callCount = 0;
        var cts = new CancellationTokenSource();
        var expectedException = new InvalidOperationException("Test error");

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw expectedException;
                }
                if (callCount >= 2)
                {
                    await cts.CancelAsync();
                }
            });

        // Act
        try
        {
            await _service.StartAsync(cts.Token);
            await Task.Delay(2500, CancellationToken.None);
            await cts.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Verify error was logged
        _logger.Received().LogError(
            expectedException,
            Arg.Is<string>(s => s.Contains("Error processing game tick")));
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationRequested()
    {
        // Arrange
        var callCount = 0;
        var cts = new CancellationTokenSource();

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Task.CompletedTask;
            });

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(1500, CancellationToken.None); // Wait for ~1-2 ticks
        await cts.CancelAsync();
        await _service.StopAsync(CancellationToken.None);

        var countAtStop = callCount;
        await Task.Delay(2000, CancellationToken.None); // Wait additional time

        // Assert - No additional ticks should have occurred after cancellation
        callCount.Should().Be(countAtStop);
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToProcessor()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        CancellationToken? receivedToken = null;

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                receivedToken = callInfo.Arg<CancellationToken>();
                await cts.CancelAsync();
            });

        // Act
        try
        {
            await _service.StartAsync(cts.Token);
            await Task.Delay(1500, CancellationToken.None);
            await cts.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        receivedToken.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_LogsStartupAndShutdown()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _tickProcessor.ProcessTickAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await cts.CancelAsync();
            });

        // Act
        try
        {
            await _service.StartAsync(cts.Token);
            await Task.Delay(1500, CancellationToken.None);
            await cts.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        _logger.Received(1).LogInformation(
            Arg.Is<string>(s => s.Contains("Game Tick Service starting")));
        _logger.Received(1).LogInformation(
            Arg.Is<string>(s => s.Contains("Game Tick Service stopping")));
    }
}

/// <summary>
/// Tests for building construction and unit production
/// </summary>
public class BuildingConstructionTests
{
    [Fact]
    public void ProcessBuildingConstruction_AdvancesProgressEachTick()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var building = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));

        // Act - Simulate 5 ticks
        for (int i = 0; i < 5; i++)
        {
            var constructionTime = NetRts.Domain.Entities.Building.GetConstructionTime(building.Type);
            var progressPerTick = 100 / constructionTime;
            building.AdvanceConstruction(progressPerTick);
        }

        // Assert
        building.ConstructionProgress.Should().BeGreaterThan(0);
        building.ConstructionProgress.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void ProcessBuildingConstruction_BecomesOperationalAt100Percent()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var building = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));

        var constructionTime = NetRts.Domain.Entities.Building.GetConstructionTime(building.Type);

        // Act - Simulate enough ticks to complete construction
        for (int i = 0; i < constructionTime + 1; i++)
        {
            var progressPerTick = 100 / constructionTime;
            building.AdvanceConstruction(progressPerTick);
        }

        // Assert
        building.ConstructionProgress.Should().Be(100);
        building.IsOperational.Should().BeTrue();
    }

    [Fact]
    public void ProcessBuildingConstruction_DifferentBuildingTypesHaveDifferentTimes()
    {
        // Arrange & Act
        var barracksTime = NetRts.Domain.Entities.Building.GetConstructionTime(
            NetRts.Domain.Enums.BuildingType.Barracks);
        var techLabTime = NetRts.Domain.Entities.Building.GetConstructionTime(
            NetRts.Domain.Enums.BuildingType.TechLab);

        // Assert
        barracksTime.Should().NotBe(techLabTime);
        barracksTime.Should().BeGreaterThan(0);
        techLabTime.Should().BeGreaterThan(0);
    }
}

/// <summary>
/// Tests for unit production
/// </summary>
public class UnitProductionTests
{
    [Fact]
    public void EnqueueProduction_AddsToProductionQueue()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        barracks.CompleteConstruction();

        // Act
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, 15);

        // Assert
        barracks.ProductionQueue.Should().HaveCount(1);
        barracks.ProductionQueue[0].UnitType.Should().Be(NetRts.Domain.Enums.UnitType.Soldier);
    }

    [Fact]
    public void ProcessProduction_DecreasesTicksRemaining()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        barracks.CompleteConstruction();
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, 15);

        var initialTicksRemaining = barracks.ProductionQueue[0].TicksRemaining;

        // Act
        barracks.ProcessProduction();

        // Assert
        barracks.ProductionQueue[0].TicksRemaining.Should().BeLessThan(initialTicksRemaining);
    }

    [Fact]
    public void ProcessProduction_CompletesAfterCorrectNumberOfTicks()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        barracks.CompleteConstruction();

        var productionTime = NetRts.Domain.Entities.Unit.GetProductionTime(
            NetRts.Domain.Enums.UnitType.Soldier);
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, productionTime);

        // Act - Process ticks until completion
        bool completed = false;
        for (int i = 0; i < productionTime; i++)
        {
            completed = barracks.ProcessProduction();
        }

        // Assert
        completed.Should().BeTrue();
        barracks.ProductionQueue[0].IsComplete().Should().BeTrue();
    }

    [Fact]
    public void GetCompletedProduction_ReturnsUnitTypeAndRemovesFromQueue()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        barracks.CompleteConstruction();

        var productionTime = NetRts.Domain.Entities.Unit.GetProductionTime(
            NetRts.Domain.Enums.UnitType.Soldier);
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, productionTime);

        // Complete production
        for (int i = 0; i < productionTime; i++)
        {
            barracks.ProcessProduction();
        }

        // Act
        var completedUnitType = barracks.GetCompletedProduction();

        // Assert
        completedUnitType.Should().Be(NetRts.Domain.Enums.UnitType.Soldier);
        barracks.ProductionQueue.Should().BeEmpty();
    }

    [Fact]
    public void ProductionQueue_ProcessesFIFO()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        barracks.CompleteConstruction();

        // Enqueue multiple units
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, 5);
        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Scout, 5);

        // Act - Complete first production
        for (int i = 0; i < 5; i++)
        {
            barracks.ProcessProduction();
        }
        var firstCompleted = barracks.GetCompletedProduction();

        // Assert
        firstCompleted.Should().Be(NetRts.Domain.Enums.UnitType.Soldier);
        barracks.ProductionQueue.Should().HaveCount(1);
        barracks.ProductionQueue[0].UnitType.Should().Be(NetRts.Domain.Enums.UnitType.Scout);
    }

    [Fact]
    public void NonOperationalBuilding_CannotProcessProduction()
    {
        // Arrange
        var matchId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var barracks = new NetRts.Domain.Entities.Building(
            1,
            matchId,
            playerId,
            NetRts.Domain.Enums.BuildingType.Barracks,
            new NetRts.Domain.ValueObjects.Position(30, 30));
        // Don't complete construction

        barracks.EnqueueProduction(NetRts.Domain.Enums.UnitType.Soldier, 15);

        // Act
        var result = barracks.ProcessProduction();

        // Assert
        result.Should().BeFalse();
    }
}
