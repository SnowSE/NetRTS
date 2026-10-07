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
    BuildingType ProducedAt);

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
    IReadOnlyList<BuildingType> Requires);

public sealed record UpgradeStats(int Cost, int ResearchTicks, UpgradeType? Requires, string Effect);

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
    public const int MobilityBonusPerTier = 2;
    public const int HarvestingBonusPerTier = 1;

    public static readonly IReadOnlyDictionary<UnitType, UnitStats> Units = new Dictionary<UnitType, UnitStats>
    {
        [UnitType.Worker] = new(Cost: 50, BuildTicks: 8, MaxHp: 40, Damage: 4, Armor: 0, Range: 1, AttackCooldown: 1, Vision: 5, Speed: 10, ProducedAt: BuildingType.CommandCenter),
        [UnitType.Soldier] = new(Cost: 100, BuildTicks: 12, MaxHp: 110, Damage: 9, Armor: 1, Range: 1, AttackCooldown: 1, Vision: 5, Speed: 10, ProducedAt: BuildingType.Barracks),
        [UnitType.Archer] = new(Cost: 125, BuildTicks: 14, MaxHp: 60, Damage: 12, Armor: 0, Range: 4, AttackCooldown: 2, Vision: 6, Speed: 9, ProducedAt: BuildingType.Barracks),
        [UnitType.Scout] = new(Cost: 75, BuildTicks: 8, MaxHp: 55, Damage: 5, Armor: 0, Range: 1, AttackCooldown: 1, Vision: 8, Speed: 20, ProducedAt: BuildingType.Barracks),
    };

    public static readonly IReadOnlyDictionary<BuildingType, BuildingStats> Buildings = new Dictionary<BuildingType, BuildingStats>
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
    };

    public static readonly IReadOnlyDictionary<UpgradeType, UpgradeStats> Upgrades = new Dictionary<UpgradeType, UpgradeStats>
    {
        [UpgradeType.Weapons1] = new(150, 40, null, $"+{WeaponsBonusPerTier} damage for all units and guard towers"),
        [UpgradeType.Weapons2] = new(250, 60, UpgradeType.Weapons1, $"+{WeaponsBonusPerTier} more damage"),
        [UpgradeType.Armor1] = new(150, 40, null, $"+{ArmorBonusPerTier} armor for all units"),
        [UpgradeType.Armor2] = new(250, 60, UpgradeType.Armor1, $"+{ArmorBonusPerTier} more armor"),
        [UpgradeType.Mobility1] = new(100, 30, null, $"+{MobilityBonusPerTier} speed (tenths of a tile per tick) for all units"),
        [UpgradeType.Mobility2] = new(200, 45, UpgradeType.Mobility1, $"+{MobilityBonusPerTier} more speed"),
        [UpgradeType.Harvesting1] = new(100, 30, null, $"+{HarvestingBonusPerTier} ore gathered per mining tick"),
        [UpgradeType.Harvesting2] = new(200, 45, UpgradeType.Harvesting1, $"+{HarvestingBonusPerTier} more ore per mining tick"),
    };

    public static RulesDto ToDto() => new()
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
        Notes =
        [
            "Coordinates are (x, y) with (0, 0) at the top-left. tile = y * mapWidth + x.",
            "Attack range and adjacency use Chebyshev distance (diagonals count as 1). Vision uses Euclidean distance.",
            "Units may share tiles. Buildings, resource deposits and rock block movement.",
            "Damage dealt = max(1, attacker damage - target armor). All attacks in a tick land simultaneously.",
            "Unit costs are paid when production is queued; building costs when the first worker starts construction.",
            "Ore beyond your storage capacity is lost. Storage = sum of StorageBonus of your completed buildings.",
            "Idle combat units fight back against enemies already in range. Attack-move (Attack with a position) engages anything seen en route.",
            "A player with no Command Center (finished or under construction) is eliminated.",
            "At the tick limit the highest total score wins; equal scores are a draw.",
            "Score: destruction (value of enemy assets destroyed) + economy (ore banked) + survival (value of your living units and finished buildings).",
        ],
    };
}
