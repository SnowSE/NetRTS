namespace NetRts.Engine.Tests;

/// <summary>Maps for five to sixteen players: bases on a ring, every share alike.</summary>
public class RingMapTests
{
    public static TheoryData<int> PlayerCounts => new(5, 6, 7, 8, 12, 16);

    private static int SizeFor(int players) => Math.Min(MapGenerator.MaxSize, (MapGenerator.MinSizeFor(players) + 16 + 7) / 8 * 8);

    [Fact]
    public void A_sixteen_player_map_fits_the_largest_map_size()
    {
        Assert.InRange(MapGenerator.MinSizeFor(16), MapGenerator.MinSize, MapGenerator.MaxSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(64, 64, seed: 1, playerCount: 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(128, 128, seed: 1, playerCount: 17));
    }

    [Theory]
    [MemberData(nameof(PlayerCounts))]
    public void Every_player_gets_a_separate_open_start_with_the_same_base_ore(int players)
    {
        var size = SizeFor(players);
        var map = MapGenerator.Generate(size, size, seed: 3, players);

        Assert.Equal(players, map.StartPositions.Count);
        Assert.Equal(players, map.StartPositions.Distinct().Count());
        foreach (var start in map.StartPositions)
        {
            Assert.True(map.InBounds(start));
            Assert.False(map.IsRock(start));
            Assert.DoesNotContain(map.Deposits, d => d.Position == start);
            foreach (var other in map.StartPositions.Where(o => o != start))
            {
                Assert.True(start.Chebyshev(other) >= 11, $"{start} and {other} are too close");
            }
        }

        var baseOre = map.StartPositions
            .Select(s => map.Deposits.Where(d => d.Position.Chebyshev(s) <= 8).Sum(d => d.Amount))
            .ToList();
        Assert.All(baseOre, ore => Assert.Equal(baseOre[0], ore));
        Assert.True(baseOre[0] >= 9_000, $"each base should have its nine-deposit seam, got {baseOre[0]}");
    }

    [Fact]
    public void Base_ore_is_equal_for_every_player_count_and_map_size()
    {
        for (var players = 5; players <= MapGenerator.MaxPlayers; players++)
        {
            for (var size = MapGenerator.MinSizeFor(players); size <= MapGenerator.MaxSize; size += 5)
            {
                var map = MapGenerator.Generate(size, size, seed: size, players);
                var seams = map.StartPositions
                    .Select(s => map.Deposits.Count(d => d.Amount == 1000 && d.Position.Chebyshev(s) <= 8))
                    .ToList();
                Assert.True(seams.All(n => n == 9), $"{players} players on {size}×{size}: seams of {string.Join(",", seams)}");
            }
        }
    }

    [Fact]
    public void Ring_maps_are_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(120, 120, seed: 42, playerCount: 16).ToDto();
        var b = MapGenerator.Generate(120, 120, seed: 42, playerCount: 16).ToDto();
        var c = MapGenerator.Generate(120, 120, seed: 43, playerCount: 16).ToDto();

        Assert.Equal(a.Terrain, b.Terrain);
        Assert.Equal(a.StartPositions, b.StartPositions);
        Assert.NotEqual(a.Terrain, c.Terrain);
    }

    [Fact]
    public void A_sixteen_player_match_runs()
    {
        var seats = Enumerable.Range(0, 16).Select(i => new PlayerSeat(Guid.NewGuid(), $"p{i}")).ToList();
        var sim = new GameSimulation(Guid.NewGuid(), new GameConfig { MapWidth = 120, MapHeight = 120, Seed = 7 }, seats);

        for (var i = 0; i < 20; i++)
        {
            sim.Step();
        }

        var view = sim.GetSpectatorView(includeVisibility: false);
        Assert.Equal(16, view.Players.Count);
        Assert.All(Enumerable.Range(0, 16), slot =>
            Assert.Contains(view.Buildings, b => b.Owner == slot && b.Type == Protocol.BuildingType.CommandCenter));
    }
}
