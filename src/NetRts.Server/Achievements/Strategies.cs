using NetRts.Protocol;

namespace NetRts.Server.Achievements;

/// <summary>
/// Names how a bot played a match, from its build: what it trained, built and researched, and how
/// early. The nightly recap uses these to spot new strategies and bots changing style.
/// </summary>
public static class Strategies
{
    /// <summary>Human-readable names for the recap and the website.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["serum-gambit"] = "Serum gambit",
        ["worker-uprising"] = "Worker uprising",
        ["tower-turtle"] = "Tower turtle",
        ["archer-rush"] = "Archer rush",
        ["soldier-rush"] = "Soldier rush",
        ["scout-swarm"] = "Scout swarm",
        ["fast-expand"] = "Fast expand",
        ["tech-heavy"] = "Tech heavy",
        ["economic-boom"] = "Economic boom",
        ["archer-army"] = "Archer army",
        ["soldier-army"] = "Soldier army",
        ["mixed-army"] = "Mixed army",
        ["passive"] = "Passive",
    };

    /// <summary>A short id such as "archer-rush", or null for matches without stats.</summary>
    public static string? Classify(PlayerMatchStatsDto? stats)
    {
        if (stats is null)
        {
            return null;
        }

        var trained = stats.UnitsTrained;
        int Trained(UnitType t) => trained.GetValueOrDefault(t);
        int Built(BuildingType t) => stats.BuildingsCompleted.GetValueOrDefault(t);
        var soldiers = Trained(UnitType.Soldier);
        var archers = Trained(UnitType.Archer);
        var scouts = Trained(UnitType.Scout);
        var army = soldiers + archers + scouts;
        var early = stats.FirstCombatUnitTick is < 150;

        if (stats.SerumTaken)
        {
            return "serum-gambit";
        }

        if (army == 0 && Built(BuildingType.Barracks) == 0 && stats.CommandCentersDestroyed > 0)
        {
            return "worker-uprising";
        }

        if (Built(BuildingType.GuardTower) >= 4)
        {
            return "tower-turtle";
        }

        if (army > 0 && scouts * 10 >= army * 6)
        {
            return "scout-swarm";
        }

        if (early && army > 0 && archers * 10 >= army * 6)
        {
            return "archer-rush";
        }

        if (early && army > 0 && soldiers * 10 >= army * 6)
        {
            return "soldier-rush";
        }

        if (Built(BuildingType.CommandCenter) > 0 || Built(BuildingType.ResourceDepot) >= 3)
        {
            return "fast-expand";
        }

        if (stats.Upgrades.Count >= 4)
        {
            return "tech-heavy";
        }

        if (army == 0)
        {
            return stats.PeakWorkers >= 15 ? "economic-boom" : "passive";
        }

        if (stats.PeakWorkers >= 20 && army < stats.PeakWorkers / 2)
        {
            return "economic-boom";
        }

        return archers * 10 >= army * 7 ? "archer-army"
            : soldiers * 10 >= army * 7 ? "soldier-army"
            : "mixed-army";
    }
}
