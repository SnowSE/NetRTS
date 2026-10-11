using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

/// <summary>A server that announces results to a fake Discord webhook.</summary>
public sealed class AnnouncingServerFactory : ServerFactory
{
    public FakeWebhook Webhook { get; } = new();

    public MatchAnnouncer Announcer => Services.GetRequiredService<MatchAnnouncer>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Discord:ResultsWebhookUrl", "https://discord.test/api/webhooks/1/x");
        builder.UseSetting("NetRts:PublicUrl", "https://netrts.test/");
        builder.ConfigureTestServices(services => services.AddSingleton<IDiscordWebhook>(Webhook));
    }
}

public sealed class FakeWebhook : IDiscordWebhook
{
    public ConcurrentQueue<JsonObject> Sent { get; } = new();

    public Task<TimeSpan?> SendAsync(JsonObject message, CancellationToken ct)
    {
        Sent.Enqueue(message);
        return Task.FromResult<TimeSpan?>(null);
    }
}

/// <summary>Finished matches on the shared server are announced in the Discord channel.</summary>
public class AnnouncementTests(AnnouncingServerFactory factory) : IClassFixture<AnnouncingServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Practice_between_real_bots_is_announced_with_a_link_and_no_pings()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("win");
        var (loser, loserInfo) = await factory.NewPlayerAsync("lose");
        var match = await winner.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await loser.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(2);
        await loser.SurrenderAsync(match.MatchId, Ct);

        var embed = await NextEmbedAsync(e => e["url"]?.GetValue<string>() == $"https://netrts.test/#/match/{match.MatchId}");
        Assert.Equal($"{winnerInfo.Name} beat {loserInfo.Name}", embed["title"]!.GetValue<string>());
        Assert.Contains("By surrender", embed["description"]!.GetValue<string>());
        Assert.Empty(LastMessageFor(embed)["allowed_mentions"]!["parse"]!.AsArray());
    }

    [Fact]
    public async Task Practice_against_house_bots_goes_into_a_digest_and_exhibitions_are_ignored()
    {
        var (client, bot) = await factory.NewPlayerAsync("solo");
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(2);
        await client.SurrenderAsync(match.MatchId, Ct);

        var exhibition = await factory.CreateClient().PostAsJsonAsync("/api/v1/exhibitions",
            new CreateExhibitionRequest { Bots = ["rusher", "sitter"], Settings = new MatchSettingsDto { MaxTicks = 10 } }, NetRtsClient.JsonOptions, Ct);
        var exhibitionId = (await exhibition.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!.MatchId;
        factory.Matches.Get(exhibitionId)!.Advance(10);

        await WaitUntilAsync(() => factory.Matches.Get(match.MatchId)!.Recorded && factory.Matches.Get(exhibitionId)!.Recorded);
        Assert.DoesNotContain(Embeds(), e => e["url"]?.GetValue<string>()?.Contains(match.MatchId.ToString()) == true);

        factory.Announcer.FlushDigest();
        var digest = await NextEmbedAsync(e => e["title"]!.GetValue<string>().StartsWith("Practice vs house bots", StringComparison.Ordinal)
                                               && e["description"]!.GetValue<string>().Contains(match.MatchId.ToString()));
        Assert.Contains("lost to sitter (house bot)", digest["description"]!.GetValue<string>());
        Assert.Contains($"**{bot.Name}**", digest["description"]!.GetValue<string>());
        Assert.DoesNotContain(Embeds(), e => e.ToJsonString().Contains(exhibitionId.ToString()));
    }

    [Fact]
    public async Task A_league_match_is_announced_once_when_its_last_heat_finishes()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var created = await admin.PostAsJsonAsync("/api/v1/admin/matches",
            new CreateLeagueMatchRequest { Category = MatchCategory.Season, Name = $"wk-{Guid.NewGuid():N}"[..12] }, NetRtsClient.JsonOptions, Ct);
        var match = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        var bots = new List<(NetRtsClient Client, Guid Id)>();
        for (var i = 0; i < 4; i++)
        {
            var owner = factory.ClientFor(await factory.NewAccountAsync(nickname: $"Owner{i}"));
            var reg = await (await owner.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"lg{i}-{Guid.NewGuid():N}"[..14] }, Ct))
                .Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct);
            var bot = new NetRtsClient(factory.CreateClient(), reg!.ApiKey);
            await bot.JoinMatchAsync(match.MatchId, Ct);
            bots.Add((bot, reg.PlayerId));
        }

        var split = await admin.PostAsJsonAsync($"/api/v1/admin/matches/{match.MatchId}/heats", new SplitHeatsRequest { Sizes = [2, 2] }, NetRtsClient.JsonOptions, Ct);
        var heats = (await split.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!.Heats!;
        (await admin.PostAsync($"/api/v1/admin/matches/{match.MatchId}/start", null, Ct)).EnsureSuccessStatusCode();
        var leagueUrl = $"https://netrts.test/#/match/{match.MatchId}";

        // First heat ends: nothing yet.
        var first = factory.Matches.Get(heats[0].MatchId)!;
        first.Advance(2);
        await bots.First(b => first.HasPlayer(b.Id)).Client.SurrenderAsync(match.MatchId, Ct);
        await WaitUntilAsync(() => first.Recorded);
        await Task.Delay(300, Ct);
        Assert.DoesNotContain(Embeds(), e => e["url"]?.GetValue<string>() == leagueUrl);

        // Second heat ends: one message covering both heats.
        var second = factory.Matches.Get(heats[1].MatchId)!;
        second.Advance(2);
        await bots.First(b => second.HasPlayer(b.Id)).Client.SurrenderAsync(match.MatchId, Ct);
        var embed = await NextEmbedAsync(e => e["url"]?.GetValue<string>() == leagueUrl);
        Assert.StartsWith("Season:", embed["title"]!.GetValue<string>());
        var fields = embed["fields"]!.AsArray();
        Assert.Equal(heats.Select(h => h.Name), fields.Select(f => f!["name"]!.GetValue<string>()));
        Assert.Contains("pts", fields[0]!["value"]!.GetValue<string>());
        Assert.Contains("(Owner", fields[0]!["value"]!.GetValue<string>());

        await Task.Delay(2500, Ct);
        Assert.Single(Embeds(), e => e["url"]?.GetValue<string>() == leagueUrl);
    }

    private IEnumerable<JsonObject> Embeds() =>
        factory.Webhook.Sent.ToArray().SelectMany(m => m["embeds"]!.AsArray().Select(e => e!.AsObject()));

    private JsonObject LastMessageFor(JsonObject embed) =>
        factory.Webhook.Sent.ToArray().Last(m => m["embeds"]!.AsArray().Any(e => ReferenceEquals(e, embed)));

    private async Task<JsonObject> NextEmbedAsync(Func<JsonObject, bool> match)
    {
        for (var i = 0; i < 100; i++)
        {
            if (Embeds().FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("The announcement was never posted. Sent: " + string.Join("\n", factory.Webhook.Sent.Select(m => m.ToJsonString())));
    }

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(50, Ct);
        }

        Assert.True(done());
    }
}
