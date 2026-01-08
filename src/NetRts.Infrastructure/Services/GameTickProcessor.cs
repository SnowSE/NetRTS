using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;

namespace NetRts.Infrastructure.Services;

public class GameTickProcessor : IGameTickProcessor
{
    private const int WorkerMovementSpeed = 2; // tiles per tick
    private const int SoldierMovementSpeed = 3; // tiles per tick
    private const int AttackRange = 1; // Adjacent tiles only
    private const int GatherAmount = 10; // Resources per tick

    private readonly ILogger<GameTickProcessor> _logger;
    private readonly GameStateCache _gameStateCache;
    private readonly IMatchRepository _matchRepository;
    private readonly ICommandQueueManager _commandQueueManager;
    private readonly IGameUpdateBroadcaster _gameUpdateBroadcaster;

    public GameTickProcessor(
        ILogger<GameTickProcessor> logger,
        GameStateCache gameStateCache,
        IMatchRepository matchRepository,
        ICommandQueueManager commandQueueManager,
        IGameUpdateBroadcaster gameUpdateBroadcaster)
    {
        _logger = logger;
        _gameStateCache = gameStateCache;
        _matchRepository = matchRepository;
        _commandQueueManager = commandQueueManager;
        _gameUpdateBroadcaster = gameUpdateBroadcaster;
    }

    public async Task ProcessTickAsync(CancellationToken cancellationToken = default)
    {
        var activeMatches = _gameStateCache.GetAll()
            .Where(m => m.Status == MatchStatus.Active)
            .ToList();

        _logger.LogDebug("Processing tick for {MatchCount} active matches", activeMatches.Count);

        foreach (var match in activeMatches)
        {
            try
            {
                await ProcessMatchTickAsync(match.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tick for match {MatchId}", match.Id);
            }
        }
    }

    public async Task ProcessMatchTickAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = _gameStateCache.Get(matchId);
        if (match == null || match.Status != MatchStatus.Active)
        {
            return;
        }

        // Advance tick
        match.AdvanceTick();

        // Check for time limit - end match if reached
        if (match.CurrentTick >= match.MaxTicksPerMatch)
        {
            var units = _gameStateCache.GetUnitsForMatch(match.Id);
            var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);

            var player1Units = units.Count(u => u.OwnerId == match.Player1Id && !u.IsDestroyed());
            var player1Buildings = buildings.Count(b => b.OwnerId == match.Player1Id && !b.IsDestroyed());
            var player2Units = units.Count(u => u.OwnerId == match.Player2Id && !u.IsDestroyed());
            var player2Buildings = buildings.Count(b => b.OwnerId == match.Player2Id && !b.IsDestroyed());

            match.EndMatchByTimeLimit(player1Units, player1Buildings, player2Units, player2Buildings);

            _gameStateCache.AddOrUpdate(match);
            await _matchRepository.UpdateAsync(match, cancellationToken);

            _logger.LogInformation("Match {MatchId} ended by time limit - Winner: {WinnerId}",
                match.Id, match.WinnerId);
            
            // Broadcast match ended
            await _gameUpdateBroadcaster.BroadcastMatchEndedAsync(match.Id, match.WinnerId, cancellationToken);
            return;
        }

        // Process commands for each player
        await ProcessPlayerCommands(match, match.Player1Id, cancellationToken);
        await ProcessPlayerCommands(match, match.Player2Id, cancellationToken);

        // Process building construction and production
        await ProcessBuildingsAsync(match, cancellationToken);

        // Process upgrade research
        await ProcessUpgradesAsync(match, cancellationToken);

        // Update game state cache
        _gameStateCache.AddOrUpdate(match);

        // Broadcast updated game state to all players via SignalR
        await _gameUpdateBroadcaster.BroadcastGameStateAsync(
            match.Id, 
            match.Player1Id, 
            match.Player2Id, 
            cancellationToken);

        _logger.LogDebug("Tick {Tick} processed and broadcast for match {MatchId}", 
            match.CurrentTick, match.Id);

