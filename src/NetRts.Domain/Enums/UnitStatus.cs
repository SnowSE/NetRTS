namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the current status/activity of a unit.
/// </summary>
public enum UnitStatus
{
    /// <summary>
    /// Unit is not performing any action.
    /// </summary>
    Idle,

    /// <summary>
    /// Unit is moving to a target position.
    /// </summary>
    Moving,

    /// <summary>
    /// Unit is attacking an enemy.
    /// </summary>
    Attacking,

    /// <summary>
    /// Unit is gathering resources.
    /// </summary>
    Gathering,

    /// <summary>
    /// Unit is constructing a building.
    /// </summary>
    Constructing
}
