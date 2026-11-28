namespace NetRts.Engine;

/// <summary>
/// How units move over the tile grid: in 8 directions, diagonal steps costing the same as straight ones
/// (Chebyshev metric) but never cutting the corner of a blocked tile. <see cref="PathSearch"/> plans routes.
/// </summary>
internal static class Pathfinder
{
    internal static readonly (int Dx, int Dy)[] Directions =
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
}

/// <summary>
/// One match's A* searches over its grid of blocked tiles (call <see cref="BlockedChanged"/> when it changes).
/// The working arrays are reused from search to search, so a short search costs what it explores, not the
/// size of the map.
/// </summary>
internal sealed class PathSearch(int width, int height, bool[] blocked)
{
    private readonly int[] _cost = new int[width * height];
    private readonly int[] _cameFrom = new int[width * height];
    private readonly int[] _seen = new int[width * height]; // the search that last touched each tile; _cost is stale otherwise
    private readonly PriorityQueue<int, (int F, int H, int Seq)> _open = new();
    private int _search;

    // Regions: which tiles can reach which (-1 for blocked ones).
    private readonly int[] _region = new int[width * height];
    private readonly Queue<int> _fill = new();
    private bool _regionsStale = true;

    private bool Passable(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height && !blocked[y * width + x];

    private int CostAt(int i) => _seen[i] == _search ? _cost[i] : int.MaxValue;

    private static int Heuristic(int x, int y, Point target, int range) =>
        Math.Max(0, Math.Max(Math.Abs(x - target.X), Math.Abs(y - target.Y)) - range);

    public void BlockedChanged() => _regionsStale = true;

    /// <summary>
    /// Whether any tile reachable from <paramref name="start"/> is nearer the goal than it is: exactly when
    /// <see cref="Find"/> returns a path, since the search ends on the nearest tile it reaches and the start
    /// wins a tie. Much cheaper than finding out by searching. A start on a blocked tile has no region, so
    /// the answer there is yes: search and see.
    /// </summary>
    public bool CanGetCloser(Point start, Point target, int range) =>
        Heuristic(start.X, start.Y, target, range) > (Nearest(start, target, range) ?? 0);

    /// <summary>
    /// How near the goal (in <see cref="Heuristic"/> terms: 0 is within range) the nearest tile reachable from
    /// <paramref name="start"/> is; null when <paramref name="start"/> is blocked and so has no region.
    /// </summary>
    private int? Nearest(Point start, Point target, int range)
    {
        var s = start.Y * width + start.X;
        if (blocked[s])
        {
            return null;
        }

        if (_regionsStale)
        {
            Relabel();
        }

        // Ring by ring out from the target: the first ring with a tile of the start's region is the nearest.
        // The start's own ring has one, so this ends.
        var region = _region[s];
        for (var r = 0; ; r++)
        {
            for (var d = -r; d <= r; d++)
            {
                if (InRegion(target.X + d, target.Y - r, region) || InRegion(target.X + d, target.Y + r, region)
                    || InRegion(target.X - r, target.Y + d, region) || InRegion(target.X + r, target.Y + d, region))
                {
                    return Math.Max(0, r - range);
                }
            }
        }
    }

    private bool InRegion(int x, int y, int region) =>
        (uint)x < (uint)width && (uint)y < (uint)height && _region[y * width + x] == region;

    /// <summary>Flood-fills the regions, moving the way <see cref="Find"/> does.</summary>
    private void Relabel()
    {
        Array.Fill(_region, -1);
        var next = 0;
        for (var first = 0; first < _region.Length; first++)
        {
            if (blocked[first] || _region[first] >= 0)
            {
                continue;
            }

            _region[first] = next;
            _fill.Enqueue(first);
            while (_fill.TryDequeue(out var current))
            {
                var (x, y) = (current % width, current / width);
                foreach (var (dx, dy) in Pathfinder.Directions)
                {
                    var n = (y + dy) * width + x + dx;
                    if (CanStep(x, y, dx, dy) && _region[n] < 0)
                    {
                        _region[n] = next;
                        _fill.Enqueue(n);
                    }
                }
            }

            next++;
        }

        _regionsStale = false;
    }

    /// <summary>Pathfinder.Neighbors' rule: onto a passable tile, without cutting the corner of a blocked one.</summary>
    private bool CanStep(int x, int y, int dx, int dy) =>
        Passable(x + dx, y + dy) && (dx == 0 || dy == 0 || (Passable(x + dx, y) && Passable(x, y + dy)));

    /// <summary>
    /// Finds a path from <paramref name="start"/> to any passable tile within Chebyshev
    /// <paramref name="range"/> of <paramref name="target"/>. If no such tile is reachable the
    /// path leads to the reachable tile closest to the target. The start tile is not included;
    /// an empty list means "already as close as possible".
    /// </summary>
    public List<Point> Find(Point start, Point target, int range)
    {
        int Heuristic(int x, int y) => PathSearch.Heuristic(x, y, target, range);

        if (Heuristic(start.X, start.Y) == 0)
        {
            return [];
        }

        // Tiles come off the queue nearest-first by cost so far plus distance left, so the first one as near as
        // this region gets is the one the search would end on anyway: stop there rather than search it all.
        var nearest = Nearest(start, target, range) ?? 0;

        if (++_search == int.MaxValue)
        {
            Array.Clear(_seen);
            _search = 1;
        }

        var size = width * height;
        var startIndex = start.Y * width + start.X;
        _seen[startIndex] = _search;
        _cost[startIndex] = 0;

        _open.Clear();
        var seq = 0;
        var startH = Heuristic(start.X, start.Y);
        _open.Enqueue(startIndex, (startH, startH, seq++));

        var bestIndex = startIndex;
        var bestH = startH;
        var bestCost = 0;
        var expansions = 0;

        while (_open.TryDequeue(out var current, out var priority))
        {
            var g = _cost[current];
            if (priority.F - priority.H != g)
            {
                continue; // stale queue entry
            }

            var (x, y) = (current % width, current / width);
            var h = priority.H;
            if (h < bestH || (h == bestH && g < bestCost))
            {
                bestIndex = current;
                bestH = h;
                bestCost = g;
            }

            if (h <= nearest || ++expansions > size)
            {
                break;
            }

            foreach (var (dx, dy) in Pathfinder.Directions) // Pathfinder.Neighbors' order
            {
                var (nx, ny) = (x + dx, y + dy);
                if (!CanStep(x, y, dx, dy))
                {
                    continue;
                }

                var ni = ny * width + nx;
                var ng = g + 1;
                if (ng >= CostAt(ni))
                {
                    continue;
                }

                _seen[ni] = _search;
                _cost[ni] = ng;
                _cameFrom[ni] = current;
                var nh = Heuristic(nx, ny);
                _open.Enqueue(ni, (ng + nh, nh, seq++));
            }
        }

        var path = new List<Point>();
        for (var i = bestIndex; i != startIndex; i = _cameFrom[i])
        {
            path.Add(new Point(i % width, i / width));
        }

        path.Reverse();
        return path;
    }
}
