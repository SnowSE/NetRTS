namespace NetRts.Contracts.Responses;

/// <summary>
/// Paginated list of game lobbies.
/// </summary>
public class LobbyListResponse
{
    /// <summary>
    /// List of lobbies in the current page.
    /// </summary>
    public List<LobbyResponse> Lobbies { get; init; } = new();

    /// <summary>
    /// Total number of matching lobbies.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; init; }
}
