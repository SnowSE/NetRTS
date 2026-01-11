using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetLobby;

public class GetLobbyQueryHandler : IRequestHandler<GetLobbyQuery, LobbyResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public GetLobbyQueryHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task<LobbyResponse> Handle(GetLobbyQuery request, CancellationToken cancellationToken)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(request.LobbyId, cancellationToken);
        if (lobby == null)
        {
            throw new KeyNotFoundException($"Lobby {request.LobbyId} not found");
        }

        return new LobbyResponse
        {
            Id = lobby.Id,
            Name = lobby.Name,
            Status = lobby.Status.ToString(),
            HostPlayerId = lobby.HostPlayerId,
            CurrentPlayerCount = lobby.Players.Count(),
            MaxPlayers = 2,
            CreatedAt = lobby.CreatedAt,
            Settings = new GameSettingsDto
            {
                MapWidth = lobby.GameSettings.MapWidth,
                MapHeight = lobby.GameSettings.MapHeight,
                MaxTicks = lobby.GameSettings.MaxTicks,
                StartingResources = lobby.GameSettings.StartingResources,
                TickIntervalMs = lobby.GameSettings.TickIntervalMs,
                CommandQueueSize = lobby.GameSettings.CommandQueueSize,
                CommandsPerTick = lobby.GameSettings.CommandsPerTick
            },
            Players = lobby.Players.Select(p => new LobbyPlayerDto
            {
                PlayerId = p.PlayerId,
                Slot = p.Slot,
                IsReady = p.IsReady,
                JoinedAt = p.JoinedAt
            }).ToList()
        };
    }
}
