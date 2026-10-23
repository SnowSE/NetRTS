namespace NetRts.Server.Data;

/// <summary>
/// Season and championship points. The top four of a match earn their score margins over everyone below
/// them in that four: 3rd gets s3-s4, 2nd gets (s2-s3)+(s2-s4), 1st gets (s1-s2)+(s1-s3)+(s1-s4).
/// Running away with a match pays a lot; scraping a win pays a little.
/// </summary>
public static class LeaguePoints
{
    public const int Counted = 4;

    /// <summary>
    /// Points for players listed in finishing order (winner first) with their final scores. A margin is
    /// never negative: a winner by elimination who outscored nobody still gets 0 from that pair.
    /// </summary>
    public static int[] Award(IReadOnlyList<int> scoresInFinishingOrder)
    {
        var points = new int[scoresInFinishingOrder.Count];
        var top = Math.Min(Counted, scoresInFinishingOrder.Count);
        for (var i = 0; i < top; i++)
        {
            for (var j = i + 1; j < top; j++)
            {
                points[i] += Math.Max(0, scoresInFinishingOrder[i] - scoresInFinishingOrder[j]);
            }
        }

        return points;
    }

    /// <summary>
    /// What one bot's heats of one league match add to its season total: their points, less the lowest
    /// heat's when it played two or more (one bad seating doesn't sink a night). A single heat counts in full.
    /// </summary>
    public static int Night(IReadOnlyCollection<int> heatPoints) =>
        heatPoints.Count < 2 ? heatPoints.Sum() : heatPoints.Sum() - heatPoints.Min();
}
