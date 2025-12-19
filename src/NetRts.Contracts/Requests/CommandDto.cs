using NetRts.Contracts.Responses;

namespace NetRts.Contracts.Requests;

/// <summary>
/// Command data transfer object for queuing unit commands.
/// </summary>
public class CommandDto
{
    /// <summary>
    /// Type of command: Move, Attack, Gather, Build, Produce, Research
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// IDs of units to execute this command
    /// </summary>
    public int[]? UnitIds { get; init; }

    /// <summary>
    /// Target position for Move commands
    /// </summary>
    public PositionDto? TargetPosition { get; init; }

    /// <summary>
    /// Target unit ID for Attack commands
    /// </summary>
    public int? TargetUnitId { get; init; }

    /// <summary>
    /// Target resource deposit ID for Gather commands
    /// </summary>
    public int? TargetResourceDepositId { get; init; }

    /// <summary>
    /// Building type for Build commands
    /// </summary>
    public string? BuildingType { get; init; }

    /// <summary>
    /// Building ID for Produce/Research commands
    /// </summary>
    public int? BuildingId { get; init; }

    /// <summary>
    /// Unit type for Produce commands
    /// </summary>
    public string? UnitType { get; init; }

    /// <summary>
    /// Upgrade type for Research commands
    /// </summary>
    public string? UpgradeType { get; init; }
}
