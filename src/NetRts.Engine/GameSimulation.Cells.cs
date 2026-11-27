using NetRts.Protocol;

namespace NetRts.Engine;

/// <summary>
/// Cell limits (<see cref="RuleSet.CellCapacity"/>) and digging spots (<see cref="RuleSet.MinersPerEdge"/>).
/// Every unit, walking or standing, takes its <see cref="UnitStats.Occupancy"/> of the cell it ends each tick
/// in: one that moved takes a place there if there is room, and is bumped to the nearest cell with room if
/// there isn't (a walker toward where it's going), so marching armies spread out. A unit that stays where it
/// is keeps its place. Nothing here runs under rules without a cell limit.
/// </summary>
public sealed partial class GameSimulation
{
    private int[] _cellLoad = [];
    private readonly Dictionary<int, List<UnitEntity>> _standing = [];
    private readonly HashSet<int> _diggers = [];
    private long _settleSeq;
    private int _tieTurn;

    private bool Crowding => Rules.CellCapacity > 0;

    private bool DigLimit => Crowding && Rules.MinersPerEdge > 0;

    private int Room(Point p) => Rules.CellCapacity - _cellLoad[Index(p)];

    private bool HasRoom(Point p, UnitEntity unit) => Passable(p) && Room(p) >= unit.Stats.Occupancy;

    /// <summary>The unit takes its share of the cell it stands in.</summary>
    private void Settle(UnitEntity unit)
    {
        var cell = Index(unit.Position);
        _cellLoad[cell] += unit.Stats.Occupancy;
        unit.SettledSeq = ++_settleSeq;
        if (!_standing.TryGetValue(cell, out var list))
        {
            _standing[cell] = list = [];
        }

        list.Add(unit);
    }

    /// <summary>The unit gives up its place (it walked away or died).</summary>
    private void Unsettle(UnitEntity unit)
    {
        if (unit.SettledSeq == 0)
        {
            return;
        }

        var cell = Index(unit.Position);
        _cellLoad[cell] -= unit.Stats.Occupancy;
        unit.SettledSeq = 0;
        _standing[cell].Remove(unit);
    }

    /// <summary>Moves a unit, giving up its place in the cell it leaves.</summary>
    private void Place(UnitEntity unit, Point to)
    {
        Unsettle(unit);
        unit.Position = to;
    }

    /// <summary>
    /// After everyone has moved: each unit without a place (it moved, or was just placed) takes one, or is bumped. Within a cell
    /// the lowest id goes first; when players' units don't all fit, they take turns, starting one player further
    /// round the table each time that happens. The bumped are placed after every cell has seated its own.
    /// </summary>
    private void ResolveCells()
    {
        if (!Crowding)
        {
            return;
        }

        var bumped = new List<UnitEntity>();
        var arrivals = _units.Where(u => !u.Dead && u.SettledSeq == 0)
            .GroupBy(u => Index(u.Position))
            .OrderBy(g => g.Key);
        foreach (var cell in arrivals)
        {
            var group = cell.OrderBy(u => u.Id).ToList();
            var room = Room(group[0].Position);
            if (group.Sum(u => u.Stats.Occupancy) > room && group.Select(u => u.Owner).Distinct().Count() > 1)
            {
                group = TakeTurns(group);
            }

            foreach (var unit in group)
            {
                if (Room(unit.Position) >= unit.Stats.Occupancy)
                {
                    Settle(unit);
                }
                else
                {
                    bumped.Add(unit);
                }
            }
        }

        foreach (var unit in bumped)
        {
            Bump(unit);
        }
    }

    /// <summary>Deals the group out one unit per player at a time, like cards, starting with the player whose turn it is.</summary>
    private List<UnitEntity> TakeTurns(List<UnitEntity> group)
    {
        var first = _tieTurn++ % _players.Count;
        var hands = group.GroupBy(u => u.Owner).ToDictionary(g => g.Key, g => new Queue<UnitEntity>(g));
        var dealt = new List<UnitEntity>(group.Count);
        while (dealt.Count < group.Count)
        {
            for (var i = 0; i < _players.Count; i++)
            {
                if (hands.TryGetValue((first + i) % _players.Count, out var hand) && hand.Count > 0)
                {
                    dealt.Add(hand.Dequeue());
                }
            }
        }

        return dealt;
    }

