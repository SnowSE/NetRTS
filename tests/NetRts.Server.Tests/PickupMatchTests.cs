using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots.Pickup;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

public class PickupMatchTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PickupPlanDto Sitter = new() { Forever = ["Worker"], Attack = new() { Army = 60 } };

    [Fact]
    public async Task Pickup_matches_are_unlisted_unrecorded_and_cleaned_up()
    {
        var kit = new StartKit(1500, [UnitType.Soldier], [BuildingType.Barracks]);
        var host = factory.Matches.CreatePickup(
        [
            (new Seat(Guid.NewGuid(), "Sneaky-Sagebrush", IsHouseBot: false, IsBlockBot: true), new PickupBot("Sneaky-Sagebrush", Sitter, 300), kit),
            (new Seat(Guid.NewGuid(), "Honey Badger", IsHouseBot: true), new PickupBot("Honey Badger", Sitter, 300), kit),
        ]);

        Assert.True(host.IsPickup);
        Assert.Equal(MatchStatus.Active, host.Status);
        Assert.Equal(MatchManager.PickupTickMs, host.TickIntervalMs);
        var client = factory.CreateClient();
        var listed = await client.GetFromJsonAsync<List<MatchSummaryDto>>("/api/v1/matches", Ct);
        Assert.DoesNotContain(listed!, m => m.MatchId == host.Id);
        var summary = await client.GetFromJsonAsync<MatchSummaryDto>($"/api/v1/matches/{host.Id}", Ct);
        Assert.True(summary!.Pickup);   // the match page shows no replay button for it

        host.Advance(MatchManager.PickupMaxTicks);
        Assert.Equal(MatchStatus.Completed, host.Status);
        await Task.Delay(300, Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            Assert.False(await db.Matches.AnyAsync(m => m.Id == host.Id, Ct));
        }

        factory.Matches.Housekeep(DateTime.UtcNow.AddHours(1));
        Assert.Null(factory.Matches.Get(host.Id));
    }

    [Fact]
    public void A_start_kit_is_in_the_match()
    {
        var kit = new StartKit(0, [UnitType.Soldier, UnitType.Soldier], [BuildingType.Barracks]);
        var host = factory.Matches.CreatePickup(
        [
            (new Seat(Guid.NewGuid(), "A", IsHouseBot: true), new PickupBot("A", Sitter, 300), kit),
            (new Seat(Guid.NewGuid(), "B", IsHouseBot: true), new PickupBot("B", Sitter, 300), kit),
        ]);

        var view = host.GetSpectatorView(null, includeVisibility: false)!;
        Assert.Equal(4, view.Units.Count(u => u.Type == UnitType.Soldier));
        Assert.Equal(2, view.Buildings.Count(b => b.Type == BuildingType.Barracks && b.Completed));
        Assert.All(view.Players, p => Assert.Equal(0, p.Resources));
        host.Stop();
    }
}
