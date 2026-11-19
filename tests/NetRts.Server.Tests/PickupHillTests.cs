using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NetRts.Protocol;
using NetRts.Server.Matches;
using NetRts.Server.Pickup;

namespace NetRts.Server.Tests;

/// <summary>The pick-up hill's line, champion and house, driven with explicit clock times (the server's AutoTick is off).</summary>
public class PickupHillTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static readonly DateTime T0 = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly PickupPlanDto Sitter = new() { Forever = ["Worker"], Attack = new() { Army = 60 } };

    private static readonly PickupDeckDto SitterDeck = new("sitter", "Sitter", "Sleepy Sitter", Sitter);

    private PickupHill NewHill(params PickupDeckDto[] house) =>
        new(factory.Matches, NullLogger<PickupHill>.Instance, house.Length == 0 ? null : house);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Lets the match that's on now play to its end, then lets the hill see it.</summary>
    private void Finish(PickupHill hill, DateTime now)
    {
        var host = factory.Matches.Get(hill.Hill(now).MatchId!.Value)!;
        host.Advance(MatchManager.PickupMaxTicks);
        hill.Update(now);
    }

    [Fact]
    public void Joining_gives_a_name_and_a_place_in_line()
    {
        var hill = NewHill();
        var a = hill.Join(null, At(0));
        var again = hill.Join(a.Token, At(1));
        var b = hill.Join(null, At(2));

        Assert.Equal(a.Name, again.Name);
        Assert.Matches("^[A-Z][a-z]+-[A-Z][a-z]+$", a.Name);
        Assert.NotEqual(a.Name, b.Name);
        Assert.Equal(1, hill.Me(a.Token, At(3)).Place);
        Assert.Equal(2, hill.Me(b.Token, At(3)).Place);
        Assert.Equal("rush", PickupPlans.Decks[0].Id);
        Assert.Equal(PickupPlans.Decks[0].Plan, a.Plan);
    }

    [Fact]
    public void The_first_in_line_gets_planning_time()
    {
        var hill = NewHill();
        var a = hill.Join(null, At(0));

        hill.Update(At(5));
        var me = hill.Me(a.Token, At(5));

        Assert.Equal("next", me.Phase);
        Assert.Equal(55, me.SecondsLeft);
    }

    [Fact]
    public void Start_now_counts_down_then_plays_the_house()
    {
        var hill = NewHill(SitterDeck);
        var a = hill.Join(null, At(0));

        hill.StartNow(a.Token, At(1));
        hill.Update(At(1));
        var counting = hill.Me(a.Token, At(2));
        Assert.Equal("countdown", counting.Phase);
        Assert.Equal(9, counting.SecondsLeft);

        hill.Update(At(11));
        var playing = hill.Me(a.Token, At(11));
        Assert.Equal("playing", playing.Phase);
        var host = factory.Matches.Get(playing.MatchId!.Value)!;
        Assert.True(host.IsPickup);
        Assert.Equal(2, host.Seats.Count);
        Assert.Equal(2, playing.Players.Count);
        Assert.Contains(playing.Players, p => p.You && p.Name == a.Name);
        Assert.Contains(playing.Players, p => p.House && p.Champion);
        Assert.Equal([0, 1], playing.Players.Select(p => p.Slot ?? -1).Order());
    }

    [Fact]
    public void A_loss_to_the_house_shows_a_result_and_the_house_keeps_the_hill()
    {
        var hill = NewHill(PickupPlans.Decks[0]);
        var a = hill.Join(null, At(0));
        Assert.Null(hill.SetPlan(a.Token, Sitter));
        hill.StartNow(a.Token, At(1));
        hill.Update(At(1));
        hill.Update(At(11));

        Finish(hill, At(200));

        var me = hill.Me(a.Token, At(200));
        Assert.Equal("over", me.Phase);
        Assert.False(me.Result!.Won);
        Assert.Equal(2, me.Result.Place);
        Assert.True(hill.Hill(At(200)).Champion!.House);

        var back = hill.Rejoin(a.Token, At(201));
        Assert.Equal("next", back.Phase);
        Assert.Equal(20, back.SecondsLeft);
    }

    [Fact]
    public void A_champion_retires_after_five_wins_against_students()
    {
        var hill = NewHill(SitterDeck);
        var champ = hill.Join(null, At(0));
        hill.StartNow(champ.Token, At(0));
        hill.Update(At(0));
        hill.Update(At(10));
        Finish(hill, At(100));
        var crowned = hill.Me(champ.Token, At(100));
        Assert.True(crowned.Result!.Won);
        Assert.True(crowned.Champion);
        Assert.Equal(0, crowned.Wins);

        var now = 100.0;
        for (var i = 1; i <= 5; i++)
        {
            var challenger = hill.Join(null, At(now));
            hill.SetPlan(challenger.Token, Sitter);
            hill.StartNow(challenger.Token, At(now));
            hill.Update(At(now));
            hill.Update(At(now + 10));
            Assert.Equal("playing", hill.Me(challenger.Token, At(now + 10)).Phase);
            Finish(hill, At(now + 20));
            Assert.Equal("over", hill.Me(challenger.Token, At(now + 20)).Phase);
            now += 30;
        }

        var retired = hill.Me(champ.Token, At(now));
        Assert.False(retired.Champion);
        Assert.Equal("over", retired.Phase);
        Assert.True(hill.Hill(At(now)).Champion!.House);
    }

    [Fact]
    public void Phones_not_seen_for_a_minute_leave_the_line()
    {
        var hill = NewHill();
        var a = hill.Join(null, At(0));
        var b = hill.Join(null, At(0));

        hill.Me(a.Token, At(61));
        hill.Update(At(61));

        Assert.Equal(a.Name, hill.Me(a.Token, At(61)).Name);
        var gone = Assert.Throws<MatchException>(() => hill.Me(b.Token, At(61)));
        Assert.Equal("UNKNOWN_TOKEN", gone.Code);
    }

    [Fact]
    public void A_house_match_ends_when_a_student_is_ready()
    {
        var hill = NewHill(SitterDeck, PickupPlans.Decks[1]);
        hill.Hill(At(0));
        hill.Update(At(0));
        var houseMatch = hill.Hill(At(0)).MatchId;
        Assert.NotNull(houseMatch);

        var a = hill.Join(null, At(5));
        hill.StartNow(a.Token, At(5));
        hill.Update(At(5));
        factory.Matches.Get(houseMatch!.Value)!.Advance(1);
        hill.Update(At(6));

        Assert.Equal(MatchStatus.Completed, factory.Matches.Get(houseMatch.Value)!.Status);
        Assert.Equal("countdown", hill.Me(a.Token, At(6)).Phase);
    }

    [Fact]
    public void Orders_need_a_player_in_a_match()
    {
        var hill = NewHill();
        var a = hill.Join(null, At(0));

        var refused = Assert.Throws<MatchException>(() => hill.Order(a.Token, "attack"));
        Assert.Equal(409, refused.Status);
        Assert.NotNull(hill.SetPlan(a.Token, new PickupPlanDto { Forever = [] }));
    }

    [Fact]
    public async Task The_api_joins_and_serves_the_decks_and_the_hill()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var join = await client.PostAsJsonAsync("/api/v1/pickup/join", new PickupTokenRequest(null), ct);
        var me = await join.Content.ReadFromJsonAsync<PickupMeDto>(ct);
        var decks = await client.GetFromJsonAsync<List<PickupDeckDto>>("/api/v1/pickup/decks", ct);
        var hill = await client.GetFromJsonAsync<PickupHillDto>("/api/v1/pickup/hill", ct);
        var unknown = await client.GetAsync("/api/v1/pickup/me?token=nope", ct);
        var badPlan = await client.PutAsJsonAsync("/api/v1/pickup/plan", new PickupPlanRequest(me!.Token, new PickupPlanDto { Forever = [] }), ct);

        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.False(string.IsNullOrEmpty(me.Token));
        Assert.Equal(3, decks!.Count);
        Assert.NotNull(hill);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badPlan.StatusCode);
    }
}
