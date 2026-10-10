using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>What a bot knows when it decides: the fogged state plus the static map and rules.</summary>
public sealed record BotContext(GameStateDto State, MapDto Map, RulesDto Rules);

/// <summary>
/// A bot's brain. Called once per tick with fresh state; returns the commands to submit.
/// Instances are per match, so they may keep memory between ticks.
/// </summary>
public interface IBotStrategy
{
    string Name { get; }

    IReadOnlyList<CommandRequest> Decide(BotContext context);
}

public static class HouseBots
{
    private static readonly Dictionary<string, (string Description, Func<IBotStrategy> Create)> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["rusher"] = ("The Honey Badger doesn't care: skimps on economy, masses soldiers from two barracks and attacks early.", () => new RusherBot()),
            ["economist"] = ("The Grub Hoarder: booms to a big economy, techs up, turtles behind bell towers, then pushes late.", () => new EconomistBot()),
            ["balanced"] = ("The Blue Badger: steady economy, mixed soldiers and archers, Weapons/Armor upgrades, attacks in waves.", () => new BalancedBot()),
            ["sitter"] = ("The Sleepy Sitter: only mines. A friendly practice badger for testing your first bot.", () => new SitterBot()),
        };

    public static IReadOnlyList<HouseBotDto> All =>
        Registry.Select(kv => new HouseBotDto(kv.Key, kv.Value.Description)).ToList();

    public static bool Exists(string name) => Registry.ContainsKey(name);

    public static IBotStrategy Create(string name) =>
        Registry.TryGetValue(name, out var entry)
            ? entry.Create()
            : throw new ArgumentException($"Unknown house bot '{name}'. Known: {string.Join(", ", Registry.Keys)}", nameof(name));
}
