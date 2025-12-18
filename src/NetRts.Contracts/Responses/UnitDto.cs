namespace NetRts.Contracts.Responses;

/// <summary>
/// Unit data transfer object for game state responses.
/// </summary>
public class UnitDto
{
    public required int UnitId { get; init; }
    public required Guid PlayerId { get; init; }
    public required string UnitType { get; init; }
    public required PositionDto Position { get; init; }
    public required int Health { get; init; }
    public required int MaxHealth { get; init; }
    public required string CurrentState { get; init; }
    public int? TargetUnitId { get; init; }
    public PositionDto? TargetPosition { get; init; }
    public int? TargetResourceId { get; init; }
    public int CarriedResources { get; init; }
}
