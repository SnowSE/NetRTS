using NetRts.Protocol;

namespace NetRts.Bots.Formics;

public sealed partial class FormicsBot
{
    private bool _attacking;
    private double _peakIncome;

    /// <summary>The strongest enemy army is probably bigger than ours: fighters come before more workers.</summary>
    private bool _outnumbered;
    private int _launchValue;
    private readonly HashSet<int> _pulledWorkers = [];

    private void CommandArmy(HiveTick hive)
    {
        var view = hive.View;
        var army = view.Army.Where(u => u.Type != UnitType.Trebuchet).ToList();
        var armyValue = army.Sum(u => view.UnitCost(u.Type));
        _emergency = false;

        // Keep an army worth guarding the workers with, and one that can stand up to the strongest enemy we expect.
        var strongest = StrongestEnemyArmy(view);
        var guard = view.MyBuildings.Any(b => b.Type == BuildingType.Barracks && b.Completed) ? view.Workers.Count * _tune.ArmyPerWorker : 0;
        _outnumbered = armyValue < Math.Max(guard, strongest * _tune.DefendShare);

        var threats = Threats(hive);
        var threatValue = threats.Sum(e => view.UnitCost(e.Type));
        var defenders = new List<UnitDto>();
        if (threats.Count > 0)
        {
            // Everyone at home answers; while attacking, only as many as it takes (the nearest first).
            var (tx, ty) = (threats[0].X, threats[0].Y);
            var needed = _attacking ? threatValue * 2 : int.MaxValue;
            foreach (var u in army.OrderBy(u => Cheb(u.X, u.Y, tx, ty)))
            {
                if (needed <= 0)
                {
                    break;
                }

                defenders.Add(u);
                needed -= view.UnitCost(u.Type);
            }

            Fight(hive, defenders, (tx, ty));
            var near = defenders.Where(u => Cheb(u.X, u.Y, tx, ty) <= 8).Sum(u => view.UnitCost(u.Type));
            _emergency = threatValue > 150 && near < threatValue * 1.2;
            if (_emergency || threats.Any(t => t.Type != UnitType.Worker && near == 0))
            {
                // Workers join in when together we can win; against a real army they get out of its way instead.
                var fighters = threats.Where(t => t.Type != UnitType.Worker).ToList();
                var helpers = view.Workers.Count(w => fighters.Any(t => Cheb(t.X, t.Y, w.X, w.Y) <= 6));
                if (near + (helpers * _tune.WorkerFightValue) >= threatValue * 1.3)
                {
                    PullWorkers(hive, threats, threatValue, near);
                }
                else
                {
                    Evacuate(hive, fighters);
                }
            }
        }

        if (!_emergency || threats.Count == 0)
        {
            ReleaseWorkers(hive, threats.Count > 0);
        }

        var rest = army.Where(u => !defenders.Contains(u)).ToList();
        var restValue = rest.Sum(u => view.UnitCost(u.Type));
        var target = AttackTarget(hive, rest);
        var enemyValue = strongest + (target is { } at ? TowersNear(view, at.X, at.Y) * _tune.TowerValue : 0);

        // With our grubs running out, sitting still loses: go and take theirs.
        _peakIncome = Math.Max(_peakIncome, hive.IncomePerTick);
        var starving = hive.Tick > 300 && hive.IncomePerTick < _peakIncome * _tune.StarvingIncomeShare;
        var (enough, edge) = starving ? (Math.Min(_tune.AttackValue, 1500), _tune.StarvingAttackEdge) : (_tune.AttackValue, _tune.AttackEdge);
        if (!_attacking && rest.Count > 0 && ((restValue >= enough && restValue >= enemyValue * edge) || hive.Tick >= _tune.AllInTick))
        {
            _attacking = true;
            _launchValue = restValue;
        }
        else if (_attacking && hive.Tick < _tune.AllInTick && restValue < _launchValue * _tune.RetreatShare)
        {
            _attacking = false;
        }

        _launchValue = Math.Max(_launchValue, restValue);
        if (_attacking && target is { } spot)
        {
            Fight(hive, rest, spot);
            return;
        }

        _attacking = _attacking && rest.Count > 0;
        var rally = RallyPoint(hive);
        Fight(hive, rest.Where(u => u.Activity != UnitActivity.Idle || Cheb(u.X, u.Y, rally.X, rally.Y) > 3).ToList(), rally, regroup: true);
    }

    /// <summary>Enemy guard towers we know of within reach of a spot.</summary>
    private static int TowersNear(BotView view, int x, int y) =>
        view.EnemyBuildings.Count(b => b.Type == BuildingType.GuardTower && Cheb(b.X, b.Y, x, y) <= 8)
        + view.State.RememberedBuildings.Count(b => b.Type == BuildingType.GuardTower && Cheb(b.X, b.Y, x, y) <= 8);

