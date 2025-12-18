using NetRts.Application.Services;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Services;

public class FogOfWarCalculator : IFogOfWarCalculator
{
    public HashSet<Position> CalculateVisiblePositions(
        Guid playerId,
        IEnumerable<Unit> units,
        IEnumerable<Building> buildings)
    {
        var visiblePositions = new HashSet<Position>();

        // Add positions visible from player's units
        foreach (var unit in units.Where(u => u.OwnerId == playerId))
        {
            AddVisiblePositionsInRadius(visiblePositions, unit.Position, unit.VisionRange);
        }

        // Add positions visible from player's buildings
        foreach (var building in buildings.Where(b => b.OwnerId == playerId && b.IsOperational))
        {
            AddVisiblePositionsInRadius(visiblePositions, building.Position, building.VisionRange);
        }

        return visiblePositions;
    }

    public IEnumerable<Unit> FilterVisibleUnits(
        Guid playerId,
        IEnumerable<Unit> allUnits,
        HashSet<Position> visiblePositions)
    {
        return allUnits.Where(u =>
            u.OwnerId == playerId || // Player can always see their own units
            visiblePositions.Contains(u.Position)); // Or enemy units in visible positions
    }

    public IEnumerable<Building> FilterVisibleBuildings(
        Guid playerId,
        IEnumerable<Building> allBuildings,
        HashSet<Position> visiblePositions)
    {
        return allBuildings.Where(b =>
            b.OwnerId == playerId || // Player can always see their own buildings
            visiblePositions.Contains(b.Position)); // Or enemy buildings in visible positions
    }

    private void AddVisiblePositionsInRadius(HashSet<Position> positions, Position center, int radius)
    {
        // Use circular vision (Euclidean distance)
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                var pos = new Position(x, y);
                if (center.IsWithinRange(pos, radius))
                {
                    positions.Add(pos);
                }
            }
        }
    }
}
