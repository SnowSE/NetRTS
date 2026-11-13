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
/// <param name="trackBuilders">
/// True (Block Bot): a build order lasts as long as its builder is on the way, however far it walks: it's
/// forgotten once its building appears, or once the builder is gone or doing something else. Its cost is
/// <see cref="SetAside"/> meanwhile, since the game only takes it when the builder breaks ground. False (the
/// house bots): it's kept for <see cref="PendingSiteTicks"/>, just not counted while a building stands on the site.
/// </param>
public sealed class BotMemory(bool trackBuilders = true)
{
    /// <summary>Without <c>trackBuilders</c>, a build order whose building hasn't appeared after this many ticks is given up on.</summary>
    public const int PendingSiteTicks = 40;

    /// <summary>With <c>trackBuilders</c>: ticks for a new order to reach its builder before its absence counts.</summary>
    private const int OrderArrivesTicks = 3;

    /// <summary>With <c>trackBuilders</c>: the longest walk waited for (a corner-to-corner trip is about 90).</summary>
    private const int LongestWalkTicks = 250;

    private readonly SortedDictionary<int, KnownDeposit> _deposits = [];
    private readonly Dictionary<(int X, int Y), (BuildingType Type, int Tick, int Builder)> _pendingSites = [];
    private bool[] _seen = [];
    private int _width;

    /// <summary>Every grub pile seen and not known to be dug out, by id.</summary>
    public IEnumerable<KnownDeposit> Deposits => _deposits.Values;

    /// <summary>Build orders given whose building hasn't appeared yet: site -> (type, tick ordered, builder's id).</summary>
    public IReadOnlyDictionary<(int X, int Y), (BuildingType Type, int Tick, int Builder)> PendingSites => _pendingSites;

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

        foreach (var site in _pendingSites.Where(kv => GivenUp(view, kv.Key, kv.Value.Tick, kv.Value.Builder)).Select(kv => kv.Key).ToList())
        {
            _pendingSites.Remove(site);
        }
    }

    private bool GivenUp(BotView view, (int X, int Y) site, int ordered, int builder)
    {
        var age = view.State.Tick - ordered;
        if (!trackBuilders)
        {
            return age > PendingSiteTicks;
        }

        if (view.MyBuildings.Any(b => b.X == site.X && b.Y == site.Y) || age > LongestWalkTicks)
        {
            return true;
        }

        // Still walking there (or about to get the order)? Then it's still coming, however long the walk.
        var worker = view.Workers.FirstOrDefault(w => w.Id == builder);
        return worker is null || (age > OrderArrivesTicks && (worker.Destination is not { } d || d.X != site.X || d.Y != site.Y));
    }

    public bool HasSeen(int x, int y) => _seen[(y * _width) + x];

    /// <summary>Counts a tile as seen, e.g. one a scout can't reach.</summary>
    public void MarkSeen(int x, int y) => _seen[(y * _width) + x] = true;

    public void AddPendingSite((int X, int Y) site, BuildingType type, int tick, int builder) => _pendingSites[site] = (type, tick, builder);

    /// <summary>
    /// With <c>trackBuilders</c>, the grubs owed for buildings ordered but not yet started (the game takes them
    /// when the builder breaks ground), so nothing else spends them while the builder walks. Else 0.
    /// </summary>
    public int SetAside(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return !trackBuilders ? 0 : _pendingSites
            .Where(kv => !view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y))
            .Sum(kv => view.Rules.Buildings.First(b => b.Type == kv.Value.Type).Cost);
    }

    /// <summary>Whether this worker is on its way to put up a building ordered earlier (and still remembered).</summary>
    /// <remarks>Only with <c>trackBuilders</c>: the house bots may re-use one.</remarks>
    public bool IsBuilder(int unitId) => trackBuilders && _pendingSites.Values.Any(p => p.Builder == unitId);

    /// <summary>Buildings of this type ordered but not yet standing.</summary>
    public int PendingCount(BotView view, BuildingType type)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _pendingSites.Count(kv => kv.Value.Type == type && !view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y));
    }
}
