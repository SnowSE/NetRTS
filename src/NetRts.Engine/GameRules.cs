using NetRts.Protocol;

namespace NetRts.Engine;

public sealed record UnitStats(
    int Cost,
    int BuildTicks,
    int MaxHp,
    int Damage,
    int Armor,
    int Range,
    int AttackCooldown,
    int Vision,
    int Speed,
    BuildingType ProducedAt,
    int MinRange = 0,
    int ShotTicks = 0,
    int Occupancy = 0);

public sealed record BuildingStats(
    int Cost,
    int BuildWork,
    int MaxHp,
    int Armor,
    int Vision,
    int Damage,
    int Range,
    int AttackCooldown,
    int StorageBonus,
    bool IsDropOff,
    IReadOnlyList<UnitType> Produces,
    bool CanResearch,
    IReadOnlyList<BuildingType> Requires,
    UpgradeType? RequiresUpgrade = null);

public sealed record UpgradeStats(int Cost, int ResearchTicks, UpgradeType? Requires, string Effect);

/// <summary>A farm holds <paramref name="Grubs"/> when finished or replanted, and gives them up <paramref name="GatherPerTick"/> at a time.</summary>
public sealed record FarmRules(int Grubs, int ReplantCost, int GatherPerTick);

/// <summary>
/// One complete set of game rules: every balance number plus the optional mechanics a match can be played
/// under. A match runs under exactly one (<see cref="GameConfig.Rules"/>); <see cref="GameRules.Classic"/>
/// is the default and is what <c>GET /api/v1/rules</c> publishes.
/// </summary>
public sealed record RuleSet
{
    public required string Name { get; init; }

    /// <summary>What people call it, e.g. "Spring 2027 draft". Not part of <see cref="Version"/>.</summary>
    public string Title { get; init; } = "";

    public int StartingResources { get; init; } = GameRules.StartingResources;
    public int StartingWorkers { get; init; } = GameRules.StartingWorkers;
    public int WorkerCarryCapacity { get; init; } = GameRules.WorkerCarryCapacity;
    public int GatherPerTick { get; init; } = GameRules.GatherPerTick;
    public int MaxProductionQueue { get; init; } = GameRules.MaxProductionQueue;
    public int MovePointsPerTile { get; init; } = GameRules.MovePointsPerTile;
    public int UnderConstructionVision { get; init; } = GameRules.UnderConstructionVision;
    public int WeaponsBonusPerTier { get; init; } = GameRules.WeaponsBonusPerTier;
    public int ArmorBonusPerTier { get; init; } = GameRules.ArmorBonusPerTier;
    public int FortifyBonusPerTier { get; init; } = GameRules.FortifyBonusPerTier;
    public int MobilityBonusPerTier { get; init; } = GameRules.MobilityBonusPerTier;
    public int HarvestingBonusPerTier { get; init; } = GameRules.HarvestingBonusPerTier;
    public int SerumTicks { get; init; } = GameRules.SerumTicks;
    public int SerumFactor { get; init; } = GameRules.SerumFactor;

    public required IReadOnlyDictionary<UnitType, UnitStats> Units { get; init; }
    public required IReadOnlyDictionary<BuildingType, BuildingStats> Buildings { get; init; }
    public required IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    // Optional mechanics. Their defaults are the classic game, so a rule set that leaves them alone plays
    // exactly like it.

    /// <summary>Percent of a destroyed building's cost paid to whoever destroyed it, as grubs (storage permitting).</summary>
    public int SpoilsPercent { get; init; }

    /// <summary>Each Command Center you already own (finished or started) adds this percent of its base cost.</summary>
    public int ExtraHqCostPercent { get; init; }

    /// <summary>
    /// A grub patch below its starting amount regrows this many millionths of what it holds now, every tick,
    /// never past its starting amount. A patch mined to 0 is gone. 0 = no regrowth.
    /// </summary>
    public int RegrowPartsPerMillion { get; init; }

