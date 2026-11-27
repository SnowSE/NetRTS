using System.Net;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>On a classic server, a match can opt in to another rule set (the match setting rules), and says which it plays.</summary>
public class MatchRulesTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_server_lists_its_rule_sets_and_which_is_the_default()
    {
        var sets = await factory.CreateClient().GetFromJsonAsync<List<RuleSetInfoDto>>("/api/v1/rulesets", NetRtsClient.JsonOptions, Ct);

        Assert.Equal(["classic", "spring-draft"], sets!.Select(s => s.Name));
        Assert.Equal("classic", Assert.Single(sets!, s => s.IsDefault).Name);
    }

    [Fact]
    public async Task A_match_can_opt_in_to_the_spring_rules_and_bots_read_them_from_the_match()
    {
        var (client, _) = await factory.NewPlayerAsync();

        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"], Settings = new MatchSettingsDto { Rules = "spring-draft" } }, Ct);

        Assert.Equal("spring-draft", match.Rules);
        Assert.Same(GameRules.SpringDraft, factory.Matches.Get(match.MatchId)!.Rules);
        var rules = await client.GetMatchRulesAsync(match.MatchId, Ct);
        Assert.Equal(9, rules.CellCapacity);
        Assert.NotNull(rules.Farm);
        var classic = await factory.CreateClient().GetFromJsonAsync<RulesDto>("/api/v1/rules", NetRtsClient.JsonOptions, Ct);
        Assert.Equal(0, classic!.CellCapacity);
    }

    [Fact]
    public async Task A_match_that_says_nothing_plays_the_servers_rules()
    {
        var (client, _) = await factory.NewPlayerAsync();

        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        Assert.Equal("classic", match.Rules);
        Assert.Null((await client.GetMatchRulesAsync(match.MatchId, Ct)).Farm);
    }

    [Fact]
    public async Task An_unknown_rule_set_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/exhibitions",
            new CreateExhibitionRequest { Bots = ["rusher", "sitter"], Settings = new MatchSettingsDto { Rules = "chess" } }, NetRtsClient.JsonOptions, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_SETTINGS", await response.Content.ReadAsStringAsync(Ct));
    }
}
