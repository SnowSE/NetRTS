using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>
/// The Poky Badger is the first opponent Block Bot's "Start now" plays: it has to fight back, and lose to
/// the starter strategy with time to spare for changing rules mid-match.
/// </summary>
public class PokyBotTests
{
    /// <summary>The locker's "My First Bot" starter (program.js starters().first).</summary>
    private static BlockProgramDto MyFirstBot(bool defend = false)
    {
        var rules = new List<BlockRuleDto>();
        if (defend)
        {
            rules.Add(BlockBotTests.Rule(new() { Kind = "enemyNearBase" }, new BlockActionDto { Kind = "defend", Who = "fighters" }));
        }

        rules.Add(BlockBotTests.Rule(BlockBotTests.Count("workers", "<", 10), new BlockActionDto { Kind = "train", Unit = UnitType.Worker }));
        rules.Add(BlockBotTests.Rule(BlockBotTests.Count("Barracks", "<", 1), new BlockActionDto { Kind = "build", Building = BuildingType.Barracks, Site = "TowardCenter" }));
        rules.Add(BlockBotTests.Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "train", Unit = UnitType.Soldier }));
        rules.Add(BlockBotTests.Rule(BlockBotTests.Count("fighters", ">=", 6), new BlockActionDto { Kind = "attack", Who = "fighters", Target = "enemyBase" }));
        return new() { Name = "My First Bot", Switches = [], Rules = rules };
    }

    private static GameSimulation Play(int seed, BlockProgramDto program)
    {
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = 1800 },
            [new PlayerSeat(Guid.NewGuid(), "blocks"), new PlayerSeat(Guid.NewGuid(), "poky")]);
        BlockBotTests.Play(sim, [new BlockBot("blocks", program), HouseBots.Create("poky")]);
        return sim;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public void The_starter_strategy_beats_it_after_a_few_minutes(int seed, bool defend)
    {
        var sim = Play(seed, MyFirstBot(defend));

        Assert.Equal("blocks", Assert.Single(sim.GetResult()!.Players, p => p.Winner).Name);
        Assert.InRange(sim.Tick, 180, 360);
    }

    [Fact]
    public void It_attacks_a_bot_that_does_nothing()
    {
        var sim = Play(2, new BlockProgramDto { Name = "Nothing", Switches = [], Rules = [] });

        Assert.Equal("poky", Assert.Single(sim.GetResult()!.Players, p => p.Winner).Name);
    }
}
