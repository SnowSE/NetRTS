using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Services;

/// <summary>
/// Service for queueing and validating player commands.
/// </summary>
public class CommandQueueService : ICommandQueueService
{
    private const int MaxQueueSize = 500;
    private readonly ICommandQueueManager _queueManager;
    private readonly IMatchRepository _matchRepository;
    private readonly IGameStateCache _gameStateCache;

    public CommandQueueService(
        ICommandQueueManager queueManager,
        IMatchRepository matchRepository,
        IGameStateCache gameStateCache)
    {
        _queueManager = queueManager;
        _matchRepository = matchRepository;
        _gameStateCache = gameStateCache;
    }

    public async Task<QueueCommandsResponse> QueueCommandsAsync(
        Guid matchId,
        Guid playerId,
        QueueCommandsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new QueueCommandsResponse();

        // Get match and verify player participation
        var match = _gameStateCache.GetMatch(matchId);
        if (match == null)
        {
            match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
            if (match == null)
            {
                response.Errors.Add(new CommandValidationError
                {
                    CommandIndex = -1,
                    ErrorCode = "MATCH_NOT_FOUND",
                    ErrorMessage = "Match not found"
                });
                return response;
            }
        }

        // Verify player is participant
        if (match.Player1Id != playerId && match.Player2Id != playerId)
        {
            response.Errors.Add(new CommandValidationError
            {
                CommandIndex = -1,
                ErrorCode = "UNAUTHORIZED",
                ErrorMessage = "Player is not a participant in this match"
            });
            return response;
        }

        // Check if match is active
        if (match.Status != MatchStatus.Active)
        {
            response.Errors.Add(new CommandValidationError
            {
                CommandIndex = -1,
                ErrorCode = "MATCH_NOT_ACTIVE",
                ErrorMessage = "Match is not active"
            });
            return response;
        }

        // Check queue size
        var currentQueueSize = await _queueManager.GetQueueSizeAsync(matchId, playerId);
        if (currentQueueSize >= MaxQueueSize)
        {
            response.Errors.Add(new CommandValidationError
            {
                CommandIndex = -1,
                ErrorCode = "QUEUE_FULL",
                ErrorMessage = $"Command queue is full (max {MaxQueueSize} commands)"
            });
            return response;
        }

        // Get game entities for validation
        var units = _gameStateCache.GetUnitsForMatch(matchId);
        var buildings = _gameStateCache.GetBuildingsForMatch(matchId);
        var resourceDeposits = _gameStateCache.GetResourceDepositsForMatch(matchId);

        // If not in cache, load from database
        if (units.Count == 0)
        {
            var matchWithEntities = await _matchRepository.GetByIdWithEntitiesAsync(matchId, cancellationToken);
            if (matchWithEntities != null)
            {
                units = matchWithEntities.Units.ToList();
                buildings = matchWithEntities.Buildings.ToList();
                resourceDeposits = matchWithEntities.ResourceDeposits.ToList();
            }
        }

        // Validate and queue each command
        for (int i = 0; i < request.Commands.Length; i++)
        {
            var commandDto = request.Commands[i];
            var validationResult = ValidateCommand(commandDto, i, playerId, matchId, match, units, buildings, resourceDeposits);

            if (validationResult.IsValid)
            {
                var command = CreateCommandEntity(commandDto, matchId, playerId, match.CurrentTick);
                var enqueued = await _queueManager.EnqueueCommandAsync(command, MaxQueueSize);

                if (enqueued)
                {
                    response.QueuedCount++;
                }
                else
                {
                    response.FailedCount++;
                    response.Errors.Add(new CommandValidationError
                    {
                        CommandIndex = i,
                        ErrorCode = "QUEUE_FULL",
                        ErrorMessage = "Command queue is full"
                    });
                }
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add(validationResult.Error!);
            }
        }

        return response;
    }