    /// <summary>Visible enemies near any of our buildings, nearest our main Command Center first.</summary>
    private List<UnitDto> Threats(HiveTick hive)
    {
        var view = hive.View;
        var cc = view.CommandCenter!;
        var r = _tune.DefenseRadius;
        return view.EnemyUnits
            .Where(e => view.MyBuildings.Any(b => Cheb(b.X, b.Y, e.X, e.Y) <= r))
            .OrderBy(e => Cheb(e.X, e.Y, cc.X, cc.Y)).ThenBy(e => e.Id)
            .ToList();
    }

    /// <summary>Workers near the raiders fight too when the army can't hold: forty hit points each, and many of them.</summary>
    private void PullWorkers(HiveTick hive, List<UnitDto> threats, int threatValue, int defenders)
    {
        var view = hive.View;
        var fighters = threats.Where(t => t.Type != UnitType.Worker).ToList();
        if (fighters.Count == 0)
        {
            return;
        }

        var want = Math.Max(0, (threatValue * 3 / 2) - defenders) / 50;
        foreach (var w in view.Workers
                     .Where(w => !hive.Turn.IsClaimed(w.Id) && fighters.Any(t => Cheb(t.X, t.Y, w.X, w.Y) <= 6))
                     .OrderBy(w => fighters.Min(t => Cheb(t.X, t.Y, w.X, w.Y))).Take(want))
        {
            _pulledWorkers.Add(w.Id);
            hive.Turn.Claim(w.Id);
            var target = fighters.OrderBy(t => Cheb(t.X, t.Y, w.X, w.Y)).ThenBy(t => t.Hp).First();
            if (w.TargetId != target.Id)
            {
                hive.Turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = [w.Id], TargetId = target.Id });
            }
        }
    }

    /// <summary>Workers near an army we can't stop dig somewhere safe instead.</summary>
    private void Evacuate(HiveTick hive, List<UnitDto> fighters)
    {
        foreach (var w in hive.View.Workers.Where(w => !hive.Turn.IsClaimed(w.Id) && w.Activity != UnitActivity.Building
                                                       && fighters.Any(t => Cheb(t.X, t.Y, w.X, w.Y) <= 7)))
        {
            _pulledWorkers.Remove(w.Id);
            if (BestDeposit(hive, w.X, w.Y, null) is { } spot && !hive.IsDangerous(spot.X, spot.Y))
            {
                hive.Turn.Claim(w.Id);
                if (w.TargetId != spot.Id)
                {
                    hive.Turn.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [w.Id], TargetId = spot.Id });
                }
            }
        }
    }

    /// <summary>Pulled workers go back to digging once the danger has passed.</summary>
    private void ReleaseWorkers(HiveTick hive, bool stillThreatened)
    {
        if (_pulledWorkers.Count == 0)
        {
            return;
        }

        var back = hive.View.Workers.Where(w => _pulledWorkers.Contains(w.Id) && (!stillThreatened || w.Activity == UnitActivity.Idle)).ToList();
        foreach (var w in back)
        {
            _pulledWorkers.Remove(w.Id);
            if (BestDeposit(hive, w.X, w.Y, null) is { } spot)
            {
                hive.Turn.Claim(w.Id);
                hive.Turn.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [w.Id], TargetId = spot.Id });
            }
        }

        _pulledWorkers.RemoveWhere(id => !hive.View.Workers.Any(w => w.Id == id));
    }

    /// <summary>Where the swarm goes: the known enemy building nearest the army, else the nearest enemy start, else unexplored grubs.</summary>
    private (int X, int Y)? AttackTarget(HiveTick hive, List<UnitDto> army)
    {
        if (army.Count == 0)
        {
            return null;
        }

        var view = hive.View;
        var cx = (int)Math.Round(army.Average(u => u.X));
        var cy = (int)Math.Round(army.Average(u => u.Y));
        var known = view.EnemyBuildings.Select(b => (b.X, b.Y, Type: b.Type))
            .Concat(view.State.RememberedBuildings.Select(b => (b.X, b.Y, b.Type)))
            .Concat(_enemySites.Select(t => (t.X, t.Y, Type: BuildingType.CommandCenter)))
            .OrderBy(b => Cheb(b.X, b.Y, cx, cy) + (TowersNear(view, b.X, b.Y) * _tune.TowerDetour) - (b.Type == BuildingType.CommandCenter ? 4 : 0))
            .Select(b => ((int X, int Y)?)(b.X, b.Y))
            .FirstOrDefault();
        if (known is not null)
        {
            return known;
        }

        var starts = view.State.Players
            .Where(p => p.Slot != hive.Me && !p.Eliminated && !_memory.HasSeen(p.StartPosition.X, p.StartPosition.Y))
            .Select(p => (p.StartPosition.X, p.StartPosition.Y))
            .OrderBy(s => Cheb(s.X, s.Y, cx, cy)).ToList();
        if (starts.Count > 0)
        {
            return starts[0];
        }

        // Hunt: grub fields we haven't laid eyes on yet are where a hiding enemy builds.
        var hidden = hive.Ore.Spots.Where(s => !_memory.HasSeen(s.X, s.Y)).OrderBy(s => Cheb(s.X, s.Y, cx, cy)).FirstOrDefault();
        if (hidden is not null)
        {
            return (hidden.X, hidden.Y);
        }

        for (var r = 4; r < Math.Max(view.Map.Width, view.Map.Height); r += 4)
        {
            for (var y = Math.Max(0, cy - r); y <= Math.Min(view.Map.Height - 1, cy + r); y += 4)
            {
                for (var x = Math.Max(0, cx - r); x <= Math.Min(view.Map.Width - 1, cx + r); x += 4)
                {
                    if (!_memory.HasSeen(x, y) && !hive.Ore.IsRock(x, y))
                    {
                        return (x, y);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Where the army waits: ahead of the main Command Center, toward the enemy.</summary>
    private static (int X, int Y) RallyPoint(HiveTick hive)
    {
        var cc = hive.View.CommandCenter!;
        var (dx, dy) = EnemyDirection(hive, cc.X, cc.Y);
        for (var d = 6; d >= 2; d--)
        {
            var (x, y) = (cc.X + (dx * d), cc.Y + (dy * d));
            if (hive.IsOpen(x, y))
            {
                return (x, y);
            }
        }

        return (cc.X, cc.Y);
    }

    /// <summary>
    /// Sends a group at a spot. Whoever has an enemy in reach shoots the one that dies soonest (shared out so no
    /// shot is wasted on a unit already as good as dead); the rest attack-move to the spot.
    /// </summary>
    private void Fight(HiveTick hive, List<UnitDto> group, (int X, int Y) spot, bool regroup = false)
    {
        if (group.Count == 0)
        {
            return;
        }

        var view = hive.View;
        var weapons = view.State.You.Upgrades.Count(u => u is UpgradeType.Weapons1 or UpgradeType.Weapons2) * 2;
        var planned = new Dictionary<int, int>();
        var byTarget = new Dictionary<int, List<int>>();
        var free = new List<UnitDto>();

        foreach (var u in group)
        {
            hive.Turn.Claim(u.Id);
            var stats = view.Rules.Units.First(s => s.Type == u.Type);
            var target = _tune.FocusFire ? PickTarget(view, u, stats, weapons, planned) : null;
            if (target is null)
            {
                free.Add(u);
                continue;
            }

            if (u.TargetId == target.Value.Id && u.Activity == UnitActivity.Attacking)
            {
                continue;
            }

            if (!byTarget.TryGetValue(target.Value.Id, out var ids))
            {
                byTarget[target.Value.Id] = ids = [];
            }

            ids.Add(u.Id);
        }

        foreach (var (target, ids) in byTarget)
        {
            hive.Turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = [.. ids], TargetId = target });
        }

        if (free.Count > 0)
        {
            hive.Turn.Order(free, CommandType.Attack, spot, retarget: !regroup);
        }
    }

    /// <summary>The enemy in this unit's reach to shoot: units before buildings, the one closest to dying (by threat) first.</summary>
    private static (int Id, int Hp)? PickTarget(BotView view, UnitDto u, UnitStatsDto stats, int weapons, Dictionary<int, int> planned)
    {
        (int Id, int Hp)? best = null;
        var bestScore = double.MaxValue;
        foreach (var e in view.EnemyUnits)
        {
            var d = Cheb(u.X, u.Y, e.X, e.Y);
            if (d > stats.Range || d < stats.MinRange)
            {
                continue;
            }

            var left = e.Hp - planned.GetValueOrDefault(e.Id);
            var score = left <= 0 ? 1e6 + e.Hp : left / Danger(e.Type);
            if (score < bestScore)
            {
                bestScore = score;
                best = (e.Id, e.Hp);
            }
        }

        if (best is null)
        {
            foreach (var b in view.EnemyBuildings)
            {
                var d = Cheb(u.X, u.Y, b.X, b.Y);
                if (d > stats.Range || d < stats.MinRange)
                {
                    continue;
                }

                var left = b.Hp - planned.GetValueOrDefault(b.Id);
                var score = (left <= 0 ? 1e6 : left) / (b.Type == BuildingType.GuardTower ? 3.0 : b.Type == BuildingType.CommandCenter ? 1.5 : 1.0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (b.Id, b.Hp);
                }
            }
        }

        if (best is { } chosen)
        {
            var enemyArmor = view.EnemyUnits.FirstOrDefault(e => e.Id == chosen.Id) is { } eu ? view.Rules.Units.First(s => s.Type == eu.Type).Armor : 1;
            planned[chosen.Id] = planned.GetValueOrDefault(chosen.Id) + Math.Max(1, stats.Damage + weapons - enemyArmor);
        }

        return best;
    }

    private static double Danger(UnitType type) => type switch
    {
        UnitType.Archer => 1.6,
        UnitType.Trebuchet => 2.0,
        UnitType.Soldier => 1.0,
        UnitType.Scout => 0.7,
        _ => 0.5,
    };
}
