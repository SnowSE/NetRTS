using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Endpoints;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/v1/matches").WithTags("Matches");

        matches.MapPost("/", (CreateMatchRequest request, ClaimsPrincipal user, MatchManager manager) =>
            {
                var host = manager.Create(user.PlayerId(), user.PlayerName(), request);
                return Results.Created($"/api/v1/matches/{host.Id}", host.ToSummary());
            })
            .RequireAuthorization()
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Create a match (you take slot 0). Optionally fill seats with house bots; it starts when full.");

        matches.MapGet("/", (MatchManager manager, MatchStatus? status) =>
                manager.All.Select(m => m.ToSummary()).Where(m => status is null || m.Status == status).ToList())
            .WithSummary("Matches currently in memory: waiting for players, running, or recently finished.");

        matches.MapGet("/history", History)
            .WithSummary("Recently completed matches from the archive.");

        matches.MapGet("/{matchId:guid}", async (Guid matchId, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                if (manager.Get(matchId) is { } host)
                {
                    return Results.Ok(host.ToSummary());
                }

                var record = await Archived(db, matchId, ct);
                return record is null ? ApiErrors.NotFound("Match") : Results.Text(record.SummaryJson, "application/json");
            })
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("One match's summary.");

        matches.MapGet("/named/{name}", async (string name, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                if (!MatchManager.IsValidName(name))
                {
                    return ApiErrors.NotFound("Match");
                }

                // A name belongs to one unfinished match at a time; after that it can be reused, so prefer
                // the unfinished one, then the newest.
                var live = manager.All.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if ((live.FirstOrDefault(m => m.Status != MatchStatus.Completed) ?? live.FirstOrDefault()) is { } host)
                {
                    return Results.Ok(host.ToSummary());
                }

                var needle = $"\"name\":\"{name.ToLowerInvariant()}\"";
                var json = await db.Matches.AsNoTracking()
                    .Where(m => m.SummaryJson.ToLower().Contains(needle))
                    .OrderByDescending(m => m.CompletedAt)
                    .Select(m => m.SummaryJson)
                    .FirstOrDefaultAsync(ct);
                return json is null ? ApiErrors.NotFound("Match") : Results.Text(json, "application/json");
            })
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("The match with this name: the one waiting or running, otherwise the most recent.");

        matches.MapPost("/{matchId:guid}/join", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
                Results.Ok(manager.Join(matchId, user.PlayerId(), user.PlayerName()).ToSummary()))
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Take a free seat in a waiting match.");

        matches.MapPost("/{matchId:guid}/speed", (Guid matchId, MatchSpeedRequest request, MatchManager manager) =>
                Results.Ok(manager.SetSpeed(matchId, request).ToSummary()))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Change a running match's tick interval or pause it. Only for matches with at most one real player; anyone watching may do it.");

        matches.MapPost("/{matchId:guid}/step", (Guid matchId, MatchManager manager) =>
                Results.Ok(manager.Step(matchId).ToSummary()))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Run one tick of a paused match.");

        matches.MapPost("/{matchId:guid}/leave", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
            {
                return Results.Ok(manager.Leave(matchId, user.PlayerId()).ToSummary());
            })
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Give up your seat in a match that hasn't started.");

        matches.MapGet("/{matchId:guid}/map", async (Guid matchId, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                if (manager.Get(matchId) is { } host)
                {
                    return host.Map is { } map
                        ? Results.Ok(map)
                        : ApiErrors.Error(409, "MATCH_NOT_STARTED", "The map is generated when the match starts.");
                }

                // Maps are deterministic, so archived matches can be regenerated from their settings.
                var record = await Archived(db, matchId, ct);
                if (record is null)
                {
                    return ApiErrors.NotFound("Match");
                }

                var players = JsonSerializer.Deserialize<MatchSummaryDto>(record.SummaryJson, NetRtsClient.JsonOptions)!.Players.Count;
                return Results.Ok(MapGenerator.Generate(record.MapWidth, record.MapHeight, record.Seed, players).ToDto());
            })
            .Produces<MapDto>()
            .ProducesErrors()
            .WithSummary("Terrain and start positions (public knowledge).");

        matches.MapGet("/{matchId:guid}/state", GetState)
            .RequireAuthorization()
            .Produces<GameStateDto>()
            .ProducesErrors()
            .WithSummary("Your fog-of-war view. Pass waitForTick=N to long-poll until tick N has been simulated.");

        matches.MapPost("/{matchId:guid}/commands", (Guid matchId, SubmitCommandsRequest request, ClaimsPrincipal user, MatchManager manager) =>
            {
                var host = LiveParticipantMatch(manager, matchId, user);
                if (host.Status == MatchStatus.Waiting)
                {
                    return ApiErrors.Error(409, "MATCH_NOT_STARTED", "The match hasn't started yet.");
                }

                if (request.Commands is null || request.Commands.Count == 0)
                {
                    return ApiErrors.Error(400, "NO_COMMANDS", "Send at least one command.");
                }

                return Results.Ok(host.Submit(user.PlayerId(), request.Commands));
            })
            .RequireAuthorization()
            .Produces<SubmitCommandsResponse>()
            .ProducesErrors()
            .WithSummary("Queue commands. Each is validated now and executed on an upcoming tick (100 per tick, FIFO).");

        matches.MapDelete("/{matchId:guid}/commands", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
                Results.Ok(new { cleared = LiveParticipantMatch(manager, matchId, user).ClearQueue(user.PlayerId()) ?? 0 }))
            .RequireAuthorization()
            .WithSummary("Drop every command still waiting in your queue.");

        matches.MapPost("/{matchId:guid}/surrender", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
                LiveParticipantMatch(manager, matchId, user) is var host && host.Surrender(user.PlayerId())
                    ? Results.Ok(host.ToSummary())
                    : ApiErrors.Error(409, "MATCH_NOT_ACTIVE", "The match is not in progress."))
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Concede. Your units and buildings are removed. Returns the match, with its outcome if it ended.");

        matches.MapGet("/{matchId:guid}/result", async (Guid matchId, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                if (manager.Get(matchId) is { } host)
                {
                    return host.GetResult() is { } result
                        ? Results.Ok(result)
                        : ApiErrors.Error(409, "MATCH_NOT_COMPLETED", "The match is still in progress.");
                }

                var record = await Archived(db, matchId, ct);
                return record is null ? ApiErrors.NotFound("Match") : Results.Text(record.ResultJson, "application/json");
            })
            .Produces<MatchResultDto>()
            .ProducesErrors()
            .WithSummary("Winner, end reason and score breakdown of a finished match.");

        matches.MapGet("/{matchId:guid}/replay", async (Guid matchId, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                if (manager.Get(matchId) is { } host)
                {
                    return host.GetReplay() is { } replay
                        ? Results.Ok(replay)
                        : ApiErrors.Error(409, "MATCH_NOT_COMPLETED", "Replays are available once the match ends.");
                }

                var record = await Archived(db, matchId, ct);
                return record is null ? ApiErrors.NotFound("Match") : Results.Text(record.ReplayJson, "application/json");
            })
            .Produces<ReplayDto>()
            .ProducesErrors()
            .WithSummary("Seed plus every executed command: enough to re-simulate the match exactly.");

        matches.MapGet("/{matchId:guid}/spectate", (Guid matchId, MatchManager manager, int? sinceTick, bool? fog) =>
            {
                var host = manager.Get(matchId);
                if (host is null)
                {
                    return ApiErrors.NotFound("Live match");
                }

                return host.GetSpectatorView(sinceTick, fog ?? false) is { } view
                    ? Results.Ok(view)
                    : ApiErrors.Error(409, "MATCH_NOT_STARTED", "The match hasn't started yet.");
            })
            .Produces<SpectatorStateDto>()
            .ProducesErrors()
            .WithSummary("Omniscient view of a live match for spectators.");

        matches.MapGet("/{matchId:guid}/spectate/stream", SpectateStream)
            .Produces(200, contentType: "text/event-stream")
            .ProducesErrors()
            .WithSummary("Server-sent events: one spectator snapshot per tick until the match ends.");

        app.MapPost("/api/v1/exhibitions", (CreateExhibitionRequest request, MatchManager manager) =>
            {
                var host = manager.CreateExhibition(request);
                return Results.Created($"/api/v1/matches/{host.Id}", host.ToSummary());
            })
            .RequireRateLimiting(RateLimits.Exhibition)
            .WithTags("Matches")
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Start a house-bot-vs-house-bot match to watch.");
    }

    private static async Task<IResult> GetState(
        Guid matchId,
        ClaimsPrincipal user,
        MatchManager manager,
        IOptions<NetRtsOptions> options,
        CancellationToken ct,
        int? waitForTick = null,
        int? sinceTick = null)
    {
        var host = LiveParticipantMatch(manager, matchId, user);
        if (waitForTick is { } tick)
        {
            await host.WaitForTickAsync(tick, TimeSpan.FromSeconds(options.Value.MaxLongPollSeconds), ct);
        }

        return host.GetPlayerView(user.PlayerId(), sinceTick) is { } view
            ? Results.Ok(view)
            : ApiErrors.Error(409, "MATCH_NOT_STARTED", "The match hasn't started yet. Long-poll with waitForTick=0 to wait for it.");
    }

    private static async Task SpectateStream(Guid matchId, bool? fog, MatchManager manager, HttpContext context, CancellationToken ct)
    {
        var host = manager.Get(matchId);
        if (host is null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new ApiErrorDto { Code = "NOT_FOUND", Message = "Live match not found." }, ct);
            return;
        }

        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var lastTick = -1;
        (int, bool) lastSpeed = default;
        while (!ct.IsCancellationRequested)
        {
            var next = host.NextTick;
            var view = host.GetSpectatorView(lastTick < 0 ? null : lastTick, fog ?? false);
            // A new tick, or a speed change or pause, which other spectators should see straight away.
            if (view is not null && (view.Tick != lastTick || (view.TickIntervalMs, view.Paused) != lastSpeed))
            {
                lastSpeed = (view.TickIntervalMs, view.Paused);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(view, NetRtsClient.JsonOptions)}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
                lastTick = view.Tick;
                if (view.Status == MatchStatus.Completed)
                {
                    return;
                }
            }

            try
            {
                await next.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            catch (TimeoutException)
            {
                await context.Response.WriteAsync(": keep-alive\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
    }

    private static async Task<IReadOnlyList<MatchSummaryDto>> History(NetRtsDb db, CancellationToken ct, int limit = 20)
    {
        var rows = await db.Matches.AsNoTracking()
            .OrderByDescending(m => m.CompletedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(m => m.SummaryJson)
            .ToListAsync(ct);
        return rows.Select(json => JsonSerializer.Deserialize<MatchSummaryDto>(json, NetRtsClient.JsonOptions)!).ToList();
    }

    private static Task<MatchRecord?> Archived(NetRtsDb db, Guid matchId, CancellationToken ct) =>
        db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);

    private static MatchHost LiveParticipantMatch(MatchManager manager, Guid matchId, ClaimsPrincipal user)
    {
        var host = manager.Get(matchId)
                   ?? throw new MatchException(404, "MATCH_NOT_FOUND", "No live match with that id (finished matches: see /result).");
        if (!host.HasPlayer(user.PlayerId()))
        {
            throw new MatchException(403, "NOT_A_PARTICIPANT", "You are not playing in this match.");
        }

        return host;
    }
}
