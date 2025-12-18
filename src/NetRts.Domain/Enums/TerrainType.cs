namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the type of terrain on a map tile.
/// </summary>
public enum TerrainType
{
    /// <summary>
    /// Terrain that units can move through.
    /// </summary>
    Passable,

    /// <summary>
    /// Terrain that blocks unit movement.
    /// </summary>
    Impassable
}
