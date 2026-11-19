using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NetRts.Bots.Pickup;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Matches;
using Microsoft.AspNetCore.Hosting;
using NetRts.Server.Pickup;

namespace NetRts.Server.Tests;

/// <summary>Fixes from the branch review: nobody pauses the hill, the line has a ceiling, phones on one network don't share a budget, and the podium agrees with the winner.</summary>
public class PickupReviewFixTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PickupPlanDto Sitter = new() { Forever = ["Worker"], Attack = new() { Army = 60 } };

    [Fact]
    public async Task Nobody_can_pause_or_slow_a_pickup_match()
    {
        var kit = new StartKit(0, [], [BuildingType.Barracks]);
        var host = factory.Matches.CreatePickup(
        [
            (new Seat(Guid.NewGuid(), "Lone-Student", IsHouseBot: false, IsBlockBot: true), new PickupBot("Lone-Student", Sitter, 120, 250), kit),
            (new Seat(Guid.NewGuid(), "Honey Badger (house)", IsHouseBot: true), new PickupBot("Honey Badger (house)", Sitter, 120, 250), kit),
        ]);

        var paused = await factory.CreateClient().PostAsJsonAsync($"/api/v1/matches/{host.Id}/speed", new MatchSpeedRequest { Paused = true }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, paused.StatusCode);
        Assert.False(host.Paused);
        host.Stop();
    }

    [Fact]
    public void The_line_has_a_ceiling()
    {
        var hill = new PickupHill(factory.Matches, NullLogger<PickupHill>.Instance);
        var now = DateTime.UtcNow;
        for (var i = 0; i < PickupHill.MaxEntries; i++)
        {
            hill.Join(null, now);
        }

        var full = Assert.Throws<MatchException>(() => hill.Join(null, now));
        Assert.Equal(503, full.Status);
        Assert.Equal("LINE_FULL", full.Code);
    }

    [Fact]
    public void The_winner_heads_the_standings_even_with_a_lower_score()
    {
        PickupPlayerDto P(string name, int score) => new() { Name = name, Score = score };

        var standings = PickupHill.Standings([P("A", 900), P("B", 400), P("C", 700)], winner: "B");

        Assert.Equal(["B", "A", "C"], standings.Select(p => p.Name));
    }
}

/// <summary>A server with production's request limit (the shared test server turns it off).</summary>
public sealed class RateLimitedServerFactory : ServerFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("NetRts:RequestsPerSecond", "30");
    }
}

public class PickupRateLimitTests(RateLimitedServerFactory factory) : IClassFixture<RateLimitedServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Phones_behind_one_address_each_get_their_own_budget()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 90; i++)
        {
            statuses.Add((await client.GetAsync($"/api/v1/pickup/me?token=phone{i}", Ct)).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }
}