    /// <summary>Percent of a shell's damage (before armor) dealt on the 8 tiles around where it lands. 0 = none.</summary>
    public int ShellSplashPercent { get; init; }

    /// <summary>
    /// Points one cell holds; a unit standing in it takes <see cref="UnitStats.Occupancy"/>. 0 = no limit (units
    /// share tiles freely).
    /// </summary>
    public int CellCapacity { get; init; }

    /// <summary>Workers that dig from one cell at once (needs <see cref="CellCapacity"/>); the rest wait. 0 = no limit.</summary>
    public int MinersPerEdge { get; init; }

    /// <summary>The farm's numbers when <see cref="Buildings"/> has <see cref="BuildingType.Farm"/>.</summary>
    public FarmRules? Farm { get; init; }

    /// <summary>Ticks the <see cref="UpgradeType.RapidFire"/> upgrade takes off guard tower cooldowns (never below 1).</summary>
    public int RapidFireCooldownCut { get; init; } = 1;

    /// <summary>
    /// Identifies these exact rules: changes when any number or mechanic changes, so a replay is only
    /// re-simulated under the rules it was played with.
    /// </summary>
    public string Version
    {
        get
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(ToDto());
            var options = (SpoilsPercent, ExtraHqCostPercent, RegrowPartsPerMillion, RapidFireCooldownCut, ShellSplashPercent);
            if (options != (0, 0, 0, 1, 0))
            {
                json = [.. json, .. System.Text.Encoding.UTF8.GetBytes(options.ToString())];
            }

            return $"{GameSimulation.Revision}-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(json))[..12]}";
        }
    }

    /// <summary>These rules with one unit's stats changed.</summary>
    public RuleSet WithUnit(UnitType type, Func<UnitStats, UnitStats> change) =>
        this with { Units = Replace(Units, type, change(Units[type])) };

    /// <summary>These rules with one building's stats changed.</summary>
    public RuleSet WithBuilding(BuildingType type, Func<BuildingStats, BuildingStats> change) =>
        this with { Buildings = Replace(Buildings, type, change(Buildings[type])) };

    /// <summary>These rules with one upgrade added or changed.</summary>
    public RuleSet WithUpgrade(UpgradeType type, UpgradeStats stats) =>
        this with { Upgrades = Replace(Upgrades, type, stats) };

    // A copy keeps the original order (and so the published order); a new key goes last.
    private static Dictionary<TKey, TValue> Replace<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source, TKey key, TValue value)
        where TKey : notnull => new(source) { [key] = value };

    /// <summary>What a Command Center costs a player who owns <paramref name="owned"/> already.</summary>
    public int CommandCenterCost(int owned) =>
        Buildings[BuildingType.CommandCenter].Cost * (100 + ExtraHqCostPercent * owned) / 100;

    public RulesDto ToDto() => new()
    {
        Units = Units.Select(kv => new UnitStatsDto
        {
            Type = kv.Key,
            Cost = kv.Value.Cost,
            BuildTicks = kv.Value.BuildTicks,
            MaxHp = kv.Value.MaxHp,
            Damage = kv.Value.Damage,
            Armor = kv.Value.Armor,
            Range = kv.Value.Range,
            AttackCooldown = kv.Value.AttackCooldown,
            Vision = kv.Value.Vision,
            Speed = kv.Value.Speed,
            ProducedAt = kv.Value.ProducedAt,
            MinRange = kv.Value.MinRange,
            ShotTicks = kv.Value.ShotTicks,
            Occupancy = kv.Value.Occupancy,
        }).ToList(),
        Buildings = Buildings.Select(kv => new BuildingStatsDto
        {
            Type = kv.Key,
            Cost = kv.Value.Cost,
            BuildWork = kv.Value.BuildWork,
            MaxHp = kv.Value.MaxHp,
            Armor = kv.Value.Armor,
            Vision = kv.Value.Vision,
            Damage = kv.Value.Damage,
            Range = kv.Value.Range,
            AttackCooldown = kv.Value.AttackCooldown,
            StorageBonus = kv.Value.StorageBonus,
            IsDropOff = kv.Value.IsDropOff,
            Produces = kv.Value.Produces,
            CanResearch = kv.Value.CanResearch,
            Requires = kv.Value.Requires,
            RequiresUpgrade = kv.Value.RequiresUpgrade,
        }).ToList(),
        Upgrades = Upgrades.Select(kv => new UpgradeStatsDto
        {
            Type = kv.Key,
            Cost = kv.Value.Cost,
            ResearchTicks = kv.Value.ResearchTicks,
            Requires = kv.Value.Requires,
            Effect = kv.Value.Effect,
        }).ToList(),
        Economy = new EconomyRulesDto
        {
            StartingResources = StartingResources,
            StartingWorkers = StartingWorkers,
            WorkerCarryCapacity = WorkerCarryCapacity,
            GatherPerTick = GatherPerTick,
            MaxProductionQueue = MaxProductionQueue,
        },
        Notes = Notes,
        ExtraCommandCenterCostPercent = ExtraHqCostPercent,
        SpoilsPercent = SpoilsPercent,
        RegrowPartsPerMillion = RegrowPartsPerMillion,
        ShellSplashPercent = ShellSplashPercent,
        CellCapacity = CellCapacity,
        MinersPerEdge = MinersPerEdge,
        Farm = Farm is { } farm ? new FarmRulesDto { Grubs = farm.Grubs, ReplantCost = farm.ReplantCost, GatherPerTick = farm.GatherPerTick } : null,
    };

    /// <summary>These rules with cells that hold <paramref name="capacity"/> points and each unit type's share of one.</summary>
    public RuleSet WithCells(int capacity, IReadOnlyDictionary<UnitType, int> occupancy) => this with
    {
        CellCapacity = capacity,
        Units = Units.ToDictionary(kv => kv.Key, kv => kv.Value with { Occupancy = occupancy[kv.Key] }),
    };

    /// <summary>These rules with farms: <paramref name="stats"/> for the building, <paramref name="farm"/> for what it grows.</summary>
    public RuleSet WithFarms(BuildingStats stats, FarmRules farm) =>
        this with { Buildings = Replace(Buildings, BuildingType.Farm, stats), Farm = farm };
}

