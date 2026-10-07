using NetRts.Protocol;

namespace NetRts.Engine.Tests;

internal static class TestGames
{
    public static readonly Guid P1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid P2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>
    /// A 64x64 two-player game. Slot 0's base is at (7,7); everything within 9 tiles of it is
    /// guaranteed rock-free, and (8..16, 8..16) is also clear of ore — a safe sandbox for scenarios.
    /// </summary>
    public static GameSimulation New(int seed = 1, int maxTicks = 1800, int queueCapacity = 500, int commandsPerTick = 100) =>
        new(Guid.NewGuid(), new GameConfig { Seed = seed, MaxTicks = maxTicks, QueueCapacity = queueCapacity, CommandsPerTick = commandsPerTick },
            [new PlayerSeat(P1, "alpha"), new PlayerSeat(P2, "bravo")]);

    public static CommandRequest Move(int x, int y, params int[] unitIds) =>
        new() { Type = CommandType.Move, UnitIds = unitIds, X = x, Y = y };

    public static CommandRequest AttackTarget(int targetId, params int[] unitIds) =>
        new() { Type = CommandType.Attack, UnitIds = unitIds, TargetId = targetId };

    public static SubmitCommandsResponse SubmitOne(this GameSimulation sim, int slot, CommandRequest command) =>
        sim.Submit(slot, [command]);

    public static void AssertAccepted(this SubmitCommandsResponse response)
    {
        var rejected = response.Results.FirstOrDefault(r => !r.Accepted);
        Assert.True(rejected is null, $"Expected acceptance but got {rejected?.Error?.Code}: {rejected?.Error?.Message}");
    }

    public static string AssertRejected(this SubmitCommandsResponse response)
    {
        var result = Assert.Single(response.Results);
        Assert.False(result.Accepted, "Expected the command to be rejected.");
        return result.Error!.Code;
    }
}
