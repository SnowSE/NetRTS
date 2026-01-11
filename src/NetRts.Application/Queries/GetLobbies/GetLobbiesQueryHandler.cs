using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;

namespace NetRts.Application.Queries.GetLobbies;

/// <summary>
/// Handler for retrieving a paginated list of open lobbies.
/// </summary>
public class GetLobbiesQueryHandler : IRequestHandler<GetLobbiesQuery, LobbyListResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public GetLobbiesQueryHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task<LobbyListResponse> Handle(GetLobbiesQuery request, CancellationToken cancellationToken)
    {
        var lobbies = await _lobbyRepository.GetOpenLobbiesAsync(cancellationToken);
        
        var totalCount = lobbies.Count;
        var pagedLobbies = lobbies
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new LobbyListResponse
        {
            Lobbies = pagedLobbies.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
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
