using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Application.Services;

/// <summary>
/// Service for queueing and validating player commands.
/// </summary>
public interface ICommandQueueService
{
    /// <summary>
    /// Queue commands for a player in a match.
    /// </summary>
    Task<QueueCommandsResponse> QueueCommandsAsync(
        Guid matchId,
        Guid playerId,
        QueueCommandsRequest request,
        CancellationToken cancellationToken = default);
}
