namespace NetRts.Api.Authorization;

/// <summary>
/// Authorization policies for the NetRts API.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Policy name for requiring a player to be a participant in a match.
    /// </summary>
    public const string MatchParticipant = "MatchParticipant";

    /// <summary>
    /// Policy name for requiring a player to be a lobby host.
    /// </summary>
    public const string LobbyHost = "LobbyHost";

    /// <summary>
    /// Policy name for requiring an authenticated player.
    /// </summary>
    public const string AuthenticatedPlayer = "AuthenticatedPlayer";
}
