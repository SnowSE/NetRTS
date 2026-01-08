namespace NetRts.Contracts.Responses;

/// <summary>
/// Represents a player's final match results with score breakdown.
/// </summary>
public class PlayerMatchResult
{
    /// <summary>
    /// Player's unique identifier.
    /// </summary>
    public Guid PlayerId { get; set; }

    /// <summary>
    /// Points from destroying enemy units (10 points each).
    /// </summary>
    public int UnitsDestroyedScore { get; set; }

    /// <summary>
    /// Points from destroying enemy buildings (50 points each).
    /// </summary>
    public int BuildingsDestroyedScore { get; set; }

    /// <summary>
    /// Points from gathered resources (1 point per 10 resources).
    /// </summary>
    public int ResourcesGatheredScore { get; set; }

    /// <summary>
    /// Points for units alive at match end (5 points each).
    /// </summary>
    public int UnitsRemainingScore { get; set; }

    /// <summary>
    /// Points for buildings standing at match end (25 points each).
    /// </summary>
    public int BuildingsRemainingScore { get; set; }

    /// <summary>
    /// Total score (sum of all components).
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// True if this player won the match.
    /// </summary>
    public bool IsWinner { get; set; }
}
