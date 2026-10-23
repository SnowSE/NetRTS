using NetRts.Protocol;

namespace NetRts.Server.Recaps;

// The night's facts, as the recap job reads them (GET /api/v1/recaps/brief). Everything a host might
// mention is spelled out here, so the script writer never has to invent a number.

public sealed record RecapBriefDto
{
    /// <summary>The day covered (local), e.g. "2026-10-21".</summary>
    public string Date { get; init; } = "";

    public string Weekday { get; init; } = "";

    /// <summary>Few matches: make a short episode.</summary>
    public bool Quiet { get; init; }

    public required RecapLoreDto Lore { get; init; }

    /// <summary>Game changes that shipped this day: the hosts announce them near the top of the show.</summary>
    public IReadOnlyList<RecapNewsDto> News { get; init; } = [];

    public required RecapTotalsDto Totals { get; init; }
    public IReadOnlyList<RecapBotDto> NewBots { get; init; } = [];
    public IReadOnlyList<RecapLadderDto> Ladders { get; init; } = [];
    public IReadOnlyList<RecapMoverDto> Movers { get; init; } = [];
    public IReadOnlyList<RecapAchievementDto> Achievements { get; init; } = [];
    public IReadOnlyList<RecapStrategyDto> Strategies { get; init; } = [];
    public RecapMatchDto? MatchOfTheNight { get; init; }
    public IReadOnlyList<RecapNoteDto> PowerPlays { get; init; } = [];
    public IReadOnlyList<RecapNoteDto> Surprises { get; init; } = [];
    public IReadOnlyList<RecapRivalryDto> Rivalries { get; init; } = [];
    public IReadOnlyList<RecapMissingDto> MissingPersons { get; init; } = [];
    public required RecapUpcomingDto Upcoming { get; init; }

    /// <summary>The mentoring reminder to work in.</summary>
    public string Mentoring { get; init; } = "";

    /// <summary>Recent episodes' titles, newest first, so the show doesn't repeat itself.</summary>
    public IReadOnlyList<string> RecentEpisodes { get; init; } = [];

    /// <summary>Rules facts for a "tip of the night".</summary>
    public IReadOnlyList<string> RulesNotes { get; init; } = [];
}

/// <summary>What things are called on this server.</summary>
public sealed record RecapLoreDto
{
    public string Game { get; init; } = "";

    /// <summary>"ore", or "grubs" in the Snow College theme.</summary>
    public string Ore { get; init; } = "";

    /// <summary>Extra flavor the hosts may use (house bot nicknames, building and upgrade names).</summary>
    public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();

    public string Setting { get; init; } = "";
}

public sealed record RecapTotalsDto
{
    public int Matches { get; init; }

    /// <summary>Matches with at least two real (non-house) bots from different people.</summary>
    public int RealMatches { get; init; }

    public int LeagueMatches { get; init; }
    public int BotsActive { get; init; }
    public long OreGathered { get; init; }
    public long UnitsKilled { get; init; }
    public int LongestMatchTicks { get; init; }
}

public sealed record RecapBotDto
{
    public string Name { get; init; } = "";
    public string? Owner { get; init; }
    public bool Registered { get; init; }
    public int Matches { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }
}

public sealed record RecapLadderDto
{
    public MatchCategory Category { get; init; }

    /// <summary>"rating" for practice, "points" for the league ladders.</summary>
    public string RankedBy { get; init; } = "";

    public IReadOnlyList<RecapLadderEntryDto> Entries { get; init; } = [];
}

public sealed record RecapLadderEntryDto
{
    public int Rank { get; init; }

    /// <summary>Rank the night before; null if they weren't on it.</summary>
    public int? PreviousRank { get; init; }

    public string Name { get; init; } = "";
    public string? Owner { get; init; }
    public bool IsHouseBot { get; init; }
    public int Rating { get; init; }
    public int Points { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
}

public sealed record RecapMoverDto
{
    public string Name { get; init; } = "";
    public string? Owner { get; init; }
    public MatchCategory Category { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
}

public sealed record RecapAchievementDto
{
    public string Bot { get; init; } = "";
    public string? Owner { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public AchievementTier Tier { get; init; }
    public bool Hidden { get; init; }

    /// <summary>Nobody had earned it before.</summary>
    public bool FirstEver { get; init; }

    /// <summary>How many bots have it now.</summary>
    public int EarnedBy { get; init; }
}

public sealed record RecapStrategyDto
{
    public string Bot { get; init; } = "";
    public string? Owner { get; init; }
    public string Strategy { get; init; } = "";
    public int Matches { get; init; }
    public int Wins { get; init; }

