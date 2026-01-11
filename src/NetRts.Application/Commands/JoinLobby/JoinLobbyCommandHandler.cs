using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;

namespace NetRts.Application.Commands.JoinLobby;

/// <summary>
/// Handler for joining an existing game lobby.
/// </summary>
public class JoinLobbyCommandHandler : IRequestHandler<JoinLobbyCommand, LobbyResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public JoinLobbyCommandHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task<LobbyResponse> Handle(JoinLobbyCommand request, CancellationToken cancellationToken)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(request.LobbyId, cancellationToken);

        if (lobby == null)
        {
            throw new InvalidOperationException($"Lobby {request.LobbyId} not found");
        }

        // Check if player is already in lobby
        if (lobby.Players.Any(p => p.PlayerId == request.PlayerId))
        {
            return MapToResponse(lobby);
        }

        // Add player to first available slot
        var slot = lobby.CurrentPlayerCount;
        lobby.AddPlayer(request.PlayerId, slot);

        await _lobbyRepository.UpdateAsync(lobby, cancellationToken);

        return MapToResponse(lobby);
    }

    private static LobbyResponse MapToResponse(MatchLobby lobby)
    {
        return new LobbyResponse
        {
            Id = lobby.Id,
            Name = lobby.Name,
            HostPlayerId = lobby.HostPlayerId,
            Status = lobby.Status.ToString(),
            MaxPlayers = lobby.MaxPlayers,
            CurrentPlayerCount = lobby.CurrentPlayerCount,
            CreatedAt = lobby.CreatedAt,
            Settings = new GameSettingsDto
            {
                MapWidth = lobby.GameSettings.MapWidth,
                MapHeight = lobby.GameSettings.MapHeight,
                MaxTicks = lobby.GameSettings.MaxTicks,
                StartingResources = lobby.GameSettings.StartingResources
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
