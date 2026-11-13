using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Bots.Blocks;
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
    public async Task A_block_bot_seat_is_marked_and_its_one_on_ones_count_but_are_not_rated()
    {
        var blocks = await PlayerAsync();
        var start = await blocks.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.Create, Seats = 2, TickIntervalMs = 20 }, NetRtsClient.JsonOptions, Ct);
        var match = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;

        var coder = await PlayerAsync();
        var joined = await coder.PostAsync($"/api/v1/matches/{match.MatchId}/join", null, Ct);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var host = factory.Matches.Get(match.MatchId)!;
        await host.WaitForTickAsync(2, TimeSpan.FromSeconds(20), Ct);

        var summary = host.ToSummary();
        Assert.True(summary.Players.Single(p => p.PlayerId == match.Players[0].PlayerId).IsBlockBot);
        Assert.False(summary.Players.Single(p => p.PlayerId != match.Players[0].PlayerId).IsBlockBot);

        var surrender = await coder.PostAsync($"/api/v1/matches/{match.MatchId}/surrender", null, Ct);
        Assert.Equal(HttpStatusCode.OK, surrender.StatusCode);

        PlayerProfileDto me = null!;
        for (var i = 0; i < 50 && me?.Wins != 1; i++)
        {
            await Task.Delay(100, Ct);
            me = (await blocks.GetFromJsonAsync<PlayerProfileDto>("/api/v1/players/me", NetRtsClient.JsonOptions, Ct))!;
        }

        Assert.Equal(1, me.Wins);
        Assert.Equal(1200, me.Rating);
        Assert.Equal(1200, (await coder.GetFromJsonAsync<PlayerProfileDto>("/api/v1/players/me", NetRtsClient.JsonOptions, Ct))!.Rating);
    }

    [Fact]
    public async Task The_vocabulary_lists_what_the_validator_accepts()
    {
        var vocabulary = (await factory.CreateClient().GetFromJsonAsync<BlockVocabularyDto>("/api/v1/blocks/vocabulary", NetRtsClient.JsonOptions, Ct))!;

        Assert.Contains("trainMix", vocabulary.ActionKinds);
        Assert.Contains("grubsLeft", vocabulary.ConditionKinds);
        Assert.Contains("TowardEnemy", vocabulary.Sites);
        Assert.Contains("fighters", vocabulary.Who);
        Assert.DoesNotContain("Worker", vocabulary.Who);
        Assert.Contains("Barracks", vocabulary.Countable);
        Assert.Equal(40, vocabulary.Limits.MaxRules);
        Assert.Equal("enemyBase", vocabulary.Defaults.Target);
    }

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
    public async Task A_created_match_waits_for_people_and_its_creator_can_start_it()
    {
        var creator = await PlayerAsync();
        var name = $"blk-{Guid.NewGuid():N}"[..14];
        var created = await creator.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.Create, Seats = 4, Name = name, TickIntervalMs = 20 }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var match = (await created.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        Assert.Equal(MatchStatus.Waiting, match.Status);
        Assert.Equal(4, match.MaxPlayers);
        Assert.Single(match.Players);

        // Others find it by name and join with their own Block Bots.
        var found = await creator.GetFromJsonAsync<MatchSummaryDto>($"/api/v1/matches/named/{name}", NetRtsClient.JsonOptions, Ct);
        Assert.Equal(match.MatchId, found!.MatchId);
        var second = await PlayerAsync();
        var join = await second.PostAsJsonAsync($"/api/v1/blocks/matches/{match.MatchId}/join", new JoinBlockMatchRequest { Program = Program }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);

        // Only the creator can start it early.
        var notYours = await second.PostAsync($"/api/v1/blocks/matches/{match.MatchId}/start", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, notYours.StatusCode);
        var start = await creator.PostAsync($"/api/v1/blocks/matches/{match.MatchId}/start", null, Ct);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        await factory.Matches.Get(match.MatchId)!.WaitForTickAsync(3, TimeSpan.FromSeconds(20), Ct);

        var tooMany = await creator.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.Create, Seats = 5 }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task A_free_for_all_can_have_several_of_the_same_house_bot()
    {
        var http = await PlayerAsync();
        var start = await http.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = Program, Mode = BlockPlayMode.FreeForAll, Opponents = ["rusher", "rusher", "sitter"], TickIntervalMs = 50 }, NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var match = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;

        var house = match.Players.Where(p => p.IsHouseBot).ToList();
        var names = house.Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["house-rusher", "house-rusher 2", "house-sitter"], names);
        Assert.Equal(3, house.Select(p => p.PlayerId).Distinct().Count());
        Assert.Equal(4, match.Players.Count);
        await http.PostAsync($"/api/v1/matches/{match.MatchId}/surrender", null, Ct);
    }

    [Fact]
    public async Task A_free_for_all_fills_the_biggest_map_and_no_more()
    {
        Assert.Equal(NetRts.Engine.MapGenerator.MaxPlayers - 1, BlockProgramValidator.MaxOpponents);
        var vocabulary = await factory.CreateClient().GetFromJsonAsync<BlockVocabularyDto>("/api/v1/blocks/vocabulary", NetRtsClient.JsonOptions, Ct);
        Assert.Equal(BlockProgramValidator.MaxOpponents, vocabulary!.Limits.MaxOpponents);

        var http = await PlayerAsync();
        string[] bots = ["sitter", "rusher", "economist", "balanced", "poky"];
        StartBlockMatchRequest Ffa(int n) => new()
        {
            Program = Program, Mode = BlockPlayMode.FreeForAll, TickIntervalMs = 200,
            Opponents = [.. Enumerable.Range(0, n).Select(i => bots[i % bots.Length])],
        };

        var full = await http.PostAsJsonAsync("/api/v1/blocks/matches", Ffa(15), NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.Created, full.StatusCode);
        var match = (await full.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        Assert.Equal(16, match.Players.Count);
        Assert.Equal(16, match.Players.Select(p => p.PlayerId).Distinct().Count());
        await factory.Matches.Get(match.MatchId)!.WaitForTickAsync(2, TimeSpan.FromSeconds(20), Ct);
        await http.PostAsync($"/api/v1/matches/{match.MatchId}/surrender", null, Ct);

        var tooMany = await http.PostAsJsonAsync("/api/v1/blocks/matches", Ffa(16), NetRtsClient.JsonOptions, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
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