/// <summary>All balance numbers live here so they can be published to bots via GET /api/v1/rules.</summary>
public static class GameRules
{
    public const int StartingResources = 500;
    public const int StartingWorkers = 5;
    public const int WorkerCarryCapacity = 10;
    public const int GatherPerTick = 2;
    public const int MaxProductionQueue = 5;

    /// <summary>Movement points needed to step one tile; unit speed is in the same unit.</summary>
    public const int MovePointsPerTile = 10;

    public const int UnderConstructionVision = 2;

    public const int WeaponsBonusPerTier = 2;
    public const int ArmorBonusPerTier = 1;
    public const int FortifyBonusPerTier = 1;
    public const int MobilityBonusPerTier = 2;
    public const int HarvestingBonusPerTier = 1;

    public const int SerumTicks = 30;
    public const int SerumFactor = 2;

    private static readonly IReadOnlyDictionary<UnitType, UnitStats> ClassicUnits = new Dictionary<UnitType, UnitStats>
    {
        [UnitType.Worker] = new(Cost: 50, BuildTicks: 8, MaxHp: 40, Damage: 4, Armor: 0, Range: 1, AttackCooldown: 1, Vision: 5, Speed: 10, ProducedAt: BuildingType.CommandCenter),
        [UnitType.Soldier] = new(Cost: 100, BuildTicks: 12, MaxHp: 110, Damage: 9, Armor: 1, Range: 1, AttackCooldown: 1, Vision: 5, Speed: 10, ProducedAt: BuildingType.Barracks),
        [UnitType.Archer] = new(Cost: 125, BuildTicks: 14, MaxHp: 60, Damage: 12, Armor: 0, Range: 4, AttackCooldown: 2, Vision: 6, Speed: 9, ProducedAt: BuildingType.Barracks),
        [UnitType.Scout] = new(Cost: 75, BuildTicks: 8, MaxHp: 55, Damage: 5, Armor: 0, Range: 1, AttackCooldown: 1, Vision: 8, Speed: 20, ProducedAt: BuildingType.Barracks),

        // Outranges guard towers but can't hit anything close, and its shells land late, so it wants an escort.
        [UnitType.Trebuchet] = new(Cost: 350, BuildTicks: 45, MaxHp: 150, Damage: 40, Armor: 0, Range: 7, AttackCooldown: 8, Vision: 7, Speed: 5,
            ProducedAt: BuildingType.SiegeWorkshop, MinRange: 3, ShotTicks: 3),
    };

