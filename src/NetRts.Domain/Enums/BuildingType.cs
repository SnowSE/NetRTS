namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of building.
/// </summary>
public enum BuildingType
{
    /// <summary>
    /// Command Center - main building, losing it results in defeat.
    /// </summary>
    CommandCenter,

    /// <summary>
    /// Barracks - produces soldier units.
    /// </summary>
    Barracks,

    /// <summary>
    /// Resource Depot - workers deliver resources here.
    /// </summary>
    ResourceDepot,

    /// <summary>
    /// Tech Lab - enables research of upgrades.
    /// </summary>
    TechLab
}
