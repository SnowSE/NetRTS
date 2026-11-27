using System.Text.Json.Serialization;

namespace NetRts.Protocol;

/// <summary>A rule set a match can be played under (GET /api/v1/rulesets): pass its name as the match setting <c>rules</c>.</summary>
public sealed record RuleSetInfoDto(string Name, string Title, string Version, bool IsDefault);

/// <summary>Static game data so bots can be data driven instead of hard-coding numbers.</summary>
public sealed record RulesDto
{
    public IReadOnlyList<UnitStatsDto> Units { get; init; } = [];
    public IReadOnlyList<BuildingStatsDto> Buildings { get; init; } = [];
    public IReadOnlyList<UpgradeStatsDto> Upgrades { get; init; } = [];
    public required EconomyRulesDto Economy { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// Each Command Center you already own (finished or under construction) adds this percent of the base
    /// cost to your next one. Left out when 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ExtraCommandCenterCostPercent { get; init; }

    /// <summary>Percent of a destroyed enemy building's cost you bank as grubs. Left out when 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SpoilsPercent { get; init; }

    /// <summary>
    /// A deposit below its starting amount regrows this many millionths of what it holds every tick, never
    /// past its starting amount; one mined to 0 is gone. Left out when 0 (no regrowth).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RegrowPartsPerMillion { get; init; }

    /// <summary>Percent of a shell's damage (before armor) dealt to enemies on the 8 tiles around where it lands. Left out when 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ShellSplashPercent { get; init; }

    /// <summary>
    /// Points one cell holds. A unit standing in a cell takes its type's <c>occupancy</c>; a unit that stops
    /// where there isn't room is bumped to the nearest cell that has it. Left out when 0 (units share tiles freely).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CellCapacity { get; init; }

    /// <summary>
    /// Workers that can dig from one cell at once, shared by every patch or farm beside it; the rest wait.
    /// Left out when 0 (no limit).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MinersPerEdge { get; init; }

    /// <summary>Farm numbers; left out when these rules have no farms.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FarmRulesDto? Farm { get; init; }
}

/// <summary>A farm holds <see cref="Grubs"/> when finished or replanted; workers dig it like a patch, at <see cref="GatherPerTick"/>.</summary>
public sealed record FarmRulesDto
{
    public int Grubs { get; init; }

    /// <summary>What a worker sent to Gather an empty farm pays to replant it.</summary>
    public int ReplantCost { get; init; }

    public int GatherPerTick { get; init; }
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

    /// <summary>Targets closer than this (Chebyshev) are too close to hit. 0 = no minimum.</summary>
    public int MinRange { get; init; }

    /// <summary>Ticks between attacks.</summary>
    public int AttackCooldown { get; init; }

    /// <summary>
    /// 0 = attacks land at once. Otherwise the unit lobs a shell at the target's tile that lands this many
    /// ticks later, hitting every enemy on that tile then; targets that move off it are missed.
    /// </summary>
    public int ShotTicks { get; init; }

    /// <summary>Sight radius in tiles (Euclidean).</summary>
    public int Vision { get; init; }

    /// <summary>Tenths of a tile per tick (10 = one tile per tick).</summary>
    public int Speed { get; init; }

    public BuildingType ProducedAt { get; init; }

    /// <summary>Points of a cell's <c>cellCapacity</c> this unit takes while it stands there. Left out when the rules have no cell limit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Occupancy { get; init; }
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

    /// <summary>An upgrade that must be researched before this building can be started, if any.</summary>
    public UpgradeType? RequiresUpgrade { get; init; }
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
