namespace NetRts.Server.Data;

/// <summary>
/// Preseason, season and championship points: every bot earns its whole score in each heat it plays.
/// (Until November 2026 only the top four scored, their margins over the bots below them in that four;
/// <see cref="ScorePointsBackfill"/> recounted the ladders when that changed.)
/// </summary>
public static class LeaguePoints
{
    /// <summary>Points for each player of a match, given their final scores: the score itself (never negative).</summary>
    public static int[] Award(IReadOnlyList<int> scores) => [.. scores.Select(s => Math.Max(0, s))];

    /// <summary>
    /// What one bot's heats of one league match add to its season total: their points, less the lowest
    /// heat's when it played two or more (one bad seating doesn't sink a night). A single heat counts in full.
    /// </summary>
    public static int Night(IReadOnlyCollection<int> heatPoints) =>
        heatPoints.Count < 2 ? heatPoints.Sum() : heatPoints.Sum() - heatPoints.Min();
}
