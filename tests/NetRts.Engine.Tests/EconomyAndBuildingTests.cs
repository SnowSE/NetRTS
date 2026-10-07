using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class EconomyAndBuildingTests
{
    [Fact]
    public void Workers_mine_and_haul_ore_home_continuously()
    {
        var sim = TestGames.New();
        var deposit = sim.GetPlayerView(0).Resources.OrderBy(r => Math.Abs(r.X - 7) + Math.Abs(r.Y - 7)).First();
        var start = sim.Resources(0);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, Units = "all", UnitType = UnitType.Worker, TargetId = deposit.Id }).AssertAccepted();
        sim.Run(100);

        var view = sim.GetPlayerView(0);
        Assert.True(view.You.Resources > start + 100, $"Expected income, have {view.You.Resources}");
        Assert.Equal(view.You.Resources - start, view.Players[0].Score.Economy);
        Assert.True(view.Resources.Single(r => r.Id == deposit.Id).Remaining < deposit.Remaining);
        Assert.All(view.Units.Where(u => u.Type == UnitType.Worker),
            w => Assert.Contains(w.Activity, new[] { UnitActivity.Gathering, UnitActivity.Returning, UnitActivity.Moving }));
    }

    [Fact]
    public void Workers_move_on_to_a_neighbouring_deposit_when_theirs_runs_dry()
    {
        var sim = TestGames.New();
        sim.ClearArmies();
        var worker = sim.SpawnUnit(0, UnitType.Worker, 12, 12);
        var small = sim.SpawnDeposit(13, 13, amount: 4);
        var next = sim.SpawnDeposit(14, 14, amount: 500);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, UnitIds = [worker], TargetId = small }).AssertAccepted();
        sim.Run(60);

        Assert.Null(sim.Find(small));
        Assert.True(sim.Find(next)!.Value.Hp < 500);
    }

    [Fact]
    public void Ore_beyond_storage_capacity_is_lost_until_you_build_a_depot()
    {
        var sim = TestGames.New();
        sim.SetResources(0, 1495); // CC storage is 1500
        var deposit = sim.GetPlayerView(0).Resources.First();

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, Units = "all", TargetId = deposit.Id }).AssertAccepted();
        sim.Run(60);

        Assert.Equal(1500, sim.Resources(0));
    }

    [Fact]
    public void Workers_construct_buildings_and_pay_when_construction_starts()
    {
        var sim = TestGames.New();
        var workers = sim.UnitIds(0, UnitType.Worker).Take(2).ToArray();

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = workers, BuildingType = BuildingType.Barracks, X = 12, Y = 9 }).AssertAccepted();
        Assert.Equal(500, sim.Resources(0)); // not paid yet

        sim.Run(8);
        var site = Assert.Single(sim.GetPlayerView(0).Buildings, b => b.Type == BuildingType.Barracks);
        Assert.False(site.Completed);
        Assert.Equal(350, sim.Resources(0));
        Assert.True(site.Hp < site.MaxHp);

        // 40 worker-ticks of work shared by two workers.
        sim.Run(25);
        var barracks = Assert.Single(sim.GetPlayerView(0).Buildings, b => b.Type == BuildingType.Barracks);
        Assert.True(barracks.Completed);
        Assert.Equal(barracks.MaxHp, barracks.Hp);
        Assert.Contains(sim.GetPlayerView(0, sinceTick: 0).Events, e => e.Kind == "BuildingCompleted");
    }

    [Fact]
    public void A_build_that_fails_on_arrival_reports_its_command_id()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0, UnitType.Worker).First();
        var accepted = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.Barracks, X = 14, Y = 14 });
        accepted.AssertAccepted();
        sim.Step();             // the order is accepted and the worker sets off...
        sim.SetResources(0, 40); // ...while the ore is spent elsewhere

        sim.Run(15);

        var failure = Assert.Single(sim.GetPlayerView(0, sinceTick: 0).Events, e => e.Kind == "CommandFailed");
        Assert.StartsWith("Cannot start Barracks", failure.Message);
        Assert.Equal(accepted.Results[0].CommandId, failure.CommandId);
        Assert.Equal(worker, failure.EntityId);
    }

    [Fact]
    public void Building_on_an_occupied_tile_is_rejected()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0).First();

        var code = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.Barracks, X = 7, Y = 7 }).AssertRejected();

        Assert.Equal("POSITION_OCCUPIED", code);
    }

    [Fact]
    public void Building_without_enough_ore_or_prerequisites_is_rejected()
    {
        var sim = TestGames.New();
        var worker = sim.UnitIds(0).First();

        Assert.Equal("MISSING_PREREQUISITE", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.TechLab, X = 12, Y = 12 }).AssertRejected());

        sim.SetResources(0, 10);
        Assert.Equal("INSUFFICIENT_RESOURCES", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.Barracks, X = 12, Y = 12 }).AssertRejected());
    }

    [Fact]
    public void Only_workers_can_build_or_gather()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 12, 12);

        var code = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Build, UnitIds = [soldier], BuildingType = BuildingType.Barracks, X = 13, Y = 13 }).AssertRejected();

        Assert.Equal("NO_MATCHING_UNITS", code);
    }

    [Fact]
    public void Production_spawns_the_unit_next_to_the_building_after_its_build_time()
    {
        var sim = TestGames.New();
        var barracks = sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = barracks, UnitType = UnitType.Soldier, Count = 2 }).AssertAccepted();
        sim.Step();
        Assert.Equal(300, sim.Resources(0)); // both paid up front

        var production = sim.GetPlayerView(0).Buildings.Single(b => b.Id == barracks).Production;
        Assert.Equal(2, production.Count);

        sim.Run(GameRules.Units[UnitType.Soldier].BuildTicks);
        var soldier = Assert.Single(sim.GetPlayerView(0).Units, u => u.Type == UnitType.Soldier);
        Assert.Equal(1, Math.Max(Math.Abs(soldier.X - 12), Math.Abs(soldier.Y - 12)));
    }

    [Fact]
    public void Production_requires_an_operational_building_that_makes_that_unit()
    {
        var sim = TestGames.New();
        var cc = sim.CommandCenterId(0);
        var site = sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12, completed: false);

        Assert.Equal("CANNOT_PRODUCE", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = cc, UnitType = UnitType.Soldier }).AssertRejected());
        Assert.Equal("BUILDING_NOT_OPERATIONAL", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = site, UnitType = UnitType.Soldier }).AssertRejected());

        sim.SetResources(0, 20);
        Assert.Equal("INSUFFICIENT_RESOURCES", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = cc, UnitType = UnitType.Worker }).AssertRejected());
    }

    [Fact]
    public void Research_enforces_prerequisites_and_reports_progress()
    {
        var sim = TestGames.New();
        sim.SetResources(0, 1000);
        var lab = sim.SpawnBuilding(0, BuildingType.TechLab, 12, 12);

        Assert.Equal("MISSING_PREREQUISITE", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Research, BuildingId = lab, Upgrade = UpgradeType.Weapons2 }).AssertRejected());

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Research, BuildingId = lab, Upgrade = UpgradeType.Weapons1 }).AssertAccepted();
        sim.Run(10);
        var researching = Assert.Single(sim.GetPlayerView(0).You.Researching);
        Assert.Equal(UpgradeType.Weapons1, researching.Upgrade);
        Assert.InRange(researching.Percent, 20, 30);

        sim.Run(GameRules.Upgrades[UpgradeType.Weapons1].ResearchTicks);
        Assert.Contains(UpgradeType.Weapons1, sim.GetPlayerView(0).You.Upgrades);
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Research, BuildingId = lab, Upgrade = UpgradeType.Weapons2 }).AssertAccepted();
    }
}
