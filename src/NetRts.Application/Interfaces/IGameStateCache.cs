using NetRts.Domain.Entities;

namespace NetRts.Application.Interfaces;

/// <summary>
/// Interface for game state caching operations.
/// </summary>
public interface IGameStateCache
{
    Match? GetMatch(Guid matchId);
    void SetMatch(Guid matchId, Match match);

    List<Unit> GetUnitsForMatch(Guid matchId);
    void SetUnitsForMatch(Guid matchId, List<Unit> units);

    List<Building> GetBuildingsForMatch(Guid matchId);
    void SetBuildingsForMatch(Guid matchId, List<Building> buildings);

    List<ResourceDeposit> GetResourceDepositsForMatch(Guid matchId);
    void SetResourceDepositsForMatch(Guid matchId, List<ResourceDeposit> deposits);

    List<MapTile> GetMapTilesForMatch(Guid matchId);
    void SetMapTilesForMatch(Guid matchId, List<MapTile> tiles);
}
