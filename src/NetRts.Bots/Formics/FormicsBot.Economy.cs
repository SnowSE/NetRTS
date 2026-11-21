using NetRts.Protocol;

namespace NetRts.Bots.Formics;

public sealed partial class FormicsBot
{
    private static readonly UpgradeType[] ResearchOrder =
    [
        UpgradeType.Harvesting1, UpgradeType.Harvesting2, UpgradeType.Weapons1, UpgradeType.Armor1, UpgradeType.Weapons2,
        UpgradeType.Armor2, UpgradeType.Armor3, UpgradeType.Mobility1, UpgradeType.Mobility2, UpgradeType.Fortify1, UpgradeType.Fortify2,
    ];

    private static readonly UpgradeType[] FightSoon =
    [
        UpgradeType.Harvesting1, UpgradeType.Weapons1, UpgradeType.Armor1, UpgradeType.Harvesting2, UpgradeType.Weapons2,
        UpgradeType.Armor2, UpgradeType.Armor3, UpgradeType.Mobility1, UpgradeType.Mobility2, UpgradeType.Fortify1, UpgradeType.Fortify2,
    ];

    // Under the gun, the fighting upgrades go first.
    private static readonly UpgradeType[] FightFirst = [UpgradeType.Weapons1, UpgradeType.Armor1, .. ResearchOrder.Except([UpgradeType.Weapons1, UpgradeType.Armor1])];

    private int _fighterCursor;

    /// <summary>Set by the army when the base is under attack by more than it can hold: fighters come before everything.</summary>
    private bool _emergency;

    private void Spend(HiveTick hive)
    {
        // Research is cheap and makes every fighter better: it goes first unless the base is being overrun.
        if (_tune.ResearchFirst && !_emergency)
        {
            Research(hive);
        }

        if (_emergency || _outnumbered)
        {
            TrainFighters(hive);
        }

        // Workers and research come before buildings, unless a building has waited too long for its grubs.
        var starved = _savingSince >= 0 && hive.Tick - _savingSince > _tune.SaveTicks;
        if (!starved)
        {
            TrainWorkers(hive);
            Research(hive);
        }

        if (!PlanBuilding(hive))
        {
            _savingSince = _savingSince < 0 ? hive.Tick : _savingSince;
        }
        else
        {
            _savingSince = -1;
            hive.Reserved = 0;
        }

        TrainFighters(hive);
    }

    private int _savingSince = -1;

    // ------------------------------------------------------------------ workers

    private int WorkerTarget(HiveTick hive)
    {
        var ore = hive.Ore.Fields.Where(f => SafeField(hive, f)).Sum(f => f.Total);
        var min = (int)(_tune.MinWorkers * Math.Min(1.0, _grubScale + 0.25));
        return Math.Clamp(min + (ore / _tune.OrePerWorker), min, _tune.MaxWorkers);
    }

