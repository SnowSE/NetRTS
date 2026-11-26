using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Sim;

/// <summary>The rule variants the runner can play, by name. Each changes one thing from classic unless it says otherwise.</summary>
internal static class Variants
{
    private static readonly RuleSet Classic = GameRules.Classic;

    private static RuleSet Tower(RuleSet rules, int cooldown, int cost) =>
        rules.WithBuilding(BuildingType.GuardTower, s => s with { AttackCooldown = cooldown, Cost = cost });

    private static RuleSet SiegeHalf(RuleSet rules) =>
        rules.WithUpgrade(UpgradeType.SiegeEngineering, rules.Upgrades[UpgradeType.SiegeEngineering] with { Cost = 100, ResearchTicks = 30 });

    private static RuleSet SiegeFree(RuleSet rules) =>
        rules.WithBuilding(BuildingType.SiegeWorkshop, s => s with { RequiresUpgrade = null });

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

        ["regrow50"] = Classic with { RegrowPartsPerMillion = 50 },
        ["regrow100"] = Classic with { RegrowPartsPerMillion = 100 },
        ["regrow200"] = Classic with { RegrowPartsPerMillion = 200 },
        ["regrow500"] = Classic with { RegrowPartsPerMillion = 500 },

        ["splash50"] = Classic with { ShellSplashPercent = 50 },

        ["spring"] = GameRules.SpringDraft,
        ["spring-spoils25"] = GameRules.SpringDraft with { SpoilsPercent = 25 },
        ["spring-regrow50"] = GameRules.SpringDraft with { RegrowPartsPerMillion = 50 },
        ["spring-nosplash"] = GameRules.SpringDraft with { ShellSplashPercent = 0 },
    };
}
