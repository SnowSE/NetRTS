namespace NetRts.Protocol;

public sealed record MatchSettingsDto
{
    public int? MapWidth { get; init; }
    public int? MapHeight { get; init; }
    public int? Seed { get; init; }
    public int? TickIntervalMs { get; init; }
    public int? MaxTicks { get; init; }
}

public sealed record CreateMatchRequest
{
    /// <summary>
    /// 2-16 players. Default 2. Up to four start in the corners; five or more start around a ring, which
    /// needs a bigger map (the default map size grows to fit, e.g. 120×120 for 16 players).
    /// </summary>
    public int? MaxPlayers { get; init; }

    /// <summary>Fill seats with house bots (see GET /api/v1/bots), e.g. ["balanced"]. Repeat a name for several copies.</summary>
    public IReadOnlyList<string>? HouseBots { get; init; }

    public MatchSettingsDto? Settings { get; init; }
}

public sealed record CreateExhibitionRequest
{
    /// <summary>2-16 house bot names to pit against each other, e.g. ["rusher", "economist"]. Repeat a name for several copies.</summary>
    public IReadOnlyList<string> Bots { get; init; } = [];

    public MatchSettingsDto? Settings { get; init; }
}

public sealed record MatchSummaryDto
{
    public Guid MatchId { get; init; }
    public MatchStatus Status { get; init; }
    public int MaxPlayers { get; init; }
    public int Tick { get; init; }
    public int MaxTicks { get; init; }
    public int TickIntervalMs { get; init; }
    public int MapWidth { get; init; }
    public int MapHeight { get; init; }
    public int Seed { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<MatchPlayerDto> Players { get; init; } = [];
    public MatchOutcomeDto? Outcome { get; init; }
}

public sealed record MatchPlayerDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public bool IsHouseBot { get; init; }
}

/// <summary>The static map: terrain is public knowledge, entities are not.</summary>
public sealed record MapDto
{
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>One string per row (y): '.' = open ground, '#' = rock (impassable).</summary>
    public IReadOnlyList<string> Terrain { get; init; } = [];

    /// <summary>Command Center location of each slot.</summary>
    public IReadOnlyList<PositionDto> StartPositions { get; init; } = [];
}

public sealed record MatchResultDto
{
    public Guid MatchId { get; init; }
    public required MatchOutcomeDto Outcome { get; init; }
    public IReadOnlyList<PlayerResultDto> Players { get; init; } = [];
}

public sealed record PlayerResultDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public bool Winner { get; init; }
    public required ScoreDto Score { get; init; }
    public int UnitsProduced { get; init; }
    public int UnitsLost { get; init; }
    public int UnitsKilled { get; init; }
    public int BuildingsLost { get; init; }
    public int BuildingsDestroyed { get; init; }
}

/// <summary>Omniscient view for spectators: every entity, every player's economy.</summary>
public sealed record SpectatorStateDto
{
    public Guid MatchId { get; init; }
    public int Tick { get; init; }
    public MatchStatus Status { get; init; }
    public int MaxTicks { get; init; }
    public int TickIntervalMs { get; init; }
    public IReadOnlyList<SpectatorPlayerDto> Players { get; init; } = [];
    public IReadOnlyList<UnitDto> Units { get; init; } = [];
    public IReadOnlyList<BuildingDto> Buildings { get; init; } = [];
    public IReadOnlyList<ResourceDepositDto> Resources { get; init; } = [];
    public IReadOnlyList<GameEventDto> Events { get; init; } = [];
    public MatchOutcomeDto? Outcome { get; init; }
}

public sealed record SpectatorPlayerDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public bool Eliminated { get; init; }
    public int Resources { get; init; }
    public int UnitCount { get; init; }
    public int BuildingCount { get; init; }
    public IReadOnlyList<UpgradeType> Upgrades { get; init; } = [];

    /// <summary>Ore banked over the last 60 ticks (one minute at normal speed).</summary>
    public int IncomePerMinute { get; init; }

    public required ScoreDto Score { get; init; }

    /// <summary>Visibility rows for this player (same format as GameStateDto.Visibility).</summary>
    public IReadOnlyList<string> Visibility { get; init; } = [];
}

/// <summary>Everything needed to deterministically re-simulate a match.</summary>
public sealed record ReplayDto
{
    public Guid MatchId { get; init; }
    public int MapWidth { get; init; }
    public int MapHeight { get; init; }
    public int Seed { get; init; }
    public int MaxTicks { get; init; }
    public IReadOnlyList<Guid> Players { get; init; } = [];

    /// <summary>Commands in the exact order they were executed.</summary>
    public IReadOnlyList<ReplayCommandDto> Commands { get; init; } = [];

    /// <summary>Surrenders, by tick.</summary>
    public IReadOnlyList<ReplaySurrenderDto> Surrenders { get; init; } = [];

    public MatchOutcomeDto? Outcome { get; init; }
}

public sealed record ReplayCommandDto(int Tick, int Slot, CommandRequest Command);

public sealed record ReplaySurrenderDto(int Tick, int Slot);
