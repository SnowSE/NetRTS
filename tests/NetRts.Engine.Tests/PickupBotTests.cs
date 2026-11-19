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

    [Fact]
    public void A_plan_changed_mid_match_keeps_the_steps_already_done_and_plays_the_new_ones()
    {
        var first = new PickupPlanDto { Steps = [new() { Kind = "train", Item = "Soldier", Count = 3 }], Forever = ["Worker"], Attack = new() { Army = 60 } };
        var (sim, bots) = Setup(first, new StartKit(1500, [], [BuildingType.Barracks]), Sitter, Bare);
        Run(sim, bots, 250);
        Assert.Equal(1, bots[0].CurrentStep);

        bots[0].Apply(first with
        {
            Steps = [first.Steps[0], new() { Kind = "train", Item = "Archer", Count = 2 }],
        });
        Run(sim, bots, 250);

        Assert.Equal(2, bots[0].CurrentStep);
        Assert.True(sim.UnitIds(0, UnitType.Archer).Count() >= 2);
    }

    [Fact]
    public void A_shorter_plan_mid_match_moves_on_to_its_forever_cards()
    {
        var plan = new PickupPlanDto
        {
            Steps = [new() { Kind = "research", Item = "Weapons1" }, new() { Kind = "workers", Count = 20 }],
            Forever = ["Worker"],
            Attack = new() { Army = 60 },
        };
        var (sim, bots) = Setup(plan, Bare, Sitter, Bare);
        Run(sim, bots, 20);
        Assert.Equal(0, bots[0].CurrentStep);   // stuck: no Makerspace

        bots[0].Apply(plan with { Steps = [], Forever = ["Soldier"] });
        Run(sim, bots, 300);

        Assert.Equal(0, bots[0].CurrentStep);
        Assert.NotEmpty(sim.UnitIds(0, UnitType.Soldier));
    }

    [Fact]
    public void A_build_step_finishes_when_the_building_is_finished_not_when_it_starts()
    {
        var plan = new PickupPlanDto { Steps = [new() { Kind = "build", Item = "TechLab" }, new() { Kind = "workers", Count = 30 }], Forever = ["Worker"], Attack = new() { Army = 60 } };
        var (sim, bots) = Setup(plan, new StartKit(1500, [], [BuildingType.Barracks]), Sitter, Bare);

        var sawGoingUp = false;
        for (var t = 0; t < 300 && bots[0].CurrentStep == 0; t++)
        {
            Run(sim, bots, 1);
            var labs = sim.BuildingsOf(0).Where(b => b.Type == BuildingType.TechLab).ToList();
            if (labs.Count > 0 && !labs[0].Completed)
            {
                sawGoingUp = true;
                Assert.Equal(0, bots[0].CurrentStep);   // still on "Build a Makerspace" while it goes up
            }
        }

        Assert.True(sawGoingUp);
        Assert.Equal(1, bots[0].CurrentStep);
        Assert.Contains(sim.BuildingsOf(0), b => b.Type == BuildingType.TechLab && b.Completed);
    }

    [Fact]
    public void A_waiting_step_says_what_it_is_waiting_for()
    {
        var plan = new PickupPlanDto { Steps = [new() { Kind = "research", Item = "Weapons1" }], Forever = ["Worker"], Attack = new() { Army = 60 } };
        var (sim, bots) = Setup(plan, Bare, Sitter, Bare);

        Run(sim, bots, 5);

        Assert.Equal(new PickupWaitDto("building", "TechLab"), bots[0].Waiting);
    }

    [Fact]
    public void With_bell_towers_first_the_army_goes_for_a_tower_it_can_see()
    {
        var plan = new PickupPlanDto
        {
            Forever = ["Worker"],
            Attack = new() { Trigger = "army", Army = 1, Priority = ["towers", "workers", "base", "nearest"] },
        };
        var (sim, bots) = Setup(plan, new StartKit(0, [], [BuildingType.Barracks]), Sitter, new StartKit(0, [], [BuildingType.Barracks, BuildingType.GuardTower]), treaty: 0);
        var tower = sim.GetPlayerView(1).Buildings.Single(b => b.Owner == 1 && b.Type == BuildingType.GuardTower);
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, tower.X - 3, tower.Y);
        sim.Step();   // past tick 0

        var commands = bots[0].Decide(new BotContext(sim.GetPlayerView(0), sim.Map.ToDto(), GameRules.ToDto()));

        Assert.Contains(commands, c => c.Type == CommandType.Attack && c.TargetId == tower.Id && c.UnitIds!.Contains(soldier));
    }
}
