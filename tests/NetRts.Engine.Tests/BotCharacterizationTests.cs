using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetRts.Bots;
using NetRts.Bots.Blocks;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>
/// Pins down exactly what the bots do: plays fixed matches and hashes every command list (and, for Block Bot,
/// every rule's explanation) tick by tick. The house bots are live opponents, so a refactor of the bot code
/// must leave these hashes alone; a deliberate change in play updates them.
/// </summary>
public class BotCharacterizationTests
{
    private const int Ticks = 1200;

    private static readonly RulesDto Rules = GameRules.ToDto();

    [Theory]
    [InlineData("rusher", "economist", 1, "AFA39928DF0CCC57")]
    [InlineData("rusher", "balanced", 1, "5518A6E1A6B0CF11")]
    [InlineData("rusher", "sitter", 2, "74572C97AD4B49EF")]
    [InlineData("economist", "balanced", 2, "EC9FF2B0572C8C92")]
    [InlineData("economist", "sitter", 1, "8D6649584AC2F617")]
    [InlineData("balanced", "sitter", 2, "8E8FB53C5ABFB462")]
    [InlineData("balanced", "rusher", 3, "58E92A55DA4729E9")]
    [InlineData("sitter", "economist", 3, "38B785BE61937893")]
    public void House_bots_play_the_same_commands(string bot0, string bot1, int seed, string expected)
    {
        var hash = PlayAndHash(seed, [HouseBots.Create(bot0), HouseBots.Create(bot1)], blocks: null);
        Assert.Equal(expected, hash);
    }

    public static TheoryData<string, string, int, string> BlockPrograms => new()
    {
        { "rush", "sitter", 1, "234F60BC51C8CA60" },
        { "rush", "balanced", 2, "05D90A3130DD0C82" },
        { "kitchen", "sitter", 3, "C3F97956D087AE83" },
        { "kitchen", "rusher", 1, "D1CCF6E978F79824" },
        { "expander", "sitter", 7, "B1AD719F022BE1D2" },
    };

    [Theory]
    [MemberData(nameof(BlockPrograms))]
    public void Block_bot_plays_the_same_commands_and_says_the_same_things(string program, string house, int seed, string expected)
    {
        var bot = new BlockBot("blocks", program switch
        {
            "rush" => BlockBotTests.Rush(),
            "kitchen" => Kitchen(),
            _ => Expander(),
        });
        var hash = PlayAndHash(seed, [bot, HouseBots.Create(house)], blocks: bot);
        Assert.Equal(expected, hash);
    }

    private static string PlayAndHash(int seed, IBotStrategy[] bots, BlockBot? blocks)
    {
        var sim = new GameSimulation(new Guid(seed, 0, 0, new byte[8]), new GameConfig { Seed = seed, MaxTicks = Ticks },
            [new PlayerSeat(new Guid(seed, 1, 0, new byte[8]), "p0"), new PlayerSeat(new Guid(seed, 2, 0, new byte[8]), "p1")]);
        var map = sim.Map.ToDto();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) => hash.AppendData(Encoding.UTF8.GetBytes(s + "\n"));

