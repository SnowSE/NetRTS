using System.Text.RegularExpressions;
using NetRts.Bots;

namespace NetRts.Server.Tests;

/// <summary>
/// The Snow College names live twice, once per language: wwwroot/lore.js for the pages and SnowLore for the
/// server (Block Bot's explanations, the recap). These tests keep the two copies the same.
/// </summary>
public partial class SnowLoreTests
{
    private static readonly string LoreJs = File.ReadAllText(Path.Combine(RepoRoot(), "src", "NetRts.Server", "wwwroot", "lore.js"));

    [Fact]
    public void Building_names_match_lore_js() =>
        Assert.Equal(Object("BUILDING_NAME"), SnowLore.Buildings.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));

    [Fact]
    public void Unit_names_match_lore_js() =>
        Assert.Equal(Object("UNIT_NAME"), SnowLore.Units.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));

    [Fact]
    public void Upgrade_names_match_lore_js() =>
        Assert.Equal(Object("UPGRADE_NAME"), SnowLore.Upgrades.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));

    [Fact]
    public void House_bot_nicknames_match_lore_js() =>
        Assert.Equal(Object("BOT_NICKNAME"), SnowLore.BotNicknames.ToDictionary(kv => kv.Key, kv => kv.Value));

    [Fact]
    public void The_game_and_the_ore_match_lore_js()
    {
        Assert.Contains($"GAME = say('NetRTS', '{SnowLore.Game}')", LoreJs);
        Assert.Contains($"ORE = say('ore', '{SnowLore.Ore}')", LoreJs);
    }

    /// <summary>A flat <c>export const NAME = { Key: 'value', ... };</c> object from lore.js.</summary>
    private static Dictionary<string, string> Object(string name)
    {
        var start = LoreJs.IndexOf($"export const {name} = {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"lore.js has no {name}");
        var end = LoreJs.IndexOf("};", start, StringComparison.Ordinal);
        var body = LoreJs[start..end];
        return Entry().Matches(body).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    [GeneratedRegex(@"(\w+):\s*'([^']*)'")]
    private static partial Regex Entry();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetRts.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root (NetRts.sln) above the test binaries.");
    }
}
