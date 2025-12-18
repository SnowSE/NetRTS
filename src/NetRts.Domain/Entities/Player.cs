namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a competitor (human or bot) across multiple matches.
/// Aggregate root for player-related operations.
/// </summary>
public class Player
{
    /// <summary>
    /// Unique player identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Display name (unique, 3-20 characters).
    /// </summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>
    /// Contact email (optional for bots).
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>
    /// Distinguishes automated vs human players.
    /// </summary>
    public bool IsBot { get; private set; }

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Last API call timestamp.
    /// </summary>
    public DateTime LastActiveAt { get; private set; }

    /// <summary>
    /// Lifetime match count.
    /// </summary>
    public int TotalMatches { get; private set; }

    /// <summary>
    /// Lifetime win count.
    /// </summary>
    public int TotalWins { get; private set; }

    /// <summary>
    /// Elo rating for matchmaking (default: 1200).
    /// </summary>
    public int CurrentElo { get; private set; }

    // EF Core constructor
    private Player() { }

    public Player(string username, string? email, bool isBot)
    {
        Id = Guid.NewGuid();
        Username = ValidateUsername(username);
        Email = email;
        IsBot = isBot;
        CreatedAt = DateTime.UtcNow;
        LastActiveAt = DateTime.UtcNow;
        TotalMatches = 0;
        TotalWins = 0;
        CurrentElo = 1200; // Starting Elo
    }

    /// <summary>
    /// Update last active timestamp.
    /// </summary>
    public void RecordActivity()
    {
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Increment match statistics.
    /// </summary>
    public void RecordMatchPlayed(bool won, int eloChange)
    {
        TotalMatches++;
        if (won)
        {
            TotalWins++;
        }
        CurrentElo += eloChange;
    }

    /// <summary>
    /// Validate username meets requirements.
    /// </summary>
    private static string ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username cannot be empty.", nameof(username));
        }

        if (username.Length < 3 || username.Length > 20)
        {
            throw new ArgumentException("Username must be 3-20 characters.", nameof(username));
        }

        if (!username.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException("Username must contain only letters, digits, and underscores.", nameof(username));
        }

        return username;
    }

    /// <summary>
    /// Calculate win rate as a percentage.
    /// </summary>
    public decimal GetWinRate()
    {
        if (TotalMatches == 0) return 0;
        return (decimal)TotalWins / TotalMatches * 100;
    }
}
