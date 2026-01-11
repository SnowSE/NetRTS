using FluentAssertions;
using NetRts.Application.Commands.RegisterPlayer;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NSubstitute;
using Xunit;

namespace NetRts.UnitTests.Application.Commands;

public class RegisterPlayerCommandHandlerTests
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IJwtTokenService _tokenService;
    private readonly RegisterPlayerCommandHandler _sut;

    public RegisterPlayerCommandHandlerTests()
    {
        _playerRepository = Substitute.For<IPlayerRepository>();
        _tokenService = Substitute.For<IJwtTokenService>();
        _sut = new RegisterPlayerCommandHandler(_playerRepository, _tokenService);
    }

    [Fact]
    public async Task Handle_ShouldRegisterPlayer_AndReturnToken()
    {
        // Arrange
        var request = new NetRts.Contracts.Requests.RegisterPlayerRequest
        {
            Username = "testuser",
            Password = "password"
        };
        var command = new RegisterPlayerCommand(request);
        
        _playerRepository.GetByUsernameAsync("testuser", Arg.Any<CancellationToken>()).Returns((Player?)null);
        _tokenService.GenerateToken(Arg.Any<Player>()).Returns("test-token");

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().Be("testuser");
        result.Token.Should().Be("test-token");
        
        await _playerRepository.Received(1).AddAsync(Arg.Any<Player>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithExistingUsername_ShouldThrowException()
    {
        // Arrange
        var request = new NetRts.Contracts.Requests.RegisterPlayerRequest
        {
            Username = "existing",
            Password = "password"
        };
        var command = new RegisterPlayerCommand(request);
        
        _playerRepository.GetByUsernameAsync("existing", Arg.Any<CancellationToken>())
            .Returns(new Player("existing", "pw", false));

        // Act & Assert
        await _sut.Invoking(x => x.Handle(command, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already taken*");
    }
}
