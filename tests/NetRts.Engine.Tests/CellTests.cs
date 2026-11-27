using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>Cell limits, digging spots and farms (the spring rules); classic leaves all three off.</summary>
public class CellTests
{
    private static GameSimulation Spring()
    {
        var sim = TestGames.New(rules: GameRules.SpringDraft);
        sim.ClearArmies();
        return sim;
    }

    private static List<UnitDto> Units(GameSimulation sim, int? owner = null) =>
        sim.GetSpectatorView().Units.Where(u => owner is null || u.Owner == owner).ToList();

    private static int[] Spawn(GameSimulation sim, int slot, UnitType type, int count, int x, int y) =>
        Enumerable.Range(0, count).Select(_ => sim.SpawnUnit(slot, type, x, y)).ToArray();

    [Fact]
    public void A_cell_holds_four_workers_and_the_fifth_is_bumped_next_door()
    {
        var sim = Spring();
        var workers = Enumerable.Range(0, 5).Select(i => sim.SpawnUnit(0, UnitType.Worker, 10 + i, 14)).ToArray();

        sim.SubmitOne(0, TestGames.Move(12, 10, workers)).AssertAccepted();
        sim.Run(8);

        var units = Units(sim);
        Assert.Equal(4, units.Count(u => (u.X, u.Y) == (12, 10)));
        var bumped = Assert.Single(units, u => (u.X, u.Y) != (12, 10));
        Assert.Equal(1, Math.Max(Math.Abs(bumped.X - 12), Math.Abs(bumped.Y - 10)));
        Assert.Null(sim.CellProblem());
    }

    [Fact]
    public void Classic_units_all_stand_on_the_same_tile()
    {
        var sim = TestGames.New();
        sim.ClearArmies();
        var workers = Enumerable.Range(0, 6).Select(i => sim.SpawnUnit(0, UnitType.Worker, 10 + i, 14)).ToArray();

        sim.SubmitOne(0, TestGames.Move(12, 10, workers)).AssertAccepted();
        sim.Run(8);

        Assert.All(Units(sim), u => Assert.Equal((12, 10), (u.X, u.Y)));
    }

    [Fact]
    public void A_trebuchet_fills_a_cell_on_its_own()
    {
        var sim = Spring();
        var trebuchet = sim.SpawnUnit(0, UnitType.Trebuchet, 12, 12);
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 12, 14);

        sim.SubmitOne(0, TestGames.Move(12, 12, soldier)).AssertAccepted();
        sim.Run(4);

