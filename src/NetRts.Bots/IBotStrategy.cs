using NetRts.Bots.Formics;
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

/// <summary>
/// A server-played bot that takes the serum when it judges the moment right. The server asks after every
/// <see cref="IBotStrategy.Decide"/>; the first yes takes it (once a match), and the bot's commands are boosted while it lasts.
/// </summary>
public interface ISerumTaker
{
    bool WantsSerum { get; }
}

public static class HouseBots
{
    private static readonly Dictionary<string, (string Description, Func<IBotStrategy> Create)> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["rusher"] = ("The Honey Badger doesn't care: rushes up the tech to two Snow Mobiles (trebuchets) and rolls them at you ringed by soldiers.", () => new RusherBot()),
            ["economist"] = ("The Grub Hoarder: booms to a big economy, techs up, turtles behind bell towers, then pushes late.", () => new EconomistBot()),
            ["balanced"] = ("The Blue Badger: steady economy, mixed soldiers and archers, Weapons/Armor upgrades, attacks in waves.", () => new BalancedBot()),
            ["sitter"] = ("The Sleepy Sitter: only mines. A friendly practice badger for testing your first bot.", () => new SitterBot()),
            ["poky"] = ("The Poky Badger: digs slowly, keeps a few soldiers home and walks the rest over three at a time. A first opponent that fights back.", () => new PokyBot()),
            ["formics"] = ("The Formics: a hive mind and the hardest house bot. Digs every corner of the map with a hundred workers, guards them with bell towers, then swarms in with upgraded soldiers that all strike the same target.", () => new FormicsBot()),
        };

    /// <summary>The house bots the Full House achievement asks a bot to beat (the Formics came later, and aren't one of them).</summary>
    public static IReadOnlyList<string> FullHouse { get; } = ["rusher", "economist", "balanced", "sitter", "poky"];

    public static IReadOnlyList<HouseBotDto> All =>
        Registry.Select(kv => new HouseBotDto(kv.Key, kv.Value.Description)).ToList();

    public static bool Exists(string name) => Registry.ContainsKey(name);

    public static IBotStrategy Create(string name) =>
        Registry.TryGetValue(name, out var entry)
            ? entry.Create()
            : throw new ArgumentException($"Unknown house bot '{name}'. Known: {string.Join(", ", Registry.Keys)}", nameof(name));
}
