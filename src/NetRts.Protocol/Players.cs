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

    /// <summary>Practice rating. Season and championship ratings are in <see cref="Records"/>.</summary>
    public int Rating { get; init; }

    /// <summary>Practice record. Season and championship records are in <see cref="Records"/>.</summary>
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>True when a Discord-verified person has claimed this bot (the blue check).</summary>
    public bool Verified { get; init; }

    /// <summary>The verified owner's name: their Discord server nickname, or Discord name. Null if unclaimed.</summary>
    public string? OwnerName { get; init; }

    /// <summary>The verified owner's id, for their public profile (GET /api/v1/people/{id}). Null if unclaimed.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>Rating and record in each category (Practice, Season, Championship).</summary>
    public IReadOnlyList<CategoryRecordDto> Records { get; init; } = [];
}

/// <summary>A player's rating and record on one leaderboard.</summary>
public sealed record CategoryRecordDto
{
    public MatchCategory Category { get; init; }
    public int Rating { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }

    /// <summary>League points (see <see cref="LeaderboardEntryDto.Points"/>).</summary>
    public int Points { get; init; }
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

    /// <summary>True when a Discord-verified person has claimed this bot (the blue check).</summary>
    public bool Verified { get; init; }

    /// <summary>The verified owner's name: their Discord server nickname, or Discord name. Null if unclaimed.</summary>
    public string? OwnerName { get; init; }

    /// <summary>The verified owner's id, for their public profile (GET /api/v1/people/{id}). Null if unclaimed.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>
    /// League points. In each match the top four (winner first, then by score) earn their score margins
    /// over everyone below them in that four: 1st gets (s1-s2)+(s1-s3)+(s1-s4), 2nd (s2-s3)+(s2-s4),
    /// 3rd s3-s4. Season and championship ladders are ranked by points.
    /// </summary>
    public int Points { get; init; }
}

public sealed record HouseBotDto(string Name, string Description);

/// <summary>RFC 7807-style error body returned by every non-2xx API response.</summary>
public sealed record ApiErrorDto
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}
