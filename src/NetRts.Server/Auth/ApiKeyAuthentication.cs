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
    private const string Prefix = "nrts_";

    public static string Generate() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public static Guid PlayerId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not authenticated."));

    public static string PlayerName(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";
}

/// <summary>Remembers key-hash → player lookups so authenticated requests don't hit the database every time.</summary>
public sealed class ApiKeyCache
{
    private readonly ConcurrentDictionary<string, (Guid Id, string Name)> _byHash = new();

    public bool TryGet(string hash, out (Guid Id, string Name) player) => _byHash.TryGetValue(hash, out player);

    public void Set(string hash, Guid id, string name) => _byHash[hash] = (id, name);
}

/// <summary>Accepts "Authorization: Bearer &lt;key&gt;" or "X-Api-Key: &lt;key&gt;".</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyCache cache,
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
            return AuthenticateResult.NoResult();
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

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()), new Claim(ClaimTypes.Name, player.Name)],
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
