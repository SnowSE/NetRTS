using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Data;
using NetRts.IntegrationTests.Infrastructure;
using Xunit;

namespace NetRts.IntegrationTests.Domain;

public class MatchRepositoryTests : IntegrationTestBase
{
    [Fact]
    public async Task CanSaveAndRetrieveMatch()
    {
        // Arrange
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var settings = new GameSettings(mapWidth: 100, mapHeight: 100);
        var match = new Match(player1Id, player2Id, settings);

        // Act - Save match
        var savedMatchId = await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            dbContext.Matches.Add(match);
            await dbContext.SaveChangesAsync();
            return match.Id;
        });

        // Act - Retrieve match
        var retrievedMatch = await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            return await dbContext.Matches.FindAsync(savedMatchId);
        });

        // Assert
        retrievedMatch.Should().NotBeNull();
        retrievedMatch!.Id.Should().Be(savedMatchId);
        retrievedMatch.Player1Id.Should().Be(player1Id);
        retrievedMatch.Player2Id.Should().Be(player2Id);
        retrievedMatch.MapWidth.Should().Be(100);
        retrievedMatch.MapHeight.Should().Be(100);
    }

    [Fact]
    public async Task CanSaveMatchWithScores()
    {
        // Arrange
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var settings = GameSettings.Default;
        var match = new Match(player1Id, player2Id, settings);

        // Update scores
        match.UpdateScore(player1Id, match.Player1Score.WithUnitsDestroyed(5));
        match.UpdateScore(player2Id, match.Player2Score.WithBuildingsDestroyed(2));

        // Act - Save
        await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            dbContext.Matches.Add(match);
            await dbContext.SaveChangesAsync();
        });

        // Act - Retrieve
        var retrievedMatch = await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            return await dbContext.Matches.FindAsync(match.Id);
        });

        // Assert
        retrievedMatch.Should().NotBeNull();
        retrievedMatch!.Player1Score.UnitsDestroyed.Should().Be(5);
        retrievedMatch.Player2Score.BuildingsDestroyed.Should().Be(2);
    }

    [Fact]
    public async Task CanUpdateMatchStatus()
    {
        // Arrange
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var match = new Match(player1Id, player2Id, GameSettings.Default);

        await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            dbContext.Matches.Add(match);
            await dbContext.SaveChangesAsync();
        });

        // Act - Update status
        await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            var matchToUpdate = await dbContext.Matches.FindAsync(match.Id);
            matchToUpdate!.Start();
            await dbContext.SaveChangesAsync();
        });

        // Assert
        var updatedMatch = await ExecuteInScope(async sp =>
        {
            var dbContext = sp.GetRequiredService<ApplicationDbContext>();
            return await dbContext.Matches.FindAsync(match.Id);
        });

        updatedMatch.Should().NotBeNull();
        updatedMatch!.Status.Should().Be(NetRts.Domain.Enums.MatchStatus.Active);
        updatedMatch.StartedAt.Should().NotBeNull();
    }
}
