using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Tests;

/// <summary>Finished matches can be played back tick by tick from their recorded frames.</summary>
public class ReplayFramesTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Frames_cover_every_tick_in_chunks_and_end_with_the_result()
    {
        var (matchId, result) = await PlayAsync(ticks: 230);
        var http = factory.CreateClient();

        var manifest = await ManifestAsync(http, matchId);
        Assert.Equal(100, manifest.ChunkTicks);
        Assert.Equal(result.Outcome.Ticks, manifest.LastTick);
        Assert.Equal(manifest.LastTick / 100 + 1, manifest.ChunkCount);
        Assert.False(manifest.Rebuilt);
        Assert.Equal(GameRules.Version, manifest.RulesVersion);
        Assert.Contains(manifest.Events, e => e.Message.Contains("surrendered", StringComparison.OrdinalIgnoreCase));

        var frames = await FramesAsync(http, matchId, manifest);
        Assert.Equal(Enumerable.Range(0, manifest.LastTick + 1), frames.Select(f => f.Tick));
        Assert.All(frames[..^1], f => Assert.Equal(MatchStatus.Active, f.Status));
        Assert.Equal(MatchStatus.Completed, frames[^1].Status);
        Assert.Equal(result.Outcome, frames[^1].Outcome);
        Assert.Equal(result.Players.Select(p => p.Name).Order(), frames[0].Players.Select(p => p.Name).Order());

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/v1/matches/{matchId}/frames/{manifest.ChunkCount}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Browsers_get_the_stored_Brotli_bytes_and_may_cache_them_for_good()
    {
        var (matchId, _) = await PlayAsync(ticks: 20);
        var http = factory.CreateClient();
        var plain = await (await http.GetAsync($"/api/v1/matches/{matchId}/frames/0", Ct)).Content.ReadAsByteArrayAsync(Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/matches/{matchId}/frames/0");
        request.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
        await using var brotli = new BrotliStream(await response.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        await brotli.CopyToAsync(decoded, Ct);
        Assert.Equal(plain, decoded.ToArray());
    }

    [Fact]
    public async Task Frames_are_saved_with_the_match_and_served_once_it_leaves_memory()
    {
        var (matchId, _) = await PlayAsync(ticks: 150);
        var http = factory.CreateClient();
        var live = await FramesAsync(http, matchId, await ManifestAsync(http, matchId));

        await DropFromMemoryAsync(matchId);
        var manifest = await ManifestAsync(http, matchId);
        Assert.False(manifest.Rebuilt);
        Assert.Equal(Json(live), Json(await FramesAsync(http, matchId, manifest)));
    }

    [Fact]
    public async Task Matches_recorded_before_frames_existed_are_rebuilt_exactly_as_they_were_played()
    {
        var (matchId, _) = await PlayAsync(ticks: 180, houseBot: "rusher");
        var http = factory.CreateClient();
        var live = await FramesAsync(http, matchId, await ManifestAsync(http, matchId));
        await DropFromMemoryAsync(matchId);
        await WithDbAsync(async db =>
        {
            await db.MatchFrameChunks.Where(c => c.MatchId == matchId).ExecuteDeleteAsync(Ct);
            await db.MatchFrames.Where(f => f.MatchId == matchId).ExecuteDeleteAsync(Ct);
            await db.Matches.Where(m => m.Id == matchId).ExecuteUpdateAsync(m => m.SetProperty(x => x.RulesVersion, (string?)null), Ct);
        });

        var manifest = await ManifestAsync(http, matchId);
        Assert.True(manifest.Rebuilt);
        Assert.Equal(Json(live), Json(await FramesAsync(http, matchId, manifest)));
    }

    [Fact]
    public async Task A_match_played_under_other_rules_without_frames_is_not_rebuilt()
    {
        var (matchId, _) = await PlayAsync(ticks: 30);
        await DropFromMemoryAsync(matchId);
        await WithDbAsync(async db =>
        {
            await db.MatchFrameChunks.Where(c => c.MatchId == matchId).ExecuteDeleteAsync(Ct);
            await db.MatchFrames.Where(f => f.MatchId == matchId).ExecuteDeleteAsync(Ct);
            await db.Matches.Where(m => m.Id == matchId).ExecuteUpdateAsync(m => m.SetProperty(x => x.RulesVersion, "0-older"), Ct);
        });

        var response = await factory.CreateClient().GetAsync($"/api/v1/matches/{matchId}/frames", Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("FRAMES_UNAVAILABLE", (await response.ErrorAsync()).Code);
    }

    [Fact]
    public async Task There_are_no_frames_until_the_match_ends()
    {
        var (client, _) = await factory.NewPlayerAsync("live");
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(3);

        var response = await factory.CreateClient().GetAsync($"/api/v1/matches/{match.MatchId}/frames", Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("MATCH_NOT_COMPLETED", (await response.ErrorAsync()).Code);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/v1/matches/{Guid.NewGuid()}/frames", Ct)).StatusCode);
    }

    /// <summary>An idle bot faces a house bot for a while, then surrenders (the rusher attacks by tick ~150 and wins by ~215).</summary>
    private async Task<(Guid MatchId, MatchResultDto Result)> PlayAsync(int ticks, string houseBot = "sitter")
    {
        var (client, _) = await factory.NewPlayerAsync("rec");
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = [houseBot] }, Ct);
        var host = factory.Matches.Get(match.MatchId)!;
        host.Advance(ticks);
        await client.SurrenderAsync(match.MatchId, Ct);
        return (match.MatchId, host.GetResult()!);
    }

    private async Task DropFromMemoryAsync(Guid matchId)
    {
        var host = factory.Matches.Get(matchId)!;
        for (var i = 0; i < 100 && !host.Recorded; i++)
        {
            await Task.Delay(50, Ct);
        }

        Assert.True(host.Recorded);
        factory.Matches.Housekeep(DateTime.UtcNow.AddDays(1));
        Assert.Null(factory.Matches.Get(matchId));
    }

    private async Task WithDbAsync(Func<NetRtsDb, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<NetRtsDb>());
    }

    private static async Task<ReplayFramesDto> ManifestAsync(HttpClient http, Guid matchId)
    {
        var response = await http.GetAsync($"/api/v1/matches/{matchId}/frames", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReplayFramesDto>(NetRtsClient.JsonOptions, Ct))!;
    }

    private static async Task<List<SpectatorStateDto>> FramesAsync(HttpClient http, Guid matchId, ReplayFramesDto manifest)
    {
        var frames = new List<SpectatorStateDto>();
        for (var i = 0; i < manifest.ChunkCount; i++)
        {
            var text = await http.GetStringAsync($"/api/v1/matches/{matchId}/frames/{i}", Ct);
            frames.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<SpectatorStateDto>(line, NetRtsClient.JsonOptions)!));
        }

        return frames;
    }

    private static string Json(List<SpectatorStateDto> frames) => JsonSerializer.Serialize(frames, NetRtsClient.JsonOptions);
}
