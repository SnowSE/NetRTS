namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of unit.
/// </summary>
public enum UnitType
{
    /// <summary>
    /// Worker unit - can gather resources and construct buildings.
    /// </summary>
    Worker,

    /// <summary>
    /// Soldier unit - combat unit with high damage.
    /// </summary>
    Soldier,

    /// <summary>
    /// Scout unit - fast unit with extended vision range.
    /// </summary>
    Scout
}
