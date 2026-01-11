using MediatR;

namespace NetRts.Application.Commands.LeaveLobby;

public record LeaveLobbyCommand(Guid LobbyId, Guid PlayerId) : IRequest;
