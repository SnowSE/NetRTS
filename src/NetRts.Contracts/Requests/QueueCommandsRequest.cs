namespace NetRts.Contracts.Requests;

/// <summary>
/// Request to queue multiple commands for execution.
/// </summary>
public class QueueCommandsRequest
{
    public required CommandDto[] Commands { get; init; }
}
