namespace NetRts.Server.Recaps;

/// <summary>The nightly recap show, bound from the "Recap" configuration section.</summary>
public sealed class RecapOptions
{
    public const string Section = "Recap";

    /// <summary>
    /// The key the recap job sends (header X-Recap-Key) to read the night's brief and publish episodes.
    /// Unset: those endpoints are off. Published episodes are listed either way.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Blob container the job uploads videos to (the "recaps" blob connection supplies the account).</summary>
    public string Container { get; set; } = "recaps";

    /// <summary>"snow" (Badger Brawl) or "classic" (NetRTS): which names the show uses.</summary>
    public string Brand { get; set; } = "snow";

    /// <summary>When league matches are played, for the "coming up" segment.</summary>
    public string LeagueSchedule { get; set; } = "League matches are played at 5 p.m. in GRSC 124.";

    /// <summary>Days league matches are played (e.g. "Tuesday"); empty when it varies. Lets the show say which day is next.</summary>
    public List<DayOfWeek> LeagueDays { get; set; } = [];

    /// <summary>The mentoring reminder the hosts work in every episode.</summary>
    public string Mentoring { get; set; } =
        "The TAs offer mentoring in the Commons. The schedule is posted there; stop by with your bot.";

    /// <summary>Local time (hour) each night when the ladders are snapshotted for the day just ended.</summary>
    public int SnapshotHour { get; set; } = 0;

    /// <summary>A day with fewer real-bot matches than this gets a short episode.</summary>
    public int QuietNightMatches { get; set; } = 5;
}
