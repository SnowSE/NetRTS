using FluentValidation;
using NetRts.Application.Interfaces;
using NetRts.Domain.Enums;

namespace NetRts.Application.Commands.QueueCommands;

/// <summary>
/// Validator for QueueCommandsCommand that ensures commands are valid and authorized.
/// </summary>
public class QueueCommandsCommandValidator : AbstractValidator<QueueCommandsCommand>
{
    private readonly IGameStateCache _gameStateCache;

    public QueueCommandsCommandValidator(IGameStateCache gameStateCache)
    {
        _gameStateCache = gameStateCache;

        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match ID is required");

        RuleFor(x => x.PlayerId)
            .NotEmpty()
            .WithMessage("Player ID is required");

        RuleFor(x => x.Commands)
            .NotEmpty()
            .WithMessage("At least one command is required")
            .Must(commands => commands.Length <= 100)
            .WithMessage("Maximum 100 commands per request");

        RuleForEach(x => x.Commands)
            .ChildRules(command =>
            {
                command.RuleFor(c => c.Type)
                    .NotEmpty()
                    .WithMessage("Command type is required")
                    .Must(type => Enum.TryParse<CommandType>(type, true, out _))
                    .WithMessage("Invalid command type");

                // Move command validation
                command.When(c => c.Type.Equals("Move", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.UnitIds)
                        .NotEmpty()
                        .WithMessage("Unit IDs are required for Move command");

                    command.RuleFor(c => c.TargetPosition)
                        .NotNull()
                        .WithMessage("Target position is required for Move command");

                    command.RuleFor(c => c.TargetPosition!.X)
                        .GreaterThanOrEqualTo(0)
                        .LessThan(200)
                        .WithMessage("Target X coordinate must be within map bounds");

                    command.RuleFor(c => c.TargetPosition!.Y)
                        .GreaterThanOrEqualTo(0)
                        .LessThan(200)
                        .WithMessage("Target Y coordinate must be within map bounds");
                });

                // Attack command validation
                command.When(c => c.Type.Equals("Attack", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.UnitIds)
                        .NotEmpty()
                        .WithMessage("Unit IDs are required for Attack command");

                    command.RuleFor(c => c.TargetUnitId)
                        .NotNull()
                        .GreaterThan(0)
                        .WithMessage("Target unit ID is required for Attack command");
                });

                // Gather command validation
                command.When(c => c.Type.Equals("Gather", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.UnitIds)
                        .NotEmpty()
                        .WithMessage("Unit IDs are required for Gather command");

                    command.RuleFor(c => c.TargetResourceDepositId)
                        .NotNull()
                        .GreaterThan(0)
                        .WithMessage("Target resource deposit ID is required for Gather command");
                });

