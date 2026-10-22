using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Recaps;

namespace NetRts.Server.Tests;

public class RecapTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient JobClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RecapEndpoints.KeyHeader, ServerFactory.RecapKey);
        return client;
    }

    [Fact]
    public async Task The_brief_and_publishing_need_the_recap_key()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/recaps/brief", Ct)).StatusCode);
        anonymous.DefaultRequestHeaders.Add(RecapEndpoints.KeyHeader, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/v1/recaps", new PublishRecapRequest { Date = "2026-10-20", Title = "x", Video = "x.mp4" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_brief_covers_the_days_matches_new_bots_and_whats_coming_up()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync("brief-w");
        var (loser, _) = await factory.NewPlayerAsync("brief-l");
        var match = await winner.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await loser.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(300);
        await loser.SurrenderAsync(match.MatchId, Ct);
        await WaitForHistoryAsync(match.MatchId);

        var today = factory.Services.GetRequiredService<LadderSnapshots>().Today;
        var brief = await JobClient().GetFromJsonAsync<RecapBriefDto>($"/api/v1/recaps/brief?date={today:yyyy-MM-dd}", NetRtsClient.JsonOptions, Ct);

        Assert.Equal(today.ToString("yyyy-MM-dd"), brief!.Date);
        Assert.True(brief.Totals.RealMatches >= 1);
        Assert.Contains(brief.NewBots, b => b.Name == winnerInfo.Name && b.Wins == 1);
        Assert.NotNull(brief.MatchOfTheNight);
        Assert.Contains(brief.Strategies, s => s.Bot == winnerInfo.Name);
        Assert.Contains("GRSC 124", brief.Upcoming.Schedule);
        Assert.Contains("Commons", brief.Mentoring);
        Assert.Equal("Badger Brawl", brief.Lore.Game);
        Assert.NotEmpty(brief.RulesNotes);
    }

    [Fact]
    public async Task Published_episodes_are_listed_newest_first_and_link_to_their_video()
    {
        var job = JobClient();
        foreach (var day in new[] { "2026-09-01", "2026-09-02" })
        {
            var response = await job.PostAsJsonAsync("/api/v1/recaps", new PublishRecapRequest
            {
                Date = day,
                Title = $"Recap {day}",
                Summary = "Upsets everywhere.",
                Video = $"https://videos.test/{day}.mp4",
                DurationSeconds = 180,
                Transcript = [new RecapLineDto { Speaker = "zip", Text = "Welcome back!", Start = 0 }],
            }, NetRtsClient.JsonOptions, Ct);
            response.EnsureSuccessStatusCode();
        }

        // Publishing a day again replaces it.
        (await job.PostAsJsonAsync("/api/v1/recaps", new PublishRecapRequest
        {
            Date = "2026-09-02",
            Title = "Recap 2026-09-02, take two",
            Video = "https://videos.test/2026-09-02b.mp4",
        }, NetRtsClient.JsonOptions, Ct)).EnsureSuccessStatusCode();

        var site = factory.CreateClient();
        var list = await site.GetFromJsonAsync<List<RecapEpisodeDto>>("/api/v1/recaps", NetRtsClient.JsonOptions, Ct);
        var mine = list!.Where(e => e.Date.StartsWith("2026-09-0", StringComparison.Ordinal)).ToList();
        Assert.Equal(["2026-09-02", "2026-09-01"], mine.Select(e => e.Date));
        Assert.Equal("Recap 2026-09-02, take two", mine[0].Title);
        Assert.Null(mine[0].Transcript);

        var episode = await site.GetFromJsonAsync<RecapEpisodeDto>($"/api/v1/recaps/{mine[1].Id}", NetRtsClient.JsonOptions, Ct);
        Assert.Equal("Welcome back!", Assert.Single(episode!.Transcript!).Text);

        using var noRedirects = factory.CreateClient(new() { AllowAutoRedirect = false });
        var video = await noRedirects.GetAsync(mine[1].VideoUrl, Ct);
        Assert.Equal(HttpStatusCode.Redirect, video.StatusCode);
        Assert.Equal("https://videos.test/2026-09-01.mp4", video.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_nightly_snapshot_records_the_ladders_and_awards_the_season_top_ten()
    {
        var (_, top) = await factory.NewPlayerAsync("season-top");
        var (_, second) = await factory.NewPlayerAsync("season-2nd");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = top.PlayerId, Category = MatchCategory.Season, Wins = 3, Points = 900 });
            db.PlayerStats.Add(new PlayerStatsRecord { PlayerId = second.PlayerId, Category = MatchCategory.Season, Wins = 1, Points = 100 });
            await db.SaveChangesAsync(Ct);
        }

        var snapshots = factory.Services.GetRequiredService<LadderSnapshots>();
        var day = new DateOnly(2026, 8, 15);
        Assert.True(await snapshots.TakeAsync(day, Ct));
        Assert.False(await snapshots.TakeAsync(day, Ct));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var season = await db.LadderSnapshots.Where(s => s.Date == LadderSnapshots.Stored(day) && s.Category == MatchCategory.Season)
                .OrderBy(s => s.Rank).ToListAsync(Ct);
            Assert.Equal(top.PlayerId, season[0].PlayerId);
            var awards = await db.BotAchievements.Where(a => a.PlayerId == top.PlayerId || a.PlayerId == second.PlayerId).ToListAsync(Ct);
            Assert.Contains(awards, a => a.PlayerId == top.PlayerId && a.AchievementId == "number-one");
            Assert.Contains(awards, a => a.PlayerId == second.PlayerId && a.AchievementId == "top-ten");
            Assert.DoesNotContain(awards, a => a.PlayerId == second.PlayerId && a.AchievementId == "number-one");
        }
    }

    private async Task WaitForHistoryAsync(Guid matchId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<NetRtsDb>().MatchPlayers.AnyAsync(r => r.MatchId == matchId, Ct))
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("The match was never recorded.");
    }
}
