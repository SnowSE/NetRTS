using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Tests;

public sealed class UnrankedServerFactory : ServerFactory
{
    public const string UnrankedDiscordId = "4242";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Discord:UnrankedAccounts:0", UnrankedDiscordId);
    }
}

public class UnrankedTests(UnrankedServerFactory factory) : IClassFixture<UnrankedServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bots_of_unranked_accounts_stay_off_the_ladders_and_lose_their_achievements()
    {
        var instructor = await factory.NewAccountAsync(nickname: "Instructor", discordId: UnrankedServerFactory.UnrankedDiscordId);
        var student = await factory.NewAccountAsync(nickname: "Student");
        var (_, theirs) = await factory.NewPlayerAsync("jbot");
        var (_, mine) = await factory.NewPlayerAsync("pupil");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            foreach (var (bot, owner) in new[] { (theirs, instructor), (mine, student) })
            {
                (await db.Players.FirstAsync(p => p.Id == bot.PlayerId, Ct)).OwnerAccountId = owner.Id;
                db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = bot.PlayerId, Category = MatchCategory.Practice, Wins = 3 });
                db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = bot.PlayerId, Category = MatchCategory.Preseason, Wins = 1, Points = 500 });
                db.BotAchievements.Add(new BotAchievementRecord { PlayerId = bot.PlayerId, AchievementId = "boot-camp", EarnedAt = DateTime.UtcNow });
            }

            await db.SaveChangesAsync(Ct);
        }

        await factory.Services.GetRequiredService<AchievementService>().RemoveUnrankedAsync(Ct);

        var http = factory.CreateClient();
        foreach (var category in new[] { "Practice", "Preseason" })
        {
            var ladder = await http.GetFromJsonAsync<List<LeaderboardEntryDto>>($"/api/v1/leaderboard?category={category}&limit=200", NetRtsClient.JsonOptions, Ct);
            Assert.Contains(ladder!, e => e.PlayerId == mine.PlayerId);
            Assert.DoesNotContain(ladder!, e => e.PlayerId == theirs.PlayerId);
        }

        var people = await http.GetFromJsonAsync<List<TrophyRoomEntryDto>>("/api/v1/people", NetRtsClient.JsonOptions, Ct);
        Assert.Contains(people!, p => p.AccountId == student.Id);
        Assert.DoesNotContain(people!, p => p.AccountId == instructor.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            Assert.False(await db.BotAchievements.AnyAsync(a => a.PlayerId == theirs.PlayerId, Ct));
            Assert.True(await db.BotAchievements.AnyAsync(a => a.PlayerId == mine.PlayerId, Ct));
        }
    }
}
