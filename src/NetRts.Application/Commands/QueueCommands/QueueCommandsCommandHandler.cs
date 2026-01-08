using MediatR;
using NetRts.Application.Services;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Application.Commands.QueueCommands;

/// <summary>
/// Handler for QueueCommandsCommand that adds commands to the player's command queue.
/// </summary>
public class QueueCommandsCommandHandler : IRequestHandler<QueueCommandsCommand, QueueCommandsResponse>
{
    private readonly ICommandQueueManager _commandQueueManager;
    private const int MaxQueueSize = 500;

    public QueueCommandsCommandHandler(ICommandQueueManager commandQueueManager)
    {
        _commandQueueManager = commandQueueManager;
    }

    public async Task<QueueCommandsResponse> Handle(
        QueueCommandsCommand request,
        CancellationToken cancellationToken)
    {
        var queuedCount = 0;
        var failedCount = 0;
        var errors = new List<Contracts.Responses.CommandValidationError>();

        for (int i = 0; i < request.Commands.Length; i++)
        {
            var commandDto = request.Commands[i];
            try
            {
                // Map DTO to domain command entity
                var command = MapCommandDtoToEntity(commandDto, request.MatchId, request.PlayerId);

                // Attempt to enqueue
                var success = await _commandQueueManager.EnqueueCommandAsync(command, MaxQueueSize);

                if (success)
                {
                    queuedCount++;
                }
                else
                {
                    failedCount++;
                    errors.Add(new Contracts.Responses.CommandValidationError
                    {
                        CommandIndex = i,
                        ErrorMessage = $"Command queue is full (max {MaxQueueSize})",
                        ErrorCode = "QUEUE_FULL"
                    });
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add(new Contracts.Responses.CommandValidationError
                {
                    CommandIndex = i,
                    ErrorMessage = $"Failed to queue command: {ex.Message}",
                    ErrorCode = "QUEUE_ERROR"
                });
            }
        }

        return new QueueCommandsResponse
        {
            QueuedCount = queuedCount,
            FailedCount = failedCount,
            Errors = errors
        };
    }

    private static Command MapCommandDtoToEntity(
        Contracts.Requests.CommandDto dto,
        Guid matchId,
        Guid playerId)
    {
        var commandType = Enum.Parse<CommandType>(dto.CommandType, ignoreCase: true);

        var command = new Command(
            id: Guid.NewGuid().GetHashCode(),
            matchId: matchId,
            playerId: playerId,
            type: commandType,
            currentTick: 0); // Will be set during execution

        // Set target position for Move and Build commands
        if (dto.TargetPosition != null)
        {
            command.SetTargetPosition(new Position(dto.TargetPosition.X, dto.TargetPosition.Y));
        }

        // Set target unit IDs for commands that operate on units
        if (dto.UnitIds != null && dto.UnitIds.Length > 0)
        {
            command.SetTargetUnits(dto.UnitIds.ToList());
        }

        // Set target entity ID for Attack commands
        if (dto.TargetUnitId.HasValue)
        {
            command.SetTargetEntity(dto.TargetUnitId.Value);
        }

        // Set target resource deposit for Gather commands
        if (dto.TargetResourceId.HasValue)
        {
            command.SetTargetResourceDeposit(dto.TargetResourceId.Value);
        }

        // Set building type for Build commands
        if (!string.IsNullOrEmpty(dto.BuildingType))
        {
            var buildingType = Enum.Parse<BuildingType>(dto.BuildingType, ignoreCase: true);
            command.SetBuildingType(buildingType);
        }

        // Set building ID for Produce/Research commands
        if (dto.TargetBuildingId.HasValue)
        {
            command.SetTargetBuilding(dto.TargetBuildingId.Value);
        }

        // Set unit type for Produce commands
        if (!string.IsNullOrEmpty(dto.UnitType))
        {
            var unitType = Enum.Parse<UnitType>(dto.UnitType, ignoreCase: true);
            command.SetUnitType(unitType);
        }

        // Set upgrade type for Research commands
        if (!string.IsNullOrEmpty(dto.UpgradeType))
        {
            var upgradeType = Enum.Parse<UpgradeType>(dto.UpgradeType, ignoreCase: true);
            command.SetUpgradeType(upgradeType);
        }

        return command;
    }
}
