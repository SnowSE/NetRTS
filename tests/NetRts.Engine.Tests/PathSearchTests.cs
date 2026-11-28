namespace NetRts.Engine.Tests;

/// <summary>
/// The pathfinder's shortcuts (reused arrays, region checks, stopping at the nearest reachable tile) must find
/// exactly the paths a plain A* search does: units move the same, so replays and the bot hashes hold.
/// </summary>
public class PathSearchTests
{
    [Fact]
    public void Finds_exactly_the_paths_a_plain_search_does_on_random_maps()
    {
        var random = new Random(2027);
        var checkedPaths = 0;
        var walledOff = 0;
        for (var map = 0; map < 60; map++)
        {
            var (width, height) = (random.Next(6, 30), random.Next(6, 30));
            var blocked = new bool[width * height];
            for (var i = 0; i < blocked.Length; i++)
            {
                blocked[i] = random.Next(100) < 10 + map % 4 * 10; // 10% to 40% blocked
            }

            var search = new PathSearch(width, height, blocked);
            for (var query = 0; query < 150; query++)
            {
                if (random.Next(4) == 0)
                {
                    // Buildings come and go between searches, sometimes walling a goal in.
                    var i = random.Next(blocked.Length);
                    blocked[i] = !blocked[i];
                    search.BlockedChanged();
                }

                var start = new Point(random.Next(width), random.Next(height));
                var target = new Point(random.Next(width), random.Next(height));
                var range = random.Next(4);
                bool Passable(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && !blocked[y * width + x];

                var expected = PlainSearch(width, height, Passable, start, target, range);

                Assert.Equal(expected, search.Find(start, target, range));
                var unsure = blocked[start.Y * width + start.X] && start.Chebyshev(target) > range; // no region to judge by
                Assert.Equal(expected.Count > 0 || unsure, search.CanGetCloser(start, target, range));
                checkedPaths++;
                if (expected.Count == 0 || Math.Max(0, expected[^1].Chebyshev(target) - range) > 0)
                {
                    walledOff++;
                }
            }
        }

        Assert.True(walledOff > checkedPaths / 10, $"only {walledOff} of {checkedPaths} searches couldn't reach their goal");
    }

    /// <summary>The pathfinder as it was before the shortcuts: a fresh A* over the whole map every time.</summary>
    private static List<Point> PlainSearch(int width, int height, Func<int, int, bool> passable, Point start, Point target, int range)
    {
        int Heuristic(Point p) => Math.Max(0, p.Chebyshev(target) - range);

        if (Heuristic(start) == 0)
        {
            return [];
        }

        var size = width * height;
        var cameFrom = new int[size];
        var cost = new int[size];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(cameFrom, -1);
        var startIndex = start.Y * width + start.X;
        cost[startIndex] = 0;
        var open = new PriorityQueue<int, (int F, int H, int Seq)>();
        var seq = 0;
        open.Enqueue(startIndex, (Heuristic(start), Heuristic(start), seq++));
        var (bestIndex, bestH, bestCost, expansions) = (startIndex, Heuristic(start), 0, 0);
        while (open.TryDequeue(out var current, out var priority))
        {
            var g = cost[current];
            if (priority.F - priority.H != g)
            {
                continue;
            }

            var p = new Point(current % width, current / width);
            var h = priority.H;
            if (h < bestH || (h == bestH && g < bestCost))
            {
                (bestIndex, bestH, bestCost) = (current, h, g);
            }

            if (h == 0 || ++expansions > size)
            {
                break;
            }

            foreach (var n in Pathfinder.Neighbors(p, passable))
            {
                var ni = n.Y * width + n.X;
                if (g + 1 >= cost[ni])
                {
                    continue;
                }

                cost[ni] = g + 1;
                cameFrom[ni] = current;
                open.Enqueue(ni, (g + 1 + Heuristic(n), Heuristic(n), seq++));
            }
        }

        var path = new List<Point>();
        for (var i = bestIndex; i != startIndex; i = cameFrom[i])
        {
            path.Add(new Point(i % width, i / width));
        }

        path.Reverse();
        return path;
    }
}
