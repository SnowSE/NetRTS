using NetRts.Application.Abstractions;

namespace NetRts.Application.Commands.CreateMatch;

/// <summary>
/// Command to create a new match from a lobby.
/// </summary>
public record CreateMatchCommand(Guid LobbyId) : ICommand<Guid>;
