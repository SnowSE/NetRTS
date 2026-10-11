using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Tests;

/// <summary>League matches (preseason, season, championship): one match id, played in heats.</summary>
public class LeagueMatchTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_discord_server_admins_create_league_matches()
    {
        var request = new CreateLeagueMatchRequest { Category = MatchCategory.Season, Name = MatchName() };
        var (botClient, bot) = await factory.NewPlayerAsync();
        var botHttp = factory.CreateClient();
        botHttp.DefaultRequestHeaders.Authorization = new("Bearer", bot.ApiKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await botHttp.PostAsJsonAsync("/api/v1/admin/matches", request, NetRtsClient.JsonOptions, Ct)).StatusCode);
        var member = factory.ClientFor(await factory.NewAccountAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/admin/matches", request, NetRtsClient.JsonOptions, Ct)).StatusCode);

        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var match = await CreateAsync(admin, request);
        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal(MatchCategory.Season, match.Category);
        Assert.Empty(match.Heats!);
        Assert.Contains(await botClient.ListMatchesAsync(MatchStatus.Waiting, Ct), m => m.MatchId == match.MatchId);
        Assert.Equal(match.MatchId, (await botClient.GetMatchByNameAsync(request.Name, Ct)).MatchId);

        var practice = await admin.PostAsJsonAsync("/api/v1/admin/matches", request with { Category = MatchCategory.Practice, Name = MatchName() }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal("INVALID_CATEGORY", (await practice.ErrorAsync()).Code);
        var taken = await admin.PostAsJsonAsync("/api/v1/admin/matches", request, NetRtsClient.JsonOptions, Ct);
        Assert.Equal("MATCH_NAME_TAKEN", (await taken.ErrorAsync()).Code);

        (await admin.DeleteAsync($"/api/v1/admin/matches/{match.MatchId}", Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/v1/matches/{match.MatchId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task League_matches_take_verified_bots_one_per_person()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var match = await CreateAsync(admin, new CreateLeagueMatchRequest { Name = MatchName() });

        var (stranger, _) = await factory.NewPlayerAsync();
        Assert.Equal("NOT_VERIFIED", (await Assert.ThrowsAsync<NetRtsApiException>(() => stranger.JoinMatchAsync(match.MatchId, Ct))).Code);

        var owner = factory.ClientFor(await factory.NewAccountAsync(nickname: "Twins"));
        var first = Client((await RegisterOwnedAsync(owner, "twin")).ApiKey);
        var second = Client((await RegisterOwnedAsync(owner, "twin")).ApiKey);
        var joined = await first.JoinMatchAsync(match.MatchId, Ct);
        var queued = Assert.Single(joined.Players);
        Assert.Equal("Twins", queued.OwnerName);
        Assert.True(queued.Connected);
        Assert.Equal("OWNER_ALREADY_JOINED", (await Assert.ThrowsAsync<NetRtsApiException>(() => second.JoinMatchAsync(match.MatchId, Ct))).Code);

        Assert.Empty((await first.LeaveMatchAsync(match.MatchId, Ct)).Players);
        Assert.Single((await second.JoinMatchAsync(match.MatchId, Ct)).Players);
    }

    [Fact]
    public async Task Bots_play_the_league_match_id_and_land_in_their_own_heat_when_the_admin_starts()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var match = await CreateAsync(admin, new CreateLeagueMatchRequest { Category = MatchCategory.Championship, Name = MatchName() });
        var bots = new List<(NetRtsClient Client, RegisterPlayerResponse Player)>();
        for (var i = 0; i < 5; i++)
        {
            var bot = await VerifiedBotAsync($"p{i}");
            await bot.Client.JoinMatchAsync(match.MatchId, Ct);
            bots.Add(bot);
        }

        // The usual "wait for the start" long-poll, on the league match's id.
        var waiting = bots[0].Client.WaitForStartAsync(match.MatchId, Ct);
        var early = await Assert.ThrowsAsync<NetRtsApiException>(() =>
            bots[1].Client.SubmitAsync(match.MatchId, [new CommandRequest { Type = CommandType.Stop, UnitIds = [1] }], Ct));
        Assert.Equal("MATCH_NOT_STARTED", early.Code);

        var bad = await admin.PostAsJsonAsync($"/api/v1/admin/matches/{match.MatchId}/heats", new SplitHeatsRequest { Sizes = [2, 2] }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal("INVALID_SPLIT", (await bad.ErrorAsync()).Code);

        var split = await SplitAsync(admin, match.MatchId, 2, 3);
        Assert.Equal(MatchStatus.Waiting, split.Status);
        Assert.Equal([2, 3], split.Heats!.Select(h => h.PlayerIds.Count));
        Assert.All(split.Heats!, h => Assert.Equal(MatchStatus.Waiting, factory.Matches.Get(h.MatchId)!.Status));
        Assert.False(waiting.IsCompleted);

        var late = (await VerifiedBotAsync("late")).Client;
        Assert.Equal("MATCH_CLOSED", (await Assert.ThrowsAsync<NetRtsApiException>(() => late.JoinMatchAsync(match.MatchId, Ct))).Code);
        Assert.Equal("HEAT_MATCH", (await Assert.ThrowsAsync<NetRtsApiException>(() => late.JoinMatchAsync(split.Heats![0].MatchId, Ct))).Code);

        // Reshuffling replaces the heats; the bots never see it.
        var reshuffled = await SplitAsync(admin, match.MatchId, 2, 3);
        Assert.All(split.Heats!, h => Assert.Null(factory.Matches.Get(h.MatchId)));

        var started = await PostAsync<MatchSummaryDto>(admin, $"/api/v1/admin/matches/{match.MatchId}/start");
        Assert.Equal(MatchStatus.Active, started.Status);
        Assert.All(reshuffled.Heats!, h =>
        {
            var heat = factory.Matches.Get(h.MatchId)!;
            Assert.Equal(MatchStatus.Active, heat.Status);
            Assert.Equal(MatchCategory.Championship, heat.Category);
            Assert.Equal(match.MatchId, heat.ToSummary().ParentMatchId);
            Assert.False(heat.SpeedAdjustable);
        });

        // Each bot's view is of its own heat, through the league match's id.
        var state = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var myHeat = reshuffled.Heats!.Single(h => h.PlayerIds.Contains(bots[0].Player.PlayerId));
        Assert.Equal(myHeat.PlayerIds.Count, state.Players.Count);
        var unit = state.Units.First(u => u.Owner == state.You.Slot);
        var submitted = await bots[0].Client.SubmitAsync(match.MatchId, [new CommandRequest { Type = CommandType.Stop, UnitIds = [unit.Id] }], Ct);
        Assert.Equal(myHeat.MatchId, factory.Matches.All.Single(m => m.HasPlayer(bots[0].Player.PlayerId) && m.ParentMatchId == match.MatchId).Id);
        Assert.NotNull(submitted);

        // Spectators pick a heat; the standings cover every heat.
        var spectate = await factory.CreateClient().GetAsync($"/api/v1/matches/{match.MatchId}/spectate", Ct);
        Assert.Equal("PICK_A_HEAT", (await spectate.ErrorAsync()).Code);
        var standings = (await factory.CreateClient().GetFromJsonAsync<List<HeatStandingsDto>>($"/api/v1/matches/{match.MatchId}/standings", NetRtsClient.JsonOptions, Ct))!;
        Assert.Equal(reshuffled.Heats!.Select(h => h.MatchId), standings.Select(s => s.MatchId));
        Assert.Equal(5, standings.Sum(s => s.Players.Count));
    }

    [Fact]
    public async Task Admins_can_take_a_bot_out_and_must_resplit_a_heat_left_too_small()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var match = await CreateAsync(admin, new CreateLeagueMatchRequest { Name = MatchName() });
        var bots = new List<RegisterPlayerResponse>();
        for (var i = 0; i < 4; i++)
        {
            var bot = await VerifiedBotAsync($"r{i}");
            await bot.Client.JoinMatchAsync(match.MatchId, Ct);
            bots.Add(bot.Player);
        }

        var split = await SplitAsync(admin, match.MatchId, 2, 2);
        var removed = await admin.DeleteAsync($"/api/v1/admin/matches/{match.MatchId}/players/{bots[0].PlayerId}", Ct);
        removed.EnsureSuccessStatusCode();
        Assert.Equal(3, (await removed.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!.Players.Count);
        Assert.DoesNotContain(split.Heats!.SelectMany(h => factory.Matches.Get(h.MatchId)!.Seats), s => s.PlayerId == bots[0].PlayerId);

        var start = await admin.PostAsync($"/api/v1/admin/matches/{match.MatchId}/start", null, Ct);
        Assert.Equal("INVALID_SPLIT", (await start.ErrorAsync()).Code);

        await SplitAsync(admin, match.MatchId, 3);
        Assert.Equal(MatchStatus.Active, (await PostAsync<MatchSummaryDto>(admin, $"/api/v1/admin/matches/{match.MatchId}/start")).Status);
    }

    [Fact]
    public async Task Heats_award_margin_points_and_placement_ratings()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var match = await CreateAsync(admin, new CreateLeagueMatchRequest { Category = MatchCategory.Preseason, Name = MatchName() });
        var bots = new List<(NetRtsClient Client, RegisterPlayerResponse Player)>();
        foreach (var nick in new[] { "Ada", "Grace", "Linus" })
        {
            var bot = await VerifiedBotAsync(nick);
            await bot.Client.JoinMatchAsync(match.MatchId, Ct);
            bots.Add(bot);
        }

        var split = await SplitAsync(admin, match.MatchId, 3);
        await PostAsync<MatchSummaryDto>(admin, $"/api/v1/admin/matches/{match.MatchId}/start");
        var heatId = split.Heats![0].MatchId;
        factory.Matches.Get(heatId)!.Advance(5);
        await bots[2].Client.SurrenderAsync(match.MatchId, Ct);
        await bots[1].Client.SurrenderAsync(match.MatchId, Ct);

        // A player's result comes from their heat, through the league match id.
        var result = await bots[0].Client.GetResultAsync(match.MatchId, Ct);
        Assert.Equal(heatId, result.MatchId);
        Assert.Equal(bots[0].Player.PlayerId, result.Outcome.WinnerId);
        var order = result.Players.OrderByDescending(p => p.Winner).ThenByDescending(p => p.Score.Total).ToList();
        var expected = LeaguePoints.Award(order.Select(p => p.Score.Total).ToList());

        var standings = (await factory.CreateClient().GetFromJsonAsync<List<HeatStandingsDto>>($"/api/v1/matches/{match.MatchId}/standings", NetRtsClient.JsonOptions, Ct))!;
        var table = Assert.Single(standings);
        Assert.Equal(MatchStatus.Completed, table.Status);
        Assert.Equal(expected, table.Players.Select(p => p.Points));
        Assert.Equal(1, table.Players[0].Place);
        Assert.True(table.Players[0].Winner);

        var ladder = await WaitFor(() => Ladder(MatchCategory.Preseason), l => l.Count(e => bots.Any(b => b.Player.PlayerId == e.PlayerId)) == 3);
        for (var i = 0; i < order.Count; i++)
        {
            Assert.Equal(expected[i], ladder.Single(e => e.PlayerId == order[i].PlayerId).Points);
        }

        var winner = ladder.Single(e => e.PlayerId == bots[0].Player.PlayerId);
        Assert.Equal(1, winner.Wins);
        Assert.True(winner.Rating > 1200);
        Assert.Equal("Ada", winner.OwnerName);
        Assert.Equal(ladder.OrderByDescending(e => e.Points).Select(e => e.Points), ladder.Select(e => e.Points));
        Assert.Equal(winner.Points, (await bots[0].Client.GetMeAsync(Ct)).Records.Single(r => r.Category == MatchCategory.Preseason).Points);
        Assert.DoesNotContain(await Ladder(MatchCategory.Season), e => e.PlayerId == winner.PlayerId);
        Assert.DoesNotContain(await Ladder(MatchCategory.Practice), e => e.PlayerId == winner.PlayerId);

        var history = await factory.CreateClient().GetFromJsonAsync<List<MatchSummaryDto>>("/api/v1/matches/history?category=Preseason", NetRtsClient.JsonOptions, Ct);
        Assert.All(history!, m => Assert.Equal(MatchCategory.Preseason, m.Category));
        Assert.Contains(history!, m => m.MatchId == heatId && m.ParentMatchId == match.MatchId);

        // At the end of a season it gets a label; its ladder stays viewable and the next starts from zero.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var ids = bots.Select(b => b.Player.PlayerId).ToList();
            await db.PlayerStats.Where(s => ids.Contains(s.PlayerId)).ExecuteUpdateAsync(u => u.SetProperty(s => s.Season, "beta-2026"), Ct);
        }

        Assert.DoesNotContain(await Ladder(MatchCategory.Preseason), e => e.PlayerId == winner.PlayerId);
        Assert.Contains(await Ladder(MatchCategory.Preseason, "beta-2026"), e => e.PlayerId == winner.PlayerId && e.Points == winner.Points);
    }

    [Fact]
    public void League_points_are_margins_over_the_rest_of_the_top_four()
    {
        Assert.Equal([180, 60, 40, 0, 0], LeaguePoints.Award([100, 60, 50, 10, 5]));
        Assert.Equal([30, 0], LeaguePoints.Award([80, 50]));
        Assert.Equal([0, 0], LeaguePoints.Award([50, 80])); // won by elimination while outscored
        Assert.Equal([0], LeaguePoints.Award([70]));
    }

    [Fact]
    public void Placement_elo_matches_a_1v1_for_two_players()
    {
        var (a, b) = Elo.Update(1250, 1180, 1.0);
        Assert.Equal([a, b], Elo.UpdateMany([1250, 1180], [1, 2]));

        var many = Elo.UpdateMany([1200, 1200, 1200, 1200], [1, 2, 3, 4]);
        Assert.True(many[0] > many[1] && many[1] > 1200 && many[2] < 1200 && many[3] < many[2]);
        Assert.InRange(many.Sum() - 4800, -2, 2);
    }

    private async Task<(NetRtsClient Client, RegisterPlayerResponse Player)> VerifiedBotAsync(string nickname)
    {
        var bot = await RegisterOwnedAsync(factory.ClientFor(await factory.NewAccountAsync(nickname: nickname)), nickname);
        return (Client(bot.ApiKey), bot);
    }

    private async Task<RegisterPlayerResponse> RegisterOwnedAsync(HttpClient owner, string prefix)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"{prefix}-{Guid.NewGuid():N}"[..16] }, Ct);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct))!;
    }

    private NetRtsClient Client(string apiKey) => new(factory.CreateClient(), apiKey);

    private static async Task<MatchSummaryDto> CreateAsync(HttpClient admin, CreateLeagueMatchRequest request)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/matches", request, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
    }

    private static async Task<MatchSummaryDto> SplitAsync(HttpClient admin, Guid matchId, params int[] sizes)
    {
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/matches/{matchId}/heats", new SplitHeatsRequest { Sizes = sizes }, NetRtsClient.JsonOptions, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
    }

    private static async Task<T> PostAsync<T>(HttpClient http, string url)
    {
        var response = await http.PostAsync(url, null, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(NetRtsClient.JsonOptions, Ct))!;
    }

    private async Task<List<LeaderboardEntryDto>> Ladder(MatchCategory category, string? season = null) =>
        (await factory.CreateClient().GetFromJsonAsync<List<LeaderboardEntryDto>>(
            $"/api/v1/leaderboard?category={category}&limit=200" + (season is null ? "" : $"&season={season}"), NetRtsClient.JsonOptions, Ct))!;

    private static string MatchName() => $"league-{Guid.NewGuid():N}"[..16];

    private static async Task<T> WaitFor<T>(Func<Task<T>> fetch, Func<T, bool> done)
    {
        for (var i = 0; i < 50; i++)
        {
            var value = await fetch();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }

        return await fetch();
    }
}
