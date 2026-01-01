namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of upgrade that can be researched.
/// </summary>
public enum UpgradeType
{
    /// <summary>
    /// Melee Damage Level 1 - increases melee unit attack damage by 5.
    /// </summary>
    MeleeDamage,

    /// <summary>
    /// Melee Damage Level 2 - increases melee unit attack damage by additional 5 (requires MeleeDamage).
    /// </summary>
    MeleeDamage2,

    /// <summary>
    /// Ranged Damage Level 1 - increases ranged unit attack damage by 5.
    /// </summary>
    RangedDamage,

    /// <summary>
    /// Ranged Damage Level 2 - increases ranged unit attack damage by additional 5 (requires RangedDamage).
    /// </summary>
    RangedDamage2,

    /// <summary>
    /// Armor Upgrade - increases unit armor by 2.
    /// </summary>
    ArmorUpgrade,

    /// <summary>
    /// Armor Upgrade Level 2 - increases unit armor by additional 2 (requires ArmorUpgrade).
    /// </summary>
    ArmorUpgrade2
}
