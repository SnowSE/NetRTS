using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class CommandTests
{
    [Fact]
    public void Move_walks_one_tile_per_tick_and_goes_idle_on_arrival()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 10, 10);

        sim.SubmitOne(0, TestGames.Move(14, 10, soldier)).AssertAccepted();
        sim.Step();
        Assert.Equal((11, 10), (sim.Find(soldier)!.Value.X, sim.Find(soldier)!.Value.Y));

        sim.Run(3);
        var unit = Assert.Single(sim.GetPlayerView(0).Units, u => u.Id == soldier);
        Assert.Equal((14, 10), (unit.X, unit.Y));
        Assert.Equal(UnitActivity.Idle, unit.Activity);
    }

    [Fact]
    public void Scouts_are_twice_as_fast_and_mobility_upgrades_speed_everyone_up()
    {
        var sim = TestGames.New();
        var scout = sim.SpawnUnit(0, UnitType.Scout, 8, 12);
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 8, 13);
        sim.GrantUpgrade(0, UpgradeType.Mobility1);
        sim.GrantUpgrade(0, UpgradeType.Mobility2);

        sim.Submit(0, [TestGames.Move(16, 12, scout), TestGames.Move(16, 13, soldier)]).AssertAccepted();
        sim.Run(5);

        Assert.Equal(16, sim.Find(scout)!.Value.X);   // 2.4 tiles/tick: arrives on tick 4
        Assert.Equal(15, sim.Find(soldier)!.Value.X); // 1.4 tiles/tick: 70 move points = 7 tiles in 5 ticks
    }

    [Fact]
    public void Range_selectors_pick_only_your_units_in_range()
    {
        var sim = TestGames.New();
        var workers = sim.UnitIds(0, UnitType.Worker).ToList();
        var selector = $"{workers.Min()}-{workers.Max()}";

        var response = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Move, Units = selector, X = 12, Y = 12 });
        response.AssertAccepted();
        sim.Step();

        Assert.All(sim.GetPlayerView(0).Units.Where(u => u.Type == UnitType.Worker),
            w => Assert.Equal(new PositionDto(12, 12), w.Destination ?? new PositionDto(w.X, w.Y)));
    }

    [Fact]
    public void Unit_type_filter_narrows_a_selection()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 12, 12);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Move, Units = "all", UnitType = UnitType.Soldier, X = 15, Y = 15 }).AssertAccepted();
        sim.Step();

        var view = sim.GetPlayerView(0);
        Assert.NotNull(view.Units.Single(u => u.Id == soldier).Destination);
        Assert.All(view.Units.Where(u => u.Type == UnitType.Worker), w => Assert.Null(w.Destination));
    }

    [Theory]
    [InlineData("1-7,12", 2)]
    [InlineData("5", 1)]
    [InlineData(" 3 - 9 ", 1)]
    public void Selector_parsing_accepts_ids_and_ranges(string selector, int expectedRanges) =>
        Assert.Equal(expectedRanges, GameSimulation.ParseSelector(selector)!.Count);

    [Theory]
    [InlineData("seven")]
    [InlineData("9-3")]
    [InlineData("1-x")]
    [InlineData(",")]
    public void Selector_parsing_rejects_garbage(string selector) =>
        Assert.Null(GameSimulation.ParseSelector(selector));

    [Fact]
    public void Commanding_units_you_do_not_own_is_rejected()
    {
        var sim = TestGames.New();
        var enemy = sim.UnitIds(1).First();

        var code = sim.SubmitOne(0, TestGames.Move(10, 10, enemy)).AssertRejected();

        Assert.Equal("UNIT_NOT_OWNED", code);
    }

    [Fact]
    public void Moving_outside_the_map_is_rejected()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0).First();

        Assert.Equal("OUT_OF_BOUNDS", sim.SubmitOne(0, TestGames.Move(64, 3, worker)).AssertRejected());
        Assert.Equal("OUT_OF_BOUNDS", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Move, UnitIds = [worker], Tile = 64 * 64 }).AssertRejected());
    }

    [Fact]
    public void Tile_index_is_an_alternative_to_x_and_y()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0).First();

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Move, UnitIds = [worker], Tile = 12 * 64 + 10 }).AssertAccepted();
        sim.Run(20);

        Assert.Equal((10, 12), (sim.Find(worker)!.Value.X, sim.Find(worker)!.Value.Y));
    }

    [Fact]
    public void The_most_recent_command_for_a_unit_wins()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 12, 12);

        sim.Submit(0, [TestGames.Move(16, 12, soldier), TestGames.Move(12, 16, soldier)]).AssertAccepted();
        sim.Run(4);

        Assert.Equal((12, 16), (sim.Find(soldier)!.Value.X, sim.Find(soldier)!.Value.Y));
    }

    [Fact]
    public void Attacking_something_you_cannot_see_is_rejected_without_leaking_that_it_exists()
    {
        var sim = TestGames.New();
        var mine = sim.UnitIds(0).First();
        var hiddenEnemyCc = sim.CommandCenterId(1);

        var code = sim.SubmitOne(0, TestGames.AttackTarget(hiddenEnemyCc, mine)).AssertRejected();

        Assert.Equal("TARGET_NOT_FOUND", code);
    }

    [Fact]
    public void Friendly_fire_is_rejected()
    {
        var sim = TestGames.New();
        var ids = sim.UnitIds(0).ToList();

        Assert.Equal("FRIENDLY_FIRE", sim.SubmitOne(0, TestGames.AttackTarget(ids[1], ids[0])).AssertRejected());
    }

    [Fact]
    public void Queue_is_capped_and_drained_a_fixed_number_of_commands_per_tick()
    {
        var sim = TestGames.New(queueCapacity: 10, commandsPerTick: 4);
        var worker = sim.UnitIds(0).First();
        var batch = Enumerable.Range(0, 12).Select(_ => TestGames.Move(12, 12, worker)).ToList();

        var response = sim.Submit(0, batch);

        Assert.Equal(10, response.Accepted);
        Assert.Equal(2, response.Rejected);
        Assert.All(response.Results.Skip(10), r => Assert.Equal("QUEUE_FULL", r.Error!.Code));
        sim.Step();
        Assert.Equal(6, sim.QueueSize(0));
        sim.Step();
        Assert.Equal(2, sim.QueueSize(0));
    }

    [Fact]
    public void Commands_that_become_invalid_before_they_run_fail_with_an_event()
    {
        var sim = TestGames.New();
        var cc = sim.CommandCenterId(0);
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = cc, UnitType = UnitType.Worker }).AssertAccepted();
        sim.SetResources(0, 0); // spent elsewhere before the tick ran

        sim.Step();

        var evt = Assert.Single(sim.GetPlayerView(0).Events, e => e.Kind == "CommandFailed");
        Assert.Contains("costs", evt.Message);
        Assert.NotNull(evt.CommandId);
    }
}
