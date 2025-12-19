using System.Security.Claims;

namespace NetRts.Api.Middleware;

/// <summary>
/// Middleware to extract player ID from JWT claims and add it to HttpContext items.
/// </summary>
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract player ID from JWT claims if authenticated
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var playerIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirst("sub");

            if (playerIdClaim != null && Guid.TryParse(playerIdClaim.Value, out var playerId))
            {
                context.Items["PlayerId"] = playerId;
            }

            var usernameClaim = context.User.FindFirst(ClaimTypes.Name)
                ?? context.User.FindFirst("name");

            if (usernameClaim != null)
            {
                context.Items["Username"] = usernameClaim.Value;
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for AuthenticationMiddleware.
/// </summary>
public static class AuthenticationMiddlewareExtensions
{
    /// <summary>
    /// Adds the authentication middleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseAuthenticationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationMiddleware>();
    }

    /// <summary>
    /// Gets the current player ID from the HTTP context.
    /// </summary>
    public static Guid? GetCurrentPlayerId(this HttpContext context)
    {
        return context.Items["PlayerId"] as Guid?;
    }

    /// <summary>
    /// Gets the current username from the HTTP context.
    /// </summary>
    public static string? GetCurrentUsername(this HttpContext context)
    {
        return context.Items["Username"] as string;
    }
}
