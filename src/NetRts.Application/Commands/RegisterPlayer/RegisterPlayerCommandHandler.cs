using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;

namespace NetRts.Application.Commands.RegisterPlayer;

public class RegisterPlayerCommandHandler : IRequestHandler<RegisterPlayerCommand, PlayerResponse>
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IJwtTokenService _tokenService;

    public RegisterPlayerCommandHandler(IPlayerRepository playerRepository, IJwtTokenService tokenService)
    {
        _playerRepository = playerRepository;
        _tokenService = tokenService;
    }

    public async Task<PlayerResponse> Handle(RegisterPlayerCommand request, CancellationToken cancellationToken)
    {
        // Check if username already exists
        var existing = await _playerRepository.GetByUsernameAsync(request.Request.Username, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Username '{request.Request.Username}' is already taken.");
        }

        var player = new Player(
            request.Request.Username, 
            request.Request.Password, 
            request.Request.IsBot);
            
        await _playerRepository.AddAsync(player, cancellationToken);

        var token = _tokenService.GenerateToken(player);

        return new PlayerResponse
        {
            PlayerId = player.Id,
            Username = player.Username,
            Token = token,
            TotalMatches = player.TotalMatches,
            TotalWins = player.TotalWins,
            CreatedAt = player.CreatedAt
        };
    }
}
