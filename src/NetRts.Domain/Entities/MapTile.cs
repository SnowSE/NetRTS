using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a single tile on the game grid.
/// </summary>
public class MapTile
{
    public Guid MatchId { get; private set; }
    public Position Position { get; private set; } = new(0, 0);
    public TerrainType TerrainType { get; private set; }
    public int? OccupiedByUnitId { get; private set; }
    public int? OccupiedByBuildingId { get; private set; }
    public bool VisibleToPlayer1 { get; private set; }
    public bool VisibleToPlayer2 { get; private set; }

    private MapTile() { }

    public MapTile(Guid matchId, Position position, TerrainType terrainType = TerrainType.Passable)
    {
        MatchId = matchId;
        Position = position;
        TerrainType = terrainType;
    }

    public void SetOccupiedByUnit(int? unitId) => OccupiedByUnitId = unitId;
    public void SetOccupiedByBuilding(int? buildingId) => OccupiedByBuildingId = buildingId;
    public void SetVisibility(bool player1, bool player2)
    {
        VisibleToPlayer1 = player1;
        VisibleToPlayer2 = player2;
    }
}
