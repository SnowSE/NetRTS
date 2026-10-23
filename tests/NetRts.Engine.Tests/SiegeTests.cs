using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class SiegeTests
{
    private static readonly UnitStats Trebuchet = GameRules.Units[UnitType.Trebuchet];

    [Fact]
    public void Siege_workshop_needs_a_tech_lab_and_siege_engineering()
    {
        var sim = TestGames.New();
        sim.SetResources(0, 1000);
        var worker = sim.UnitIds(0).First();
        var build = new CommandRequest { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.SiegeWorkshop, X = 12, Y = 12 };

        sim.SpawnBuilding(0, BuildingType.TechLab, 14, 14);
        Assert.Equal("MISSING_PREREQUISITE", sim.SubmitOne(0, build).AssertRejected());

        sim.GrantUpgrade(0, UpgradeType.SiegeEngineering);
        sim.SubmitOne(0, build).AssertAccepted();
    }

    [Fact]
    public void Siege_workshop_trains_trebuchets()
    {
        var sim = TestGames.New();
        sim.SetResources(0, 1000);
        var workshop = sim.SpawnBuilding(0, BuildingType.SiegeWorkshop, 12, 12);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = workshop, UnitType = UnitType.Trebuchet }).AssertAccepted();
        Assert.Equal("CANNOT_PRODUCE", sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = workshop, UnitType = UnitType.Soldier }).AssertRejected());
        sim.Run(Trebuchet.BuildTicks + 1);

        Assert.Single(sim.UnitIds(0, UnitType.Trebuchet));
        Assert.Equal(1000 - Trebuchet.Cost, sim.Resources(0));
    }

    [Fact]
    public void A_trebuchet_outranges_a_guard_tower()
    {
        Assert.True(Trebuchet.Range > GameRules.Buildings[BuildingType.GuardTower].Range);

        var sim = TestGames.New();
        var trebuchet = sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        var tower = sim.SpawnBuilding(1, BuildingType.GuardTower, 8 + Trebuchet.Range, 12);

        sim.Step();
        var shell = Assert.Single(sim.GetSpectatorView().Shells);
        Assert.Equal((8 + Trebuchet.Range, 12, trebuchet, 1 + Trebuchet.ShotTicks), (shell.X, shell.Y, shell.AttackerId, shell.LandsTick));

        sim.Run(Trebuchet.ShotTicks);
        var towerStats = GameRules.Buildings[BuildingType.GuardTower];
        Assert.Equal(towerStats.MaxHp - (Trebuchet.Damage - towerStats.Armor), sim.Find(tower)!.Value.Hp);
        Assert.Equal(Trebuchet.MaxHp, sim.Find(trebuchet)!.Value.Hp);
        Assert.Empty(sim.GetSpectatorView().Shells);
    }

    [Fact]
    public void A_shell_hits_everyone_standing_on_its_tile_when_it_lands()
    {
        var sim = TestGames.New();
        sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        var a = sim.SpawnUnit(1, UnitType.Soldier, 14, 12);
        var b = sim.SpawnUnit(1, UnitType.Soldier, 14, 12);

        sim.Run(1 + Trebuchet.ShotTicks);

        var hp = GameRules.Units[UnitType.Soldier].MaxHp - (Trebuchet.Damage - GameRules.Units[UnitType.Soldier].Armor);
        Assert.Equal(hp, sim.Find(a)!.Value.Hp);
        Assert.Equal(hp, sim.Find(b)!.Value.Hp);
    }

    [Fact]
    public void Units_that_keep_moving_dodge_the_shell()
    {
        var sim = TestGames.New();
        var runner = sim.SpawnUnit(1, UnitType.Soldier, 14, 12);
        sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        sim.SubmitOne(1, TestGames.Move(14, 16, runner)).AssertAccepted();

        sim.Step();
        Assert.Single(sim.GetSpectatorView().Shells);
        sim.Run(Trebuchet.ShotTicks);

        Assert.Equal(GameRules.Units[UnitType.Soldier].MaxHp, sim.Find(runner)!.Value.Hp);
    }

    [Fact]
    public void A_trebuchet_cannot_fire_inside_its_minimum_range()
    {
        var sim = TestGames.New();
        var trebuchet = sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        var close = sim.SpawnUnit(1, UnitType.Worker, 8 + Trebuchet.MinRange - 1, 12);

        sim.Step();
        Assert.Empty(sim.GetSpectatorView().Shells);

        // Even when ordered to.
        sim.SubmitOne(0, TestGames.AttackTarget(close, trebuchet)).AssertAccepted();
        sim.Run(Trebuchet.ShotTicks + 2);
        Assert.Empty(sim.GetSpectatorView().Shells);
        Assert.Equal(GameRules.Units[UnitType.Worker].MaxHp, sim.Find(close)!.Value.Hp);
    }

    [Fact]
    public void An_idle_trebuchet_prefers_a_target_that_is_standing_still()
    {
        var sim = TestGames.New();
        var runner = sim.SpawnUnit(1, UnitType.Scout, 13, 12);
        var sitter = sim.SpawnUnit(1, UnitType.Worker, 15, 12);
        sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        sim.SubmitOne(1, TestGames.Move(13, 16, runner)).AssertAccepted();

        sim.Step();

        var shell = Assert.Single(sim.GetSpectatorView().Shells);
        Assert.Equal((15, 12), (shell.X, shell.Y));
        Assert.NotNull(sim.Find(sitter));
    }

    [Fact]
    public void Players_see_shells_they_fired_or_that_land_in_their_sight()
    {
        var sim = TestGames.New();
        sim.SpawnUnit(0, UnitType.Trebuchet, 8, 12);
        sim.SpawnUnit(1, UnitType.Soldier, 14, 12);

        sim.Step();

        Assert.Single(sim.GetPlayerView(0).Shells);
        Assert.Single(sim.GetPlayerView(1).Shells);
    }
}
