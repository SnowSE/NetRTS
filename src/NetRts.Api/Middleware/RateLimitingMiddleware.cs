using System.Collections.Concurrent;

namespace NetRts.Api.Middleware;

/// <summary>
/// Rate limiting middleware that limits requests per second per player.
/// Default limit is 10 requests/second for command endpoints.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestTimestamps = new();
    private readonly int _requestsPerSecond;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _requestsPerSecond = configuration.GetValue<int>("RateLimiting:RequestsPerSecond", 10);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply rate limiting to command endpoints
        if (!context.Request.Path.StartsWithSegments("/api/v1/matches") ||
            context.Request.Method != HttpMethod.Post.Method)
        {
            await _next(context);
            return;
        }

        var playerId = context.GetCurrentPlayerId();
        if (playerId == null)
        {
            await _next(context);
            return;
        }

        var key = playerId.ToString()!;
        var now = DateTime.UtcNow;

        var timestamps = _requestTimestamps.GetOrAdd(key, _ => new Queue<DateTime>());

        bool rateLimitExceeded;
        lock (timestamps)
        {
            // Remove timestamps older than 1 second
            while (timestamps.Count > 0 && (now - timestamps.Peek()).TotalSeconds > 1)
            {
                timestamps.Dequeue();
            }

            // Check if rate limit exceeded
            rateLimitExceeded = timestamps.Count >= _requestsPerSecond;

            if (!rateLimitExceeded)
            {
                timestamps.Enqueue(now);
            }
        }

        if (rateLimitExceeded)
        {
            _logger.LogWarning(
                "Rate limit exceeded for player {PlayerId}. Limit: {RequestsPerSecond} requests/second",
                playerId,
                _requestsPerSecond);

            context.Response.StatusCode = 429; // Too Many Requests
            context.Response.Headers.Append("Retry-After", "1");
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                message = $"Maximum {_requestsPerSecond} requests per second allowed",
                retryAfter = 1
            });
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for RateLimitingMiddleware.
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    /// <summary>
    /// Adds the rate limiting middleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseRateLimitingMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
