namespace NetRts.Protocol;

/// <summary>Static game data so bots can be data driven instead of hard-coding numbers.</summary>
public sealed record RulesDto
{
    public IReadOnlyList<UnitStatsDto> Units { get; init; } = [];
    public IReadOnlyList<BuildingStatsDto> Buildings { get; init; } = [];
    public IReadOnlyList<UpgradeStatsDto> Upgrades { get; init; } = [];
    public required EconomyRulesDto Economy { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record UnitStatsDto
{
    public UnitType Type { get; init; }
    public int Cost { get; init; }
    public int BuildTicks { get; init; }
    public int MaxHp { get; init; }
    public int Damage { get; init; }
    public int Armor { get; init; }

    /// <summary>Attack reach in tiles (Chebyshev distance: diagonals count as 1).</summary>
    public int Range { get; init; }

    /// <summary>Ticks between attacks.</summary>
    public int AttackCooldown { get; init; }

    /// <summary>Sight radius in tiles (Euclidean).</summary>
    public int Vision { get; init; }

    /// <summary>Tenths of a tile per tick (10 = one tile per tick).</summary>
    public int Speed { get; init; }

    public BuildingType ProducedAt { get; init; }
}

public sealed record BuildingStatsDto
{
    public BuildingType Type { get; init; }
    public int Cost { get; init; }

    /// <summary>Worker-ticks of construction (two workers build twice as fast).</summary>
    public int BuildWork { get; init; }

    public int MaxHp { get; init; }
    public int Armor { get; init; }
    public int Vision { get; init; }
    public int Damage { get; init; }
    public int Range { get; init; }
    public int AttackCooldown { get; init; }
    public int StorageBonus { get; init; }
    public bool IsDropOff { get; init; }
    public IReadOnlyList<UnitType> Produces { get; init; } = [];
    public bool CanResearch { get; init; }
    public IReadOnlyList<BuildingType> Requires { get; init; } = [];
}

public sealed record UpgradeStatsDto
{
    public UpgradeType Type { get; init; }
    public int Cost { get; init; }
    public int ResearchTicks { get; init; }
    public UpgradeType? Requires { get; init; }
    public string Effect { get; init; } = "";
}

public sealed record EconomyRulesDto
{
    public int StartingResources { get; init; }
    public int StartingWorkers { get; init; }
    public int WorkerCarryCapacity { get; init; }
    public int GatherPerTick { get; init; }
    public int MaxProductionQueue { get; init; }
}
