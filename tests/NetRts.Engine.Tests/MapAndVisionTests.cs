using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class MapAndVisionTests
{
    [Fact]
    public void Map_generation_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(64, 64, seed: 42, playerCount: 2).ToDto();
        var b = MapGenerator.Generate(64, 64, seed: 42, playerCount: 2).ToDto();
        var c = MapGenerator.Generate(64, 64, seed: 43, playerCount: 2).ToDto();

        Assert.Equal(a.Terrain, b.Terrain);
        Assert.NotEqual(a.Terrain, c.Terrain);
    }

    [Theory]
    [InlineData(1, 64, 64)]
    [InlineData(7, 48, 40)]
    [InlineData(99, 32, 32)]
    [InlineData(5, 100, 100)]
    public void Map_is_mirror_symmetric_so_every_start_is_fair(int seed, int width, int height)
    {
        var map = MapGenerator.Generate(width, height, seed, playerCount: 4);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(map.IsRock(x, y), map.IsRock(width - 1 - x, y));
                Assert.Equal(map.IsRock(x, y), map.IsRock(x, height - 1 - y));
            }
        }

        var deposits = map.Deposits.Select(d => (d.Position, d.Amount)).ToHashSet();
        foreach (var d in map.Deposits)
        {
            Assert.Contains((new Point(width - 1 - d.Position.X, height - 1 - d.Position.Y), d.Amount), deposits);
        }
    }

    [Fact]
    public void New_match_gives_each_player_a_command_center_workers_and_starting_ore()
    {
        var sim = TestGames.New();

        var view = sim.GetPlayerView(0);

        Assert.Equal(GameRules.StartingResources, view.You.Resources);
        var cc = Assert.Single(view.Buildings, b => b.Owner == 0);
        Assert.Equal(BuildingType.CommandCenter, cc.Type);
        Assert.Equal(GameRules.StartingWorkers, view.Units.Count(u => u.Owner == 0 && u.Type == UnitType.Worker));
        Assert.NotEmpty(view.Resources); // base ore is within the Command Center's sight
        Assert.Equal(64, view.Visibility.Count);
        Assert.All(view.Visibility, row => Assert.Equal(64, row.Length));
    }

    [Fact]
    public void Fog_of_war_hides_the_enemy_base_at_the_start()
    {
        var sim = TestGames.New();

        var view = sim.GetPlayerView(0);

        Assert.DoesNotContain(view.Units, u => u.Owner == 1);
        Assert.DoesNotContain(view.Buildings, b => b.Owner == 1);
        Assert.Equal('0', view.Visibility[56][56]);
        Assert.Equal('1', view.Visibility[7][7]);
    }

    [Fact]
    public void A_unit_sees_exactly_its_vision_radius()
    {
        var sim = TestGames.New();
        sim.ClearArmies();
        var cc = sim.CommandCenterId(0);
        sim.RemoveEntity(cc); // only the soldier provides vision now
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 20, 20);
        var near = sim.SpawnUnit(1, UnitType.Worker, 25, 20); // distance 5 = soldier vision
        var far = sim.SpawnUnit(1, UnitType.Worker, 26, 20);  // distance 6

        var view = sim.GetPlayerView(0);

        Assert.Contains(view.Units, u => u.Id == soldier);
        Assert.Contains(view.Units, u => u.Id == near);
        Assert.DoesNotContain(view.Units, u => u.Id == far);
    }

    [Fact]
    public void Enemy_orders_and_production_are_never_revealed()
    {
        var sim = TestGames.New();
        var spy = sim.SpawnUnit(1, UnitType.Scout, 9, 9);
        sim.SubmitOne(1, TestGames.Move(30, 30, spy)).AssertAccepted();
        sim.Step();

        var enemy = Assert.Single(sim.GetPlayerView(0).Units, u => u.Id == spy);

        Assert.Null(enemy.Destination);
        Assert.Null(enemy.TargetId);
    }
}
