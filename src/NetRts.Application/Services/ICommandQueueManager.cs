using NetRts.Domain.Entities;

namespace NetRts.Application.Services;

/// <summary>
/// Service for managing command queues per player.
/// </summary>
public interface ICommandQueueManager
{
    /// <summary>
    /// Enqueue a command for processing.
    /// </summary>
    Task<bool> EnqueueCommandAsync(Command command, int maxQueueSize);

    /// <summary>
    /// Dequeue commands for a player up to the limit.
    /// </summary>
    Task<List<Command>> DequeueCommandsAsync(Guid matchId, Guid playerId, int limit);

    /// <summary>
    /// Get current queue size for a player.
    /// </summary>
    Task<int> GetQueueSizeAsync(Guid matchId, Guid playerId);

    /// <summary>
    /// Clear all commands for a match.
    /// </summary>
    Task ClearMatchCommandsAsync(Guid matchId);
}
