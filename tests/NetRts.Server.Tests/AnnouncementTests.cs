using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Matches;
using NetRts.Server.Recaps;

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
        builder.UseSetting("Discord:MessageGapMs", "0");
        builder.UseSetting("Discord:ResultsThreadId", "42");
        builder.ConfigureTestServices(services => services.AddSingleton<IDiscordWebhook>(Webhook));
    }
}

public sealed class FakeWebhook : IDiscordWebhook
{
    public ConcurrentQueue<JsonObject> Sent { get; } = new();

    /// <summary>The thread each sent message went to (null: the channel itself).</summary>
    public ConcurrentDictionary<JsonObject, string?> ThreadOf { get; } = new(ReferenceEqualityComparer.Instance);

    public Task<TimeSpan?> SendAsync(JsonObject message, string? threadId, CancellationToken ct)
    {
        ThreadOf[message] = threadId;
        Sent.Enqueue(message);
        return Task.FromResult<TimeSpan?>(null);
    }
}

/// <summary>Finished matches on the shared server are announced in the Discord channel.</summary>
public class AnnouncementTests(AnnouncingServerFactory factory) : IClassFixture<AnnouncingServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Practice_between_real_bots_waits_for_the_roundup_with_a_link_and_no_pings()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("win");
        var (loser, loserInfo) = await factory.NewPlayerAsync("lose");
        var match = await winner.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await loser.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(2);
        await loser.SurrenderAsync(match.MatchId, Ct);

        await WaitUntilAsync(() => factory.Matches.Get(match.MatchId)!.Recorded);
        await Task.Delay(300, Ct);
        Assert.DoesNotContain(Embeds(), e => e.ToJsonString().Contains(match.MatchId.ToString()));

        factory.Announcer.FlushDigest();
        var embed = await RoundupWithAsync(match.MatchId);
        Assert.StartsWith("Practice roundup", embed["title"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains($"🏆 **{winnerInfo.Name}** beat **{loserInfo.Name}** · [watch](https://netrts.test/#/match/{match.MatchId})", embed["description"]!.GetValue<string>());
        Assert.Empty(LastMessageFor(embed)["allowed_mentions"]!["parse"]!.AsArray());
        Assert.Equal("42", factory.Webhook.ThreadOf[LastMessageFor(embed)]);
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
        var digest = await RoundupWithAsync(match.MatchId);
        Assert.Contains($"**{bot.Name}** won 0 of 1 against sitter · [latest]", digest["description"]!.GetValue<string>());
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
        bool IsResults(JsonObject e) => e["url"]?.GetValue<string>() == leagueUrl && e["title"]!.GetValue<string>().StartsWith("Season:", StringComparison.Ordinal);
        Assert.DoesNotContain(Embeds(), IsResults);

        // Second heat ends: one message covering both heats.
        var second = factory.Matches.Get(heats[1].MatchId)!;
        second.Advance(2);
        await bots.First(b => second.HasPlayer(b.Id)).Client.SurrenderAsync(match.MatchId, Ct);
        var embed = await NextEmbedAsync(IsResults);
        var fields = embed["fields"]!.AsArray();
        Assert.Equal(heats.Select(h => h.Name), fields.Select(f => f!["name"]!.GetValue<string>()));
        Assert.Contains("pts", fields[0]!["value"]!.GetValue<string>());
        Assert.Contains("(Owner", fields[0]!["value"]!.GetValue<string>());

        Assert.Equal("42", factory.Webhook.ThreadOf[LastMessageFor(embed)]);

        await Task.Delay(2500, Ct);
        Assert.Single(Embeds(), IsResults);
        var open = Assert.Single(Embeds(), e => e["url"]?.GetValue<string>() == leagueUrl && e["title"]!.GetValue<string>().StartsWith("Season match open", StringComparison.Ordinal));
        Assert.Null(factory.Webhook.ThreadOf[LastMessageFor(open)]);
    }

    [Fact]
    public async Task Preseason_results_wait_for_the_roundup()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var name = $"pre-{Guid.NewGuid():N}"[..12];
        var created = await admin.PostAsJsonAsync("/api/v1/admin/matches",
            new CreateLeagueMatchRequest { Category = MatchCategory.Preseason, Name = name }, NetRtsClient.JsonOptions, Ct);
        var match = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        var bots = new List<(NetRtsClient Client, Guid Id)>();
        for (var i = 0; i < 2; i++)
        {
            var owner = factory.ClientFor(await factory.NewAccountAsync(nickname: $"Pre{i}"));
            var reg = await (await owner.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"ps{i}-{Guid.NewGuid():N}"[..14] }, Ct))
                .Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct);
            var bot = new NetRtsClient(factory.CreateClient(), reg!.ApiKey);
            await bot.JoinMatchAsync(match.MatchId, Ct);
            bots.Add((bot, reg.PlayerId));
        }

        var split = await admin.PostAsJsonAsync($"/api/v1/admin/matches/{match.MatchId}/heats", new SplitHeatsRequest { Sizes = [2] }, NetRtsClient.JsonOptions, Ct);
        var heat = factory.Matches.Get((await split.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!.Heats![0].MatchId)!;
        (await admin.PostAsync($"/api/v1/admin/matches/{match.MatchId}/start", null, Ct)).EnsureSuccessStatusCode();
        heat.Advance(2);
        await bots[0].Client.SurrenderAsync(match.MatchId, Ct);
        await WaitUntilAsync(() => heat.Recorded);
        await Task.Delay(300, Ct);
        Assert.DoesNotContain(Embeds(), e => e["title"]!.GetValue<string>().StartsWith($"Preseason: {name}", StringComparison.Ordinal));

        factory.Announcer.FlushDigest();
        var roundup = await NextEmbedAsync(e => e["title"]!.GetValue<string>().Contains("roundup", StringComparison.Ordinal)
                                                && e["description"]!.GetValue<string>().Contains($"**{name}**", StringComparison.Ordinal));
        Assert.Contains("**Preseason**", roundup["description"]!.GetValue<string>());
        Assert.Contains($"[results](https://netrts.test/#/match/{match.MatchId})", roundup["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Owner_names_are_escaped_so_discord_shows_them_as_typed()
    {
        var (winner, winnerName) = await VerifiedBotAsync("*Star*_Lord");
        var (loser, _) = await VerifiedBotAsync("[Groot](x)");
        var match = await winner.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await loser.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(2);
        await loser.SurrenderAsync(match.MatchId, Ct);

        await WaitUntilAsync(() => factory.Matches.Get(match.MatchId)!.Recorded);
        factory.Announcer.FlushDigest();
        var embed = await RoundupWithAsync(match.MatchId);
        var description = embed["description"]!.GetValue<string>();
        Assert.Contains($@"**{winnerName}** ✅ (\*Star\*\_Lord)", description);
        Assert.Contains(@"(\[Groot\]\(x\))", description);
    }

    [Fact]
    public async Task The_first_four_people_to_take_the_serum_are_announced_once_each()
    {
        var (unverified, _) = await factory.NewPlayerAsync("anon");
        await TakeSerumAsync(unverified);

        var first = await VerifiedBotAsync("Steve_Rogers");
        await TakeSerumAsync(first.Client);
        await TakeSerumAsync(first.Client);
        for (var i = 2; i <= 5; i++)
        {
            await TakeSerumAsync((await VerifiedBotAsync($"Recruit{i}")).Client);
        }

        // Wait for four, then long enough for a fifth to show up.
        for (var i = 0; i < 150 && SerumNews().Count() < 4; i++)
        {
            await Task.Delay(100, Ct);
        }

        await Task.Delay(1000, Ct);
        Assert.Equal(
            [@"**Steve\_Rogers** unlocked super soldiers!", "**Recruit2** has super soldiers now too!",
             "**Recruit3** has super soldiers now too!", "**Recruit4** has super soldiers now too!"],
            SerumNews());
        Assert.All(factory.Webhook.Sent.ToArray().Where(m => m["content"] is not null), m => Assert.Empty(m["allowed_mentions"]!["parse"]!.AsArray()));
    }

    [Fact]
    public async Task A_new_league_match_is_announced_with_its_name_and_a_link_to_join()
    {
        var admin = factory.ClientFor(await factory.NewAccountAsync(admin: true));
        var name = $"pre-{Guid.NewGuid():N}"[..12];
        var created = await admin.PostAsJsonAsync("/api/v1/admin/matches",
            new CreateLeagueMatchRequest { Category = MatchCategory.Preseason, Name = name }, NetRtsClient.JsonOptions, Ct);
        var match = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;

        var embed = await NextEmbedAsync(e => e["title"]?.GetValue<string>() == $"Preseason match open: {name}");
        Assert.Equal($"https://netrts.test/#/match/{match.MatchId}", embed["url"]!.GetValue<string>());
        Assert.Contains($"--join {name}", embed["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_first_bot_to_earn_an_achievement_is_announced_at_once_and_everyones_appear_in_the_daily_digest()
    {
        var (bot, info) = await factory.NewPlayerAsync("firsts");
        var match = await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(2);
        await bot.SurrenderAsync(match.MatchId, Ct);

        // Somebody in this class's matches is the first to finish a match; one message names them.
        for (var i = 0; i < 100 && !Contents().Any(c => c.Contains("first ever to earn", StringComparison.Ordinal) && c.Contains("Hello, World", StringComparison.Ordinal)); i++)
        {
            await Task.Delay(100, Ct);
        }

        Assert.Single(Contents(), c => c.Contains("first ever to earn", StringComparison.Ordinal) && c.Contains("Hello, World", StringComparison.Ordinal));

        // The day's digest goes out with the nightly ladder snapshot.
        var snapshots = factory.Services.GetRequiredService<NetRts.Server.Recaps.LadderSnapshots>();
        Assert.True(await snapshots.TakeAsync(snapshots.Today, Ct));
        var digest = await NextEmbedAsync(e => e["title"]!.GetValue<string>().StartsWith("Achievements unlocked", StringComparison.Ordinal));
        Assert.Contains($"**{info.Name}**: Hello, World", digest["description"]!.GetValue<string>());
        Assert.Equal("https://netrts.test/#/achievements", digest["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Last_nights_recap_is_posted_once_at_the_announce_time()
    {
        var episode = new RecapEpisodeRecord
        {
            Id = Guid.NewGuid(), Date = LadderSnapshots.Stored(new DateOnly(2030, 1, 14)), Title = "Badgers Gone Wild",
            Summary = "Upsets everywhere.", Video = "2030-01-14/recap.mp4", Thumbnail = "2030-01-14/poster.jpg",
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            db.RecapEpisodes.Add(episode);
            await db.SaveChangesAsync(Ct);
        }

        // 6:30 a.m. in Denver on the 15th is 13:30 UTC.
        var recaps = factory.Services.GetRequiredService<RecapAnnouncer>();
        Assert.False(await recaps.AnnounceAsync(new DateTimeOffset(2030, 1, 15, 13, 29, 0, TimeSpan.Zero), Ct));
        Assert.True(await recaps.AnnounceAsync(new DateTimeOffset(2030, 1, 15, 13, 30, 0, TimeSpan.Zero), Ct));
        Assert.False(await recaps.AnnounceAsync(new DateTimeOffset(2030, 1, 15, 13, 31, 0, TimeSpan.Zero), Ct));
        // The next morning it's the 15th's episode that's due, not this one.
        Assert.False(await recaps.AnnounceAsync(new DateTimeOffset(2030, 1, 16, 13, 30, 0, TimeSpan.Zero), Ct));

        var embed = await NextEmbedAsync(e => e["url"]?.GetValue<string>() == $"https://netrts.test/#/recaps/{episode.Id}");
        Assert.Equal("📺 Badgers Gone Wild", embed["title"]!.GetValue<string>());
        Assert.Contains("Monday, January 14", embed["description"]!.GetValue<string>());
        Assert.Equal($"https://netrts.test/recaps/{episode.Id}/thumbnail", embed["image"]!["url"]!.GetValue<string>());
        Assert.Single(Embeds(), e => e["url"]?.GetValue<string>() == embed["url"]!.GetValue<string>());
    }

    /// <summary>A bot claimed by a new Discord account with this server nickname.</summary>
    private async Task<(NetRtsClient Client, string Name)> VerifiedBotAsync(string nickname)
    {
        var owner = factory.ClientFor(await factory.NewAccountAsync(nickname: nickname));
        var reg = await (await owner.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"v-{Guid.NewGuid():N}"[..14] }, Ct))
            .Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct);
        return (new NetRtsClient(factory.CreateClient(), reg!.ApiKey), reg.Name);
    }

    /// <summary>Starts a practice match against a house bot and takes the serum in it.</summary>
    private async Task TakeSerumAsync(NetRtsClient bot)
    {
        var match = await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bot.ApiKey);
        var url = $"/api/v1/matches/{match.MatchId}/superpower/superserum";
        (await http.GetAsync(url, Ct)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync(url, new { ifGrenadeThrown = "jump on it" }, Ct)).EnsureSuccessStatusCode();
    }

    private IEnumerable<string> SerumNews() => Contents().Where(c => c.Contains("super soldiers", StringComparison.Ordinal));

    /// <summary>The plain-text messages sent so far.</summary>
    private IEnumerable<string> Contents() =>
        factory.Webhook.Sent.ToArray().Select(m => m["content"]?.GetValue<string>()).OfType<string>();

    private IEnumerable<JsonObject> Embeds() =>
        factory.Webhook.Sent.ToArray().SelectMany(m => m["embeds"]?.AsArray().Select(e => e!.AsObject()) ?? []);

    private JsonObject LastMessageFor(JsonObject embed) =>
        factory.Webhook.Sent.ToArray().Last(m => m["embeds"]?.AsArray().Any(e => ReferenceEquals(e, embed)) == true);

    /// <summary>The roundup that lists this match.</summary>
    private Task<JsonObject> RoundupWithAsync(Guid matchId) =>
        NextEmbedAsync(e => e["title"]!.GetValue<string>().StartsWith("Practice roundup", StringComparison.Ordinal)
                            && e["description"]!.GetValue<string>().Contains(matchId.ToString()));

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
