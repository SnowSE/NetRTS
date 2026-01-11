using FluentAssertions;
using NetRts.Application.Interfaces;
using NetRts.Application.Queries.GetMatchResult;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NSubstitute;
using Xunit;
using Microsoft.EntityFrameworkCore;
using NetRts.Infrastructure.Data;
using Xunit;

namespace NetRts.UnitTests.Application.Queries;

public class GetMatchResultQueryHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly GetMatchResultQueryHandler _sut;

    public GetMatchResultQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        var dbContext = new ApplicationDbContext(options);
        _context = dbContext;
        _sut = new GetMatchResultQueryHandler(_context);
    }

    [Fact]
    public async Task Handle_ShouldReturnResult_WhenMatchIsCompleted()
    {
        // Arrange
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var match = new Match(p1Id, p2Id, GameSettings.Default);
        
        // Simulate completion
        match.Start();
        match.EndMatchByElimination(p2Id);
        
        await _context.Matches.AddAsync(match);
        await _context.SaveChangesAsync();

        var query = new GetMatchResultQuery(match.Id);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.MatchId.Should().Be(match.Id);
        result.Status.Should().Be("Completed");
        result.WinnerId.Should().Be(p1Id);
        result.Player1Result.IsWinner.Should().BeTrue();
        result.Player2Result.IsWinner.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenMatchIsNotCompleted()
    {
        // Arrange
        var match = new Match(Guid.NewGuid(), Guid.NewGuid(), GameSettings.Default);
        match.Start();
        
        await _context.Matches.AddAsync(match);
        await _context.SaveChangesAsync();

        var query = new GetMatchResultQuery(match.Id);

        // Act & Assert
        await _sut.Invoking(x => x.Handle(query, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not completed*");
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenMatchNotFound()
    {
        // Arrange
        var query = new GetMatchResultQuery(Guid.NewGuid());

        // Act & Assert
        await _sut.Invoking(x => x.Handle(query, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
