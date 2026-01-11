using MediatR;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.CreateMatch;

/// <summary>
/// Command to create a new match from a lobby.
/// </summary>
public record CreateMatchCommand(Guid LobbyId, Guid PlayerId) : IRequest<StartMatchResponse>;
