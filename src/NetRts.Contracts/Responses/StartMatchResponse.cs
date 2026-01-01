namespace NetRts.Contracts.Responses;

public class StartMatchResponse
{
    public Guid MatchId { get; set; }
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
}
