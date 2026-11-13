using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NetRts.Protocol;
using NetRts.Server.Health;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

/// <summary>/health: what the uptime test and the deploy check read.</summary>
public class HealthTests(TickingServerFactory factory) : IClassFixture<TickingServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The clock, some time from now.</summary>
    private sealed class Later(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }

    [Fact]
    public async Task Health_lists_each_check_and_the_running_commit()
    {
        var response = await factory.CreateClient().GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("commit").GetString()));
        Assert.Equal(Extensions.Commit, root.GetProperty("commit").GetString());
        var checks = root.GetProperty("checks");
        foreach (var name in new[] { "self", "database", "matches", "saving-matches" })
        {
            Assert.Equal("Healthy", checks.GetProperty(name).GetProperty("status").GetString());
        }

        var live = await factory.CreateClient().GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task A_running_match_that_stops_ticking_is_unhealthy_but_a_paused_one_is_not()
    {
        var (client, _) = await factory.NewPlayerAsync("health");
        var match = await client.CreateMatchAsync(new CreateMatchRequest
        {
            HouseBots = ["sitter"],
            Settings = new MatchSettingsDto { TickIntervalMs = 50 },
        }, Ct);
        await client.WaitForStartAsync(match.MatchId, Ct);
        var host = factory.Matches.Get(match.MatchId)!;
        try
        {
            var now = await Check(TimeProvider.System);
            Assert.Equal(HealthStatus.Healthy, now.Status);

            // Seen from two minutes on, with no tick since: frozen, as after a crashed tick loop.
            var frozen = await Check(new Later(TimeSpan.FromMinutes(2)));
            Assert.Equal(HealthStatus.Unhealthy, frozen.Status);
            Assert.Contains(match.MatchId.ToString(), frozen.Description);

            // A paused match isn't expected to tick.
            Assert.Null(host.SetSpeed(null, paused: true, DateTime.UtcNow));
            Assert.Equal(HealthStatus.Healthy, (await Check(new Later(TimeSpan.FromMinutes(2)))).Status);
        }
        finally
        {
            await client.SurrenderAsync(match.MatchId, Ct);
        }
    }

    [Fact]
    public void A_match_counts_as_stalled_after_ten_missed_ticks_and_never_under_half_a_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), MatchTicksHealthCheck.StallAfter(50));
        Assert.Equal(TimeSpan.FromSeconds(50), MatchTicksHealthCheck.StallAfter(5000));
    }

    private Task<HealthCheckResult> Check(TimeProvider time) =>
        new MatchTicksHealthCheck(factory.Matches, time).CheckHealthAsync(new HealthCheckContext(), Ct);
}
