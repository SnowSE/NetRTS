using NetRts.Protocol;

namespace NetRts.Bots.Formics;

/// <summary>A grub pile the hive knows of: seen (with its id), or inferred from the map's mirror symmetry (id 0).</summary>
internal sealed class OreSpot(int x, int y, int remaining)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Id { get; set; }
    public int Remaining { get; set; } = remaining;
}

/// <summary>Piles close enough together to share one drop-off.</summary>
internal sealed class OreField(List<OreSpot> spots)
{
    public List<OreSpot> Spots { get; } = spots;
    public int Total { get; } = spots.Sum(s => s.Remaining);
    public int X { get; } = (int)Math.Round(spots.Average(s => s.X));
    public int Y { get; } = (int)Math.Round(spots.Average(s => s.Y));
}

/// <summary>
/// Where the grubs are. Two- to four-player maps are mirrored both ways, so the piles seen around our own base
/// tell us where every other corner's piles are before anyone walks there. Piles are grouped into fields, and a
/// drop-off two tiles from a pile lets a digger bank without taking a step.
/// </summary>
internal sealed class OreFields
{
    /// <summary>A drop-off this close (Chebyshev) to a pile means no walking: dig, bank, dig.</summary>
    public const int ServedRange = 2;

    private readonly Dictionary<(int X, int Y), OreSpot> _spots = [];
    private readonly bool _mirrored;
    private readonly int _width;
    private readonly int _height;
    private readonly MapDto _map;
    private List<OreField> _fields = [];
    private bool _dirty = true;

    public OreFields(MapDto map)
    {
        _map = map;
        _width = map.Width;
        _height = map.Height;
        _mirrored = map.StartPositions.Count <= 4;
    }

    public IEnumerable<OreSpot> Spots => _spots.Values;

    public IReadOnlyList<OreField> Fields
    {
        get
        {
            if (_dirty)
            {
                _fields = Cluster();
                _dirty = false;
            }

            return _fields;
        }
    }

    public bool IsRock(int x, int y) => x < 0 || y < 0 || x >= _width || y >= _height || _map.Terrain[y][x] != '.';

    public bool HasOre(int x, int y) => _spots.ContainsKey((x, y));

    public void Update(GameStateDto state)
    {
        foreach (var r in state.Resources)
        {
            if (!_spots.TryGetValue((r.X, r.Y), out var spot))
            {
                spot = new OreSpot(r.X, r.Y, r.Remaining);
                _spots[(r.X, r.Y)] = spot;
                _dirty = true;
                foreach (var m in Mirrors(r.X, r.Y).Where(m => !_spots.ContainsKey(m)))
                {
                    _spots[m] = new OreSpot(m.X, m.Y, r.Remaining);
                }
            }

            spot.Id = r.Id;
            spot.Remaining = r.Remaining;
        }

        // A pile whose tile we can see but that isn't listed is gone (dug out, or never there after all).
        var rows = state.Visibility;
        var gone = _spots.Values.Where(s => s.Y < rows.Count && s.X < rows[s.Y].Length && rows[s.Y][s.X] == '1'
                                            && !state.Resources.Any(r => r.X == s.X && r.Y == s.Y)).ToList();
        foreach (var s in gone)
        {
            _spots.Remove((s.X, s.Y));
            _dirty = true;
        }

        if (state.Tick % 20 == 0)
        {
            _dirty = true;
        }
    }

    private IEnumerable<(int X, int Y)> Mirrors(int x, int y)
    {
        if (!_mirrored)
        {
            yield break;
        }

        yield return (_width - 1 - x, y);
        yield return (x, _height - 1 - y);
        yield return (_width - 1 - x, _height - 1 - y);
    }

    private List<OreField> Cluster()
    {
        var left = _spots.Values.Where(s => s.Remaining > 0).OrderBy(s => s.Y).ThenBy(s => s.X).ToList();
        var fields = new List<OreField>();
        while (left.Count > 0)
        {
            var group = new List<OreSpot> { left[0] };
            left.RemoveAt(0);
            for (var i = 0; i < group.Count; i++)
            {
                var g = group[i];
                var near = left.Where(s => Math.Max(Math.Abs(s.X - g.X), Math.Abs(s.Y - g.Y)) <= 3).ToList();
                group.AddRange(near);
                left.RemoveAll(near.Contains);
            }

            fields.Add(new OreField(group));
        }

        return fields;
    }

    /// <summary>
    /// The best tile for a new drop-off serving these piles: open, two tiles from the piles it serves (so the
    /// tiles beside the piles stay free to dig from), scored by the grubs it serves that no drop-off serves yet.
    /// </summary>
    /// <param name="spots">The piles to serve.</param>
    /// <param name="isOpen">Whether a tile is free to build on.</param>
    /// <param name="dropOffs">Our drop-offs; piles they already serve don't count.</param>
    /// <param name="steps">Walking distance from our base to a tile (int.MaxValue: can't get there); ties go to the nearer site.</param>
    public (int X, int Y, int Served)? DropOffSite(IEnumerable<OreSpot> spots, Func<int, int, bool> isOpen, IReadOnlyCollection<(int X, int Y)> dropOffs, Func<int, int, int> steps)
    {
        var candidates = spots.Where(s => s.Remaining > 0 && !dropOffs.Any(d => Cheb(d.X, d.Y, s.X, s.Y) <= ServedRange)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        (int X, int Y, int Served)? best = null;
        var bestSteps = int.MaxValue;
        var tried = new HashSet<(int, int)>();
        foreach (var s in candidates)
        {
            for (var dy = -ServedRange; dy <= ServedRange; dy++)
            {
                for (var dx = -ServedRange; dx <= ServedRange; dx++)
                {
                    var (x, y) = (s.X + dx, s.Y + dy);
                    if (!tried.Add((x, y)) || !isOpen(x, y) || _spots.Values.Any(o => Cheb(o.X, o.Y, x, y) < ServedRange))
                    {
                        continue;
                    }

                    // The drop-off goes on the near side of the piles, where the diggers come from.
                    var walk = steps(x, y);
                    if (walk == int.MaxValue)
                    {
                        continue;
                    }

                    var served = candidates.Where(c => Cheb(c.X, c.Y, x, y) <= ServedRange && HasStandingTile(c, x, y, isOpen)).Sum(c => c.Remaining);
                    if (best is null || served > best.Value.Served || (served == best.Value.Served && walk < bestSteps))
                    {
                        best = (x, y, served);
                        bestSteps = walk;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Whether a digger can stand beside this pile and the drop-off at once.</summary>
    private bool HasStandingTile(OreSpot spot, int x, int y, Func<int, int, bool> isOpen)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var (sx, sy) = (spot.X + dx, spot.Y + dy);
                if (Cheb(sx, sy, x, y) <= 1 && (sx, sy) != (x, y) && !IsRock(sx, sy) && !HasOre(sx, sy) && isOpen(sx, sy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static int Cheb(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
