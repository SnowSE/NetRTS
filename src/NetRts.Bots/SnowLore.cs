using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>
/// The Snow College theme's names for things (Badger Brawl): the server's one copy, used by Block Bot's
/// explanations and the nightly recap. It mirrors wwwroot/lore.js, which the pages use; a test checks the two
/// agree, so change both together. The API's own names never change.
/// </summary>
public static class SnowLore
{
    public const string Game = "Badger Brawl";

    public const string Ore = "grubs";

    public static readonly IReadOnlyDictionary<BuildingType, string> Buildings = new Dictionary<BuildingType, string>
    {
        [BuildingType.CommandCenter] = "HQ",
        [BuildingType.Barracks] = "Housing",
        [BuildingType.ResourceDepot] = "Co-op Store",
        [BuildingType.TechLab] = "GRSC Makerspace",
        [BuildingType.GuardTower] = "The Bell Tower",
        [BuildingType.SiegeWorkshop] = "Washburn Welding Shop",
    };

    /// <summary>Units keep their API names, except the siege unit.</summary>
    public static readonly IReadOnlyDictionary<UnitType, string> Units = new Dictionary<UnitType, string>
    {
        [UnitType.Trebuchet] = "Snow Mobile",
    };

    /// <summary>Upgrades are coursework: tier 1 is the intro class, tier 2 the upper-division one, tier 3 the capstone.</summary>
    public static readonly IReadOnlyDictionary<UpgradeType, string> Upgrades = new Dictionary<UpgradeType, string>
    {
        [UpgradeType.Weapons1] = "Intro to Chemistry",
        [UpgradeType.Weapons2] = "Organic Chemistry",
        [UpgradeType.Armor1] = "Unit Testing",
        [UpgradeType.Armor2] = "Code Review",
        [UpgradeType.Armor3] = "Software Quality Assurance",
        [UpgradeType.Mobility1] = "Algorithms",
        [UpgradeType.Mobility2] = "Parallel Computing",
        [UpgradeType.Harvesting1] = "Intro to Biology",
        [UpgradeType.Harvesting2] = "Entomology",
        [UpgradeType.SiegeEngineering] = "Welding Technology",
        [UpgradeType.Fortify1] = "Intro to Construction",
        [UpgradeType.Fortify2] = "Structural Engineering",
    };

    /// <summary>House bots keep their API names; these are their mascot-league nicknames.</summary>
    public static readonly IReadOnlyDictionary<string, string> BotNicknames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sitter"] = "Sleepy Sitter",
        ["poky"] = "Poky Badger",
        ["rusher"] = "Honey Badger",
        ["balanced"] = "Blue Badger",
        ["economist"] = "Grub Hoarder",
    };

    /// <summary>A name to use mid-sentence ("a Bell Tower"): without a leading "The".</summary>
    public static string Bare(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? name[4..] : name;
}
