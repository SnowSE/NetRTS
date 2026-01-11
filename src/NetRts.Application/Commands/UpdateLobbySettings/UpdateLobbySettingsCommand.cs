using MediatR;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.UpdateLobbySettings;

public record UpdateLobbySettingsCommand(Guid LobbyId, Guid PlayerId, UpdateLobbySettingsRequest Settings) : IRequest<LobbyResponse>;
