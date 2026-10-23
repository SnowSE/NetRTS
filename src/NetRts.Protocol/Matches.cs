using System.Text.Json.Serialization;

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

    /// <summary>
    /// Optional name so others can find the match, e.g. "cs1400-lab": 3-32 letters, digits, '_' or '-'.
    /// No two matches that haven't finished can share a name.
    /// </summary>
    public string? Name { get; init; }
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

    /// <summary>The name given when the match was created, or null.</summary>
    public string? Name { get; init; }

    /// <summary>True while the match is paused (see POST /api/v1/matches/{id}/speed).</summary>
    public bool Paused { get; init; }

    /// <summary>
    /// Whether spectators may change the speed: only matches with at most one real player (exhibitions and
    /// games against house bots), so nobody's opponent can be sped up or paused on them.
    /// </summary>
    public bool SpeedAdjustable { get; init; }

    /// <summary>Which leaderboard the match counts toward. Matches bots create are Practice.</summary>
    public MatchCategory Category { get; init; }

    /// <summary>
    /// For a league match (preseason, season, championship): its heats once the admin has split it,
    /// empty before that. Null for ordinary matches. Bots just play the league match's id; the server
    /// routes each bot to its own heat.
    /// </summary>
    public IReadOnlyList<HeatDto>? Heats { get; init; }

    /// <summary>A league match the admin has closed: no more rounds or players.</summary>
    public bool Closed { get; init; }

    /// <summary>A closed league match whose points are on the ladder.</summary>
    public bool Finalized { get; init; }

    /// <summary>For a heat: the league match it belongs to.</summary>
    public Guid? ParentMatchId { get; init; }
}

/// <summary>One page of finished matches from the archive (GET /api/v1/matches/archive), newest first.</summary>
public sealed record MatchArchivePageDto
{
    /// <summary>How many finished matches pass the filters, across every page.</summary>
    public int Total { get; init; }

    /// <summary>How many matching matches come before this page.</summary>
    public int Skip { get; init; }

    /// <summary>The page size asked for (1-100).</summary>
    public int Limit { get; init; }

    public IReadOnlyList<MatchSummaryDto> Matches { get; init; } = [];
}

/// <summary>Change a running match's speed. Either field may be left out.</summary>
public sealed record MatchSpeedRequest
{
    /// <summary>New milliseconds per tick, within the server's limits (100-10000 by default).</summary>
    public int? TickIntervalMs { get; init; }

    /// <summary>Pause or resume. A paused match resumes on its own after five minutes.</summary>
    public bool? Paused { get; init; }
}

public sealed record MatchPlayerDto
{
    public Guid PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int Slot { get; init; }
    public bool IsHouseBot { get; init; }

    /// <summary>True when a Discord-verified person has claimed this bot (the blue check).</summary>
    public bool Verified { get; init; }

    /// <summary>The verified owner's name: their Discord server nickname, or Discord name. Null if unclaimed.</summary>
    public string? OwnerName { get; init; }

    /// <summary>In a league match's queue: whether the bot made an API call in the last 40 seconds. Null elsewhere.</summary>
    public bool? Connected { get; init; }
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

    /// <summary>How the match went for this player, in more detail. Null for matches recorded before these were kept.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerMatchStatsDto? Stats { get; init; }
}

/// <summary>A player's match in numbers: what they built and trained, and how they played. Used for achievements and recaps.</summary>
public sealed record PlayerMatchStatsDto
{
    /// <summary>Units trained, by type. The starting workers don't count.</summary>
    public IReadOnlyDictionary<UnitType, int> UnitsTrained { get; init; } = new Dictionary<UnitType, int>();

    /// <summary>Buildings finished, by type. The starting Command Center doesn't count.</summary>
    public IReadOnlyDictionary<BuildingType, int> BuildingsCompleted { get; init; } = new Dictionary<BuildingType, int>();

    /// <summary>Research completed.</summary>
    public IReadOnlyList<UpgradeType> Upgrades { get; init; } = [];

    /// <summary>Tick the first Barracks was finished, if one was.</summary>
    public int? FirstBarracksTick { get; init; }

    /// <summary>Tick the first Soldier, Archer or Scout was ready, if one was.</summary>
    public int? FirstCombatUnitTick { get; init; }

    /// <summary>Tick this player first saw an enemy Command Center, if they did.</summary>
    public int? EnemyCommandCenterSeenTick { get; init; }

    /// <summary>Resource deposits this player's workers mined dry.</summary>
    public int DepositsExhausted { get; init; }

