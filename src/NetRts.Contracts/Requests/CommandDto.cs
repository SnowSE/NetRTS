using NetRts.Contracts.Responses;

namespace NetRts.Contracts.Requests;

/// <summary>
/// Represents a command to be executed for one or more units.
/// </summary>
public class CommandDto
{
    /// <summary>
    /// Type of command: "Move", "Attack", "Gather", "Build", "Produce", "Research"
    /// </summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Alias for CommandType to support various test clients.
    /// </summary>
    public string Type { get => CommandType; set => CommandType = value; }

    /// <summary>
    /// IDs of units that should execute this command.
    /// </summary>
    public int[] UnitIds { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Building ID for Produce or Research commands.
    /// </summary>
    public int? BuildingId { get; set; }

    /// <summary>
    /// Target position for Move commands.
    /// </summary>
    public PositionDto? TargetPosition { get; set; }

    /// <summary>
    /// Target unit ID for Attack commands.
    /// </summary>
    public int? TargetUnitId { get; set; }

    /// <summary>
    /// Target building ID for attacks or production.
    /// </summary>
    public int? TargetBuildingId { get; set; }

    /// <summary>
    /// Target resource deposit ID for Gather commands.
    /// </summary>
    public int? TargetResourceId { get; set; }

    /// <summary>
    /// Building type for Build commands.
    /// </summary>
    public string? BuildingType { get; set; }

    /// <summary>
    /// Unit type for Produce commands.
    /// </summary>
    public string? UnitType { get; set; }

    /// <summary>
    /// Upgrade type for Research commands.
    /// </summary>
    public string? UpgradeType { get; set; }
}
