using FluentAssertions;
using NetRts.Application.Commands.JoinLobby;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NSubstitute;
using Xunit;

namespace NetRts.UnitTests.Application.Commands;

public class JoinLobbyCommandHandlerTests
{
    private readonly IMatchLobbyRepository _lobbyRepository;
    private readonly JoinLobbyCommandHandler _sut;

    public JoinLobbyCommandHandlerTests()
    {
        _lobbyRepository = Substitute.For<IMatchLobbyRepository>();
        _sut = new JoinLobbyCommandHandler(_lobbyRepository);
    }

    [Fact]
    public async Task Handle_ShouldAddPlayerToLobby()
    {
        // Arrange
        var lobbyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var lobby = new MatchLobby("Test Lobby", hostId);
        lobby.AddPlayer(hostId, 0);

        _lobbyRepository.GetByIdAsync(lobbyId, Arg.Any<CancellationToken>()).Returns(lobby);

        var command = new JoinLobbyCommand(lobbyId, playerId);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Players.Should().HaveCount(2);
        result.Players.Should().Contain(p => p.PlayerId == playerId);
        
        await _lobbyRepository.Received(1).UpdateAsync(lobby, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithFullLobby_ShouldThrowException()
    {
        // Arrange
        var lobbyId = Guid.NewGuid();
        var lobby = new MatchLobby("Test Lobby", Guid.NewGuid());
        lobby.AddPlayer(Guid.NewGuid(), 0);
        lobby.AddPlayer(Guid.NewGuid(), 1); // Full

        _lobbyRepository.GetByIdAsync(lobbyId, Arg.Any<CancellationToken>()).Returns(lobby);

        var command = new JoinLobbyCommand(lobbyId, Guid.NewGuid());

        // Act & Assert
        await _sut.Invoking(x => x.Handle(command, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*full*");
    }
}
