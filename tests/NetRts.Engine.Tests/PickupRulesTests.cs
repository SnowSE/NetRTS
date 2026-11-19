using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>The pick-up game's match rules: start kits, the treaty and the combat score. Normal matches leave them off.</summary>
public class PickupRulesTests
{
    private static GameSimulation New(StartKit? kit0 = null, StartKit? kit1 = null, int treaty = 0, bool combat = false, int seed = 1) =>
        new(Guid.NewGuid(), new GameConfig { Seed = seed, MapWidth = 32, MapHeight = 32, TreatyTicks = treaty, CombatScore = combat },
            [new PlayerSeat(TestGames.P1, "alpha", kit0), new PlayerSeat(TestGames.P2, "bravo", kit1)]);

    private static readonly StartKit Kit = new(200, [UnitType.Soldier, UnitType.Soldier, UnitType.Soldier],
        [BuildingType.Barracks, BuildingType.GuardTower]);

    [Fact]
    public void Kit_sets_bank_units_and_completed_buildings()
    {
        var sim = New(Kit, Kit);

        Assert.Equal(200, sim.Resources(0));
        Assert.Equal(GameRules.StartingWorkers, sim.UnitIds(0, UnitType.Worker).Count());
        Assert.Equal(3, sim.UnitIds(0, UnitType.Soldier).Count());
        var buildings = sim.BuildingsOf(0).ToList();
        Assert.Equal([BuildingType.CommandCenter, BuildingType.Barracks, BuildingType.GuardTower], buildings.Select(b => b.Type).Order());
        Assert.All(buildings, b => Assert.True(b.Completed));
        Assert.All(buildings, b => Assert.False(sim.Map.IsRock(b.X, b.Y)));
        foreach (var a in buildings)
        {
            foreach (var b in buildings.Where(b => b != a))
            {
                Assert.True(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) >= 2, $"{a.Type} and {b.Type} touch");
            }
        }
    }

    [Fact]
    public void No_kit_is_unchanged()
    {
        var plain = TestGames.New();
        var again = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = 1 },
            [new PlayerSeat(TestGames.P1, "alpha", null), new PlayerSeat(TestGames.P2, "bravo")]);

        Assert.Equal(GameRules.StartingResources, plain.Resources(0));
        plain.Run(50);
        again.Run(50);
        Assert.Equal(plain.StateHash(), again.StateHash());
    }

    [Fact]
    public void Treaty_blocks_damage_between_players()
    {
        var sim = New(treaty: 50);
        sim.ClearArmies();
        var a = sim.SpawnUnit(0, UnitType.Soldier, 15, 15);
        var b = sim.SpawnUnit(1, UnitType.Soldier, 16, 15);
        sim.SubmitOne(0, TestGames.AttackTarget(b, a)).AssertAccepted();

        sim.Run(50);
        Assert.Equal(GameRules.Units[UnitType.Soldier].MaxHp, sim.Find(a)!.Value.Hp);
        Assert.Equal(GameRules.Units[UnitType.Soldier].MaxHp, sim.Find(b)!.Value.Hp);

        sim.Run(20);
        Assert.True(sim.Find(b) is null || sim.Find(b)!.Value.Hp < GameRules.Units[UnitType.Soldier].MaxHp);
    }

    [Fact]
    public void Towers_hold_fire_during_treaty()
    {
        var sim = New(Kit, null, treaty: 40);
        var tower = sim.BuildingsOf(0).Single(b => b.Type == BuildingType.GuardTower);
        var intruder = sim.SpawnUnit(1, UnitType.Soldier, tower.X + 1, tower.Y);

        sim.Run(30);

        Assert.Equal(GameRules.Units[UnitType.Soldier].MaxHp, sim.Find(intruder)!.Value.Hp);
    }

    [Fact]
    public void Combat_score_counts_damage_and_kills_only()
    {
        var sim = New(Kit, Kit, combat: true);
        Assert.All(sim.GetPlayerView(0, sinceTick: 0).Players, p => Assert.Equal(0, p.Score.Total));

        sim.ClearArmies();
        var a = sim.SpawnUnit(0, UnitType.Soldier, 15, 15);
        var b = sim.SpawnUnit(1, UnitType.Worker, 16, 15);
        sim.SubmitOne(0, TestGames.AttackTarget(b, a)).AssertAccepted();
        sim.Run(20);

        var me = sim.GetPlayerView(0, sinceTick: 0).Players[0].Score;
        Assert.True(me.Total > GameRules.Units[UnitType.Worker].Cost);
        Assert.Equal(me.Destruction, me.Total);
        Assert.Equal(0, me.Economy);
        Assert.Equal(0, me.Survival);
    }
}
