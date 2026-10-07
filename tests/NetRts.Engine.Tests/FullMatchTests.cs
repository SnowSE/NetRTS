using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>Whole matches between the house bots, driven exactly like the server drives them.</summary>
public class FullMatchTests
{
    internal static GameSimulation Play(string bot0, string bot1, int seed, int maxTicks = 1800)
    {
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks },
            [new PlayerSeat(Guid.NewGuid(), bot0), new PlayerSeat(Guid.NewGuid(), bot1)]);
        var bots = new[] { HouseBots.Create(bot0), HouseBots.Create(bot1) };
        var rules = GameRules.ToDto();
        var map = sim.Map.ToDto();

        while (sim.Status == MatchStatus.Active)
        {
            for (var slot = 0; slot < 2; slot++)
            {
                var commands = bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, rules));
                if (commands.Count > 0)
                {
                    sim.Submit(slot, commands);
                }
            }

            sim.Step();
        }

        return sim;
    }

    [Theory]
    [InlineData("rusher", "sitter", 1)]
    [InlineData("balanced", "sitter", 2)]
    [InlineData("economist", "sitter", 3)]
    public void Every_house_bot_beats_a_passive_opponent(string bot, string victim, int seed)
    {
        var sim = Play(bot, victim, seed);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        var result = sim.GetResult()!;
        var winner = Assert.Single(result.Players, p => p.Winner);
        Assert.Equal(bot, winner.Name);
        Assert.True(winner.UnitsProduced > 5, "the bot should have built an army");
    }

    [Theory]
    [InlineData("rusher", "balanced", 11)]
    [InlineData("rusher", "economist", 12)]
    [InlineData("balanced", "economist", 13)]
    public void House_bots_finish_matches_against_each_other_with_a_clear_result(string bot0, string bot1, int seed)
    {
        var sim = Play(bot0, bot1, seed);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        var result = sim.GetResult()!;
        Assert.All(result.Players, p => Assert.True(p.Score.Economy > 0));
        Assert.True(result.Players.Sum(p => p.UnitsKilled) > 0, "the bots should have fought");
    }

    [Fact]
    public void Replaying_a_match_reproduces_it_exactly()
    {
        var original = Play("rusher", "balanced", seed: 21);

        var replay = GameSimulation.FromReplay(original.GetReplay());

        Assert.Equal(original.Tick, replay.Tick);
        Assert.Equal(original.StateHash(), replay.StateHash());
        Assert.Equal(original.Outcome!.Reason, replay.Outcome!.Reason);
        Assert.Equal(original.Outcome.WinnerId, replay.Outcome.WinnerId);
    }
}
