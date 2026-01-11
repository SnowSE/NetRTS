using FluentAssertions;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Services;
using Xunit;

namespace NetRts.UnitTests.Infrastructure.Services;

public class ScoringServiceTests
{
    private readonly ScoringService _sut;

    public ScoringServiceTests()
    {
        _sut = new ScoringService();
    }

    [Fact]
    public void CalculatePlayerScore_ShouldSumCorrectComponents()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var match = new Match(playerId, Guid.NewGuid(), GameSettings.Default);
        
        var units = new List<Unit>
        {
            new Unit(1, matchId, playerId, Domain.Enums.UnitType.Worker, new Position(0, 0)),
            new Unit(2, matchId, playerId, Domain.Enums.UnitType.Worker, new Position(1, 1))
        };
        
        var buildings = new List<Building>
        {
            new Building(1, matchId, playerId, Domain.Enums.BuildingType.CommandCenter, new Position(0, 0))
        };

        // Act
        var score = _sut.CalculatePlayerScore(
            playerId,
            match,
            units,
            buildings,
            resourcesGathered: 1000,
            enemyUnitsDestroyed: 5,
            enemyBuildingsDestroyed: 1);

        // Assert
        score.UnitsDestroyed.Should().Be(5);
        score.BuildingsDestroyed.Should().Be(1);
        score.ResourcesGathered.Should().Be(1000);
        score.UnitsRemaining.Should().Be(2);
        score.BuildingsRemaining.Should().Be(1);
        
        // Total score = (5 * 10) + (1 * 50) + (1000 / 10) + (2 * 5) + (1 * 25)
        // = 50 + 50 + 100 + 10 + 25 = 235
        score.TotalScore.Should().Be(235);
    }

    [Fact]
    public void DetermineWinner_ShouldReturnPlayerWithHigherScore()
    {
        // Arrange
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var match = new Match(p1Id, p2Id, GameSettings.Default);
        
        // Use reflection or update methods if available to set scores
        // Score is immutable value object
        
        var p1Score = new Score(10, 2, 2000, 5, 2); // Higher
        var p2Score = new Score(5, 1, 1000, 2, 1);  // Lower
        
        match.UpdateScore(p1Id, p1Score);
        match.UpdateScore(p2Id, p2Score);

        // Act
        var winnerId = _sut.DetermineWinner(match);

        // Assert
        winnerId.Should().Be(p1Id);
    }
}
