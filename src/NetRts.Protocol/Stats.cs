namespace NetRts.Protocol;

/// <summary>
/// Every finished match in a compact form, for the Stats page: the bots once in <see cref="Bots"/>, and
/// each match naming its players by their place in that list.
/// </summary>
public sealed record MatchStatsDto
{
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<StatsBotDto> Bots { get; init; } = [];

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<StatsMatchDto> Matches { get; init; } = [];
}

/// <summary>A bot as the Stats page shows it: its latest name and who plays it.</summary>
public sealed record StatsBotDto
{
    public string Name { get; init; } = "";

    /// <summary>"house", "block" (Block Bot) or "code" (a player's own program).</summary>
    public string Kind { get; init; } = "";
}

public sealed record StatsMatchDto
{
    public Guid Id { get; init; }
    public DateTimeOffset At { get; init; }
    public string Reason { get; init; } = "";
    public int Ticks { get; init; }
    public int TickMs { get; init; }

    /// <summary>The winner's place in <see cref="Players"/>; -1 for a draw.</summary>
    public int Winner { get; init; }

    /// <summary>Places in <see cref="MatchStatsDto.Bots"/>, by slot.</summary>
    public IReadOnlyList<int> Players { get; init; } = [];

    public MatchCategory Category { get; init; }
}

/// <summary>
/// How the server held up, from its telemetry (Application Insights), rebuilt each morning through the end
/// of yesterday. Covers what the telemetry still keeps: <see cref="From"/> to <see cref="To"/>.
/// </summary>
public sealed record ServerStatsDto
{
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Every HTTP request the server answered, and the count for each status code.</summary>
    public long Requests { get; init; }
    public IReadOnlyList<StatsCountDto> StatusCodes { get; init; } = [];

    /// <summary>Bots' moves (POST commands) and board reads (GET state).</summary>
    public long Commands { get; init; }
    public long StatePolls { get; init; }

    /// <summary>Response times of traced requests (bots' long-poll waits aren't traced).</summary>
    public double MedianMs { get; init; }
    public double P99Ms { get; init; }

    public DateTimeOffset? PeakMinute { get; init; }
    public long PeakMinuteRequests { get; init; }

    public long Ticks { get; init; }
    public double TickAverageMs { get; init; }
    public long TickOverruns { get; init; }
    public int MostLiveMatches { get; init; }

    public int Exceptions { get; init; }
    public int DatabaseExceptions { get; init; }
    public int Restarts { get; init; }

    public long DatabaseQueries { get; init; }
    public double DatabaseAverageMs { get; init; }
    public double MemoryAverageMb { get; init; }
    public double MemoryPeakMb { get; init; }

    /// <summary>Uptime checks by local day, and how many places they ran from.</summary>
    public IReadOnlyList<StatsHealthDayDto> Health { get; init; } = [];
    public int HealthLocations { get; init; }

    /// <summary>Traced requests in each local hour of each day.</summary>
    public IReadOnlyList<StatsHourDto> Hours { get; init; } = [];
}

public sealed record StatsCountDto(string Name, long Count);

public sealed record StatsHealthDayDto(string Day, int Checks, int Passed);

public sealed record StatsHourDto(string Day, int Hour, long Requests);
