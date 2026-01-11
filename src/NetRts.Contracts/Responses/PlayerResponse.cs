namespace NetRts.Contracts.Responses;

/// <summary>
/// Response after player registration or profile retrieval.
/// </summary>
public class PlayerResponse
{
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }
    public bool IsBot { get; init; }
    public int CurrentElo { get; init; }
    public decimal WinRate { get; init; }
    public string? Token { get; init; } // Only provided after registration/login
    public int TotalMatches { get; init; }
    public int TotalWins { get; init; }
    public DateTime CreatedAt { get; init; }
}