namespace NetRts.Protocol;

/// <summary>
/// One order from a bot. Which fields matter depends on <see cref="Type"/>:
/// <list type="bullet">
/// <item>Move: units + (x,y | tile)</item>
/// <item>Attack: units + (targetId | x,y | tile). A position means attack-move.</item>
/// <item>Gather: units + targetId (a resource deposit)</item>
/// <item>Build: units + buildingType + (x,y | tile)</item>
/// <item>Produce: buildingId + unitType [+ count] [+ x,y | tile to set the rally point]</item>
/// <item>Rally: buildingId + (x,y | tile)</item>
/// <item>Research: buildingId + upgrade</item>
/// <item>Stop: units</item>
/// </list>
/// Units are selected with <see cref="Units"/> (e.g. "5-10", "1-7,12", "all", "idle")
/// and/or <see cref="UnitIds"/>, optionally narrowed by <see cref="UnitType"/>.
/// </summary>
public sealed record CommandRequest
{
    public CommandType Type { get; init; }

    /// <summary>Selector string: comma separated ids and inclusive ranges ("1-7,12"), or "all" / "idle".</summary>
    public string? Units { get; init; }

    /// <summary>Explicit unit ids. Every id listed here must be a unit you own.</summary>
    public int[]? UnitIds { get; init; }

    /// <summary>For unit commands: only select units of this type. For Produce: the unit to produce.</summary>
    public UnitType? UnitType { get; init; }

    /// <summary>Attack target (unit or building) or Gather target (resource deposit).</summary>
    public int? TargetId { get; init; }

    public int? X { get; init; }
    public int? Y { get; init; }

    /// <summary>Alternative to X/Y: tile index = y * mapWidth + x.</summary>
    public int? Tile { get; init; }

    public BuildingType? BuildingType { get; init; }

    /// <summary>The acting building for Produce / Research / Rally.</summary>
    public int? BuildingId { get; init; }

    public UpgradeType? Upgrade { get; init; }

    /// <summary>Produce: how many units to queue (default 1).</summary>
    public int? Count { get; init; }
}

public sealed record SubmitCommandsRequest
{
    public IReadOnlyList<CommandRequest> Commands { get; init; } = [];
}

public sealed record CommandError(string Code, string Message);

public sealed record CommandResult
{
    public int Index { get; init; }
    public bool Accepted { get; init; }

    /// <summary>Id of the queued command; execution outcomes are reported as events carrying this id.</summary>
    public long? CommandId { get; init; }

    public CommandError? Error { get; init; }
}

public sealed record SubmitCommandsResponse
{
    public int Accepted { get; init; }
    public int Rejected { get; init; }
    public IReadOnlyList<CommandResult> Results { get; init; } = [];
    public int QueueSize { get; init; }
    public int QueueCapacity { get; init; }
}
