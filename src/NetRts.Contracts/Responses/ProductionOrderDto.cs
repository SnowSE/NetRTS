namespace NetRts.Contracts.Responses;

/// <summary>
/// Represents a unit production order in a building's queue.
/// </summary>
public class ProductionOrderDto
{
    /// <summary>
    /// Type of unit being produced
    /// </summary>
    public required string UnitType { get; init; }

    /// <summary>
    /// Number of ticks remaining until unit spawns
    /// </summary>
    public required int TicksRemaining { get; init; }

    /// <summary>
    /// Total ticks required for production
    /// </summary>
    public required int TotalTicks { get; init; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public required int ProgressPercentage { get; init; }
}
