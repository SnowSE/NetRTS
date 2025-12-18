namespace NetRts.Domain.Entities;

/// <summary>
/// Leaderboard entry for a single player.
/// </summary>
public class PlayerScore
{
    public Guid PlayerId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public int TotalScore { get; private set; }
    public int MatchesPlayed { get; private set; }
    public int MatchesWon { get; private set; }
    public decimal WinRate { get; private set; }
    public int AverageScore { get; private set; }
    public int Rank { get; private set; }

    private PlayerScore() { }

    public PlayerScore(Guid playerId, string username)
    {
        PlayerId = playerId;
        Username = username;
    }

    public void UpdateStats(int matchScore, bool won)
    {
        TotalScore += matchScore;
        MatchesPlayed++;
        if (won) MatchesWon++;
        RecalculateStats();
    }

    private void RecalculateStats()
    {
        WinRate = MatchesPlayed > 0 ? (decimal)MatchesWon / MatchesPlayed * 100 : 0;
        AverageScore = MatchesPlayed > 0 ? TotalScore / MatchesPlayed : 0;
    }

    public void SetRank(int rank) => Rank = rank;
}