    /// <summary>Enemy Command Centers destroyed.</summary>
    public int CommandCentersDestroyed { get; init; }

    /// <summary>Most workers alive at once.</summary>
    public int PeakWorkers { get; init; }

    /// <summary>Most ore banked at once.</summary>
    public int PeakResources { get; init; }

    /// <summary>Ore workers brought home that didn't fit in storage.</summary>
    public int OreWasted { get; init; }

    /// <summary>Enemy units killed by this player's guard towers.</summary>
    public int TowerKills { get; init; }

    /// <summary>Whether this player took the serum.</summary>
    public bool SerumTaken { get; init; }

    /// <summary>Enemy units killed by serum-boosted soldiers.</summary>
    public int SerumKills { get; init; }

    /// <summary>Commands accepted into the queue.</summary>
    public int CommandsQueued { get; init; }

    /// <summary>Commands rejected when submitted.</summary>
    public int CommandsRejected { get; init; }

    /// <summary>Commands that failed when they ran (CommandFailed events).</summary>
    public int CommandsFailed { get; init; }

    /// <summary>Longest stretch, in ticks, any one worker spent idle.</summary>
    public int LongestWorkerIdleTicks { get; init; }

    /// <summary>Different buildings given a rally point.</summary>
    public int BuildingsRallied { get; init; }

    /// <summary>Completed guard towers standing when the match ended.</summary>
    public int GuardTowersStanding { get; init; }

    /// <summary>Health of the player's healthiest Command Center when the match ended, in percent; null if they had none.</summary>
    public int? CommandCenterHealthPercent { get; init; }

    /// <summary>
    /// The lowest this player's score fell relative to the leader's, in percent (100 = never behind),
    /// checked every 10 ticks from tick 100 while they were still in the match.
    /// </summary>
    public int LowestScoreSharePercent { get; init; } = 100;
}

/// <summary>Omniscient view for spectators: every entity, every player's economy.</summary>
public sealed record SpectatorStateDto
{
    public Guid MatchId { get; init; }
    public int Tick { get; init; }
    public MatchStatus Status { get; init; }
    public int MaxTicks { get; init; }
    public int TickIntervalMs { get; init; }
    public bool Paused { get; init; }
    public bool SpeedAdjustable { get; init; }
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

    /// <summary>Map size and seed: the map is generated from these, so it isn't stored.</summary>
    public int MapWidth { get; init; }

    public int MapHeight { get; init; }
    public int Seed { get; init; }
    public int MaxTicks { get; init; }

    /// <summary>Player ids by slot.</summary>
    public IReadOnlyList<Guid> Players { get; init; } = [];

    /// <summary>Commands in the order they were queued, each with the tick it was queued on.</summary>
    public IReadOnlyList<ReplayCommandDto> Commands { get; init; } = [];

    /// <summary>Surrenders, by tick.</summary>
    public IReadOnlyList<ReplaySurrenderDto> Surrenders { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ReplayBoostDto>? Boosts { get; init; }

    /// <summary>How the match ended; null while it is still being played.</summary>
    public MatchOutcomeDto? Outcome { get; init; }
}

public sealed record ReplayCommandDto(
    int Tick,
    int Slot,
    CommandRequest Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Boosted = false);

public sealed record ReplaySurrenderDto(int Tick, int Slot);

public sealed record ReplayBoostDto(int Tick, int Slot);

/// <summary>
/// How to play back a finished match frame by frame: what spectators saw on every tick, in chunks of
/// <see cref="ChunkTicks"/> ticks. Fetch chunk <c>n</c> from <c>/frames/{n}</c>: newline-separated
/// <see cref="SpectatorStateDto"/>s for ticks <c>n * ChunkTicks</c> onward, one per line.
/// </summary>
public sealed record ReplayFramesDto
{
    public Guid MatchId { get; init; }
    public int ChunkTicks { get; init; }
    public int ChunkCount { get; init; }

    /// <summary>The last tick with a frame: where the match ended.</summary>
    public int LastTick { get; init; }

    /// <summary>
    /// The game rules the match was played under. Frames show what happened then, whatever the rules
    /// are now.
    /// </summary>
    public string RulesVersion { get; init; } = "";

    /// <summary>
    /// The match was played before frames were recorded, so they were rebuilt from its replay
    /// (and checked against the recorded result).
    /// </summary>
    public bool Rebuilt { get; init; }

    public required MapDto Map { get; init; }

    /// <summary>The whole play-by-play, oldest first.</summary>
    public IReadOnlyList<GameEventDto> Events { get; init; } = [];
}
