using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Endpoints;

/// <summary>What the website knows about whoever is looking at it.</summary>
public sealed record AccountDto
{
    /// <summary>Whether this server has Discord sign-in configured at all.</summary>
    public bool DiscordEnabled { get; init; }

    /// <summary>Whether a Discord server is configured: then only its members are verified.</summary>
    public bool ServerRequired { get; init; }

    public bool SignedIn { get; init; }
    public AccountProfileDto? Account { get; init; }

    /// <summary>The bots this account has claimed.</summary>
    public IReadOnlyList<PlayerProfileDto> Bots { get; init; } = [];

    /// <summary>Every achievement any of their bots has earned, once each, with the bots that earned it.</summary>
    public IReadOnlyList<EarnedAchievementDto> Achievements { get; init; } = [];

    /// <summary>Admins only: Azure portal links to the server's telemetry (empty when not configured).</summary>
    public IReadOnlyList<TelemetryLinkDto> TelemetryLinks { get; init; } = [];
}

public sealed record AccountProfileDto
{
    public Guid AccountId { get; init; }
    public string DiscordId { get; init; } = "";
    public string Username { get; init; } = "";

    /// <summary>Server nickname, else Discord display name, else username.</summary>
    public string DisplayName { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public bool InServer { get; init; }

    /// <summary>Whether their bots get the blue check (and may play season and championship matches).</summary>
    public bool Verified { get; init; }

    /// <summary>Discord server administrators are game admins.</summary>
    public bool IsAdmin { get; init; }

    /// <summary>Their public profile's name (#/player/{slug}) when they're verified.</summary>
    public string? ProfileSlug { get; init; }
}

public sealed record ClaimBotRequest
{
    /// <summary>The bot's API key, proving you control it.</summary>
    public string ApiKey { get; init; } = "";
}

/// <summary>Sign-in with Discord, and the bots a signed-in person owns. Used by the website, not by bots.</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // IsPersistent: the sign-in cookie outlives the browser session (30 days, sliding), not just this window.
        app.MapGet("/auth/discord", (IOptions<DiscordOptions> discord, string? returnUrl) =>
                discord.Value.Enabled
                    ? Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl), IsPersistent = true }, [Accounts.DiscordScheme])
                    : ApiErrors.Error(404, "DISCORD_DISABLED", "Discord sign-in isn't configured on this server."))
            .ExcludeFromDescription();

        app.MapPost("/auth/signout", async (HttpContext context) =>
            {
                await context.SignOutAsync(Accounts.CookieScheme);
                return Results.NoContent();
            })
            .ExcludeFromDescription();

        var account = app.MapGroup("/api/v1/account").WithTags("Accounts (website)");

        account.MapGet("/", Get)
            .AllowAnonymous()
            .WithSummary("Who is signed in on the website, and the bots they have claimed.");

        var bots = account.MapGroup("/bots").RequireAuthorization(Accounts.SignedInPolicy);

        bots.MapPost("/", (RegisterPlayerRequest request, ClaimsPrincipal user, NetRtsDb db, CancellationToken ct) =>
                PlayerEndpoints.CreatePlayerAsync(db, request.Name, user.AccountId(), ct))
            .RequireRateLimiting(RateLimits.Registration)
            .Produces<RegisterPlayerResponse>(201)
            .ProducesErrors()
            .WithSummary("Register a new bot owned by you; its API key is shown once.");

        bots.MapPost("/claim", Claim)
            .Produces<PlayerProfileDto>()
            .ProducesErrors()
            .WithSummary("Claim a bot you registered earlier by proving you have its API key.");

        bots.MapPost("/{playerId:guid}/key", NewKey)
            .Produces<RegisterPlayerResponse>()
            .ProducesErrors()
            .WithSummary("Replace one of your bots' API keys. The old key stops working at once.");

        bots.MapDelete("/{playerId:guid}", Release)
            .ProducesErrors()
            .WithSummary("Give up ownership of one of your bots (it loses its blue check).");

        // Hosting: a named practice match the signed-in person runs for bots to join, like an admin's league match.
        var hosted = account.MapGroup("/matches").RequireAuthorization(Accounts.SignedInPolicy);

        hosted.MapPost("/", async (CreateMatchRequest request, ClaimsPrincipal user, MatchManager manager, NetRtsDb db, CancellationToken ct) =>
            {
                var host = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == user.AccountId(), ct);
                var match = manager.Host(host.Id, host.DisplayName, request);
                return Results.Created($"/api/v1/matches/{match.Id}", match.ToSummary());
            })
            .Produces<MatchSummaryDto>(201)
            .ProducesErrors()
            .WithSummary("Host a named practice match for bots to join (they find it by name). Your own bots can join too; start it when they're in.");

        hosted.MapGet("/", (ClaimsPrincipal user, MatchManager manager) =>
                manager.HostedBy(user.AccountId()).Select(m => m.ToSummary()).ToList())
            .Produces<List<MatchSummaryDto>>()
            .WithSummary("The matches you're hosting, newest first (finished ones until they're tidied away).");

        hosted.MapPost("/{matchId:guid}/start", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
                manager.StartHosted(matchId, user.AccountId()).ToSummary())
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Start a match you're hosting with the bots that have joined (at least two players, one a real bot).");

        hosted.MapDelete("/{matchId:guid}", (Guid matchId, ClaimsPrincipal user, MatchManager manager) =>
                manager.CancelHosted(matchId, user.AccountId()).ToSummary())
            .Produces<MatchSummaryDto>()
            .ProducesErrors()
            .WithSummary("Call off a match you're hosting that hasn't started.");
    }

    private static async Task<AccountDto> Get(
        HttpContext context, NetRtsDb db, IOptions<DiscordOptions> options, IOptions<NetRtsOptions> netRts, CancellationToken ct)
    {
        var discord = options.Value;
        var auth = await context.AuthenticateAsync(Accounts.CookieScheme);
        var record = auth.Succeeded
            ? await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == auth.Principal.AccountId(), ct)
            : null;
        if (record is null)
        {
            return new AccountDto { DiscordEnabled = discord.Enabled, ServerRequired = discord.HasServer };
        }

        return new AccountDto
        {
            DiscordEnabled = discord.Enabled,
            ServerRequired = discord.HasServer,
            SignedIn = true,
            Account = new AccountProfileDto
            {
                AccountId = record.Id,
                DiscordId = record.DiscordId,
                Username = record.Username,
                DisplayName = record.DisplayName,
                AvatarUrl = Accounts.AvatarUrl(record),
                InServer = record.InServer,
                Verified = Accounts.IsVerified(record, discord),
                IsAdmin = record.IsAdmin,
                ProfileSlug = (await ProfileSlugs.LoadAsync(db, discord, ct)).Of(record.Id),
            },
            Bots = await PlayerEndpoints.ProfilesAsync(db, discord, db.Players.Where(p => p.OwnerAccountId == record.Id), ct),
            Achievements = await AchievementEndpoints.EarnedAsync(db,
                db.BotAchievements.Where(a => db.Players.Any(p => p.Id == a.PlayerId && p.OwnerAccountId == record.Id)), listBots: true, ct),
            TelemetryLinks = record.IsAdmin ? Endpoints.TelemetryLinks.For(netRts.Value) : [],
        };
    }

    private static async Task<IResult> Claim(ClaimBotRequest request, ClaimsPrincipal user, NetRtsDb db, IOptions<DiscordOptions> discord, CancellationToken ct)
    {
        var hash = ApiKeys.Hash(request.ApiKey?.Trim() ?? "");
        var player = await db.Players.FirstOrDefaultAsync(p => p.ApiKeyHash == hash && !p.IsHouseBot, ct);
        if (player is null)
        {
            return ApiErrors.Error(404, "UNKNOWN_API_KEY", "No bot has that API key.");
        }

        var accountId = user.AccountId();
        if (player.OwnerAccountId is { } owner && owner != accountId)
        {
            return ApiErrors.Error(409, "ALREADY_CLAIMED", $"'{player.Name}' already belongs to someone else.");
        }

        player.OwnerAccountId = accountId;
        await db.SaveChangesAsync(ct);
        var profiles = await PlayerEndpoints.ProfilesAsync(db, discord.Value, db.Players.Where(p => p.Id == player.Id), ct);
        return Results.Ok(profiles[0]);
    }

    private static async Task<IResult> NewKey(Guid playerId, ClaimsPrincipal user, NetRtsDb db, ApiKeyCache cache, CancellationToken ct)
    {
        var player = await OwnedAsync(db, user, playerId, ct);
        if (player is null)
        {
            return ApiErrors.NotFound("Bot");
        }

        var apiKey = ApiKeys.Generate();
        player.ApiKeyHash = ApiKeys.Hash(apiKey);
        await db.SaveChangesAsync(ct);
        cache.Forget(player.Id);
        return Results.Ok(new RegisterPlayerResponse { PlayerId = player.Id, Name = player.Name, ApiKey = apiKey });
    }

    private static async Task<IResult> Release(Guid playerId, ClaimsPrincipal user, NetRtsDb db, CancellationToken ct)
    {
        var player = await OwnedAsync(db, user, playerId, ct);
        if (player is null)
        {
            return ApiErrors.NotFound("Bot");
        }

        player.OwnerAccountId = null;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<PlayerRecord?> OwnedAsync(NetRtsDb db, ClaimsPrincipal user, Guid playerId, CancellationToken ct)
    {
        var accountId = user.AccountId();
        return db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.OwnerAccountId == accountId, ct);
    }

    /// <summary>Only paths on this site, so the sign-in can't be used to bounce people elsewhere.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is ['/', not '/' and not '\\', ..] || returnUrl == "/" ? returnUrl : "/#/account";
}