    private ValidationResult ValidateCommand(
        CommandDto dto,
        int index,
        Guid playerId,
        Guid matchId,
        Match match,
        List<Unit> units,
        List<Building> buildings,
        List<ResourceDeposit> resourceDeposits)
    {
        // Validate command type
        if (!Enum.TryParse<CommandType>(dto.CommandType, true, out var commandType))
        {
            return ValidationResult.Failure(index, "INVALID_COMMAND_TYPE", $"Invalid command type: {dto.CommandType}");
        }

        // Validate unit ownership for unit-based commands
        bool needsUnits = commandType == CommandType.Move || 
                          commandType == CommandType.Attack || 
                          commandType == CommandType.Gather || 
                          commandType == CommandType.Build;

        if (needsUnits)
        {
            if (dto.UnitIds == null || dto.UnitIds.Length == 0)
            {
                return ValidationResult.Failure(index, "NO_UNITS_SPECIFIED", "No units specified for command");
            }

            foreach (var unitId in dto.UnitIds)
            {
                var unit = units.FirstOrDefault(u => u.Id == unitId && u.MatchId == matchId);
                if (unit == null)
                {
                    return ValidationResult.Failure(index, "UNIT_NOT_FOUND", $"Unit {unitId} not found");
                }

                if (unit.OwnerId != playerId)
                {
                    return ValidationResult.Failure(index, "UNAUTHORIZED_UNIT", $"Unit {unitId} does not belong to player");
                }
            }
        }

        // Validate command-specific parameters
        switch (commandType)
        {
            case CommandType.Move:
                if (dto.TargetPosition == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET_POSITION", "Move command requires target position");
                }
                if (dto.TargetPosition.X < 0 || dto.TargetPosition.X >= match.MapWidth ||
                    dto.TargetPosition.Y < 0 || dto.TargetPosition.Y >= match.MapHeight)
                {
                    return ValidationResult.Failure(index, "INVALID_TARGET_POSITION", "Target position is out of map bounds");
                }
                break;

            case CommandType.Attack:
                if (dto.TargetUnitId == null && dto.TargetBuildingId == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET", "Attack command requires target unit or building");
                }
                if (dto.TargetUnitId.HasValue)
                {
                    var targetUnit = units.FirstOrDefault(u => u.Id == dto.TargetUnitId.Value && u.MatchId == matchId);
                    if (targetUnit == null)
                    {
                        return ValidationResult.Failure(index, "TARGET_NOT_FOUND", $"Target unit {dto.TargetUnitId} not found");
                    }
                    if (targetUnit.OwnerId == playerId)
                    {
                        return ValidationResult.Failure(index, "FRIENDLY_FIRE", "Cannot attack own units");
                    }
                }
                break;

            case CommandType.Gather:
                if (dto.TargetResourceId == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET", "Gather command requires target resource deposit");
                }
                var deposit = resourceDeposits.FirstOrDefault(d => d.Id == dto.TargetResourceId.Value && d.MatchId == matchId);
                if (deposit == null)
                {
                    return ValidationResult.Failure(index, "RESOURCE_NOT_FOUND", $"Resource deposit {dto.TargetResourceId} not found");
                }
                break;

            case CommandType.Build:
                if (string.IsNullOrEmpty(dto.BuildingType))
                {
                    return ValidationResult.Failure(index, "MISSING_BUILDING_TYPE", "Build command requires building type");
                }
                if (!Enum.TryParse<BuildingType>(dto.BuildingType, true, out var buildingType))
                {
                    return ValidationResult.Failure(index, "INVALID_BUILDING_TYPE", $"Invalid building type: {dto.BuildingType}");
                }
                if (dto.TargetPosition == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET_POSITION", "Build command requires target position");
                }
                if (dto.TargetPosition.X < 0 || dto.TargetPosition.X >= match.MapWidth ||
                    dto.TargetPosition.Y < 0 || dto.TargetPosition.Y >= match.MapHeight)
                {
                    return ValidationResult.Failure(index, "INVALID_TARGET_POSITION", "Target position is out of map bounds");
                }
                // Check if position is occupied by another building
                var buildingAtPosition = buildings.FirstOrDefault(b =>
                    b.Position.X == dto.TargetPosition.X &&
                    b.Position.Y == dto.TargetPosition.Y &&
                    b.MatchId == matchId);
                if (buildingAtPosition != null)
                {
                    return ValidationResult.Failure(index, "POSITION_OCCUPIED", "A building already exists at this position");
                }
                // Check if player has enough resources
                var buildCost = Building.GetBuildingCost(buildingType);
                if (match.GetPlayerResources(playerId) < buildCost)
                {
                    return ValidationResult.Failure(index, "INSUFFICIENT_RESOURCES", $"Insufficient resources to build {buildingType}. Required: {buildCost}");
                }
                break;

            case CommandType.Produce:
                var produceBuildingId = dto.BuildingId ?? dto.TargetBuildingId;
                if (produceBuildingId == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET_BUILDING", "Produce command requires target building");
                }
                var productionBuilding = buildings.FirstOrDefault(b => b.Id == produceBuildingId.Value && b.MatchId == matchId);
                if (productionBuilding == null)
                {
                    return ValidationResult.Failure(index, "BUILDING_NOT_FOUND", $"Building {produceBuildingId} not found");
                }
                if (productionBuilding.OwnerId != playerId)
                {
                    return ValidationResult.Failure(index, "UNAUTHORIZED_BUILDING", "Cannot produce from opponent's building");
                }
                if (!productionBuilding.IsOperational)
                {
                    return ValidationResult.Failure(index, "BUILDING_NOT_OPERATIONAL", "Building is not operational (still under construction)");
                }
                if (string.IsNullOrEmpty(dto.UnitType))
                {
                    return ValidationResult.Failure(index, "MISSING_UNIT_TYPE", "Produce command requires unit type");
                }
                if (!Enum.TryParse<UnitType>(dto.UnitType, true, out var produceUnitType))
                {
                    return ValidationResult.Failure(index, "INVALID_UNIT_TYPE", $"Invalid unit type: {dto.UnitType}");
                }
                // Check if player has enough resources
                var productionCost = Unit.GetUnitCost(produceUnitType);
                if (match.GetPlayerResources(playerId) < productionCost)
                {
                    return ValidationResult.Failure(index, "INSUFFICIENT_RESOURCES", $"Insufficient resources to produce {produceUnitType}. Required: {productionCost}");
                }
                break;

            case CommandType.Research:
                var researchBuildingId = dto.BuildingId ?? dto.TargetBuildingId;
                if (researchBuildingId == null)
                {
                    return ValidationResult.Failure(index, "MISSING_TARGET_BUILDING", "Research command requires target building");
                }
                var researchBuilding = buildings.FirstOrDefault(b => b.Id == researchBuildingId.Value && b.MatchId == matchId);
                if (researchBuilding == null)
                {
                    return ValidationResult.Failure(index, "BUILDING_NOT_FOUND", $"Building {researchBuildingId} not found");
                }
                if (researchBuilding.OwnerId != playerId)
                {
                    return ValidationResult.Failure(index, "UNAUTHORIZED_BUILDING", "Cannot research from opponent's building");
                }
                if (!researchBuilding.IsOperational)
                {
                    return ValidationResult.Failure(index, "BUILDING_NOT_OPERATIONAL", "Building is not operational (still under construction)");
                }
                if (researchBuilding.Type != BuildingType.TechLab)
                {
                    return ValidationResult.Failure(index, "INVALID_BUILDING_TYPE", "Research can only be conducted at TechLab");
                }
                if (string.IsNullOrEmpty(dto.UpgradeType))
                {
                    return ValidationResult.Failure(index, "MISSING_UPGRADE_TYPE", "Research command requires upgrade type");
                }
                if (!Enum.TryParse<UpgradeType>(dto.UpgradeType, true, out var upgradeType))
                {
                    return ValidationResult.Failure(index, "INVALID_UPGRADE_TYPE", $"Invalid upgrade type: {dto.UpgradeType}");
                }
                // Check if upgrade is already completed or in progress
                var existingUpgrade = match.Upgrades.FirstOrDefault(u => u.PlayerId == playerId && u.Type == upgradeType);
                if (existingUpgrade != null)
                {
                    if (existingUpgrade.IsComplete)
                    {
                        return ValidationResult.Failure(index, "UPGRADE_ALREADY_COMPLETE", $"Upgrade {upgradeType} is already researched");
                    }
                    return ValidationResult.Failure(index, "UPGRADE_IN_PROGRESS", $"Upgrade {upgradeType} is already being researched");
                }
                // Check prerequisites
                var completedUpgrades = match.Upgrades.Where(u => u.PlayerId == playerId && u.IsComplete).ToList();
                if (!Upgrade.CheckPrerequisites(upgradeType, completedUpgrades))
                {
                    return ValidationResult.Failure(index, "PREREQUISITES_NOT_MET", $"Prerequisites not met for upgrade {upgradeType}");
                }
                // Check if player has enough resources
                var upgradeCost = Upgrade.GetUpgradeCost(upgradeType);
                if (match.GetPlayerResources(playerId) < upgradeCost)
                {
                    return ValidationResult.Failure(index, "INSUFFICIENT_RESOURCES", $"Insufficient resources to research {upgradeType}. Required: {upgradeCost}");
                }
                break;
        }

        return ValidationResult.Success();
    }

