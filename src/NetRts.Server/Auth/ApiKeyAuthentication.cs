using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Server.Data;

namespace NetRts.Server.Auth;

public static class ApiKeys
{
    public const string Scheme = "ApiKey";

    /// <summary>
    /// The website's way to act as one of the signed-in person's own bots without its key (Block Bot's "Play as
    /// one of your bots"). A custom header, so another site's page can't send it without a CORS preflight.
    /// </summary>
    public const string PlayAsHeader = "X-Play-As";
    private const string Prefix = "nrts_";

    public static string Generate() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public static Guid PlayerId(this ClaimsPrincipal user) =>
        user.TryGetPlayerId(out var id) ? id : throw new InvalidOperationException("Not authenticated.");

    /// <summary>The calling bot's player id, if the request carries an API key.</summary>
    public static bool TryGetPlayerId(this ClaimsPrincipal user, out Guid playerId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out playerId);

    public static string PlayerName(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";
}

/// <summary>Remembers key-hash → player lookups so authenticated requests don't hit the database every time.</summary>
public sealed class ApiKeyCache
{
    private readonly ConcurrentDictionary<string, (Guid Id, string Name)> _byHash = new();

    public bool TryGet(string hash, out (Guid Id, string Name) player) => _byHash.TryGetValue(hash, out player);

    public void Set(string hash, Guid id, string name) => _byHash[hash] = (id, name);

    /// <summary>Forgets every cached key of a player, after their key is replaced.</summary>
    public void Forget(Guid playerId)
    {
        foreach (var (hash, player) in _byHash)
        {
            if (player.Id == playerId)
            {
                _byHash.TryRemove(hash, out _);
            }
        }
    }
}

/// <summary>
/// Accepts "Authorization: Bearer &lt;key&gt;" or "X-Api-Key: &lt;key&gt;"; or, with no key, "X-Play-As: &lt;player id&gt;"
/// from someone signed in to the website who owns that bot.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyCache cache,
    Presence presence,
    NetRtsDb db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? key = null;
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = authorization["Bearer ".Length..].Trim();
        }
        else if (Request.Headers.TryGetValue("X-Api-Key", out var header))
        {
            key = header.ToString().Trim();
        }

        if (string.IsNullOrEmpty(key))
        {
            return Request.Headers.TryGetValue(ApiKeys.PlayAsHeader, out var playAs)
                ? await PlayAsAsync(playAs.ToString().Trim())
                : AuthenticateResult.NoResult();
        }

        var hash = ApiKeys.Hash(key);
        if (!cache.TryGet(hash, out var player))
        {
            var record = await db.Players.AsNoTracking()
                .Where(p => p.ApiKeyHash == hash)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(Context.RequestAborted);
            if (record is null)
            {
                return AuthenticateResult.Fail("Unknown API key.");
            }

            player = (record.Id, record.Name);
            cache.Set(hash, record.Id, record.Name);
        }

        return Success(player.Id, player.Name);
    }

    /// <summary>The bot with this id, when the website account signed in on this request owns it.</summary>
    private async Task<AuthenticateResult> PlayAsAsync(string value)
    {
        if (!Guid.TryParse(value, out var playerId))
        {
            return AuthenticateResult.Fail($"{ApiKeys.PlayAsHeader} must be a bot's player id.");
        }

        var signedIn = await Context.AuthenticateAsync(Accounts.CookieScheme);
        if (signedIn.Principal?.TryGetAccountId(out var accountId) != true)
        {
            return AuthenticateResult.Fail("Sign in with Discord to play as one of your bots.");
        }

        var record = await db.Players.AsNoTracking()
            .Where(p => p.Id == playerId && p.OwnerAccountId == accountId && !p.IsHouseBot)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(Context.RequestAborted);
        return record is null
            ? AuthenticateResult.Fail("That bot doesn't belong to the account signed in.")
            : Success(record.Id, record.Name);
    }

    private AuthenticateResult Success(Guid playerId, string name)
    {
        presence.Touch(playerId);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, playerId.ToString()), new Claim(ClaimTypes.Name, name)],
            ApiKeys.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeys.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(new Protocol.ApiErrorDto
        {
            Code = "UNAUTHORIZED",
            Message = "Send your API key as 'Authorization: Bearer <key>'. Register with POST /api/v1/players to get one.",
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new Protocol.ApiErrorDto { Code = "FORBIDDEN", Message = "You are not allowed to do that." });
    }
}
