namespace NetRts.Server.Data;

public static class Elo
{
    public const int InitialRating = 1200;
    private const double K = 32;

    /// <summary>New ratings after a 1v1. <paramref name="scoreA"/> is 1 for an A win, 0.5 for a draw, 0 for a loss.</summary>
    public static (int A, int B) Update(int ratingA, int ratingB, double scoreA)
    {
        var expectedA = 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));
        var delta = (int)Math.Round(K * (scoreA - expectedA));
        return (ratingA + delta, ratingB - delta);
    }
}