        // Periodic snapshot to database (every 10 ticks)
        if (match.CurrentTick % 10 == 0)
        {
            await _matchRepository.UpdateAsync(match, cancellationToken);
        }
    }

    private async Task ProcessPlayerCommands(
        Domain.Entities.Match match,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        // Dequeue commands for this player (up to CommandsPerTick limit)
        var commands = await _commandQueueManager.DequeueCommandsAsync(
            match.Id,
            playerId,
            100); // Max commands per tick

        // Get game entities from cache
        var units = _gameStateCache.GetUnitsForMatch(match.Id);
        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var resourceDeposits = _gameStateCache.GetResourceDepositsForMatch(match.Id);

        foreach (var command in commands)
        {
            try
            {
                switch (command.Type)
                {
                    case CommandType.Move:
                        ExecuteMoveCommand(command, units);
                        break;

                    case CommandType.Attack:
                        ExecuteAttackCommand(command, match, units);
                        break;

                    case CommandType.Gather:
                        ExecuteGatherCommand(command, match, units, resourceDeposits);
                        break;

                    case CommandType.Build:
                        ExecuteBuildCommand(command, match, buildings);
                        break;

                    case CommandType.Produce:
                        ExecuteProduceCommand(command, match, buildings);
                        break;

                    case CommandType.Research:
                        ExecuteResearchCommand(command, match, buildings);
                        break;

                    default:
                        command.MarkFailed($"Unknown command type: {command.Type}", match.CurrentTick);
                        break;
                }

                if (command.Status == CommandStatus.Queued)
                {
                    command.MarkExecuted(match.CurrentTick);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command {CommandId} for player {PlayerId}", command.Id, playerId);
                command.MarkFailed($"Execution error: {ex.Message}", match.CurrentTick);
            }
        }

        // Update cache with modified entities
        _gameStateCache.SetUnitsForMatch(match.Id, units);
        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
        _gameStateCache.SetResourceDepositsForMatch(match.Id, resourceDeposits);
    }

    private void ExecuteMoveCommand(Command command, List<Unit> units)
    {
        _logger.LogInformation("ExecuteMoveCommand called. TargetPosition: {TargetPos}, TargetUnitIds count: {Count}",
            command.TargetPosition, command.TargetUnitIds.Count);

        if (command.TargetPosition == null)
        {
            command.MarkFailed("No target position specified", 0);
            _logger.LogWarning("ExecuteMoveCommand failed: No target position");
            return;
        }

        _logger.LogInformation("Processing {Count} units for move command", command.TargetUnitIds.Count);

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit == null)
            {
                _logger.LogWarning("ExecuteMoveCommand: Unit {UnitId} not found in list of {TotalUnits} units", unitId, units.Count);
                continue;
            }

            // Calculate direction to target
            var currentPos = unit.Position;
            var targetPos = command.TargetPosition;

            _logger.LogInformation("Moving unit {UnitId} from ({X1},{Y1}) to ({X2},{Y2})",
                unitId, currentPos.X, currentPos.Y, targetPos.X, targetPos.Y);

            if (currentPos.X == targetPos.X && currentPos.Y == targetPos.Y)
            {
                // Already at target
                unit.SetStatus(UnitStatus.Idle);
                _logger.LogInformation("Unit {UnitId} already at target", unitId);
                continue;
            }

            // Calculate movement speed based on unit type
            var movementSpeed = unit.Type == UnitType.Worker ? WorkerMovementSpeed : SoldierMovementSpeed;

            // Move toward target
            var newPosition = MoveToward(currentPos, targetPos, movementSpeed);
            _logger.LogInformation("Calculated new position: ({X},{Y}), MovementSpeed: {Speed}",
                newPosition.X, newPosition.Y, movementSpeed);

            unit.MoveTo(newPosition);
            _logger.LogInformation("After MoveTo: Unit position is now ({X},{Y}), status: {Status}",
                unit.Position.X, unit.Position.Y, unit.CurrentStatus);

            // Update status
            if (newPosition.X == targetPos.X && newPosition.Y == targetPos.Y)
            {
                unit.SetStatus(UnitStatus.Idle); // Reached destination
                _logger.LogInformation("Unit {UnitId} reached destination at ({X},{Y})", unitId, newPosition.X, newPosition.Y);
            }
            else
            {
                unit.SetStatus(UnitStatus.Moving);
                _logger.LogInformation("Unit {UnitId} moving to ({X},{Y}), status: Moving", unitId, newPosition.X, newPosition.Y);
            }
        }
    }

    private void ExecuteAttackCommand(Command command, Match match, List<Unit> units)
    {
        if (command.TargetEntityId == null)
        {
            command.MarkFailed("No target specified", 0);
            return;
        }

        var targetUnit = units.FirstOrDefault(u => u.Id == command.TargetEntityId.Value);
        if (targetUnit == null)
        {
            command.MarkFailed("Target unit not found", 0);
            return;
        }

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit == null) continue;

            // Check if in attack range
            var distance = unit.Position.DistanceTo(targetUnit.Position);

            if (distance <= AttackRange)
            {
                // In range - attack
                unit.SetStatus(UnitStatus.Attacking);
                unit.SetTargetEntity(targetUnit.Id);

                // Apply damage
                var damage = unit.AttackDamage;
                targetUnit.TakeDamage(damage);

                // If target destroyed, set unit back to idle and update score
                if (targetUnit.HealthPoints <= 0)
                {
                    unit.SetStatus(UnitStatus.Idle);
                    unit.SetTargetEntity(null);

                    // Update attacker's score for destroying enemy unit
                    var attackerId = unit.OwnerId;
                    var currentScore = match.GetPlayerScore(attackerId);
                    var newScore = currentScore.WithUnitsDestroyed(1);
                    match.UpdateScore(attackerId, newScore);
                }
            }
            else
            {
                // Not in range - move toward target
                var movementSpeed = unit.Type == UnitType.Worker ? WorkerMovementSpeed : SoldierMovementSpeed;
                var newPosition = MoveToward(unit.Position, targetUnit.Position, movementSpeed);
                unit.MoveTo(newPosition);
                unit.SetStatus(UnitStatus.Moving);
                unit.SetTargetEntity(targetUnit.Id);
            }
        }
    }

    private void ExecuteGatherCommand(Command command, Match match, List<Unit> units, List<ResourceDeposit> resourceDeposits)
    {
        if (command.TargetResourceDepositId == null)
        {
            command.MarkFailed("No resource deposit specified", 0);
            return;
        }

        var deposit = resourceDeposits.FirstOrDefault(d => d.Id == command.TargetResourceDepositId.Value);
        if (deposit == null)
        {
            command.MarkFailed("Resource deposit not found", 0);
            return;
        }

        if (deposit.RemainingCapacity == 0)
        {
            command.MarkFailed("Resource deposit is depleted", 0);
            return;
        }

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit == null || unit.Type != UnitType.Worker) continue;

            // Check if at deposit location
            var distance = unit.Position.DistanceTo(deposit.Position);

            if (distance <= 1)
            {
                // At deposit - gather resources
                unit.SetStatus(UnitStatus.Gathering);
                unit.SetTargetEntity(deposit.Id);

                // Collect resources
                var amountToGather = Math.Min(GatherAmount, deposit.RemainingCapacity);
                if (amountToGather > 0)
                {
                    var gathered = deposit.Gather(amountToGather);
                    unit.CollectResources(gathered);

                    // Automatically deposit gathered resources to player
                    var deposited = unit.DepositResources();
                    if (deposited > 0)
                    {
                        match.UpdateResources(unit.OwnerId, deposited);
                    }
                }
            }
            else
            {
                // Not at deposit - move toward it
                var newPosition = MoveToward(unit.Position, deposit.Position, WorkerMovementSpeed);
                unit.MoveTo(newPosition);
                unit.SetStatus(UnitStatus.Moving);
                unit.SetTargetEntity(deposit.Id);
            }
        }
    }

    private Position MoveToward(Position current, Position target, int maxDistance)
    {
        var dx = target.X - current.X;
        var dy = target.Y - current.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance <= maxDistance)
        {
            // Can reach target this tick
            return target;
        }

        // Move toward target by maxDistance
        var ratio = maxDistance / distance;
        var newX = current.X + (int)Math.Round(dx * ratio);
        var newY = current.Y + (int)Math.Round(dy * ratio);

        return new Position(newX, newY);
    }

    private void ExecuteBuildCommand(Command command, Match match, List<Building> buildings)
    {
        if (command.TargetPosition == null || command.BuildingType == null)
        {
            command.MarkFailed("Build command missing required parameters", 0);
            return;
        }

        // Check if player has enough resources
        var buildCost = Building.GetBuildingCost(command.BuildingType.Value);
        if (!match.DeductResources(command.PlayerId, buildCost))
        {
            command.MarkFailed("Insufficient resources", 0);
            return;
        }

        // Get next building ID
        var maxId = buildings.Where(b => b.MatchId == match.Id).Select(b => b.Id).DefaultIfEmpty(0).Max();
        var newBuildingId = maxId + 1;

        // Create new building
        var newBuilding = new Building(
            id: newBuildingId,
            matchId: match.Id,
            ownerId: command.PlayerId,
            type: command.BuildingType.Value,
            position: command.TargetPosition);

        buildings.Add(newBuilding);
    }

    private void ExecuteProduceCommand(Command command, Match match, List<Building> buildings)
    {
        if (command.TargetBuildingId == null || command.UnitType == null)
        {
            command.MarkFailed("Produce command missing required parameters", 0);
            return;
        }

        var building = buildings.FirstOrDefault(b => b.Id == command.TargetBuildingId.Value && b.MatchId == match.Id);
        if (building == null)
        {
            command.MarkFailed("Building not found", 0);
            return;
        }

        if (!building.IsOperational)
        {
            command.MarkFailed("Building not yet operational", 0);
            return;
        }

        // Check if player has enough resources
        var productionCost = Unit.GetUnitCost(command.UnitType.Value);
        if (!match.DeductResources(command.PlayerId, productionCost))
        {
            command.MarkFailed("Insufficient resources", 0);
            return;
        }

        // Add to building's production queue
        building.QueueProduction(command.UnitType.Value);
    }

    private void ExecuteResearchCommand(Command command, Match match, List<Building> buildings)
    {
        if (command.TargetBuildingId == null || command.UpgradeType == null)
        {
            command.MarkFailed("Research command missing required parameters", 0);
            return;
        }

        var building = buildings.FirstOrDefault(b => b.Id == command.TargetBuildingId.Value && b.MatchId == match.Id);
        if (building == null)
        {
            command.MarkFailed("Building not found", 0);
            return;
        }

        if (!building.IsOperational)
        {
            command.MarkFailed("Building not yet operational", 0);
            return;
        }

        if (building.Type != BuildingType.TechLab)
        {
            command.MarkFailed("Research can only be conducted at TechLab", 0);
            return;
        }

        // Check if player has enough resources
        var upgradeCost = Upgrade.GetUpgradeCost(command.UpgradeType.Value);
        if (!match.DeductResources(command.PlayerId, upgradeCost))
        {
            command.MarkFailed("Insufficient resources", 0);
            return;
        }

        // Get upgrades from cache
        var upgrades = _gameStateCache.GetUpgradesForMatch(match.Id);

        // Get next upgrade ID
        var maxId = upgrades.Where(u => u.MatchId == match.Id).Select(u => u.Id).DefaultIfEmpty(0).Max();
        var newUpgradeId = maxId + 1;

        // Create new upgrade
        var newUpgrade = new Upgrade(
            id: newUpgradeId,
            matchId: match.Id,
            playerId: command.PlayerId,
            type: command.UpgradeType.Value,
            currentTick: match.CurrentTick);

        upgrades.Add(newUpgrade);

        // Update cache
        _gameStateCache.SetUpgradesForMatch(match.Id, upgrades);
    }

    private async Task ProcessBuildingsAsync(Match match, CancellationToken cancellationToken)
    {
        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var units = _gameStateCache.GetUnitsForMatch(match.Id);

        foreach (var building in buildings)
        {
            // Process construction if not yet operational
            if (!building.IsOperational)
            {
                var constructionTime = Building.GetConstructionTime(building.Type);
                var progressPerTick = 100 / constructionTime;
                building.AdvanceConstruction(progressPerTick);
            }
            // Process unit production if operational
            else
            {
                var completedUnitType = building.ProcessProduction();
                if (completedUnitType.HasValue)
                {
                    // Spawn new unit adjacent to building
                    var spawnPosition = FindAdjacentPosition(building.Position, units, buildings);
                    if (spawnPosition != null)
                    {
                        var maxUnitId = units.Where(u => u.MatchId == match.Id).Select(u => u.Id).DefaultIfEmpty(0).Max();
                        var newUnit = new Unit(
                            id: maxUnitId + 1,
                            matchId: match.Id,
                            ownerId: building.OwnerId,
                            type: completedUnitType.Value,
                            position: spawnPosition);

                        // Apply all completed upgrades to the newly created unit
                        var completedUpgrades = _gameStateCache.GetUpgradesForMatch(match.Id)
                            .Where(u => u.PlayerId == building.OwnerId && u.IsComplete)
                            .ToList();

                        foreach (var upgrade in completedUpgrades)
                        {
                            var (damageBonus, healthBonus, speedBonus) = Upgrade.GetUpgradeEffects(upgrade.Type);
                            newUnit.ApplyUpgrade(damageBonus, healthBonus, speedBonus);
                        }

                        units.Add(newUnit);
                    }
                    else
                    {
                        _logger.LogWarning("Could not find spawn position for unit near building {BuildingId}", building.Id);
                    }
                }
            }
        }

        // Check for destroyed buildings and Command Center elimination
        var destroyedBuildings = buildings.Where(b => b.IsDestroyed()).ToList();
        foreach (var building in destroyedBuildings)
        {
            // Update opponent's score for destroying the building
            var opponentId = match.GetOpponentId(building.OwnerId);
            var currentScore = match.GetPlayerScore(opponentId);
            var newScore = currentScore.WithBuildingsDestroyed(1);
            match.UpdateScore(opponentId, newScore);

            // Check if it was a Command Center - end match by elimination
            if (building.Type == BuildingType.CommandCenter)
            {
                match.EndMatchByElimination(building.OwnerId);
                _logger.LogInformation("Match {MatchId} ended by elimination - Player {PlayerId} lost their Command Center",
                    match.Id, building.OwnerId);
            }
        }

        // Remove destroyed buildings from the list
        buildings.RemoveAll(b => b.IsDestroyed());

        // Remove destroyed units from the list
        units.RemoveAll(u => u.IsDestroyed());

        // Update cache with modified entities
        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
        _gameStateCache.SetUnitsForMatch(match.Id, units);
    }

    private Position? FindAdjacentPosition(Position center, List<Unit> units, List<Building> buildings)
    {
        // Try positions around the building (8 directions)
        var offsets = new[] {
            (0, -1), (1, -1), (1, 0), (1, 1),
            (0, 1), (-1, 1), (-1, 0), (-1, -1)
        };

        foreach (var (dx, dy) in offsets)
        {
            var pos = new Position(center.X + dx, center.Y + dy);

            // Check if position is occupied by unit
            if (units.Any(u => u.Position.X == pos.X && u.Position.Y == pos.Y))
                continue;

            // Check if position is occupied by building
            if (buildings.Any(b => b.Position.X == pos.X && b.Position.Y == pos.Y))
                continue;

            return pos;
        }

        return null; // No available position
    }

    private async Task ProcessUpgradesAsync(Match match, CancellationToken cancellationToken)
    {
        var upgrades = _gameStateCache.GetUpgradesForMatch(match.Id);
        var units = _gameStateCache.GetUnitsForMatch(match.Id);

        foreach (var upgrade in upgrades.Where(u => !u.IsComplete))
        {
            // Calculate research progress for this tick
            var researchTime = Upgrade.GetResearchTime(upgrade.Type);
            var progressPerTick = 100 / researchTime;

            // Advance research
            var wasComplete = upgrade.IsComplete;
            upgrade.AdvanceResearch(progressPerTick);

            // If research just completed, apply upgrade effects to existing units
            if (!wasComplete && upgrade.IsComplete)
            {
                ApplyUpgradeEffects(upgrade, units);
            }
        }

        // Update cache with modified entities
        _gameStateCache.SetUpgradesForMatch(match.Id, upgrades);
        _gameStateCache.SetUnitsForMatch(match.Id, units);
    }

    private void ApplyUpgradeEffects(Upgrade upgrade, List<Unit> units)
    {
        var (damageBonus, healthBonus, speedBonus) = Upgrade.GetUpgradeEffects(upgrade.Type);

        // Apply to all units owned by the player who researched the upgrade
        var playerUnits = units.Where(u => u.OwnerId == upgrade.PlayerId).ToList();

        foreach (var unit in playerUnits)
        {
            unit.ApplyUpgrade(damageBonus, healthBonus, speedBonus);
        }
    }
}
