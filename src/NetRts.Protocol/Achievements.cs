using System.Text.Json.Serialization;

namespace NetRts.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter<AchievementTier>))]
public enum AchievementTier
{
    Bronze,
    Silver,
    Gold,
    Platinum,
}

/// <summary>One achievement, as the catalog lists it (GET /api/v1/achievements).</summary>
public sealed record AchievementDto
{
    /// <summary>Stable id, e.g. "first-blood".</summary>
    public string Id { get; init; } = "";

    /// <summary>"???" for a hidden achievement nobody has earned yet.</summary>
    public string Name { get; init; } = "";

    /// <summary>What earns it; "???" for a hidden achievement nobody has earned yet.</summary>
    public string Description { get; init; } = "";

    /// <summary>e.g. "First steps", "Combat".</summary>
    public string Group { get; init; } = "";

    public AchievementTier Tier { get; init; }

    /// <summary>A secret until someone earns it.</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether matches against house bots count toward it. Every other achievement needs a real opponent.</summary>
    public bool HouseBotsCount { get; init; }

    /// <summary>How many bots have earned it.</summary>
    public int EarnedBy { get; init; }

    /// <summary>The first bot to earn it, if any has.</summary>
    public string? FirstEarnedBy { get; init; }

    public DateTimeOffset? FirstEarnedAt { get; init; }

    /// <summary>Who has it, first earners first (at most <c>earners</c> of them; see GET /api/v1/achievements).</summary>
    public IReadOnlyList<AchievementEarnerDto> Earners { get; init; } = [];
}

/// <summary>A bot that has earned an achievement, and its owner when Discord-verified.</summary>
public sealed record AchievementEarnerDto
{
    public Guid PlayerId { get; init; }
    public string Bot { get; init; } = "";
    public string? OwnerName { get; init; }

    /// <summary>The owner's id, for their public profile; null if the bot is unclaimed.</summary>
    public Guid? OwnerId { get; init; }

    public DateTimeOffset EarnedAt { get; init; }
}

/// <summary>An achievement a bot has earned (GET /api/v1/players/{id}/achievements).</summary>
public sealed record EarnedAchievementDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Group { get; init; } = "";
    public AchievementTier Tier { get; init; }
    public DateTimeOffset EarnedAt { get; init; }

    /// <summary>The match that earned it, if one did.</summary>
    public Guid? MatchId { get; init; }

    /// <summary>
    /// The bots that earned it, when listing a person's achievements across all their bots; empty
    /// when listing one bot's.
    /// </summary>
    public IReadOnlyList<string> Bots { get; init; } = [];
}
