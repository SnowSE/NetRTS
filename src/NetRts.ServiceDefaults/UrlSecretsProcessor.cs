using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Masks secrets carried in outgoing request paths before traces are exported. A Discord webhook's
/// token is part of its URL, and the HTTP client's trace (an Application Insights dependency) records
/// the full URL, so anyone who could read the telemetry could post to the channel.
/// </summary>
public sealed partial class UrlSecretsProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (data.GetTagItem("url.full") is string url && Redact(url) is var redacted && redacted != url)
        {
            data.SetTag("url.full", redacted);
            data.DisplayName = Redact(data.DisplayName);
        }
    }

    /// <summary>".../api/webhooks/123/abc?x" → ".../api/webhooks/123/***?x".</summary>
    public static string Redact(string url) => WebhookToken().Replace(url, "$1***");

    [GeneratedRegex(@"(/webhooks/\d+/)[^/?#\s]+")]
    private static partial Regex WebhookToken();
}
