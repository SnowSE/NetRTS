using NetRts.Bots;
using NetRts.Bots.Pickup;
using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class PickupBotTests
{
    private const int Treaty = 300;

    private static readonly PickupPlanDto Rush = new()
    {
        Start = [new("Soldier", 10), new("Archer", 4)],
        Steps = [new() { Kind = "workers", Count = 8 }, new() { Kind = "build", Item = "Barracks" }, new() { Kind = "train", Item = "Soldier", Count = 18, Keep = true }],
        Forever = ["Soldier", "Soldier", "Archer"],
        Attack = new() { Trigger = "army", Army = 12, Target = "base" },
    };

    private static readonly StartKit RushKit = new(0,
        [.. Enumerable.Repeat(UnitType.Soldier, 10), .. Enumerable.Repeat(UnitType.Archer, 4)], [BuildingType.Barracks]);

    private static readonly PickupPlanDto Sitter = new() { Forever = ["Worker"], Attack = new() { Trigger = "army", Army = 60 } };

    private static readonly StartKit Bare = new(200, [], [BuildingType.Barracks]);

    private static (GameSimulation Sim, PickupBot[] Bots) Setup(PickupPlanDto p0, StartKit k0, PickupPlanDto p1, StartKit k1, int treaty = Treaty) =>
        (new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = 3, MapWidth = 32, MapHeight = 32, MaxTicks = 1500, TreatyTicks = treaty, CombatScore = true },
            [new PlayerSeat(TestGames.P1, "alpha", k0), new PlayerSeat(TestGames.P2, "bravo", k1)]),
         [new PickupBot("alpha", p0, treaty), new PickupBot("bravo", p1, treaty)]);

    private static void Run(GameSimulation sim, PickupBot[] bots, int ticks)
    {
        var rules = GameRules.ToDto();
        var map = sim.Map.ToDto();
        for (var t = 0; t < ticks && sim.Status == MatchStatus.Active; t++)
        {
            for (var slot = 0; slot < bots.Length; slot++)
            {
                if (!sim.IsEliminated(slot))
                {
                    sim.Submit(slot, bots[slot].Decide(new BotContext(sim.GetPlayerView(slot), map, rules)));
                }
            }

            sim.Step();
        }
    }

    [Fact]
    public void The_rush_deck_beats_a_plan_that_never_fights()
    {
        var (sim, bots) = Setup(Rush, RushKit, Sitter, Bare);

        Run(sim, bots, 1500);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        Assert.Equal(TestGames.P1, sim.Outcome!.WinnerId);
    }

    [Fact]
    public void A_train_up_to_step_finishes_when_the_count_is_reached()
    {
        var plan = new PickupPlanDto { Steps = [new() { Kind = "train", Item = "Soldier", Count = 3 }], Forever = ["Worker"], Attack = new() { Army = 60 } };
        var (sim, bots) = Setup(plan, new StartKit(1500, [], [BuildingType.Barracks]), Sitter, Bare);

        Run(sim, bots, 250);

        Assert.Equal(1, bots[0].CurrentStep);
        Assert.True(sim.UnitIds(0, UnitType.Soldier).Count() >= 3);
    }

    [Fact]
    public void A_step_that_needs_a_missing_building_waits_and_the_workers_keep_digging()
    {
        var plan = new PickupPlanDto { Steps = [new() { Kind = "research", Item = "Weapons1" }], Forever = ["Soldier"], Attack = new() { Army = 60 } };
        var (sim, bots) = Setup(plan, Bare, Sitter, Bare);

        Run(sim, bots, 300);

        Assert.Equal(0, bots[0].CurrentStep);
        Assert.True(sim.Resources(0) > 200, $"banked {sim.Resources(0)}");
    }

    [Fact]
    public void Pull_back_brings_the_army_home()
    {
        var eager = Rush with { Attack = new() { Trigger = "army", Army = 1, Target = "base" } };
        var (sim, bots) = Setup(eager, RushKit, Sitter, Bare, treaty: 20);
        Run(sim, bots, 60);
        var hq = sim.BuildingsOf(0).Single(b => b.Type == BuildingType.CommandCenter);
        Assert.Contains(sim.UnitIds(0, UnitType.Soldier), id => Dist(sim.Find(id)!.Value, hq) > 8);

        bots[0].PullBack();
        Run(sim, bots, 100);

        Assert.All(sim.UnitIds(0, UnitType.Soldier), id => Assert.True(Dist(sim.Find(id)!.Value, hq) <= 8));
    }

    private static int Dist((int X, int Y, int Hp) a, (BuildingType Type, int X, int Y, bool Completed) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