    /// <summary>The bot hadn't played this way before (it had played before).</summary>
    public bool NewForBot { get; init; }

    /// <summary>Nobody had played this way before.</summary>
    public bool NewOnServer { get; init; }

    /// <summary>How the bot usually played before, if that was different.</summary>
    public string? UsedToPlay { get; init; }
}

public sealed record RecapMatchDto
{
    public Guid MatchId { get; init; }
    public MatchCategory Category { get; init; }
    public MatchEndReason Reason { get; init; }
    public int Ticks { get; init; }
    public int MaxTicks { get; init; }

    /// <summary>Why it was picked, e.g. "comeback from 31% of the leader's score".</summary>
    public IReadOnlyList<string> Why { get; init; } = [];

    public IReadOnlyList<RecapMatchPlayerDto> Players { get; init; } = [];
}

public sealed record RecapMatchPlayerDto
{
    public string Name { get; init; } = "";
    public string? Owner { get; init; }
    public int Slot { get; init; }
    public bool Winner { get; init; }
    public int Score { get; init; }
    public int UnitsKilled { get; init; }
    public int UnitsLost { get; init; }
    public int? RatingBefore { get; init; }
    public string? Strategy { get; init; }
}

/// <summary>Something worth a line on the show, with the match it happened in.</summary>
public sealed record RecapNoteDto
{
    public string Bot { get; init; } = "";
    public string? Owner { get; init; }
    public string Text { get; init; } = "";
    public Guid? MatchId { get; init; }
}

public sealed record RecapRivalryDto
{
    public string A { get; init; } = "";
    public string B { get; init; } = "";
    public int AWins { get; init; }
    public int BWins { get; init; }
    public int Draws { get; init; }
}

public sealed record RecapMissingDto
{
    public string Name { get; init; } = "";
    public string? Owner { get; init; }
    public int DaysAway { get; init; }
    public int Matches { get; init; }
}

public sealed record RecapNewsDto
{
    public string Headline { get; init; } = "";

    /// <summary>What changed and how bots use it, API names included.</summary>
    public string Details { get; init; } = "";

    /// <summary>A few short facts for the big screen.</summary>
    public IReadOnlyList<string> Facts { get; init; } = [];
}

public sealed record RecapUpcomingDto
{
    /// <summary>When league matches are played, in words.</summary>
    public string Schedule { get; init; } = "";

    /// <summary>The next league day, if the days are configured, e.g. "Tuesday, October 27".</summary>
    public string? NextLeagueDay { get; init; }

    /// <summary>League matches created and taking players now.</summary>
    public IReadOnlyList<RecapLeagueMatchDto> LeagueMatches { get; init; } = [];
}

public sealed record RecapLeagueMatchDto
{
    public string Name { get; init; } = "";
    public MatchCategory Category { get; init; }
    public int Joined { get; init; }
    public bool Started { get; init; }
}

/// <summary>One line of an episode's script.</summary>
public sealed record RecapLineDto
{
    /// <summary>The host's key, e.g. "zip".</summary>
    public string Speaker { get; init; } = "";

    public string Text { get; init; } = "";

    /// <summary>Seconds into the video.</summary>
    public double Start { get; init; }
}

/// <summary>A published episode, as the website lists it.</summary>
public sealed record RecapEpisodeDto
{
    public Guid Id { get; init; }
    public string Date { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";

    /// <summary>Where to play it (redirects to the video).</summary>
    public string VideoUrl { get; init; } = "";

    /// <summary>Its poster frame, or null.</summary>
    public string? ThumbnailUrl { get; init; }

    public int DurationSeconds { get; init; }

    /// <summary>The script; only when one episode is asked for.</summary>
    public IReadOnlyList<RecapLineDto>? Transcript { get; init; }
}

/// <summary>What the recap job publishes once it has uploaded the video (POST /api/v1/recaps).</summary>
public sealed record PublishRecapRequest
{
    public string Date { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";

    /// <summary>The video's blob name in the recaps container, or an absolute URL.</summary>
    public string Video { get; init; } = "";

    public string? Thumbnail { get; init; }
    public int DurationSeconds { get; init; }
    public IReadOnlyList<RecapLineDto> Transcript { get; init; } = [];
}
