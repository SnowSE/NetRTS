using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;

namespace NetRts.Application.Commands.CreateLobby;

/// <summary>
/// Handler for creating a new game lobby.
/// </summary>
public class CreateLobbyCommandHandler : IRequestHandler<CreateLobbyCommand, LobbyResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public CreateLobbyCommandHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task<LobbyResponse> Handle(CreateLobbyCommand request, CancellationToken cancellationToken)
    {
        NetRts.Domain.ValueObjects.GameSettings? settings = null;
        if (request.Request.Settings != null)
        {
            settings = new NetRts.Domain.ValueObjects.GameSettings(
                mapWidth: request.Request.Settings.MapWidth,
                mapHeight: request.Request.Settings.MapHeight,
                maxTicks: request.Request.Settings.MaxTicks,
                startingResources: request.Request.Settings.StartingResources
            );
        }

        var lobby = new MatchLobby(
            request.Request.Name,
            request.HostPlayerId,
            settings);

        // Host automatically joins the lobby in slot 0
        lobby.AddPlayer(request.HostPlayerId, 0);

        await _lobbyRepository.AddAsync(lobby, cancellationToken);

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
