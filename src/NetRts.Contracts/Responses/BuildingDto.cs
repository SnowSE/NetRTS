namespace NetRts.Contracts.Responses;

/// <summary>
/// Building data transfer object for game state responses.
/// </summary>
public class BuildingDto
{
    public required int BuildingId { get; init; }
    public required Guid PlayerId { get; init; }
    public required string BuildingType { get; init; }
    public required PositionDto Position { get; init; }
    public required int Health { get; init; }
    public required int MaxHealth { get; init; }
    public required bool IsConstructed { get; init; }
    public required int ConstructionProgress { get; init; }
    public bool IsTraining { get; init; }
    public string? TrainingUnitType { get; init; }
    public int? TrainingProgress { get; init; }
    public int? TrainingTimeRemaining { get; init; }
}
