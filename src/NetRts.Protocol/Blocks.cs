using System.Text.Json.Serialization;

namespace NetRts.Protocol;

/// <summary>
/// A Block Bot strategy: rules the server checks top to bottom every tick. Built in the browser's block
/// editor (#/blocks) and run on the server, so the bot keeps playing when the tab closes.
/// </summary>
public sealed record BlockProgramDto
{
    public string Name { get; init; } = "";
    public IReadOnlyList<BlockRuleDto> Rules { get; init; } = [];

    /// <summary>The switches the player can flip during a match (rules check them with a "switch" condition).</summary>
    public IReadOnlyList<string> Switches { get; init; } = [];
}

public sealed record BlockRuleDto
{
    public bool Enabled { get; init; } = true;
    public BlockConditionDto When { get; init; } = new() { Kind = "always" };
    public IReadOnlyList<BlockActionDto> Do { get; init; } = [];
}

/// <summary>
/// A condition. Kind is one of: always, enemyNearBase, seeEnemy, count (What Op Value), grubsLeft (Op Value: the
/// grubs left in the piles near your HQs and Co-op Stores), minute (Op Value),
/// switch (Name), and (A, B), or (A, B), not (A).
/// What for count: grubs, workers, fighters, a unit type (Soldier, Archer, Scout, Trebuchet) or a building
/// type (Barracks, TechLab, ...). Units in production and buildings going up count too.
/// </summary>
public sealed record BlockConditionDto
{
    public string Kind { get; init; } = "";
    public string? What { get; init; }
    public string? Op { get; init; }
    public int? Value { get; init; }
    public string? Name { get; init; }
    public BlockConditionDto? A { get; init; }
    public BlockConditionDto? B { get; init; }
}

/// <summary>
/// An action. Kind is one of: train (Unit), trainMix (Units), build (Building, Site), research (Upgrade),
/// attack (Who, Target), defend (Who), retreat (Who), scout (Target: enemyBase or explore), expand (Building:
/// ResourceDepot or CommandCenter, built beside the nearest grub pile you know of that isn't near one yet).
/// Who: fighters or a unit type. Target: enemyBase or nearestEnemy. Site: TowardCenter, NearOre or TowardEnemy.
/// </summary>
public sealed record BlockActionDto
{
    public string Kind { get; init; } = "";
    public UnitType? Unit { get; init; }
    public IReadOnlyList<UnitType>? Units { get; init; }
    public BuildingType? Building { get; init; }
    public string? Site { get; init; }
    public UpgradeType? Upgrade { get; init; }
    public string? Who { get; init; }
    public string? Target { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<BlockPlayMode>))]
public enum BlockPlayMode
{
    Sandbox,
    HouseBot,
    FreeForAll,

    /// <summary>A match for other players to join (people with Block Bots, or bots calling in): 2-4 seats, optionally named.</summary>
    Create,
}

public sealed record StartBlockMatchRequest
{
    public required BlockProgramDto Program { get; init; }
    public BlockPlayMode Mode { get; init; } = BlockPlayMode.HouseBot;

    /// <summary>House bots to play (one for HouseBot, two or three for FreeForAll; ignored for Sandbox).</summary>
    public IReadOnlyList<string> Opponents { get; init; } = [];

    /// <summary>Milliseconds per tick; null for the server's default.</summary>
    public int? TickIntervalMs { get; init; }

    /// <summary>The site theme the page shows, for the words in the status: "snow" (default) or "classic".</summary>
    public string? Theme { get; init; }

    /// <summary>Create only: how many players, 2-4 (default 4). It starts when they're all in, or when its creator starts it.</summary>
    public int? Seats { get; init; }

    /// <summary>Create only: an optional name others can type to find it, e.g. "period-3": 3-32 letters, digits, '_' or '-'.</summary>
    public string? Name { get; init; }
}

public sealed record JoinBlockMatchRequest
{
    public required BlockProgramDto Program { get; init; }

    /// <summary>The site theme the page shows: "snow" (default) or "classic".</summary>
    public string? Theme { get; init; }
}

public sealed record SetBlockSwitchRequest
{
    public string Name { get; init; } = "";
    public bool On { get; init; }
}

