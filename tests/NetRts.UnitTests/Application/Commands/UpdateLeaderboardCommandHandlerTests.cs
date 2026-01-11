using FluentAssertions;
using NSubstitute;
using NetRts.Application.Commands.UpdateLeaderboard;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using Xunit;

namespace NetRts.UnitTests.Application.Commands;

public class UpdateLeaderboardCommandHandlerTests
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ILeaderboardRepository _leaderboardRepository;
    private readonly UpdateLeaderboardCommandHandler _handler;

    public UpdateLeaderboardCommandHandlerTests()
    {
        _matchRepository = Substitute.For<IMatchRepository>();
        _playerRepository = Substitute.For<IPlayerRepository>();
        _leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        _handler = new UpdateLeaderboardCommandHandler(_matchRepository, _playerRepository, _leaderboardRepository);
    }

    [Fact]
    public async Task Handle_ShouldUpdateScoresAndRecalculateRanks()
    {
        // Arrange
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var match = new Match(player1Id, player2Id, GameSettings.Default);
        
        match.Start();
        match.UpdateScore(player1Id, new Score(10, 0, 100, 5, 1));
        match.UpdateScore(player2Id, new Score(5, 0, 50, 2, 0));
        match.EndMatchByElimination(player2Id); // Player 1 wins

        _matchRepository.GetByIdAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(match);

        _leaderboardRepository.GetPlayerScoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PlayerScore?)null);

        _playerRepository.GetByIdAsync(player1Id, Arg.Any<CancellationToken>())
            .Returns(new Player("Winner", "winner@example.com", true));
        _playerRepository.GetByIdAsync(player2Id, Arg.Any<CancellationToken>())
            .Returns(new Player("Loser", "loser@example.com", true));

        var command = new UpdateLeaderboardCommand(match.Id);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        await _leaderboardRepository.Received(2).UpsertPlayerScoreAsync(Arg.Any<PlayerScore>(), Arg.Any<CancellationToken>());
        await _leaderboardRepository.Received(1).RecalculateRanksAsync(Arg.Any<CancellationToken>());
    }
}
