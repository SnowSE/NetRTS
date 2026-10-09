using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

public class ApiTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_returns_a_working_api_key()
    {
        var (client, player) = await factory.NewPlayerAsync();

        Assert.StartsWith("nrts_", player.ApiKey);
        var me = await client.GetMeAsync(Ct);
        Assert.Equal(player.PlayerId, me.PlayerId);
        Assert.Equal(1200, me.Rating);
    }

    [Theory]
    [InlineData("ab", "INVALID_NAME")]
    [InlineData("has space", "INVALID_NAME")]
    [InlineData("house-rusher", "INVALID_NAME")]
    public async Task Registration_rejects_bad_names(string name, string code)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/players", new RegisterPlayerRequest { Name = name }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.ErrorAsync()).Code);
    }

    [Fact]
    public async Task Registration_rejects_taken_names_case_insensitively()
    {
        var (_, player) = await factory.NewPlayerAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/players", new RegisterPlayerRequest { Name = player.Name.ToUpperInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NAME_TAKEN", (await response.ErrorAsync()).Code);
    }

    [Fact]
    public async Task Game_endpoints_require_a_valid_api_key()
    {
        var (owner, _) = await factory.NewPlayerAsync();
        var match = await owner.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        var anonymous = factory.CreateClient();

        var noKey = await anonymous.GetAsync($"/api/v1/matches/{match.MatchId}/state", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await noKey.ErrorAsync()).Code);

        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nrts_not-a-real-key");
        var badKey = await anonymous.GetAsync($"/api/v1/matches/{match.MatchId}/state", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, badKey.StatusCode);
    }

    [Fact]
    public async Task Players_cannot_see_or_command_matches_they_are_not_in()
    {
        var (owner, ownerInfo) = await factory.NewPlayerAsync();
        var (intruder, _) = await factory.NewPlayerAsync();
        var match = await owner.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        var ex = await Assert.ThrowsAsync<NetRtsApiException>(() => intruder.GetStateAsync(match.MatchId, ct: Ct));
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
        Assert.Equal("NOT_A_PARTICIPANT", ex.Code);

        // The old test-only impersonation header must not work.
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Test-Player-Id", ownerInfo.PlayerId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/api/v1/matches/{match.MatchId}/state", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_match_against_a_house_bot_starts_immediately_with_fog_applied()
    {
        var (client, player) = await factory.NewPlayerAsync();

        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["balanced"], Settings = new MatchSettingsDto { Seed = 5 } }, Ct);

        Assert.Equal(MatchStatus.Active, match.Status);
        Assert.Equal(2, match.Players.Count);
        Assert.True(match.Players[1].IsHouseBot);

        var map = await client.GetMapAsync(match.MatchId, Ct);
        Assert.Equal(64, map.Width);
        Assert.Equal(2, map.StartPositions.Count);

        var state = await client.GetStateAsync(match.MatchId, ct: Ct);
        Assert.Equal(player.PlayerId, state.You.PlayerId);
        Assert.Equal(0, state.Tick);
        Assert.All(state.Units, u => Assert.Equal(0, u.Owner));
        Assert.Equal(1000, state.TickIntervalMs);
    }

    [Fact]
    public async Task Commands_are_validated_on_submit_and_executed_on_the_next_tick()
    {
        var (client, _) = await factory.NewPlayerAsync();
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        var state = await client.GetStateAsync(match.MatchId, ct: Ct);
        var worker = state.Units.First(u => u.Type == UnitType.Worker);

        var response = await client.SubmitAsync(match.MatchId,
        [
            new CommandRequest { Type = CommandType.Move, UnitIds = [worker.Id], X = worker.X + 3, Y = worker.Y },
            new CommandRequest { Type = CommandType.Move, UnitIds = [worker.Id], X = 999, Y = 0 },
            new CommandRequest { Type = CommandType.Produce, BuildingId = 999_999, UnitType = UnitType.Worker },
        ], Ct);

        Assert.Equal(1, response.Accepted);
        Assert.Equal("OUT_OF_BOUNDS", response.Results[1].Error!.Code);
        Assert.Equal("BUILDING_NOT_FOUND", response.Results[2].Error!.Code);
        Assert.Equal(1, response.QueueSize);

        factory.Matches.Get(match.MatchId)!.Advance();

        var after = await client.GetStateAsync(match.MatchId, ct: Ct);
        Assert.Equal(1, after.Tick);
        Assert.Equal(worker.X + 1, after.Units.Single(u => u.Id == worker.Id).X);
    }

    [Fact]
    public async Task Malformed_json_gets_a_structured_error()
    {
        var (client, player) = await factory.NewPlayerAsync();
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player.ApiKey);

        var response = await http.PostAsync($"/api/v1/matches/{match.MatchId}/commands",
            new StringContent("""{"commands":[{"type":"Teleport"}]}""", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_JSON", (await response.ErrorAsync()).Code);
    }

    [Fact]
    public async Task Waiting_matches_start_when_the_last_seat_is_filled()
    {
        var (host, _) = await factory.NewPlayerAsync();
        var (guest, _) = await factory.NewPlayerAsync();
        var (latecomer, _) = await factory.NewPlayerAsync();

        var match = await host.CreateMatchAsync(new CreateMatchRequest(), Ct);
        Assert.Equal(MatchStatus.Waiting, match.Status);

        var notStarted = await Assert.ThrowsAsync<NetRtsApiException>(() => host.GetStateAsync(match.MatchId, ct: Ct));
        Assert.Equal("MATCH_NOT_STARTED", notStarted.Code);
        Assert.Contains(await host.ListMatchesAsync(MatchStatus.Waiting, Ct), m => m.MatchId == match.MatchId);

        var joined = await guest.JoinMatchAsync(match.MatchId, Ct);
        Assert.Equal(MatchStatus.Active, joined.Status);
        Assert.Equal(1, (await guest.GetStateAsync(match.MatchId, ct: Ct)).You.Slot);

        var full = await Assert.ThrowsAsync<NetRtsApiException>(() => latecomer.JoinMatchAsync(match.MatchId, Ct));
        Assert.Equal(HttpStatusCode.Conflict, full.Status);
    }

    [Fact]
    public async Task Long_polling_returns_as_soon_as_the_requested_tick_is_simulated()
    {
        var (client, _) = await factory.NewPlayerAsync();
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        var poll = client.GetStateAsync(match.MatchId, waitForTick: 1, ct: Ct);
        await Task.Delay(200, Ct);
        Assert.False(poll.IsCompleted);

        factory.Matches.Get(match.MatchId)!.Advance();

        var state = await poll.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(1, state.Tick);
    }

    [Fact]
    public async Task Surrender_ends_the_match_and_the_result_is_archived_and_rated()
    {
        var (winner, winnerInfo) = await factory.NewPlayerAsync();
        var (loser, loserInfo) = await factory.NewPlayerAsync();
        var match = await winner.CreateMatchAsync(new CreateMatchRequest(), Ct);
        await loser.JoinMatchAsync(match.MatchId, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(3);

        var afterSurrender = await loser.SurrenderAsync(match.MatchId, Ct);
        Assert.Equal(MatchStatus.Completed, afterSurrender.Status);
        Assert.Equal(winnerInfo.PlayerId, afterSurrender.Outcome!.WinnerId);

        var result = await winner.GetResultAsync(match.MatchId, Ct);
        Assert.Equal(MatchEndReason.Surrender, result.Outcome.Reason);
        Assert.Equal(winnerInfo.PlayerId, result.Outcome.WinnerId);

        var me = await WaitFor(() => winner.GetMeAsync(Ct), p => p.Wins == 1);
        Assert.True(me.Rating > 1200);
        Assert.Equal(1, (await loser.GetMeAsync(Ct)).Losses);

        // Once aged out of memory, everything is served from the archive.
        factory.Matches.Housekeep(DateTime.UtcNow.AddHours(2));
        Assert.Null(factory.Matches.Get(match.MatchId));
        Assert.Equal(winnerInfo.PlayerId, (await winner.GetResultAsync(match.MatchId, Ct)).Outcome.WinnerId);
        var replay = await factory.CreateClient().GetFromJsonAsync<ReplayDto>($"/api/v1/matches/{match.MatchId}/replay", NetRtsClient.JsonOptions, Ct);
        Assert.Equal(1, Assert.Single(replay!.Surrenders).Slot);
        Assert.Equal(64, (await winner.GetMapAsync(match.MatchId, Ct)).Width);

        var leaderboard = await factory.CreateClient().GetFromJsonAsync<List<LeaderboardEntryDto>>("/api/v1/leaderboard", NetRtsClient.JsonOptions, Ct);
        Assert.Contains(leaderboard!, e => e.PlayerId == winnerInfo.PlayerId && e.Wins == 1);
        Assert.Contains(leaderboard!, e => e.PlayerId == loserInfo.PlayerId && e.Losses == 1);

        var history = await factory.CreateClient().GetFromJsonAsync<List<MatchSummaryDto>>("/api/v1/matches/history", NetRtsClient.JsonOptions, Ct);
        Assert.Contains(history!, m => m.MatchId == match.MatchId);
    }

    [Fact]
    public async Task Matches_can_be_named_and_names_are_unique_until_the_match_ends()
    {
        var (host, _) = await factory.NewPlayerAsync();
        var (other, _) = await factory.NewPlayerAsync();
        var name = $"lab-{Guid.NewGuid():N}"[..12];

        var match = await host.CreateMatchAsync(new CreateMatchRequest { Name = name }, Ct);
        Assert.Equal(name, match.Name);
        Assert.Contains(await other.ListMatchesAsync(MatchStatus.Waiting, Ct), m => m.MatchId == match.MatchId && m.Name == name);

        var taken = await Assert.ThrowsAsync<NetRtsApiException>(() => other.CreateMatchAsync(new CreateMatchRequest { Name = name.ToUpperInvariant() }, Ct));
        Assert.Equal("MATCH_NAME_TAKEN", taken.Code);
        var invalid = await Assert.ThrowsAsync<NetRtsApiException>(() => other.CreateMatchAsync(new CreateMatchRequest { Name = "no spaces" }, Ct));
        Assert.Equal("INVALID_NAME", invalid.Code);

        await other.JoinMatchAsync(match.MatchId, Ct);
        await other.SurrenderAsync(match.MatchId, Ct);
        var again = await other.CreateMatchAsync(new CreateMatchRequest { Name = name, HouseBots = ["sitter"] }, Ct);
        Assert.Equal(name, again.Name);
    }

    [Fact]
    public async Task Exhibitions_can_be_spectated()
    {
        var http = factory.CreateClient();

        var created = await http.PostAsJsonAsync("/api/v1/exhibitions", new CreateExhibitionRequest { Bots = ["rusher", "economist"] }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var match = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        factory.Matches.Get(match.MatchId)!.Advance(5);

        var view = await http.GetFromJsonAsync<SpectatorStateDto>($"/api/v1/matches/{match.MatchId}/spectate?fog=true", NetRtsClient.JsonOptions, Ct);

        Assert.Equal(5, view!.Tick);
        Assert.Equal(2, view.Players.Count);
        Assert.Contains(view.Units, u => u.Owner == 0);
        Assert.Contains(view.Units, u => u.Owner == 1);
        Assert.Equal(64, view.Players[0].Visibility.Count);
    }

    [Fact]
    public async Task Spectator_stream_sends_a_snapshot_per_tick()
    {
        var (client, _) = await factory.NewPlayerAsync();
        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        var http = factory.CreateClient();

        using var response = await http.GetAsync($"/api/v1/matches/{match.MatchId}/spectate/stream", HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        var first = await reader.ReadLineAsync(Ct);
        Assert.StartsWith("data: ", first);
        factory.Matches.Get(match.MatchId)!.Advance();
        await reader.ReadLineAsync(Ct); // blank separator
        var second = await reader.ReadLineAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Contains("\"tick\":1", second);
    }

    [Fact]
    public async Task Invalid_match_settings_are_rejected()
    {
        var (client, _) = await factory.NewPlayerAsync();

        var tooSmall = await Assert.ThrowsAsync<NetRtsApiException>(() =>
            client.CreateMatchAsync(new CreateMatchRequest { Settings = new MatchSettingsDto { MapWidth = 8 } }, Ct));
        var unknownBot = await Assert.ThrowsAsync<NetRtsApiException>(() =>
            client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["skynet"] }, Ct));

        Assert.Equal("INVALID_SETTINGS", tooSmall.Code);
        Assert.Equal("UNKNOWN_HOUSE_BOT", unknownBot.Code);
    }

    [Fact]
    public async Task Big_matches_take_up_to_sixteen_players_and_all_four_house_bots()
    {
        var (host, _) = await factory.NewPlayerAsync();

        var match = await host.CreateMatchAsync(new CreateMatchRequest
        {
            MaxPlayers = 16,
            HouseBots = ["sitter", "rusher", "balanced", "economist"],
        }, Ct);

        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal(16, match.MaxPlayers);
        Assert.Equal(5, match.Players.Count);
        Assert.Equal(120, match.MapWidth);   // the default map grows to fit a 16-player ring

        var tooMany = await Assert.ThrowsAsync<NetRtsApiException>(() =>
            host.CreateMatchAsync(new CreateMatchRequest { MaxPlayers = 17 }, Ct));
        var mapTooSmall = await Assert.ThrowsAsync<NetRtsApiException>(() =>
            host.CreateMatchAsync(new CreateMatchRequest { MaxPlayers = 16, Settings = new MatchSettingsDto { MapWidth = 64, MapHeight = 64 } }, Ct));
        Assert.Equal("INVALID_SETTINGS", tooMany.Code);
        Assert.Equal("INVALID_SETTINGS", mapTooSmall.Code);
        Assert.Contains("16-player map", mapTooSmall.Message);
    }

    [Fact]
    public async Task A_house_bot_can_fill_several_seats_as_numbered_copies()
    {
        var (host, me) = await factory.NewPlayerAsync();

        var match = await host.CreateMatchAsync(new CreateMatchRequest
        {
            MaxPlayers = 16,
            HouseBots = [.. Enumerable.Repeat("rusher", 8), .. Enumerable.Repeat("sitter", 7)],
        }, Ct);

        Assert.Equal(MatchStatus.Active, match.Status);   // 1 player + 15 house bots fills every seat
        Assert.Equal(16, match.Players.Count);
        Assert.Equal(16, match.Players.Select(p => p.PlayerId).Distinct().Count());
        Assert.Equal(["house-rusher", "house-rusher 2", "house-rusher 3"], match.Players.Skip(1).Take(3).Select(p => p.Name));
        Assert.Equal("house-sitter 7", match.Players[^1].Name);
        Assert.Equal(me.PlayerId, match.Players[0].PlayerId);
    }

    [Fact]
    public async Task Exhibitions_take_all_four_house_bots()
    {
        var http = factory.CreateClient();

        var created = await http.PostAsJsonAsync("/api/v1/exhibitions",
            new CreateExhibitionRequest { Bots = ["sitter", "rusher", "balanced", "economist"] }, NetRtsClient.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var summary = await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct);
        Assert.Equal(4, summary!.Players.Count);
    }

    [Fact]
    public async Task Rules_bots_and_unknown_routes_are_served_as_json()
    {
        var http = factory.CreateClient();

        var rules = await http.GetFromJsonAsync<RulesDto>("/api/v1/rules", NetRtsClient.JsonOptions, Ct);
        Assert.Equal(4, rules!.Units.Count);
        Assert.Equal(5, rules.Buildings.Count);
        Assert.Equal(8, rules.Upgrades.Count);

        var bots = await http.GetFromJsonAsync<List<HouseBotDto>>("/api/v1/bots", NetRtsClient.JsonOptions, Ct);
        Assert.Contains(bots!, b => b.Name == "rusher");

        var missing = await http.GetAsync("/api/v1/nope", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("NOT_FOUND", (await missing.ErrorAsync()).Code);
    }

    private static async Task<T> WaitFor<T>(Func<Task<T>> fetch, Func<T, bool> done)
    {
        for (var i = 0; i < 50; i++)
        {
            var value = await fetch();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }

        return await fetch();
    }
}
