using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a queued action submitted by a player.
/// </summary>
public class Command
{
    /// <summary>
    /// Unique command identifier.
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    /// Parent match ID.
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>
    /// Player who issued the command.
    /// </summary>
    public Guid PlayerId { get; private set; }

    /// <summary>
    /// Command type.
    /// </summary>
    public CommandType Type { get; private set; }

    /// <summary>
    /// Unit IDs receiving this command.
    /// </summary>
    public List<int> TargetUnitIds { get; private set; } = new();

    /// <summary>
    /// Building ID for production/research commands.
    /// </summary>
    public int? TargetBuildingId { get; private set; }

    /// <summary>
    /// Destination for move/build commands.
    /// </summary>
    public Position? TargetPosition { get; private set; }

    /// <summary>
    /// Target entity for attack commands.
    /// </summary>
    public int? TargetEntityId { get; private set; }

    /// <summary>
    /// Resource deposit for gather commands.
    /// </summary>
    public int? TargetResourceDepositId { get; private set; }

    /// <summary>
    /// Building type for build commands.
    /// </summary>
    public BuildingType? BuildingType { get; private set; }

    /// <summary>
    /// Unit type for produce commands.
    /// </summary>
    public UnitType? UnitType { get; private set; }

    /// <summary>
    /// Upgrade type for research commands.
    /// </summary>
    public UpgradeType? UpgradeType { get; private set; }

    /// <summary>
    /// Tick when command was queued.
    /// </summary>
    public int SubmittedAtTick { get; private set; }

    /// <summary>
    /// Tick when command was executed.
    /// </summary>
    public int? ProcessedAtTick { get; private set; }

    /// <summary>
    /// Command execution status.
    /// </summary>
    public CommandStatus Status { get; private set; }

    /// <summary>
    /// Error message if Status == Failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    // EF Core constructor
    private Command() { }

    public Command(
        long id,
        Guid matchId,
        Guid playerId,
        CommandType type,
        int currentTick)
    {
        Id = id;
        MatchId = matchId;
        PlayerId = playerId;
        Type = type;
        SubmittedAtTick = currentTick;
        Status = CommandStatus.Queued;
    }

    /// <summary>
    /// Set target units for this command.
    /// </summary>
    public void SetTargetUnits(List<int> unitIds)
    {
        TargetUnitIds = unitIds ?? new List<int>();
    }

    /// <summary>
    /// Set target position for move/build commands.
    /// </summary>
    public void SetTargetPosition(Position position)
    {
        TargetPosition = position;
    }

    /// <summary>
    /// Set target entity for attack commands.
    /// </summary>
    public void SetTargetEntity(int entityId)
    {
        TargetEntityId = entityId;
    }

    /// <summary>
    /// Set target resource deposit for gather commands.
    /// </summary>
    public void SetTargetResourceDeposit(int depositId)
    {
        TargetResourceDepositId = depositId;
    }

    /// <summary>
    /// Set building type for build commands.
    /// </summary>
    public void SetBuildingType(BuildingType buildingType)
    {
        BuildingType = buildingType;
    }

    /// <summary>
    /// Set target building for production/research.
    /// </summary>
    public void SetTargetBuilding(int buildingId)
    {
        TargetBuildingId = buildingId;
    }

    /// <summary>
    /// Set unit type for produce commands.
    /// </summary>
    public void SetUnitType(UnitType unitType)
    {
        UnitType = unitType;
    }

    /// <summary>
    /// Set upgrade type for research commands.
    /// </summary>
    public void SetUpgradeType(UpgradeType upgradeType)
    {
        UpgradeType = upgradeType;
    }

    /// <summary>
    /// Mark command as executed.
    /// </summary>
    public void MarkExecuted(int tick)
    {
        Status = CommandStatus.Executed;
        ProcessedAtTick = tick;
    }

    /// <summary>
    /// Mark command as failed with reason.
    /// </summary>
    public void MarkFailed(string reason, int tick)
    {
        Status = CommandStatus.Failed;
        FailureReason = reason;
        ProcessedAtTick = tick;
    }

    /// <summary>
    /// Mark command as cancelled.
    /// </summary>
    public void MarkCancelled()
    {
        Status = CommandStatus.Cancelled;
    }
}
