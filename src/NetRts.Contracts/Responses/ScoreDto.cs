namespace NetRts.Contracts.Responses;

/// <summary>
/// Score data transfer object for game state responses.
/// </summary>
public class ScoreDto
{
    public required int UnitsDestroyed { get; init; }
    public required int BuildingsDestroyed { get; init; }
    public required int ResourcesCollected { get; init; }
    public required int TotalScore { get; init; }
}
