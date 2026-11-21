using NetRts.Bots;
using NetRts.Bots.Formics;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>The Formics, the hardest house bot: whole matches, driven the way the server drives them.</summary>
public class FormicsBotTests
{
    private static readonly RulesDto Rules = GameRules.ToDto();

    private static GameSimulation Play(IReadOnlyList<IBotStrategy> bots, int seed, int size = 64, int grubPercent = 100, int maxTicks = 1800)
    {
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks, MapWidth = size, MapHeight = size, GrubPercent = grubPercent },
            [.. bots.Select((b, i) => new PlayerSeat(Guid.NewGuid(), $"{b.Name}-{i}"))]);
        var map = sim.Map.ToDto();
        while (sim.Status == MatchStatus.Active)
        {
            for (var slot = 0; slot < bots.Count; slot++)
            {
                var commands = bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, Rules));
                if (commands.Count > 0)
                {
                    sim.Submit(slot, commands.Take(sim.Config.MaxCommandsPerSubmit).ToList());
                }
            }

            sim.Step();
        }

        return sim;
    }

    private static int Winner(GameSimulation sim) => Assert.Single(sim.GetResult()!.Players, p => p.Winner).Slot;

    [Theory]
    [InlineData("sitter", 31)]
    [InlineData("poky", 32)]
    [InlineData("rusher", 33)]
    [InlineData("balanced", 34)]
    [InlineData("economist", 35)]
    public void The_formics_beat_every_other_house_bot_from_either_corner(string house, int seed)
    {
        Assert.Equal(0, Winner(Play([new FormicsBot(), HouseBots.Create(house)], seed)));
        Assert.Equal(1, Winner(Play([HouseBots.Create(house), new FormicsBot()], seed)));
    }

    [Fact]
    public void They_are_listed_as_a_house_bot()
    {
        Assert.Contains(HouseBots.All, b => b.Name == "formics");
        Assert.IsType<FormicsBot>(HouseBots.Create("formics"));
        Assert.DoesNotContain("formics", HouseBots.FullHouse);
        Assert.Equal("The Formics", SnowLore.BotNicknames["formics"]);
    }

    [Fact]
    public void Shared_out_focus_fire_beats_the_same_swarm_attack_moving()
    {
        // Same economy, same plan; only the fighting differs.
        var blunt = new FormicsTuning { FocusFire = false };
        Assert.Equal(0, Winner(Play([new FormicsBot(), new FormicsBot(blunt)], seed: 41)));
        Assert.Equal(1, Winner(Play([new FormicsBot(blunt), new FormicsBot()], seed: 42)));
    }

    [Fact]
    public void An_early_soldier_rush_is_held_and_answered()
    {
        var rush = new PlanBot("rush", new BotPlan
        {
            TargetWorkers = 8,
            BuildOrder = [new(BuildingType.Barracks, 2, MinWorkers: 5, SitePreference.TowardEnemy)],
            ArmyMix = [UnitType.Soldier],
            AttackThreshold = 5,
        });
        Assert.Equal(1, Winner(Play([rush, new FormicsBot()], seed: 43)));
    }

    [Theory]
    [InlineData(6, 72, 100)]
    [InlineData(3, 64, 25)]
    [InlineData(2, 48, 400)]
    public void Any_map_and_any_grubs_play_out_without_a_hitch(int players, int size, int grubs)
    {
        IBotStrategy[] bots = [new FormicsBot(), .. Enumerable.Range(1, players - 1).Select(i => HouseBots.Create(i % 2 == 0 ? "balanced" : "economist"))];
        var sim = Play(bots, seed: 44 + players, size, grubs, maxTicks: 900);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        var me = sim.GetResult()!.Players.Single(p => p.Slot == 0);
        Assert.True(me.Winner || me.Score.Total >= sim.GetResult()!.Players.Max(p => p.Score.Total), "the Formics should come out on top");
    }
}
