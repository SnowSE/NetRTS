using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Protocol;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Auth;

/// <summary>Discord sign-in settings, bound from the "Discord" configuration section.</summary>
public sealed class DiscordOptions
{
    public const string Section = "Discord";

    /// <summary>OAuth2 client id of the Discord application (discord.com/developers).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth2 client secret. Keep it in user secrets or app settings, never in source.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The Discord server (guild) id whose nicknames are shown and whose administrators are game admins.
    /// When set, only members of that server count as verified.
    /// </summary>
    public string? ServerId { get; set; }

    /// <summary>
    /// Incoming webhook of the channel where finished matches are announced (channel settings →
    /// Integrations → Webhooks). Leave unset on private servers: nothing is posted.
    /// </summary>
    public string? ResultsWebhookUrl { get; set; }

    /// <summary>
    /// A thread in the webhook's channel (right-click it → Copy Thread ID) that match results,
    /// achievements and recaps go to; unset, they go to the channel itself. New league matches always do.
    /// </summary>
    public string? ResultsThreadId { get; set; }

    /// <summary>How often practice games and preseason results are announced, as one roundup.</summary>
    public int ResultsDigestMinutes { get; set; } = 60;

    /// <summary>Pause between Discord messages: Discord allows about 30 webhook messages a minute.</summary>
    public int MessageGapMs { get; set; } = 2100;

    /// <summary>
    /// Discord user ids of people who play but don't compete (the instructor): their bots stay off every
    /// ladder and the trophy room, and never keep achievements.
    /// </summary>
    public List<string> UnrankedAccounts { get; set; } = [];

    public bool IsUnranked(AccountRecord? owner) => owner is not null && UnrankedAccounts.Contains(owner.DiscordId);

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public bool HasServer => !string.IsNullOrWhiteSpace(ServerId);
}

/// <summary>
/// People sign in to the website with Discord (a cookie); bots keep using API keys. An account can claim
/// bots, which then show a blue check and their owner's server nickname, and may join season and
/// championship matches. Administrators of the Discord server are game admins.
/// </summary>
public static class Accounts
{
    public const string CookieScheme = "Account";
    public const string DiscordScheme = DiscordAuthenticationDefaults.AuthenticationScheme;
    public const string SignedInPolicy = "SignedIn";
    public const string AdminPolicy = "Admin";

    private const string AccountIdClaim = "netrts:account";
    private const string AdminClaim = "netrts:admin";

    /// <summary>Discord's ADMINISTRATOR permission bit.</summary>
    private const ulong AdministratorPermission = 0x8;

