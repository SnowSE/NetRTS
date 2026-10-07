namespace NetRts.Protocol;

/// <summary>Everything one player is allowed to see this tick (fog of war applied).</summary>
public sealed record GameStateDto
{
    public Guid MatchId { get; init; }
    public int Tick { get; init; }
    public MatchStatus Status { get; init; }
    public int MaxTicks { get; init; }
    public int TickIntervalMs { get; init; }
    public int MapWidth { get; init; }
    public int MapHeight { get; init; }

    public required SelfDto You { get; init; }
    public IReadOnlyList<PlayerSummaryDto> Players { get; init; } = [];

    /// <summary>One string per map row (y), one char per column (x): '1' = currently visible, '0' = fogged.</summary>
    public IReadOnlyList<string> Visibility { get; init; } = [];

    /// <summary>Your units plus enemy units you can currently see.</summary>
    public IReadOnlyList<UnitDto> Units { get; init; } = [];

    /// <summary>Your buildings plus enemy buildings you can currently see.</summary>
    public IReadOnlyList<BuildingDto> Buildings { get; init; } = [];

    /// <summary>Resource deposits you can currently see.</summary>
    public IReadOnlyList<ResourceDepositDto> Resources { get; init; } = [];

    /// <summary>
    /// Enemy buildings you have seen before that are now hidden by fog, as they were when last seen.
    /// An entry disappears once you see its tile again and the building is gone.
    /// </summary>
    public IReadOnlyList<RememberedBuildingDto> RememberedBuildings { get; init; } = [];

    /// <summary>Your recent events (command failures, completions, losses...), newest last.</summary>
    public IReadOnlyList<GameEventDto> Events { get; init; } = [];

    public MatchOutcomeDto? Outcome { get; init; }
}

public sealed record SelfDto
{
    public Guid PlayerId { get; init; }
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public int Resources { get; init; }
    public int StorageCapacity { get; init; }
    public int QueuedCommands { get; init; }
    public int QueueCapacity { get; init; }
    public IReadOnlyList<UpgradeType> Upgrades { get; init; } = [];
    public IReadOnlyList<ResearchDto> Researching { get; init; } = [];

    /// <summary>Ore banked over the last 60 ticks (one minute at normal speed).</summary>
    public int IncomePerMinute { get; init; }
}

public sealed record PlayerSummaryDto
{
    public Guid PlayerId { get; init; }
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public bool Eliminated { get; init; }
    public required ScoreDto Score { get; init; }
    public PositionDto StartPosition { get; init; } = new(0, 0);
}

public sealed record ScoreDto
{
    /// <summary>Resource value of enemy units and buildings destroyed.</summary>
    public int Destruction { get; init; }

    /// <summary>Total ore gathered.</summary>
    public int Economy { get; init; }

    /// <summary>Resource value of your surviving units and completed buildings.</summary>
    public int Survival { get; init; }

    public int Total => Destruction + Economy + Survival;
}

public sealed record PositionDto(int X, int Y);

public sealed record UnitDto
{
    public int Id { get; init; }
    public int Owner { get; init; }
    public UnitType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public UnitActivity Activity { get; init; }

    /// <summary>Ore being carried (workers).</summary>
    public int Carrying { get; init; }

    /// <summary>Current target entity (attack target, deposit, construction site), if any.</summary>
    public int? TargetId { get; init; }

    /// <summary>Current destination, if any.</summary>
    public PositionDto? Destination { get; init; }
}

public sealed record BuildingDto
{
    public int Id { get; init; }
    public int Owner { get; init; }
    public BuildingType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public bool Completed { get; init; }

    /// <summary>Construction progress 0-100.</summary>
    public int ConstructionPercent { get; init; }

    /// <summary>Production queue (own buildings only); the first entry is in progress.</summary>
    public IReadOnlyList<ProductionDto> Production { get; init; } = [];

    /// <summary>Upgrade being researched here (own buildings only).</summary>
    public ResearchDto? Research { get; init; }

    /// <summary>Where newly trained units go (own buildings only).</summary>
    public PositionDto? Rally { get; init; }
}

public sealed record RememberedBuildingDto
{
    public int Id { get; init; }
    public int Owner { get; init; }
    public BuildingType Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public bool Completed { get; init; }
    public int LastSeenTick { get; init; }
}

public sealed record ProductionDto(UnitType UnitType, int Percent);

public sealed record ResearchDto(UpgradeType Upgrade, int Percent, int BuildingId);

public sealed record ResourceDepositDto
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Remaining { get; init; }
}

public sealed record GameEventDto
{
    public int Tick { get; init; }

    /// <summary>
    /// Machine readable kind, e.g. CommandFailed, UnitProduced, UnitLost, UnitKilled, BuildingCompleted,
    /// BuildingLost, BuildingDestroyed, ResearchCompleted, DepositDepleted, PlayerEliminated.
    /// </summary>
    public string Kind { get; init; } = "";

    public string Message { get; init; } = "";
    public int? EntityId { get; init; }
    public long? CommandId { get; init; }

    /// <summary>Slot of the player the event concerns (set in spectator feeds; null for match-wide events).</summary>
    public int? Owner { get; init; }
}

public sealed record MatchOutcomeDto
{
    public MatchEndReason Reason { get; init; }

    /// <summary>Null for a draw.</summary>
    public Guid? WinnerId { get; init; }

    public int Ticks { get; init; }
}
