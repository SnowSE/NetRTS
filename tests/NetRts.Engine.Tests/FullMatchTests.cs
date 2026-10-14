using System.Text.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>Whole matches between the house bots, driven exactly like the server drives them.</summary>
public class FullMatchTests
{
    /// <param name="surrenderAt">Slot 1 surrenders at this tick.</param>
    /// <param name="boostAt">Slot 0 takes its boost at this tick, and sends every command boosted.</param>
    internal static GameSimulation Play(string bot0, string bot1, int seed, int maxTicks = 1800, int? surrenderAt = null, int? boostAt = null)
    {
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks },
            [new PlayerSeat(Guid.NewGuid(), bot0), new PlayerSeat(Guid.NewGuid(), bot1)]);
        var bots = new[] { HouseBots.Create(bot0), HouseBots.Create(bot1) };
        var rules = GameRules.ToDto();
        var map = sim.Map.ToDto();

        while (sim.Status == MatchStatus.Active)
        {
            if (sim.Tick == boostAt)
            {
                sim.Boost(0);
            }

            for (var slot = 0; slot < 2; slot++)
            {
                var commands = bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, rules));
                if (commands.Count > 0)
                {
                    sim.Submit(slot, commands, boosted: slot == 0 && boostAt is not null);
                }
            }

            if (sim.Tick == surrenderAt)
            {
                sim.Surrender(1);
                break;
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

    [Fact]
    public void A_replay_survives_a_round_trip_through_json()
    {
        var original = Play("balanced", "economist", seed: 22);

        var json = JsonSerializer.Serialize(original.GetReplay(), NetRtsClient.JsonOptions);
        var replay = GameSimulation.FromReplay(JsonSerializer.Deserialize<ReplayDto>(json, NetRtsClient.JsonOptions)!);

        Assert.Equal(original.Tick, replay.Tick);
        Assert.Equal(original.StateHash(), replay.StateHash());
    }

    [Fact]
    public void Replaying_a_surrender_ends_the_match_on_the_same_tick()
    {
        var original = Play("rusher", "economist", seed: 23, surrenderAt: 150);

        var replay = GameSimulation.FromReplay(original.GetReplay());

        Assert.Equal(MatchEndReason.Surrender, replay.Outcome!.Reason);
        Assert.Equal(150, replay.Tick);
        Assert.Equal(original.StateHash(), replay.StateHash());
    }

    [Fact]
    public void Cleared_commands_are_left_out_of_the_replay()
    {
        var original = TestGames.New(maxTicks: 20);
        var worker = original.UnitIds(0, UnitType.Worker).First();
        original.SubmitOne(0, TestGames.Move(14, 14, worker)).AssertAccepted();
        original.SubmitOne(0, TestGames.Move(9, 15, worker)).AssertAccepted();
        Assert.Equal(2, original.ClearQueue(0));
        original.SubmitOne(0, TestGames.Move(15, 9, worker)).AssertAccepted();
        original.Run(20);

        var replay = GameSimulation.FromReplay(original.GetReplay());

        Assert.Single(original.GetReplay().Commands);
        Assert.Equal((15, 9), (replay.Find(worker)!.Value.X, replay.Find(worker)!.Value.Y));
        Assert.Equal(original.StateHash(), replay.StateHash());
    }
}
