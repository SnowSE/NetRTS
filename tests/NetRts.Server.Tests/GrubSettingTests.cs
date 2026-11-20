using System.Net;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>Scrimmages can ask for a map size and how many grubs it holds.</summary>
public class GrubSettingTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_exhibition_takes_a_map_size_and_a_grub_amount()
    {
        var http = factory.CreateClient();
        var created = await http.PostAsJsonAsync("/api/v1/exhibitions",
            new CreateExhibitionRequest { Bots = ["rusher", "sitter"], Settings = new MatchSettingsDto { MapWidth = 48, MapHeight = 48, GrubPercent = 200 } },
            NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var summary = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        Assert.Equal((48, 48, 200), (summary.MapWidth, summary.MapHeight, summary.GrubPercent));

        var usual = await http.PostAsJsonAsync("/api/v1/exhibitions", new CreateExhibitionRequest { Bots = ["rusher", "sitter"] }, NetRtsClient.JsonOptions, Ct);
        Assert.Null((await usual.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!.GrubPercent);
    }

    [Fact]
    public async Task Too_many_grubs_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/exhibitions",
            new CreateExhibitionRequest { Bots = ["rusher", "sitter"], Settings = new MatchSettingsDto { GrubPercent = 1000 } }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_SETTINGS", await response.Content.ReadAsStringAsync(Ct));
    }
}
