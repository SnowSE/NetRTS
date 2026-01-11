using MediatR;
using NetRts.Application.Interfaces;

namespace NetRts.Application.Commands.LeaveLobby;

public class LeaveLobbyCommandHandler : IRequestHandler<LeaveLobbyCommand>
{
    private readonly IMatchLobbyRepository _lobbyRepository;

    public LeaveLobbyCommandHandler(IMatchLobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    public async Task Handle(LeaveLobbyCommand request, CancellationToken cancellationToken)
    {
        var lobby = await _lobbyRepository.GetByIdAsync(request.LobbyId, cancellationToken);
        if (lobby == null)
        {
            throw new KeyNotFoundException($"Lobby {request.LobbyId} not found");
        }

        if (lobby.HostPlayerId == request.PlayerId)
        {
            // If host leaves, close the lobby
            lobby.Close();
        }
        else
        {
            lobby.RemovePlayer(request.PlayerId);
        }

        await _lobbyRepository.UpdateAsync(lobby, cancellationToken);
    }
}
