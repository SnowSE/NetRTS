using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Recaps;

/// <summary>
/// Posts last night's recap to Discord each morning at <see cref="RecapOptions.AnnounceAt"/> (local
/// time), or as soon as it's published if that's later in the day. Each episode is posted once:
/// re-making a day doesn't post it again, and older days are never posted.
/// </summary>
public sealed class RecapAnnouncer(
    IServiceScopeFactory scopes,
    IOptions<NetRtsOptions> options,
    IOptions<RecapOptions> recap,
    MatchAnnouncer announcer,
    TimeProvider time,
    ILogger<RecapAnnouncer> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!announcer.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every, time);
        do
        {
            try
            {
                await AnnounceAsync(time.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not announce the recap");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Posts the previous day's episode if it's <paramref name="now"/> past the announce time and it hasn't been posted. Returns whether it posted.</summary>
    public async Task<bool> AnnounceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(now, options.Value.Zone).DateTime;
        if (TimeOnly.FromDateTime(local) < recap.Value.AnnounceAt)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        var date = LadderSnapshots.Stored(DateOnly.FromDateTime(local).AddDays(-1));
        var episode = await db.RecapEpisodes.FirstOrDefaultAsync(e => e.Date == date && e.AnnouncedAt == null, ct);
        if (episode is null)
        {
            return false;
        }

        episode.AnnouncedAt = now.UtcDateTime;
        await db.SaveChangesAsync(ct);
        announcer.RecapReady(episode);
        logger.LogInformation("Announced the recap for {Date}", DateOnly.FromDateTime(episode.Date));
        return true;
    }
}
