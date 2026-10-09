using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>Aspire service defaults: OpenTelemetry, health checks, service discovery and HTTP resilience.</summary>
public static class Extensions
{
    /// <summary>Meter and activity source name used by the game server's own instrumentation.</summary>
    public const string NetRtsTelemetryName = "NetRts";

    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(NetRtsTelemetryName))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(NetRtsTelemetryName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsNoisy(context.Request.Path))
                .AddHttpClientInstrumentation());

        // Application Insights in Azure, the Aspire dashboard (OTLP) locally.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Requests every bot or spectator makes once per tick (state long-polls, command submissions,
    /// spectator polls and streams) plus health probes. They would drown out every other trace; the
    /// HTTP request metrics still count them.
    /// </summary>
    private static bool IsNoisy(PathString path)
    {
        if (path.StartsWithSegments("/health"))
        {
            return true;
        }

        var value = path.Value ?? "";
        return value.StartsWith("/api/v1/matches/", StringComparison.OrdinalIgnoreCase)
               && (value.EndsWith("/state", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith("/commands", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith("/spectate", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith("/spectate/stream", StringComparison.OrdinalIgnoreCase));
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
