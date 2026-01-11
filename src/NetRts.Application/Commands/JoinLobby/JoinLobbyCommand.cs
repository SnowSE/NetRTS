using NetRts.Application.Abstractions;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.JoinLobby;

/// <summary>
/// Command to join an existing game lobby.
/// </summary>
public record JoinLobbyCommand(
    Guid LobbyId,
    Guid PlayerId) : ICommand<LobbyResponse>;