        var placed = sim.Find(soldier)!.Value;
        Assert.NotEqual((12, 12), (placed.X, placed.Y));
        Assert.Equal((12, 12), (sim.Find(trebuchet)!.Value.X, sim.Find(trebuchet)!.Value.Y));
    }

    [Fact]
    public void Units_walk_through_a_full_cell()
    {
        var sim = Spring();
        Spawn(sim, 0, UnitType.Soldier, 9, 12, 12);
        var scout = sim.SpawnUnit(0, UnitType.Scout, 12, 9);

        sim.SubmitOne(0, TestGames.Move(12, 15, scout)).AssertAccepted();
        sim.Run(4);

        Assert.Equal((12, 15), (sim.Find(scout)!.Value.X, sim.Find(scout)!.Value.Y));
        Assert.Null(sim.CellProblem());
    }

    [Fact]
    public void First_in_keeps_the_cell()
    {
        var sim = Spring();
        var settled = Spawn(sim, 0, UnitType.Soldier, 8, 12, 12);
        var late = Spawn(sim, 0, UnitType.Worker, 1, 12, 15);

        sim.SubmitOne(0, TestGames.Move(12, 12, late)).AssertAccepted();
        sim.Run(5);

        Assert.All(settled, id => Assert.Equal((12, 12), (sim.Find(id)!.Value.X, sim.Find(id)!.Value.Y)));
        Assert.NotEqual((12, 12), (sim.Find(late[0])!.Value.X, sim.Find(late[0])!.Value.Y));
    }

    [Fact]
    public void Players_take_turns_into_a_contested_cell_and_the_first_turn_moves_round()
    {
        // Two cells, each reached on the same tick by five soldiers from each player: ten soldiers, room for nine.
        var sim = Spring();
        var west = Spawn(sim, 0, UnitType.Soldier, 5, 9, 10);
        var east = Spawn(sim, 1, UnitType.Soldier, 5, 15, 10);
        var west2 = Spawn(sim, 0, UnitType.Soldier, 5, 9, 15);
        var east2 = Spawn(sim, 1, UnitType.Soldier, 5, 15, 15);
        sim.Submit(0, [TestGames.Move(12, 10, west), TestGames.Move(12, 15, west2)]).AssertAccepted();
        sim.Submit(1, [TestGames.Move(12, 10, east), TestGames.Move(12, 15, east2)]).AssertAccepted();

        sim.Run(3);   // they arrive together on the third tick, before anyone swings

        int At(int owner, int y) => Units(sim, owner).Count(u => (u.X, u.Y) == (12, y));
        Assert.Equal((5, 4), (At(0, 10), At(1, 10)));   // first collision: player 0 deals first
        Assert.Equal((4, 5), (At(0, 15), At(1, 15)));   // the next: player 1 does
    }

    [Fact]
    public void New_units_spread_out_from_the_building_as_cells_fill()
    {
        var sim = TestGames.New(rules: GameRules.SpringDraft);
        sim.SetResources(0, 5000);
        var cc = sim.CommandCenterId(0);
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = cc, UnitType = UnitType.Worker, Count = 5 }).AssertAccepted();

        sim.Run(60);

        Assert.Equal(10, sim.UnitIds(0, UnitType.Worker).Count());
        Assert.All(Units(sim, 0).GroupBy(u => (u.X, u.Y)), g => Assert.True(g.Count() <= 4));
        Assert.Null(sim.CellProblem());
    }

    // ------------------------------------------------------------------ digging

    /// <summary>A patch at (12,12) with buildings on every side but its west, so (11,12) is the only cell to dig from.</summary>
    private static (GameSimulation Sim, int Patch) OneSidedPatch()
    {
        var sim = Spring();
        var patch = sim.SpawnDeposit(12, 12, 5000);
        foreach (var (x, y) in new[] { (11, 11), (12, 11), (13, 11), (13, 12), (11, 13), (12, 13), (13, 13) })
        {
            sim.SpawnBuilding(0, BuildingType.Barracks, x, y);
        }

        return (sim, patch);
    }

    private static CommandRequest Gather(int targetId, params int[] unitIds) =>
        new() { Type = CommandType.Gather, UnitIds = unitIds, TargetId = targetId };

    [Fact]
    public void Two_dig_from_a_cell_two_more_wait_and_a_fifth_finds_nowhere_and_stops()
    {
        var (sim, patch) = OneSidedPatch();
        var workers = Spawn(sim, 0, UnitType.Worker, 5, 9, 14);

        sim.SubmitOne(0, Gather(patch, workers)).AssertAccepted();
        sim.Run(5);

        var units = Units(sim, 0);
        Assert.Equal(2, units.Count(u => u.Activity == UnitActivity.Gathering));
        Assert.Equal(2, units.Count(u => u.Activity == UnitActivity.Waiting));
        Assert.Single(units, u => u.Activity == UnitActivity.Idle);
        Assert.Contains(sim.GetPlayerView(0, 0).Events, e => e.Kind == "CommandFailed" && e.Message.Contains("no free spot"));
        Assert.Null(sim.CellProblem());
    }

    [Fact]
    public void A_digger_leaving_to_drop_off_hands_its_spot_to_the_next_in_line()
    {
        var (sim, patch) = OneSidedPatch();
        var workers = Spawn(sim, 0, UnitType.Worker, 3, 9, 14);
        sim.SubmitOne(0, Gather(patch, workers)).AssertAccepted();

        sim.Run(4);
        var waiter = Assert.Single(Units(sim, 0), u => u.Activity == UnitActivity.Waiting).Id;

        var waiterDug = false;
        for (var tick = 0; tick < 20 && !waiterDug; tick++)
        {
            sim.Step();
            Assert.Null(sim.CellProblem());
            var units = Units(sim, 0);
            waiterDug = units.Any(u => u.Activity == UnitActivity.Returning) && units.Single(u => u.Id == waiter).Activity == UnitActivity.Gathering;
        }

        Assert.True(waiterDug, "once a digger heads home, the worker in line should dig");
    }

    [Fact]
    public void Workers_sent_to_one_patch_spread_round_it_two_to_a_cell()
    {
        var sim = Spring();
        var patch = sim.SpawnDeposit(12, 12, 5000);
        var workers = Spawn(sim, 0, UnitType.Worker, 6, 9, 15);

        sim.SubmitOne(0, Gather(patch, workers)).AssertAccepted();
        sim.Run(6);

        var digging = Units(sim, 0).Where(u => u.Activity == UnitActivity.Gathering).ToList();
        Assert.Equal(6, digging.Count);
        Assert.All(digging.GroupBy(u => (u.X, u.Y)), g => Assert.True(g.Count() <= 2));
    }

    [Fact]
    public void Classic_workers_all_dig_from_the_same_tile()
    {
        var sim = TestGames.New();
        sim.ClearArmies();
        var patch = sim.SpawnDeposit(12, 12, 5000);
        var workers = Spawn(sim, 0, UnitType.Worker, 6, 9, 15);

        sim.SubmitOne(0, Gather(patch, workers)).AssertAccepted();
        sim.Run(6);

        Assert.Equal(6, Units(sim, 0).Count(u => u.Activity == UnitActivity.Gathering));
    }

    // ------------------------------------------------------------------ farms

    [Fact]
    public void Classic_has_no_farms()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0, UnitType.Worker).First();

        var code = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.Farm, X = 12, Y = 12 }).AssertRejected();

        Assert.Equal("MISSING_BUILDING_TYPE", code);
    }

    [Fact]
    public void A_finished_farm_grows_a_crop_that_workers_dig_slowly()
    {
        var sim = Spring();
        var worker = sim.SpawnUnit(0, UnitType.Worker, 10, 12);
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.Farm, X = 12, Y = 12 }).AssertAccepted();
        sim.Run(40);
        var farm = sim.GetSpectatorView().Buildings.Single(b => b.Type == BuildingType.Farm);
        Assert.True(farm.Completed);
        Assert.Equal(GameRules.SpringDraft.Farm!.Grubs, farm.Grubs);

        sim.SubmitOne(0, Gather(farm.Id, worker)).AssertAccepted();
        sim.Run(5);

        var left = sim.FarmGrubs(farm.Id);
        Assert.InRange(GameRules.SpringDraft.Farm.Grubs - left, 3, 4);   // 1 a tick, from the tick it starts
    }

    [Fact]
    public void A_dug_out_farm_sends_its_workers_on_and_a_gather_replants_it()
    {
        var sim = Spring();
        sim.SetResources(0, 200);
        var farm = sim.SpawnBuilding(0, BuildingType.Farm, 12, 12);
        var worker = sim.SpawnUnit(0, UnitType.Worker, 11, 12);
        sim.SetFarmGrubs(farm, 2);

        sim.SubmitOne(0, Gather(farm, worker)).AssertAccepted();
        sim.Run(4);
        Assert.Equal(0, sim.FarmGrubs(farm));
        Assert.Contains(sim.GetPlayerView(0, 0).Events, e => e.Kind == "FarmEmpty");
        Assert.NotEqual(farm, sim.GetSpectatorView().Units.Single(u => u.Id == worker).TargetId);

        sim.SubmitOne(0, Gather(farm, worker)).AssertAccepted();
        sim.Run(15);

        var replant = GameRules.SpringDraft.Farm!.ReplantCost;
        Assert.True(sim.FarmGrubs(farm) > GameRules.SpringDraft.Farm.Grubs - 15);
        Assert.Contains(sim.GetPlayerView(0, 0).Events, e => e.Kind == "FarmReplanted");
        Assert.True(sim.Resources(0) <= 200 + 2 - replant + 15, "replanting is paid for");
    }

    [Fact]
    public void Nobody_digs_an_enemy_farm()
    {
        var sim = Spring();
        var farm = sim.SpawnBuilding(1, BuildingType.Farm, 12, 12);
        var worker = sim.SpawnUnit(0, UnitType.Worker, 11, 12);

        Assert.Equal("RESOURCE_NOT_FOUND", sim.SubmitOne(0, Gather(farm, worker)).AssertRejected());
    }

    [Fact]
    public void Replanting_needs_the_grubs()
    {
        var sim = Spring();
        sim.SetResources(0, 10);
        var farm = sim.SpawnBuilding(0, BuildingType.Farm, 12, 12);
        var worker = sim.SpawnUnit(0, UnitType.Worker, 11, 12);
        sim.SetFarmGrubs(farm, 0);

        Assert.Equal("INSUFFICIENT_RESOURCES", sim.SubmitOne(0, Gather(farm, worker)).AssertRejected());
    }
}
