namespace NetRts.Contracts.Responses;

/// <summary>
/// Response DTO for player registration and profile.
/// </summary>
public class PlayerResponse
{
    /// <summary>
    /// Unique player identifier.
    /// </summary>
    public Guid PlayerId { get; set; }

    /// <summary>
    /// Display name.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Contact email (optional).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether this player is a bot.
    /// </summary>
    public bool IsBot { get; set; }

    /// <summary>
    /// JWT token for authentication (only included on registration).
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Total matches played.
    /// </summary>
    public int TotalMatches { get; set; }

    /// <summary>
    /// Total matches won.
    /// </summary>
    public int TotalWins { get; set; }

    /// <summary>
    /// Current Elo rating.
    /// </summary>
    public int CurrentElo { get; set; }

    /// <summary>
    /// Win rate as percentage.
    /// </summary>
    public decimal WinRate { get; set; }
}
