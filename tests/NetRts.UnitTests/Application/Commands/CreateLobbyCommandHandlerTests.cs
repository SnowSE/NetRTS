using FluentAssertions;
using NetRts.Application.Commands.CreateLobby;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NSubstitute;
using Xunit;

namespace NetRts.UnitTests.Application.Commands;

public class CreateLobbyCommandHandlerTests
{
    private readonly IMatchLobbyRepository _lobbyRepository;
    private readonly CreateLobbyCommandHandler _sut;

    public CreateLobbyCommandHandlerTests()
    {
        _lobbyRepository = Substitute.For<IMatchLobbyRepository>();
        _sut = new CreateLobbyCommandHandler(_lobbyRepository);
    }

    [Fact]
    public async Task Handle_ShouldCreateLobby_AndAddHostAsPlayer()
    {
        // Arrange
        var hostId = Guid.NewGuid();
        var request = new NetRts.Contracts.Requests.CreateLobbyRequest
        {
            Name = "Test Lobby"
        };
        var command = new CreateLobbyCommand(hostId, request);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Lobby");
        result.HostPlayerId.Should().Be(hostId);
        result.Players.Should().HaveCount(1);
        result.Players[0].PlayerId.Should().Be(hostId);
        
        await _lobbyRepository.Received(1).AddAsync(Arg.Any<MatchLobby>(), Arg.Any<CancellationToken>());
    }
}
