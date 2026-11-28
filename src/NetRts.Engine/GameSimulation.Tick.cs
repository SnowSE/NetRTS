using NetRts.Protocol;

namespace NetRts.Engine;

public sealed partial class GameSimulation
{
    private enum StepResult
    {
        Arrived,
        Moving,
        Stuck,
    }

    private readonly record struct Attack(Entity Attacker, int AttackerOwner, Entity Target, int Damage);

    /// <summary>
    /// Advances the match by one tick:
    /// queued commands → production &amp; research → unit behavior and movement →
    /// simultaneous combat → deaths → elimination / victory check.
    /// </summary>
    public void Step()
    {
        if (Status != MatchStatus.Active)
        {
            return;
        }

        Tick++;
        _visibility = null;
        foreach (var p in _players)
        {
            p.IncomeWindow[Tick % p.IncomeWindow.Length] = 0;
        }

        ExecuteQueuedCommands();
        ProgressBuildings();

        // Decisions this tick are made against what each player could see at the start of it.
        var attacks = new List<Attack>();
        ChooseDiggers();
        foreach (var unit in _units.ToList())
        {
            if (unit.Cooldown > 0)
            {
                unit.Cooldown--;
            }

            UpdateUnit(unit, attacks);
        }

        ResolveCells();

        _towerShots.Clear();
        foreach (var tower in _buildings.Where(b => b.Completed && b.Stats.Damage > 0).ToList())
        {
            if (tower.Cooldown > 0)
            {
                tower.Cooldown--;
            }

            var target = InTreaty ? null : FindEnemy(tower.Owner, tower.Position, tower.Stats.Range);
            if (target is not null && tower.Cooldown == 0)
            {
                attacks.Add(new Attack(tower, tower.Owner, target, DamageAgainst(tower, target)));
                tower.Cooldown = TowerCooldown(tower);
                _towerShots.Add(new TowerShot(tower.Id, tower.Owner, tower.Position, target.Id, target.Position));
            }
        }

        LandShells(attacks);
        RegrowDeposits();

        var killers = new Dictionary<int, Attack>();
        foreach (var attack in attacks)
        {
            var crossFire = attack.Target.Owner >= 0 && attack.Target.Owner != attack.AttackerOwner;
            if (crossFire && InTreaty)
            {
                continue;
            }

            var wasAlive = !attack.Target.Dead;
            if (crossFire && wasAlive)
            {
                _players[attack.AttackerOwner].DamageDealt += Math.Min(attack.Damage, attack.Target.Hp);
            }

            attack.Target.Hp -= attack.Damage;
            if (wasAlive && attack.Target.Dead)
            {
                killers[attack.Target.Id] = attack;
            }
        }

        RemoveDead(killers);

        foreach (var player in _players.Where(p => !p.Eliminated))
        {
            if (!_buildings.Any(b => b.Owner == player.Slot && b.Type == BuildingType.CommandCenter))
            {
                Eliminate(player, "lost every Command Center");
            }
        }

        foreach (var player in _players)
        {
            if (player.SerumUntilTick - Rules.SerumTicks + 1 == Tick)
            {
                Announce(player, "SuperSoldiers", $"Super soldiers activated. {Rules.SerumTicks} ticks.");
            }

            if (player.SerumUntilTick == Tick)
            {
                Announce(player, "SuperSoldiersEnded", "The serum wore off.");
            }
        }

        CheckForEnd(MatchEndReason.Elimination);
        _visibility = null;
        UpdateMemories();
        UpdateStatistics();
    }

    /// <summary>Peaks and the score race, for <see cref="PlayerMatchStatsDto"/>.</summary>
    private void UpdateStatistics()
    {
        var workers = new int[_players.Count];
        foreach (var unit in _units)
        {
            if (unit.Type == UnitType.Worker)
            {
                workers[unit.Owner]++;
            }
        }

        foreach (var player in _players.Where(p => !p.Eliminated))
        {
            player.PeakWorkers = Math.Max(player.PeakWorkers, workers[player.Slot]);
            player.PeakResources = Math.Max(player.PeakResources, player.Resources);
        }

        if (Tick >= 100 && Tick % 10 == 0)
        {
            var totals = _players.Where(p => !p.Eliminated).Select(p => (Player: p, ScoreOf(p).Total)).ToList();
            var leader = totals.Count > 0 ? totals.Max(t => t.Total) : 0;
            if (leader > 0)
            {
                foreach (var (player, total) in totals)
                {
                    player.LowestScoreSharePercent = Math.Min(player.LowestScoreSharePercent, total * 100 / leader);
                }
            }
        }
    }

