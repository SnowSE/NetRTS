namespace NetRts.Server;

/// <summary>Server tunables, bound from the "NetRts" configuration section.</summary>
public sealed class NetRtsOptions
{
    public const string Section = "NetRts";

    public int DefaultTickIntervalMs { get; set; } = 1000;
    public int MinTickIntervalMs { get; set; } = 100;
    public int MaxTickIntervalMs { get; set; } = 10_000;
    public int DefaultMaxTicks { get; set; } = 1800;
    public int DefaultMapSize { get; set; } = 64;

    /// <summary>Pause between a match filling up and its first tick, so every bot can read the opening state.</summary>
    public int StartDelayMs { get; set; } = 3000;

    /// <summary>When false, matches only advance when <c>MatchHost.Advance</c> is called (tests).</summary>
    public bool AutoTick { get; set; } = true;

    public int MaxLiveMatches { get; set; } = 50;
    public int MaxWaitingMatchesPerPlayer { get; set; } = 3;
    public int MaxConcurrentExhibitions { get; set; } = 4;

    /// <summary>Waiting matches nobody joins are dropped after this long.</summary>
    public int WaitingMatchTimeoutMinutes { get; set; } = 30;

    /// <summary>Finished matches stay in memory (for spectators) this long; afterwards they're served from the database.</summary>
    public int CompletedMatchRetentionMinutes { get; set; } = 10;

    /// <summary>Longest a state long-poll (waitForTick) is held open.</summary>
    public int MaxLongPollSeconds { get; set; } = 30;

    /// <summary>API requests allowed per second per player in each match (4x this across all of a player's matches).</summary>
    public int RequestsPerSecond { get; set; } = 30;

    public int RegistrationsPerMinutePerIp { get; set; } = 5;

    public int ExhibitionsPerMinutePerIp { get; set; } = 10;
}
