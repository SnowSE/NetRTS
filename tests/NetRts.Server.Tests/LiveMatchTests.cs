using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>Matches running on the real tick loop, with a bot playing over HTTP.</summary>
public class LiveMatchTests(TickingServerFactory factory) : IClassFixture<TickingServerFactory>
{
    [Fact]
    public async Task A_bot_plays_a_whole_match_over_http_and_beats_the_sitter()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, player) = await factory.NewPlayerAsync("rusher");
        var match = await client.CreateMatchAsync(new CreateMatchRequest
        {
            HouseBots = ["sitter"],
            Settings = new MatchSettingsDto { TickIntervalMs = 15, Seed = 9 },
        }, ct);

        var result = await BotLoop.RunAsync(client, match.MatchId, HouseBots.Create("rusher"), ct: ct)
            .WaitAsync(TimeSpan.FromMinutes(2), ct);

        Assert.NotNull(result);
        Assert.Equal(player.PlayerId, result.Outcome.WinnerId);
        Assert.Equal(MatchEndReason.Elimination, result.Outcome.Reason);
    }

    [Fact]
    public async Task A_paused_match_stops_ticking_until_it_is_resumed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await factory.NewPlayerAsync();
        var match = await client.CreateMatchAsync(new CreateMatchRequest
        {
            HouseBots = ["sitter"],
            Settings = new MatchSettingsDto { TickIntervalMs = 20 },
        }, ct);
        await client.WaitForStartAsync(match.MatchId, ct);
        var host = factory.Matches.Get(match.MatchId)!;

        Assert.Null(host.SetSpeed(null, paused: true, DateTime.UtcNow));
        await Task.Delay(100, ct); // let a tick already in flight finish
        var pausedAt = host.ToSummary().Tick;
        await Task.Delay(300, ct);
        Assert.Equal(pausedAt, host.ToSummary().Tick);

        Assert.Null(host.SetSpeed(10, paused: false, DateTime.UtcNow));
        await host.WaitForTickAsync(pausedAt + 5, TimeSpan.FromSeconds(10), ct);
        Assert.True(host.ToSummary().Tick >= pausedAt + 5);
    }

    [Fact]
    public async Task Ten_concurrent_matches_all_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var matches = new List<MatchSummaryDto>();
        var (client, _) = await factory.NewPlayerAsync();
        for (var i = 0; i < 10; i++)
        {
            matches.Add(await client.CreateMatchAsync(new CreateMatchRequest
            {
                HouseBots = ["balanced"],
                Settings = new MatchSettingsDto { TickIntervalMs = 50, MaxTicks = 40 },
            }, ct));
        }

        foreach (var m in matches)
        {
            var state = await client.GetStateAsync(m.MatchId, waitForTick: 40, ct: ct);
            Assert.Equal(MatchStatus.Completed, state.Status);
            Assert.Equal(40, state.Tick);
        }
    }
}