        while (sim.Status == MatchStatus.Active)
        {
            for (var slot = 0; slot < bots.Length; slot++)
            {
                var commands = bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, Rules));
                Add($"{sim.Tick}/{slot}:{JsonSerializer.Serialize(commands, NetRtsClient.JsonOptions)}");
                if (commands.Count > 0)
                {
                    sim.Submit(slot, commands.Take(sim.Config.MaxCommandsPerSubmit).ToList());
                }
            }

            if (blocks is not null)
            {
                foreach (var words in new[] { BlockWords.Snow, BlockWords.Classic })
                {
                    var status = blocks.Status(Guid.Empty, sim.Status, 0, words);
                    Add(status.Error ?? "");
                    foreach (var r in status.Rules)
                    {
                        Add($"{r.Index}|{r.State}|{r.Why}|{string.Join(',', r.UnitIds)}|{r.Progress}");
                    }
                }
            }

            sim.Step();
        }

        Add($"end {sim.Tick} {sim.Status}");
        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    private static BlockConditionDto Always => new() { Kind = "always" };

    private static BlockConditionDto Count(string what, string op, int value) => new() { Kind = "count", What = what, Op = op, Value = value };

    private static BlockConditionDto Minute(string op, int value) => new() { Kind = "minute", Op = op, Value = value };

    private static BlockRuleDto Rule(BlockConditionDto when, params BlockActionDto[] actions) => new() { When = when, Do = actions };

    /// <summary>Something of every kind: tech, research, siege, scouting, retreating, expanding, attacking.</summary>
    private static BlockProgramDto Kitchen() => new()
    {
        Name = "Kitchen sink",
        Rules =
        [
            Rule(new() { Kind = "enemyNearBase" }, new BlockActionDto { Kind = "defend", Who = "Soldier" }, new BlockActionDto { Kind = "defend" }),
            Rule(Count("workers", "<", 14), new BlockActionDto { Kind = "train", Unit = UnitType.Worker }),
            Rule(Always,
                new BlockActionDto { Kind = "build", Building = BuildingType.SiegeWorkshop },
                new BlockActionDto { Kind = "build", Building = BuildingType.Barracks, Site = "TowardEnemy" }),
            Rule(Count("Barracks", ">=", 1), new BlockActionDto { Kind = "build", Building = BuildingType.TechLab, Site = "NearOre" }),
            Rule(Always,
                new BlockActionDto { Kind = "research", Upgrade = UpgradeType.Weapons2 },
                new BlockActionDto { Kind = "research", Upgrade = UpgradeType.SiegeEngineering },
                new BlockActionDto { Kind = "research", Upgrade = UpgradeType.Weapons1 }),
            Rule(Always, new BlockActionDto { Kind = "train", Unit = UnitType.Trebuchet }),
            Rule(Count("Scout", "<", 1), new BlockActionDto { Kind = "train", Unit = UnitType.Scout }),
            Rule(Minute("<", 4), new BlockActionDto { Kind = "scout", Target = "explore" }),
            Rule(Minute(">=", 4), new BlockActionDto { Kind = "scout", Target = "enemyBase" }),
            Rule(new() { Kind = "grubsLeft", Op = "<", Value = 4000 }, new BlockActionDto { Kind = "expand" }),
            Rule(new() { Kind = "and", A = Minute(">=", 9), B = new() { Kind = "seeEnemy" } },
                new BlockActionDto { Kind = "attack", Target = "nearestEnemy" }),
            Rule(Minute(">=", 12), new BlockActionDto { Kind = "attack", Who = "Archer" }),
            Rule(Count("fighters", ">", 0), new BlockActionDto { Kind = "retreat" }),
            Rule(new() { Kind = "not", A = Count("grubs", "<", 120) },
                new BlockActionDto { Kind = "trainMix", Units = [UnitType.Soldier, UnitType.Archer] },
                new BlockActionDto { Kind = "trainMix", Units = [UnitType.Archer, UnitType.Scout] }),
        ],
    };

    private static BlockProgramDto Expander() => new()
    {
        Name = "Expander",
        Rules =
        [
            Rule(Count("workers", "<", 16), new BlockActionDto { Kind = "train", Unit = UnitType.Worker }),
            Rule(Count("Barracks", "<", 1), new BlockActionDto { Kind = "build", Building = BuildingType.Barracks }),
            Rule(Count("Scout", "<", 1), new BlockActionDto { Kind = "train", Unit = UnitType.Scout }),
            Rule(Always, new BlockActionDto { Kind = "expand", Building = BuildingType.ResourceDepot }),
            Rule(Count("CommandCenter", "<", 2), new BlockActionDto { Kind = "expand", Building = BuildingType.CommandCenter }),
        ],
    };
}