    private static readonly IReadOnlyDictionary<BuildingType, BuildingStats> ClassicBuildings = new Dictionary<BuildingType, BuildingStats>
    {
        [BuildingType.CommandCenter] = new(Cost: 400, BuildWork: 80, MaxHp: 1500, Armor: 2, Vision: 6, Damage: 0, Range: 0, AttackCooldown: 0,
            StorageBonus: 1500, IsDropOff: true, Produces: [UnitType.Worker], CanResearch: false, Requires: []),
        [BuildingType.Barracks] = new(Cost: 150, BuildWork: 40, MaxHp: 700, Armor: 1, Vision: 6, Damage: 0, Range: 0, AttackCooldown: 0,
            StorageBonus: 0, IsDropOff: false, Produces: [UnitType.Soldier, UnitType.Archer, UnitType.Scout], CanResearch: false, Requires: []),
        [BuildingType.ResourceDepot] = new(Cost: 100, BuildWork: 25, MaxHp: 450, Armor: 1, Vision: 6, Damage: 0, Range: 0, AttackCooldown: 0,
            StorageBonus: 750, IsDropOff: true, Produces: [], CanResearch: false, Requires: []),
        [BuildingType.TechLab] = new(Cost: 150, BuildWork: 40, MaxHp: 550, Armor: 1, Vision: 6, Damage: 0, Range: 0, AttackCooldown: 0,
            StorageBonus: 0, IsDropOff: false, Produces: [], CanResearch: true, Requires: [BuildingType.Barracks]),
        [BuildingType.GuardTower] = new(Cost: 125, BuildWork: 35, MaxHp: 500, Armor: 2, Vision: 7, Damage: 10, Range: 5, AttackCooldown: 1,
            StorageBonus: 0, IsDropOff: false, Produces: [], CanResearch: false, Requires: [BuildingType.Barracks]),
        [BuildingType.SiegeWorkshop] = new(Cost: 250, BuildWork: 60, MaxHp: 650, Armor: 1, Vision: 6, Damage: 0, Range: 0, AttackCooldown: 0,
            StorageBonus: 0, IsDropOff: false, Produces: [UnitType.Trebuchet], CanResearch: false, Requires: [BuildingType.TechLab],
            RequiresUpgrade: UpgradeType.SiegeEngineering),
    };

    /// <summary>The spring rules' share of a 9-point cell each unit takes.</summary>
    public static readonly IReadOnlyDictionary<UnitType, int> SpringOccupancy = new Dictionary<UnitType, int>
    {
        [UnitType.Worker] = 2,
        [UnitType.Soldier] = 1,
        [UnitType.Archer] = 1,
        [UnitType.Scout] = 2,
        [UnitType.Trebuchet] = 9,
    };

    private static readonly BuildingStats SpringFarm = new(Cost: 100, BuildWork: 30, MaxHp: 300, Armor: 0, Vision: 3, Damage: 0, Range: 0, AttackCooldown: 0,
        StorageBonus: 0, IsDropOff: false, Produces: [], CanResearch: false, Requires: []);

    private const string SharedTilesNote = "Units may share tiles. Buildings, resource deposits and rock block movement.";

