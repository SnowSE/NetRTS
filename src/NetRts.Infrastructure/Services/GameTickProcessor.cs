using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;

namespace NetRts.Infrastructure.Services;

public class GameTickProcessor : IGameTickProcessor
{
    private readonly ILogger<GameTickProcessor> _logger;
    private readonly GameStateCache _gameStateCache;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ICommandQueueManager _commandQueueManager;

    public GameTickProcessor(
        ILogger<GameTickProcessor> logger,
        GameStateCache gameStateCache,
        IServiceScopeFactory serviceScopeFactory,
        ICommandQueueManager commandQueueManager)
    {
        _logger = logger;
        _gameStateCache = gameStateCache;
        _serviceScopeFactory = serviceScopeFactory;
        _commandQueueManager = commandQueueManager;
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

        // Process commands for each player
        await ProcessPlayerCommands(match, match.Player1Id, cancellationToken);
        await ProcessPlayerCommands(match, match.Player2Id, cancellationToken);

        // Update game state cache
        _gameStateCache.AddOrUpdate(match);

        // Periodic snapshot to database (every 10 ticks)
        if (match.CurrentTick % 10 == 0)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
            await matchRepository.UpdateAsync(match, cancellationToken);
        }
    }

    private async Task ProcessPlayerCommands(
        Domain.Entities.Match match,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        // Dequeue up to 100 commands for this player
        var commands = await _commandQueueManager.DequeueCommandsAsync(
            match.Id,
            playerId,
            100); // Max commands per tick

        // Get game state
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
                        ExecuteAttackCommand(command, units);
                        break;
                    case CommandType.Gather:
                        ExecuteGatherCommand(command, units, resourceDeposits, match, playerId);
                        break;
                    case CommandType.Build:
                        ExecuteBuildCommand(command, match, playerId);
                        break;
                    case CommandType.Produce:
                        ExecuteProduceCommand(command, match, playerId);
                        break;
                    case CommandType.Research:
                        ExecuteResearchCommand(command, match, playerId);
                        break;
                    default:
                        _logger.LogWarning("Unsupported command type {CommandType} for match {MatchId}",
                            command.Type, match.Id);
                        break;
                }

                command.MarkExecuted(match.CurrentTick);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command {CommandId} for match {MatchId}",
                    command.Id, match.Id);
                command.MarkFailed(ex.Message, match.CurrentTick);
            }
        }

        // Update unit positions and states based on movement
        ProcessUnitMovement(units);

        // Process resource gathering
        ProcessResourceGathering(units, match, playerId);

        // Process building construction
        ProcessBuildingConstruction(match);

        // Process unit production
        ProcessUnitProduction(match, playerId);

        // Process upgrade research
        ProcessUpgradeResearch(match, playerId);

        // Update cache
        _gameStateCache.SetUnitsForMatch(match.Id, units);
        _gameStateCache.SetResourceDepositsForMatch(match.Id, resourceDeposits);
    }

    private void ExecuteMoveCommand(Domain.Entities.Command command, List<Domain.Entities.Unit> units)
    {
        if (command.TargetPosition == null || command.TargetUnitIds == null)
        {
            return;
        }

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit != null)
            {
                unit.SetTargetPosition(command.TargetPosition);
                unit.SetStatus(UnitStatus.Moving);
            }
        }
    }

    private void ExecuteAttackCommand(Domain.Entities.Command command, List<Domain.Entities.Unit> units)
    {
        if (command.TargetEntityId == null || command.TargetUnitIds == null)
        {
            return;
        }

        var targetUnit = units.FirstOrDefault(u => u.Id == command.TargetEntityId.Value);
        if (targetUnit == null)
        {
            return;
        }

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit == null)
            {
                continue;
            }

            // Check if target is in range
            var distance = unit.Position.DistanceTo(targetUnit.Position);
            if (distance <= unit.AttackRange)
            {
                // Attack target
                targetUnit.TakeDamage(unit.AttackDamage);
                unit.SetTargetEntity(targetUnit.Id);
                unit.SetStatus(UnitStatus.Attacking);
            }
            else
            {
                // Move toward target
                unit.SetTargetPosition(targetUnit.Position);
                unit.SetTargetEntity(targetUnit.Id);
                unit.SetStatus(UnitStatus.Moving);
            }
        }
    }

    private void ExecuteGatherCommand(
        Domain.Entities.Command command,
        List<Domain.Entities.Unit> units,
        List<Domain.Entities.ResourceDeposit> resourceDeposits,
        Domain.Entities.Match match,
        Guid playerId)
    {
        if (command.TargetResourceDepositId == null || command.TargetUnitIds == null)
        {
            return;
        }

        var deposit = resourceDeposits.FirstOrDefault(r => r.Id == command.TargetResourceDepositId.Value);
        if (deposit == null || deposit.RemainingCapacity <= 0)
        {
            return;
        }

        foreach (var unitId in command.TargetUnitIds)
        {
            var unit = units.FirstOrDefault(u => u.Id == unitId);
            if (unit == null || unit.Type != UnitType.Worker)
            {
                continue;
            }

            // Check if unit is at the deposit
            var distance = unit.Position.DistanceTo(deposit.Position);
            if (distance <= 1.0) // Adjacent to deposit
            {
                unit.SetTargetResourceDeposit(deposit.Id);
                unit.SetStatus(UnitStatus.Gathering);
            }
            else
            {
                // Move toward deposit
                unit.SetTargetPosition(deposit.Position);
                unit.SetStatus(UnitStatus.Moving);
            }
        }
    }

    private void ProcessUnitMovement(List<Domain.Entities.Unit> units)
    {
        foreach (var unit in units.Where(u => u.CurrentStatus == UnitStatus.Moving && u.TargetPosition != null))
        {
            var movementSpeed = unit.MovementSpeed; // tiles per tick
            var currentPos = unit.Position;
            var targetPos = unit.TargetPosition!;

            var distance = currentPos.DistanceTo(targetPos);
            if (distance <= movementSpeed)
            {
                // Reached destination
                unit.SetPosition(targetPos);
                unit.SetTargetPosition(null);
                unit.SetStatus(UnitStatus.Idle);
            }
            else
            {
                // Move toward target
                var dx = targetPos.X - currentPos.X;
                var dy = targetPos.Y - currentPos.Y;
                var ratio = movementSpeed / distance;

                var newX = currentPos.X + (int)(dx * ratio);
                var newY = currentPos.Y + (int)(dy * ratio);

                unit.SetPosition(new Domain.ValueObjects.Position(newX, newY));
            }
        }
    }

    private void ProcessResourceGathering(
        List<Domain.Entities.Unit> units,
        Domain.Entities.Match match,
        Guid playerId)
    {
        var resourceDeposits = _gameStateCache.GetResourceDepositsForMatch(match.Id);

        foreach (var unit in units.Where(u => u.CurrentStatus == UnitStatus.Gathering))
        {
            if (unit.TargetResourceDepositId == null)
            {
                continue;
            }

            var deposit = resourceDeposits.FirstOrDefault(r => r.Id == unit.TargetResourceDepositId.Value);
            if (deposit == null || deposit.RemainingCapacity <= 0)
            {
                unit.SetStatus(UnitStatus.Idle);
                continue;
            }

            // Gather resources (10 per tick for workers)
            const int gatherRate = 10;
            var actualGathered = deposit.Gather(gatherRate);

            match.AddPlayerResources(playerId, actualGathered);
            unit.AddCarriedResources(actualGathered);

            _logger.LogDebug(
                "Unit {UnitId} gathered {Amount} resources from deposit {DepositId} for player {PlayerId}",
                unit.Id, actualGathered, deposit.Id, playerId);
        }
    }

    private void ExecuteBuildCommand(
        Domain.Entities.Command command,
        Domain.Entities.Match match,
        Guid playerId)
    {
        if (command.TargetPosition == null || command.BuildingType == null)
        {
            return;
        }

        // Deduct resources
        var buildCost = Domain.Entities.Building.GetBuildingCost(command.BuildingType.Value);
        match.DeductPlayerResources(playerId, buildCost);

        // Create building with 0% construction progress
        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var newBuildingId = buildings.Any() ? buildings.Max(b => b.Id) + 1 : 1;

        var newBuilding = new Domain.Entities.Building(
            newBuildingId,
            match.Id,
            playerId,
            command.BuildingType.Value,
            command.TargetPosition);

        buildings.Add(newBuilding);
        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);

        _logger.LogDebug(
            "Player {PlayerId} started building {BuildingType} at position ({X},{Y}) for match {MatchId}",
            playerId, command.BuildingType, command.TargetPosition.X, command.TargetPosition.Y, match.Id);
    }

    private void ExecuteProduceCommand(
        Domain.Entities.Command command,
        Domain.Entities.Match match,
        Guid playerId)
    {
        if (command.TargetBuildingId == null || command.UnitType == null)
        {
            return;
        }

        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var building = buildings.FirstOrDefault(b => b.Id == command.TargetBuildingId.Value);

        if (building == null || !building.IsOperational)
        {
            return;
        }

        // Deduct resources
        var unitCost = Domain.Entities.Unit.GetUnitCost(command.UnitType.Value);
        match.DeductPlayerResources(playerId, unitCost);

        // Add to building's production queue
        var productionTime = Domain.Entities.Unit.GetProductionTime(command.UnitType.Value);
        building.EnqueueProduction(command.UnitType.Value, productionTime);

        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);

        _logger.LogDebug(
            "Player {PlayerId} queued production of {UnitType} at building {BuildingId} for match {MatchId}",
            playerId, command.UnitType, building.Id, match.Id);
    }

    private void ProcessBuildingConstruction(Domain.Entities.Match match)
    {
        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var updated = false;

        foreach (var building in buildings.Where(b => !b.IsOperational))
        {
            var constructionTime = Domain.Entities.Building.GetConstructionTime(building.Type);
            var progressPerTick = 100 / constructionTime;

            building.AdvanceConstruction(progressPerTick);
            updated = true;

            if (building.IsOperational)
            {
                _logger.LogDebug(
                    "Building {BuildingId} ({BuildingType}) completed construction for match {MatchId}",
                    building.Id, building.Type, match.Id);
            }
        }

        if (updated)
        {
            _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
        }
    }

    private void ProcessUnitProduction(Domain.Entities.Match match, Guid playerId)
    {
        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id)
            .Where(b => b.OwnerId == playerId && b.IsOperational)
            .ToList();

        var units = _gameStateCache.GetUnitsForMatch(match.Id);
        var unitsAdded = false;

        foreach (var building in buildings)
        {
            if (building.ProcessProduction())
            {
                // Production completed, spawn unit
                var completedProduction = building.GetCompletedProduction();
                if (completedProduction != null)
                {
                    var spawnPosition = FindAdjacentSpawnPosition(building.Position, units);
                    var newUnitId = units.Any() ? units.Max(u => u.Id) + 1 : 1;

                    var newUnit = new Domain.Entities.Unit(
                        newUnitId,
                        match.Id,
                        playerId,
                        completedProduction.Value,
                        spawnPosition);

                    units.Add(newUnit);
                    unitsAdded = true;

                    _logger.LogDebug(
                        "Spawned {UnitType} (ID: {UnitId}) near building {BuildingId} at position ({X},{Y}) for player {PlayerId}",
                        completedProduction.Value, newUnitId, building.Id, spawnPosition.X, spawnPosition.Y, playerId);
                }
            }
        }

        if (unitsAdded)
        {
            _gameStateCache.SetUnitsForMatch(match.Id, units);
        }

        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
    }

    private Position FindAdjacentSpawnPosition(Position buildingPosition, List<Domain.Entities.Unit> units)
    {
        // Try positions in a 3x3 grid around the building
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue; // Skip the building's own position
                }

                var candidatePosition = new Position(
                    buildingPosition.X + dx,
                    buildingPosition.Y + dy);

                // Check if position is available (not occupied by another unit)
                if (!units.Any(u => u.Position.Equals(candidatePosition)))
                {
                    return candidatePosition;
                }
            }
        }

        // If all adjacent positions are occupied, return building position + offset
        return new Position(buildingPosition.X + 2, buildingPosition.Y);
    }

    private void ExecuteResearchCommand(
        Domain.Entities.Command command,
        Domain.Entities.Match match,
        Guid playerId)
    {
        if (command.TargetBuildingId == null || command.UpgradeType == null)
        {
            return;
        }

        var buildings = _gameStateCache.GetBuildingsForMatch(match.Id);
        var building = buildings.FirstOrDefault(b => b.Id == command.TargetBuildingId.Value);

        if (building == null || !building.IsOperational || building.Type != Domain.Enums.BuildingType.TechLab)
        {
            return;
        }

        // Deduct resources
        var upgradeCost = Domain.Entities.Upgrade.GetUpgradeCost(command.UpgradeType.Value);
        match.DeductPlayerResources(playerId, upgradeCost);

        // Create upgrade entity
        var upgradeId = match.Upgrades.Count + 1;
        var upgrade = new Domain.Entities.Upgrade(upgradeId, match.Id, playerId, command.UpgradeType.Value);
        match.AddUpgrade(upgrade);

        _gameStateCache.AddOrUpdate(match);

        _logger.LogDebug(
            "Player {PlayerId} started research of {UpgradeType} at building {BuildingId} for match {MatchId}",
            playerId, command.UpgradeType, building.Id, match.Id);
    }

    private void ProcessUpgradeResearch(Domain.Entities.Match match, Guid playerId)
    {
        var upgrades = match.GetPlayerUpgrades(playerId).Where(u => !u.IsCompleted).ToList();

        foreach (var upgrade in upgrades)
        {
            // Advance research progress (e.g., 5% per tick based on research ticks required)
            var progressPerTick = 100.0 / upgrade.ResearchTicksRequired;
            upgrade.AdvanceResearch((int)Math.Ceiling(progressPerTick));

            // If upgrade just completed, apply effects to all player units
            if (upgrade.IsCompleted)
            {
                ApplyUpgradeToUnits(match, playerId, upgrade.UpgradeType);

                _logger.LogInformation(
                    "Player {PlayerId} completed upgrade {UpgradeType} in match {MatchId}",
                    playerId, upgrade.UpgradeType, match.Id);
            }
        }

        _gameStateCache.AddOrUpdate(match);
    }

    private void ApplyUpgradeToUnits(Domain.Entities.Match match, Guid playerId, Domain.Enums.UpgradeType upgradeType)
    {
        var units = _gameStateCache.GetUnitsForMatch(match.Id);
        var playerUnits = units.Where(u => u.OwnerId == playerId).ToList();

        var (damageBonus, armorBonus) = Domain.Entities.Upgrade.GetUpgradeEffects(upgradeType);

        foreach (var unit in playerUnits)
        {
            unit.ApplyUpgrade(damageBonus, armorBonus);
        }

        _gameStateCache.SetUnitsForMatch(match.Id, units);

        _logger.LogDebug(
            "Applied upgrade {UpgradeType} to {UnitCount} units for player {PlayerId} in match {MatchId}",
            upgradeType, playerUnits.Count, playerId, match.Id);
    }
}