    /// <summary>Each player remembers enemy buildings they've seen, and forgets them once they see the tile empty.</summary>
    private void UpdateMemories()
    {
        foreach (var player in _players.Where(p => !p.Eliminated))
        {
            var visibility = VisibilityFor(player.Slot);
            foreach (var b in _buildings.Where(b => b.Owner != player.Slot && IsVisible(visibility, b.Position)))
            {
                if (b.Type == BuildingType.CommandCenter)
                {
                    player.EnemyCommandCenterSeenTick ??= Tick;
                }

                player.Remembered[b.Id] = new RememberedBuildingDto
                {
                    Id = b.Id,
                    Owner = b.Owner,
                    Type = b.Type,
                    X = b.Position.X,
                    Y = b.Position.Y,
                    Hp = b.Hp,
                    MaxHp = b.MaxHp,
                    Completed = b.Completed,
                    LastSeenTick = Tick,
                };
            }

            var gone = player.Remembered.Values
                .Where(r => IsVisible(visibility, new Point(r.X, r.Y)) && !_entities.ContainsKey(r.Id))
                .Select(r => r.Id)
                .ToList();
            foreach (var id in gone)
            {
                player.Remembered.Remove(id);
            }
        }
    }

    private void ProgressBuildings()
    {
        foreach (var building in _buildings.Where(b => b.Completed).ToList())
        {
            var owner = _players[building.Owner];
            if (building.Production.Count > 0)
            {
                var item = building.Production[0];
                item.Progress = Math.Min(item.Progress + 1, Rules.Units[item.Type].BuildTicks);
                if (item.Progress >= Rules.Units[item.Type].BuildTicks && FindFreeTileNear(building.Position, Rules.Units[item.Type].Occupancy) is { } spawn)
                {
                    building.Production.RemoveAt(0);
                    var unit = AddUnit(building.Owner, item.Type, spawn);
                    SendToRally(unit, building);
                    owner.UnitsProduced++;
                    owner.UnitsTrained[item.Type] = owner.UnitsTrained.GetValueOrDefault(item.Type) + 1;
                    if (item.Type != UnitType.Worker)
                    {
                        owner.FirstCombatUnitTick ??= Tick;
                    }

                    AddEvent(owner, "UnitProduced", $"{item.Type} {unit.Id} ready at {spawn}", unit.Id, spectator: false);
                }
            }

            if (building.Research is { } research)
            {
                research.Progress++;
                if (research.Progress >= Rules.Upgrades[research.Type].ResearchTicks)
                {
                    building.Research = null;
                    owner.Upgrades.Add(research.Type);
                    AddEvent(owner, "ResearchCompleted", $"Research complete: {research.Type}", building.Id);
                }
            }
        }
    }

    private void SendToRally(UnitEntity unit, BuildingEntity building)
    {
        if (building.Rally is not { } rally || rally == unit.Position)
        {
            return;
        }

        var depositId = _depositAt[Index(rally)];
        if (depositId == 0 && _buildingAt[Index(rally)] is var farmId and not 0 && _entities[farmId] is BuildingEntity { Type: BuildingType.Farm } farm && farm.Owner == unit.Owner)
        {
            depositId = farmId;
        }

        if (unit.Type == UnitType.Worker && depositId != 0)
        {
            unit.Order = OrderKind.Gather;
            unit.TargetId = depositId;
            unit.LastDepositPosition = rally;
        }
        else
        {
            unit.Order = OrderKind.Move;
            unit.Destination = rally;
        }

        unit.Activity = UnitActivity.Moving;
    }

