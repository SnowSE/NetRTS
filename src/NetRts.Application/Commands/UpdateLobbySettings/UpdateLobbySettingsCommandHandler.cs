using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.ValueObjects;

namespace NetRts.Application.Commands.UpdateLobbySettings;

public class UpdateLobbySettingsCommandHandler : IRequestHandler<UpdateLobbySettingsCommand, LobbyResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public UpdateLobbySettingsCommandHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task<LobbyResponse> Handle(UpdateLobbySettingsCommand request, CancellationToken cancellationToken)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(request.LobbyId, cancellationToken);
        if (lobby == null)
        {
            throw new KeyNotFoundException($"Lobby {request.LobbyId} not found");
        }

        if (lobby.HostPlayerId != request.PlayerId)
        {
            throw new UnauthorizedAccessException("Only the host can update lobby settings");
        }

        var newSettings = new GameSettings(
            mapWidth: request.Settings.MapWidth ?? lobby.GameSettings.MapWidth,
            mapHeight: request.Settings.MapHeight ?? lobby.GameSettings.MapHeight,
            maxTicks: request.Settings.MaxTicks ?? lobby.GameSettings.MaxTicks,
            startingResources: request.Settings.StartingResources ?? lobby.GameSettings.StartingResources
        );

        lobby.UpdateSettings(newSettings);
        await _lobbyRepository.UpdateAsync(lobby, cancellationToken);

        return new LobbyResponse
        {
            Id = lobby.Id,
            Name = lobby.Name,
            Status = lobby.Status.ToString(),
            HostPlayerId = lobby.HostPlayerId,
            CurrentPlayerCount = lobby.Players.Count(),
            MaxPlayers = 2,
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
