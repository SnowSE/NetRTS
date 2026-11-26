using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>A server told to run the spring draft (NetRts:Rules) publishes those rules and plays new matches under them.</summary>
public class RuleSetServerTests(RuleSetServerTests.SpringServer factory) : IClassFixture<RuleSetServerTests.SpringServer>
{
    public sealed class SpringServer : ServerFactory
    {
        protected override string Rules => "spring-draft";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_rules_endpoint_publishes_the_servers_rule_set()
    {
        var rules = await factory.CreateClient().GetFromJsonAsync<RulesDto>("/api/v1/rules", NetRtsClient.JsonOptions, Ct);

        Assert.Equal(50, rules!.SpoilsPercent);
        Assert.Contains(rules.Upgrades, u => u.Type == UpgradeType.RapidFire);
        Assert.Equal(3, rules.Buildings.Single(b => b.Type == BuildingType.GuardTower).AttackCooldown);
    }

    [Fact]
    public async Task New_matches_are_played_under_the_servers_rule_set()
    {
        var (client, _) = await factory.NewPlayerAsync();

        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        Assert.Same(GameRules.SpringDraft, factory.Matches.Get(match.MatchId)!.Rules);
    }
}
