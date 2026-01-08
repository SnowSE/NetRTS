namespace NetRts.Contracts.Responses;

/// <summary>
/// Represents an upgrade's progress and effects.
/// </summary>
public class UpgradeDto
{
    /// <summary>
    /// Unique identifier for this upgrade instance.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Type of upgrade (e.g., "WeaponDamage1", "Armor1", "Speed1").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Research progress percentage (0-100).
    /// </summary>
    public int ResearchProgress { get; set; }

    /// <summary>
    /// True when ResearchProgress == 100.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Tick when research started.
    /// </summary>
    public int StartedAtTick { get; set; }

    /// <summary>
    /// Damage bonus provided by this upgrade.
    /// </summary>
    public int DamageBonus { get; set; }

    /// <summary>
    /// Health bonus provided by this upgrade.
    /// </summary>
    public int HealthBonus { get; set; }

    /// <summary>
    /// Speed bonus provided by this upgrade.
    /// </summary>
    public int SpeedBonus { get; set; }
}
