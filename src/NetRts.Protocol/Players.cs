namespace NetRts.Protocol;

public sealed record RegisterPlayerRequest
{
    public string Name { get; init; } = "";
}

public sealed record RegisterPlayerResponse
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Send as "Authorization: Bearer {apiKey}". Shown only once — store it.</summary>
    public string ApiKey { get; init; } = "";
}

public sealed record PlayerProfileDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public bool IsHouseBot { get; init; }
    public int Rating { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record LeaderboardEntryDto
{
    public int Rank { get; init; }
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public bool IsHouseBot { get; init; }
    public int Rating { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }
}

public sealed record HouseBotDto(string Name, string Description);

/// <summary>RFC 7807-style error body returned by every non-2xx API response.</summary>
public sealed record ApiErrorDto
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}
