using NetRts.Domain.Enums;

namespace NetRts.Domain.ValueObjects;

/// <summary>
/// Type-specific unit attributes and combat statistics.
/// </summary>
public record UnitStats
{
    /// <summary>
    /// The unit type these stats apply to.
    /// </summary>
    public UnitType UnitType { get; init; }

    /// <summary>
    /// Maximum health points.
    /// </summary>
    public int MaxHealthPoints { get; init; }

    /// <summary>
    /// Damage dealt per attack.
    /// </summary>
    public int AttackDamage { get; init; }

    /// <summary>
    /// Attack range in tiles.
    /// </summary>
    public int AttackRange { get; init; }

    /// <summary>
    /// Movement speed in tiles per tick.
    /// </summary>
    public int MovementSpeed { get; init; }

    /// <summary>
    /// Vision radius in tiles.
    /// </summary>
    public int VisionRange { get; init; }

    /// <summary>
    /// Resource cost to produce this unit.
    /// </summary>
    public int ResourceCost { get; init; }

    /// <summary>
    /// Ticks required to produce this unit.
    /// </summary>
    public int ProductionTime { get; init; }

    public UnitStats(
        UnitType unitType,
        int maxHealthPoints,
        int attackDamage,
        int attackRange,
        int movementSpeed,
        int visionRange,
        int resourceCost,
        int productionTime)
    {
        UnitType = unitType;
        MaxHealthPoints = maxHealthPoints;
        AttackDamage = attackDamage;
        AttackRange = attackRange;
        MovementSpeed = movementSpeed;
        VisionRange = visionRange;
        ResourceCost = resourceCost;
        ProductionTime = productionTime;
    }

    /// <summary>
    /// Get default stats for a given unit type.
    /// </summary>
    public static UnitStats GetStatsForType(UnitType unitType) => unitType switch
    {
        UnitType.Worker => new UnitStats(
            unitType: UnitType.Worker,
            maxHealthPoints: 50,
            attackDamage: 5,
            attackRange: 1,
            movementSpeed: 2,
            visionRange: 5,
            resourceCost: 50,
            productionTime: 5),

        UnitType.Soldier => new UnitStats(
            unitType: UnitType.Soldier,
            maxHealthPoints: 100,
            attackDamage: 15,
            attackRange: 2,
            movementSpeed: 1,
            visionRange: 5,
            resourceCost: 100,
            productionTime: 10),

        UnitType.Scout => new UnitStats(
            unitType: UnitType.Scout,
            maxHealthPoints: 60,
            attackDamage: 8,
            attackRange: 2,
            movementSpeed: 3,
            visionRange: 8,
            resourceCost: 75,
            productionTime: 7),

        _ => throw new ArgumentException($"Unknown unit type: {unitType}", nameof(unitType))
    };

    /// <summary>
    /// Apply upgrade modifications to stats.
    /// </summary>
    public UnitStats WithUpgrades(int damageBonus, int healthBonus, int speedBonus)
    {
        return this with
        {
            AttackDamage = AttackDamage + damageBonus,
            MaxHealthPoints = MaxHealthPoints + healthBonus,
            MovementSpeed = MovementSpeed + speedBonus
        };
    }
}
