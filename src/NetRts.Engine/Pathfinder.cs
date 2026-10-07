namespace NetRts.Engine;

/// <summary>
/// 8-directional A* over the tile grid. Diagonal steps cost the same as straight ones
/// (Chebyshev metric) but may not cut the corner of a blocked tile.
/// </summary>
internal static class Pathfinder
{
    private static readonly (int Dx, int Dy)[] Directions =
    [
        (0, -1), (1, 0), (0, 1), (-1, 0),
        (1, -1), (1, 1), (-1, 1), (-1, -1),
    ];

    public static IEnumerable<Point> Neighbors(Point p, Func<int, int, bool> passable)
    {
        foreach (var (dx, dy) in Directions)
        {
            var nx = p.X + dx;
            var ny = p.Y + dy;
            if (!passable(nx, ny))
            {
                continue;
            }

            if (dx != 0 && dy != 0 && (!passable(p.X + dx, p.Y) || !passable(p.X, p.Y + dy)))
            {
                continue;
            }

            yield return new Point(nx, ny);
        }
    }

    /// <summary>
    /// Finds a path from <paramref name="start"/> to any passable tile within Chebyshev
    /// <paramref name="range"/> of <paramref name="target"/>. If no such tile is reachable the
    /// path leads to the reachable tile closest to the target. The start tile is not included;
    /// an empty list means "already as close as possible".
    /// </summary>
    public static List<Point> FindPath(int width, int height, Func<int, int, bool> passable, Point start, Point target, int range)
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

        var bestIndex = startIndex;
        var bestH = Heuristic(start);
        var bestCost = 0;
        var expansions = 0;

        while (open.TryDequeue(out var current, out var priority))
        {
            var g = cost[current];
            if (priority.F - priority.H != g)
            {
                continue; // stale queue entry
            }

            var p = new Point(current % width, current / width);
            var h = priority.H;
            if (h < bestH || (h == bestH && g < bestCost))
            {
                bestIndex = current;
                bestH = h;
                bestCost = g;
            }

            if (h == 0 || ++expansions > size)
            {
                break;
            }

            foreach (var n in Neighbors(p, passable))
            {
                var ni = n.Y * width + n.X;
                var ng = g + 1;
                if (ng >= cost[ni])
                {
                    continue;
                }

                cost[ni] = ng;
                cameFrom[ni] = current;
                var nh = Heuristic(n);
                open.Enqueue(ni, (ng + nh, nh, seq++));
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
