using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a controllable game entity (worker, soldier, scout).
/// </summary>
public class Unit
{
    /// <summary>
    /// Unique identifier within match.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Parent match ID.
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>
    /// Player who owns this unit.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Unit type.
    /// </summary>
    public UnitType Type { get; private set; }

    /// <summary>
    /// Current (X, Y) coordinates.
    /// </summary>
    public Position Position { get; private set; } = new(0, 0);

    /// <summary>
    /// Current health (0 = destroyed).
    /// </summary>
    public int HealthPoints { get; private set; }

    /// <summary>
    /// Maximum health.
    /// </summary>
    public int MaxHealthPoints { get; private set; }

    /// <summary>
    /// Damage per attack.
    /// </summary>
    public int AttackDamage { get; private set; }

    /// <summary>
    /// Attack range in tiles.
    /// </summary>
    public int AttackRange { get; private set; }

    /// <summary>
    /// Movement speed in tiles per tick.
    /// </summary>
    public int MovementSpeed { get; private set; }

    /// <summary>
    /// Vision radius in tiles.
    /// </summary>
    public int VisionRange { get; private set; }

    /// <summary>
    /// Current activity status.
    /// </summary>
    public UnitStatus CurrentStatus { get; private set; }

    /// <summary>
    /// Destination for movement commands.
    /// </summary>
    public Position? TargetPosition { get; private set; }

    /// <summary>
    /// Target unit/building for attack.
    /// </summary>
    public int? TargetEntityId { get; private set; }

    /// <summary>
    /// Resources being carried (workers only).
    /// </summary>
    public int ResourcesCarried { get; private set; }

    // EF Core constructor
    private Unit() { }

    public Unit(int id, Guid matchId, Guid ownerId, UnitType type, Position position)
    {
        Id = id;
        MatchId = matchId;
        OwnerId = ownerId;
        Type = type;
        Position = position;
        CurrentStatus = UnitStatus.Idle;

        // Apply default stats
        var stats = UnitStats.GetStatsForType(type);
        MaxHealthPoints = stats.MaxHealthPoints;
        HealthPoints = stats.MaxHealthPoints;
        AttackDamage = stats.AttackDamage;
        AttackRange = stats.AttackRange;
        MovementSpeed = stats.MovementSpeed;
        VisionRange = stats.VisionRange;
    }

    /// <summary>
    /// Apply damage to this unit.
    /// </summary>
    public void TakeDamage(int damage)
    {
        HealthPoints = Math.Max(0, HealthPoints - damage);
    }

    /// <summary>
    /// Check if unit is destroyed.
    /// </summary>
    public bool IsDestroyed() => HealthPoints <= 0;

    /// <summary>
    /// Move to a new position.
    /// </summary>
    public void MoveTo(Position newPosition)
    {
        Position = newPosition;
        CurrentStatus = UnitStatus.Moving;
    }

    /// <summary>
    /// Set idle status.
    /// </summary>
    public void SetIdle()
    {
        CurrentStatus = UnitStatus.Idle;
        TargetPosition = null;
        TargetEntityId = null;
    }

    /// <summary>
    /// Start attacking a target.
    /// </summary>
    public void Attack(int targetId)
    {
        TargetEntityId = targetId;
        CurrentStatus = UnitStatus.Attacking;
    }

    /// <summary>
    /// Start gathering resources.
    /// </summary>
    public void GatherResources()
    {
        CurrentStatus = UnitStatus.Gathering;
    }

    /// <summary>
    /// Carry resources (workers only).
    /// </summary>
    public void CarryResources(int amount)
    {
        if (Type != UnitType.Worker)
        {
            throw new InvalidOperationException("Only workers can carry resources.");
        }
        ResourcesCarried = amount;
    }

    /// <summary>
    /// Deposit carried resources.
    /// </summary>
    public int DepositResources()
    {
        var amount = ResourcesCarried;
        ResourcesCarried = 0;
        return amount;
    }

    /// <summary>
    /// Apply upgrade bonuses.
    /// </summary>
    public void ApplyUpgrade(int damageBonus, int healthBonus, int speedBonus)
    {
        AttackDamage += damageBonus;
        MaxHealthPoints += healthBonus;
        HealthPoints = Math.Min(HealthPoints + healthBonus, MaxHealthPoints);
        MovementSpeed += speedBonus;
    }

    /// <summary>
    /// Check if target is within attack range.
    /// </summary>
    public bool IsInAttackRange(Position targetPosition)
    {
        return Position.IsWithinRange(targetPosition, AttackRange);
    }

    /// <summary>
    /// Set the unit's status.
    /// </summary>
    public void SetStatus(UnitStatus status)
    {
        CurrentStatus = status;
    }

    /// <summary>
    /// Set the target entity for this unit.
    /// </summary>
    public void SetTargetEntity(int? targetId)
    {
        TargetEntityId = targetId;
    }

    /// <summary>
    /// Collect resources (workers only).
    /// </summary>
    public void CollectResources(int amount)
    {
        if (Type != UnitType.Worker)
        {
            throw new InvalidOperationException("Only workers can collect resources.");
        }
        ResourcesCarried += amount;
    }

    /// <summary>
    /// Get resource cost for this unit type.
    /// </summary>
    public static int GetUnitCost(UnitType type) => type switch
    {
        UnitType.Worker => 50,
        UnitType.Soldier => 100,
        UnitType.Scout => 75,
        _ => throw new ArgumentException($"Unknown unit type: {type}")
    };

    /// <summary>
    /// Get production time in ticks for this unit type.
    /// </summary>
    public static int GetProductionTime(UnitType type) => type switch
    {
        UnitType.Worker => 5,
        UnitType.Soldier => 10,
        UnitType.Scout => 7,
        _ => throw new ArgumentException($"Unknown unit type: {type}")
    };
}
