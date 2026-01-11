using NetRts.Contracts.Responses;

namespace NetRts.Contracts.Requests;

/// <summary>
/// Request to create a new game lobby.
/// </summary>
public class CreateLobbyRequest
{
    /// <summary>
    /// User-defined name for the lobby.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Game settings for the match (optional, defaults will be used if null).
    /// </summary>
    public GameSettingsDto? Settings { get; init; }
}