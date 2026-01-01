namespace NetRts.Contracts.Requests;

/// <summary>
/// Request to queue multiple commands for execution in the next game tick.
/// </summary>
public class QueueCommandsRequest
{
    /// <summary>
    /// Array of commands to be queued.
    /// </summary>
    public CommandDto[] Commands { get; set; } = Array.Empty<CommandDto>();
}
