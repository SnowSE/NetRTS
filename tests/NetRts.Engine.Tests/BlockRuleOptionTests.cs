using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;
using static NetRts.Engine.Tests.BlockBotTests;

namespace NetRts.Engine.Tests;

/// <summary>Block Bot's switch action, once and every-N-seconds rules, enemy and attack checks, and attacking with a share.</summary>
public class BlockRuleOptionTests
{
    private static readonly BlockConditionDto Always = new() { Kind = "always" };
    private static readonly BlockActionDto TrainWorker = new() { Kind = "train", Unit = UnitType.Worker };

    private static (BlockBot Bot, GameSimulation Sim) Run(BlockProgramDto program, int ticks, int seed = 3, Action<BlockBot, int>? everyTick = null)
    {
        var sim = NewSim(seed);
        var bot = new BlockBot("blocks", program);
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= ticks, everyTick: t => everyTick?.Invoke(bot, t));
        return (bot, sim);
    }

    private static IReadOnlyList<BlockRuleStatusDto> Rules(BlockBot bot, GameSimulation sim) => bot.Status(Guid.Empty, sim.Status, 0).Rules;

    [Fact]
    public void A_rule_that_runs_once_stays_done()
    {
        var (bot, sim) = Run(new BlockProgramDto { Rules = [Rule(Always, TrainWorker) with { Once = true }] }, ticks: 30);
        var rule = Rules(bot, sim)[0];
        Assert.Equal("idle", rule.State);
        Assert.Contains("runs only once", rule.Why);
    }

    [Fact]
    public void A_rule_with_a_wait_runs_at_most_that_often()
    {
        var fired = new List<int>();
        Run(new BlockProgramDto { Rules = [Rule(Always, TrainWorker) with { EverySeconds = 10 }] }, ticks: 60, everyTick: (bot, t) =>
        {
            if (t > 0 && bot.Status(Guid.Empty, MatchStatus.Active, 0).Rules is [{ State: "firing" }])
            {
                fired.Add(t);
            }
        });
        Assert.True(fired.Count >= 3, $"fired at {string.Join(", ", fired)}");
        Assert.All(fired.Zip(fired.Skip(1)), p => Assert.True(p.Second - p.First >= 10, $"fired at {string.Join(", ", fired)}"));
    }

    [Fact]
    public void A_rule_can_turn_a_switch_on_for_the_rules_below_it()
    {
        var program = new BlockProgramDto
        {
            Switches = ["Go"],
            Rules =
            [
                Rule(Always, new BlockActionDto { Kind = "setSwitch", Name = "Go", On = true }),
                Rule(new() { Kind = "switch", Name = "Go" }, TrainWorker),
            ],
        };
        var (bot, sim) = Run(program, ticks: 3);
        var status = bot.Status(Guid.Empty, sim.Status, 0);
        Assert.True(status.Switches["Go"]);
        Assert.Contains("already on", status.Rules[0].Why);
        Assert.NotEqual("idle", status.Rules[1].State);
    }

    [Fact]
    public void Enemy_and_health_checks_say_what_they_see()
    {
        var program = new BlockProgramDto
        {
            Rules =
            [
                Rule(new() { Kind = "enemies", What = "fighters", Op = ">=", Value = 1 }, TrainWorker),
                Rule(new() { Kind = "hqHealth", Op = "<", Value = 50 }, TrainWorker),
                Rule(new() { Kind = "underAttack", What = "workers" }, TrainWorker),
            ],
        };
        var (bot, sim) = Run(program, ticks: 5);
        var rules = Rules(bot, sim);
        Assert.Contains("enemy fighters you can see is 0", rules[0].Why);
        Assert.Contains("health (%) is 100", rules[1].Why);
        Assert.Contains("workers aren't under attack", rules[2].Why);
    }

    [Fact]
    public void Something_that_loses_health_or_is_destroyed_is_under_attack_for_a_few_seconds()
    {
        var watch = new AttackWatch();
        UnitDto Unit(int id, UnitType type, int hp) => new() { Id = id, Owner = 0, Type = type, Hp = hp, MaxHp = 100 };
        BuildingDto Hq(int hp) => new() { Id = 1, Owner = 0, Type = BuildingType.CommandCenter, Hp = hp, MaxHp = 1000, Completed = true };
        void See(int tick, BuildingDto hq, params UnitDto[] units) => watch.Update(new BotView(new BotContext(
            new GameStateDto { Tick = tick, You = new SelfDto { Slot = 0 }, Units = units, Buildings = [hq] },
            new MapDto { Width = 10, Height = 10 },
            new RulesDto { Economy = new EconomyRulesDto() })));

        See(1, Hq(1000), Unit(7, UnitType.Worker, 100), Unit(8, UnitType.Soldier, 100));
        Assert.False(watch.Any("units", 1));

        See(2, Hq(1000), Unit(7, UnitType.Worker, 80), Unit(8, UnitType.Soldier, 100));
        Assert.True(watch.Any("workers", 2));
        Assert.True(watch.Any("units", 2));
        Assert.False(watch.Any("fighters", 2));
        Assert.False(watch.Any("buildings", 2));

        See(3, Hq(900), Unit(7, UnitType.Worker, 80));   // the soldier is gone, the HQ was hit
        Assert.True(watch.Any("Soldier", 3));
        Assert.True(watch.Any("CommandCenter", 3));
        Assert.True(watch.Any("buildings", 3));
        Assert.False(watch.Any("Barracks", 3));

        See(3 + AttackWatch.Window + 1, Hq(900), Unit(7, UnitType.Worker, 80));
        Assert.False(watch.Any("units", 3 + AttackWatch.Window + 1));
    }

    [Fact]
    public void Attack_with_a_share_sends_part_and_leaves_the_rest_for_rules_below()
    {
        var program = new BlockProgramDto
        {
            Rules =
            [
                Rule(Count("fighters", ">=", 4), new BlockActionDto { Kind = "attack", Who = "fighters", Percent = 50 }),
                Rule(Count("fighters", ">=", 4), new BlockActionDto { Kind = "retreat", Who = "fighters" }),
                Rule(Count("workers", "<", 8), TrainWorker),
                Rule(Count("Barracks", "<", 1), new BlockActionDto { Kind = "build", Building = BuildingType.Barracks }),
                Rule(Always, new BlockActionDto { Kind = "train", Unit = UnitType.Soldier }),
            ],
        };
        var sim = NewSim(4);
        var bot = new BlockBot("blocks", program);
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t > 0 && sim.GetPlayerView(0).Units.Count(u => u.Owner == 0 && u.Type == UnitType.Soldier) >= 5);
        var next = sim.Tick + 1;   // one more tick, so the statuses are from a tick with five soldiers
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= next);

        var soldiers = sim.GetPlayerView(0).Units.Count(u => u.Owner == 0 && u.Type == UnitType.Soldier);
        var rules = Rules(bot, sim);
        Assert.Equal("firing", rules[0].State);
        Assert.Equal((int)Math.Ceiling(soldiers / 2.0), rules[0].UnitIds.Count);
        Assert.Empty(rules[0].UnitIds.Intersect(rules[1].UnitIds));
    }

    [Theory]
    [InlineData("{\"kind\":\"enemies\",\"what\":\"dragons\",\"op\":\">=\",\"value\":1}", "can't count enemy 'dragons'")]
    [InlineData("{\"kind\":\"underAttack\",\"what\":\"moon\"}", "can't watch 'moon'")]
    [InlineData("{\"kind\":\"hqHealth\",\"op\":\"<\",\"value\":101}", "0 to 100 percent")]
    public void Bad_new_conditions_are_rejected(string when, string reason)
    {
        var condition = System.Text.Json.JsonSerializer.Deserialize<BlockConditionDto>(when, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Contains(reason, BlockProgramValidator.Validate(new BlockProgramDto { Rules = [Rule(condition)] }));
    }

    [Fact]
    public void Bad_new_actions_and_rule_options_are_rejected()
    {
        string? Check(BlockRuleDto rule) => BlockProgramValidator.Validate(new BlockProgramDto { Switches = ["Go"], Rules = [rule] });
        Assert.Contains("no switch called 'Stop'", Check(Rule(Always, new BlockActionDto { Kind = "setSwitch", Name = "Stop", On = true })));
        Assert.Contains("on or off", Check(Rule(Always, new BlockActionDto { Kind = "setSwitch", Name = "Go" })));
        Assert.Null(Check(Rule(Always, new BlockActionDto { Kind = "setSwitch", Name = "go", On = false })));
        Assert.Contains("1 to 100 percent", Check(Rule(Always, new BlockActionDto { Kind = "attack", Percent = 0 })));
        Assert.Contains("not both", Check(Rule(Always) with { Once = true, EverySeconds = 5 }));
        Assert.Contains("1 to 3600 seconds", Check(Rule(Always) with { EverySeconds = 0 }));
        Assert.Null(Check(Rule(Always) with { EverySeconds = 30 }));
    }
}
