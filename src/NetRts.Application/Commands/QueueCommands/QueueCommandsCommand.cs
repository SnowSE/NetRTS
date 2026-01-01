using NetRts.Application.Abstractions;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Commands.QueueCommands;

/// <summary>
/// Command to queue multiple commands for execution in a match.
/// </summary>
public record QueueCommandsCommand(
    Guid MatchId,
    Guid PlayerId,
    CommandDto[] Commands) : ICommand<QueueCommandsResponse>;
