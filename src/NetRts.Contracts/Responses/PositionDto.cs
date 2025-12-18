namespace NetRts.Contracts.Responses;

/// <summary>
/// Position data transfer object.
/// </summary>
public class PositionDto
{
    public required int X { get; init; }
    public required int Y { get; init; }
}
