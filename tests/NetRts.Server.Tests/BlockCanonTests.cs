using System.Text.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>
/// blocks/canon.js, run under node: how the pages compare a program they built with the server's copy of it
/// (account sync, the cockpit) and keep the account's copy. It used to drop every false, so a rule turned off
/// (or a "turn the switch off" action) came back from the account turned on, and the change never went up.
/// </summary>
public class BlockCanonTests
{
    // The page's own form: it leaves out what's at its default.
    private const string PageOff = """
        {"name":"S","switches":["Attack!"],"rules":[
          {"enabled":false,"when":{"kind":"always"},"do":[{"kind":"setSwitch","name":"Attack!","on":false}]}]}
        """;

    private const string PageOn = """
        {"name":"S","switches":["Attack!"],"rules":[
          {"when":{"kind":"always"},"do":[{"kind":"setSwitch","name":"Attack!","on":false}]}]}
        """;

    /// <summary>The same program as the server sends it: every field, "once": false, nulls and all.</summary>
    private static string Server(bool enabled) => JsonSerializer.Serialize(new BlockProgramDto
    {
        Name = "S",
        Switches = ["Attack!"],
        Rules =
        [
            new()
            {
                Enabled = enabled,
                When = new() { Kind = "always" },
                Do = [new() { Kind = "setSwitch", Name = "Attack!", On = false }],
            },
        ],
    }, NetRtsClient.JsonOptions);

    [Fact]
    public void A_rule_or_switch_turned_off_stays_off_and_counts_as_a_change()
    {
        var canon = new Uri(Repo.Path("src", "NetRts.Server", "wwwroot", "blocks", "canon.js")).AbsoluteUri;
        var script = $$"""
            import { canon, sameProgram } from '{{canon}}';
            const [pageOff, pageOn, serverOff, serverOn] = process.argv.slice(1).map((a) => JSON.parse(a));
            const kept = canon(serverOff);
            console.log(JSON.stringify({
              keptRuleOff: kept.rules[0].enabled,
              keptSwitchOff: kept.rules[0].do[0].on,
              offVsOn: sameProgram(pageOff, pageOn),
              pageVsServerOff: sameProgram(pageOff, serverOff),
              pageVsServerOn: sameProgram(pageOn, serverOn),
              serverOffVsOn: sameProgram(serverOff, serverOn),
            }));
            """;
        var (exit, output) = Repo.Run("node", "--input-type=module", "-e", script, PageOff, PageOn, Server(enabled: false), Server(enabled: true));
        Assert.True(exit == 0, output);

        var r = JsonDocument.Parse(output).RootElement;
        Assert.False(r.GetProperty("keptRuleOff").GetBoolean());       // kept from the account: still off
        Assert.False(r.GetProperty("keptSwitchOff").GetBoolean());     // "turn Attack! off" stays off
        Assert.False(r.GetProperty("offVsOn").GetBoolean());           // turning a rule off is a change to save
        Assert.False(r.GetProperty("serverOffVsOn").GetBoolean());
        Assert.True(r.GetProperty("pageVsServerOff").GetBoolean());    // the page's and the server's forms match
        Assert.True(r.GetProperty("pageVsServerOn").GetBoolean());
    }
}
