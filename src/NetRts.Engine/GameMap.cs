using NetRts.Protocol;

namespace NetRts.Engine;

public readonly record struct Point(int X, int Y)
{
    public int Chebyshev(Point other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int DistanceSquared(Point other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public override string ToString() => $"({X},{Y})";
}

public sealed record DepositSpawn(Point Position, int Amount);

/// <summary>Static terrain plus where things start. Immutable once generated.</summary>
public sealed class GameMap
{
    private readonly bool[] _rock;

    internal GameMap(int width, int height, bool[] rock, IReadOnlyList<Point> startPositions, IReadOnlyList<DepositSpawn> deposits)
    {
        Width = width;
        Height = height;
        _rock = rock;
        StartPositions = startPositions;
        Deposits = deposits;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<Point> StartPositions { get; }
    public IReadOnlyList<DepositSpawn> Deposits { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool InBounds(Point p) => InBounds(p.X, p.Y);

    public bool IsRock(int x, int y) => _rock[y * Width + x];

    public bool IsRock(Point p) => IsRock(p.X, p.Y);

    public MapDto ToDto()
    {
        var rows = new string[Height];
        var row = new char[Width];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                row[x] = IsRock(x, y) ? '#' : '.';
            }

            rows[y] = new string(row);
        }

        return new MapDto
        {
            Width = Width,
            Height = Height,
            Terrain = rows,
            StartPositions = StartPositions.Select(p => new PositionDto(p.X, p.Y)).ToList(),
        };
    }
}

/// <summary>
/// Builds a fair map. Up to four players start in the corners of a map with four-fold mirror symmetry:
/// features are generated in the top-left quadrant and mirrored into the other three. Five to sixteen
/// players start evenly spaced around a ring, and every base, expansion, contested deposit and rock
/// outcrop is repeated once per player around that ring.
/// </summary>
public static class MapGenerator
{
    public const int MinSize = 32;
    public const int MaxSize = 128;
    public const int MaxPlayers = 16;

    private const int BaseOreAmount = 1000;
    private const int ExpansionOreAmount = 1200;
    private const int RichOreAmount = 1500;

    /// <summary>Command Centers on a ring sit at least this far apart from their neighbours.</summary>
    private const int RingSpacing = 16;

    /// <summary>Tiles kept between a ring base and the map edge, for the ore seam behind it.</summary>
    private const int RingBackroom = 9;

    /// <summary>The smallest width and height a map for this many players can have.</summary>
    public static int MinSizeFor(int playerCount) =>
        playerCount <= 4 ? MinSize : 2 * ((int)Math.Ceiling(RingRadiusFor(playerCount)) + RingBackroom);

    private static double RingRadiusFor(int playerCount) => RingSpacing / 2.0 / Sin(Math.PI / playerCount);

    public static GameMap Generate(int width, int height, int seed, int playerCount)
    {
        if (width < MinSize || height < MinSize || width > MaxSize || height > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Map dimensions must be between {MinSize} and {MaxSize}.");
        }

        if (playerCount is < 2 or > MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), $"Matches support 2 to {MaxPlayers} players.");
        }

        if (playerCount > 4)
        {
            var min = MinSizeFor(playerCount);
            if (width < min || height < min)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"A {playerCount}-player map must be at least {min}×{min}.");
            }

            return GenerateRing(width, height, seed, playerCount);
        }

        var rng = new Rng(seed);
        var inset = Math.Min(width, height) >= 40 ? 7 : 6;
        var quadrantW = width / 2;
        var quadrantH = height / 2;

        // Slot order: top-left, bottom-right, top-right, bottom-left, so 2-player games face diagonally.
        var corners = new[]
        {
            new Point(inset, inset),
            new Point(width - 1 - inset, height - 1 - inset),
            new Point(width - 1 - inset, inset),
            new Point(inset, height - 1 - inset),
        };

        // Base ore: nine deposits five tiles behind the Command Center, in one of four layouts
        // chosen by the seed so maps differ from game to game. Every layout holds the same ore.
        var edge = inset - 5;
        var baseOre = rng.Next(0, 4) switch
        {
            0 => Enumerable.Range(inset - 3, 6).Select(y => new Point(edge, y))           // long side wall
                 .Concat(Enumerable.Range(inset - 2, 3).Select(x => new Point(x, edge))),
            1 => Enumerable.Range(inset - 3, 6).Select(x => new Point(x, edge))           // long top wall
                 .Concat(Enumerable.Range(inset - 2, 3).Select(y => new Point(edge, y))),
            2 => Enumerable.Range(edge, 5).Select(y => new Point(edge, y))                // tucked into the corner
                 .Concat(Enumerable.Range(edge + 1, 4).Select(x => new Point(x, edge))),
            _ => Enumerable.Range(inset - 4, 9).Select(y => new Point(edge, y)),          // one long seam
        };
        var quadrantDeposits = baseOre.Select(p => new DepositSpawn(p, BaseOreAmount)).ToList();

        var expansionMin = inset + 8;
        var expansionMaxX = quadrantW - 4;
        var expansionMaxY = quadrantH - 4;
        if (expansionMaxX - expansionMin >= 3 && expansionMaxY - expansionMin >= 3)
        {
            var ex = rng.Next(expansionMin, expansionMaxX);
            var ey = rng.Next(expansionMin, expansionMaxY);
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, 0) })
            {
                quadrantDeposits.Add(new DepositSpawn(new Point(ex + dx, ey + dy), ExpansionOreAmount));
            }
        }

        // Contested ore just off the centre (mirrors into N/S and W/E pairs).
        quadrantDeposits.Add(new DepositSpawn(new Point(quadrantW - 1, quadrantH - 4), RichOreAmount));
        quadrantDeposits.Add(new DepositSpawn(new Point(quadrantW - 4, quadrantH - 1), RichOreAmount));

        var deposits = Mirror(quadrantDeposits.Select(d => d.Position), width, height)
            .Distinct()
            .Select(p => new DepositSpawn(p, AmountAt(quadrantDeposits, p, width, height)))
            .OrderBy(d => d.Position.Y).ThenBy(d => d.Position.X)
            .ToList();

        var depositSet = deposits.Select(d => d.Position).ToHashSet();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var rock = new bool[width * height];
            var blobs = Math.Max(2, quadrantW * quadrantH / 175);
            for (var i = 0; i < blobs; i++)
            {
                var cx = rng.Next(0, quadrantW);
                var cy = rng.Next(0, quadrantH);
                var radius = rng.Next(1, 4);
                for (var y = cy - radius; y <= cy + radius; y++)
                {
                    for (var x = cx - radius; x <= cx + radius; x++)
                    {
                        if (x < 0 || y < 0 || x >= quadrantW || y >= quadrantH)
                        {
                            continue;
                        }

                        var p = new Point(x, y);
                        if (p.DistanceSquared(new Point(cx, cy)) > radius * radius + 1)
                        {
                            continue;
                        }

                        if (p.Chebyshev(corners[0]) <= 9 || quadrantDeposits.Any(d => d.Position.Chebyshev(p) <= 2))
                        {
                            continue;
                        }

                        foreach (var m in Mirror([p], width, height))
                        {
                            rock[m.Y * width + m.X] = true;
                        }
                    }
                }
            }

            if (IsConnected(rock, depositSet, width, height, corners))
            {
                return new GameMap(width, height, rock, corners.Take(playerCount).ToList(), deposits);
            }
        }

        return new GameMap(width, height, new bool[width * height], corners.Take(playerCount).ToList(), deposits);
    }

    /// <summary>
    /// Five to sixteen players around a ring, slot 0 at the top-left. Each player gets an identical
    /// share: a nine-deposit seam behind the base, an expansion and a contested deposit in each gap
    /// to the next player, and the same rock outcrops in every gap. Positions are rounded to tiles,
    /// so shares can differ by a tile, never by ore.
    /// </summary>
    private static GameMap GenerateRing(int width, int height, int seed, int playerCount)
    {
        var rng = new Rng(seed);
        var cx = (width - 1) / 2.0;
        var cy = (height - 1) / 2.0;
        var radius = Math.Min(width, height) / 2.0 - RingBackroom;
        var step = 2 * Math.PI / playerCount;

        Point At(double angle, double distance) => new(
            (int)Math.Round(cx + distance * Cos(angle), MidpointRounding.AwayFromZero),
            (int)Math.Round(cy + distance * Sin(angle), MidpointRounding.AwayFromZero));

        // A line of tiles across the ray at this angle, stepping along the nearest of the eight grid
        // directions so every tile is distinct (rounding a true perpendicular can land two on one tile).
        Point Across(double angle, double distance, int along)
        {
            var c = At(angle, distance);
            double px = -Sin(angle), py = Cos(angle);
            var m = Math.Max(Math.Abs(px), Math.Abs(py));
            var sx = (int)Math.Round(px / m, MidpointRounding.AwayFromZero);
            var sy = (int)Math.Round(py / m, MidpointRounding.AwayFromZero);
            return new Point(c.X + along * sx, c.Y + along * sy);
        }

        var angles = Enumerable.Range(0, playerCount).Select(i => -3 * Math.PI / 4 + i * step).ToArray();
        var starts = angles.Select(a => At(a, radius)).ToArray();

        var spawns = new List<DepositSpawn>();
        var expansionAt = 0.55 + rng.Next(0, 20) / 100.0;   // how far out the expansions sit, same for everyone
        var contestedAt = Math.Max(4, radius * (0.2 + rng.Next(0, 15) / 100.0));
        foreach (var angle in angles)
        {
            for (var k = -4; k <= 4; k++)
            {
                spawns.Add(new DepositSpawn(Across(angle, radius + 4, k), BaseOreAmount));   // the seam behind the base
            }

            var gap = angle + step / 2;
            foreach (var along in new[] { -1, 0, 1 })
            {
                spawns.Add(new DepositSpawn(Across(gap, radius * expansionAt, along), ExpansionOreAmount));
            }

            spawns.Add(new DepositSpawn(At(gap, contestedAt), RichOreAmount));
        }

        var deposits = spawns
            .Where(d => d.Position.X >= 0 && d.Position.Y >= 0 && d.Position.X < width && d.Position.Y < height)
            .DistinctBy(d => d.Position)
            .OrderBy(d => d.Position.Y).ThenBy(d => d.Position.X)
            .ToList();
        var depositSet = deposits.Select(d => d.Position).ToHashSet();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            // Outcrops: a few (distance, offset into the gap, size) templates, stamped into every gap alike.
            var templates = Enumerable.Range(0, rng.Next(2, 5))
                .Select(_ => (Distance: radius * rng.Next(30, 95) / 100.0, Offset: step * rng.Next(25, 76) / 100.0, Size: rng.Next(1, 4)))
                .ToList();
            var rock = new bool[width * height];
            foreach (var angle in angles)
            {
                foreach (var (distance, offset, size) in templates)
                {
                    var c = At(angle + offset, distance);
                    for (var y = c.Y - size; y <= c.Y + size; y++)
                    {
                        for (var x = c.X - size; x <= c.X + size; x++)
                        {
                            var p = new Point(x, y);
                            if (x < 0 || y < 0 || x >= width || y >= height || p.DistanceSquared(c) > size * size + 1
                                || starts.Any(s => s.Chebyshev(p) <= 6) || deposits.Any(d => d.Position.Chebyshev(p) <= 2))
                            {
                                continue;
                            }

                            rock[y * width + x] = true;
                        }
                    }
                }
            }

            if (IsConnected(rock, depositSet, width, height, starts))
            {
                return new GameMap(width, height, rock, starts, deposits);
            }
        }

        return new GameMap(width, height, new bool[width * height], starts, deposits);
    }

    // Sine and cosine from a Taylor series: only +, * and /, which IEEE 754 makes identical on every
    // platform, unlike Math.Sin. Map generation (and so replays) must not depend on the OS.
    private static double Sin(double x)
    {
        x = Math.IEEERemainder(x, 2 * Math.PI);
        double term = x, sum = x, x2 = x * x;
        for (var n = 1; n <= 12; n++)
        {
            term *= -x2 / ((2 * n) * (2 * n + 1));
            sum += term;
        }

        return sum;
    }

    private static double Cos(double x) => Sin(x + Math.PI / 2);

    private static int AmountAt(List<DepositSpawn> quadrant, Point p, int width, int height)
    {
        var qx = p.X < width / 2 ? p.X : width - 1 - p.X;
        var qy = p.Y < height / 2 ? p.Y : height - 1 - p.Y;
        return quadrant.First(d => d.Position == new Point(qx, qy)).Amount;
    }

    private static IEnumerable<Point> Mirror(IEnumerable<Point> points, int width, int height)
    {
        foreach (var p in points)
        {
            yield return p;
            yield return new Point(width - 1 - p.X, p.Y);
            yield return new Point(p.X, height - 1 - p.Y);
            yield return new Point(width - 1 - p.X, height - 1 - p.Y);
        }
    }

    private static bool IsConnected(bool[] rock, HashSet<Point> deposits, int width, int height, Point[] corners)
    {
        bool Passable(int x, int y) =>
            x >= 0 && y >= 0 && x < width && y < height && !rock[y * width + x] && !deposits.Contains(new Point(x, y));

        var seen = new bool[width * height];
        var queue = new Queue<Point>();
        queue.Enqueue(corners[0]);
        seen[corners[0].Y * width + corners[0].X] = true;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var n in Pathfinder.Neighbors(p, Passable))
            {
                var i = n.Y * width + n.X;
                if (!seen[i])
                {
                    seen[i] = true;
                    queue.Enqueue(n);
                }
            }
        }

        if (corners.Any(c => !seen[c.Y * width + c.X]))
        {
            return false;
        }

        foreach (var d in deposits)
        {
            var reachable = false;
            for (var dy = -1; dy <= 1 && !reachable; dy++)
            {
                for (var dx = -1; dx <= 1 && !reachable; dx++)
                {
                    var x = d.X + dx;
                    var y = d.Y + dy;
                    reachable = Passable(x, y) && seen[y * width + x];
                }
            }

            if (!reachable)
            {
                return false;
            }
        }

        return true;
    }
}
