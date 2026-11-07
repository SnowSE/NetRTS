using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>Block Bot programs played the way the server plays them, against the house bots.</summary>
public class BlockBotTests
{
    private static int Dist(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private static BlockConditionDto Count(string what, string op, int value) => new() { Kind = "count", What = what, Op = op, Value = value };

    private static BlockRuleDto Rule(BlockConditionDto when, params BlockActionDto[] actions) => new() { When = when, Do = actions };

    /// <summary>The prototype's "Honey Rush": defend, workers, two Housing, a mixed army, attack at eight.</summary>
    internal static BlockProgramDto Rush(int attackAt = 8, bool withAttack = true) => new()
    {
        Name = "Honey Rush",
        Switches = ["Attack!"],
        Rules =
        [
            Rule(new() { Kind = "enemyNearBase" }, new BlockActionDto { Kind = "defend", Who = "fighters" }),
            Rule(Count("workers", "<", 12), new BlockActionDto { Kind = "train", Unit = UnitType.Worker }),
            Rule(Count("Barracks", "<", 2), new BlockActionDto { Kind = "build", Building = BuildingType.Barracks, Site = "TowardCenter" }),
            Rule(new() { Kind = "or", A = new() { Kind = "switch", Name = "Attack!" }, B = Count("fighters", ">=", attackAt) },
                new BlockActionDto { Kind = "attack", Who = "fighters", Target = "enemyBase" }) with { Enabled = withAttack },
            Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "trainMix", Units = [UnitType.Soldier, UnitType.Soldier, UnitType.Archer] }),
        ],
    };

    private static void Play(GameSimulation sim, IBotStrategy[] bots, Func<int, bool>? until = null, Action<int>? everyTick = null)
    {
        var rules = GameRules.ToDto();
        var map = sim.Map.ToDto();
        while (sim.Status == MatchStatus.Active && until?.Invoke(sim.Tick) != true)
        {
            everyTick?.Invoke(sim.Tick);
            for (var slot = 0; slot < bots.Length; slot++)
            {
                var commands = bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, rules));
                if (commands.Count > 0)
                {
                    sim.Submit(slot, commands.Take(sim.Config.MaxCommandsPerSubmit).ToList());
                }
            }

            sim.Step();
        }
    }

    private static GameSimulation NewSim(int seed, int maxTicks = 1800) =>
        new(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks },
            [new PlayerSeat(Guid.NewGuid(), "blocks"), new PlayerSeat(Guid.NewGuid(), "sitter")]);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_five_rule_program_beats_the_sitter(int seed)
    {
        Assert.Null(BlockProgramValidator.Validate(Rush()));
        var sim = NewSim(seed);
        Play(sim, [new BlockBot("blocks", Rush()), HouseBots.Create("sitter")]);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        var winner = Assert.Single(sim.GetResult()!.Players, p => p.Winner);
        Assert.Equal("blocks", winner.Name);
    }

    [Fact]
    public void Each_rule_says_why_it_is_or_isnt_doing_anything()
    {
        var sim = NewSim(3);
        var bot = new BlockBot("blocks", Rush());
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 2);

        var status = bot.Status(Guid.Empty, sim.Status, 0);
        Assert.Equal(5, status.Rules.Count);
        Assert.Equal("idle", status.Rules[0].State);
        Assert.Contains("no enemies near your base", status.Rules[0].Why);
        Assert.Equal("firing", status.Rules[1].State);
        Assert.Contains("training a worker", status.Rules[1].Why);
        Assert.Equal("idle", status.Rules[3].State);
        Assert.Contains("fighters is 0 (needs at least 8)", status.Rules[3].Why);
        Assert.Equal(0, status.Rules[3].Progress);
        Assert.Equal("waiting", status.Rules[4].State);
        Assert.Contains("Housing", status.Rules[4].Why);
    }

    [Fact]
    public void The_original_theme_explains_itself_in_its_own_words()
    {
        var sim = NewSim(3);
        var snow = new BlockBot("blocks", Rush());
        var classic = new BlockBot("blocks", Rush()) { Words = BlockWords.For("classic") };
        Play(sim, [snow, HouseBots.Create("sitter")], until: t => t >= 2);
        var view = new BotContext(sim.GetPlayerView(0), sim.Map.ToDto(), GameRules.ToDto());
        classic.Decide(view);

        Assert.Contains("Housing", snow.Status(Guid.Empty, sim.Status, 0).Rules[4].Why);
        var why = classic.Status(Guid.Empty, sim.Status, 0).Rules[4].Why;
        Assert.Contains("Barracks", why);
        Assert.DoesNotContain("Housing", why);
        Assert.Same(BlockWords.Snow, BlockWords.For(null));
        Assert.Equal("ore", BlockWords.Classic.What("grubs"));
    }

    [Fact]
    public void A_new_version_takes_over_mid_match_and_wins()
    {
        var sim = NewSim(4, maxTicks: 2400);
        var bot = new BlockBot("blocks", Rush(withAttack: false));
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 400);
        Assert.Equal(MatchStatus.Active, sim.Status);
        Assert.Equal("off", bot.Status(Guid.Empty, sim.Status, 0).Rules[3].State);

        var version = bot.Apply(Rush());
        Assert.Equal(2, version);
        Play(sim, [bot, HouseBots.Create("sitter")]);

        var status = bot.Status(Guid.Empty, sim.Status, 0);
        Assert.Equal(2, status.Version);
        Assert.Equal([1, 2], status.Versions.Select(v => v.Version));
        Assert.Equal(400, status.Versions[1].Tick);
        Assert.Equal("blocks", Assert.Single(sim.GetResult()!.Players, p => p.Winner).Name);
    }

    [Fact]
    public void A_switch_sends_the_army_in_before_the_count_is_reached()
    {
        var sim = NewSim(5);
        var bot = new BlockBot("blocks", Rush(attackAt: 1000));
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 500);
        var before = bot.Status(Guid.Empty, sim.Status, 0).Rules[3];
        Assert.Equal("idle", before.State);
        Assert.Contains("Attack! is off", before.Why);

        Assert.True(bot.SetSwitch("attack!", true));
        Assert.False(bot.SetSwitch("Retreat", true));
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 502);

        var after = bot.Status(Guid.Empty, sim.Status, 0).Rules[3];
        Assert.Equal("firing", after.State);
        Assert.NotEmpty(after.UnitIds);
        Assert.True(bot.Status(Guid.Empty, sim.Status, 0).Switches["Attack!"]);
    }

    [Fact]
    public void Rules_higher_up_get_the_fighters_first()
    {
        var program = new BlockProgramDto
        {
            Rules =
            [
                Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "retreat", Who = "fighters" }),
                Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "attack", Who = "fighters" }),
                Rule(Count("Barracks", "<", 1), new BlockActionDto { Kind = "build", Building = BuildingType.Barracks }),
                Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "train", Unit = UnitType.Soldier }),
            ],
        };
        var sim = NewSim(6);
        var bot = new BlockBot("blocks", program);
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 400);

        var status = bot.Status(Guid.Empty, sim.Status, 0);
        Assert.Equal("firing", status.Rules[0].State);
        Assert.Equal("waiting", status.Rules[1].State);
        Assert.Contains("no fighters to send", status.Rules[1].Why);
    }

    [Fact]
    public void Expand_finds_grubs_past_the_base_and_builds_a_coop_beside_them()
    {
        var sim = NewSim(7, maxTicks: 1500);
        var cc = sim.Find(sim.CommandCenterId(0))!.Value;
        sim.SpawnUnit(0, UnitType.Scout, cc.X + 1, cc.Y + 1);
        sim.SetResources(0, 1500);
        var program = new BlockProgramDto
        {
            Name = "Expander",
            Rules =
            [
                Rule(Count("workers", "<", 10), new BlockActionDto { Kind = "train", Unit = UnitType.Worker }),
                Rule(new() { Kind = "grubsLeft", Op = "<=", Value = 100_000 }, new BlockActionDto { Kind = "expand", Building = BuildingType.ResourceDepot }),
            ],
        };
        var bot = new BlockBot("blocks", program);
        var explored = false;
        BuildingDto? Outpost() => sim.GetPlayerView(0).Buildings.FirstOrDefault(b =>
            b.Owner == 0 && b.Type == BuildingType.ResourceDepot && Dist(b.X, b.Y, cc.X, cc.Y) > 8);
        Play(sim, [bot, HouseBots.Create("sitter")], until: _ => Outpost() is { Completed: true }, everyTick: _ =>
            explored |= bot.Status(Guid.Empty, sim.Status, 0).Rules.ElementAtOrDefault(1)?.Why.Contains("exploring") == true);

        Assert.True(explored, "with no grubs known past the base, expand sends the scout exploring first");
        var outpost = Outpost();
        Assert.NotNull(outpost);
        Assert.Contains(sim.Map.Deposits, d => Dist(d.Position.X, d.Position.Y, outpost.X, outpost.Y) <= 3);
    }

    [Fact]
    public void Grubs_left_counts_the_piles_near_the_base()
    {
        var sim = NewSim(8);
        var bot = new BlockBot("blocks", new BlockProgramDto
        {
            Rules = [Rule(new() { Kind = "grubsLeft", Op = "<=", Value = 0 }, new BlockActionDto { Kind = "expand" })],
        });
        Play(sim, [bot, HouseBots.Create("sitter")], until: t => t >= 2);

        var why = bot.Status(Guid.Empty, sim.Status, 0).Rules[0].Why;
        Assert.Matches(@"grubs left near your base is [1-9]\d* \(needs at most 0\)", why);
    }

    [Theory]
    [InlineData("{\"kind\":\"expand\",\"building\":\"Barracks\"}", "expand builds a ResourceDepot or a CommandCenter")]
    [InlineData("{\"kind\":\"scout\",\"target\":\"moon\"}", "unknown place to scout")]
    public void Bad_actions_are_rejected_with_a_reason(string action, string reason)
    {
        var a = System.Text.Json.JsonSerializer.Deserialize<BlockActionDto>(action, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Contains(reason, BlockProgramValidator.Validate(new BlockProgramDto { Rules = [Rule(new() { Kind = "always" }, a)] }));
    }

    [Theory]
    [InlineData("{\"kind\":\"dance\"}", "unknown condition")]
    [InlineData("{\"kind\":\"switch\",\"name\":\"Nope\"}", "no switch called 'Nope'")]
    [InlineData("{\"kind\":\"count\",\"what\":\"dragons\",\"op\":\">=\",\"value\":1}", "can't count 'dragons'")]
    [InlineData("{\"kind\":\"count\",\"what\":\"grubs\",\"op\":\"~\",\"value\":1}", "unknown comparison")]
    public void Bad_conditions_are_rejected_with_a_reason(string when, string reason)
    {
        var condition = System.Text.Json.JsonSerializer.Deserialize<BlockConditionDto>(when, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var error = BlockProgramValidator.Validate(new BlockProgramDto { Rules = [Rule(condition)] });
        Assert.NotNull(error);
        Assert.Contains(reason, error);
        Assert.StartsWith("Rule 1:", error);
    }

    [Fact]
    public void Programs_have_size_limits()
    {
        var tooMany = new BlockProgramDto { Rules = Enumerable.Range(0, BlockProgramValidator.MaxRules + 1).Select(_ => Rule(new() { Kind = "always" })).ToList() };
        Assert.NotNull(BlockProgramValidator.Validate(tooMany));

        BlockConditionDto deep = new() { Kind = "always" };
        for (var i = 0; i < BlockProgramValidator.MaxDepth + 1; i++)
        {
            deep = new() { Kind = "not", A = deep };
        }

        Assert.Contains("nest", BlockProgramValidator.Validate(new BlockProgramDto { Rules = [Rule(deep)] }));
        Assert.NotNull(BlockProgramValidator.Validate(new BlockProgramDto { Rules = [Rule(new() { Kind = "always" }, new BlockActionDto { Kind = "attack", Who = "Worker" })] }));
    }
}
