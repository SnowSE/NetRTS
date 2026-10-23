namespace NetRts.Server.Data;

/// <summary>A skill estimate: the mean <see cref="Mu"/> and its uncertainty <see cref="Sigma"/>.</summary>
public readonly record struct SkillRating(double Mu, double Sigma);

/// <summary>
/// OpenSkill: the Weng-Lin Bayesian rating model with the Plackett-Luce ranking likelihood (Weng &amp; Lin,
/// "A Bayesian Approximation Method for Online Ranking", JMLR 12, 2011, Algorithm 4), as implemented by
/// openskill.py / openskill.js (<c>PlackettLuce</c>), for teams of one. Unlike Elo, a result moves a rating
/// by how surprising it was given everyone's skill <em>and</em> uncertainty, so beating strong bots
/// counts for more than beating weak ones, whoever you happen to be seated with.
/// </summary>
public static class OpenSkill
{
    public const double DefaultMu = 25.0;
    public const double DefaultSigma = DefaultMu / 3;

    /// <summary>Performance noise: how much one game's result varies around a bot's skill.</summary>
    public const double Beta = DefaultSigma / 2;

    /// <summary>Added (in quadrature) to every sigma before each game, so ratings never freeze.</summary>
    public const double Tau = DefaultMu / 300;

    /// <summary>Floor on the variance shrink factor, so sigma never collapses to zero.</summary>
    public const double Kappa = 0.0001;

    /// <summary>The multiplier from the conservative estimate (mu - 3 sigma) to the skill bots see.</summary>
    public const int DisplayScale = 40;

    public static SkillRating Initial => new(DefaultMu, DefaultSigma);

    /// <summary>A ladder row's skill; rows that predate skills (sigma 0, before <see cref="SkillBackfill"/>) start fresh.</summary>
    public static SkillRating Of(PlayerStatsRecord stats) =>
        stats.Sigma > 0 ? new SkillRating(stats.Mu, stats.Sigma) : Initial;

    /// <summary>
    /// The skill shown on ladders: the conservative estimate mu - 3 sigma (the model is ~99% sure the
    /// bot is at least this good), times <see cref="DisplayScale"/>, rounded, and never below zero. New
    /// bots start at 0 (25 - 3 x 25/3); a consistently strong bot climbs past 1000.
    /// </summary>
    public static int Display(double mu, double sigma) =>
        sigma <= 0 ? 0 : Math.Max(0, (int)Math.Round((mu - 3 * sigma) * DisplayScale, MidpointRounding.AwayFromZero));

    /// <summary>
    /// New ratings after one game, from each player's finishing place (1 = best; equal places tie).
    /// </summary>
    public static SkillRating[] Rate(IReadOnlyList<SkillRating> ratings, IReadOnlyList<int> places, double tau = Tau)
    {
        var n = ratings.Count;
        if (places.Count != n)
        {
            throw new ArgumentException("Every player needs a place.", nameof(places));
        }

        if (n < 2)
        {
            return ratings.ToArray();
        }

        // Dynamics: a little extra uncertainty before every game.
        var mu = new double[n];
        var variance = new double[n];
        for (var i = 0; i < n; i++)
        {
            mu[i] = ratings[i].Mu;
            variance[i] = ratings[i].Sigma * ratings[i].Sigma + tau * tau;
        }

        var c = Math.Sqrt(variance.Sum() + n * Beta * Beta);
        var strength = mu.Select(m => Math.Exp(m / c)).ToArray();

        // sumQ[q]: total strength of everyone who finished at or below q. a[q]: how many share q's place.
        var sumQ = new double[n];
        var a = new int[n];
        for (var q = 0; q < n; q++)
        {
            for (var i = 0; i < n; i++)
            {
                if (places[i] >= places[q])
                {
                    sumQ[q] += strength[i];
                }

                if (places[i] == places[q])
                {
                    a[q]++;
                }
            }
        }

        var result = new SkillRating[n];
        for (var i = 0; i < n; i++)
        {
            double omega = 0, delta = 0;
            for (var q = 0; q < n; q++)
            {
                if (places[q] > places[i])
                {
                    continue;
                }

                var p = strength[i] / sumQ[q];
                delta += p * (1 - p) / a[q];
                omega += (q == i ? 1 - p : -p) / a[q];
            }

            var gamma = Math.Sqrt(variance[i]) / c;
            omega *= variance[i] / c;
            delta *= gamma * variance[i] / (c * c);
            result[i] = new SkillRating(
                mu[i] + omega,
                Math.Sqrt(variance[i]) * Math.Sqrt(Math.Max(1 - delta, Kappa)));
        }

        return result;
    }

    /// <summary>
    /// Finishing places from finishing order: the winner first, then everyone by score; equal scores
    /// share a place. The same rule as the match history's places.
    /// </summary>
    public static int[] Places(IReadOnlyList<(bool Winner, int Score)> players) =>
        players.Select(p => 1 + players.Count(q => (q.Winner && !p.Winner) || (q.Winner == p.Winner && q.Score > p.Score))).ToArray();
}