    private void TrainWorkers(HiveTick hive)
    {
        var view = hive.View;
        var queued = view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == UnitType.Worker));
        var want = WorkerTarget(hive) - view.Workers.Count - queued;
        foreach (var cc in view.MyBuildings.Where(b => b.Type == BuildingType.CommandCenter && b.Completed).OrderBy(b => b.Production.Count))
        {
            if (want <= 0 || cc.Production.Count >= 2 || Budget(hive) < view.UnitCost(UnitType.Worker))
            {
                continue;
            }

            // New workers walk straight to a pile and start digging: rally the Command Center onto one it serves.
            var rally = BestDeposit(hive, cc.X, cc.Y, assigned: null);
            var command = new CommandRequest { Type = CommandType.Produce, BuildingId = cc.Id, UnitType = UnitType.Worker };
            if (rally is not null && (cc.Rally is not { } r || r.X != rally.X || r.Y != rally.Y))
            {
                command = command with { X = rally.X, Y = rally.Y };
            }

            hive.Turn.Add(command);
            SpendBudget(hive, view.UnitCost(UnitType.Worker));
            want--;
        }
    }

    // The turn's budget only shrinks through its own actions; a Produce we add ourselves pays through this.
    private static void SpendBudget(HiveTick hive, int cost) => hive.Spent += cost;

    // ------------------------------------------------------------------ gathering

    /// <summary>How many tiles a digger at this pile walks to bank (0 when a drop-off is two tiles away).</summary>
    private static int Walk(HiveTick hive, OreSpot spot, IReadOnlyList<(int X, int Y)> standing)
    {
        var best = int.MaxValue;
        foreach (var (x, y) in standing)
        {
            best = Math.Min(best, Cheb(x, y, spot.X, spot.Y));
        }

        return best == int.MaxValue ? 99 : Math.Max(0, best - OreFields.ServedRange);
    }

    private static OreSpot? BestDeposit(HiveTick hive, int fromX, int fromY, Dictionary<int, int>? assigned)
    {
        var standing = hive.View.MyBuildings.Where(b => b.Type is BuildingType.CommandCenter or BuildingType.ResourceDepot).Select(b => (b.X, b.Y)).ToList();
        OreSpot? best = null;
        var bestScore = int.MaxValue;
        foreach (var spot in hive.Ore.Spots)
        {
            if (spot.Id == 0 || spot.Remaining <= 0)
            {
                continue;
            }

            var load = assigned?.GetValueOrDefault(spot.Id) ?? 0;
            var crowd = Math.Max(0, load - Math.Max(2, spot.Remaining / 120));
            var score = (Walk(hive, spot, standing) * 12) + (crowd * 6) + (Cheb(fromX, fromY, spot.X, spot.Y) / 4)
                        + (hive.IsDangerous(spot.X, spot.Y) ? 1000 : 0);
            if (score < bestScore)
            {
                bestScore = score;
                best = spot;
            }
        }

        return best;
    }

    private void GatherWorkers(HiveTick hive)
    {
        var view = hive.View;
        var assigned = view.Workers.Where(w => w.TargetId is not null && w.Activity is UnitActivity.Gathering or UnitActivity.Moving or UnitActivity.Returning)
            .GroupBy(w => w.TargetId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var standing = view.MyBuildings.Where(b => b.Type is BuildingType.CommandCenter or BuildingType.ResourceDepot).Select(b => (b.X, b.Y)).ToList();
        var spots = hive.Ore.Spots.Where(s => s.Id != 0).ToDictionary(s => s.Id);

        var moved = 0;
        foreach (var w in view.Workers.OrderBy(w => w.Id))
        {
            if (hive.Turn.IsClaimed(w.Id) || _memory.IsBuilder(w.Id) || w.Activity == UnitActivity.Building)
            {
                continue;
            }

            var idle = w.Activity == UnitActivity.Idle;

            // Every so often, a digger hauling grubs a long way moves to a pile beside a drop-off with room.
            var rebalance = !idle && hive.Tick % 10 == w.Id % 10 && moved < 6 && w.Carrying == 0
                            && w.TargetId is { } t && spots.TryGetValue(t, out var current) && Walk(hive, current, standing) >= 3;
            if (!idle && !rebalance)
            {
                continue;
            }

            var spot = BestDeposit(hive, w.X, w.Y, assigned);
            if (spot is null || (rebalance && (spot.Id == w.TargetId || Walk(hive, spot, standing) >= 2)))
            {
                continue;
            }

            if (w.TargetId is { } old && assigned.ContainsKey(old))
            {
                assigned[old]--;
            }

            hive.Turn.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [w.Id], TargetId = spot.Id });
            assigned[spot.Id] = assigned.GetValueOrDefault(spot.Id) + 1;
            moved++;
        }
    }

    // ------------------------------------------------------------------ buildings

    /// <summary>
    /// Orders the most urgent building. Returns false when it can't be afforded yet: fighters then leave its cost
    /// in the bank (<see cref="HiveTick.Reserved"/>), and if it waits too long it goes ahead of workers too.
    /// </summary>
    private bool PlanBuilding(HiveTick hive)
    {
        var view = hive.View;
        var workers = view.Workers.Count;
        int Have(BuildingType t) => view.MyBuildings.Count(b => b.Type == t) + _memory.PendingCount(view, t);

        // 1. A drop-off beside every field we dig: a hundred grubs that halve every trip.
        if (DropOffWanted(hive, onlyNear: true) is { } depot && Have(BuildingType.ResourceDepot) + Have(BuildingType.CommandCenter) <= 1 + (workers / 4))
        {
            var cc = Have(BuildingType.CommandCenter) < CommandCentersWanted(workers) && hive.Tick > 0;
            return Place(hive, cc ? BuildingType.CommandCenter : BuildingType.ResourceDepot, depot);
        }

        // Facing a bigger army than ours, Barracks come before expansions.
        var barracks = Have(BuildingType.Barracks);
        var barracksWanted = workers < _tune.FirstBarracksWorkers && !_emergency && !_outnumbered ? 0
            : Math.Clamp((int)Math.Ceiling(hive.IncomePerTick * (_rushed ? 1.0 : ArmyShare(hive)) / _tune.IncomePerBarracks) + (view.State.You.Resources / 800), 1, _tune.MaxBarracks);
        var rising = barracks - view.MyBuildings.Count(b => b.Type == BuildingType.Barracks && b.Completed);
        if ((_outgunned || _rushed) && barracks < barracksWanted && rising < 2)
        {
            return PlaceNearBase(hive, BuildingType.Barracks);
        }

        // Upgraded soldiers beat more soldiers.
        var labFirst = _tune.LabFirst && view.MyBuildings.Any(b => b.Type == BuildingType.Barracks && b.Completed);
        if ((_outgunned || _rushed || labFirst) && (barracks >= 2 || labFirst) && Have(BuildingType.TechLab) == 0)
        {
            return PlaceNearBase(hive, BuildingType.TechLab);
        }

        // ...and towers by the Command Centers: a tower holds off three soldiers for the price of one and a quarter.
        if ((_outgunned || _rushed) && view.MyBuildings.Any(b => b.Type == BuildingType.Barracks && b.Completed) && TowerWanted(hive) is { } guard)
        {
            return Place(hive, BuildingType.GuardTower, guard);
        }

        // 2. More Command Centers: more workers at once, and the free corners' grubs.
        if (!_rushed && !_outgunned && Have(BuildingType.CommandCenter) < CommandCentersWanted(workers) && DropOffWanted(hive, onlyNear: false) is { } expansion)
        {
            return Place(hive, BuildingType.CommandCenter, expansion, helpers: 2);
        }

        // 3. Barracks, Tech Labs, towers.
        var hasBarracks = view.MyBuildings.Any(b => b.Type == BuildingType.Barracks && b.Completed);
        var labs = Have(BuildingType.TechLab);
        var labsWanted = !hasBarracks ? 0 : workers >= _tune.SecondLabWorkers ? 2 : 1;
        if (barracks == 0 && barracksWanted > 0)
        {
            return PlaceNearBase(hive, BuildingType.Barracks);
        }

        if (labs < labsWanted && (labs == 0 || barracks >= 2))
        {
            return PlaceNearBase(hive, BuildingType.TechLab);
        }

        if (hasBarracks && TowerWanted(hive) is { } tower)
        {
            return Place(hive, BuildingType.GuardTower, tower);
        }

        if (barracks < barracksWanted && rising < 3)
        {
            return PlaceNearBase(hive, BuildingType.Barracks);
        }

        // 4. Storage, when the bank is nearly full.
        var you = view.State.You;
        if (you.Resources > you.StorageCapacity - 250 && Have(BuildingType.ResourceDepot) < 12)
        {
            return PlaceNearBase(hive, BuildingType.ResourceDepot);
        }

        return true;
    }

    /// <summary>The share of income that goes to fighters: little while the hive grows, nearly all once the grubs run low.</summary>
    private double ArmyShare(HiveTick hive)
    {
        var share = hive.Tick < _tune.MidGameTick ? _tune.EarlyArmyShare : hive.Tick < _tune.LateGameTick ? _tune.MidArmyShare : 1.0;
        if (_outnumbered)
        {
            share = Math.Max(share, _tune.MidArmyShare);
        }

        return hive.View.Workers.Count >= WorkerTarget(hive) ? Math.Max(share, _tune.MidArmyShare) : share;
    }

    // Build orders given, by site and tick, to notice the ones that never got built.
    private readonly Dictionary<(int X, int Y), int> _ordered = [];

    /// <summary>A site whose builder never broke ground (killed on the way, mostly) is left alone for a while.</summary>
    private void CheckOrders(HiveTick hive)
    {
        foreach (var (site, tick) in _ordered.ToList())
        {
            if (hive.View.MyBuildings.Any(b => b.X == site.X && b.Y == site.Y))
            {
                _ordered.Remove(site);
            }
            else if (hive.Tick - tick > 3 && !_memory.PendingSites.ContainsKey(site))
            {
                _ordered.Remove(site);
                _takenSites[site] = hive.Tick + _tune.FailedSiteTicks;
            }
        }
    }

    private int CommandCentersWanted(int workers) => _tune.CommandCenterAtWorkers.Count(w => workers >= w);

    /// <summary>
    /// Where a new drop-off would serve the most grubs nobody serves yet. Near: fields we already dig at.
    /// Otherwise: safe fields anywhere, worth more the closer they are to our main base.
    /// </summary>
    private (int X, int Y)? DropOffWanted(HiveTick hive, bool onlyNear)
    {
        var cc = hive.View.CommandCenter!;
        (int X, int Y)? best = null;
        var bestScore = 0.0;
        foreach (var field in hive.Ore.Fields)
        {
            var nearest = hive.DropOffs.Count == 0 ? 99 : hive.DropOffs.Min(d => Cheb(d.X, d.Y, field.X, field.Y));
            var minOre = (int)(_tune.ExpansionMinOre * _grubScale);
            if (onlyNear ? nearest > 8 : nearest <= 6 || field.Total < minOre || !SafeField(hive, field))
            {
                continue;
            }

            var site = hive.Ore.DropOffSite(field.Spots, hive.IsOpen, hive.DropOffs, hive.Steps);
            if (site is not { } s || s.Served < (onlyNear ? (int)(1500 * _grubScale) : minOre))
            {
                continue;
            }

            if (hive.IsDangerous(s.X, s.Y) || _takenSites.Keys.Any(t => Cheb(t.X, t.Y, s.X, s.Y) <= 3))
            {
                continue;
            }

            // Closer to us than to any enemy is better; in the middle of the map, less so.
            var mine = Cheb(cc.X, cc.Y, s.X, s.Y);
            var theirs = EnemyStartDistance(hive, s.X, s.Y);
            var score = s.Served / (double)(mine + 12) * Math.Clamp(1.0 + ((theirs - mine) / 20.0), 0.3, 1.5);
            if (score > bestScore)
            {
                bestScore = score;
                best = (s.X, s.Y);
            }
        }

        return best;
    }

    private static int EnemyStartDistance(HiveTick hive, int x, int y) =>
        hive.View.State.Players.Where(p => p.Slot != hive.Me && !p.Eliminated).Select(p => Cheb(p.StartPosition.X, p.StartPosition.Y, x, y)).DefaultIfEmpty(99).Min();

    /// <summary>A field away from every enemy building and start we know of.</summary>
    private bool SafeField(HiveTick hive, OreField field)
    {
        var r = _tune.ExpansionEnemyDistance;
        var state = hive.View.State;
        if (state.Players.Any(p => p.Slot != hive.Me && !p.Eliminated && Cheb(p.StartPosition.X, p.StartPosition.Y, field.X, field.Y) <= r))
        {
            return false;
        }

        return !hive.View.EnemyBuildings.Any(b => Cheb(b.X, b.Y, field.X, field.Y) <= r - 4)
               && !_takenSites.Keys.Any(t => Cheb(t.X, t.Y, field.X, field.Y) <= r - 4)
               && !state.RememberedBuildings.Any(b => Cheb(b.X, b.Y, field.X, field.Y) <= r - 4);
    }

    private (int X, int Y)? TowerWanted(HiveTick hive)
    {
        var view = hive.View;
        if ((view.Workers.Count < _tune.TowersFromWorkers && !_emergency && !_outgunned) || _tune.TowersPerBase == 0)
        {
            return null;
        }

        var towers = view.MyBuildings.Where(b => b.Type == BuildingType.GuardTower).Select(b => (b.X, b.Y))
            .Concat(_memory.PendingSites.Where(p => p.Value.Type == BuildingType.GuardTower).Select(p => p.Key)).ToList();
        foreach (var cc in view.MyBuildings.Where(b => b.Type == BuildingType.CommandCenter && b.Completed).OrderBy(b => b.Id))
        {
            if (towers.Count(t => Cheb(t.X, t.Y, cc.X, cc.Y) <= 6) >= _tune.TowersPerBase)
            {
                continue;
            }

            var (ex, ey) = EnemyDirection(hive, cc.X, cc.Y);
            return FindSite(hive, cc.X + (ex * 3), cc.Y + (ey * 3), cc.X, cc.Y);
        }

        return null;
    }

    private bool PlaceNearBase(HiveTick hive, BuildingType type)
    {
        var cc = hive.View.CommandCenter!;
        var (ex, ey) = EnemyDirection(hive, cc.X, cc.Y);
        var site = FindSite(hive, cc.X + (ex * 4), cc.Y + (ey * 4), cc.X, cc.Y);
        return site is null || Place(hive, type, site.Value, helpers: type == BuildingType.ResourceDepot ? 0 : 1);
    }

    /// <summary>Orders the building if it can be paid for; false when it can't (yet).</summary>
    private bool Place(HiveTick hive, BuildingType type, (int X, int Y) site, int helpers = 0)
    {
        var cost = hive.View.Rules.Buildings.First(b => b.Type == type).Cost;
        if (Budget(hive) < cost)
        {
            hive.Reserved = cost; // fighters leave this much for it
            return false;
        }

        if (hive.Turn.PlaceBuilding(type, site) is not Attempt.Done)
        {
            return true;
        }

        _ordered[site] = hive.Tick;
        hive.Reserve(site.X, site.Y);
        foreach (var helper in hive.View.Workers
                     .Where(w => !hive.Turn.IsClaimed(w.Id) && !_memory.IsBuilder(w.Id) && w.Activity != UnitActivity.Building && w.Carrying < 6)
                     .OrderBy(w => Cheb(w.X, w.Y, site.X, site.Y)).Take(helpers))
        {
            hive.Turn.Claim(helper.Id);
            hive.Turn.Add(new CommandRequest { Type = CommandType.Build, UnitIds = [helper.Id], BuildingType = type, X = site.X, Y = site.Y });
        }

        return true;
    }

    /// <summary>The nearest open tile to (ax, ay) with a free tile all round, off the ore, out of the enemy's reach, a few tiles from (cx, cy).</summary>
    private static (int X, int Y)? FindSite(HiveTick hive, int ax, int ay, int cx, int cy)
    {
        (int X, int Y)? best = null;
        var bestScore = int.MaxValue;
        for (var y = ay - 7; y <= ay + 7; y++)
        {
            for (var x = ax - 7; x <= ax + 7; x++)
            {
                if (!hive.IsOpen(x, y) || Cheb(x, y, cx, cy) < 2 || !RoomAround(hive, x, y) || hive.IsDangerous(x, y))
                {
                    continue;
                }

                var score = (Cheb(x, y, ax, ay) * 4) + Cheb(x, y, cx, cy);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                }
            }
        }

        return best;
    }

    private static bool RoomAround(HiveTick hive, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && !hive.IsOpen(x + dx, y + dy))
                {
                    return false;
                }
            }
        }

        // Stay off the diggers' tiles beside the piles.
        return !hive.Ore.Spots.Any(s => Cheb(s.X, s.Y, x, y) <= 2);
    }

    /// <summary>The step (-1..1 each way) from here toward the nearest enemy start still in the game.</summary>
    private static (int Dx, int Dy) EnemyDirection(HiveTick hive, int x, int y)
    {
        var enemy = hive.View.EnemyStart(x, y);
        return enemy is null
            ? (Math.Sign((hive.View.Map.Width / 2) - x), Math.Sign((hive.View.Map.Height / 2) - y))
            : (Math.Sign(enemy.X - x), Math.Sign(enemy.Y - y));
    }

    // ------------------------------------------------------------------ research and fighters

    private int Budget(HiveTick hive) => hive.Turn.Budget - hive.Spent;

    private void Research(HiveTick hive)
    {
        var view = hive.View;
        if (view.Workers.Count < _tune.ResearchFromWorkers)
        {
            return;
        }

        foreach (var upgrade in _outgunned || _rushed ? FightFirst : _tune.EarlyFightUpgrades ? FightSoon : ResearchOrder)
        {
            var stats = view.Rules.Upgrades.First(u => u.Type == upgrade);
            if (Budget(hive) < stats.Cost)
            {
                return;
            }

            var attempt = hive.Turn.TryResearch(upgrade);
            if (attempt is Attempt.Busy or Attempt.NeedsBuilding or Attempt.TooExpensive)
            {
                return;
            }
        }
    }

    private void TrainFighters(HiveTick hive)
    {
        var view = hive.View;
        var rally = RallyPoint(hive);
        foreach (var barracks in view.MyBuildings.Where(b => b.Type == BuildingType.Barracks && b.Completed && b.Production.Count < 2).OrderBy(b => b.Production.Count))
        {
            var type = _tune.ArcherEvery > 0 && _fighterCursor % _tune.ArcherEvery == _tune.ArcherEvery - 1 ? UnitType.Archer : UnitType.Soldier;
            var cost = view.UnitCost(type);
            if (Budget(hive) - (_emergency || _rushed ? 0 : hive.Reserved) < cost)
            {
                return;
            }

            var command = new CommandRequest { Type = CommandType.Produce, BuildingId = barracks.Id, UnitType = type };
            if (barracks.Rally is not { } r || r.X != rally.X || r.Y != rally.Y)
            {
                command = command with { X = rally.X, Y = rally.Y };
            }

            hive.Turn.Add(command);
            SpendBudget(hive, cost);
            _fighterCursor++;
        }
    }
}
