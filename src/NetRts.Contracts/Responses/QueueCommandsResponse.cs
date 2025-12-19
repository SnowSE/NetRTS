namespace NetRts.Contracts.Responses;

/// <summary>
/// Response indicating the result of command queuing.
/// </summary>
public class QueueCommandsResponse
{
    public required int QueuedCount { get; init; }
    public required int FailedCount { get; init; }
    public IReadOnlyList<string>? Failures { get; init; }
}
