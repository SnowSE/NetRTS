namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the current status of a match.
/// </summary>
public enum MatchStatus
{
    /// <summary>
    /// Match has been created but not yet started.
    /// </summary>
    Pending,

    /// <summary>
    /// Match is currently in progress.
    /// </summary>
    Active,

    /// <summary>
    /// Match has finished with a winner determined.
    /// </summary>
    Completed,

    /// <summary>
    /// Match was abandoned (e.g., player disconnect).
    /// </summary>
    Abandoned
}
