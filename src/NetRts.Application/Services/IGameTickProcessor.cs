namespace NetRts.Application.Services;

/// <summary>
/// Service for processing game ticks for all active matches.
/// </summary>
public interface IGameTickProcessor
{
    /// <summary>
    /// Process a single tick for all active matches.
    /// </summary>
    Task ProcessTickAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a single tick for a specific match.
    /// </summary>
    Task ProcessMatchTickAsync(Guid matchId, CancellationToken cancellationToken = default);
}
