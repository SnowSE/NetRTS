using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
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
    private const string SerumHeader = "X-SUPERPOWER-SUPERSERUM";

    internal sealed record SerumRequest(string? IfGrenadeThrown);

    public static void MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/v1/matches").WithTags("Matches");

        matches.MapPost("/", async (CreateMatchRequest request, ClaimsPrincipal user, MatchManager manager, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct) =>
            {
                var host = manager.Create(await Accounts.SeatForAsync(db, discord.Value, user, ct), request);
                return Results.Created($"/api/v1/matches/{host.Id}", host.ToSummary());
            })
            .RequireAuthorization()
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Create a practice match (you take slot 0). Optionally fill seats with house bots; it starts when full.");

        matches.MapGet("/", (MatchManager manager, LeagueMatchManager leagues, MatchStatus? status) =>
                leagues.All.Select(leagues.ToSummary).Concat(manager.All.Select(m => m.ToSummary()))
                    .Where(m => status is null || m.Status == status).ToList())
            .WithSummary("Matches currently in memory: waiting for players, running, or recently finished. League matches come first.");

        matches.MapGet("/history", History)
            .WithSummary("Recently completed matches from the archive, optionally only one category.");

        matches.MapGet("/{matchId:guid}", async (Guid matchId, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, CancellationToken ct) =>
            {
                if (leagues.Find(matchId) is { } league)
                {
                    return Results.Ok(leagues.ToSummary(league));
                }

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

        matches.MapGet("/named/{name}", async (string name, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, CancellationToken ct) =>
            {
                if (!MatchManager.IsValidName(name))
                {
                    return ApiErrors.NotFound("Match");
                }

                if (leagues.Named(name) is { } league)
                {
                    return Results.Ok(leagues.ToSummary(league));
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

        matches.MapPost("/{matchId:guid}/join", async (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct) =>
            {
                var seat = await Accounts.SeatForAsync(db, discord.Value, user, ct);
                return leagues.Find(matchId) is { } league
                    ? Results.Ok(leagues.ToSummary(leagues.Join(league.Id, seat)))
                    : Results.Ok(manager.Join(matchId, seat).ToSummary());
            })
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Take a seat in a waiting match. League matches (preseason, season, championship) take Discord-verified bots, one per person; play continues on the same match id once the admin starts the heats.");

        matches.MapPost("/{matchId:guid}/speed", (Guid matchId, MatchSpeedRequest request, MatchManager manager, LeagueMatchManager leagues) =>
                Results.Ok(manager.SetSpeed(NotLeague(matchId, leagues), request).ToSummary()))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Change a running match's tick interval or pause it. Only for matches with at most one real player; anyone watching may do it.");

        matches.MapPost("/{matchId:guid}/step", (Guid matchId, MatchManager manager, LeagueMatchManager leagues) =>
                Results.Ok(manager.Step(NotLeague(matchId, leagues)).ToSummary()))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Run one tick of a paused match.");

        matches.MapPost("/{matchId:guid}/leave", (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues) =>
                leagues.Find(matchId) is { } league
                    ? Results.Ok(leagues.ToSummary(leagues.Leave(league.Id, user.PlayerId())))
                    : Results.Ok(manager.Leave(matchId, user.PlayerId()).ToSummary()))
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Give up your seat in a match that hasn't started.");

        matches.MapGet("/{matchId:guid}/map", async (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, CancellationToken ct) =>
            {
                matchId = HeatOfCaller(matchId, user, leagues);
                if (manager.Get(matchId) is { } host)
                {
                    return host.Map is { } map
                        ? Results.Ok(map)
                        : NotStarted("The map is generated when the match starts.");
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

        matches.MapPost("/{matchId:guid}/commands", (Guid matchId, SubmitCommandsRequest request, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, HttpContext http) =>
            {
                var host = LiveParticipantMatch(manager, HeatOfCaller(matchId, user, leagues), user);
                if (host.Status == MatchStatus.Waiting)
                {
                    return NotStarted();
                }

                if (request.Commands is null || request.Commands.Count == 0)
                {
                    return ApiErrors.Error(400, "NO_COMMANDS", "Send at least one command.");
                }

                return Results.Ok(host.Submit(user.PlayerId(), request.Commands, http.Request.Headers[SerumHeader].FirstOrDefault()));
            })
            .RequireAuthorization()
            .Produces<SubmitCommandsResponse>()
            .ProducesErrors()
            .WithSummary("Queue commands. Each is validated now and executed on an upcoming tick (100 per tick, FIFO).");

        matches.MapDelete("/{matchId:guid}/commands", (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues) =>
                Results.Ok(new { cleared = LiveParticipantMatch(manager, HeatOfCaller(matchId, user, leagues), user).ClearQueue(user.PlayerId()) ?? 0 }))
            .RequireAuthorization()
            .WithSummary("Drop every command still waiting in your queue.");

        matches.MapPost("/{matchId:guid}/surrender", (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues) =>
                LiveParticipantMatch(manager, HeatOfCaller(matchId, user, leagues), user) is var host && host.Surrender(user.PlayerId())
                    ? Results.Ok(host.ToSummary())
                    : ApiErrors.Error(409, "MATCH_NOT_ACTIVE", "The match is not in progress."))
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Concede. Your units and buildings are removed. Returns the match, with its outcome if it ended.");

        matches.MapGet("/{matchId:guid}/superpower/superserum", (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues) =>
                Results.Text(LiveParticipantMatch(manager, HeatOfCaller(matchId, user, leagues), user).IssueSerum(user.PlayerId())))
            .RequireAuthorization()
            .ExcludeFromDescription();

        matches.MapPost("/{matchId:guid}/superpower/superserum", async (Guid matchId, [FromBody] SerumRequest? request, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, MatchAnnouncer announcer, CancellationToken ct) =>
            {
                var host = LiveParticipantMatch(manager, HeatOfCaller(matchId, user, leagues), user);
                if (!string.Equals(request?.IfGrenadeThrown?.Trim(), "jump on it", StringComparison.OrdinalIgnoreCase))
                {
                    return ApiErrors.Error(400, "INVALID_REQUEST", "Everybody else ran.");
                }

                if (host.Status == MatchStatus.Waiting)
                {
                    return NotStarted();
                }

                var (outcome, untilTick) = host.TakeSerum(user.PlayerId());
                if (outcome == SerumOutcome.Taken && host.Seats.FirstOrDefault(s => s.PlayerId == user.PlayerId()) is { } seat)
                {
                    await announcer.SerumTakenAsync(seat, ct);
                }

                return outcome switch
                {
                    SerumOutcome.Taken => Results.Ok(new { message = "Not a perfect soldier, but a good man.", untilTick }),
                    SerumOutcome.NotIssued => ApiErrors.Error(409, "NOT_SELECTED", "Is this a test?"),
                    SerumOutcome.AlreadyTaken => ApiErrors.Error(410, "SERUM_GONE", "There's no more serum. Erskine took the formula with him."),
                    _ => ApiErrors.Error(409, "MATCH_NOT_ACTIVE", "The match is not in progress."),
                };
            })
            .RequireAuthorization()
            .ExcludeFromDescription();

        matches.MapGet("/{matchId:guid}/result", async (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, CancellationToken ct) =>
            {
                matchId = HeatOfCaller(matchId, user, leagues);
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

        matches.MapGet("/{matchId:guid}/replay", async (Guid matchId, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, CancellationToken ct) =>
            {
                matchId = HeatOfCaller(matchId, user, leagues);
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

        matches.MapGet("/{matchId:guid}/frames", async (Guid matchId, HttpContext http, ReplayFrameStore frames, CancellationToken ct) =>
            {
                var (status, manifest) = await frames.ManifestAsync(matchId, ct);
                return status switch
                {
                    FramesStatus.Ok => Brotli(http, manifest!, "application/json"),
                    FramesStatus.NotCompleted => ApiErrors.Error(409, "MATCH_NOT_COMPLETED", "Frames are available once the match ends."),
                    FramesStatus.Unavailable => ApiErrors.Error(409, "FRAMES_UNAVAILABLE",
                        "This match was played before frames were recorded, under rules that have changed since, so it can't be shown frame by frame. Its replay is still available."),
                    _ => ApiErrors.NotFound("Match"),
                };
            })
            .Produces<ReplayFramesDto>()
            .ProducesErrors()
            .WithSummary("How to play a finished match back tick by tick: its map, its play-by-play and how many chunks of frames there are.");

        matches.MapGet("/{matchId:guid}/frames/{index:int}", async (Guid matchId, int index, HttpContext http, ReplayFrameStore frames, CancellationToken ct) =>
                await frames.ChunkAsync(matchId, index, ct) is { } chunk
                    ? Brotli(http, chunk, "application/x-ndjson")
                    : ApiErrors.NotFound("Frames"))
            .Produces(200, contentType: "application/x-ndjson")
            .ProducesErrors()
            .WithSummary("One chunk of a finished match's frames: a SpectatorStateDto per line, from tick index × chunkTicks.");

        matches.MapGet("/{matchId:guid}/spectate", (Guid matchId, MatchManager manager, LeagueMatchManager leagues, int? sinceTick, bool? fog) =>
            {
                var host = manager.Get(NotLeague(matchId, leagues));
                if (host is null)
                {
                    return ApiErrors.NotFound("Live match");
                }

                return host.GetSpectatorView(sinceTick, fog ?? false) is { } view
                    ? Results.Ok(view)
                    : NotStarted();
            })
            .Produces<SpectatorStateDto>()
            .ProducesErrors()
            .WithSummary("Omniscient view of a live match for spectators.");

        matches.MapGet("/{matchId:guid}/standings", (Guid matchId, MatchManager manager, LeagueMatchManager leagues) =>
                leagues.Find(matchId) is { } league ? Results.Ok(leagues.Standings(league))
                : manager.Get(matchId) is { } host ? Results.Ok(new[] { LeagueMatchManager.StandingsOf(host) })
                : ApiErrors.NotFound("Live match"))
            .Produces<IReadOnlyList<HeatStandingsDto>>()
            .ProducesErrors()
            .WithSummary("Live scoreboard: for a league match, one table per heat (with the points each player would earn); otherwise one table.");

        matches.MapGet("/{matchId:guid}/spectate/stream", SpectateStream)
            .Produces(200, contentType: "text/event-stream")
            .ProducesErrors()
            .WithSummary("Server-sent events: one spectator snapshot per tick until the match ends.");

        var admin = app.MapGroup("/api/v1/admin/matches").WithTags("Admin").RequireAuthorization(Accounts.AdminPolicy);

        admin.MapPost("/", (CreateLeagueMatchRequest request, LeagueMatchManager leagues, MatchAnnouncer announcer) =>
            {
                var league = leagues.Create(request);
                announcer.LeagueMatchOpened(league);
                return Results.Created($"/api/v1/matches/{league.Id}", leagues.ToSummary(league));
            })
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Create a preseason, season or championship match for verified bots to join (Discord server administrators, signed in on the website).");

        admin.MapPost("/{matchId:guid}/heats", (Guid matchId, SplitHeatsRequest request, LeagueMatchManager leagues) =>
                leagues.ToSummary(leagues.Split(matchId, request.Sizes)))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Shuffle everyone into heats of these sizes (they must add up to everyone who joined). Again to reshuffle.");

        admin.MapDelete("/{matchId:guid}/heats", (Guid matchId, LeagueMatchManager leagues) => leagues.ToSummary(leagues.Unsplit(matchId)))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Undo the split and reopen the match to new players.");

        admin.MapPost("/{matchId:guid}/start", (Guid matchId, LeagueMatchManager leagues) => leagues.ToSummary(leagues.Start(matchId)))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Start every heat of the split round at once.");

        admin.MapPost("/{matchId:guid}/close", async (Guid matchId, LeagueMatchManager leagues, LeagueFinalizer finalizer, CancellationToken ct) =>
            {
                var league = leagues.Close(matchId);
                await finalizer.FinishAsync(league, TimeSpan.FromSeconds(15), ct);
                return leagues.ToSummary(league);
            })
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Close a league match between rounds: no more rounds, its points go on the ladder (each bot's heats less its lowest), and the results are posted.");

        admin.MapDelete("/{matchId:guid}/players/{playerId:guid}", (Guid matchId, Guid playerId, LeagueMatchManager leagues) =>
                leagues.ToSummary(leagues.Remove(matchId, playerId)))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Take a bot out of the match (and out of its heat, if split).");

        admin.MapDelete("/{matchId:guid}", (Guid matchId, LeagueMatchManager leagues) => leagues.ToSummary(leagues.Cancel(matchId)))
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Call off a league match that hasn't started.");

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

    /// <summary>
    /// Sends stored Brotli-compressed bytes as they are when the client accepts Brotli, decompressed
    /// otherwise. Recorded frames never change, so they can be cached for good.
    /// </summary>
    private static IResult Brotli(HttpContext http, byte[] data, string contentType)
    {
        http.Response.Headers.Vary = "Accept-Encoding";
        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        if (http.Request.Headers.AcceptEncoding.ToString().Contains("br", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Headers.ContentEncoding = "br";
            return Results.Bytes(data, contentType);
        }

        return Results.Bytes(ReplayFrameRecorder.Decompress(data), contentType);
    }

    private static async Task<IResult> GetState(
        Guid matchId,
        ClaimsPrincipal user,
        MatchManager manager,
        LeagueMatchManager leagues,
        IOptions<NetRtsOptions> options,
        CancellationToken ct,
        int? waitForTick = null,
        int? sinceTick = null)
    {
        // A league match: wait (when long-polling) for the admin to start the round, then use this bot's
        // heat. Waiting for tick 0 after a heat has finished means waiting for the next round.
        if (leagues.Find(matchId) is { } league)
        {
            var wait = waitForTick is null ? TimeSpan.Zero : TimeSpan.FromSeconds(options.Value.MaxLongPollSeconds);
            var heat = await leagues.HeatForAsync(league, user.PlayerId(), nextGame: waitForTick == 0, wait, ct);
            if (heat is null)
            {
                return NotStarted("The admin hasn't started the next round yet. Long-poll with waitForTick=0 to wait for it.");
            }

            matchId = heat.Id;
        }

        var host = LiveParticipantMatch(manager, matchId, user);
        if (waitForTick is { } tick)
        {
            await host.WaitForTickAsync(tick, TimeSpan.FromSeconds(options.Value.MaxLongPollSeconds), ct);
        }

        return host.GetPlayerView(user.PlayerId(), sinceTick) is { } view
            ? Results.Ok(view)
            : NotStarted("The match hasn't started yet. Long-poll with waitForTick=0 to wait for it.");
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

    private static async Task<IReadOnlyList<MatchSummaryDto>> History(NetRtsDb db, CancellationToken ct, int limit = 20, MatchCategory? category = null)
    {
        var rows = await db.Matches.AsNoTracking()
            .Where(m => category == null || m.Category == category)
            .OrderByDescending(m => m.CompletedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(m => m.SummaryJson)
            .ToListAsync(ct);
        return rows.Select(json => JsonSerializer.Deserialize<MatchSummaryDto>(json, NetRtsClient.JsonOptions)!).ToList();
    }

    /// <summary>409 MATCH_NOT_STARTED, for anything that needs a running match.</summary>
    private static IResult NotStarted(string message = "The match hasn't started yet.") =>
        ApiErrors.Error(409, "MATCH_NOT_STARTED", message);

    private static Task<MatchRecord?> Archived(NetRtsDb db, Guid matchId, CancellationToken ct) =>
        db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);

    /// <summary>
    /// League matches are played in heats: a bot's calls on the league match go to its own heat. Others
    /// (spectators) must pick a heat. Ordinary match ids pass through unchanged.
    /// </summary>
    private static Guid HeatOfCaller(Guid matchId, ClaimsPrincipal user, LeagueMatchManager leagues)
    {
        if (leagues.Find(matchId) is not { } league)
        {
            return matchId;
        }

        if (user.TryGetPlayerId(out var playerId))
        {
            if (leagues.StartedHeatOf(league, playerId) is { } heat)
            {
                return heat.Id;
            }

            lock (league.Gate)
            {
                if (league.IsQueued(playerId))
                {
                    throw new MatchException(409, "MATCH_NOT_STARTED", "The admin hasn't started the first round yet.");
                }
            }
        }

        return NotLeague(matchId, leagues);
    }

    /// <summary>Spectator calls on a league match must name one of its heats.</summary>
    private static Guid NotLeague(Guid matchId, LeagueMatchManager leagues) =>
        leagues.Find(matchId) is null
            ? matchId
            : throw new MatchException(409, "PICK_A_HEAT", "This match is played in heats; use a heat's id (see heats in GET /api/v1/matches/{id}).");

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
