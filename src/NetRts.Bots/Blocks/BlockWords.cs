using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

/// <summary>
/// The words Block Bot uses when it explains itself, in either site theme: Snow College's Badger Brawl
/// (Housing for Barracks, grubs for ore, Snow Mobile for Trebuchet) or the original NetRTS names. The
/// page sends the theme it shows; the API's own names never change.
/// </summary>
public sealed class BlockWords
{
    public static readonly BlockWords Snow = new(snow: true);
    public static readonly BlockWords Classic = new(snow: false);

    private readonly bool _snow;

    private BlockWords(bool snow) => _snow = snow;

    /// <summary>"classic" for the original theme; anything else (or nothing) is the Snow College theme, the site's default.</summary>
    public static BlockWords For(string? theme) =>
        string.Equals(theme, "classic", StringComparison.OrdinalIgnoreCase) ? Classic : Snow;

    public bool IsSnow => _snow;

    public string Ore => _snow ? "grubs" : "ore";

    /// <summary>What a grubsLeft condition counts.</summary>
    public string OreLeft => _snow ? "grubs left near your base" : "ore left near your base";

    public string Unit(UnitType type, int count = 1)
    {
        var one = type switch
        {
            UnitType.Trebuchet => _snow ? "Snow Mobile" : "trebuchet",
            _ => type.ToString().ToLowerInvariant(),
        };
        return count == 1 ? one : one + "s";
    }

    public string Building(BuildingType type) => _snow
        ? type switch
        {
            BuildingType.CommandCenter => "HQ",
            BuildingType.Barracks => "Housing",
            BuildingType.ResourceDepot => "Co-op Store",
            BuildingType.TechLab => "Makerspace",
            BuildingType.GuardTower => "Bell Tower",
            BuildingType.SiegeWorkshop => "Welding Shop",
            _ => type.ToString(),
        }
        : type switch
        {
            BuildingType.CommandCenter => "Command Center",
            BuildingType.Barracks => "Barracks",
            BuildingType.ResourceDepot => "Resource Depot",
            BuildingType.TechLab => "Tech Lab",
            BuildingType.GuardTower => "Guard Tower",
            BuildingType.SiegeWorkshop => "Siege Workshop",
            _ => type.ToString(),
        };

    public string Upgrade(UpgradeType type) => _snow
        ? type switch
        {
            UpgradeType.Weapons1 => "Intro to Chemistry",
            UpgradeType.Weapons2 => "Organic Chemistry",
            UpgradeType.Armor1 => "Unit Testing",
            UpgradeType.Armor2 => "Code Review",
            UpgradeType.Mobility1 => "Algorithms",
            UpgradeType.Mobility2 => "Parallel Computing",
            UpgradeType.Harvesting1 => "Intro to Biology",
            UpgradeType.Harvesting2 => "Entomology",
            UpgradeType.SiegeEngineering => "Welding Technology",
            _ => type.ToString(),
        }
        : type switch
        {
            UpgradeType.Weapons1 => "Weapons 1",
            UpgradeType.Weapons2 => "Weapons 2",
            UpgradeType.Armor1 => "Armor 1",
            UpgradeType.Armor2 => "Armor 2",
            UpgradeType.Mobility1 => "Mobility 1",
            UpgradeType.Mobility2 => "Mobility 2",
            UpgradeType.Harvesting1 => "Harvesting 1",
            UpgradeType.Harvesting2 => "Harvesting 2",
            UpgradeType.SiegeEngineering => "Siege Engineering",
            _ => type.ToString(),
        };

    /// <summary>"learn" an upgrade: a class at the Makerspace in the Snow theme, research otherwise.</summary>
    public string Learning => _snow ? "taking" : "researching";

    /// <summary>Who an army order goes to: "fighters" or a unit type.</summary>
    public string Who(string? who, int count = 2) =>
        who is null || who.Equals("fighters", StringComparison.OrdinalIgnoreCase)
            ? (count == 1 ? "fighter" : "fighters")
            : Enum.TryParse<UnitType>(who, true, out var type) ? Unit(type, count) : who;

    /// <summary>What a count condition counts.</summary>
    public string What(string what) =>
        what.ToLowerInvariant() switch
        {
            "grubs" => Ore,
            "workers" => "workers",
            "fighters" => "fighters",
            _ when Enum.TryParse<UnitType>(what, true, out var unit) => Unit(unit, 2),
            _ when Enum.TryParse<BuildingType>(what, true, out var building) => Building(building),
            _ => what,
        };

    public static string Op(string op) => op switch
    {
        "<" => "fewer than",
        "<=" => "at most",
        ">" => "more than",
        ">=" => "at least",
        "=" => "exactly",
        _ => op,
    };
}
