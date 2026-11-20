using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Stats;

namespace NetRts.Server.Tests;

/// <summary>The Stats page's data: every match from the archive, and the server's own telemetry.</summary>
public class StatsTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MatchRecord Match(DateTime at, Guid? winner, params MatchPlayerDto[] players) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = at,
        CompletedAt = at.AddMinutes(2),
        Ticks = 300,
        TickIntervalMs = 100,
        Reason = "Elimination",
        WinnerId = winner,
        Category = MatchCategory.Practice,
        SummaryJson = JsonSerializer.Serialize(new MatchSummaryDto { Players = players }, NetRtsClient.JsonOptions),
    };

    [Fact]
    public async Task Matches_list_each_bot_once_and_name_players_by_their_place()
    {
        var house = new MatchPlayerDto { PlayerId = Guid.NewGuid(), Name = "house-sitter", Slot = 1, IsHouseBot = true };
        var block = new MatchPlayerDto { PlayerId = Guid.NewGuid(), Name = "stats-blocky", Slot = 0, IsBlockBot = true };
        var coder = new MatchPlayerDto { PlayerId = Guid.NewGuid(), Name = "stats-coder", Slot = 0 };
        var t = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);   // older than any other test's matches
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            db.Matches.Add(Match(t.AddHours(1), house.PlayerId, coder, house));
            db.Matches.Add(Match(t, block.PlayerId, house, block));
            db.Matches.Add(Match(t.AddHours(2), null, coder with { Name = "stats-coder-2" }, block with { Slot = 1 }));
            await db.SaveChangesAsync(Ct);
        }

        var stats = (await factory.CreateClient().GetFromJsonAsync<MatchStatsDto>("/api/v1/stats/matches", NetRtsClient.JsonOptions, Ct))!;
        var ours = stats.Matches.Where(m => m.At.Year == 2020).ToList();
        Assert.Equal(3, ours.Count);
        string Name(StatsMatchDto m, int i) => stats.Bots[m.Players[i]].Name;

        // Oldest first, players by slot, the winner by their place in the match.
        Assert.Equal(["stats-blocky", "house-sitter"], [Name(ours[0], 0), Name(ours[0], 1)]);
        Assert.Equal(0, ours[0].Winner);
        Assert.Equal(1, ours[1].Winner);
        Assert.Equal(-1, ours[2].Winner);
        Assert.Equal("Elimination", ours[1].Reason);

        // One entry per bot, under its latest name, with who plays it.
        Assert.Same(stats.Bots[ours[1].Players[0]], stats.Bots[ours[2].Players[0]]);
        Assert.Equal("stats-coder-2", Name(ours[1], 0));
        Assert.Equal(["block", "house", "code"], new[] { ours[0].Players[0], ours[0].Players[1], ours[1].Players[0] }.Select(i => stats.Bots[i].Kind));
    }

    [Fact]
    public async Task Server_stats_are_unavailable_until_the_telemetry_has_been_read()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/stats/server", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("NOT_AVAILABLE", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Server_stats_read_through_the_end_of_yesterday_and_add_up_the_telemetry()
    {
        static JsonElement[] Row(params object?[] cells) => cells.Select(c => JsonSerializer.SerializeToElement(c)).ToArray();
        DateTimeOffset? asked = null;
        Task<IReadOnlyList<JsonElement[]>> Fake(string kql, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        {
            asked = to;
            IReadOnlyList<JsonElement[]> rows =
                kql.Contains("min(TimeGenerated)") ? [Row("2026-10-09T15:54:53Z")]
                : kql.Contains("http.response.status_code") ? [Row("200", 900), Row("404", 90), Row("500", 10)]
                : kql.Contains("commands = ") ? [Row(30, 60)]
                : kql.Contains("bin(TimeGenerated, 1m)") ? [Row("2026-10-27T21:27:00Z", 9996)]
                : kql.Contains("percentile") ? [Row(0.31, 16.2)]
                : kql.Contains("netrts.tick.duration") ? [Row(1000, 2140.0, 4, 9)]
                : kql.Contains("AppExceptions") ? [Row(38, 30)]
                : kql.Contains("Application started") ? [Row(120)]
                : kql.Contains("db.client.operation.duration") ? [Row(400, 1.0, 400.0 * 1048576, 600.0 * 1048576)]
                : kql.Contains("dcount(Location)") ? [Row(3)]
                : kql.Contains("AppAvailabilityResults") ? [Row("2026-11-13", 522, 522), Row("2026-11-14", 864, 863)]
                : kql.Contains("hourofday") ? [Row("2026-11-14", 4, 53939L)]
                : [];
            return Task.FromResult(rows);
        }

        var now = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);   // 8 a.m. in Utah
        var stats = await ServerStatsBuilder.BuildAsync(Fake, new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero), "America/Denver", now, Ct);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero), asked);
        Assert.Equal(1000, stats.Requests);
        Assert.Contains(new StatsCountDto("500", 10), stats.StatusCodes);
        Assert.Equal(2.14, stats.TickAverageMs, 3);
        Assert.Equal(9, stats.MostLiveMatches);
        Assert.Equal(2.5, stats.DatabaseAverageMs, 3);   // seconds in, milliseconds out
        Assert.Equal(400, stats.MemoryAverageMb, 3);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 15, 54, 53, TimeSpan.Zero), stats.From);
        Assert.Equal(9996, stats.PeakMinuteRequests);
        Assert.Equal(863, stats.Health[1].Passed);
        Assert.Equal(new StatsHourDto("2026-11-14", 4, 53939), Assert.Single(stats.Hours));
        Assert.Equal((30, 60), (stats.Commands, stats.StatePolls));
    }

    [Fact]
    public async Task The_morning_refresh_stops_at_local_midnight()
    {
        var sp = factory.Services;   // its own instance, so the shared one stays unread for the test above
        var service = new ServerStatsService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetRtsOptions>>(),
            sp.GetRequiredService<IHttpClientFactory>(), TimeProvider.System, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServerStatsService>>());
        DateTimeOffset to = default;
        await service.RefreshAsync((kql, f, t, ct) => { to = t; return Task.FromResult<IReadOnlyList<JsonElement[]>>([]); }, Ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var local = TimeZoneInfo.ConvertTime(to, zone);
        Assert.Equal(TimeSpan.Zero, local.TimeOfDay);
        Assert.InRange(DateTimeOffset.UtcNow - to, TimeSpan.Zero, TimeSpan.FromDays(1));
        Assert.NotNull(service.Current);
        Assert.Equal(0, service.Current.Requests);
    }
}
