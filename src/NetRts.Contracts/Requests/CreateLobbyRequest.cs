namespace NetRts.Contracts.Requests;

public class CreateLobbyRequest
{
    public string Name { get; set; } = string.Empty;
    public int? MapWidth { get; set; }
    public int? MapHeight { get; set; }
    public int? MaxTicks { get; set; }
    public int? StartingResources { get; set; }
}
