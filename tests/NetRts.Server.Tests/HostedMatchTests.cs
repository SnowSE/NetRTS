using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Achievements;

namespace NetRts.Server.Tests;

/// <summary>Matches a signed-in person hosts from the website: named, joined by bots, started by the host.</summary>
public class HostedMatchTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_host_starts_their_match_once_bots_have_joined_it_by_name()
    {
        var host = factory.ClientFor(await factory.NewAccountAsync(nickname: "Ms Frizzle"));
        var name = MatchName();
        var match = await HostAsync(host, new CreateMatchRequest { Name = name, MaxPlayers = 8 });
        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal("Ms Frizzle", match.HostedBy);
        Assert.Empty(match.Players);
        Assert.Equal(match.MatchId, Assert.Single(await ListAsync(host)).MatchId);

        var (first, _) = await factory.NewPlayerAsync();
        var (second, _) = await factory.NewPlayerAsync();
        await first.JoinMatchAsync((await first.GetMatchByNameAsync(name, Ct)).MatchId, Ct);
        Assert.Equal("NOT_ENOUGH_PLAYERS", (await (await StartAsync(host, match.MatchId)).ErrorAsync()).Code);

        var someoneElse = factory.ClientFor(await factory.NewAccountAsync());
        await second.JoinMatchAsync(match.MatchId, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await StartAsync(someoneElse, match.MatchId)).StatusCode);

        var started = await (await StartAsync(host, match.MatchId)).Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct);
        Assert.Equal(MatchStatus.Active, started!.Status);
        Assert.Equal(2, started.Players.Count);
        Assert.Equal("MATCH_ALREADY_STARTED", (await (await StartAsync(host, match.MatchId)).ErrorAsync()).Code);
    }

    [Fact]
    public async Task The_hosts_own_bot_can_play_in_their_match()
    {
        var host = factory.ClientFor(await factory.NewAccountAsync());
        var mine = await (await host.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"mine-{Guid.NewGuid():N}"[..16] }, NetRtsClient.JsonOptions, Ct))
            .Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct);
        var match = await HostAsync(host, new CreateMatchRequest { Name = MatchName(), MaxPlayers = 4, HouseBots = ["sitter"] });
        var bot = new NetRtsClient(factory.CreateClient(), mine!.ApiKey);

        await bot.JoinMatchAsync(match.MatchId, Ct);
        var started = await (await StartAsync(host, match.MatchId)).Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct);

        Assert.Equal(MatchStatus.Active, started!.Status);
        Assert.Contains(started.Players, p => p.PlayerId == mine.PlayerId);
    }

    [Fact]
    public async Task Hosting_takes_a_signed_in_person_and_a_name()
    {
        var (_, bot) = await factory.NewPlayerAsync();
        var botHttp = factory.CreateClient();
        botHttp.DefaultRequestHeaders.Authorization = new("Bearer", bot.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await botHttp.PostAsJsonAsync("/api/v1/account/matches", new CreateMatchRequest { Name = MatchName() }, NetRtsClient.JsonOptions, Ct)).StatusCode);

        var host = factory.ClientFor(await factory.NewAccountAsync());
        var unnamed = await host.PostAsJsonAsync("/api/v1/account/matches", new CreateMatchRequest(), NetRtsClient.JsonOptions, Ct);
        Assert.Equal("INVALID_NAME", (await unnamed.ErrorAsync()).Code);
    }

    [Fact]
    public async Task A_bot_leaving_keeps_the_match_and_the_host_can_call_it_off()
    {
        var host = factory.ClientFor(await factory.NewAccountAsync());
        var match = await HostAsync(host, new CreateMatchRequest { Name = MatchName(), MaxPlayers = 4, HouseBots = ["sitter"] });
        var (bot, _) = await factory.NewPlayerAsync();
        await bot.JoinMatchAsync(match.MatchId, Ct);
        await bot.LeaveMatchAsync(match.MatchId, Ct);
        Assert.NotNull(factory.Matches.Get(match.MatchId));

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientFor(await factory.NewAccountAsync()).DeleteAsync($"/api/v1/account/matches/{match.MatchId}", Ct)).StatusCode);
        (await host.DeleteAsync($"/api/v1/account/matches/{match.MatchId}", Ct)).EnsureSuccessStatusCode();
        Assert.Null(factory.Matches.Get(match.MatchId));
        Assert.Empty(await ListAsync(host));
    }

    [Fact]
    public async Task Hosting_a_match_real_bots_play_earns_host_with_the_most_for_the_hosts_bots()
    {
        var account = await factory.NewAccountAsync();
        var host = factory.ClientFor(account);
        var owned = await (await host.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"mine-{Guid.NewGuid():N}"[..16] }, NetRtsClient.JsonOptions, Ct))
            .Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct);
        var match = await HostAsync(host, new CreateMatchRequest { Name = MatchName() });
        var (first, _) = await factory.NewPlayerAsync();
        var (second, _) = await factory.NewPlayerAsync();
        await first.JoinMatchAsync(match.MatchId, Ct);
        await second.JoinMatchAsync(match.MatchId, Ct);   // the second seat fills it, so it starts by itself

        factory.Matches.Get(match.MatchId)!.Advance(20);
        await second.SurrenderAsync(match.MatchId, Ct);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<EarnedAchievementDto> earned;
        while (!(earned = await EarnedAsync(owned!.PlayerId)).Any(a => a.Id == AchievementCatalog.HostWithTheMost))
        {
            Assert.True(DateTime.UtcNow < deadline, "host-with-the-most was never earned");
            await Task.Delay(50, Ct);
        }

        Assert.Equal(match.MatchId, earned.Single(a => a.Id == AchievementCatalog.HostWithTheMost).MatchId);
        await factory.Services.GetRequiredService<AchievementService>().DrainAsync(Ct);
    }

    private static string MatchName() => $"host-{Guid.NewGuid():N}"[..20];

    private async Task<MatchSummaryDto> HostAsync(HttpClient host, CreateMatchRequest request)
    {
        var response = await host.PostAsJsonAsync("/api/v1/account/matches", request, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
    }

    private static async Task<List<MatchSummaryDto>> ListAsync(HttpClient host) =>
        (await host.GetFromJsonAsync<List<MatchSummaryDto>>("/api/v1/account/matches", NetRtsClient.JsonOptions, Ct))!;

    private static Task<HttpResponseMessage> StartAsync(HttpClient host, Guid matchId) =>
        host.PostAsync($"/api/v1/account/matches/{matchId}/start", null, Ct);

    private async Task<List<EarnedAchievementDto>> EarnedAsync(Guid playerId) =>
        (await factory.CreateClient().GetFromJsonAsync<List<EarnedAchievementDto>>($"/api/v1/players/{playerId}/achievements", NetRtsClient.JsonOptions, Ct))!;
}
