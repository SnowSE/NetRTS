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

    /// <summary>
    /// New ratings after a match of any size, from each player's finishing place (1 = best; equal places
    /// draw). Every pair counts as a 1v1, scaled by 1/(n-1) so a big match moves a rating about as much
    /// as one 1v1; with two players it is exactly <see cref="Update"/>.
    /// </summary>
    public static int[] UpdateMany(IReadOnlyList<int> ratings, IReadOnlyList<int> places)
    {
        var n = ratings.Count;
        var result = ratings.ToArray();
        if (n < 2)
        {
            return result;
        }

        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var expected = 1.0 / (1.0 + Math.Pow(10, (ratings[j] - ratings[i]) / 400.0));
                var actual = places[i] < places[j] ? 1.0 : places[i] == places[j] ? 0.5 : 0.0;
                sum += actual - expected;
            }

            result[i] = ratings[i] + (int)Math.Round(K * sum / (n - 1));
        }

        return result;
    }
}
