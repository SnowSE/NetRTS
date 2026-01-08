namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of upgrade that can be researched.
/// </summary>
public enum UpgradeType
{
    /// <summary>
    /// Weapon Damage Level 1 - increases unit attack damage by 5.
    /// </summary>
    WeaponDamage1,

    /// <summary>
    /// Weapon Damage Level 2 - increases unit attack damage by additional 10 (requires WeaponDamage1).
    /// </summary>
    WeaponDamage2,

    /// <summary>
    /// Armor Level 1 - increases unit max health by 20.
    /// </summary>
    Armor1,

    /// <summary>
    /// Armor Level 2 - increases unit max health by additional 40 (requires Armor1).
    /// </summary>
    Armor2,

    /// <summary>
    /// Speed Level 1 - increases unit movement speed by 1.
    /// </summary>
    Speed1,

    /// <summary>
    /// Speed Level 2 - increases unit movement speed by additional 1 (requires Speed1).
    /// </summary>
    Speed2
}
