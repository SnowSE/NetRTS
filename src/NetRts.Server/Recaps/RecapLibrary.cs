using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Recaps;

/// <summary>
/// Keeps the episode list in step with blob storage, so nobody has to manage it. The recap job writes
/// <c>{date}/episode.json</c> next to each video (as well as telling the server directly); every few
/// minutes this imports any episode the server doesn't know yet or that has changed, and drops
/// episodes whose video has been deleted from storage. Does nothing when blob storage isn't configured.
/// </summary>
public sealed class RecapLibrary(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    IOptions<RecapOptions> recap,
    TimeProvider time,
    ILogger<RecapLibrary> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, Azure.ETag> _seen = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (services.GetService<BlobServiceClient>() is not { } blobs)
        {
            return;
        }

        var container = blobs.GetBlobContainerClient(recap.Value.Container);
        using var timer = new PeriodicTimer(Every, time);
        do
        {
            try
            {
                await SyncAsync(container, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not sync recap episodes from storage");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: import new or changed episodes, drop ones whose video is gone. Returns how many changed.</summary>
    public async Task<int> SyncAsync(BlobContainerClient container, CancellationToken ct)
    {
        if (!await container.ExistsAsync(ct))
        {
            return 0;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<(string Name, Azure.ETag ETag)>();
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
        {
            names.Add(blob.Name);
            if (blob.Name.EndsWith("/episode.json", StringComparison.Ordinal))
            {
                details.Add((blob.Name, blob.Properties.ETag ?? default));
            }
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        var changed = 0;
        foreach (var (name, etag) in details)
        {
            if (_seen.TryGetValue(name, out var known) && known == etag)
            {
                continue;
            }

            var json = (await container.GetBlobClient(name).DownloadContentAsync(ct)).Value.Content;
            var request = JsonSerializer.Deserialize<PublishRecapRequest>(json, NetRtsClient.JsonOptions);
            // Only once its video is there: the job writes episode.json after uploading it.
            if (request is not null && names.Contains(request.Video))
            {
                var date = LadderSnapshots.Stored(DateOnly.ParseExact(request.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                var existing = await db.RecapEpisodes.AsNoTracking().FirstOrDefaultAsync(e => e.Date == date, ct);
                if (existing is null || existing.Video != request.Video || existing.Title != request.Title)
                {
                    var (episode, error) = await RecapEndpoints.UpsertAsync(db, request, time.GetUtcNow().UtcDateTime, ct);
                    if (episode is null)
                    {
                        logger.LogWarning("Skipped {Blob}: {Error}", name, error);
                    }
                    else
                    {
                        changed++;
                        logger.LogInformation("Imported the recap for {Date} from storage", request.Date);
                    }
                }
            }

            _seen[name] = etag;
        }

        // Episodes whose video was deleted from storage come off the site. (Absolute URLs aren't ours to check.)
        foreach (var episode in await db.RecapEpisodes.ToListAsync(ct))
        {
            if (!Uri.IsWellFormedUriString(episode.Video, UriKind.Absolute) && !names.Contains(episode.Video))
            {
                db.RecapEpisodes.Remove(episode);
                changed++;
                logger.LogInformation("Removed the recap for {Date}: its video is no longer in storage", DateOnly.FromDateTime(episode.Date));
            }
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }
}