    private static readonly IReadOnlyDictionary<UpgradeType, UpgradeStats> ClassicUpgrades = new Dictionary<UpgradeType, UpgradeStats>
    {
        [UpgradeType.Weapons1] = new(150, 40, null, $"+{WeaponsBonusPerTier} damage for all units and guard towers"),
        [UpgradeType.Weapons2] = new(250, 60, UpgradeType.Weapons1, $"+{WeaponsBonusPerTier} more damage"),
        [UpgradeType.Armor1] = new(150, 40, null, $"+{ArmorBonusPerTier} armor for all units"),
        [UpgradeType.Armor2] = new(250, 60, UpgradeType.Armor1, $"+{ArmorBonusPerTier} more armor"),
        [UpgradeType.Armor3] = new(350, 80, UpgradeType.Armor2, $"+{ArmorBonusPerTier} more armor"),
        [UpgradeType.Mobility1] = new(100, 30, null, $"+{MobilityBonusPerTier} speed (tenths of a tile per tick) for all units"),
        [UpgradeType.Mobility2] = new(200, 45, UpgradeType.Mobility1, $"+{MobilityBonusPerTier} more speed"),
        [UpgradeType.Harvesting1] = new(100, 30, null, $"+{HarvestingBonusPerTier} ore gathered per mining tick"),
        [UpgradeType.Harvesting2] = new(200, 45, UpgradeType.Harvesting1, $"+{HarvestingBonusPerTier} more ore per mining tick"),
        [UpgradeType.Fortify1] = new(150, 40, null, $"+{FortifyBonusPerTier} armor for all completed buildings"),
        [UpgradeType.Fortify2] = new(250, 60, UpgradeType.Fortify1, $"+{FortifyBonusPerTier} more building armor"),
        [UpgradeType.SiegeEngineering] = new(200, 60, null, "Unlocks the SiegeWorkshop, which builds Trebuchets"),
    };

    /// <summary>
    /// Identifies these rules and this simulation, e.g. "1-3f9a0c2b7d41". Recorded with every match: a
    /// replay re-simulated under a different version may not play out the way the match did.
    /// </summary>
    private static readonly IReadOnlyList<string> ClassicNotes =
    [
        "Coordinates are (x, y) with (0, 0) at the top-left. tile = y * mapWidth + x.",
        "Attack range and adjacency use Chebyshev distance (diagonals count as 1). Vision uses Euclidean distance.",
        SharedTilesNote,
        "Damage dealt = max(1, attacker damage - target armor). All attacks in a tick land simultaneously.",
        "Unit costs are paid when production is queued; building costs when the first worker starts construction.",
        "Ore beyond your storage capacity is lost. Storage = sum of StorageBonus of your completed buildings.",
        "Siege units (ShotTicks > 0) lob shells at a tile: they land ShotTicks later and hit every enemy on that tile then. They can't fire inside MinRange.",
        "Idle combat units fight back against enemies already in range. Attack-move (Attack with a position) engages anything seen en route.",
        "A player with no Command Center (finished or under construction) is eliminated.",
        "At the tick limit the highest total score wins; equal scores are a draw.",
        "Score: destruction (value of enemy assets destroyed) + economy (ore banked) + survival (value of your living units and finished buildings).",
    ];

    /// <summary>The rules every match has been played under so far, and the default.</summary>
    public static RuleSet Classic { get; } = new()
    {
        Name = "classic",
        Title = "Classic",
        Units = ClassicUnits,
        Buildings = ClassicBuildings,
        Upgrades = ClassicUpgrades,
        Notes = ClassicNotes,
    };

    /// <summary>
    /// The spring 2027 rules as drafted: slower, dearer guard towers with the RapidFire upgrade, cheaper
    /// siege with splashing shells, stronger Fortify, spoils, dearer extra Command Centers and regrowing
    /// grub patches. Numbers are still being tuned with netrts-sim.
    /// </summary>
    public static RuleSet SpringDraft { get; } = MakeSpringDraft();