    private Command CreateCommandEntity(CommandDto dto, Guid matchId, Guid playerId, int currentTick)
    {
        var commandType = Enum.Parse<CommandType>(dto.CommandType, true);

        // Generate a unique command ID (simplified for now - could use database sequence)
        var commandId = DateTime.UtcNow.Ticks;

        var command = new Command(
            id: commandId,
            matchId: matchId,
            playerId: playerId,
            type: commandType,
            currentTick: currentTick);

        // Set command targets based on type
        command.SetTargetUnits(dto.UnitIds.ToList());

        if (dto.TargetPosition != null)
        {
            command.SetTargetPosition(new Position(dto.TargetPosition.X, dto.TargetPosition.Y));
        }

        if (dto.BuildingId.HasValue || dto.TargetBuildingId.HasValue)
        {
            var buildingId = dto.BuildingId ?? dto.TargetBuildingId ?? 0;
            if (buildingId > 0)
            {
                command.SetTargetBuilding(buildingId);
            }
        }

        if (dto.TargetUnitId.HasValue)
        {
            command.SetTargetEntity(dto.TargetUnitId.Value);
        }

        if (dto.TargetResourceId.HasValue)
        {
            command.SetTargetResourceDeposit(dto.TargetResourceId.Value);
        }

        if (!string.IsNullOrEmpty(dto.BuildingType) && Enum.TryParse<BuildingType>(dto.BuildingType, true, out var buildingType))
        {
            command.SetBuildingType(buildingType);
        }

        if (!string.IsNullOrEmpty(dto.UnitType) && Enum.TryParse<UnitType>(dto.UnitType, true, out var unitType))
        {
            command.SetUnitType(unitType);
        }

        if (!string.IsNullOrEmpty(dto.UpgradeType) && Enum.TryParse<UpgradeType>(dto.UpgradeType, true, out var upgradeType))
        {
            command.SetUpgradeType(upgradeType);
        }

        return command;
    }

    private class ValidationResult
    {
        public bool IsValid { get; init; }
        public CommandValidationError? Error { get; init; }

        public static ValidationResult Success() => new() { IsValid = true };

        public static ValidationResult Failure(int index, string code, string message) =>
            new()
            {
                IsValid = false,
                Error = new CommandValidationError
                {
                    CommandIndex = index,
                    ErrorCode = code,
                    ErrorMessage = message
                }
            };
    }
}
