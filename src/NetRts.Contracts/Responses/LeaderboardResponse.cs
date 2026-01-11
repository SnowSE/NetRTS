namespace NetRts.Contracts.Responses;

/// <summary>
/// Response for leaderboard queries.
/// </summary>
public class LeaderboardResponse
{
    public List<PlayerScoreDto> Entries { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Data for a single player on the leaderboard.
/// </summary>
public class PlayerScoreDto
{
    public int Rank { get; init; }
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public int TotalScore { get; init; }
    public int MatchesPlayed { get; init; }
    public int TotalWins { get; init; }
    public double WinRate { get; init; }
}
