using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

/// <summary>OpenSkill skills, and league nights that drop each bot's lowest heat.</summary>
public class SkillTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private const double Precision = 1e-9;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Reference values. The 1v1 is worked by hand from Weng & Lin (2011), Algorithm 4 (Plackett-Luce),
    // with beta = 25/6 and no tau: c^2 = 2 (sigma^2 + beta^2) = 173.6111, so c = 13.17616. Both players
    // have the same strength, so the winner's win probability is 1/2: omega = +1/2 (loser -1/2), delta = 1/4,
    // gamma = sigma / c. mu' = 25 ± (sigma^2 / c) / 2 = 25 ± 2.6352314, and
    // sigma' = sigma * sqrt(1 - gamma * delta * sigma^2 / c^2) = 8.3333 * sqrt(1 - 0.6325 * 0.25 * 0.4) = 8.0655063.
    // The other cases were computed with a line-by-line Python transcription of openskill.py's
    // PlackettLuce._compute (teams of one, kappa = 0.0001, tau = 25/300 added to sigma in quadrature
    // first), which also reproduces the hand-worked 1v1 above when tau is 0.
    [Fact]
    public void A_1v1_between_new_players_matches_the_hand_computed_update()
    {
        var after = OpenSkill.Rate([OpenSkill.Initial, OpenSkill.Initial], [1, 2], tau: 0);
        Assert.Equal(27.6352313835, after[0].Mu, Precision);
        Assert.Equal(8.0655063163, after[0].Sigma, Precision);
        Assert.Equal(22.3647686165, after[1].Mu, Precision);
        Assert.Equal(8.0655063163, after[1].Sigma, Precision);
    }

    [Fact]
    public void The_default_tau_adds_a_little_uncertainty_before_each_game()
    {
        var after = OpenSkill.Rate([OpenSkill.Initial, OpenSkill.Initial], [1, 2]);
        Assert.Equal(27.6353894931, after[0].Mu, Precision);
        Assert.Equal(8.0659014135, after[0].Sigma, Precision);
        Assert.Equal(22.3646105069, after[1].Mu, Precision);
    }

    [Fact]
    public void A_free_for_all_with_a_shared_place_matches_the_reference_implementation()
    {
        SkillRating[] before = [new(30, 4), new(25, 25.0 / 3), new(22, 6), new(28, 3)];
        var after = OpenSkill.Rate(before, [2, 1, 2, 4]);
        (double Mu, double Sigma)[] expected =
        [
            (29.7405582622, 3.9800057052),
            (28.8129570713, 8.1842203599),
            (22.2187589926, 5.9251120452),
            (27.5967727466, 2.9949879099),
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Mu, after[i].Mu, Precision);
            Assert.Equal(expected[i].Sigma, after[i].Sigma, Precision);
        }

        // Input order doesn't matter: the same game listed backwards gives the same ratings.
        var reversed = OpenSkill.Rate(Enumerable.Reverse(before).ToArray(), [4, 2, 1, 2]);
        Assert.Equal(Enumerable.Reverse(after).Select(r => Math.Round(r.Mu, 9)), reversed.Select(r => Math.Round(r.Mu, 9)));

        // A three-way tie between equals moves nobody's mean, but everyone grows surer.
        var tie = OpenSkill.Rate([OpenSkill.Initial, OpenSkill.Initial, OpenSkill.Initial], [1, 1, 1], tau: 0);
        Assert.All(tie, r => Assert.Equal(25, r.Mu, Precision));
        Assert.All(tie, r => Assert.Equal(8.2048370308, r.Sigma, Precision));
    }

    [Fact]
    public void Skill_is_shown_as_a_conservative_estimate_and_places_follow_finishing_order()
    {
        Assert.Equal(0, OpenSkill.Display(OpenSkill.DefaultMu, OpenSkill.DefaultSigma));
        Assert.Equal(1020, OpenSkill.Display(30, 1.5));
        Assert.Equal(200, OpenSkill.Display(20, 5));
        Assert.Equal(0, OpenSkill.Display(20, 7)); // never below zero
        Assert.Equal(0, OpenSkill.Display(30, 0)); // a row from before skills existed
        Assert.Equal(OpenSkill.Initial, OpenSkill.Of(new PlayerStatsRecord { Mu = 0, Sigma = 0 }));

        // The winner first (even when outscored), then by score; equal scores share a place.
        Assert.Equal([1, 2, 2, 4], OpenSkill.Places([(true, 10), (false, 50), (false, 50), (false, 20)]));
    }

    [Fact]
    public void A_night_drops_the_lowest_heat_only_when_there_are_two_or_more()
    {
        Assert.Equal(0, LeaguePoints.Night([]));
        Assert.Equal(70, LeaguePoints.Night([70]));
        Assert.Equal(0, LeaguePoints.Night([0]));
        Assert.Equal(120, LeaguePoints.Night([120, 40]));
        Assert.Equal(160, LeaguePoints.Night([120, 40, 0]));
        Assert.Equal(80, LeaguePoints.Night([40, 40, 40]));

        // Added heat by heat, in any order, the changes sum to the night's total.
        int[] heats = [30, 90, 10];
        foreach (var order in new[] { new[] { 0, 1, 2 }, [2, 0, 1], [1, 2, 0] })
        {
            var seen = new List<int>();
            var total = 0;
            foreach (var i in order)
            {
                var before = LeaguePoints.Night(seen);
                seen.Add(heats[i]);
                total += LeaguePoints.Night(seen) - before;
            }

            Assert.Equal(120, total);
        }
    }

    [Fact]
    public async Task Recording_a_rated_match_updates_mu_and_sigma_beside_elo()
    {
        var players = await PlayersAsync(2);
        var heat = await PlayHeatAsync(Guid.NewGuid(), MatchCategory.Championship, players, winner: 0);

        var stats = await StatsAsync(MatchCategory.Championship, players);
        var expected = OpenSkill.Rate([OpenSkill.Initial, OpenSkill.Initial], [1, 2]);
        Assert.Equal(expected[0].Mu, stats[0].Mu, Precision);
        Assert.Equal(expected[0].Sigma, stats[0].Sigma, Precision);
        Assert.Equal(expected[1].Mu, stats[1].Mu, Precision);
        Assert.True(stats[0].Rating > Elo.InitialRating); // Elo moves exactly as before
        Assert.Equal(2 * Elo.InitialRating, stats[0].Rating + stats[1].Rating);

        // One heat that night: once the match is closed its points count in full, and the live
        // standings show the same.
        var row = await RowAsync(heat.Id, players[0]);
        Assert.Equal(heat.ParentMatchId, row.LeagueMatchId);
        Assert.True(row.Points > 0);
        Assert.Equal(0, stats[0].Points);
        await factory.Services.GetRequiredService<LeagueFinalizer>().FinalizeAsync(heat.ParentMatchId!.Value, Ct);
        Assert.Equal(row.Points, (await StatsAsync(MatchCategory.Championship, players))[0].Points);
        Assert.Equal(row.Points, LeagueMatchManager.StandingsOf(heat).Players.Single(p => p.PlayerId == players[0].PlayerId).Points);

        var profile = await WithDbAsync(async db =>
            (await PlayerEndpoints.ProfilesAsync(db, new DiscordOptions(), db.Players.Where(p => p.Id == players[0].PlayerId), Ct)).Single());
        var record = profile.Records.Single(r => r.Category == MatchCategory.Championship);
        Assert.Equal(OpenSkill.Display(expected[0].Mu, expected[0].Sigma), record.Skill);
        Assert.True(record.Skill > 0);
    }

    [Theory]
    [InlineData(new[] { 2, 3, 4 })]
    [InlineData(new[] { 4, 2, 3 })]
    [InlineData(new[] { 3, 4, 2 })]
    public async Task A_bot_with_several_heats_in_one_league_match_drops_its_lowest(int[] sizes)
    {
        // The same bot wins a 2-, 3- and 4-player heat of one league match (in the given order), so
        // its heats are worth different points; the others play only some heats.
        var players = await PlayersAsync(4);
        var night = Guid.NewGuid();
        var heats = new List<int>();
        foreach (var size in sizes)
        {
            var heat = await PlayHeatAsync(night, MatchCategory.Season, players.Take(size).ToList(), winner: 0);
            var row = await RowAsync(heat.Id, players[0]);
            heats.Add(row.Points);

            // The live standings show each heat's own points; the ladder waits for the match to close.
            Assert.Equal(row.Points, LeagueMatchManager.StandingsOf(heat).Players[0].Points);
            Assert.False(row.PointsCounted);
            Assert.Equal(0, (await StatsAsync(MatchCategory.Season, players))[0].Points);
        }

        Assert.True(heats.Min() > 0 && heats.Distinct().Count() == 3, $"heats should differ: {string.Join(", ", heats)}");
        var finalizer = factory.Services.GetRequiredService<LeagueFinalizer>();
        var closed = await finalizer.FinalizeAsync(night, Ct);
        Assert.Equal(MatchCategory.Season, closed!.Value.Category);
        Assert.Equal(heats.Sum() - heats.Min(), closed.Value.Totals.Single(t => t.PlayerId == players[0].PlayerId).Points);
        Assert.Equal(heats, closed.Value.Totals.Single(t => t.PlayerId == players[0].PlayerId).HeatPoints);
        var final = await StatsAsync(MatchCategory.Season, players);
        Assert.Equal(heats.Sum() - heats.Min(), final[0].Points);
        Assert.All(final.Skip(1), s => Assert.Equal(0, s.Points)); // losers' heats are worth nothing
        Assert.Null(await finalizer.FinalizeAsync(night, Ct)); // only once

        // A second league match is its own night: one heat there counts in full.
        var next = await PlayHeatAsync(Guid.NewGuid(), MatchCategory.Season, players.Take(2).ToList(), winner: 0);
        var nextPoints = (await RowAsync(next.Id, players[0])).Points;
        await finalizer.FinalizeAsync(next.ParentMatchId!.Value, Ct);
        Assert.Equal(heats.Sum() - heats.Min() + nextPoints, (await StatsAsync(MatchCategory.Season, players))[0].Points);
    }

    [Fact]
    public async Task League_ladders_stay_ranked_by_points_with_skill_alongside()
    {
        var players = await PlayersAsync(2);
        var heat = await PlayHeatAsync(Guid.NewGuid(), MatchCategory.Preseason, players, winner: 0);
        await factory.Services.GetRequiredService<LeagueFinalizer>().FinalizeAsync(heat.ParentMatchId!.Value, Ct);

        // Give the loser the stronger skill but fewer points: points still decide.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            await db.PlayerStats.Where(s => s.PlayerId == players[1].PlayerId && s.Category == MatchCategory.Preseason)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Mu, 40).SetProperty(s => s.Sigma, 2), Ct);
        }

        await using var ladderScope = factory.Services.CreateAsyncScope();
        var ladder = (await PlayerEndpoints.Leaderboard(ladderScope.ServiceProvider.GetRequiredService<NetRtsDb>(),
                ladderScope.ServiceProvider.GetRequiredService<IOptions<DiscordOptions>>(), Ct, MatchCategory.Preseason, limit: 200))
            .Where(e => players.Any(p => p.PlayerId == e.PlayerId)).ToList();
        Assert.Equal([players[0].PlayerId, players[1].PlayerId], ladder.Select(e => e.PlayerId));
        Assert.True(ladder[0].Points > ladder[1].Points);
        Assert.True(ladder[1].Skill > ladder[0].Skill);
        Assert.Equal(OpenSkill.Display(40, 2), ladder[1].Skill);
        Assert.Equal(40, ladder[1].Mu);
        Assert.Equal(2, ladder[1].Sigma);

        // Equal points: skill breaks the tie, though the other bot has the better Elo rating.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var ids = players.Select(p => p.PlayerId).ToList();
            await db.PlayerStats.Where(s => ids.Contains(s.PlayerId) && s.Category == MatchCategory.Preseason)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Points, ladder[0].Points), Ct);
        }

        var tied = (await PlayerEndpoints.Leaderboard(ladderScope.ServiceProvider.GetRequiredService<NetRtsDb>(),
                ladderScope.ServiceProvider.GetRequiredService<IOptions<DiscordOptions>>(), Ct, MatchCategory.Preseason, limit: 200))
            .Where(e => players.Any(p => p.PlayerId == e.PlayerId)).ToList();
        Assert.Equal([players[1].PlayerId, players[0].PlayerId], tied.Select(e => e.PlayerId));
        Assert.True(tied[1].Rating > tied[0].Rating);
    }

    [Fact]
    public async Task Databases_from_before_skills_are_backfilled_once_by_replaying_the_history()
    {
        var players = await PlayersAsync(3);
        var night = Guid.NewGuid();
        await PlayHeatAsync(night, MatchCategory.Season, players, winner: 0);
        await PlayHeatAsync(night, MatchCategory.Season, players.Take(2).ToList(), winner: 1);
        await PlayHeatAsync(Guid.NewGuid(), MatchCategory.Championship, [players[2], players[0]], winner: 0);
        var ids = players.Select(p => p.PlayerId).ToList();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        // Only these players: other tests share the database and may have set skills by hand.
        var stats = await db.PlayerStats.AsNoTracking().Where(s => ids.Contains(s.PlayerId)).ToListAsync(Ct);
        var rows = await db.MatchPlayers.AsNoTracking().ToListAsync(Ct);
        Assert.Contains(rows, r => r.LeagueMatchId == night && r.Points > 0);

        // What the migration leaves behind: sigma (and mu) 0, no per-heat points, no league match ids.
        await db.PlayerStats.ExecuteUpdateAsync(u => u.SetProperty(s => s.Mu, 0).SetProperty(s => s.Sigma, 0), Ct);
        await db.MatchPlayers.ExecuteUpdateAsync(u => u.SetProperty(r => r.Points, 0).SetProperty(r => r.LeagueMatchId, (Guid?)null), Ct);
        var pointsBefore = await db.PlayerStats.AsNoTracking().Where(s => ids.Contains(s.PlayerId)).SumAsync(s => s.Points, Ct);

        Assert.True(await SkillBackfill.RunAsync(db, NullLogger.Instance, Ct));
        Assert.False(await SkillBackfill.RunAsync(db, NullLogger.Instance, Ct)); // only once

        db.ChangeTracker.Clear();
        var replayed = await db.PlayerStats.AsNoTracking().ToDictionaryAsync(s => (s.PlayerId, s.Category, s.Season), Ct);
        foreach (var s in stats)
        {
            var r = replayed[(s.PlayerId, s.Category, s.Season)];
            Assert.Equal(s.Mu, r.Mu, 1e-6);
            Assert.Equal(s.Sigma, r.Sigma, 1e-6);
        }

        Assert.True(replayed.Values.All(s => s.Sigma > 0));
        Assert.Equal(pointsBefore, replayed.Values.Where(s => ids.Contains(s.PlayerId)).Sum(s => s.Points)); // totals untouched
        var refilled = await db.MatchPlayers.AsNoTracking().ToDictionaryAsync(r => (r.MatchId, r.PlayerId), Ct);
        foreach (var row in rows)
        {
            Assert.Equal(row.Points, refilled[(row.MatchId, row.PlayerId)].Points);
            Assert.Equal(row.LeagueMatchId, refilled[(row.MatchId, row.PlayerId)].LeagueMatchId);
        }
    }

    private async Task<List<RegisterPlayerResponse>> PlayersAsync(int count)
    {
        var players = new List<RegisterPlayerResponse>();
        for (var i = 0; i < count; i++)
        {
            players.Add((await factory.NewPlayerAsync("sk")).Player);
        }

        return players;
    }

    /// <summary>Plays a heat of <paramref name="leagueMatchId"/> to the end (everyone else surrenders) and waits until it is recorded.</summary>
    private async Task<MatchHost> PlayHeatAsync(Guid leagueMatchId, MatchCategory category, IReadOnlyList<RegisterPlayerResponse> players, int winner)
    {
        var seats = players.Select(p => new Seat(p.PlayerId, p.Name, IsHouseBot: false)).ToList();
        var heat = factory.Matches.CreateHeat(leagueMatchId, $"heat-{Guid.NewGuid():N}"[..20], category, seats, tickIntervalMs: null, maxTicks: null);
        factory.Matches.StartHeat(heat);
        heat.Advance(5);
        foreach (var (p, i) in players.Select((p, i) => (p, i)).Where(x => x.i != winner))
        {
            Assert.True(heat.Surrender(p.PlayerId));
        }

        Assert.Equal(MatchStatus.Completed, heat.Status);
        for (var i = 0; i < 200 && !heat.Recorded; i++)
        {
            await Task.Delay(25, Ct);
        }

        Assert.True(heat.Recorded);
        return heat;
    }

    private async Task<T> WithDbAsync<T>(Func<NetRtsDb, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<NetRtsDb>());
    }

    /// <summary>Each player's ladder row in the current season (a blank one if they have none), in the order given.</summary>
    private async Task<List<PlayerStatsRecord>> StatsAsync(MatchCategory category, IReadOnlyList<RegisterPlayerResponse> players)
    {
        var ids = players.Select(p => p.PlayerId).ToList();
        var stats = await WithDbAsync(db => db.PlayerStats.AsNoTracking()
            .Where(s => ids.Contains(s.PlayerId) && s.Category == category && s.Season == Seasons.Current).ToListAsync(Ct));
        return players.Select(p => stats.SingleOrDefault(s => s.PlayerId == p.PlayerId) ?? new PlayerStatsRecord { PlayerId = p.PlayerId }).ToList();
    }

    private Task<MatchPlayerRecord> RowAsync(Guid matchId, RegisterPlayerResponse player) =>
        WithDbAsync(db => db.MatchPlayers.AsNoTracking().SingleAsync(r => r.MatchId == matchId && r.PlayerId == player.PlayerId, Ct));
}
