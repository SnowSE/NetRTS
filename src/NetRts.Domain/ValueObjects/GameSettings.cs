namespace NetRts.Domain.ValueObjects;

/// <summary>
/// Configurable match parameters set by lobby host.
/// </summary>
public record GameSettings
{
    /// <summary>
    /// Map width in tiles (50-200, default: 100).
    /// </summary>
    public int MapWidth { get; init; }

    /// <summary>
    /// Map height in tiles (50-200, default: 100).
    /// </summary>
    public int MapHeight { get; init; }

    /// <summary>
    /// Match duration limit in ticks (600-3600, default: 1800).
    /// </summary>
    public int MaxTicks { get; init; }

    /// <summary>
    /// Milliseconds per tick (500-2000, default: 1000).
    /// </summary>
    public int TickIntervalMs { get; init; }

    /// <summary>
    /// Max command queue size per player (100-1000, default: 500).
    /// </summary>
    public int CommandQueueSize { get; init; }

    /// <summary>
    /// Commands processed per tick (50-200, default: 100).
    /// </summary>
    public int CommandsPerTick { get; init; }

    /// <summary>
    /// Initial resources for each player (100-1000, default: 500).
    /// </summary>
    public int StartingResources { get; init; }

    public GameSettings(
        int mapWidth = 100,
        int mapHeight = 100,
        int maxTicks = 1800,
        int tickIntervalMs = 1000,
        int commandQueueSize = 500,
        int commandsPerTick = 100,
        int startingResources = 500)
    {
        MapWidth = Constrain(mapWidth, 50, 200);
        MapHeight = Constrain(mapHeight, 50, 200);
        MaxTicks = Constrain(maxTicks, 600, 3600);
        TickIntervalMs = Constrain(tickIntervalMs, 500, 2000);
        CommandQueueSize = Constrain(commandQueueSize, 100, 1000);
        CommandsPerTick = Constrain(commandsPerTick, 50, 200);
        StartingResources = Constrain(startingResources, 100, 1000);
    }

    private static int Constrain(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Default game settings for standard matches.
    /// </summary>
    public static GameSettings Default => new();

    /// <summary>
    /// Quick match settings (smaller map, shorter duration).
    /// </summary>
    public static GameSettings Quick => new(
        mapWidth: 50,
        mapHeight: 50,
        maxTicks: 600);

    /// <summary>
    /// Epic match settings (larger map, longer duration).
    /// </summary>
    public static GameSettings Epic => new(
        mapWidth: 150,
        mapHeight: 150,
        maxTicks: 3000);
}
