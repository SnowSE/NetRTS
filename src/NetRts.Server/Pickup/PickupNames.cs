namespace NetRts.Server.Pickup;

/// <summary>
/// Two-word names for pick-up players ("Sneaky-Sagebrush"), the same words Block Bot's welcome uses
/// (blocks/intro.js). Nobody types a name, so nothing a student types reaches the big screen.
/// </summary>
public static class PickupNames
{
    private static readonly string[] First =
    [
        "Sneaky", "Brave", "Fuzzy", "Speedy", "Mighty", "Clever", "Rusty", "Jolly", "Lucky", "Bouncy", "Grumpy", "Dizzy",
        "Plucky", "Zippy", "Frosty", "Muddy", "Sunny", "Wily", "Noisy", "Gentle", "Stubborn", "Scrappy", "Sleepy", "Tiny",
    ];

    private static readonly string[] Second =
    [
        "Sagebrush", "Pinecone", "Snowdrift", "Marmot", "Juniper", "Boulder", "Canyon", "Cricket", "Beetle", "Gopher", "Burrow", "Pebble",
        "Mesa", "Thistle", "Aspen", "Ridge", "Coyote", "Tumbleweed", "Chipmunk", "Acorn", "Magpie", "Willow", "Hedgehog", "Bramble",
    ];

    /// <summary>A name not in <paramref name="taken"/> (after many tries, one with a number on the end).</summary>
    public static string Pick(Random rng, IReadOnlySet<string> taken)
    {
        for (var i = 0; i < 200; i++)
        {
            var name = $"{First[rng.Next(First.Length)]}-{Second[rng.Next(Second.Length)]}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }

        return $"{First[rng.Next(First.Length)]}-{Second[rng.Next(Second.Length)]}{rng.Next(2, 99)}";
    }
}
