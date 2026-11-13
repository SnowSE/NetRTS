using System.Security.Claims;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Endpoints;

/// <summary>
/// Block Bot: strategies built from blocks in the browser (#/blocks) and played by the server. The page
/// starts or joins a match with a program, then can swap in a new version, flip switches and read what
/// every rule is doing, all while the match runs.
/// </summary>
public static class BlockEndpoints
{
    public static void MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        var blocks = app.MapGroup("/api/v1/blocks").WithTags("Block Bot");

        blocks.MapGet("/vocabulary", () => BlockProgramValidator.Vocabulary)
            .Produces<BlockVocabularyDto>()
            .WithSummary("The conditions, actions, choices and limits a Block Bot program may use, and the defaults for fields left out.");

        blocks.MapPost("/validate", (BlockProgramDto program) =>
                BlockProgramValidator.Validate(program) is { } error
                    ? ApiErrors.Error(400, "INVALID_PROGRAM", error)
                    : Results.NoContent())
            .Produces(204)
            .ProducesErrors()
            .WithSummary("Check a Block Bot program without playing it.");

        blocks.MapPost("/matches", Start)
            .RequireAuthorization()
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Start a practice match that the server plays for you with a Block Bot program: a sandbox (against the Sleepy Sitter), one house bot, a free-for-all, or (Create) a match of 2-4 seats for other players to join.");

        blocks.MapGet("/matches", (ClaimsPrincipal user, MatchManager manager, string? theme) =>
                manager.All
                    .Select(m => (Match: m, Bot: m.BotFor(user.PlayerId()) as BlockBot))
                    .Where(x => x.Bot is not null)
                    .Select(x => x.Bot!.Status(x.Match.Id, x.Match.Status, x.Match.SlotOf(user.PlayerId()) ?? -1, BlockWords.For(theme)))
                    .ToList())
            .RequireAuthorization()
            .Produces<IReadOnlyList<BlockBotStatusDto>>()
            .WithSummary("Your Block Bots in matches the server still has in memory, newest first. Every call that returns a Block Bot status takes theme=classic for the original NetRTS names instead of Snow College's.");

        blocks.MapPost("/matches/{matchId:guid}/join", Join)
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Take a seat in a waiting practice or hosted match with a Block Bot program. League matches don't take Block Bots.");

        blocks.MapPost("/matches/{matchId:guid}/start", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
            {
                Find(matchId, user, manager);
                return Results.Ok(manager.StartByCreator(matchId, user.PlayerId()).ToSummary());
            })
            .RequireAuthorization()
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Start a match you created (mode Create) with the players who have joined so far, at least two.");

        blocks.MapPut("/matches/{matchId:guid}/program", (Guid matchId, BlockProgramDto program, ClaimsPrincipal user, MatchManager manager, string? theme) =>
            {
                if (BlockProgramValidator.Validate(program) is { } error)
                {
                    return ApiErrors.Error(400, "INVALID_PROGRAM", error);
                }

                var (host, bot) = Find(matchId, user, manager);
                bot.Apply(program);
                return Results.Ok(bot.Status(matchId, host.Status, host.SlotOf(user.PlayerId()) ?? -1, BlockWords.For(theme)));
            })
            .RequireAuthorization()
            .Produces<BlockBotStatusDto>()
            .ProducesErrors()
            .WithSummary("Swap in a new version of your Block Bot's program. It takes over on the next tick.");

        blocks.MapPut("/matches/{matchId:guid}/switches", (Guid matchId, SetBlockSwitchRequest request, ClaimsPrincipal user, MatchManager manager, string? theme) =>
            {
                var (host, bot) = Find(matchId, user, manager);
                return bot.SetSwitch(request.Name, request.On)
                    ? Results.Ok(bot.Status(matchId, host.Status, host.SlotOf(user.PlayerId()) ?? -1, BlockWords.For(theme)))
                    : ApiErrors.Error(404, "SWITCH_NOT_FOUND", $"Your program has no switch called '{request.Name}'.");
            })
            .RequireAuthorization()
            .Produces<BlockBotStatusDto>()
            .ProducesErrors()
            .WithSummary("Flip one of your Block Bot's switches. Rules see it on the next tick.");