                // Build command validation
                command.When(c => c.Type.Equals("Build", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.UnitIds)
                        .NotEmpty()
                        .WithMessage("Unit IDs are required for Build command");

                    command.RuleFor(c => c.BuildingType)
                        .NotEmpty()
                        .WithMessage("Building type is required for Build command")
                        .Must(type => Enum.TryParse<BuildingType>(type, true, out _))
                        .WithMessage("Invalid building type");

                    command.RuleFor(c => c.TargetPosition)
                        .NotNull()
                        .WithMessage("Target position is required for Build command");
                });

                // Produce command validation
                command.When(c => c.Type.Equals("Produce", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.BuildingId)
                        .NotNull()
                        .GreaterThan(0)
                        .WithMessage("Building ID is required for Produce command");

                    command.RuleFor(c => c.UnitType)
                        .NotEmpty()
                        .WithMessage("Unit type is required for Produce command")
                        .Must(type => Enum.TryParse<UnitType>(type, true, out _))
                        .WithMessage("Invalid unit type");
                });

                // Research command validation
                command.When(c => c.Type.Equals("Research", StringComparison.OrdinalIgnoreCase), () =>
                {
                    command.RuleFor(c => c.BuildingId)
                        .NotNull()
                        .GreaterThan(0)
                        .WithMessage("Building ID is required for Research command");

                    command.RuleFor(c => c.UpgradeType)
                        .NotEmpty()
                        .WithMessage("Upgrade type is required for Research command")
                        .Must(type => Enum.TryParse<UpgradeType>(type, true, out _))
                        .WithMessage("Invalid upgrade type");
                });
            });

        // Custom validation for unit ownership
        RuleFor(x => x)
            .Custom((command, context) =>
            {
                var match = _gameStateCache.GetMatch(command.MatchId);
                if (match == null)
                {
                    context.AddFailure("Match not found");
                    return;
                }

                // Verify player is participant
                if (match.Player1Id != command.PlayerId && match.Player2Id != command.PlayerId)
                {
                    context.AddFailure("Player is not a participant in this match");
                    return;
                }

                // Get units for validation
                var units = _gameStateCache.GetUnitsForMatch(command.MatchId);
                var buildings = _gameStateCache.GetBuildingsForMatch(command.MatchId);
                var resourceDeposits = _gameStateCache.GetResourceDepositsForMatch(command.MatchId);

                foreach (var cmd in command.Commands)
                {
                    // Validate unit ownership for commands that target units
                    if (cmd.UnitIds != null && cmd.UnitIds.Length > 0)
                    {
                        foreach (var unitId in cmd.UnitIds)
                        {
                            var unit = units.FirstOrDefault(u => u.Id == unitId);
                            if (unit == null)
                            {
                                context.AddFailure($"Unit {unitId} not found");
                            }
                            else if (unit.OwnerId != command.PlayerId)
                            {
                                context.AddFailure($"Unit {unitId} is not owned by player");
                            }
                        }
                    }

                    // Validate target unit exists for attack commands
                    if (cmd.Type.Equals("Attack", StringComparison.OrdinalIgnoreCase) && cmd.TargetUnitId.HasValue)
                    {
                        var targetUnit = units.FirstOrDefault(u => u.Id == cmd.TargetUnitId.Value);
                        if (targetUnit == null)
                        {
                            context.AddFailure($"Target unit {cmd.TargetUnitId} not found");
                        }
                    }

                    // Validate resource deposit exists for gather commands
                    if (cmd.Type.Equals("Gather", StringComparison.OrdinalIgnoreCase) && cmd.TargetResourceDepositId.HasValue)
                    {
                        var deposit = resourceDeposits.FirstOrDefault(r => r.Id == cmd.TargetResourceDepositId.Value);
                        if (deposit == null)
                        {
                            context.AddFailure($"Resource deposit {cmd.TargetResourceDepositId} not found");
                        }
                    }

                    // Validate building ownership for produce/research commands
                    if ((cmd.Type.Equals("Produce", StringComparison.OrdinalIgnoreCase) ||
                         cmd.Type.Equals("Research", StringComparison.OrdinalIgnoreCase)) &&
                        cmd.BuildingId.HasValue)
                    {
                        var building = buildings.FirstOrDefault(b => b.Id == cmd.BuildingId.Value);
                        if (building == null)
                        {
                            context.AddFailure($"Building {cmd.BuildingId} not found");
                        }
                        else if (building.OwnerId != command.PlayerId)
                        {
                            context.AddFailure($"Building {cmd.BuildingId} is not owned by player");
                        }
                        else if (!building.IsOperational)
                        {
                            context.AddFailure($"Building {cmd.BuildingId} is not operational yet");
                        }
                    }

                    // Validate build command: tile unoccupied, within bounds, sufficient resources
                    if (cmd.Type.Equals("Build", StringComparison.OrdinalIgnoreCase) && cmd.TargetPosition != null)
                    {
                        var targetX = cmd.TargetPosition.X;
                        var targetY = cmd.TargetPosition.Y;

                        // Check bounds (already checked in basic validation, but double-check here)
                        if (targetX < 0 || targetX >= 200 || targetY < 0 || targetY >= 200)
                        {
                            context.AddFailure($"Build position ({targetX},{targetY}) is out of bounds");
                        }

                        // Check if tile is occupied by existing building
                        var existingBuilding = buildings.FirstOrDefault(b =>
                            b.Position.X == targetX && b.Position.Y == targetY);
                        if (existingBuilding != null)
                        {
                            context.AddFailure($"Tile at position ({targetX},{targetY}) is already occupied by a building");
                        }

                        // Check sufficient resources for building
                        if (!string.IsNullOrEmpty(cmd.BuildingType) &&
                            Enum.TryParse<BuildingType>(cmd.BuildingType, true, out var buildingType))
                        {
                            var buildCost = NetRts.Domain.Entities.Building.GetBuildingCost(buildingType);
                            var playerResources = match.GetPlayerResources(command.PlayerId);
                            if (playerResources < buildCost)
                            {
                                context.AddFailure($"Insufficient resources to build {cmd.BuildingType}. Required: {buildCost}, Available: {playerResources}");
                            }
                        }
                    }

                    // Validate produce command: sufficient resources
                    if (cmd.Type.Equals("Produce", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(cmd.UnitType))
                    {
                        if (Enum.TryParse<UnitType>(cmd.UnitType, true, out var unitType))
                        {
                            var unitCost = NetRts.Domain.Entities.Unit.GetUnitCost(unitType);
                            var playerResources = match.GetPlayerResources(command.PlayerId);
                            if (playerResources < unitCost)
                            {
                                context.AddFailure($"Insufficient resources to produce {cmd.UnitType}. Required: {unitCost}, Available: {playerResources}");
                            }
                        }
                    }

                    // Validate research command: building is TechLab, sufficient resources, prerequisites
                    if (cmd.Type.Equals("Research", StringComparison.OrdinalIgnoreCase) && cmd.BuildingId.HasValue)
                    {
                        var building = buildings.FirstOrDefault(b => b.Id == cmd.BuildingId.Value);
                        if (building != null)
                        {
                            // Verify building is a TechLab
                            if (building.Type != BuildingType.TechLab)
                            {
                                context.AddFailure($"Building {cmd.BuildingId} is not a TechLab. Research can only be performed at TechLab buildings.");
                            }

                            // Check sufficient resources and prerequisites
                            if (!string.IsNullOrEmpty(cmd.UpgradeType) &&
                                Enum.TryParse<UpgradeType>(cmd.UpgradeType, true, out var upgradeType))
                            {
                                var upgradeCost = NetRts.Domain.Entities.Upgrade.GetUpgradeCost(upgradeType);
                                var playerResources = match.GetPlayerResources(command.PlayerId);
                                if (playerResources < upgradeCost)
                                {
                                    context.AddFailure($"Insufficient resources to research {cmd.UpgradeType}. Required: {upgradeCost}, Available: {playerResources}");
                                }

                                // Validate prerequisites
                                var prerequisite = NetRts.Domain.Entities.Upgrade.GetPrerequisiteUpgrade(upgradeType);
                                if (prerequisite.HasValue)
                                {
                                    var player = match.Players.FirstOrDefault(p => p.PlayerId == command.PlayerId);
                                    if (player != null)
                                    {
                                        var hasPrerequisite = player.Upgrades.Any(u =>
                                            u.UpgradeType == prerequisite.Value && u.IsCompleted);
                                        if (!hasPrerequisite)
                                        {
                                            context.AddFailure($"Prerequisite upgrade {prerequisite.Value} must be completed before researching {cmd.UpgradeType}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
    }
}
