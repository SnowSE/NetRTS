using FluentAssertions;
using NSubstitute;
using NetRts.Application.Interfaces;
using NetRts.Application.Queries.GetLeaderboard;
using NetRts.Domain.Entities;
using Xunit;

namespace NetRts.UnitTests.Application.Queries;

public class GetLeaderboardQueryHandlerTests
{
    private readonly ILeaderboardRepository _leaderboardRepository;
    private readonly GetLeaderboardQueryHandler _handler;

    public GetLeaderboardQueryHandlerTests()
    {
        _leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        _handler = new GetLeaderboardQueryHandler(_leaderboardRepository);
    }

    [Fact]
    public async Task Handle_ShouldReturnPagedLeaderboard()
    {
        // Arrange
        var players = new List<PlayerScore>
        {
            CreatePlayerScore("Player1", 1000),
            CreatePlayerScore("Player2", 800),
            CreatePlayerScore("Player3", 600),
            CreatePlayerScore("Player4", 400)
        };

        _leaderboardRepository.GetTopPlayersAsync(100, Arg.Any<CancellationToken>())
            .Returns(players);

        var query = new GetLeaderboardQuery(1, 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Entries.Should().HaveCount(2);
        result.Entries[0].Username.Should().Be("Player1");
        result.Entries[1].Username.Should().Be("Player2");
        result.TotalCount.Should().Be(4);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ShouldReturnCorrectRank()
    {
        // Arrange
        var players = new List<PlayerScore>
        {
            CreatePlayerScore("Player1", 1000),
            CreatePlayerScore("Player2", 800),
            CreatePlayerScore("Player3", 600)
        };

        _leaderboardRepository.GetTopPlayersAsync(100, Arg.Any<CancellationToken>())
            .Returns(players);

        var query = new GetLeaderboardQuery(2, 1);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Entries.Should().HaveCount(1);
        result.Entries[0].Username.Should().Be("Player2");
        result.Entries[0].Rank.Should().Be(2);
    }

    private PlayerScore CreatePlayerScore(string username, int totalScore)
    {
        var ps = new PlayerScore(Guid.NewGuid(), username);
        ps.UpdateStats(totalScore, true);
        return ps;
    }
}