    public static Guid AccountId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AccountIdClaim) ?? throw new InvalidOperationException("Not signed in."));

    public static void AddNetRtsAccounts(this IServiceCollection services, AuthenticationBuilder auth, DiscordOptions discord)
    {
        services.AddDataProtection().SetApplicationName("NetRts").PersistKeysToDbContext<NetRtsDb>();

        auth.AddCookie(CookieScheme, o =>
        {
            o.Cookie.Name = "netrts-account";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // This is an API: answer with status codes instead of redirecting to a login page.
            o.Events.OnRedirectToLogin = ctx => WriteError(ctx.Response, 401, "NOT_SIGNED_IN", "Sign in with Discord first.");
            o.Events.OnRedirectToAccessDenied = ctx => WriteError(ctx.Response, 403, "FORBIDDEN", "Only Discord server administrators can do that.");
            // Rebuild the claims from the database on every request, so a deleted account or lost admin
            // role takes effect without waiting for the cookie to expire.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var id = ctx.Principal?.FindFirstValue(AccountIdClaim);
                var db = ctx.HttpContext.RequestServices.GetRequiredService<NetRtsDb>();
                var account = Guid.TryParse(id, out var accountId)
                    ? await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ctx.HttpContext.RequestAborted)
                    : null;
                if (account is null)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieScheme);
                    return;
                }

                ctx.ReplacePrincipal(Principal(account));
            };
        });

        if (discord.Enabled)
        {
            auth.AddDiscord(DiscordScheme, o =>
            {
                o.ClientId = discord.ClientId!;
                o.ClientSecret = discord.ClientSecret!;
                o.SignInScheme = CookieScheme;
                o.CallbackPath = "/signin-discord";
                o.Scope.Add("identify");
                if (discord.HasServer)
                {
                    o.Scope.Add("guilds");               // permissions in each server, to spot administrators
                    o.Scope.Add("guilds.members.read");  // the server nickname
                }

                o.Events.OnCreatingTicket = OnDiscordSignInAsync;
                o.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/#/account?error=discord");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(SignedInPolicy, p => p.AddAuthenticationSchemes(CookieScheme).RequireClaim(AccountIdClaim))
            .AddPolicy(AdminPolicy, p => p.AddAuthenticationSchemes(CookieScheme).RequireClaim(AccountIdClaim).RequireClaim(AdminClaim, "true"));
    }

    /// <summary>A bot's blue check: claimed by an account that is verified (in the Discord server, when one is configured).</summary>
    public static bool IsVerified(AccountRecord? owner, DiscordOptions discord) =>
        owner is not null && (!discord.HasServer || owner.InServer);

    /// <summary>The seat a bot takes in a match, carrying its owner's badge for spectators.</summary>
    public static async Task<Seat> SeatForAsync(NetRtsDb db, DiscordOptions discord, ClaimsPrincipal bot, CancellationToken ct)
    {
        var playerId = bot.PlayerId();
        var owner = await db.Players.AsNoTracking().Where(p => p.Id == playerId).Select(p => p.Owner).FirstOrDefaultAsync(ct);
        var verified = IsVerified(owner, discord);
        return new Seat(playerId, bot.PlayerName(), IsHouseBot: false, verified, verified ? owner!.DisplayName : null, owner?.Id);
    }

    public static string? AvatarUrl(AccountRecord account) =>
        account.AvatarHash is { } hash ? $"https://cdn.discordapp.com/avatars/{account.DiscordId}/{hash}.png?size=64" : null;

    internal static ClaimsPrincipal Principal(AccountRecord account) =>
        new(new ClaimsIdentity(
            [
                new Claim(AccountIdClaim, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.DisplayName),
                new Claim(AdminClaim, account.IsAdmin ? "true" : "false"),
            ],
            CookieScheme));

    /// <summary>Records who signed in: their Discord profile, server nickname and whether they administer the server.</summary>
    private static async Task OnDiscordSignInAsync(OAuthCreatingTicketContext ctx)
    {
        var services = ctx.HttpContext.RequestServices;
        var discord = services.GetRequiredService<IOptions<DiscordOptions>>().Value;
        var db = services.GetRequiredService<NetRtsDb>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Accounts));
        var ct = ctx.HttpContext.RequestAborted;

        var user = ctx.User;
        var discordId = user.GetProperty("id").GetString()!;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.DiscordId == discordId, ct);
        if (account is null)
        {
            account = new AccountRecord { Id = Guid.NewGuid(), DiscordId = discordId, CreatedAt = DateTime.UtcNow };
            db.Accounts.Add(account);
        }

        account.Username = Truncate(user.GetProperty("username").GetString()) ?? discordId;
        account.GlobalName = Truncate(OptionalString(user, "global_name"));
        account.AvatarHash = Truncate(OptionalString(user, "avatar"));
        account.LastSignInAt = DateTime.UtcNow;

        if (discord.HasServer)
        {
            // Not a member: Discord answers 404. Any other failure keeps what we knew before.
            using var member = await DiscordGetAsync(ctx, $"users/@me/guilds/{discord.ServerId}/member", ct);
            if (member.StatusCode == HttpStatusCode.NotFound)
            {
                account.InServer = false;
                account.ServerNickname = null;
                account.IsAdmin = false;
            }
            else if (member.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await member.Content.ReadAsStringAsync(ct));
                account.InServer = true;
                account.ServerNickname = Truncate(OptionalString(json.RootElement, "nick"));
                account.IsAdmin = await IsServerAdminAsync(ctx, discord.ServerId!, logger, ct) ?? account.IsAdmin;
            }
            else
            {
                logger.LogWarning("Discord member lookup failed with {Status} for {DiscordId}", member.StatusCode, discordId);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Discord sign-in: {DiscordId} as {Name} (in server: {InServer}, admin: {Admin})",
            discordId, account.DisplayName, account.InServer, account.IsAdmin);
        ctx.Principal = Principal(account);
    }

    /// <summary>True if they own the server or hold the Administrator permission there; null if Discord didn't say.</summary>
    private static async Task<bool?> IsServerAdminAsync(OAuthCreatingTicketContext ctx, string serverId, ILogger logger, CancellationToken ct)
    {
        using var response = await DiscordGetAsync(ctx, "users/@me/guilds", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord server list failed with {Status}", response.StatusCode);
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var guild in json.RootElement.EnumerateArray())
        {
            if (guild.GetProperty("id").GetString() != serverId)
            {
                continue;
            }

            var owner = guild.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.True;
            var permissions = ulong.TryParse(OptionalString(guild, "permissions"), out var p) ? p : 0;
            return owner || (permissions & AdministratorPermission) != 0;
        }

        return false;
    }

    private static async Task<HttpResponseMessage> DiscordGetAsync(OAuthCreatingTicketContext ctx, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
        return await ctx.Backchannel.SendAsync(request, ct);
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Truncate(string? value) => value is { Length: > 64 } ? value[..64] : value;

    private static Task WriteError(HttpResponse response, int status, string code, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new ApiErrorDto { Code = code, Message = message });
    }
}
