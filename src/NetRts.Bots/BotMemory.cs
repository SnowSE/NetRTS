using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>A grub pile as last seen. Piles never move, so one seen once can be found again in the fog.</summary>
public readonly record struct KnownDeposit(int Id, int X, int Y, int Remaining);

/// <summary>Where a bot last sent a unit, so it doesn't repeat the same order every tick.</summary>
public readonly record struct LastOrder(CommandType Type, int X, int Y, int Tick);

/// <summary>
/// What a bot remembers from tick to tick (one per bot, per match): the tiles it has seen, the grub piles it
/// has seen, the buildings it has ordered that haven't appeared yet, and its last order to each unit.
/// Call <see cref="Update"/> once at the start of every tick. The rest of the shared layer is
/// <see cref="BotView"/> (this tick) and <see cref="BotTurn"/> (this tick's spending and commands).
/// </summary>
/// <param name="forgetSitesOnceBuilt">
/// True (Block Bot): a build order is forgotten as soon as its building appears. False (the house bots): it's
/// kept for its full <see cref="PendingSiteTicks"/>, just not counted while a building stands on the site.
/// </param>
public sealed class BotMemory(bool forgetSitesOnceBuilt = true)
{
    /// <summary>A build order whose building hasn't appeared after this many ticks is given up on.</summary>
    public const int PendingSiteTicks = 40;

    private readonly SortedDictionary<int, KnownDeposit> _deposits = [];
    private readonly Dictionary<(int X, int Y), (BuildingType Type, int Tick)> _pendingSites = [];
    private bool[] _seen = [];
    private int _width;

    /// <summary>Every grub pile seen and not known to be dug out, by id.</summary>
    public IEnumerable<KnownDeposit> Deposits => _deposits.Values;

    /// <summary>Build orders given whose building hasn't appeared yet: site -> (type, tick ordered).</summary>
    public IReadOnlyDictionary<(int X, int Y), (BuildingType Type, int Tick)> PendingSites => _pendingSites;

    /// <summary>The last move or attack order each unit got from <see cref="BotTurn.Order"/>.</summary>
    public Dictionary<int, LastOrder> LastOrders { get; } = [];

    public void Update(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var state = view.State;
        var rows = state.Visibility;
        if (_seen.Length == 0)
        {
            _width = view.Map.Width;
            _seen = new bool[_width * view.Map.Height];
        }

        for (var y = 0; y < rows.Count && y < view.Map.Height; y++)
        {
            var row = rows[y];
            for (var x = 0; x < row.Length && x < _width; x++)
            {
                if (row[x] == '1')
                {
                    _seen[(y * _width) + x] = true;
                }
            }
        }

        var listed = new HashSet<int>();
        foreach (var d in state.Resources)
        {
            _deposits[d.Id] = new KnownDeposit(d.Id, d.X, d.Y, d.Remaining);
            listed.Add(d.Id);
        }

        // A pile we can see the tile of, but that isn't listed, has been dug out.
        bool Visible(int x, int y) => y < rows.Count && x < rows[y].Length && rows[y][x] == '1';
        foreach (var id in _deposits.Values.Where(d => !listed.Contains(d.Id) && Visible(d.X, d.Y)).Select(d => d.Id).ToList())
        {
            _deposits.Remove(id);
        }

        foreach (var site in _pendingSites
                     .Where(kv => state.Tick - kv.Value.Tick > PendingSiteTicks
                                  || (forgetSitesOnceBuilt && view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y)))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _pendingSites.Remove(site);
        }
    }

    public bool HasSeen(int x, int y) => _seen[(y * _width) + x];

    /// <summary>Counts a tile as seen, e.g. one a scout can't reach.</summary>
    public void MarkSeen(int x, int y) => _seen[(y * _width) + x] = true;

    public void AddPendingSite((int X, int Y) site, BuildingType type, int tick) => _pendingSites[site] = (type, tick);

    /// <summary>Buildings of this type ordered but not yet standing.</summary>
    public int PendingCount(BotView view, BuildingType type)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _pendingSites.Count(kv => kv.Value.Type == type && !view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y));
    }
}
