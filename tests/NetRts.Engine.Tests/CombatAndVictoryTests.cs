using NetRts.Protocol;

namespace NetRts.Engine.Tests;

public class CombatAndVictoryTests
{
    [Fact]
    public void Units_chase_and_attack_a_target_until_it_dies()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 10, 10);
        var victim = sim.SpawnUnit(1, UnitType.Worker, 13, 10);

        sim.SubmitOne(0, TestGames.AttackTarget(victim, soldier)).AssertAccepted();
        sim.Run(15);

        Assert.Null(sim.Find(victim));
        var view = sim.GetPlayerView(0, sinceTick: 0);
        Assert.Contains(view.Events, e => e.Kind == "UnitKilled" && e.EntityId == victim);
        Assert.Equal(GameRules.Units[UnitType.Worker].Cost, view.Players[0].Score.Destruction);
        Assert.Equal(UnitActivity.Idle, view.Units.Single(u => u.Id == soldier).Activity);
    }

    [Fact]
    public void Damage_is_reduced_by_armor_and_increased_by_weapon_upgrades()
    {
        var sim = TestGames.New();
        var attacker = sim.SpawnUnit(0, UnitType.Soldier, 10, 10);
        var target = sim.SpawnUnit(1, UnitType.Soldier, 11, 10);
        sim.SetHp(target, 1000);
        sim.SetHp(attacker, 1000);

        sim.SubmitOne(0, TestGames.AttackTarget(target, attacker)).AssertAccepted();
        sim.Step();
        Assert.Equal(1000 - (9 - 1), sim.Find(target)!.Value.Hp);

        sim.GrantUpgrade(0, UpgradeType.Weapons1);
        sim.GrantUpgrade(1, UpgradeType.Armor1);
        sim.Step();
        Assert.Equal(1000 - 8 - (9 + 2 - 2), sim.Find(target)!.Value.Hp);
    }

    [Fact]
    public void Attacks_in_the_same_tick_land_simultaneously()
    {
        var sim = TestGames.New();
        var a = sim.SpawnUnit(0, UnitType.Soldier, 10, 10);
        var b = sim.SpawnUnit(1, UnitType.Soldier, 11, 10);
        sim.SetHp(a, 5);
        sim.SetHp(b, 5);

        sim.SubmitOne(0, TestGames.AttackTarget(b, a)).AssertAccepted();
        sim.SubmitOne(1, TestGames.AttackTarget(a, b)).AssertAccepted();
        sim.Step();

        Assert.Null(sim.Find(a));
        Assert.Null(sim.Find(b));
    }

    [Fact]
    public void Idle_soldiers_defend_themselves_but_idle_workers_do_not()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 12, 12);
        var worker = sim.SpawnUnit(0, UnitType.Worker, 14, 14);
        var intruder = sim.SpawnUnit(1, UnitType.Scout, 13, 12);
        var bystander = sim.SpawnUnit(1, UnitType.Scout, 15, 14);

        sim.Step();

        Assert.True(sim.Find(intruder)!.Value.Hp < GameRules.Units[UnitType.Scout].MaxHp);
        Assert.Equal(GameRules.Units[UnitType.Scout].MaxHp, sim.Find(bystander)!.Value.Hp);
        Assert.NotNull(sim.Find(worker));
        Assert.NotNull(sim.Find(soldier));
    }

    [Fact]
    public void Attack_move_engages_enemies_seen_along_the_way()
    {
        var sim = TestGames.New();
        var soldier = sim.SpawnUnit(0, UnitType.Soldier, 9, 12);
        var enemy = sim.SpawnUnit(1, UnitType.Worker, 13, 14);

        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Attack, UnitIds = [soldier], X = 16, Y = 12 }).AssertAccepted();
        sim.Run(20);

        Assert.Null(sim.Find(enemy));
    }

    [Fact]
    public void Guard_towers_shoot_enemy_units_in_range()
    {
        var sim = TestGames.New();
        sim.SpawnBuilding(0, BuildingType.GuardTower, 12, 12);
        var scout = sim.SpawnUnit(1, UnitType.Scout, 16, 12);

        sim.Step();

        Assert.Equal(GameRules.Units[UnitType.Scout].MaxHp - 10, sim.Find(scout)!.Value.Hp);
    }

    [Fact]
    public void Destroying_the_last_command_center_eliminates_the_player_and_ends_the_match()
    {
        var sim = TestGames.New();
        var enemyCc = sim.CommandCenterId(1);
        var squad = Enumerable.Range(0, 3).Select(i => sim.SpawnUnit(0, UnitType.Soldier, 55, 53 + i)).ToArray();
        sim.SetHp(enemyCc, 30);

        sim.SubmitOne(0, TestGames.AttackTarget(enemyCc, squad)).AssertAccepted();
        sim.Run(10);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        Assert.Equal(MatchEndReason.Elimination, sim.Outcome!.Reason);
        Assert.Equal(TestGames.P1, sim.Outcome.WinnerId);
        var result = sim.GetResult()!;
        Assert.True(result.Players.Single(p => p.Slot == 0).Winner);
        Assert.Equal(1, result.Players.Single(p => p.Slot == 0).BuildingsDestroyed);
        Assert.Contains(sim.GetPlayerView(1, sinceTick: 0).Events, e => e.Kind == "PlayerEliminated");
    }

    [Fact]
    public void At_the_time_limit_the_higher_score_wins()
    {
        var sim = TestGames.New(maxTicks: 5);
        sim.SpawnUnit(0, UnitType.Soldier, 12, 12); // more surviving value

        sim.Run(5);

        Assert.Equal(MatchStatus.Completed, sim.Status);
        Assert.Equal(MatchEndReason.TimeLimit, sim.Outcome!.Reason);
        Assert.Equal(TestGames.P1, sim.Outcome.WinnerId);
        Assert.Equal(5, sim.Outcome.Ticks);
    }

    [Fact]
    public void Equal_scores_at_the_time_limit_are_a_draw()
    {
        var sim = TestGames.New(maxTicks: 3);

        sim.Run(10);

        Assert.Equal(MatchEndReason.TimeLimit, sim.Outcome!.Reason);
        Assert.Null(sim.Outcome.WinnerId);
        Assert.Equal(3, sim.Tick);
    }

    [Fact]
    public void Commands_are_rejected_once_the_match_is_over()
    {
        var sim = TestGames.New(maxTicks: 1);
        sim.Step();
        var worker = sim.UnitIds(0).First();

        Assert.Equal("MATCH_NOT_ACTIVE", sim.SubmitOne(0, TestGames.Move(10, 10, worker)).AssertRejected());
    }

    [Fact]
    public void Surrender_hands_the_win_to_the_opponent()
    {
        var sim = TestGames.New();

        sim.Surrender(1);

        Assert.Equal(MatchEndReason.Surrender, sim.Outcome!.Reason);
        Assert.Equal(TestGames.P1, sim.Outcome.WinnerId);
    }

    [Fact]
    public void Four_player_matches_continue_until_one_player_remains()
    {
        var seats = Enumerable.Range(1, 4).Select(i => new PlayerSeat(Guid.NewGuid(), $"p{i}")).ToList();
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { Seed = 3 }, seats);

        sim.Surrender(1);
        Assert.Equal(MatchStatus.Active, sim.Status);
        Assert.Empty(sim.UnitIds(1));
        sim.Surrender(2);
        sim.Surrender(3);

        Assert.Equal(seats[0].Id, sim.Outcome!.WinnerId);
    }
}
