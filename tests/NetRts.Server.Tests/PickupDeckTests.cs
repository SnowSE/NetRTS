using NetRts.Bots;
using NetRts.Bots.Pickup;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Matches;
using NetRts.Server.Pickup;

namespace NetRts.Server.Tests;

/// <summary>
/// The three decks are rock-paper-scissors under the pick-up rules (start kits, the treaty, the combat score,
/// 250 ms ticks): each beats one and loses to another, from either corner, so no deck wins every time.
/// </summary>
public class PickupDeckTests
{
    [Theory]
    [InlineData("rush", "boom")]
    [InlineData("boom", "turtle")]
    [InlineData("turtle", "rush")]
    public void Each_deck_beats_the_next_from_either_corner(string winner, string loser)
    {
        Assert.Equal(winner, Play(winner, loser));
        Assert.Equal(winner, Play(loser, winner));
    }

    private static string Play(string first, string second)
    {
        var decks = new[] { first, second }.Select(id => PickupPlans.Decks.Single(d => d.Id == id)).ToArray();
        var ids = decks.Select(_ => Guid.NewGuid()).ToArray();
        var sim = new GameSimulation(Guid.NewGuid(),
            new GameConfig { Seed = 1, MapWidth = 32, MapHeight = 32, MaxTicks = MatchManager.PickupMaxTicks, TreatyTicks = MatchManager.PickupTreatyTicks, CombatScore = true },
            decks.Select((d, i) => new PlayerSeat(ids[i], d.Id, PickupPlans.KitFor(d.Plan))).ToList());
        var bots = decks.Select(d => new PickupBot(d.Id, d.Plan, MatchManager.PickupTreatyTicks, MatchManager.PickupTickMs)).ToArray();
        var rules = GameRules.ToDto();
        var map = sim.Map.ToDto();
        while (sim.Status == MatchStatus.Active)
        {
            for (var s = 0; s < bots.Length; s++)
            {
                if (!sim.IsEliminated(s))
                {
                    sim.Submit(s, bots[s].Decide(new BotContext(sim.GetPlayerView(s), map, rules)));
                }
            }

            sim.Step();
        }

        return sim.Outcome!.WinnerId == ids[0] ? first : sim.Outcome.WinnerId == ids[1] ? second : "draw";
    }
}