    private void UpdateUnit(UnitEntity unit, List<Attack> attacks)
    {
        if (unit.Dead)
        {
            return;
        }

        if (unit.Type == UnitType.Worker)
        {
            unit.IdleTicks = unit.Order == OrderKind.Idle ? unit.IdleTicks + 1 : 0;
            var owner = _players[unit.Owner];
            owner.LongestWorkerIdleTicks = Math.Max(owner.LongestWorkerIdleTicks, unit.IdleTicks);
        }

        switch (unit.Order)
        {
            case OrderKind.Idle:
                unit.Activity = UnitActivity.Idle;
                if (unit.Type != UnitType.Worker && FindTarget(unit) is { } nearby)
                {
                    unit.Activity = UnitActivity.Attacking;
                    TryAttack(unit, nearby, attacks);
                }

                break;

            case OrderKind.Move:
                unit.Activity = UnitActivity.Moving;
                if (StepToward(unit, unit.Destination!.Value, 0) != StepResult.Moving)
                {
                    unit.ClearOrder();
                }

                break;

            case OrderKind.AttackMove:
            {
                var enemy = FindEnemyInSight(unit);
                if (enemy is not null)
                {
                    Engage(unit, enemy, attacks);
                }
                else
                {
                    unit.Activity = UnitActivity.Moving;
                    if (StepToward(unit, unit.Destination!.Value, 0) != StepResult.Moving)
                    {
                        unit.ClearOrder();
                    }
                }

                break;
            }

            case OrderKind.AttackTarget:
            {
                var target = unit.TargetId is { } id ? _entities.GetValueOrDefault(id) : null;
                if (target is null || target.Dead || target is DepositEntity || !IsVisible(VisibilityFor(unit.Owner), target.Position))
                {
                    unit.ClearOrder();
                    break;
                }

                Engage(unit, target, attacks);
                break;
            }

            case OrderKind.Gather:
                UpdateGatherer(unit);
                break;

            case OrderKind.Build:
                UpdateBuilder(unit);
                break;
        }
    }

    private void Engage(UnitEntity unit, Entity target, List<Attack> attacks)
    {
        unit.Activity = UnitActivity.Attacking;
        if (unit.Position.Chebyshev(target.Position) <= unit.Stats.Range)
        {
            // Inside the minimum range a siege unit holds its fire; the target has to back off (or be dealt with by others).
            unit.ClearPath();
            if (unit.Position.Chebyshev(target.Position) >= unit.Stats.MinRange)
            {
                TryAttack(unit, target, attacks);
            }

            return;
        }

        if (StepToward(unit, target.Position, unit.Stats.Range) == StepResult.Stuck)
        {
            unit.ClearOrder();
        }
    }

    private void TryAttack(UnitEntity unit, Entity target, List<Attack> attacks)
    {
        if (unit.Cooldown > 0)
        {
            return;
        }

        if (unit.Stats.ShotTicks > 0)
        {
            _shells.Add(new Shell(unit, unit.Position, target.Position, Tick, Tick + unit.Stats.ShotTicks));
        }
        else
        {
            attacks.Add(new Attack(unit, unit.Owner, target, DamageAgainst(unit, target)));
        }

        unit.Cooldown = unit.Stats.AttackCooldown;
    }

