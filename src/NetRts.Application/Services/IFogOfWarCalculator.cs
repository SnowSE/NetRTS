using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Application.Services;

/// <summary>
/// Service for calculating fog of war visibility.
/// </summary>
public interface IFogOfWarCalculator
{
    /// <summary>
    /// Get all positions visible to a player.
    /// </summary>
    HashSet<Position> CalculateVisiblePositions(
        Guid playerId,
        IEnumerable<Unit> units,
        IEnumerable<Building> buildings);

    /// <summary>
    /// Filter entities based on fog of war for a player.
    /// </summary>
    IEnumerable<Unit> FilterVisibleUnits(
        Guid playerId,
        IEnumerable<Unit> allUnits,
        HashSet<Position> visiblePositions);

    IEnumerable<Building> FilterVisibleBuildings(
        Guid playerId,
        IEnumerable<Building> allBuildings,
        HashSet<Position> visiblePositions);
}
