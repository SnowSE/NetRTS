using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Auth;

namespace NetRts.Server.Tests;

/// <summary>Someone signed in to the website plays as one of their own bots without its key (X-Play-As).</summary>
public class PlayAsTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BlockProgramDto Program = new()
    {
        Name = "Diggers",
        Rules = [new() { When = new() { Kind = "count", What = "workers", Op = "<", Value = 8 }, Do = [new() { Kind = "train", Unit = UnitType.Worker }] }],
    };

    private async Task<(HttpClient Http, RegisterPlayerResponse Bot)> OwnerWithBotAsync()
    {
        var http = factory.ClientFor(await factory.NewAccountAsync());
        var made = await http.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"mine-{Guid.NewGuid():N}"[..20] }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        return (http, (await made.Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct))!);
    }

    private static HttpRequestMessage AsBot(HttpMethod method, string path, Guid playerId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(ApiKeys.PlayAsHeader, playerId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: NetRtsClient.JsonOptions);
        }

        return request;
    }

    [Fact]
    public async Task The_owner_plays_a_block_bot_as_their_bot_and_its_key_keeps_working()
    {
        var (http, bot) = await OwnerWithBotAsync();

        var me = await http.SendAsync(AsBot(HttpMethod.Get, "/api/v1/players/me", bot.PlayerId), Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(bot.Name, (await me.Content.ReadFromJsonAsync<PlayerProfileDto>(NetRtsClient.JsonOptions, Ct))!.Name);

        var start = await http.SendAsync(AsBot(HttpMethod.Post, "/api/v1/blocks/matches", bot.PlayerId,
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.Sandbox, TickIntervalMs = 20 }), Ct);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var match = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        Assert.Contains(match.Players, p => p.PlayerId == bot.PlayerId);

        var status = await http.SendAsync(AsBot(HttpMethod.Get, $"/api/v1/blocks/matches/{match.MatchId}/status", bot.PlayerId), Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var withKey = factory.CreateClient();
        withKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bot.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await withKey.GetAsync("/api/v1/players/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Nobody_else_can_play_as_a_bot()
    {
        var (_, bot) = await OwnerWithBotAsync();
        var someoneElse = factory.ClientFor(await factory.NewAccountAsync());
        var signedOut = factory.CreateClient();
        var (_, unowned) = await factory.NewPlayerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await someoneElse.SendAsync(AsBot(HttpMethod.Get, "/api/v1/players/me", bot.PlayerId), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedOut.SendAsync(AsBot(HttpMethod.Get, "/api/v1/players/me", bot.PlayerId), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await someoneElse.SendAsync(AsBot(HttpMethod.Get, "/api/v1/players/me", unowned.PlayerId), Ct)).StatusCode);

        var garbage = new HttpRequestMessage(HttpMethod.Get, "/api/v1/players/me");
        garbage.Headers.Add(ApiKeys.PlayAsHeader, "not-a-guid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await someoneElse.SendAsync(garbage, Ct)).StatusCode);
    }
}
