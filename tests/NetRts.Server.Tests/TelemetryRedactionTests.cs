using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace NetRts.Server.Tests;

/// <summary>Secrets in outgoing URLs never reach the exported traces.</summary>
public class TelemetryRedactionTests
{
    [Fact]
    public void A_discord_webhook_token_is_masked_in_the_url_and_name()
    {
        using var activity = new Activity("System.Net.Http.HttpRequestOut");
        activity.DisplayName = "POST /api/webhooks/1559213972799094834/s3cr3t-T0ken_x";
        activity.SetTag("url.full", "https://discord.com/api/webhooks/1559213972799094834/s3cr3t-T0ken_x?*");

        new UrlSecretsProcessor().OnEnd(activity);

        Assert.Equal("https://discord.com/api/webhooks/1559213972799094834/***?*", activity.GetTagItem("url.full"));
        Assert.Equal("POST /api/webhooks/1559213972799094834/***", activity.DisplayName);
    }

    [Fact]
    public void Other_urls_are_left_alone()
    {
        using var activity = new Activity("System.Net.Http.HttpRequestOut");
        activity.DisplayName = "GET";
        activity.SetTag("url.full", "https://discord.com/api/v10/users/@me/guilds");

        new UrlSecretsProcessor().OnEnd(activity);

        Assert.Equal("https://discord.com/api/v10/users/@me/guilds", activity.GetTagItem("url.full"));
        Assert.Equal("GET", activity.DisplayName);
    }
}
