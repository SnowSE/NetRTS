using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

/// <summary>
/// The words Block Bot uses when it explains itself, in either site theme: Snow College's Badger Brawl
/// (<see cref="SnowLore"/>: Housing for Barracks, grubs for ore, Snow Mobile for Trebuchet) or the original
/// NetRTS names. Each request for a status says which theme the page shows; the API's own names never change.
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

    public string Ore => _snow ? SnowLore.Ore : "ore";

    /// <summary>What a grubsLeft condition counts.</summary>
    public string OreLeft => $"{Ore} left near your base";

    public string Unit(UnitType type, int count = 1)
    {
        var one = _snow && SnowLore.Units.TryGetValue(type, out var snow) ? snow : type.ToString().ToLowerInvariant();
        return count == 1 ? one : one + "s";
    }

    /// <summary>A building's name mid-sentence ("a Bell Tower"), in the Snow theme as the pages show it, without "The".</summary>
    public string Building(BuildingType type) =>
        _snow && SnowLore.Buildings.TryGetValue(type, out var snow) ? SnowLore.Bare(snow) : Spaced(type.ToString());

    public string Upgrade(UpgradeType type) =>
        _snow && SnowLore.Upgrades.TryGetValue(type, out var snow) ? snow : Spaced(type.ToString());

    /// <summary>"CommandCenter" -> "Command Center", "Weapons1" -> "Weapons 1" (as lore.js's spaced()).</summary>
    private static string Spaced(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsLower(name[i - 1]) && (char.IsUpper(name[i]) || char.IsDigit(name[i])))
            {
                sb.Append(' ');
            }

            sb.Append(name[i]);
        }

        return sb.ToString();
    }

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
