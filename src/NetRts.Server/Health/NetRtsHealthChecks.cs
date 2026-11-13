using Microsoft.Extensions.Diagnostics.HealthChecks;
using NetRts.Server.Data;
using NetRts.Server.Matches;

namespace NetRts.Server.Health;

/// <summary>
/// What <c>/health</c> checks beyond "the server answers" (<c>/health/live</c>): the database, that running
/// matches are still ticking, and that finished matches are being saved. Unhealthy (503) means players are
/// affected now: the uptime test alerts on it. Degraded still answers 200 and shows in the JSON.
/// </summary>
public static class NetRtsHealthChecks
{
    public static IServiceCollection AddNetRtsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(10))
            .AddCheck<MatchTicksHealthCheck>("matches")
            .AddCheck<MatchSavesHealthCheck>("saving-matches");
        return services;
    }
}

/// <summary>The database answers. Without it nobody can sign in, register or have a match saved.</summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        // No exception text: the endpoint is public, and connection errors can name hosts and users.
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Reachable.")
            : HealthCheckResult.Unhealthy("Can't connect to the database.");
    }
}

/// <summary>
/// Every running match has ticked recently. A match's tick loop that crashes only logs it, and leaves the
/// match frozen in memory (nothing clears an active match), so this stays unhealthy until it's dealt with.
/// </summary>
public sealed class MatchTicksHealthCheck(MatchManager matches, TimeProvider time) : IHealthCheck
{
    /// <summary>A match counts as stalled after ten missed ticks, and never sooner than this.</summary>
    public static readonly TimeSpan MinStall = TimeSpan.FromSeconds(30);

    public static TimeSpan StallAfter(int tickIntervalMs) => TimeSpan.FromMilliseconds(Math.Max(MinStall.TotalMilliseconds, 10.0 * tickIntervalMs));

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var running = 0;
        var stalled = new List<(Guid Id, TimeSpan Since)>();
        foreach (var host in matches.All)
        {
            if (host.SinceLastTick(now) is not { } since)
            {
                continue;
            }

            running++;
            if (since > StallAfter(host.TickIntervalMs))
            {
                stalled.Add((host.Id, since));
            }
        }

        return Task.FromResult(stalled.Count == 0
            ? HealthCheckResult.Healthy($"{running} running, all ticking.")
            : HealthCheckResult.Unhealthy($"{stalled.Count} of {running} running matches stopped ticking: "
                + string.Join(", ", stalled.Select(s => $"{s.Id} ({(int)s.Since.TotalSeconds} s)")) + "."));
    }
}

/// <summary>
/// Finished matches are being saved. Degraded, not unhealthy: play goes on, but results, ratings and
/// achievements wait (or are lost on a restart).
/// </summary>
public sealed class MatchSavesHealthCheck(MatchRecorder recorder, TimeProvider time) : IHealthCheck
{
    /// <summary>More finished matches than this waiting to be saved is a backlog.</summary>
    public const int MaxBacklog = 20;

    /// <summary>A failed save with none since counts for this long.</summary>
    public static readonly TimeSpan FailureCounts = TimeSpan.FromHours(1);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var backlog = recorder.Backlog;
        var (saved, failed) = recorder.LastSaves;
        if (failed is { } f && now - f < FailureCounts && (saved is null || saved < f))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Saving a finished match failed {(int)(now - f).TotalMinutes} min ago, and none has been saved since. {backlog} waiting."));
        }

        return Task.FromResult(backlog > MaxBacklog
            ? HealthCheckResult.Degraded($"{backlog} finished matches waiting to be saved.")
            : HealthCheckResult.Healthy($"{backlog} waiting to be saved."));
    }
}
