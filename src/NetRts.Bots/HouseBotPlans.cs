using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>Early aggression: few workers, two barracks, constant soldiers, attack at six.</summary>
public sealed class RusherBot() : PlanBot("rusher", new BotPlan
{
    TargetWorkers = 8,
    BuildOrder =
    [
        new(BuildingType.Barracks, 1, MinWorkers: 6, SitePreference.TowardEnemy),
        new(BuildingType.Barracks, 2, MinWorkers: 7, SitePreference.TowardEnemy),
    ],
    ArmyMix = [UnitType.Soldier, UnitType.Soldier, UnitType.Soldier, UnitType.Scout],
    AttackThreshold = 6,
});

/// <summary>Greedy economy, towers for safety, upgrades, then a big late push.</summary>
public sealed class EconomistBot() : PlanBot("economist", new BotPlan
{
    TargetWorkers = 18,
    BuildOrder =
    [
        new(BuildingType.ResourceDepot, 1, MinWorkers: 9, SitePreference.NearOre),
        new(BuildingType.Barracks, 1, MinWorkers: 11),
        new(BuildingType.GuardTower, 1, MinWorkers: 12, SitePreference.TowardEnemy),
        new(BuildingType.TechLab, 1, MinWorkers: 13),
        new(BuildingType.GuardTower, 2, MinWorkers: 14, SitePreference.TowardEnemy),
        new(BuildingType.Barracks, 2, MinWorkers: 16),
        new(BuildingType.ResourceDepot, 2, MinWorkers: 17),
    ],
    Upgrades = [UpgradeType.Harvesting1, UpgradeType.Weapons1, UpgradeType.Armor1, UpgradeType.Harvesting2, UpgradeType.Weapons2, UpgradeType.Armor2],
    ArmyMix = [UnitType.Soldier, UnitType.Archer, UnitType.Archer],
    AttackThreshold = 18,
    AllInTick = 1200,
});

/// <summary>Middle of the road: steady economy, mixed army, a couple of upgrades, waves of ten.</summary>
public sealed class BalancedBot() : PlanBot("balanced", new BotPlan
{
    TargetWorkers = 13,
    BuildOrder =
    [
        new(BuildingType.Barracks, 1, MinWorkers: 8),
        new(BuildingType.ResourceDepot, 1, MinWorkers: 10, SitePreference.NearOre),
        new(BuildingType.TechLab, 1, MinWorkers: 11),
        new(BuildingType.Barracks, 2, MinWorkers: 12),
        new(BuildingType.GuardTower, 1, MinWorkers: 13, SitePreference.TowardEnemy),
    ],
    Upgrades = [UpgradeType.Weapons1, UpgradeType.Armor1, UpgradeType.Mobility1, UpgradeType.Weapons2],
    ArmyMix = [UnitType.Soldier, UnitType.Soldier, UnitType.Archer],
    AttackThreshold = 10,
    AllInTick = 1500,
});

/// <summary>Only mines and never builds an army. A target for testing a first bot.</summary>
public sealed class SitterBot() : PlanBot("sitter", new BotPlan
{
    TargetWorkers = 8,
    ArmyMix = [],
    AttackThreshold = 0,
});