    /// <summary>Moves a unit that found no room to the nearest cell that has some, and lets it carry on from there.</summary>
    private void Bump(UnitEntity unit)
    {
        var walking = unit.Path.Count > 0;
        if (NearestRoom(unit, walking ? unit.Path[^1] : null) is not { } cell)
        {
            return; // nowhere near: it stays put without a place and tries again next tick
        }

        Place(unit, cell);
        Settle(unit);
        if (walking)
        {
            // Sidestepped: keep the route if its next step is still one step away (or this is it), else plan again.
            if (unit.Path[0] == cell)
            {
                unit.Path.RemoveAt(0);
            }
            else if (unit.Path[0].Chebyshev(cell) > 1)
            {
                unit.ClearPath();
            }

            return;
        }

        unit.ClearPath();
        if (unit.Order == OrderKind.Gather && unit.Carrying < Rules.WorkerCarryCapacity)
        {
            FindPlaceToDig(unit);
        }
    }

    /// <summary>
    /// The nearest cell (by steps over open ground, in a fixed order) with room for the unit; among the nearest,
    /// for a walker the one nearest <paramref name="toward"/> (where it's going), for anyone else one where it
    /// can get on with its order (dig, build, shoot its target) if there is one.
    /// </summary>
    private Point? NearestRoom(UnitEntity unit, Point? toward, int maxSteps = 8)
    {
        var seen = new HashSet<Point> { unit.Position };
        var ring = new List<Point> { unit.Position };
        for (var step = 1; step <= maxSteps && ring.Count > 0; step++)
        {
            var next = new List<Point>();
            foreach (var from in ring)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var p = new Point(from.X + dx, from.Y + dy);
                        if (Passable(p) && seen.Add(p))
                        {
                            next.Add(p);
                        }
                    }
                }
            }

            var open = next.Where(p => Room(p) >= unit.Stats.Occupancy).ToList();
            if (open.Count > 0)
            {
                return toward is { } goal
                    ? open.MinBy(p => (p.Chebyshev(goal), p.DistanceSquared(goal)))
                    : open.FirstOrDefault(p => CanWorkFrom(unit, p), open[0]);
            }

            ring = next;
        }

        return null;
    }

    /// <summary>Whether, standing at <paramref name="p"/>, the unit could do what it was told to without moving.</summary>
    private bool CanWorkFrom(UnitEntity unit, Point p) => unit.Order switch
    {
        OrderKind.Gather => unit.Carrying < Rules.WorkerCarryCapacity && SourcesBeside(unit, p).Any(),
        OrderKind.Build => p.Chebyshev(unit.Destination!.Value) <= 1,
        OrderKind.AttackTarget => unit.TargetId is { } id && _entities.GetValueOrDefault(id) is { } target
                                  && p.Chebyshev(target.Position) is var d && d <= unit.Stats.Range && d >= unit.Stats.MinRange,
        _ => false,
    };

    /// <summary>
    /// A bumped worker: dig from where it stands if a patch beside it has a free spot here, else head for the
    /// nearest free spot it can see, else give up.
    /// </summary>
    private void FindPlaceToDig(UnitEntity unit)
    {
        var here = SourcesBeside(unit, unit.Position).ToList();
        var source = here.FirstOrDefault(s => s.Id == unit.TargetId) ?? here.FirstOrDefault();
        if (source is not null)
        {
            unit.TargetId = source.Id;
            unit.GatherSpot = unit.Position;
            return;
        }

        if (FreeSpotNear(unit, unit.Position, unit.Stats.Vision) is { } found)
        {
            unit.TargetId = found.Source.Id;
            unit.GatherSpot = found.Spot;
            return;
        }

        AddEvent(_players[unit.Owner], "CommandFailed", $"Worker {unit.Id} has no free spot to dig within sight", unit.Id, unit.OrderCommandId, spectator: false);
        unit.ClearOrder();
    }

    /// <summary>Patches and the unit's own farms beside <paramref name="p"/> that someone could dig from there now.</summary>
    private IEnumerable<Entity> SourcesBeside(UnitEntity unit, Point p)
    {
        if (DiggersAt(p, unit) >= Rules.MinersPerEdge && DigLimit)
        {
            yield break;
        }

        foreach (var source in Sources(unit.Owner).Where(s => s.Position.Chebyshev(p) == 1).OrderBy(s => s.Id))
        {
            yield return source;
        }
    }

    /// <summary>Everything a player's workers can dig: every patch, and their own farms that have grubs.</summary>
    private IEnumerable<Entity> Sources(int owner) =>
        _deposits.Cast<Entity>().Concat(_buildings.Where(b => b.Owner == owner && b.Type == BuildingType.Farm && b.Completed && b.Grubs > 0));

    /// <summary>
    /// Workers who mean to dig from <paramref name="p"/> (there now, or on their way), other than
    /// <paramref name="except"/>. Spots are handed out by this, so workers sent together spread out.
    /// </summary>
    private int DiggersAt(Point p, UnitEntity? except = null) =>
        _units.Count(u => u != except && u.Order == OrderKind.Gather && u.GatherSpot == p);

    /// <summary>
    /// The nearest source within <paramref name="radius"/> of <paramref name="from"/> (Euclidean, like sight) with a
    /// cell beside it that has room for the worker and a free digging spot, and that cell.
    /// </summary>
    private (Entity Source, Point Spot)? FreeSpotNear(UnitEntity unit, Point from, int radius)
    {
        var r2 = radius * radius;
        foreach (var source in Sources(unit.Owner).Where(s => from.DistanceSquared(s.Position) <= r2)
                     .OrderBy(s => from.DistanceSquared(s.Position)).ThenBy(s => s.Id))
        {
            var spot = Beside(source.Position)
                .Where(p => HasRoom(p, unit) && (!DigLimit || DiggersAt(p, unit) < Rules.MinersPerEdge))
                .OrderBy(p => p.Chebyshev(unit.Position))
                .Cast<Point?>()
                .FirstOrDefault();
            if (spot is { } s)
            {
                return (source, s);
            }
        }

        return null;
    }

    /// <summary>Where a worker heading for <paramref name="source"/> should stand: a free spot, else a cell with room to wait in, else the nearest.</summary>
    private Point? ChooseSpot(UnitEntity unit, Entity source) =>
        Beside(source.Position)
            .Where(Passable)
            .OrderBy(p => !HasRoom(p, unit) ? 2 : DigLimit && DiggersAt(p, unit) >= Rules.MinersPerEdge ? 1 : 0)
            .ThenBy(p => p.Chebyshev(unit.Position))
            .Cast<Point?>()
            .FirstOrDefault();

    /// <summary>The 8 cells around <paramref name="p"/>, row by row.</summary>
    private IEnumerable<Point> Beside(Point p)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx, dy) != (0, 0) && Map.InBounds(p.X + dx, p.Y + dy))
                {
                    yield return new Point(p.X + dx, p.Y + dy);
                }
            }
        }
    }

    /// <summary>
    /// Before units act: in each cell, the first <see cref="RuleSet.MinersPerEdge"/> workers to arrive who are there
    /// to dig (and aren't full) are the ones who dig this tick; the rest wait.
    /// </summary>
    private void ChooseDiggers()
    {
        _diggers.Clear();
        if (!DigLimit)
        {
            return;
        }

        var waiting = _units
            .Where(u => u.SettledSeq != 0 && u.Order == OrderKind.Gather && u.GatherSpot == u.Position && u.Carrying < Rules.WorkerCarryCapacity)
            .GroupBy(u => Index(u.Position));
        foreach (var cell in waiting)
        {
            foreach (var unit in cell.OrderBy(u => u.SettledSeq).Take(Rules.MinersPerEdge))
            {
                _diggers.Add(unit.Id);
            }
        }
    }

    /// <summary>A worker with a cell limit: walk to its spot beside the source, then dig when its turn comes.</summary>
    private void ApproachAndDig(UnitEntity unit, PlayerState player, Entity source)
    {
        if (unit.GatherSpot is not { } spot || !Passable(spot) || spot.Chebyshev(source.Position) != 1)
        {
            unit.GatherSpot = ChooseSpot(unit, source);
            if (unit.GatherSpot is not { } chosen)
            {
                unit.ClearOrder();
                return;
            }

            spot = chosen;
        }

        if (unit.Position != spot)
        {
            unit.Activity = UnitActivity.Moving;
            if (StepToward(unit, spot, 0) == StepResult.Stuck)
            {
                unit.ClearOrder();
            }

            return;
        }

        unit.ClearPath();
        if (unit.SettledSeq == 0 || (DigLimit && !_diggers.Contains(unit.Id)))
        {
            unit.Activity = UnitActivity.Waiting;
            return;
        }

        Dig(unit, player, source);
    }
}
