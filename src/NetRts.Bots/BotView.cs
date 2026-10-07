using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>Convenience queries over one tick's <see cref="GameStateDto"/>.</summary>
public sealed class BotView
{
    private readonly HashSet<(int X, int Y)> _blocked;

    public BotView(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        State = context.State;
        Map = context.Map;
        Rules = context.Rules;

        var me = State.You.Slot;
        MyUnits = State.Units.Where(u => u.Owner == me).ToList();
        MyBuildings = State.Buildings.Where(b => b.Owner == me).ToList();
        EnemyUnits = State.Units.Where(u => u.Owner != me).ToList();
        EnemyBuildings = State.Buildings.Where(b => b.Owner != me).ToList();
        Workers = MyUnits.Where(u => u.Type == UnitType.Worker).ToList();
        Army = MyUnits.Where(u => u.Type != UnitType.Worker).ToList();
        CommandCenter = MyBuildings.Where(b => b.Type == BuildingType.CommandCenter)
            .OrderByDescending(b => b.Completed).ThenBy(b => b.Id).FirstOrDefault();

        _blocked = State.Buildings.Select(b => (b.X, b.Y))
            .Concat(State.Resources.Select(r => (r.X, r.Y)))
            .ToHashSet();
    }

    public GameStateDto State { get; }
    public MapDto Map { get; }
    public RulesDto Rules { get; }
    public IReadOnlyList<UnitDto> MyUnits { get; }
    public IReadOnlyList<BuildingDto> MyBuildings { get; }
    public IReadOnlyList<UnitDto> EnemyUnits { get; }
    public IReadOnlyList<BuildingDto> EnemyBuildings { get; }
    public IReadOnlyList<UnitDto> Workers { get; }
    public IReadOnlyList<UnitDto> Army { get; }
    public BuildingDto? CommandCenter { get; }

    public int UnitCost(UnitType type) => Rules.Units.First(u => u.Type == type).Cost;

    public bool IsOpen(int x, int y) =>
        x >= 0 && y >= 0 && x < Map.Width && y < Map.Height && Map.Terrain[y][x] == '.' && !_blocked.Contains((x, y));

    /// <summary>Where the opposing bases started; the first one still in the game.</summary>
    public PositionDto? EnemyStart() =>
        State.Players.Where(p => p.Slot != State.You.Slot && !p.Eliminated).Select(p => p.StartPosition).FirstOrDefault();

    /// <summary>Nearest visible enemy building, else the enemy's starting base.</summary>
    public (int X, int Y)? AttackTarget()
    {
        var cc = CommandCenter!;
        var building = EnemyBuildings.OrderBy(b => PlanBot.Dist(b.X, b.Y, cc.X, cc.Y)).ThenBy(b => b.Id).FirstOrDefault();
        if (building is not null)
        {
            return (building.X, building.Y);
        }

        return EnemyStart() is { } start ? (start.X, start.Y) : null;
    }

    /// <summary>Where the army waits: a few tiles from the Command Center toward the map centre.</summary>
    public (int X, int Y) RallyPoint()
    {
        var cc = CommandCenter!;
        var (dx, dy) = TowardCenter(cc.X, cc.Y);
        for (var d = 5; d >= 2; d--)
        {
            var x = cc.X + dx * d;
            var y = cc.Y + dy * d;
            if (IsOpen(x, y))
            {
                return (x, y);
            }
        }

        return (cc.X, cc.Y);
    }

    /// <summary>A worker to borrow for construction: not carrying much, closest to the site.</summary>
    public UnitDto? PickBuilder((int X, int Y) site) =>
        Workers
            .Where(w => w.Activity is UnitActivity.Gathering or UnitActivity.Moving or UnitActivity.Idle && w.Carrying < 6)
            .OrderBy(w => PlanBot.Dist(w.X, w.Y, site.X, site.Y)).ThenBy(w => w.Id)
            .FirstOrDefault();

    /// <summary>
    /// Picks an open tile near the base that keeps a one-tile walkway around other buildings and
    /// stays off the mining lanes.
    /// </summary>
    public (int X, int Y)? FindBuildSite(SitePreference preference, IEnumerable<(int X, int Y)> reserved)
    {
        var cc = CommandCenter!;
        var reservedSet = reserved.ToHashSet();
        var anchor = Anchor(preference, cc);

        (int X, int Y)? best = null;
        var bestScore = int.MaxValue;
        for (var y = cc.Y - 8; y <= cc.Y + 8; y++)
        {
            for (var x = cc.X - 8; x <= cc.X + 8; x++)
            {
                var ring = PlanBot.Dist(x, y, cc.X, cc.Y);
                if (ring < 3 || !IsOpen(x, y) || reservedSet.Contains((x, y)))
                {
                    continue;
                }

                if (State.Buildings.Any(b => PlanBot.Dist(b.X, b.Y, x, y) <= 1)
                    || reservedSet.Any(r => PlanBot.Dist(r.X, r.Y, x, y) <= 1)
                    || State.Resources.Any(r => PlanBot.Dist(r.X, r.Y, x, y) <= 2 && preference != SitePreference.NearOre)
                    || State.Resources.Any(r => PlanBot.Dist(r.X, r.Y, x, y) <= 1))
                {
                    continue;
                }

                var score = PlanBot.Dist(x, y, anchor.X, anchor.Y) * 10 + ring;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                }
            }
        }

        return best;
    }

    private (int X, int Y) Anchor(SitePreference preference, BuildingDto cc)
    {
        switch (preference)
        {
            case SitePreference.NearOre:
            {
                var ore = State.Resources.Where(r => PlanBot.Dist(r.X, r.Y, cc.X, cc.Y) <= 10).ToList();
                return ore.Count == 0
                    ? (cc.X, cc.Y)
                    : ((int)Math.Round(ore.Average(r => r.X)), (int)Math.Round(ore.Average(r => r.Y)));
            }

            case SitePreference.TowardEnemy when EnemyStart() is { } enemy:
            {
                var dx = Math.Sign(enemy.X - cc.X);
                var dy = Math.Sign(enemy.Y - cc.Y);
                return (cc.X + dx * 5, cc.Y + dy * 5);
            }

            default:
            {
                var (dx, dy) = TowardCenter(cc.X, cc.Y);
                return (cc.X + dx * 4, cc.Y + dy * 4);
            }
        }
    }

    private (int Dx, int Dy) TowardCenter(int x, int y) =>
        (Math.Sign(Map.Width / 2 - x), Math.Sign(Map.Height / 2 - y));
}
