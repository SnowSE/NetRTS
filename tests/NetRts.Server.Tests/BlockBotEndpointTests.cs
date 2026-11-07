using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>Block Bot matches over HTTP: the server plays the program; the page changes it and reads its status.</summary>
public class BlockBotEndpointTests(TickingServerFactory factory) : IClassFixture<TickingServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BlockProgramDto Program = new()
    {
        Name = "Tester",
        Switches = ["Attack!"],
        Rules =
        [
            new() { When = new() { Kind = "count", What = "workers", Op = "<", Value = 10 }, Do = [new() { Kind = "train", Unit = UnitType.Worker }] },
            new() { When = new() { Kind = "switch", Name = "Attack!" }, Do = [new() { Kind = "attack", Who = "fighters" }] },
        ],
    };

    private async Task<HttpClient> PlayerAsync()
    {
        var http = factory.CreateClient();
        var register = await http.PostAsJsonAsync("/api/v1/players", new RegisterPlayerRequest { Name = $"blk-{Guid.NewGuid():N}"[..16] }, Ct);
        var player = (await register.Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player.ApiKey);
        return http;
    }

    private static async Task<BlockBotStatusDto> StatusAsync(HttpClient http, Guid matchId) =>
        (await http.GetFromJsonAsync<BlockBotStatusDto>($"/api/v1/blocks/matches/{matchId}/status", NetRtsClient.JsonOptions, Ct))!;

    [Fact]
    public async Task The_server_plays_the_program_and_takes_new_versions_and_switches()
    {
        var http = await PlayerAsync();
        var start = await http.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.HouseBot, Opponents = ["sitter"], TickIntervalMs = 20 }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var match = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        var host = factory.Matches.Get(match.MatchId)!;
        await host.WaitForTickAsync(5, TimeSpan.FromSeconds(20), Ct);

        var status = await StatusAsync(http, match.MatchId);
        Assert.Equal(1, status.Version);
        // Training workers: firing, or waiting while the HQ's queue is full.
        Assert.Contains(status.Rules[0].State, new[] { "firing", "waiting" });
        Assert.Contains("Attack! is off", status.Rules[1].Why);

        var flip = await http.PutAsJsonAsync($"/api/v1/blocks/matches/{match.MatchId}/switches", new SetBlockSwitchRequest { Name = "Attack!", On = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, flip.StatusCode);
        var missing = await http.PutAsJsonAsync($"/api/v1/blocks/matches/{match.MatchId}/switches", new SetBlockSwitchRequest { Name = "Retreat", On = true }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var v2 = Program with { Name = "Tester v2", Rules = [.. Program.Rules, new() { Enabled = false }] };
        var apply = await http.PutAsJsonAsync($"/api/v1/blocks/matches/{match.MatchId}/program", v2, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var applied = (await apply.Content.ReadFromJsonAsync<BlockBotStatusDto>(NetRtsClient.JsonOptions, Ct))!;
        Assert.Equal(2, applied.Version);
        Assert.True(applied.Switches["Attack!"]);

        await host.WaitForTickAsync(applied.Tick + 3, TimeSpan.FromSeconds(20), Ct);
        status = await StatusAsync(http, match.MatchId);
        Assert.Equal(3, status.Rules.Count);
        Assert.Equal("off", status.Rules[2].State);
        Assert.Equal("Tester v2", status.ProgramName);

        var mine = await http.GetFromJsonAsync<List<BlockBotStatusDto>>("/api/v1/blocks/matches", NetRtsClient.JsonOptions, Ct);
        Assert.Contains(mine!, s => s.MatchId == match.MatchId);
    }

    [Fact]
    public async Task Bad_programs_and_other_players_are_turned_away()
    {
        var http = await PlayerAsync();
        var bad = Program with { Rules = [new() { When = new() { Kind = "switch", Name = "Nope" } }] };
        var rejected = await http.PostAsJsonAsync("/api/v1/blocks/matches", new StartBlockMatchRequest { Program = bad, Opponents = ["sitter"] }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("INVALID_PROGRAM", (await rejected.ErrorAsync()).Code);

        var noOpponent = await http.PostAsJsonAsync("/api/v1/blocks/matches", new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.FreeForAll, Opponents = ["sitter"] }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noOpponent.StatusCode);

        var start = await http.PostAsJsonAsync("/api/v1/blocks/matches", new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.Sandbox, TickIntervalMs = 50 }, NetRtsClient.JsonOptions, Ct);
        var match = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        var stranger = await PlayerAsync();
        var peek = await stranger.GetAsync($"/api/v1/blocks/matches/{match.MatchId}/status", Ct);
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);

        var check = await http.PostAsJsonAsync("/api/v1/blocks/validate", Program, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
    }
}
