namespace NetRts.Protocol;

/// <summary>One heat of a season, championship or preseason match: a separate game with some of its players.</summary>
public sealed record HeatDto
{
    /// <summary>The heat's own id: open it to watch, or for its result and replay.</summary>
    public Guid MatchId { get; init; }

    /// <summary>e.g. "season-week3-h2".</summary>
    public string Name { get; init; } = "";

    public MatchStatus Status { get; init; }
    public IReadOnlyList<Guid> PlayerIds { get; init; } = [];
}

/// <summary>
/// Create a league match (admins, on the website). Verified bots join it like any match; the admin
/// then splits everyone into heats and starts them all at once.
/// </summary>
public sealed record CreateLeagueMatchRequest
{
    /// <summary>Preseason, Season or Championship.</summary>
    public MatchCategory Category { get; init; } = MatchCategory.Season;

    /// <summary>3-28 letters, digits, '_' or '-'. Its heats are named {name}-h1, {name}-h2, ...</summary>
    public string Name { get; init; } = "";

    /// <summary>Milliseconds per tick in every heat.</summary>
    public int? TickIntervalMs { get; init; }

    /// <summary>Tick limit in every heat.</summary>
    public int? MaxTicks { get; init; }
}

/// <summary>How many players go in each heat; the sizes must add up to everyone who joined.</summary>
public sealed record SplitHeatsRequest
{
    public IReadOnlyList<int> Sizes { get; init; } = [];
}

/// <summary>Live (or final) standings of one heat, for scoreboards.</summary>
public sealed record HeatStandingsDto
{
    public Guid MatchId { get; init; }
    public string Name { get; init; } = "";
    public MatchStatus Status { get; init; }
    public int Tick { get; init; }
    public int MaxTicks { get; init; }
    public MatchOutcomeDto? Outcome { get; init; }

    /// <summary>Best first: the winner once there is one, then by score.</summary>
    public IReadOnlyList<StandingDto> Players { get; init; } = [];
}

public sealed record StandingDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public bool Verified { get; init; }
    public string? OwnerName { get; init; }
    public int Score { get; init; }
    public bool Eliminated { get; init; }
    public bool Winner { get; init; }

    /// <summary>1 = best; equal scores share a place.</summary>
    public int Place { get; init; }

    /// <summary>League points for this heat: final once it has ended, otherwise what they'd get if it ended now.</summary>
    public int Points { get; init; }
}
