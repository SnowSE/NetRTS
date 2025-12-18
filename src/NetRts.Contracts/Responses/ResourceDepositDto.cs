namespace NetRts.Contracts.Responses;

/// <summary>
/// Resource deposit data transfer object for game state responses.
/// </summary>
public class ResourceDepositDto
{
    public required int ResourceId { get; init; }
    public required PositionDto Position { get; init; }
    public required int RemainingCapacity { get; init; }
    public required bool IsDepleted { get; init; }
}
