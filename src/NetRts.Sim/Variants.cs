using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Sim;

/// <summary>The rule variants the runner can play, by name. Each changes one thing from classic unless it says otherwise.</summary>
internal static class Variants
{
    private static readonly RuleSet Classic = GameRules.Classic;

    private static RuleSet Tower(RuleSet rules, int cooldown, int cost) =>
        rules.WithBuilding(BuildingType.GuardTower, s => s with { AttackCooldown = cooldown, Cost = cost });

    private static RuleSet RapidFire(RuleSet rules) =>
        rules.WithUpgrade(UpgradeType.RapidFire, new UpgradeStats(150, 40, null, "Guard towers fire faster"));

    private static RuleSet SiegeHalf(RuleSet rules) =>
        rules.WithUpgrade(UpgradeType.SiegeEngineering, rules.Upgrades[UpgradeType.SiegeEngineering] with { Cost = 100, ResearchTicks = 30 });

    private static RuleSet SiegeFree(RuleSet rules) =>
        rules.WithBuilding(BuildingType.SiegeWorkshop, s => s with { RequiresUpgrade = null });

    /// <summary>The spring draft: every round 3 decision at its first-guess number.</summary>
    private static RuleSet Spring(int spoils, int regrowPpm, int towerCost, int fortify) =>
        RapidFire(SiegeHalf(Tower(Classic, 3, towerCost))) with
        {
            Name = "spring",
            SpoilsPercent = spoils,
            ExtraHqCostPercent = 25,
            RegrowPartsPerMillion = regrowPpm,
            FortifyBonusPerTier = fortify,
        };

    public static IReadOnlyDictionary<string, RuleSet> All { get; } = new Dictionary<string, RuleSet>
    {
        ["classic"] = Classic,

        ["tower-cd3"] = Tower(Classic, 3, 125),
        ["tower-cd3-150"] = Tower(Classic, 3, 150),
        ["tower-cd3-175"] = Tower(Classic, 3, 175),
        ["tower-cd2"] = Tower(Classic, 2, 125),

        ["spoils25"] = Classic with { SpoilsPercent = 25 },
        ["spoils50"] = Classic with { SpoilsPercent = 50 },
        ["spoils75"] = Classic with { SpoilsPercent = 75 },
        ["spoils100"] = Classic with { SpoilsPercent = 100 },

        ["siege-half"] = SiegeHalf(Classic),
        ["siege-free"] = SiegeFree(Classic),

        ["fortify2"] = Classic with { FortifyBonusPerTier = 2 },
        ["fortify3"] = Classic with { FortifyBonusPerTier = 3 },

        ["hq25"] = Classic with { ExtraHqCostPercent = 25 },

        ["regrow100"] = Classic with { RegrowPartsPerMillion = 100 },
        ["regrow200"] = Classic with { RegrowPartsPerMillion = 200 },
        ["regrow500"] = Classic with { RegrowPartsPerMillion = 500 },

        ["spring"] = Spring(spoils: 50, regrowPpm: 200, towerCost: 150, fortify: 2),
        ["spring-spoils25"] = Spring(spoils: 25, regrowPpm: 200, towerCost: 150, fortify: 2),
    };
}
