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
    /// queued commands → production &amp; research → unit behaviour and movement →
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
        foreach (var unit in _units.ToList())
        {
            if (unit.Cooldown > 0)
            {
                unit.Cooldown--;
            }

            UpdateUnit(unit, attacks);
        }

        foreach (var tower in _buildings.Where(b => b.Completed && b.Stats.Damage > 0).ToList())
        {
            if (tower.Cooldown > 0)
            {
                tower.Cooldown--;
            }

            var target = FindEnemy(tower.Owner, tower.Position, tower.Stats.Range, unitsOnly: true);
            if (target is not null && tower.Cooldown == 0)
            {
                attacks.Add(new Attack(tower, tower.Owner, target, DamageAgainst(tower, target)));
                tower.Cooldown = tower.Stats.AttackCooldown;
            }
        }

        var killers = new Dictionary<int, Attack>();
        foreach (var attack in attacks)
        {
            var wasAlive = !attack.Target.Dead;
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
            if (player.SerumUntilTick - GameRules.SerumTicks + 1 == Tick)
            {
                Announce(player, "SuperSoldiers", $"Super soldiers activated. {GameRules.SerumTicks} ticks.");
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
                item.Progress = Math.Min(item.Progress + 1, GameRules.Units[item.Type].BuildTicks);
                if (item.Progress >= GameRules.Units[item.Type].BuildTicks && FindFreeTileNear(building.Position) is { } spawn)
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
                if (research.Progress >= GameRules.Upgrades[research.Type].ResearchTicks)
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
                if (unit.Type != UnitType.Worker
                    && FindEnemy(unit.Owner, unit.Position, unit.Stats.Range, unitsOnly: false) is { } nearby)
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
            unit.ClearPath();
            TryAttack(unit, target, attacks);
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

        attacks.Add(new Attack(unit, unit.Owner, target, DamageAgainst(unit, target)));
        unit.Cooldown = unit.Stats.AttackCooldown;
    }

    private int DamageAgainst(Entity attacker, Entity target)
    {
        var damage = attacker switch
        {
            UnitEntity u => (u.Stats.Damage + _players[u.Owner].WeaponsBonus) * Factor(u),
            BuildingEntity b => b.Stats.Damage + _players[b.Owner].WeaponsBonus,
            _ => 0,
        };
        var armor = target switch
        {
            UnitEntity u => u.Stats.Armor + _players[u.Owner].ArmorBonus,
            BuildingEntity { Completed: true } b => b.Stats.Armor,
            _ => 0,
        };
        return Math.Max(1, damage - armor);
    }

    /// <summary>Closest enemy within Chebyshev <paramref name="range"/>; units are preferred over buildings.</summary>
    private Entity? FindEnemy(int owner, Point from, int range, bool unitsOnly)
    {
        var visibility = VisibilityFor(owner);
        return ClosestEnemy(owner, from, e => from.Chebyshev(e.Position) <= range && IsVisible(visibility, e.Position), unitsOnly);
    }

    /// <summary>Closest enemy inside this unit's own sight radius (used by attack-move).</summary>
    private Entity? FindEnemyInSight(UnitEntity unit)
    {
        var r2 = unit.Stats.Vision * unit.Stats.Vision;
        return ClosestEnemy(unit.Owner, unit.Position, e => unit.Position.DistanceSquared(e.Position) <= r2, unitsOnly: false);
    }

    /// <summary>
    /// The living enemy of <paramref name="owner"/> that <paramref name="inReach"/> accepts and is nearest
    /// <paramref name="from"/>: units before buildings, then the weakest, then the lowest id.
    /// </summary>
    private Entity? ClosestEnemy(int owner, Point from, Func<Entity, bool> inReach, bool unitsOnly)
    {
        Entity? best = Closest(_units.Where(u => u.Owner != owner && !u.Dead && inReach(u)), from);
        return best is null && !unitsOnly
            ? Closest(_buildings.Where(b => b.Owner != owner && !b.Dead && inReach(b)), from)
            : best;
    }

    private static T? Closest<T>(IEnumerable<T> candidates, Point from)
        where T : Entity =>
        candidates.OrderBy(e => from.Chebyshev(e.Position)).ThenBy(e => e.Hp).ThenBy(e => e.Id).FirstOrDefault();

    /// <summary>Tenths of a tile this unit moves per tick.</summary>
    private int MoveSpeed(UnitEntity unit) => (unit.Stats.Speed + _players[unit.Owner].MobilityBonus) * Factor(unit);

    private int Factor(UnitEntity unit) => unit.Type == UnitType.Soldier && Tick <= unit.SerumUntilTick ? GameRules.SerumFactor : 1;

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
            unit.Path = Pathfinder.FindPath(Map.Width, Map.Height, Passable, unit.Position, goal, range);
            unit.PathGoal = goal;
            unit.PathRange = range;
            if (unit.Path.Count == 0)
            {
                unit.MovePoints = 0;
                return StepResult.Stuck;
            }
        }

        unit.MovePoints += MoveSpeed(unit);
        while (unit.MovePoints >= GameRules.MovePointsPerTile && unit.Path.Count > 0)
        {
            var next = unit.Path[0];
            if (!Passable(next))
            {
                break;
            }

            unit.Position = next;
            unit.Path.RemoveAt(0);
            unit.MovePoints -= GameRules.MovePointsPerTile;
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
        var deposit = unit.TargetId is { } id ? _entities.GetValueOrDefault(id) as DepositEntity : null;

        if (deposit is null && unit.Carrying == 0)
        {
            // Our patch ran dry: move on to the nearest other deposit near where it was.
            deposit = unit.LastDepositPosition is { } last
                ? _deposits.Where(d => d.Position.Chebyshev(last) <= 6)
                           .OrderBy(d => d.Position.Chebyshev(unit.Position)).ThenBy(d => d.Id)
                           .FirstOrDefault()
                : null;

            if (deposit is null)
            {
                unit.ClearOrder();
                return;
            }

            unit.TargetId = deposit.Id;
        }

        if (deposit is null || unit.Carrying >= GameRules.WorkerCarryCapacity)
        {
            ReturnCargo(unit, player);
            return;
        }

        if (unit.Position.Chebyshev(deposit.Position) <= 1)
        {
            unit.ClearPath();
            unit.Activity = UnitActivity.Gathering;
            var take = Math.Min(GameRules.GatherPerTick + player.HarvestingBonus,
                Math.Min(deposit.Remaining, GameRules.WorkerCarryCapacity - unit.Carrying));
            deposit.Hp -= take;
            unit.Carrying += take;
            unit.LastDepositPosition = deposit.Position;
            if (deposit.Remaining <= 0)
            {
                _deposits.Remove(deposit);
                _entities.Remove(deposit.Id);
                _depositAt[Index(deposit.Position)] = 0;
                player.DepositsExhausted++;
                AddEvent(player, "DepositDepleted", $"Deposit {deposit.Id} at {deposit.Position} is exhausted", deposit.Id);
            }

            return;
        }

        unit.Activity = UnitActivity.Moving;
        if (StepToward(unit, deposit.Position, 1) == StepResult.Stuck)
        {
            unit.ClearOrder();
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

        player.Resources -= GameRules.Buildings[type].Cost;
        var placed = AddBuilding(unit.Owner, type, site, completed: false);
        foreach (var standing in _units.Where(u => u.Position == site).ToList())
        {
            standing.Position = FindFreeTileNear(site) ?? standing.Position;
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
            _buildingAt[Index(building.Position)] = 0;
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
            }
        }
    }

    private void Eliminate(PlayerState player, string reason)
    {
        player.Eliminated = true;
        player.CommandQueue.Clear();
        foreach (var unit in _units.Where(u => u.Owner == player.Slot).ToList())
        {
            _units.Remove(unit);
            _entities.Remove(unit.Id);
        }

        foreach (var building in _buildings.Where(b => b.Owner == player.Slot).ToList())
        {
            _buildings.Remove(building);
            _entities.Remove(building.Id);
            _buildingAt[Index(building.Position)] = 0;
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

        if (Tick >= Config.MaxTicks)
        {
            var totals = standing.Select(p => (Player: p, Total: ScoreOf(p).Total)).OrderByDescending(t => t.Total).ToList();
            End(MatchEndReason.TimeLimit, totals[0].Total > totals[1].Total ? totals[0].Player : null);
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
}
