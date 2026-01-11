using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetPlayerProfile;

public class GetPlayerProfileQueryHandler : IRequestHandler<GetPlayerProfileQuery, PlayerResponse>
{
    private readonly IPlayerRepository _playerRepository;

    public GetPlayerProfileQueryHandler(IPlayerRepository playerRepository)
    {
        _playerRepository = playerRepository;
    }

    public async Task<PlayerResponse> Handle(GetPlayerProfileQuery request, CancellationToken cancellationToken)
    {
        var player = await _playerRepository.GetByIdAsync(request.PlayerId, cancellationToken);
        if (player == null)
        {
            throw new InvalidOperationException($"Player {request.PlayerId} not found.");
        }

        return new PlayerResponse
        {
            PlayerId = player.Id,
            Username = player.Username,
            TotalMatches = player.TotalMatches,
            TotalWins = player.TotalWins,
            CreatedAt = player.CreatedAt
        };
    }
}
