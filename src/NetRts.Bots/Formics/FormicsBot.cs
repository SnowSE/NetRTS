using NetRts.Protocol;

namespace NetRts.Bots.Formics;

/// <summary>
/// The Formics: a hive mind. It plays the macro game the strongest student bots play, only tighter: workers
/// from every Command Center without a break, a drop-off two tiles from every grub field so diggers never walk,
/// new Command Centers on the free corners' grubs, Entomology early, bell towers by every base, then a swarm of
/// upgraded soldiers (equal grubs of soldiers beat any mix with archers or scouts; Weapons and Armor are researched
/// before anything but Entomology, and before more soldiers) that shares out its blows so none is wasted, defends
/// every base, takes the serum into the first big fight, and strikes once it outnumbers what it expects the enemy
/// to have. The public scoreboard is its spy: survival and income say roughly how big an enemy army is before it's
/// seen; in a crowd, the armies of near neighbors count for more than far ones.
/// <para>
/// Built on the same pieces as the other house bots (<see cref="BotView"/>, <see cref="BotMemory"/>,
/// <see cref="BotTurn"/>); the economy is in FormicsBot.Economy.cs and the fighting in FormicsBot.Army.cs.
/// </para>
/// </summary>
public sealed partial class FormicsBot : IBotStrategy, ISerumTaker
{
    private readonly BotMemory _memory = new(trackBuilders: true);
    private readonly FormicsTuning _tune;
    private OreFields? _ore;
    private double _grubScale = 1.0;

    // Enemy units seen lately, by id: what they are and when we last saw them.
    private readonly Dictionary<int, (UnitType Type, int Owner, int X, int Y, int Tick)> _enemySeen = [];

    public FormicsBot()
        : this(FormicsTuning.Default)
    {
    }

    internal FormicsBot(FormicsTuning tune)
    {
        _tune = tune;
    }

    public string Name => "formics";

    public IReadOnlyList<CommandRequest> Decide(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new BotView(context);
        if (view.State.Status != MatchStatus.Active || view.CommandCenter is null)
        {
            return [];
        }

        if (_ore is null)
        {
            _ore = new OreFields(view.Map);

            // Piles beside a base start at 1,000 grubs on a usual map; scarce and rich maps scale every threshold.
            var piles = view.State.Resources.Where(r => Cheb(r.X, r.Y, view.CommandCenter.X, view.CommandCenter.Y) <= 8).ToList();
            _grubScale = piles.Count == 0 ? 1.0 : Math.Clamp(piles.Max(r => r.Remaining) / 1000.0, 0.25, 4.0);
        }

        _memory.Update(view);
        _ore.Update(view.State);
        UpdateIntel(view);
        RecordScores(view);

        var turn = new BotTurn(view, _memory, reserveUnits: true);
        var hive = new HiveTick(view, turn, _ore);
        MarkDanger(hive);
        CheckOrders(hive);

        CommandArmy(hive);
        Scout(hive);
        Spend(hive);
        if (_tune.FinishSites)
        {
            // A builder killed or chased off leaves a half-built site: someone else finishes it, once it's safe.
            turn.FinishAbandonedSites(site => hive.IsDangerous(site.X, site.Y));
        }

        Replant(hive);
        GatherWorkers(hive);

        return turn.Commands.Count <= MaxCommands ? turn.Commands : [.. turn.Commands.Take(MaxCommands)];
    }

    /// <summary>The server runs at most 100 queued commands a tick; staying under it keeps orders fresh.</summary>
    private const int MaxCommands = 90;

    // Build sites someone else's building turned out to hold (seen in the fog by its failure), until a tick.
    private readonly Dictionary<(int X, int Y), int> _takenSites = [];

    // Sites the fog hides an enemy building on, found when our build order there failed.
    private readonly HashSet<(int X, int Y)> _enemySites = [];

    private void UpdateIntel(BotView view)
    {
        var tick = view.State.Tick;
        foreach (var e in view.State.Events.Where(e => e.Kind == "CommandFailed" && e.Message.Contains("taken by another building", StringComparison.Ordinal)))
        {
            if (ParsePoint(e.Message) is { } site && !view.MyBuildings.Any(b => b.X == site.X && b.Y == site.Y))
            {
                _takenSites[site] = tick + 600;
                _enemySites.Add(site);
            }
        }

        // An enemy site we've had a look at since is known (as a building, or gone): no need to guess any more.
        _enemySites.RemoveWhere(t => view.State.Visibility[t.Y][t.X] == '1');

        foreach (var site in _takenSites.Where(kv => kv.Value < tick).Select(kv => kv.Key).ToList())
        {
            _takenSites.Remove(site);
        }

        foreach (var e in view.EnemyUnits)
        {
            _enemySeen[e.Id] = (e.Type, e.Owner, e.X, e.Y, tick);
        }

        // Forget what we haven't seen for a long while, and what we can see is no longer there (dead, mostly).
        var rows = view.State.Visibility;
        var visibleIds = view.EnemyUnits.Select(e => e.Id).ToHashSet();
        foreach (var (id, seen) in _enemySeen.ToList())
        {
            var tileVisible = seen.Y < rows.Count && seen.X < rows[seen.Y].Length && rows[seen.Y][seen.X] == '1';
            if (tick - seen.Tick > _tune.ForgetEnemyTicks || (tileVisible && !visibleIds.Contains(id)))
            {
                _enemySeen.Remove(id);
            }
        }
    }

