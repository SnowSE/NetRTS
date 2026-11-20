using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

/// <summary>The scoreboard's ▲/▼: each bot's rank just before the latest league match went on the ladder.</summary>
public class RankChangeTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MatchPlayerRecord Heat(Guid player, Guid league, int points, DateTime at, bool counted) => new()
    {
        MatchId = Guid.NewGuid(),
        PlayerId = player,
        LeagueMatchId = league,
        Category = MatchCategory.Championship,
        CompletedAt = at,
        Points = points,
        PointsCounted = counted,
        Result = MatchResultKind.Win,
    };

    private async Task<Dictionary<Guid, LeaderboardEntryDto>> LadderAsync() =>
        (await factory.CreateClient().GetFromJsonAsync<List<LeaderboardEntryDto>>("/api/v1/leaderboard?category=Championship", NetRtsClient.JsonOptions, Ct))!
            .ToDictionary(e => e.PlayerId);

    [Fact]
    public async Task Closing_a_league_match_keeps_who_moved_and_the_backfill_works_it_out_again()
    {
        var (_, leader) = await factory.NewPlayerAsync("rank-a");
        var (_, climber) = await factory.NewPlayerAsync("rank-b");
        var (_, rookie) = await factory.NewPlayerAsync("rank-c");
        var earlier = Guid.NewGuid();
        var latest = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            // Before the latest match: the leader 100, the climber 50. The climber and the rookie play it
            // (their wins are already on; the points wait for the match to close).
            db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = leader.PlayerId, Category = MatchCategory.Championship, Wins = 1, Points = 100 });
            db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = climber.PlayerId, Category = MatchCategory.Championship, Wins = 2, Points = 50 });
            db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = rookie.PlayerId, Category = MatchCategory.Championship, Wins = 1 });
            MatchPlayerRecord[] heats =
            [
                Heat(leader.PlayerId, earlier, 100, t, counted: true),
                Heat(climber.PlayerId, earlier, 50, t, counted: true),
                Heat(climber.PlayerId, latest, 80, t.AddHours(1), counted: false),
                Heat(rookie.PlayerId, latest, 10, t.AddHours(1), counted: false),
            ];
            foreach (var h in heats)
            {
                db.Matches.Add(new MatchRecord { Id = h.MatchId, CreatedAt = h.CompletedAt, CompletedAt = h.CompletedAt, Category = h.Category, SummaryJson = "{}" });
            }

            db.MatchPlayers.AddRange(heats);
            await db.SaveChangesAsync(Ct);
        }

        Assert.NotNull(await factory.Services.GetRequiredService<LeagueFinalizer>().FinalizeAsync(latest, Ct));
        var ladder = await LadderAsync();
        Assert.Equal((1, 2), (ladder[climber.PlayerId].Rank, ladder[climber.PlayerId].PreviousRank));   // ▲ 1
        Assert.Equal((2, 1), (ladder[leader.PlayerId].Rank, ladder[leader.PlayerId].PreviousRank));     // ▼ 1
        Assert.Equal(3, ladder[rookie.PlayerId].Rank);
        Assert.Null(ladder[rookie.PlayerId].PreviousRank);                                               // new

        // A database that had league matches before ranks were kept works the same out from the history.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            await db.PlayerStats.Where(s => s.Category == MatchCategory.Championship).ExecuteUpdateAsync(u => u.SetProperty(s => s.PreviousRank, (int?)null), Ct);
            await RankChanges.BackfillAsync(db, scope.ServiceProvider.GetRequiredService<IOptions<DiscordOptions>>(), NullLogger.Instance, Ct);
        }

        ladder = await LadderAsync();
        Assert.Equal(2, ladder[climber.PlayerId].PreviousRank);
        Assert.Equal(1, ladder[leader.PlayerId].PreviousRank);
        Assert.Null(ladder[rookie.PlayerId].PreviousRank);
    }

    [Fact]
    public async Task The_practice_ladder_has_no_previous_ranks()
    {
        var practice = await factory.CreateClient().GetFromJsonAsync<List<LeaderboardEntryDto>>("/api/v1/leaderboard", NetRtsClient.JsonOptions, Ct);
        Assert.All(practice!, e => Assert.Null(e.PreviousRank));
    }
}
