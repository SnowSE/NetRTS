using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NetRts.Protocol;

namespace NetRts.Server;

public static class RateLimits
{
    public const string Registration = "registration";
    public const string Exhibition = "exhibition";

    public static IServiceCollection AddNetRtsRateLimits(this IServiceCollection services, NetRtsOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiErrorDto { Code = "RATE_LIMITED", Message = "Too many requests; slow down." }, ct);
            };

            // Each bot gets its own budget in each match it plays (so one key can run several matches),
            // plus an overall cap per key (or per IP for anonymous calls).
            static string Caller(HttpContext context) =>
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            TokenBucketRateLimiterOptions Bucket(int perSecond) => new()
            {
                TokenLimit = perSecond * 2,
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        $"{Caller(context)}|{context.Request.RouteValues["matchId"]}",
                        _ => Bucket(options.RequestsPerSecond))),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        Caller(context),
                        _ => Bucket(options.RequestsPerSecond * 4))));

            limiter.AddPolicy(Registration, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.RegistrationsPerMinutePerIp,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.AddPolicy(Exhibition, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.ExhibitionsPerMinutePerIp,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }
}
