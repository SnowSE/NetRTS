using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Server.Data;
using NetRts.Server.Recaps;

namespace NetRts.Server.Endpoints;

/// <summary>
/// The nightly recap show: the episode list for the website, and (with the recap key) the night's
/// brief and publishing for the job that makes the videos (recap/ in the repository).
/// </summary>
public static class RecapEndpoints
{
    public const string KeyHeader = "X-Recap-Key";

    public static void MapRecapEndpoints(this IEndpointRouteBuilder app)
    {
        var recaps = app.MapGroup("/api/v1/recaps").WithTags("Recaps");

        recaps.MapGet("/", async (NetRtsDb db, CancellationToken ct, int limit = 30) =>
                (await db.RecapEpisodes.AsNoTracking().OrderByDescending(e => e.Date).Take(Math.Clamp(limit, 1, 365)).ToListAsync(ct))
                    .Select(e => ToDto(e, transcript: false)).ToList())
            .Produces<List<RecapEpisodeDto>>()
            .WithSummary("The nightly recap episodes, newest first.");

        recaps.MapGet("/latest", async (NetRtsDb db, CancellationToken ct) =>
                await db.RecapEpisodes.AsNoTracking().OrderByDescending(e => e.Date).FirstOrDefaultAsync(ct) is { } e
                    ? Results.Ok(ToDto(e, transcript: false))
                    : ApiErrors.NotFound("Episode"))
            .Produces<RecapEpisodeDto>()
            .ProducesErrors()
            .WithSummary("Last night's recap.");

        recaps.MapGet("/{id:guid}", async (Guid id, NetRtsDb db, CancellationToken ct) =>
                await db.RecapEpisodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct) is { } e
                    ? Results.Ok(ToDto(e, transcript: true))
                    : ApiErrors.NotFound("Episode"))
            .Produces<RecapEpisodeDto>()
            .ProducesErrors()
            .WithSummary("One episode, with its transcript.");

        recaps.MapGet("/brief", async (HttpContext http, RecapBrief brief, IOptions<NetRtsOptions> options, IOptions<RecapOptions> recap,
                TimeProvider time, CancellationToken ct, string? date = null) =>
            {
                if (KeyError(http, recap.Value) is { } error)
                {
                    return error;
                }

                DateOnly day;
                if (date is null)
                {
                    day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Value.Zone).DateTime).AddDays(-1);
                }
                else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                {
                    return ApiErrors.Error(400, "INVALID_DATE", "date is yyyy-MM-dd.");
                }

                return Results.Ok(await brief.BuildAsync(day, ct));
            })
            .Produces<RecapBriefDto>()
            .ProducesErrors()
            .WithSummary("The recap job only (X-Recap-Key): one day's facts for the show. Default: yesterday.");

        recaps.MapPost("/", Publish)
            .Produces<RecapEpisodeDto>()
            .ProducesErrors()
            .WithSummary("The recap job only (X-Recap-Key): publish an episode. Replaces any episode for the same day.");

        app.MapGet("/recaps/{id:guid}/video", (Guid id, NetRtsDb db, RecapVideos videos, CancellationToken ct) =>
                RedirectAsync(db, videos, id, e => e.Video, ct))
            .ExcludeFromDescription();

        app.MapGet("/recaps/{id:guid}/thumbnail", (Guid id, NetRtsDb db, RecapVideos videos, CancellationToken ct) =>
                RedirectAsync(db, videos, id, e => e.Thumbnail, ct))
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Publish(PublishRecapRequest request, HttpContext http, NetRtsDb db, IOptions<RecapOptions> recap,
        TimeProvider time, CancellationToken ct)
    {
        if (KeyError(http, recap.Value) is { } error)
        {
            return error;
        }

        var (episode, problem) = await UpsertAsync(db, request, time.GetUtcNow().UtcDateTime, ct);
        return episode is null ? ApiErrors.Error(400, "INVALID_EPISODE", problem!) : Results.Ok(ToDto(episode, transcript: true));
    }

    /// <summary>Adds an episode, or replaces the one for the same day. Returns the error instead when the request isn't one.</summary>
    internal static async Task<(RecapEpisodeRecord? Episode, string? Error)> UpsertAsync(NetRtsDb db, PublishRecapRequest request, DateTime now, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (null, "date is yyyy-MM-dd.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Video))
        {
            return (null, "An episode needs a title and a video.");
        }

        var date = LadderSnapshots.Stored(day);
        var episode = await db.RecapEpisodes.FirstOrDefaultAsync(e => e.Date == date, ct);
        if (episode is null)
        {
            episode = new RecapEpisodeRecord { Id = Guid.NewGuid(), Date = date };
            db.RecapEpisodes.Add(episode);
        }

        episode.Title = Truncate(request.Title.Trim(), 200);
        episode.Summary = Truncate(request.Summary?.Trim() ?? "", 2000);
        episode.Video = Truncate(request.Video.Trim(), 512);
        episode.Thumbnail = Truncate(request.Thumbnail?.Trim() ?? "", 512);
        episode.DurationSeconds = Math.Max(0, request.DurationSeconds);
        episode.TranscriptJson = JsonSerializer.Serialize(request.Transcript ?? [], NetRtsClient.JsonOptions);
        episode.CreatedAt = now;
        await db.SaveChangesAsync(ct);
        return (episode, null);
    }

    private static async Task<IResult> RedirectAsync(NetRtsDb db, RecapVideos videos, Guid id, Func<RecapEpisodeRecord, string> pick, CancellationToken ct)
    {
        var episode = await db.RecapEpisodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return episode is not null && await videos.UrlAsync(pick(episode), ct) is { } url
            ? Results.Redirect(url.ToString())
            : ApiErrors.NotFound("Video");
    }

    /// <summary>Null when the request carries the recap key; otherwise the error to return.</summary>
    private static IResult? KeyError(HttpContext http, RecapOptions recap)
    {
        if (string.IsNullOrEmpty(recap.ApiKey))
        {
            return ApiErrors.Error(404, "RECAPS_DISABLED", "The recap job isn't set up on this server.");
        }

        var given = Encoding.UTF8.GetBytes(http.Request.Headers[KeyHeader].FirstOrDefault() ?? "");
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(recap.ApiKey))
            ? null
            : ApiErrors.Error(401, "INVALID_RECAP_KEY", $"Send the recap key in {KeyHeader}.");
    }

    private static RecapEpisodeDto ToDto(RecapEpisodeRecord e, bool transcript) => new()
    {
        Id = e.Id,
        Date = DateOnly.FromDateTime(e.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Title = e.Title,
        Summary = e.Summary,
        VideoUrl = $"/recaps/{e.Id}/video",
        ThumbnailUrl = string.IsNullOrEmpty(e.Thumbnail) ? null : $"/recaps/{e.Id}/thumbnail",
        DurationSeconds = e.DurationSeconds,
        Transcript = transcript ? JsonSerializer.Deserialize<List<RecapLineDto>>(e.TranscriptJson, NetRtsClient.JsonOptions) : null,
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
