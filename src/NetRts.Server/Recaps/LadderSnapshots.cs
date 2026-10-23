using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;

namespace NetRts.Server.Recaps;

/// <summary>
/// Once a night (at <see cref="RecapOptions.SnapshotHour"/>, local time) records every ladder as it
/// stands for the day just ended, so the recap can show who moved, and awards the season ladder's
/// Top Ten and Number One achievements. A missed night (the server was down) is taken at startup.
/// </summary>
public sealed class LadderSnapshots(
    IServiceScopeFactory scopes,
    IOptions<NetRtsOptions> options,
    IOptions<RecapOptions> recap,
    IOptions<DiscordOptions> discord,
    MatchAnnouncer announcer,
    TimeProvider time,
    ILogger<LadderSnapshots> logger) : BackgroundService
{
    /// <summary>A local date as stored: midnight UTC on that date.</summary>
    public static DateTime Stored(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>A local day as a span of UTC times.</summary>
    public static (DateTime Start, DateTime End) Window(DateOnly day, TimeZoneInfo zone) =>
        (TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone),
         TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));

    /// <summary>The local date right now.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Value.Zone).DateTime);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The day that has most recently ended at the snapshot hour.
                var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Value.Zone);
                var day = DateOnly.FromDateTime(now.DateTime).AddDays(now.Hour >= recap.Value.SnapshotHour ? -1 : -2);
                await TakeAsync(day, stoppingToken);

                var next = now.Date.AddDays(now.Hour >= recap.Value.SnapshotHour ? 1 : 0).AddHours(recap.Value.SnapshotHour).AddMinutes(5);
                await Task.Delay(next - now.DateTime, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ladder snapshot failed");
                await Task.Delay(TimeSpan.FromMinutes(15), time, stoppingToken);
            }
        }
    }

    /// <summary>Records the ladders for <paramref name="day"/> unless they already are. Returns whether it took them.</summary>
    public async Task<bool> TakeAsync(DateOnly day, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        var date = Stored(day);
        // Ladder achievements belong to the day they were standing at the end of.
        var earnedAt = Window(day, options.Value.Zone).End.AddSeconds(-1);
        if (await db.LadderSnapshots.AnyAsync(s => s.Date == date, ct))
        {
            return false;
        }

        foreach (var category in Enum.GetValues<MatchCategory>())
        {
            var ladder = await PlayerEndpoints.Leaderboard(db, discord, ct, category, limit: 200);
            db.LadderSnapshots.AddRange(ladder.Select(e => new LadderSnapshotRecord
            {
                Date = date,
                Category = category,
                PlayerId = e.PlayerId,
                Rank = e.Rank,
                Rating = e.Rating,
                Points = e.Points,
                Skill = e.Skill,
            }));

            if (category == MatchCategory.Season)
            {
                foreach (var entry in ladder.Where(e => !e.IsHouseBot && e.Rank <= 10))
                {
                    await AwardAsync(db, entry, AchievementCatalog.TopTen, earnedAt, ct);
                    if (entry.Rank == 1)
                    {
                        await AwardAsync(db, entry, AchievementCatalog.NumberOne, earnedAt, ct);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Took the ladder snapshot for {Day}", day);
        await PostAchievementDigestAsync(db, day, ct);
        return true;
    }

    /// <summary>
    /// Tells Discord who earned what that day: once, alongside the day's snapshot (the first-ever
    /// earners were announced as it happened).
    /// </summary>
    private async Task PostAchievementDigestAsync(NetRtsDb db, DateOnly day, CancellationToken ct)
    {
        var (start, end) = Window(day, options.Value.Zone);
        var earned = await db.BotAchievements.AsNoTracking()
            .Where(a => a.EarnedAt >= start && a.EarnedAt < end)
            .Join(db.Players.Include(p => p.Owner).Where(p => !p.IsHouseBot), a => a.PlayerId, p => p.Id, (a, p) => new { a.AchievementId, a.EarnedAt, Player = p })
            .ToListAsync(ct);
        var bots = earned
            .Where(e => AchievementCatalog.ById.ContainsKey(e.AchievementId))
            .GroupBy(e => e.Player.Id)
            .Select(g =>
            {
                var bot = g.First().Player;
                var verified = Accounts.IsVerified(bot.Owner, discord.Value);
                IReadOnlyList<string> names = g.OrderBy(e => e.EarnedAt).Select(e => AchievementCatalog.ById[e.AchievementId].Name).ToList();
                return (Bot: bot.Name, Verified: verified, Owner: verified ? bot.Owner!.DisplayName : null, Achievements: names);
            })
            .OrderByDescending(b => b.Achievements.Count).ThenBy(b => b.Bot, StringComparer.Ordinal)
            .ToList();
        announcer.AchievementDigest(day, bots);
    }

    private async Task AwardAsync(NetRtsDb db, LeaderboardEntryDto entry, string achievementId, DateTime earnedAt, CancellationToken ct)
    {
        if (await db.BotAchievements.AnyAsync(a => a.PlayerId == entry.PlayerId && a.AchievementId == achievementId, ct)
            || db.BotAchievements.Local.Any(a => a.PlayerId == entry.PlayerId && a.AchievementId == achievementId))
        {
            return;
        }

        var first = !await db.BotAchievements.AnyAsync(a => a.AchievementId == achievementId, ct)
                    && !db.BotAchievements.Local.Any(a => a.AchievementId == achievementId);
        db.BotAchievements.Add(new BotAchievementRecord { PlayerId = entry.PlayerId, AchievementId = achievementId, EarnedAt = earnedAt });
        if (first)
        {
            var a = AchievementCatalog.ById[achievementId];
            announcer.FirstToEarn(entry.Name, entry.Verified, entry.OwnerName, [(a.Name, a.Description)]);
        }
    }
}
