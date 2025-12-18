namespace NetRts.Domain.ValueObjects;

/// <summary>
/// Represents an immutable (X, Y) coordinate pair on the game map.
/// </summary>
public record Position
{
    /// <summary>
    /// Horizontal position (0 to MapWidth - 1).
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Vertical position (0 to MapHeight - 1).
    /// </summary>
    public int Y { get; init; }

    public Position(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Calculate Euclidean distance to another position.
    /// </summary>
    public double DistanceTo(Position other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Check if another position is within a given range.
    /// </summary>
    public bool IsWithinRange(Position other, int range)
    {
        return DistanceTo(other) <= range;
    }

    /// <summary>
    /// Get the 4 orthogonally adjacent positions.
    /// </summary>
    public IEnumerable<Position> GetAdjacentPositions()
    {
        yield return new Position(X, Y - 1); // North
        yield return new Position(X + 1, Y); // East
        yield return new Position(X, Y + 1); // South
        yield return new Position(X - 1, Y); // West
    }

    /// <summary>
    /// Get all positions within a rectangular area.
    /// </summary>
    public static IEnumerable<Position> GetPositionsInArea(Position topLeft, Position bottomRight)
    {
        for (int x = topLeft.X; x <= bottomRight.X; x++)
        {
            for (int y = topLeft.Y; y <= bottomRight.Y; y++)
            {
                yield return new Position(x, y);
            }
        }
    }

    public override string ToString() => $"({X}, {Y})";
}
