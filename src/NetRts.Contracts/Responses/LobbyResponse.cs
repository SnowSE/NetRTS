namespace NetRts.Contracts.Responses;

public class LobbyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid HostPlayerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MaxPlayers { get; set; }
    public int CurrentPlayerCount { get; set; }
    public List<LobbyPlayerDto> Players { get; set; } = new();
    public GameSettingsDto Settings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class LobbyPlayerDto
{
    public Guid PlayerId { get; set; }
    public int Slot { get; set; }
    public bool IsReady { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class GameSettingsDto
{
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int MaxTicks { get; set; }
    public int StartingResources { get; set; }
}
