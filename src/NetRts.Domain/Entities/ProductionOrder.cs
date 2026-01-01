using NetRts.Domain.Enums;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a unit being produced by a building.
/// </summary>
public class ProductionOrder
{
    /// <summary>
    /// Type of unit being produced.
    /// </summary>
    public UnitType UnitType { get; private set; }

    /// <summary>
    /// Ticks remaining until production completes.
    /// </summary>
    public int TicksRemaining { get; private set; }

    /// <summary>
    /// Total ticks required for production.
    /// </summary>
    public int TotalTicks { get; private set; }

    // EF Core constructor
    private ProductionOrder() { }

    public ProductionOrder(UnitType unitType)
    {
        UnitType = unitType;
        TotalTicks = Unit.GetProductionTime(unitType);
        TicksRemaining = TotalTicks;
    }

    /// <summary>
    /// Advance production by one tick. Returns true if complete.
    /// </summary>
    public bool AdvanceProduction()
    {
        if (TicksRemaining > 0)
        {
            TicksRemaining--;
        }
        return TicksRemaining == 0;
    }

    /// <summary>
    /// Get production progress as a percentage (0-100).
    /// </summary>
    public int GetProgressPercentage()
    {
        if (TotalTicks == 0) return 100;
        return (int)((TotalTicks - TicksRemaining) * 100.0 / TotalTicks);
    }
}
