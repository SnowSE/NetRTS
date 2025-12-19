using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a structure that produces units or enables upgrades.
/// </summary>
public class Building
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
    /// Player who owns this building.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Building type.
    /// </summary>
    public BuildingType Type { get; private set; }

    /// <summary>
    /// Location on map.
    /// </summary>
    public Position Position { get; private set; } = new(0, 0);

    /// <summary>
    /// Current health.
    /// </summary>
    public int HealthPoints { get; private set; }

    /// <summary>
    /// Maximum health.
    /// </summary>
    public int MaxHealthPoints { get; private set; }

    /// <summary>
    /// Construction progress percentage (0-100).
    /// </summary>
    public int ConstructionProgress { get; private set; }

    /// <summary>
    /// True when ConstructionProgress == 100.
    /// </summary>
    public bool IsOperational { get; private set; }

    /// <summary>
    /// Vision radius in tiles.
    /// </summary>
    public int VisionRange { get; private set; }

    /// <summary>
    /// Queue of units being produced (unit type, ticks remaining).
    /// </summary>
    public List<ProductionOrder> ProductionQueue { get; private set; } = new();

    // EF Core constructor
    private Building() { }

    public Building(int id, Guid matchId, Guid ownerId, BuildingType type, Position position)
    {
        Id = id;
        MatchId = matchId;
        OwnerId = ownerId;
        Type = type;
        Position = position;
        ConstructionProgress = 0;
        IsOperational = false;

        // Set default stats based on type
        (MaxHealthPoints, VisionRange) = type switch
        {
            BuildingType.CommandCenter => (500, 6),
            BuildingType.Barracks => (300, 5),
            BuildingType.ResourceDepot => (200, 5),
            BuildingType.TechLab => (250, 5),
            _ => throw new ArgumentException($"Unknown building type: {type}")
        };

        HealthPoints = MaxHealthPoints;
    }

    /// <summary>
    /// Advance construction progress.
    /// </summary>
    public void AdvanceConstruction(int progressAmount)
    {
        if (IsOperational)
        {
            return; // Already complete
        }

        ConstructionProgress = Math.Min(100, ConstructionProgress + progressAmount);

        if (ConstructionProgress >= 100)
        {
            IsOperational = true;
        }
    }

    /// <summary>
    /// Apply damage to this building.
    /// </summary>
    public void TakeDamage(int damage)
    {
        HealthPoints = Math.Max(0, HealthPoints - damage);
    }

    /// <summary>
    /// Check if building is destroyed.
    /// </summary>
    public bool IsDestroyed() => HealthPoints <= 0;

    /// <summary>
    /// Get construction time in ticks for this building type.
    /// </summary>
    public static int GetConstructionTime(BuildingType type) => type switch
    {
        BuildingType.CommandCenter => 30,
        BuildingType.Barracks => 20,
        BuildingType.ResourceDepot => 15,
        BuildingType.TechLab => 25,
        _ => throw new ArgumentException($"Unknown building type: {type}")
    };

    /// <summary>
    /// Get resource cost for this building type.
    /// </summary>
    public static int GetBuildingCost(BuildingType type) => type switch
    {
        BuildingType.CommandCenter => 0, // Starting building, not built
        BuildingType.Barracks => 200,
        BuildingType.ResourceDepot => 150,
        BuildingType.TechLab => 300,
        _ => throw new ArgumentException($"Unknown building type: {type}")
    };

    /// <summary>
    /// Complete construction immediately (for testing).
    /// </summary>
    public void CompleteConstruction()
    {
        ConstructionProgress = 100;
        IsOperational = true;
    }

    /// <summary>
    /// Add a unit to the production queue.
    /// </summary>
    public void EnqueueProduction(UnitType unitType, int productionTime)
    {
        ProductionQueue.Add(new ProductionOrder(unitType, productionTime));
    }

    /// <summary>
    /// Process production for one tick. Returns true if a unit was completed.
    /// </summary>
    public bool ProcessProduction()
    {
        if (!IsOperational || ProductionQueue.Count == 0)
        {
            return false;
        }

        var currentProduction = ProductionQueue[0];
        currentProduction.DecrementTimer();

        if (currentProduction.IsComplete())
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get and remove the completed production from the queue.
    /// </summary>
    public UnitType? GetCompletedProduction()
    {
        if (ProductionQueue.Count == 0)
        {
            return null;
        }

        var completed = ProductionQueue[0];
        if (completed.IsComplete())
        {
            ProductionQueue.RemoveAt(0);
            return completed.UnitType;
        }

        return null;
    }
}

/// <summary>
/// Represents a unit in the building's production queue.
/// </summary>
public class ProductionOrder
{
    public UnitType UnitType { get; private set; }
    public int TicksRemaining { get; private set; }
    public int TotalTicks { get; private set; }

    public ProductionOrder(UnitType unitType, int totalTicks)
    {
        UnitType = unitType;
        TotalTicks = totalTicks;
        TicksRemaining = totalTicks;
    }

    public void DecrementTimer()
    {
        TicksRemaining = Math.Max(0, TicksRemaining - 1);
    }

    public bool IsComplete() => TicksRemaining == 0;

    public int GetProgressPercentage() =>
        TotalTicks > 0 ? (int)((TotalTicks - TicksRemaining) * 100.0 / TotalTicks) : 0;
}
