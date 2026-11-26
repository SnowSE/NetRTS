using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>The optional mechanics a rule set can turn on, and that classic leaves them off.</summary>
public class RuleSetTests
{
    private static readonly RuleSet Spoils50 = GameRules.Classic with { Name = "spoils", SpoilsPercent = 50 };

    [Fact]
    public void Renaming_a_rule_set_keeps_its_version_and_changing_a_mechanic_does_not()
    {
        Assert.Equal(GameRules.Version, (GameRules.Classic with { Name = "copy" }).Version);
        Assert.NotEqual(GameRules.Version, Spoils50.Version);
        Assert.NotEqual(GameRules.Version, GameRules.Classic.WithBuilding(BuildingType.GuardTower, s => s with { AttackCooldown = 3 }).Version);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 75)]
    public void Destroying_a_building_pays_its_share_of_the_cost_as_spoils(int percent, int expected)
    {
        var sim = TestGames.New(rules: GameRules.Classic with { Name = "test", SpoilsPercent = percent });
        sim.SetResources(0, 100);
        var barracks = sim.SpawnBuilding(1, BuildingType.Barracks, 12, 12);
        sim.SetHp(barracks, 1);
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 11, 12);
        sim.SubmitOne(0, TestGames.AttackTarget(barracks, soldier)).AssertAccepted();

        sim.Run(3);

        Assert.Null(sim.Find(barracks));
        Assert.Equal(100 + expected, sim.Resources(0));
    }

    [Theory]
    [InlineData(0, 400)]
    [InlineData(25, 500)]
    public void Each_command_center_you_own_raises_the_price_of_the_next(int percent, int expected)
    {
        var sim = TestGames.New(rules: GameRules.Classic with { Name = "test", ExtraHqCostPercent = percent });
        var worker = sim.UnitIds(0, UnitType.Worker).First();
        CommandRequest Build() => new() { Type = CommandType.Build, UnitIds = [worker], BuildingType = BuildingType.CommandCenter, X = 14, Y = 14 };

        sim.SetResources(0, expected - 1);
        Assert.Equal("INSUFFICIENT_RESOURCES", sim.SubmitOne(0, Build()).AssertRejected());
        sim.SetResources(0, expected);
        sim.SubmitOne(0, Build()).AssertAccepted();
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 5)]
    public void Rapid_fire_cuts_the_tower_cooldown(bool rapidFire, int shotsIn9Ticks)
    {
        var rules = GameRules.Classic.WithBuilding(BuildingType.GuardTower, s => s with { AttackCooldown = 3 }) with { Name = "test" };
        var sim = TestGames.New(rules: rules);
        if (rapidFire)
        {
            sim.GrantUpgrade(0, UpgradeType.RapidFire);
        }

        sim.SpawnBuilding(0, BuildingType.GuardTower, 12, 12);
        var target = sim.SpawnUnit(1, UnitType.Soldier, 14, 12);
        sim.SetHp(target, 100_000);

        var shots = 0;
        for (var i = 0; i < 9; i++)
        {
            sim.Step();
            shots += sim.GetSpectatorView().TowerShots.Count;
        }

        Assert.Equal(shotsIn9Ticks, shots);
    }

    [Fact]
    public void Rapid_fire_cannot_be_researched_where_the_rules_do_not_list_it()
    {
        var sim = TestGames.New();
        sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12);
        var lab = sim.SpawnBuilding(0, BuildingType.TechLab, 14, 12);
        sim.SetResources(0, 1000);

        var code = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Research, BuildingId = lab, Upgrade = UpgradeType.RapidFire }).AssertRejected();

        Assert.Equal("MISSING_UPGRADE", code);
    }

    [Fact]
    public void A_patch_regrows_in_proportion_to_what_it_holds()
    {
        var sim = TestGames.New(rules: GameRules.Classic with { Name = "test", RegrowPartsPerMillion = 2000 });
        var patch = sim.SpawnDeposit(14, 14, 1000);
        sim.SetHp(patch, 500);

        sim.Run(10);

        Assert.Equal(510, sim.Find(patch)!.Value.Hp);   // 500 x 0.2% = 1 grub a tick
    }

    [Fact]
    public void A_patch_never_regrows_past_its_starting_amount()
    {
        var sim = TestGames.New(rules: GameRules.Classic with { Name = "test", RegrowPartsPerMillion = 100_000 });
        var patch = sim.SpawnDeposit(14, 14, 1000);
        sim.SetHp(patch, 990);

        sim.Run(5);

        Assert.Equal(1000, sim.Find(patch)!.Value.Hp);
    }

    [Fact]
    public void Classic_patches_do_not_regrow()
    {
        var sim = TestGames.New();
        var patch = sim.SpawnDeposit(14, 14, 1000);
        sim.SetHp(patch, 500);

        sim.Run(50);

        Assert.Equal(500, sim.Find(patch)!.Value.Hp);
    }
}
