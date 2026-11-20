namespace NetRts.Engine.Tests;

/// <summary>The grubPercent match setting: every deposit scaled, the same map otherwise, and replays that keep it.</summary>
public class GrubAmountTests
{
    [Theory]
    [InlineData(2, 64)]
    [InlineData(8, 96)]
    public void Every_deposit_scales_and_nothing_else_moves(int players, int size)
    {
        var usual = MapGenerator.Generate(size, size, seed: 5, players);
        var rich = MapGenerator.Generate(size, size, seed: 5, players, grubPercent: 200);
        var scarce = MapGenerator.Generate(size, size, seed: 5, players, grubPercent: 50);

        Assert.Equal(usual.StartPositions, rich.StartPositions);
        Assert.Equal(usual.Deposits.Select(d => d.Position), rich.Deposits.Select(d => d.Position));
        Assert.Equal(usual.Deposits.Select(d => d.Amount * 2), rich.Deposits.Select(d => d.Amount));
        Assert.Equal(usual.Deposits.Select(d => (d.Amount + 1) / 2), scarce.Deposits.Select(d => d.Amount));
        Assert.Equal(usual.ToDto().Terrain, scarce.ToDto().Terrain);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(401)]
    public void Amounts_outside_a_quarter_to_four_times_are_refused(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(64, 64, seed: 1, playerCount: 2, grubPercent: percent));

    [Fact]
    public void A_replay_of_a_gold_rush_plays_out_the_same()
    {
        var original = FullMatchTests.Play("economist", "balanced", seed: 4, maxTicks: 900, grubPercent: 400);
        var replay = original.GetReplay();
        Assert.Equal(400, replay.GrubPercent);

        var again = GameSimulation.FromReplay(replay);
        Assert.Equal(original.StateHash(), again.StateHash());
        Assert.Null(FullMatchTests.Play("rusher", "sitter", seed: 4, maxTicks: 10).GetReplay().GrubPercent);   // the usual amount isn't written
    }
}
