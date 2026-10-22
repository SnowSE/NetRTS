using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Tests;

public class AchievementTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalog_lists_every_achievement_and_keeps_hidden_ones_secret()
    {
        Assert.Equal(75, AchievementCatalog.All.Count);
        Assert.Equal(AchievementCatalog.All.Count, AchievementCatalog.All.Select(a => a.Id).Distinct().Count());
        Assert.All(AchievementCatalog.All, a => Assert.True(a.Id.Length <= 40));

        var catalog = await GetAsync<List<AchievementDto>>("/api/v1/achievements");
        Assert.Equal(75, catalog.Count);
        var serum = catalog.Single(a => a.Id == "super-soldier");
        Assert.True(serum.Hidden);
        Assert.Equal("???", serum.Name);
        Assert.Equal("???", serum.Description);
        Assert.Equal("Hello, World", catalog.Single(a => a.Id == "hello-world").Name);
    }

    [Fact]
    public async Task Beating_a_real_bot_earns_achievements_shown_on_the_bot_and_in_the_catalog()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("ach-w");
        var (loser, loserInfo) = await factory.NewPlayerAsync("ach-l");
        await PlayAsync(winner, loser, ticks: 300);

        var won = await EarnedAsync(winnerInfo.PlayerId, "first-win");
        Assert.Contains(won, a => a.Id == "hello-world");
        Assert.Contains(won, a => a.Id == "worthy-opponent");
        Assert.NotNull(won.Single(a => a.Id == "first-win").MatchId);

        var lost = await EarnedAsync(loserInfo.PlayerId, "know-when-to-fold-em");
        Assert.DoesNotContain(lost, a => a.Id == "first-win");

        var catalog = await GetAsync<List<AchievementDto>>("/api/v1/achievements");
        Assert.True(catalog.Single(a => a.Id == "first-win").EarnedBy >= 1);
        Assert.NotNull(catalog.Single(a => a.Id == "first-win").FirstEarnedBy);
    }

    [Fact]
    public async Task A_win_handed_over_by_an_early_surrender_does_not_count()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("early-w");
        var (loser, _) = await factory.NewPlayerAsync("early-l");
        await PlayAsync(winner, loser, ticks: 5);

        var earned = await EarnedAsync(winnerInfo.PlayerId, "worthy-opponent");
        Assert.DoesNotContain(earned, a => a.Id == "first-win");
        Assert.DoesNotContain(earned, a => a.Id == "flawless-victory");
    }

    [Fact]
    public async Task Matches_against_house_bots_only_count_toward_first_steps()
    {
        var (client, info) = await factory.NewPlayerAsync("solo");
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(300);
        await client.SurrenderAsync(match.MatchId, Ct);

        var earned = await EarnedAsync(info.PlayerId, "know-when-to-fold-em");
        Assert.Contains(earned, a => a.Id == "hello-world");
        Assert.DoesNotContain(earned, a => a.Id == "worthy-opponent");
        Assert.DoesNotContain(earned, a => a.Id == "regular");
    }

    [Fact]
    public async Task Bots_with_the_same_owner_are_not_real_opponents_and_owners_see_all_their_bots_achievements()
    {
        var account = await factory.NewAccountAsync();
        var site = factory.ClientFor(account);
        var (a, aInfo) = await factory.NewPlayerAsync("own-a");
        var (b, bInfo) = await factory.NewPlayerAsync("own-b");
        foreach (var key in new[] { aInfo.ApiKey, bInfo.ApiKey })
        {
            (await site.PostAsJsonAsync("/api/v1/account/bots/claim", new ClaimBotRequest { ApiKey = key }, Ct)).EnsureSuccessStatusCode();
        }

        await PlayAsync(a, b, ticks: 300);

        var earned = await EarnedAsync(aInfo.PlayerId, "blue-check");
        Assert.DoesNotContain(earned, x => x.Id == "worthy-opponent");
        Assert.DoesNotContain(earned, x => x.Id == "first-win");

        var info = await site.GetFromJsonAsync<AccountDto>("/api/v1/account", NetRtsClient.JsonOptions, Ct);
        var hello = info!.Achievements.Single(x => x.Id == "hello-world");
        Assert.Equal([aInfo.Name, bInfo.Name], hello.Bots.Order());
    }

    [Fact]
    public async Task Matches_recorded_before_the_history_existed_are_backfilled()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("old-w");
        var (loser, loserInfo) = await factory.NewPlayerAsync("old-l");
        var matchId = await PlayAsync(winner, loser, ticks: 300);
        await EarnedAsync(winnerInfo.PlayerId, "first-win");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            Guid[] ids = [winnerInfo.PlayerId, loserInfo.PlayerId];
            await db.BotAchievements.Where(x => ids.Contains(x.PlayerId)).ExecuteDeleteAsync(Ct);
            await db.MatchPlayers.Where(x => x.MatchId == matchId).ExecuteDeleteAsync(Ct);
        }

        await factory.Services.GetRequiredService<AchievementService>().BackfillAsync(Ct);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var rows = await db.MatchPlayers.Where(x => x.MatchId == matchId).ToListAsync(Ct);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(1, r.RealOpponents));
            var match = await db.Matches.SingleAsync(m => m.Id == matchId, Ct);
            var firstWin = await db.BotAchievements.SingleAsync(x => x.PlayerId == winnerInfo.PlayerId && x.AchievementId == "first-win", Ct);
            Assert.Equal(match.CompletedAt, firstWin.EarnedAt);
        }
    }

    [Fact]
    public void Streaks_come_from_the_bots_history_against_real_bots()
    {
        var bot = Guid.NewGuid();
        var history = new List<MatchPlayerRecord>();
        var opponents = new List<MatchPlayerRecord>();
        var start = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);
        MatchResultKind[] results = [MatchResultKind.Loss, MatchResultKind.Win, MatchResultKind.Win, MatchResultKind.Win];
        for (var i = 0; i < results.Length; i++)
        {
            var match = Guid.NewGuid();
            history.Add(new MatchPlayerRecord { MatchId = match, PlayerId = bot, Result = results[i], RealOpponents = 1, CompletedAt = start.AddHours(i), Ticks = 900 });
            opponents.Add(new MatchPlayerRecord { MatchId = match, PlayerId = Guid.NewGuid(), Slot = 1, Result = MatchResultKind.Loss });
        }

        var result = new PlayerResultDto { Slot = 0, Winner = true, Score = new ScoreDto() };
        var context = new AchievementContext(history[^1], result, [result], history, opponents.ToLookup(o => o.MatchId), 1800, [], verified: false,
            Elo.InitialRating, 0, TimeZoneInfo.Utc);

        Assert.Equal(3, context.WinStreak);
        Assert.Equal(4, context.DistinctRealOpponents);
        var earned = AchievementService.Earned(context).Select(a => a.Id).ToList();
        Assert.Contains("hat-trick", earned);
        Assert.DoesNotContain("unstoppable", earned);
    }

    /// <summary>A plays B for some ticks, then B surrenders; returns once the match's achievements are in.</summary>
    private async Task<Guid> PlayAsync(NetRtsClient a, NetRtsClient b, int ticks)
    {
        var match = await a.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await b.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(ticks);
        await b.SurrenderAsync(match.MatchId, Ct);
        return match.MatchId;
    }

    /// <summary>Waits until the bot has the given achievement, then returns everything it has.</summary>
    private async Task<List<EarnedAchievementDto>> EarnedAsync(Guid playerId, string waitFor)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var earned = await GetAsync<List<EarnedAchievementDto>>($"/api/v1/players/{playerId}/achievements");
            if (earned.Any(a => a.Id == waitFor))
            {
                await factory.Services.GetRequiredService<AchievementService>().DrainAsync(Ct);
                return await GetAsync<List<EarnedAchievementDto>>($"/api/v1/players/{playerId}/achievements");
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{waitFor} was never earned; got {string.Join(", ", earned.Select(a => a.Id))}");
            }

            await Task.Delay(50, Ct);
        }
    }

    private async Task<T> GetAsync<T>(string path) =>
        (await factory.CreateClient().GetFromJsonAsync<T>(path, NetRtsClient.JsonOptions, Ct))!;
}
