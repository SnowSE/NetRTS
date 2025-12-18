namespace NetRts.Domain.Enums;

/// <summary>
/// Represents the status of a match lobby.
/// </summary>
public enum LobbyStatus
{
    /// <summary>
    /// Lobby is open and accepting players.
    /// </summary>
    Open,

    /// <summary>
    /// Lobby has reached maximum player capacity.
    /// </summary>
    Full,

    /// <summary>
    /// Lobby host is starting the match.
    /// </summary>
    Starting,

    /// <summary>
    /// Lobby is closed (match started or abandoned).
    /// </summary>
    Closed
}