    /// <summary>
    /// Shells due this tick hit every enemy of their owner on the tile they land on, and with
    /// <see cref="RuleSet.ShellSplashPercent"/> that share of the damage on the 8 tiles around it; whoever
    /// moved away is missed.
    /// </summary>
    private void LandShells(List<Attack> attacks)
    {
        foreach (var shell in _shells.Where(s => s.LandsTick <= Tick).ToList())
        {
            _shells.Remove(shell);
            HitTile(shell, shell.Target, 100, attacks);
            if (Rules.ShellSplashPercent <= 0)
            {
                continue;
            }

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var tile = new Point(shell.Target.X + dx, shell.Target.Y + dy);
                    if ((dx, dy) != (0, 0) && Map.InBounds(tile.X, tile.Y))
                    {
                        HitTile(shell, tile, Rules.ShellSplashPercent, attacks);
                    }
                }
            }
        }
    }

    private void HitTile(Shell shell, Point tile, int percent, List<Attack> attacks)
    {
        var owner = shell.Attacker.Owner;
        foreach (var unit in _units.Where(u => u.Owner != owner && !u.Dead && u.Position == tile))
        {
            attacks.Add(new Attack(shell.Attacker, owner, unit, DamageAgainst(shell.Attacker, unit, percent)));
        }

        if (_buildingAt[Index(tile)] is var id and not 0 && _entities[id] is BuildingEntity { Dead: false } building && building.Owner != owner)
        {
            attacks.Add(new Attack(shell.Attacker, owner, building, DamageAgainst(shell.Attacker, building, percent)));
        }
    }

    /// <summary>
    /// What an idle combat unit fires at: the closest visible enemy it can hit from where it stands.
    /// Siege units, whose shells land late, pick targets that are standing still first.
    /// </summary>
    private Entity? FindTarget(UnitEntity unit)
    {
        var visibility = VisibilityFor(unit.Owner);
        bool InReach(Entity e) => InStrikeZone(unit, e.Position) && IsVisible(visibility, e.Position);
        if (unit.Stats.ShotTicks > 0
            && ClosestEnemy(unit.Owner, unit.Position, e => InReach(e) && !IsMoving(e)) is { } still)
        {
            return still;
        }

        return ClosestEnemy(unit.Owner, unit.Position, InReach);
    }

    private static bool InStrikeZone(UnitEntity unit, Point target) =>
        unit.Position.Chebyshev(target) is var d && d <= unit.Stats.Range && d >= unit.Stats.MinRange;

    private static bool IsMoving(Entity e) => e is UnitEntity { Path.Count: > 0 };

    /// <summary>Damage of one hit after armor; <paramref name="percent"/> scales it before armor (splash).</summary>
    private int DamageAgainst(Entity attacker, Entity target, int percent = 100)
    {
        var damage = attacker switch
        {
            UnitEntity u => (u.Stats.Damage + _players[u.Owner].WeaponsBonus) * Factor(u),
            BuildingEntity b => b.Stats.Damage + _players[b.Owner].WeaponsBonus,
            _ => 0,
        };
        damage = damage * percent / 100;
        var armor = target switch
        {
            UnitEntity u => u.Stats.Armor + _players[u.Owner].ArmorBonus,
            BuildingEntity { Completed: true } b => b.Stats.Armor + _players[b.Owner].FortifyBonus,
            _ => 0,
        };
        return Math.Max(1, damage - armor);
    }

    /// <summary>Closest enemy within Chebyshev <paramref name="range"/> (a guard tower's target); units are preferred over buildings.</summary>
    private Entity? FindEnemy(int owner, Point from, int range)
    {
        var visibility = VisibilityFor(owner);
        return ClosestEnemy(owner, from, e => from.Chebyshev(e.Position) <= range && IsVisible(visibility, e.Position));
    }

    /// <summary>Closest enemy inside this unit's own sight radius and outside its minimum range (used by attack-move).</summary>
    private Entity? FindEnemyInSight(UnitEntity unit)
    {
        var r2 = unit.Stats.Vision * unit.Stats.Vision;
        return ClosestEnemy(unit.Owner, unit.Position,
            e => unit.Position.DistanceSquared(e.Position) <= r2 && unit.Position.Chebyshev(e.Position) >= unit.Stats.MinRange);
    }

    /// <summary>
    /// The living enemy of <paramref name="owner"/> that <paramref name="inReach"/> accepts and is nearest
    /// <paramref name="from"/>: units before buildings, then the weakest, then the lowest id.
    /// </summary>
    private Entity? ClosestEnemy(int owner, Point from, Func<Entity, bool> inReach) =>
        (Entity?)Closest(_units.Where(u => u.Owner != owner && !u.Dead && inReach(u)), from)
        ?? Closest(_buildings.Where(b => b.Owner != owner && !b.Dead && inReach(b)), from);

    private static T? Closest<T>(IEnumerable<T> candidates, Point from)
        where T : Entity =>
        candidates.OrderBy(e => from.Chebyshev(e.Position)).ThenBy(e => e.Hp).ThenBy(e => e.Id).FirstOrDefault();

    /// <summary>Tenths of a tile this unit moves per tick.</summary>
    private int MoveSpeed(UnitEntity unit) => (unit.Stats.Speed + _players[unit.Owner].MobilityBonus) * Factor(unit);

    private int Factor(UnitEntity unit) => unit.Type == UnitType.Soldier && Tick <= unit.SerumUntilTick ? Rules.SerumFactor : 1;

    private StepResult StepToward(UnitEntity unit, Point goal, int range)
    {
        if (unit.Position.Chebyshev(goal) <= range)
        {
            unit.ClearPath();
            return StepResult.Arrived;
        }

        // Re-plan when there's no plan, the goal moved noticeably, or the next step got blocked.
        var needsPath = unit.Path.Count == 0
                        || unit.PathGoal is not { } planned
                        || unit.PathRange != range
                        || planned.Chebyshev(goal) > 1
                        || !Passable(unit.Path[0]);
        if (needsPath)
        {
            // A unit already as close as it can get (its goal walled off, and a bot sending the same order
            // every tick) would otherwise search everywhere it can reach, every tick, to find that out.
            unit.Path = _paths.CanGetCloser(unit.Position, goal, range) ? _paths.Find(unit.Position, goal, range) : [];
            unit.PathGoal = goal;
            unit.PathRange = range;
            if (unit.Path.Count == 0)
            {
                unit.MovePoints = 0;
                return StepResult.Stuck;
            }
        }

        unit.MovePoints += MoveSpeed(unit);
        while (unit.MovePoints >= Rules.MovePointsPerTile && unit.Path.Count > 0)
        {
            var next = unit.Path[0];
            if (!Passable(next))
            {
                break;
            }

            Place(unit, next);
            unit.Path.RemoveAt(0);
            unit.MovePoints -= Rules.MovePointsPerTile;
            if (unit.Position.Chebyshev(goal) <= range)
            {
                unit.ClearPath();
                return StepResult.Arrived;
            }
        }

        if (unit.Path.Count == 0)
        {
            unit.MovePoints = 0;
        }

        return StepResult.Moving;
    }

    private void UpdateGatherer(UnitEntity unit)
    {
        var player = _players[unit.Owner];
        var source = HarvestSource(unit);

        if (source is null && unit.Carrying == 0)
        {
            // Our patch ran dry: move on to the nearest other deposit near where it was.
            source = unit.LastDepositPosition is { } last ? NextSource(unit, last) : null;
            if (source is null)
            {
                unit.ClearOrder();
                return;
            }

            unit.TargetId = source.Id;
        }

        if (source is null || unit.Carrying >= Rules.WorkerCarryCapacity)
        {
            unit.GatherSpot = null;
            ReturnCargo(unit, player);
            return;
        }

        if (Crowding)
        {
            ApproachAndDig(unit, player, source);
            return;
        }

        if (unit.Position.Chebyshev(source.Position) <= 1)
        {
            Dig(unit, player, source);
            return;
        }

        unit.Activity = UnitActivity.Moving;
        if (StepToward(unit, source.Position, 1) == StepResult.Stuck)
        {
            unit.ClearOrder();
        }
    }

    /// <summary>What a gatherer digs: its patch, or its own finished farm.</summary>
    private Entity? HarvestSource(UnitEntity unit) => unit.TargetId is { } id
        ? _entities.GetValueOrDefault(id) switch
        {
            DepositEntity deposit => deposit,
            BuildingEntity { Type: BuildingType.Farm, Completed: true } farm when farm.Owner == unit.Owner => farm,
            _ => null,
        }
        : null;

    /// <summary>Where a worker whose patch ran dry goes next: the nearest deposit within 6 of it (with a cell limit, one with a free spot first).</summary>
    private Entity? NextSource(UnitEntity unit, Point last)
    {
        if (Crowding && FreeSpotNear(unit, last, 6) is { } free)
        {
            unit.GatherSpot = free.Spot;
            return free.Source;
        }

        return _deposits.Where(d => d.Position.Chebyshev(last) <= 6)
            .OrderBy(d => d.Position.Chebyshev(unit.Position)).ThenBy(d => d.Id)
            .FirstOrDefault();
    }

    /// <summary>One tick of digging beside <paramref name="source"/>; at an empty farm, replanting it instead.</summary>
    private void Dig(UnitEntity unit, PlayerState player, Entity source)
    {
        unit.ClearPath();
        if (source is BuildingEntity { Grubs: 0 } empty)
        {
            Replant(unit, player, empty);
            return;
        }

        unit.Activity = UnitActivity.Gathering;
        var rate = source is BuildingEntity ? Rules.Farm!.GatherPerTick : Rules.GatherPerTick;
        var take = Math.Min(rate + player.HarvestingBonus,
            Math.Min(source is DepositEntity d ? d.Remaining : ((BuildingEntity)source).Grubs, Rules.WorkerCarryCapacity - unit.Carrying));
        unit.Carrying += take;
        unit.LastDepositPosition = source.Position;
        if (source is BuildingEntity farm)
        {
            farm.Grubs -= take;
            if (farm.Grubs == 0)
            {
                FarmEmptied(player, farm);
            }

            return;
        }

        var deposit = (DepositEntity)source;
        deposit.Hp -= take;
        if (deposit.Remaining <= 0)
        {
            _deposits.Remove(deposit);
            _entities.Remove(deposit.Id);
            SetDepositAt(deposit.Position, 0);
            player.DepositsExhausted++;
            AddEvent(player, "DepositDepleted", $"Deposit {deposit.Id} at {deposit.Position} is exhausted", deposit.Id);
        }
    }

    private void ReturnCargo(UnitEntity unit, PlayerState player)
    {
        var dropOff = _buildings
            .Where(b => b.Owner == unit.Owner && b.Completed && b.Stats.IsDropOff)
            .OrderBy(b => b.Position.Chebyshev(unit.Position)).ThenBy(b => b.Id)
            .FirstOrDefault();

        if (dropOff is null)
        {
            unit.ClearOrder();
            return;
        }

        unit.Activity = UnitActivity.Returning;
        if (unit.Position.Chebyshev(dropOff.Position) <= 1)
        {
            unit.ClearPath();
            var banked = Math.Min(unit.Carrying, Math.Max(0, StorageCapacity(unit.Owner) - player.Resources));
            player.OreWasted += unit.Carrying - banked;
            player.Resources += banked;
            player.Gathered += banked;
            player.IncomeWindow[Tick % player.IncomeWindow.Length] += banked;
            unit.Carrying = 0;
            return;
        }

        if (StepToward(unit, dropOff.Position, 1) == StepResult.Stuck)
        {
            unit.ClearOrder();
        }
    }

    private void UpdateBuilder(UnitEntity unit)
    {
        var player = _players[unit.Owner];
        var site = unit.Destination!.Value;
        var type = unit.BuildType!.Value;
        var existingId = _buildingAt[Index(site)];

        if (existingId != 0)
        {
            var building = (BuildingEntity)_entities[existingId];
            if (building.Owner != unit.Owner || building.Type != type)
            {
                AddEvent(player, "CommandFailed", $"Build site {site} is taken by another building", unit.Id, unit.OrderCommandId, spectator: false);
                unit.ClearOrder();
                return;
            }

            if (building.Completed)
            {
                unit.ClearOrder();
                return;
            }

            unit.TargetId = building.Id;
            if (unit.Position.Chebyshev(site) <= 1)
            {
                unit.ClearPath();
                unit.Activity = UnitActivity.Building;
                if (building.AddWork(1))
                {
                    Planted(building);
                    player.BuildingsCompleted[building.Type] = player.BuildingsCompleted.GetValueOrDefault(building.Type) + 1;
                    if (building.Type == BuildingType.Barracks)
                    {
                        player.FirstBarracksTick ??= Tick;
                    }

                    AddEvent(player, "BuildingCompleted", $"{building.Type} {building.Id} completed at {site}", building.Id);
                }

                return;
            }

            unit.Activity = UnitActivity.Moving;
            if (StepToward(unit, site, 1) == StepResult.Stuck)
            {
                AddEvent(player, "CommandFailed", $"Worker {unit.Id} cannot reach build site {site}", unit.Id, unit.OrderCommandId, spectator: false);
                unit.ClearOrder();
            }

            return;
        }

        if (unit.Position.Chebyshev(site) > 1)
        {
            unit.Activity = UnitActivity.Moving;
            if (StepToward(unit, site, 1) == StepResult.Stuck)
            {
                AddEvent(player, "CommandFailed", $"Worker {unit.Id} cannot reach build site {site}", unit.Id, unit.OrderCommandId, spectator: false);
                unit.ClearOrder();
            }

            return;
        }

        // Adjacent to an empty site: break ground (this is when the cost is paid).
        var error = CheckBuildSite(player, type, site, VisibilityFor(unit.Owner), forAssist: false);
        if (error is not null)
        {
            AddEvent(player, "CommandFailed", $"Cannot start {type} at {site}: {error.Message}", unit.Id, unit.OrderCommandId, spectator: false);
            unit.ClearOrder();
            return;
        }

        player.Resources -= BuildingCost(player, type);
        var placed = AddBuilding(unit.Owner, type, site, completed: false);
        foreach (var standing in _units.Where(u => u.Position == site).ToList())
        {
            Place(standing, FindFreeTileNear(site, standing.Stats.Occupancy) ?? standing.Position);
            standing.ClearPath();
        }

        unit.TargetId = placed.Id;
        unit.Activity = UnitActivity.Building;
        AddEvent(player, "BuildingStarted", $"{type} {placed.Id} started at {site}", placed.Id);
    }

    private void RemoveDead(Dictionary<int, Attack> killers)
    {
        foreach (var unit in _units.Where(u => u.Dead).ToList())
        {
            Unsettle(unit);
            _units.Remove(unit);
            _entities.Remove(unit.Id);
            var owner = _players[unit.Owner];
            owner.UnitsLost++;
            AddEvent(owner, "UnitLost", $"{unit.Type} {unit.Id} was destroyed at {unit.Position}", unit.Id, spectator: false);
            if (killers.TryGetValue(unit.Id, out var kill))
            {
                var killer = _players[kill.AttackerOwner];
                killer.UnitsKilled++;
                if (kill.Attacker is BuildingEntity)
                {
                    killer.TowerKills++;
                }
                else if (kill.Attacker is UnitEntity attacker && Factor(attacker) > 1)
                {
                    killer.SerumKills++;
                }

                killer.Destruction += unit.Stats.Cost;
                AddEvent(killer, "UnitKilled", $"Killed enemy {unit.Type} {unit.Id}", unit.Id);
            }
        }

        foreach (var building in _buildings.Where(b => b.Dead).ToList())
        {
            _buildings.Remove(building);
            _entities.Remove(building.Id);
            SetBuildingAt(building.Position, 0);
            var owner = _players[building.Owner];
            owner.BuildingsLost++;
            AddEvent(owner, "BuildingLost", $"{building.Type} {building.Id} was destroyed at {building.Position}", building.Id, spectator: false);
            if (killers.TryGetValue(building.Id, out var kill))
            {
                var killer = _players[kill.AttackerOwner];
                killer.BuildingsDestroyed++;
                if (building.Type == BuildingType.CommandCenter)
                {
                    killer.CommandCentersDestroyed++;
                }

                killer.Destruction += building.Stats.Cost;
                AddEvent(killer, "BuildingDestroyed", $"Destroyed enemy {building.Type} {building.Id}", building.Id);
                TakeSpoils(killer, building);
            }
        }
    }

    private void Eliminate(PlayerState player, string reason)
    {
        player.Eliminated = true;
        player.CommandQueue.Clear();
        _shells.RemoveAll(s => s.Attacker.Owner == player.Slot);
        foreach (var unit in _units.Where(u => u.Owner == player.Slot).ToList())
        {
            Unsettle(unit);
            _units.Remove(unit);
            _entities.Remove(unit.Id);
        }

        foreach (var building in _buildings.Where(b => b.Owner == player.Slot).ToList())
        {
            _buildings.Remove(building);
            _entities.Remove(building.Id);
            SetBuildingAt(building.Position, 0);
        }

        var message = $"{player.Name} {reason}";
        foreach (var other in _players)
        {
            AddEvent(other, "PlayerEliminated", message, spectator: false);
        }

        _spectatorEvents.Add(new GameEventDto { Tick = Tick, Kind = "PlayerEliminated", Message = message });
    }

    private void CheckForEnd(MatchEndReason reason)
    {
        if (Status != MatchStatus.Active)
        {
            return;
        }

        var standing = _players.Where(p => !p.Eliminated).ToList();
        if (standing.Count <= 1)
        {
            End(reason, standing.SingleOrDefault());
            return;
        }

        if (Tick >= TickLimit)
        {
            // Under net-worth scoring, equal totals go to whoever gathered more; classic calls them a draw.
            var totals = standing.Select(p => (Player: p, Total: ScoreOf(p).Total, Tiebreak: Rules.ScoreNetWorth ? p.Gathered : 0))
                .OrderByDescending(t => t.Total).ThenByDescending(t => t.Tiebreak).ToList();
            var clear = (totals[0].Total, totals[0].Tiebreak).CompareTo((totals[1].Total, totals[1].Tiebreak)) > 0;
            End(MatchEndReason.TimeLimit, clear ? totals[0].Player : null);
        }
    }

    private void End(MatchEndReason reason, PlayerState? winner)
    {
        Status = MatchStatus.Completed;
        Outcome = new MatchOutcomeDto { Reason = reason, WinnerId = winner?.Id, Ticks = Tick };
        foreach (var p in _players)
        {
            p.CommandQueue.Clear();
        }

        var message = winner is null ? $"Match over ({reason}): draw" : $"Match over ({reason}): {winner.Name} wins";
        _spectatorEvents.Add(new GameEventDto { Tick = Tick, Kind = "MatchEnded", Message = message });
        foreach (var p in _players)
        {
            p.Events.Add(new GameEventDto { Tick = Tick, Kind = "MatchEnded", Message = message });
        }
    }

    /// <summary>What <paramref name="type"/> costs <paramref name="player"/> now: extra Command Centers cost more.</summary>
    private int BuildingCost(PlayerState player, BuildingType type) => type == BuildingType.CommandCenter
        ? Rules.CommandCenterCost(_buildings.Count(b => b.Owner == player.Slot && b.Type == BuildingType.CommandCenter))
        : Rules.Buildings[type].Cost;

    private int TowerCooldown(BuildingEntity tower) => _players[tower.Owner].Upgrades.Contains(UpgradeType.RapidFire)
        ? Math.Max(1, tower.Stats.AttackCooldown - Rules.RapidFireCooldownCut)
        : tower.Stats.AttackCooldown;

    /// <summary>Pays the destroyer of an enemy building its share of the building's cost, as far as storage allows.</summary>
    private void TakeSpoils(PlayerState killer, BuildingEntity building)
    {
        var spoils = building.Stats.Cost * Rules.SpoilsPercent / 100;
        if (spoils <= 0 || killer.Eliminated)
        {
            return;
        }

        var banked = Math.Min(spoils, Math.Max(0, StorageCapacity(killer.Slot) - killer.Resources));
        killer.Resources += banked;
        killer.OreWasted += spoils - banked;
        killer.Spoils += banked;
    }

    /// <summary>
    /// Grub patches below their starting amount regrow in proportion to what they hold (whole grubs; the
    /// fraction is carried to the next tick), never past the starting amount.
    /// </summary>
    private void RegrowDeposits()
    {
        if (Rules.RegrowPartsPerMillion <= 0)
        {
            return;
        }

        foreach (var deposit in _deposits)
        {
            if (deposit.Remaining >= deposit.Initial)
            {
                deposit.RegrowCarry = 0;
                continue;
            }

            deposit.RegrowCarry += (long)deposit.Remaining * Rules.RegrowPartsPerMillion;
            var grown = (int)(deposit.RegrowCarry / 1_000_000);
            deposit.RegrowCarry %= 1_000_000;
            deposit.Hp = Math.Min(deposit.Initial, deposit.Hp + grown);
        }
    }
}
