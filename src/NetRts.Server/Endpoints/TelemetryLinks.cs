using System.IO.Compression;
using System.Text;

namespace NetRts.Server.Endpoints;

/// <summary>A link from the admin section of the account page to an Azure portal page.</summary>
public sealed record TelemetryLinkDto(string Group, string Label, string Description, string Url);

/// <summary>
/// Azure portal links to the server's Application Insights resource: its built-in pages, and Logs
/// opened on saved queries over the last day.
/// </summary>
public static class TelemetryLinks
{
    public static IReadOnlyList<TelemetryLinkDto> For(NetRtsOptions options)
    {
        var resourceId = options.AppInsightsResourceId?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(resourceId))
        {
            return [];
        }

        if (!resourceId.StartsWith('/'))
        {
            resourceId = "/" + resourceId;
        }

        var portal = "https://portal.azure.com/#" +
            (string.IsNullOrWhiteSpace(options.AzureTenant) ? "" : "@" + options.AzureTenant.Trim());

        TelemetryLinkDto Page(string label, string description, string blade) =>
            new("Pages", label, description, $"{portal}/resource{resourceId}/{blade}");

        TelemetryLinkDto Query(string label, string description, string kql) =>
            new("Queries", label, description,
                $"{portal}/blade/Microsoft_Azure_Monitoring_Logs/LogsBlade/resourceId/{Uri.EscapeDataString(resourceId)}" +
                $"/source/LogsBlade.AnalyticsShareLinkToQuery/q/{Uri.EscapeDataString(Compress(kql))}/timespan/P1D");

        return
        [
            Page("Overview", "Requests, failures and response times at a glance.", "overview"),
            Page("Live metrics", "Requests, failures, CPU and memory as they happen.", "quickPulse"),
            Page("Failures", "Failed requests and exceptions, grouped, with sample stack traces.", "failures"),
            Page("Performance", "Slowest operations and their dependency calls.", "performance"),
            Page("Transaction search", "Find one request, trace or log line and open its end-to-end trace.", "searchV1"),
            Page("Application map", "The server, PostgreSQL and Discord, with call rates and failures.", "applicationMap"),
            Page("Metrics", "Charts of any metric, including the netrts.* game metrics.", "metrics"),
            Page("Logs", "Write your own KQL queries.", "logs"),

            Query("Exceptions", "Every exception, newest first.",
                """
                exceptions
                | project timestamp, type, outerMessage, operation_Name, operation_Id, details
                | order by timestamp desc
                """),
            Query("Warnings and errors", "Log messages at Warning or above.",
                """
                traces
                | where severityLevel >= 2
                | project timestamp, severityLevel, message, operation_Name, operation_Id, customDimensions
                | order by timestamp desc
                """),
            Query("All logs", "The latest 500 log messages.",
                """
                traces
                | project timestamp, severityLevel, message, operation_Name, customDimensions
                | order by timestamp desc
                | take 500
                """),
            Query("Failed requests", "Requests that returned an error status.",
                """
                requests
                | where success == false
                | project timestamp, name, resultCode, duration, client_IP, operation_Id
                | order by timestamp desc
                """),
            Query("Slowest endpoints", "Request counts and latency percentiles by endpoint.",
                """
                requests
                | summarize requests = count(), failed = countif(success == false),
                    p50_ms = percentile(duration, 50), p95_ms = percentile(duration, 95) by name
                | order by p95_ms desc
                """),
            Query("Game metrics", "Ticks, overruns, matches, house-bot and recorder failures.",
                """
                customMetrics
                | where name startswith "netrts."
                | summarize total = sum(valueSum), samples = sum(valueCount), peak = max(valueMax),
                    average = sum(valueSum) / sum(valueCount) by name
                | order by name asc
                """),
            Query("Tick health", "Tick duration and overruns in 5-minute buckets.",
                """
                customMetrics
                | where name in ("netrts.tick.duration", "netrts.tick.overruns")
                | summarize total = sum(valueSum), samples = sum(valueCount) by name, bin(timestamp, 5m)
                | extend value = iff(name == "netrts.tick.overruns", total, total / samples)
                | project timestamp, name, value
                | render timechart
                """),
        ];
    }

    /// <summary>The portal's share-a-query format: the query text gzipped, then base64.</summary>
    private static string Compress(string kql)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(kql.Replace("\r\n", "\n")));
        }

        return Convert.ToBase64String(buffer.ToArray());
    }
}
