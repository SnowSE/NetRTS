using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class RallyIncomeMemoryTests
{
    [Fact]
    public void Produced_units_walk_to_the_rally_point()
    {
        var sim = TestGames.New();
        var barracks = sim.SpawnBuilding(0, BuildingType.Barracks, 12, 12);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Produce, BuildingId = barracks, UnitType = UnitType.Scout, X = 16, Y = 9 }).AssertAccepted();
        sim.Run(GameRules.Units[UnitType.Scout].BuildTicks + 6);

        var scout = Assert.Single(sim.GetPlayerView(0).Units, u => u.Type == UnitType.Scout);
        Assert.Equal((16, 9), (scout.X, scout.Y));
        Assert.Equal(new PositionDto(16, 9), sim.GetPlayerView(0).Buildings.Single(b => b.Id == barracks).Rally);
    }

    [Fact]
    public void Workers_rallied_onto_ore_start_mining()
    {
        var sim = TestGames.New();
        var cc = sim.CommandCenterId(0);
        var ore = sim.GetPlayerView(0).Resources.First();

        sim.Submit(0,
        [
            new CommandRequest { Type = CommandType.Rally, BuildingId = cc, X = ore.X, Y = ore.Y },
            new CommandRequest { Type = CommandType.Produce, BuildingId = cc, UnitType = UnitType.Worker },
        ]).AssertAccepted();
        sim.Run(GameRules.Units[UnitType.Worker].BuildTicks + 8);

        var newest = sim.GetPlayerView(0).Units.Where(u => u.Type == UnitType.Worker).MaxBy(u => u.Id)!;
        Assert.Equal(ore.Id, newest.TargetId);
        Assert.Contains(newest.Activity, new[] { UnitActivity.Gathering, UnitActivity.Returning, UnitActivity.Moving });
    }

    [Fact]
    public void Only_buildings_that_train_units_take_a_rally_point()
    {
        var sim = TestGames.New();
        var lab = sim.SpawnBuilding(0, BuildingType.TechLab, 12, 12);

        var code = sim.SubmitOne(0, new CommandRequest { Type = CommandType.Rally, BuildingId = lab, X = 14, Y = 14 }).AssertRejected();

        Assert.Equal("CANNOT_RALLY", code);
    }

    [Fact]
    public void Income_counts_ore_banked_in_the_last_sixty_ticks()
    {
        var sim = TestGames.New();
        var ore = sim.GetPlayerView(0).Resources.OrderBy(r => Math.Abs(r.X - 7) + Math.Abs(r.Y - 7)).First();
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Gather, Units = "all", TargetId = ore.Id }).AssertAccepted();

        sim.Run(60);
        var view = sim.GetPlayerView(0);
        Assert.True(view.You.IncomePerMinute > 0);
        Assert.Equal(view.Players[0].Score.Economy, view.You.IncomePerMinute); // everything so far was within the window

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Stop, Units = "all" }).AssertAccepted();
        sim.Run(61);
        Assert.Equal(0, sim.GetPlayerView(0).You.IncomePerMinute);
        Assert.Equal(sim.GetPlayerView(0).You.IncomePerMinute, sim.GetSpectatorView().Players[0].IncomePerMinute);
    }

    [Fact]
    public void Enemy_buildings_are_remembered_after_they_slip_into_the_fog()
    {
        var sim = TestGames.New();
        var tower = sim.SpawnBuilding(1, BuildingType.Barracks, 30, 30);
        var scout = sim.SpawnUnit(0, UnitType.Scout, 28, 30);
        sim.Step();
        Assert.Contains(sim.GetPlayerView(0).Buildings, b => b.Id == tower);
        Assert.Empty(sim.GetPlayerView(0).RememberedBuildings);

        sim.SubmitOne(0, TestGames.Move(16, 16, scout)).AssertAccepted();
        sim.Run(10);

        var view = sim.GetPlayerView(0);
        Assert.DoesNotContain(view.Buildings, b => b.Id == tower);
        var memory = Assert.Single(view.RememberedBuildings);
        Assert.Equal((tower, BuildingType.Barracks, 30, 30), (memory.Id, memory.Type, memory.X, memory.Y));
        Assert.True(memory.LastSeenTick < view.Tick);
    }

    [Fact]
    public void A_remembered_building_is_forgotten_once_you_see_it_is_gone()
    {
        var sim = TestGames.New();
        var barracks = sim.SpawnBuilding(1, BuildingType.Barracks, 30, 30);
        var scout = sim.SpawnUnit(0, UnitType.Scout, 28, 30);
        sim.Step();
        sim.SubmitOne(0, TestGames.Move(16, 16, scout)).AssertAccepted();
        sim.Run(10);
        sim.RemoveEntity(barracks); // destroyed while nobody was looking

        Assert.Single(sim.GetPlayerView(0).RememberedBuildings); // still believed to be there

        sim.SubmitOne(0, TestGames.Move(28, 30, scout)).AssertAccepted();
        sim.Run(10);
        Assert.Empty(sim.GetPlayerView(0).RememberedBuildings);
    }

    [Fact]
    public void Different_seeds_produce_different_base_layouts()
    {
        var layouts = Enumerable.Range(1, 12)
            .Select(seed => string.Join(';', MapGenerator.Generate(64, 64, seed, 2).Deposits
                .Where(d => d.Position.X < 10 && d.Position.Y < 12)
                .Select(d => d.Position)))
            .Distinct()
            .Count();

        Assert.True(layouts >= 3, $"only {layouts} distinct base layouts across 12 seeds");
    }
}