/// <summary>What a Block Bot is doing in a match, for the battle cockpit.</summary>
public sealed record BlockBotStatusDto
{
    public Guid MatchId { get; init; }
    public MatchStatus MatchStatus { get; init; }
    public int Tick { get; init; }
    public int Slot { get; init; }

    /// <summary>The program version running now (1 when the match started; each apply adds one).</summary>
    public int Version { get; init; }
    public string ProgramName { get; init; } = "";

    /// <summary>The program running now, so a page that didn't start the match can show and edit it.</summary>
    public BlockProgramDto? Program { get; init; }
    public IReadOnlyList<BlockVersionDto> Versions { get; init; } = [];
    public IReadOnlyDictionary<string, bool> Switches { get; init; } = new Dictionary<string, bool>();
    public IReadOnlyList<BlockRuleStatusDto> Rules { get; init; } = [];
    public BlockStatsDto Stats { get; init; } = new();
    public string? Error { get; init; }
}

public sealed record BlockVersionDto(int Version, int Tick, string Name);

public sealed record BlockRuleStatusDto
{
    public int Index { get; init; }

    /// <summary>firing, waiting (condition true but nothing could be done), idle (condition false) or off.</summary>
    public string State { get; init; } = "idle";

    /// <summary>A sentence a kid can read: why the rule is or isn't doing anything this tick.</summary>
    public string Why { get; init; } = "";

    /// <summary>The units this rule gave orders to this tick (for the pulsing rings in the 3D view).</summary>
    public IReadOnlyList<int> UnitIds { get; init; } = [];

    /// <summary>How close a count condition is to coming true, 0-100, when it has a number to reach.</summary>
    public int? Progress { get; init; }
}

public sealed record BlockStatsDto
{
    public int Grubs { get; init; }
    public int Workers { get; init; }
    public int Fighters { get; init; }
    public int Buildings { get; init; }
    public int Score { get; init; }
}

/// <summary>
/// The words a Block Bot program may use and the limits it must keep (GET /api/v1/blocks/vocabulary). Editors
/// build their pickers from this rather than keeping their own copy.
/// </summary>
public sealed record BlockVocabularyDto
{
    public IReadOnlyList<string> ConditionKinds { get; init; } = [];
    public IReadOnlyList<string> ActionKinds { get; init; } = [];
    public IReadOnlyList<string> Ops { get; init; } = [];

    /// <summary>Where attack goes.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>Where scout goes.</summary>
    public IReadOnlyList<string> ScoutTargets { get; init; } = [];

    /// <summary>Where build puts a building.</summary>
    public IReadOnlyList<string> Sites { get; init; } = [];

    /// <summary>Who an army order goes to: "fighters" or a unit type.</summary>
    public IReadOnlyList<string> Who { get; init; } = [];

    /// <summary>What a count condition can count.</summary>
    public IReadOnlyList<string> Countable { get; init; } = [];

    /// <summary>What expand can build.</summary>
    public IReadOnlyList<BuildingType> ExpandBuildings { get; init; } = [];

    public BlockLimitsDto Limits { get; init; } = new();
    public BlockDefaultsDto Defaults { get; init; } = new();
}

public sealed record BlockLimitsDto
{
    public int MaxRules { get; init; }
    public int MaxActionsPerRule { get; init; }

    /// <summary>How deep and/or/not may nest.</summary>
    public int MaxDepth { get; init; }
    public int MaxSwitches { get; init; }
    public int MaxNameLength { get; init; }
    public int MaxSwitchNameLength { get; init; }

    /// <summary>The largest number a condition may compare with (the smallest is 0).</summary>
    public int MaxNumber { get; init; }

    /// <summary>The most units a trainMix may cycle through.</summary>
    public int MaxMixUnits { get; init; }
}

/// <summary>What the server uses when an optional field is left out.</summary>
public sealed record BlockDefaultsDto
{
    public string Site { get; init; } = "TowardCenter";
    public string Who { get; init; } = "fighters";
    public string Target { get; init; } = "enemyBase";
    public string ScoutTarget { get; init; } = "enemyBase";
    public BuildingType ExpandBuilding { get; init; } = BuildingType.ResourceDepot;
}
