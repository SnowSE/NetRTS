namespace NetRts.Domain.ValueObjects;

/// <summary>
/// Represents a breakdown of match scoring components.
/// </summary>
public record Score
{
    /// <summary>
    /// Points from destroying enemy units (10 points each).
    /// </summary>
    public int UnitsDestroyed { get; init; }

    /// <summary>
    /// Points from destroying enemy buildings (50 points each).
    /// </summary>
    public int BuildingsDestroyed { get; init; }

    /// <summary>
    /// Points from gathered resources (1 point per 10 resources).
    /// </summary>
    public int ResourcesGathered { get; init; }

    /// <summary>
    /// Points for units alive at match end (5 points each).
    /// </summary>
    public int UnitsRemaining { get; init; }

    /// <summary>
    /// Points for buildings standing at match end (25 points each).
    /// </summary>
    public int BuildingsRemaining { get; init; }

    /// <summary>
    /// Sum of all scoring components.
    /// </summary>
    public int TotalScore => CalculateTotal();

    public Score(
        int unitsDestroyed = 0,
        int buildingsDestroyed = 0,
        int resourcesGathered = 0,
        int unitsRemaining = 0,
        int buildingsRemaining = 0)
    {
        UnitsDestroyed = unitsDestroyed;
        BuildingsDestroyed = buildingsDestroyed;
        ResourcesGathered = resourcesGathered;
        UnitsRemaining = unitsRemaining;
        BuildingsRemaining = buildingsRemaining;
    }

    /// <summary>
    /// Calculate total score from all components.
    /// </summary>
    private int CalculateTotal()
    {
        return (UnitsDestroyed * 10)
             + (BuildingsDestroyed * 50)
             + (ResourcesGathered / 10)
             + (UnitsRemaining * 5)
             + (BuildingsRemaining * 25);
    }

    /// <summary>
    /// Create a new Score with updated values.
    /// </summary>
    public Score WithUnitsDestroyed(int count) =>
        this with { UnitsDestroyed = UnitsDestroyed + count };

    public Score WithBuildingsDestroyed(int count) =>
        this with { BuildingsDestroyed = BuildingsDestroyed + count };

    public Score WithResourcesGathered(int amount) =>
        this with { ResourcesGathered = ResourcesGathered + amount };

    public Score WithUnitsRemaining(int count) =>
        this with { UnitsRemaining = count };

    public Score WithBuildingsRemaining(int count) =>
        this with { BuildingsRemaining = count };

    public static Score Empty => new();
}
