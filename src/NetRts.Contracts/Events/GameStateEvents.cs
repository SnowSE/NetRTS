using NetRts.Contracts.Responses;

namespace NetRts.Contracts.Events;

/// <summary>
/// Event broadcast when game state is updated after a tick.
/// </summary>
public class GameStateUpdatedEvent
{
    public Guid MatchId { get; init; }
    public int CurrentTick { get; init; }
    public GameStateResponse GameState { get; init; } = null!;
}

/// <summary>
/// Event broadcast when a match ends.
/// </summary>
public class MatchEndedEvent
{
    public Guid MatchId { get; init; }
    public Guid? WinnerId { get; init; }
    public MatchResultResponse? Result { get; init; }
}

/// <summary>
/// Event broadcast when a player joins a lobby.
/// </summary>
public class LobbyPlayerJoinedEvent
{
    public Guid LobbyId { get; init; }
    public LobbyPlayerDto Player { get; init; } = new();
}

/// <summary>
/// Event broadcast when a player leaves a lobby.
/// </summary>
public class LobbyPlayerLeftEvent
{
    public Guid LobbyId { get; init; }
    public Guid PlayerId { get; init; }
}
