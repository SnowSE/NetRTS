using NetRts.Application.Abstractions;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.RegisterPlayer;

/// <summary>
/// Command to register a new player.
/// </summary>
public record RegisterPlayerCommand(RegisterPlayerRequest Request) : ICommand<PlayerResponse>;
