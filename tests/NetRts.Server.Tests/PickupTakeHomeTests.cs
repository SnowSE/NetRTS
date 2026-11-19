using System.Text.Json;
using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;
using NetRts.Server.Pickup;

namespace NetRts.Server.Tests;

/// <summary>
/// "Take your bot home": the match-over page turns a pick-up plan into a Block Bot program (pickup/convert.js) and
/// puts it in a link. Every deck's program, after the link's encoding and decoding, must pass Block Bot's check.
/// </summary>
public class PickupTakeHomeTests
{
    [Fact]
    public void Every_deck_converts_to_a_block_program_the_server_accepts()
    {
        var convert = new Uri(Repo.Path("src", "NetRts.Server", "wwwroot", "pickup", "convert.js")).AbsoluteUri;
        var script = $$"""
            import { toBlockProgram, encodeProgram, decodeProgram } from '{{convert}}';
            const plans = JSON.parse(process.argv[1]);
            console.log(JSON.stringify(plans.map((p) => decodeProgram(encodeProgram(toBlockProgram(p, 'Sneaky-Sagebrush'))))));
            """;
        var plans = JsonSerializer.Serialize(PickupPlans.Decks.Select(d => d.Plan), NetRtsClient.JsonOptions);

        var (exit, output) = Repo.Run("node", "--input-type=module", "-e", script, plans);

        Assert.True(exit == 0, output);
        var programs = JsonSerializer.Deserialize<List<BlockProgramDto>>(output, NetRtsClient.JsonOptions)!;
        Assert.Equal(PickupPlans.Decks.Count, programs.Count);
        Assert.All(programs, p =>
        {
            Assert.Null(BlockProgramValidator.Validate(p));
            Assert.NotEmpty(p.Rules);
        });
    }
}
