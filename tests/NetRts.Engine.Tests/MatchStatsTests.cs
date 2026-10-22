using NetRts.Protocol;

namespace NetRts.Engine.Tests;

/// <summary>The per-player numbers in a match result (PlayerResultDto.Stats), used for achievements and recaps.</summary>
public class MatchStatsTests
{
    [Fact]
    public void A_full_match_reports_what_each_player_built_trained_and_researched()
    {
        var sim = FullMatchTests.Play("balanced", "sitter", seed: 2);
        var result = sim.GetResult()!;
        var winner = result.Players.Single(p => p.Winner).Stats!;
        var loser = result.Players.Single(p => !p.Winner).Stats!;

        Assert.True(winner.UnitsTrained.Values.Sum() > 0);
        Assert.Equal(result.Players.Single(p => p.Winner).UnitsProduced, winner.UnitsTrained.Values.Sum());
        Assert.True(winner.BuildingsCompleted.GetValueOrDefault(BuildingType.Barracks) > 0);
        Assert.NotNull(winner.FirstBarracksTick);
        Assert.NotNull(winner.FirstCombatUnitTick);
        Assert.True(winner.FirstBarracksTick < winner.FirstCombatUnitTick);
        Assert.True(winner.PeakWorkers >= GameRules.StartingWorkers);
        Assert.True(winner.PeakResources >= GameRules.StartingResources);
        Assert.True(winner.CommandsQueued > 0);
        Assert.NotNull(winner.EnemyCommandCenterSeenTick);
        Assert.True(winner.CommandCentersDestroyed > 0);
        Assert.False(winner.SerumTaken);

        // The loser was eliminated: no Command Center left, and it fell behind on score along the way.
        Assert.Null(loser.CommandCenterHealthPercent);
        Assert.True(loser.LowestScoreSharePercent < 100);
        Assert.Equal(100, winner.LowestScoreSharePercent);
    }

    [Fact]
    public void Taking_the_serum_and_its_kills_are_counted()
    {
        var sim = FullMatchTests.Play("rusher", "sitter", seed: 1, boostAt: 40);
        var stats = sim.GetResult()!.Players[0].Stats!;

        Assert.True(stats.SerumTaken);
        Assert.False(sim.GetResult()!.Players[1].Stats!.SerumTaken);
        Assert.True(stats.SerumKills <= sim.GetResult()!.Players[0].UnitsKilled);
    }

    [Fact]
    public void Rejected_and_failed_commands_are_counted()
    {
        var sim = TestGames.New();
        sim.SubmitOne(0, TestGames.Move(10, 10, 999_999)).AssertRejected();
        sim.Step();

        var stats = StatsAtEnd(sim);
        Assert.Equal(1, stats.CommandsRejected);
        Assert.Equal(0, stats.CommandsQueued);
    }

    [Fact]
    public void Idle_workers_and_rally_points_are_tracked()
    {
        var sim = TestGames.New(maxTicks: 30);
        var commandCenter = sim.GetPlayerView(0).Buildings.Single().Id;
        sim.SubmitOne(0, new CommandRequest { Type = CommandType.Rally, BuildingId = commandCenter, X = 12, Y = 12 }).AssertAccepted();
        while (sim.Status == MatchStatus.Active)
        {
            sim.Step();
        }

        var stats = sim.GetResult()!.Players[0].Stats!;
        Assert.Equal(1, stats.BuildingsRallied);
        Assert.Equal(30, stats.LongestWorkerIdleTicks);
        Assert.Equal(100, stats.CommandCenterHealthPercent);
    }

    [Fact]
    public void A_replay_reproduces_the_stats()
    {
        var original = FullMatchTests.Play("rusher", "economist", seed: 12);
        var replay = GameSimulation.FromReplay(original.GetReplay());

        var a = original.GetResult()!.Players.Select(p => p.Stats! with { CommandsRejected = 0 }).ToList();
        var b = replay.GetResult()!.Players.Select(p => p.Stats! with { CommandsRejected = 0 }).ToList();
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].UnitsTrained, b[i].UnitsTrained);
            Assert.Equal(a[i].BuildingsCompleted, b[i].BuildingsCompleted);
            Assert.Equal(a[i].Upgrades, b[i].Upgrades);
            Assert.Equal(a[i] with { UnitsTrained = b[i].UnitsTrained, BuildingsCompleted = b[i].BuildingsCompleted, Upgrades = b[i].Upgrades }, b[i]);
        }
    }

    /// <summary>Ends the match by surrender so there is a result to read.</summary>
    private static PlayerMatchStatsDto StatsAtEnd(GameSimulation sim)
    {
        sim.Surrender(1);
        return sim.GetResult()!.Players[0].Stats!;
    }
}
