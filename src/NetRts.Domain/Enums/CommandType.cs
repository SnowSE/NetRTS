namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of command issued to units or buildings.
/// </summary>
public enum CommandType
{
    /// <summary>
    /// Move units to a target position.
    /// </summary>
    Move,

    /// <summary>
    /// Attack a target unit or building.
    /// </summary>
    Attack,

    /// <summary>
    /// Gather resources from a resource deposit.
    /// </summary>
    Gather,

    /// <summary>
    /// Construct a building at a target location.
    /// </summary>
    Build,

    /// <summary>
    /// Produce a unit from a building.
    /// </summary>
    Produce,

    /// <summary>
    /// Research an upgrade at a tech building.
    /// </summary>
    Research
}
