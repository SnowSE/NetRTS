using NetRts.Application.Abstractions;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.CreateLobby;

/// <summary>
/// Command to create a new game lobby.
/// </summary>
public record CreateLobbyCommand(
    Guid HostPlayerId,
    CreateLobbyRequest Request) : ICommand<LobbyResponse>;
