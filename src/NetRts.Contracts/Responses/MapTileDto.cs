namespace NetRts.Contracts.Responses;

/// <summary>
/// Map tile data transfer object for game state responses.
/// </summary>
public class MapTileDto
{
    public required PositionDto Position { get; init; }
    public required string TerrainType { get; init; }
    public required bool IsPassable { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsExplored { get; init; }
}