    /// <summary>Every rule set a server can be told to run, by <see cref="RuleSet.Name"/>.</summary>
    public static IReadOnlyDictionary<string, RuleSet> Named { get; } =
        new[] { Classic, SpringDraft }.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The named rule set with this <see cref="RuleSet.Version"/>, or null if none matches any more.</summary>
    public static RuleSet? ByVersion(string version) => Named.Values.FirstOrDefault(r => r.Version == version);

    private static RuleSet MakeSpringDraft()
    {
        var rules = Classic
            .WithBuilding(BuildingType.GuardTower, s => s with { AttackCooldown = 3, Cost = 150 })
            .WithUpgrade(UpgradeType.SiegeEngineering, ClassicUpgrades[UpgradeType.SiegeEngineering] with { Cost = 100, ResearchTicks = 30 })
            .WithUpgrade(UpgradeType.RapidFire, new UpgradeStats(150, 40, null, "Guard towers fire every 2 ticks instead of 3"))
            .WithUpgrade(UpgradeType.Fortify1, ClassicUpgrades[UpgradeType.Fortify1] with { Effect = "+2 armor for all completed buildings" })
            .WithUpgrade(UpgradeType.Fortify2, ClassicUpgrades[UpgradeType.Fortify2] with { Effect = "+2 more building armor" })
            .WithCells(9, SpringOccupancy)
            .WithFarms(SpringFarm, new FarmRules(Grubs: 300, ReplantCost: 75, GatherPerTick: 1));
        const string siegeNote = "Siege units (ShotTicks > 0) lob shells at a tile: they land ShotTicks later and hit every enemy on that tile then. They can't fire inside MinRange.";
        return rules with
        {
            Name = "spring-draft",
            Title = "Spring 2027 (draft)",
            FortifyBonusPerTier = 2,
            SpoilsPercent = 50,
            ExtraHqCostPercent = 25,
            RegrowPartsPerMillion = 200,
            ShellSplashPercent = 50,
            MinersPerEdge = 2,
            Notes =
            [
                .. ClassicNotes.Select(n => n switch
                {
                    siegeNote => "Siege units (ShotTicks > 0) lob shells at a tile: they land ShotTicks later and hit every enemy on that tile then, and every enemy on the 8 tiles around it for shellSplashPercent of the damage (before armor). They can't fire inside MinRange.",
                    SharedTilesNote => "Units walk through each other, but a cell holds cellCapacity points: a unit standing in it (for any reason) takes its occupancy. First in keeps the cell; a unit that stops where there isn't room is bumped to the nearest cell that has it. Ties between players take turns, starting one player further round each time. Buildings, resource deposits and rock block movement.",
                    _ => n,
                }),
                "Digging: at most minersPerEdge workers dig from one cell at once, shared by every patch or farm beside it; others there wait their turn (activity Waiting). A worker leaving to drop off gives up its spot. A worker bumped away from its patch looks within its vision for a cell with a free spot, and goes idle if there is none.",
                "Farms: a finished farm holds farm.grubs, dug like a patch at farm.gatherPerTick (plus Harvesting). An empty farm stays standing; its workers move on. Gather on your own empty farm replants it when the worker gets there, for farm.replantCost.",
                "Destroying an enemy building banks spoilsPercent of its cost for you (storage permitting). Spoils are not economy score.",
                "Each Command Center you own (finished or under construction) adds extraCommandCenterCostPercent of the base cost to your next one.",
                "A deposit below its starting amount regrows regrowPartsPerMillion millionths of what it holds every tick, never past its starting amount. One mined to 0 is gone.",
            ],
        };
    }

    public static IReadOnlyDictionary<UnitType, UnitStats> Units => Classic.Units;
    public static IReadOnlyDictionary<BuildingType, BuildingStats> Buildings => Classic.Buildings;
    public static IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades => Classic.Upgrades;
    public static string Version => Classic.Version;
    public static RulesDto ToDto() => Classic.ToDto();
}
