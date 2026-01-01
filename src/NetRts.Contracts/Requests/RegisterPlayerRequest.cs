namespace NetRts.Contracts.Requests;

/// <summary>
/// Request DTO for player registration.
/// </summary>
public class RegisterPlayerRequest
{
    /// <summary>
    /// Unique display name (3-20 characters, alphanumeric and underscores only).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Contact email (optional).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether this player is a bot (automated client).
    /// </summary>
    public bool IsBot { get; set; }
}