    /// <summary>Where workers shouldn't dig: by enemy towers and buildings, and where enemy fighters were just seen.</summary>
    private void MarkDanger(HiveTick hive)
    {
        foreach (var b in hive.View.EnemyBuildings.Select(b => (b.X, b.Y, b.Type)).Concat(hive.View.State.RememberedBuildings.Select(b => (b.X, b.Y, b.Type))))
        {
            hive.Danger.Add((b.X, b.Y, b.Type == BuildingType.GuardTower ? 7 : 5));
        }

        foreach (var e in _enemySeen.Values.Where(e => e.Type != UnitType.Worker && hive.Tick - e.Tick <= 30))
        {
            hive.Danger.Add((e.X, e.Y, 5));
        }
    }

    /// <summary>The grub value of the enemy fighters we know of (optionally one player's).</summary>
    private int SeenArmyValue(BotView view, int? owner = null) =>
        _enemySeen.Values.Where(e => e.Type != UnitType.Worker && (owner is null || e.Owner == owner)).Sum(e => view.UnitCost(e.Type));

    // Each player's banked-grubs total, a while back, to work out their income.
    private readonly Dictionary<int, Queue<int>> _economyHistory = [];
    private const int IncomeWindow = 40;

    /// <summary>
    /// What an enemy's army is likely worth. The scoreboard is public: survival is the value of everything they
    /// have standing, and the economy score's growth gives their income and so roughly their workers. Take away a
    /// Command Center, the buildings we know of and the workers, and what's left is mostly army.
    /// </summary>
    private int EstimatedArmy(BotView view, PlayerSummaryDto enemy)
    {
        var seen = SeenArmyValue(view, enemy.Slot);
        if (!_economyHistory.TryGetValue(enemy.Slot, out var history) || history.Count < IncomeWindow)
        {
            return seen;
        }

        var income = (enemy.Score.Economy - history.Peek()) / (double)IncomeWindow;

        // Assume they dig at least as well as we do: if anything that overrates their army, which is the safe side.
        var mine = _economyHistory.TryGetValue(view.State.You.Slot, out var own) && own.Count >= IncomeWindow && view.Workers.Count > 0
            ? (view.State.Players.First(p => p.Slot == view.State.You.Slot).Score.Economy - own.Peek()) / (double)IncomeWindow / view.Workers.Count
            : 0;
        var workers = Math.Max(5, (int)(income / Math.Max(_tune.EnemyOrePerWorkerTick, mine)));
        var buildings = view.EnemyBuildings.Where(b => b.Owner == enemy.Slot && b.Completed).Select(b => (b.Id, b.Type))
            .Concat(view.State.RememberedBuildings.Where(b => b.Owner == enemy.Slot && b.Completed).Select(b => (b.Id, b.Type)))
            .DistinctBy(b => b.Id)
            .Sum(b => view.Rules.Buildings.First(r => r.Type == b.Type).Cost);
        var guess = enemy.Score.Survival - Math.Max(buildings, 400) - (workers * view.UnitCost(UnitType.Worker));
        return Math.Max(seen, (int)(guess * _tune.ScoreArmyShare));
    }

    /// <summary>An enemy started close by (a crowded map): their base is a short walk from ours.</summary>
    private bool CloseNeighbor(BotView view)
    {
        var me = view.State.Players.First(p => p.Slot == view.State.You.Slot).StartPosition;
        return view.State.Players.Any(p => p.Slot != view.State.You.Slot && !p.Eliminated
                                           && Cheb(p.StartPosition.X, p.StartPosition.Y, me.X, me.Y) <= _tune.CloseNeighborTiles);
    }

    /// <summary>The strongest enemy army we expect.</summary>
    private int StrongestEnemyArmy(BotView view) =>
        view.State.Players.Where(p => p.Slot != view.State.You.Slot && !p.Eliminated).Select(p => EstimatedArmy(view, p)).DefaultIfEmpty(0).Max();

