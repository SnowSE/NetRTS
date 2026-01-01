namespace NetRts.Contracts.Responses;

/// <summary>
/// Response after queueing commands.
/// </summary>
public class QueueCommandsResponse
{
    /// <summary>
    /// Number of commands successfully queued.
    /// </summary>
    public int QueuedCount { get; set; }

    /// <summary>
    /// Number of commands that failed validation.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// List of validation errors for failed commands.
    /// </summary>
    public List<CommandValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Validation error for a specific command.
/// </summary>
public class CommandValidationError
{
    /// <summary>
    /// Index of the command in the request array.
    /// </summary>
    public int CommandIndex { get; set; }

    /// <summary>
    /// Error message describing why the command failed validation.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;
}