        blocks.MapGet("/matches/{matchId:guid}/status", (Guid matchId, ClaimsPrincipal user, MatchManager manager, string? theme) =>
            {
                var (host, bot) = Find(matchId, user, manager);
                host.Touch(user.PlayerId());
                return Results.Ok(bot.Status(matchId, host.Status, host.SlotOf(user.PlayerId()) ?? -1, BlockWords.For(theme)));
            })
            .RequireAuthorization()
            .Produces<BlockBotStatusDto>()
            .ProducesErrors()
            .WithSummary("What your Block Bot's rules did on the last tick: which fired, which are waiting and why. Pass theme=classic for the original NetRTS names instead of Snow College's.");
    }

    private static async Task<IResult> Start(StartBlockMatchRequest request, ClaimsPrincipal user, MatchManager manager, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct)
    {
        if (BlockProgramValidator.Validate(request.Program) is { } error)
        {
            return ApiErrors.Error(400, "INVALID_PROGRAM", error);
        }

        var opponents = request.Opponents ?? [];
        IReadOnlyList<string> houseBots = request.Mode switch
        {
            BlockPlayMode.Sandbox => ["sitter"],
            BlockPlayMode.Create => [],
            BlockPlayMode.HouseBot when opponents.Count == 1 => opponents,
            BlockPlayMode.FreeForAll when opponents.Count >= 2 && opponents.Count <= BlockProgramValidator.MaxOpponents => opponents,
            BlockPlayMode.HouseBot => throw new MatchException(400, "INVALID_SETTINGS", "Pick one house bot to play."),
            _ => throw new MatchException(400, "INVALID_SETTINGS", $"Pick 2 to {BlockProgramValidator.MaxOpponents} house bots for a free-for-all."),
        };

        var seat = await Accounts.SeatForAsync(db, discord.Value, user, ct) with { IsBlockBot = true };
        var bot = new BlockBot(seat.Name, request.Program);
        var create = request.Mode == BlockPlayMode.Create;
        if (create && request.Seats is { } seats && seats is < 2 or > 4)
        {
            return ApiErrors.Error(400, "INVALID_SETTINGS", "A match you create has 2 to 4 seats.");
        }

        var host = manager.Create(seat, new CreateMatchRequest
        {
            MaxPlayers = create ? request.Seats ?? 4 : houseBots.Count + 1,
            HouseBots = houseBots,
            Settings = request.TickIntervalMs is { } ms ? new MatchSettingsDto { TickIntervalMs = ms } : null,
            Name = create && !string.IsNullOrWhiteSpace(request.Name) ? request.Name.Trim() : null,
        }, bot);
        return Results.Created($"/api/v1/matches/{host.Id}", host.ToSummary());
    }

    private static async Task<IResult> Join(Guid matchId, JoinBlockMatchRequest request, ClaimsPrincipal user, MatchManager manager, LeagueMatchManager leagues, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct)
    {
        if (BlockProgramValidator.Validate(request.Program) is { } error)
        {
            return ApiErrors.Error(400, "INVALID_PROGRAM", error);
        }

        if (leagues.Find(matchId) is not null)
        {
            return ApiErrors.Error(409, "LEAGUE_MATCH", "League matches don't take Block Bots yet. Join a practice or hosted match.");
        }

        var seat = await Accounts.SeatForAsync(db, discord.Value, user, ct) with { IsBlockBot = true };
        var bot = new BlockBot(seat.Name, request.Program);
        var host = manager.Join(matchId, seat, bot);
        return Results.Ok(host.ToSummary());
    }

    private static (MatchHost Host, BlockBot Bot) Find(Guid matchId, ClaimsPrincipal user, MatchManager manager)
    {
        var host = manager.Get(matchId) ?? throw new MatchException(404, "MATCH_NOT_FOUND", "No live match with that id.");
        var bot = host.BotFor(user.PlayerId()) as BlockBot ?? throw new MatchException(404, "NO_BLOCK_BOT", "You don't have a Block Bot in this match.");
        return (host, bot);
    }
}