    /// <summary>The strongest enemy army, each counted by how near its base is to ours (the nearest in full): the far ones have others to fight first.</summary>
    private int NearestThreat(BotView view)
    {
        var me = view.State.Players.First(p => p.Slot == view.State.You.Slot).StartPosition;
        var enemies = view.State.Players.Where(p => p.Slot != view.State.You.Slot && !p.Eliminated).ToList();
        if (enemies.Count == 0)
        {
            return 0;
        }

        var nearest = Math.Max(1, enemies.Min(p => Cheb(p.StartPosition.X, p.StartPosition.Y, me.X, me.Y)));
        return enemies.Max(p => (int)(EstimatedArmy(view, p) * nearest / (double)Math.Max(1, Cheb(p.StartPosition.X, p.StartPosition.Y, me.X, me.Y))));
    }

    private void RecordScores(BotView view)
    {
        foreach (var p in view.State.Players)
        {
            if (!_economyHistory.TryGetValue(p.Slot, out var history))
            {
                _economyHistory[p.Slot] = history = new Queue<int>();
            }

            history.Enqueue(p.Score.Economy);
            while (history.Count > IncomeWindow)
            {
                history.Dequeue();
            }
        }
    }

    /// <summary>The first "(x,y)" in an event message.</summary>
    private static (int X, int Y)? ParsePoint(string message)
    {
        var open = message.IndexOf('(', StringComparison.Ordinal);
        var close = open < 0 ? -1 : message.IndexOf(')', open);
        if (close < 0)
        {
            return null;
        }

        var parts = message[(open + 1)..close].Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y) ? (x, y) : null;
    }

    internal static int Cheb(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}

/// <summary>This tick: the view, the turn's budget and commands, and site bookkeeping.</summary>
internal sealed class HiveTick
{
    private readonly HashSet<(int X, int Y)> _blocked;

    public HiveTick(BotView view, BotTurn turn, OreFields ore)
    {
        View = view;
        Turn = turn;
        Ore = ore;
        Tick = view.State.Tick;
        Me = view.State.You.Slot;
        _blocked = view.State.Buildings.Select(b => (b.X, b.Y))
            .Concat(view.State.RememberedBuildings.Select(b => (b.X, b.Y)))
            .Concat(turn.Memory.PendingSites.Keys)
            .ToHashSet();
        DropOffs = view.MyBuildings.Where(b => b.Type is BuildingType.CommandCenter or BuildingType.ResourceDepot)
            .Select(b => (b.X, b.Y))
            .Concat(turn.Memory.PendingSites.Where(p => p.Value.Type is BuildingType.CommandCenter or BuildingType.ResourceDepot).Select(p => p.Key))
            .ToList();
        IncomePerTick = view.State.You.IncomePerMinute / 60.0;
    }

    public BotView View { get; }
    public BotTurn Turn { get; }
    public OreFields Ore { get; }
    public int Tick { get; }
    public int Me { get; }

    /// <summary>Our Command Centers and depots, standing, going up or ordered.</summary>
    public List<(int X, int Y)> DropOffs { get; }

    public double IncomePerTick { get; }

    /// <summary>Grubs kept back for a building being saved up for.</summary>
    public int Reserved { get; set; }

    /// <summary>Grubs spent this tick on orders added outside the turn's own actions.</summary>
    public int Spent { get; set; }

    /// <summary>Danger spots: (x, y, radius).</summary>
    public List<(int X, int Y, int R)> Danger { get; } = [];

    public bool IsDangerous(int x, int y) => Danger.Any(d => Math.Max(Math.Abs(d.X - x), Math.Abs(d.Y - y)) <= d.R);

    public bool IsOpen(int x, int y) => !Ore.IsRock(x, y) && !Ore.HasOre(x, y) && !_blocked.Contains((x, y));

    private int[]? _steps;

    /// <summary>Walking distance from the main Command Center to a tile, int.MaxValue if it can't be reached.</summary>
    public int Steps(int x, int y)
    {
        var w = View.Map.Width;
        if (_steps is null)
        {
            _steps = new int[w * View.Map.Height];
            Array.Fill(_steps, int.MaxValue);
            var cc = View.CommandCenter!;
            var queue = new Queue<(int X, int Y)>();
            _steps[(cc.Y * w) + cc.X] = 0;
            queue.Enqueue((cc.X, cc.Y));
            while (queue.Count > 0)
            {
                var (px, py) = queue.Dequeue();
                var next = _steps[(py * w) + px] + 1;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (px + dx, py + dy);
                        if (Ore.IsRock(nx, ny) || _steps[(ny * w) + nx] != int.MaxValue || Ore.HasOre(nx, ny) || _blocked.Contains((nx, ny)))
                        {
                            continue;
                        }

                        _steps[(ny * w) + nx] = next;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        return Ore.IsRock(x, y) ? int.MaxValue : _steps[(y * w) + x];
    }

    public void Reserve(int x, int y) => _blocked.Add((x, y));
}
