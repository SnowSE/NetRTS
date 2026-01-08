namespace NetRts.Contracts.Responses;

/// <summary>
/// Represents the final results of a completed match.
/// </summary>
public class MatchResultResponse
{
    /// <summary>
    /// Unique match identifier.
    /// </summary>
    public Guid MatchId { get; set; }

    /// <summary>
    /// Match status (should be "Completed").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// ID of the winning player.
    /// </summary>
    public Guid WinnerId { get; set; }

    /// <summary>
    /// Match duration in game ticks.
    /// </summary>
    public int DurationTicks { get; set; }

    /// <summary>
    /// Match duration in real-world seconds (approximate).
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Player 1's match results and score breakdown.
    /// </summary>
    public PlayerMatchResult Player1Result { get; set; } = new();

    /// <summary>
    /// Player 2's match results and score breakdown.
    /// </summary>
    public PlayerMatchResult Player2Result { get; set; } = new();

    /// <summary>
    /// Reason for match end (e.g., "TimeLimit", "Elimination", "Resignation").
    /// </summary>
    public string EndReason { get; set; } = string.Empty;
}
