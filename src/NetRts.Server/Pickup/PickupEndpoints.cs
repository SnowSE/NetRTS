using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;

namespace NetRts.Server.Pickup;

/// <summary>The pick-up game's API: phones (join, plan, start now, orders) and the big screen (the hill).</summary>
public static class PickupEndpoints
{
    public static void MapPickupEndpoints(this IEndpointRouteBuilder app)
    {
        var pickup = app.MapGroup("/api/v1/pickup").WithTags("Pick-up Game");

        pickup.MapPost("/join", (PickupTokenRequest request, PickupHill hill, TimeProvider time) =>
                Guarded(() => Results.Ok(hill.Join(request.Token, Now(time), request.Name))))
            .RequireRateLimiting(RateLimits.PickupJoin)
            .Produces<PickupMeDto>()
            .ProducesErrors()
            .WithSummary("Join the pick-up line (or come back to it with your token): a name, a place in line and the first deck as your plan.");

        pickup.MapGet("/me", (string token, PickupHill hill, TimeProvider time) => Guarded(() => Results.Ok(hill.Me(token, Now(time)))))
            .Produces<PickupMeDto>()
            .ProducesErrors()
            .WithSummary("Your phone's view: your place in line, the countdown, your match as it plays, or how it went.");

        pickup.MapPut("/plan", (PickupPlanRequest request, PickupHill hill) => Guarded(() =>
                hill.SetPlan(request.Token, request.Plan) is { } error ? ApiErrors.Error(400, "INVALID_PLAN", error) : Results.NoContent()))
            .ProducesErrors()
            .WithSummary("Store your plan. It's read when your match starts.");

        pickup.MapPost("/start-now", (PickupTokenRequest request, PickupHill hill, TimeProvider time) =>
                Guarded(() => Results.Ok(hill.StartNow(request.Token ?? "", Now(time)))))
            .Produces<PickupMeDto>()
            .ProducesErrors()
            .WithSummary("Skip the rest of your planning time.");

        pickup.MapPost("/rejoin", (PickupTokenRequest request, PickupHill hill, TimeProvider time) =>
                Guarded(() => Results.Ok(hill.Rejoin(request.Token ?? "", Now(time)))))
            .Produces<PickupMeDto>()
            .ProducesErrors()
            .WithSummary("After your match: back in line.");

        pickup.MapPost("/hold", (PickupHoldRequest request, PickupHill hill, TimeProvider time) =>
                Guarded(() => Results.Ok(hill.Hold(request.Token, request.Hold, Now(time)))))
            .Produces<PickupMeDto>()
            .ProducesErrors()
            .WithSummary("Not ready yet (hold: true) keeps your place while the next ready player goes ahead; hold: false when you're ready.");

        pickup.MapPost("/order", (PickupOrderRequest request, PickupHill hill) => Guarded(() =>
            {
                hill.Order(request.Token, request.Order);
                return Results.NoContent();
            }))
            .ProducesErrors()
            .WithSummary("While your bot plays: attack (attack now) or pullback (bring the fighters home).");

        pickup.MapGet("/hill", (PickupHill hill, TimeProvider time) => Results.Ok(hill.Hill(Now(time))))
            .Produces<PickupHillDto>()
            .WithSummary("The big screen: the match on now, the champion, who's playing and who's in line.");

        pickup.MapGet("/go/{link}", (string link, string? program, MatchMetrics metrics) =>
            {
                // The match-over page's links come through here so the booth can count them, then go on.
                if (link == "takehome")
                {
                    if (program is null || program.Length > 16_000 || !program.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    {
                        return ApiErrors.Error(400, "INVALID_PROGRAM", "That take-home link is broken.");
                    }

                    metrics.PickupLinkClicked(link);
                    return Results.Redirect($"/#/blocks/import?program={program}");
                }

                if (!GoLinks.TryGetValue(link, out var to))
                {
                    return ApiErrors.Error(404, "NOT_FOUND", "No such link.");
                }

                metrics.PickupLinkClicked(link);
                return Results.Redirect(to);
            })
            .ExcludeFromDescription();

        pickup.MapGet("/decks", () => Results.Ok(PickupPlans.Decks))
            .Produces<IReadOnlyList<PickupDeckDto>>()
            .WithSummary("The ready-made plans (the house plays these).");
    }

    private static DateTime Now(TimeProvider time) => time.GetUtcNow().UtcDateTime;

    /// <summary>Where the match-over links go (takehome carries the plan as a Block Bot program).</summary>
    private static readonly Dictionary<string, string> GoLinks = new()
    {
        ["program"] = "https://engineering.snow.edu",
        ["cost"] = "https://engineering.snow.edu/CostCalculator",
        ["degree"] = "https://engineering.snow.edu/DegreeBuilder",
        ["blocks"] = "/#/blocks",
    };

    private static IResult Guarded(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (MatchException ex)
        {
            return ApiErrors.Error(ex.Status, ex.Code, ex.Message);
        }
    }
}

/// <summary>Moves the pick-up hill along four times a second (only when matches tick on their own).</summary>
public sealed class PickupHillLoop(PickupHill hill, IOptions<NetRtsOptions> options, TimeProvider time, ILogger<PickupHillLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoTick)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                hill.Update(time.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pick-up hill update failed");
            }
        }
    }
}
