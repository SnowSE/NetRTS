using MediatR;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Queries.GetLobby;

public record GetLobbyQuery(Guid LobbyId) : IRequest<LobbyResponse>;
