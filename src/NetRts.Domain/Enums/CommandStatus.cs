namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the status of a queued command.
/// </summary>
public enum CommandStatus
{
    /// <summary>
    /// Command is waiting in the queue to be executed.
    /// </summary>
    Queued,

    /// <summary>
    /// Command has been executed successfully.
    /// </summary>
    Executed,

    /// <summary>
    /// Command execution failed (e.g., invalid target, insufficient resources).
    /// </summary>
    Failed,

    /// <summary>
    /// Command was cancelled before execution.
    /// </summary>
    Cancelled
}
