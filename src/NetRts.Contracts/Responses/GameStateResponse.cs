namespace NetRts.Contracts.Responses;

/// <summary>
/// Game state response containing all visible game information for a player.
/// </summary>
public class GameStateResponse
{
    public required Guid MatchId { get; init; }
    public required Guid PlayerId { get; init; }
    public required int CurrentTick { get; init; }
    public required string MatchStatus { get; init; }
    public required int PlayerResources { get; init; }
    public required ScoreDto PlayerScore { get; init; }
    public required ScoreDto OpponentScore { get; init; }
    public required IReadOnlyList<UnitDto> Units { get; init; }
    public required IReadOnlyList<BuildingDto> Buildings { get; init; }
    public required IReadOnlyList<ResourceDepositDto> ResourceDeposits { get; init; }
    public required IReadOnlyList<MapTileDto> VisibleTiles { get; init; }
    public required int MapWidth { get; init; }
    public required int MapHeight { get; init; }
    public Guid? WinnerId { get; init; }
}
