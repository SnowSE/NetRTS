namespace NetRts.Server;

/// <summary>Server tunables, bound from the "NetRts" configuration section.</summary>
public sealed class NetRtsOptions
{
    public const string Section = "NetRts";

    /// <summary>The site's public address, e.g. https://netrts.snowse.io, for links in Discord announcements.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Azure resource ID of the Application Insights resource the server exports to
    /// (/subscriptions/…/resourceGroups/…/providers/microsoft.insights/components/…). When set, admins
    /// get links to its portal pages and saved log queries on their account page.
    /// </summary>
    public string? AppInsightsResourceId { get; set; }

    /// <summary>Tenant ID or domain the portal links sign in to, so they open in the right directory.</summary>
    public string? AzureTenant { get; set; }

    /// <summary>
    /// The players' time zone (IANA or Windows id): what "night" means for the Night Owl achievement,
    /// and which day the nightly ladder snapshot and recap belong to.
    /// </summary>
    public string TimeZone { get; set; } = "America/Denver";

    /// <summary><see cref="TimeZone"/>, or UTC if it isn't a known zone.</summary>
    public TimeZoneInfo Zone => TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

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

    /// <summary>
    /// Practice matches one bot may have running at once. Each runs until its time limit (30 minutes at
    /// the default speed) whether or not anyone is playing it, so without a cap one bot could fill the server.
    /// </summary>
    public int MaxRunningMatchesPerPlayer { get; set; } = 3;

    /// <summary>
    /// A running practice match whose creator hasn't read its state or sent commands for this long has been
    /// left (the bot crashed or was stopped). Starting a match past the cap above surrenders those first.
    /// </summary>
    public int AbandonedMatchSeconds { get; set; } = 40;
    public int MaxConcurrentExhibitions { get; set; } = 4;

    /// <summary>Waiting matches nobody joins are dropped after this long.</summary>
    public int WaitingMatchTimeoutMinutes { get; set; } = 30;

    /// <summary>Season and championship matches wait this long for their players before they're dropped.</summary>
    public int LeagueMatchTimeoutMinutes { get; set; } = 24 * 60;

    /// <summary>
    /// A league match that was never closed (say the server restarted mid-match) has its points added
    /// to the ladder this long after its last heat finished.
    /// </summary>
    public int LeagueAutoFinalizeMinutes { get; set; } = 60;

    /// <summary>Finished matches stay in memory (for spectators) this long; afterwards they're served from the database.</summary>
    public int CompletedMatchRetentionMinutes { get; set; } = 10;

    /// <summary>Longest a state long-poll (waitForTick) is held open.</summary>
    public int MaxLongPollSeconds { get; set; } = 30;

    /// <summary>API requests allowed per second per player in each match (4x this across all of a player's matches).</summary>
    public int RequestsPerSecond { get; set; } = 30;

    /// <summary>A classroom shares one address, so this leaves room for a whole class signing up at once.</summary>
    public int RegistrationsPerMinutePerIp { get; set; } = 25;

    public int ExhibitionsPerMinutePerIp { get; set; } = 10;
}
