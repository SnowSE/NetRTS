using System.Globalization;
using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Server.Pickup;

/// <summary>
/// What a pick-up plan may say (checked before it's stored), the start kit it buys, and the three ready-made
/// decks (what the house plays; a phone loads one with a tap). Prices come from <see cref="GameRules"/>.
/// </summary>
public static class PickupPlans
{
    public const int StartGrubs = 1500;
    /// <summary>Not a game rule (plans can be as long as you like): a guard against junk requests.</summary>
    public const int MaxSteps = 100;
    public const int MaxForever = 3;
    public const int MaxEachBuilding = 3;
    public const int MaxCount = 60;
    public const int MatchSeconds = 150;

    /// <summary>What the start pile sells: the basic army, workers, and the first buildings.</summary>
    public static readonly IReadOnlyList<string> StartItems =
        ["Soldier", "Archer", "Scout", "Worker", "Barracks", "ResourceDepot", "TechLab", "GuardTower"];

    private static readonly string[] Kinds = ["workers", "train", "build", "research"];

    /// <summary>What an attack can go for first (PickupAttackDto.Priority).</summary>
    public static readonly IReadOnlyList<string> AttackTargets = ["towers", "workers", "base", "nearest"];

    public static IReadOnlyList<PickupDeckDto> Decks { get; } =
    [
        new("rush", "Honey Rush", "Honey Badger", new PickupPlanDto
        {
            Start = [new("Soldier", 8), new("Archer", 5)],
            Steps =
            [
                new() { Kind = "workers", Count = 8 },
                new() { Kind = "build", Item = "Barracks" },
                new() { Kind = "train", Item = "Soldier", Count = 18, Keep = true },
            ],
            Forever = ["Soldier", "Soldier", "Archer"],
            Attack = new() { Trigger = "army", Army = 12, Target = "base" },
        }),
        new("turtle", "Turtle Town", "Blue Badger", new PickupPlanDto
        {
            Start = [new("GuardTower", 3), new("Soldier", 6), new("Archer", 4)],
            Steps =
            [
                new() { Kind = "train", Item = "Archer", Count = 8, Keep = true },
                new() { Kind = "build", Item = "TechLab" },
                new() { Kind = "research", Item = "Armor1" },
                new() { Kind = "build", Item = "GuardTower" },
                new() { Kind = "research", Item = "Weapons1" },
            ],
            Forever = ["Archer", "Soldier"],
            Attack = new() { Trigger = "army", Army = 22, Target = "nearest" },
        }),
        new("boom", "Grub Boom", "Grub Hoarder", new PickupPlanDto
        {
            Start = [new("Worker", 10), new("ResourceDepot", 1), new("TechLab", 1), new("Soldier", 2)],
            Steps =
            [
                new() { Kind = "research", Item = "Harvesting1" },
                new() { Kind = "workers", Count = 24 },
                new() { Kind = "build", Item = "Barracks" },
                new() { Kind = "train", Item = "Archer", Count = 10 },
                new() { Kind = "research", Item = "Weapons1" },
                new() { Kind = "train", Item = "Soldier", Count = 15 },
            ],
            Forever = ["Soldier", "Archer"],
            Attack = new() { Trigger = "time", AtSecond = 120, Target = "base" },
        }),
    ];

    /// <summary>Null when the plan is fine; otherwise a sentence saying what's wrong.</summary>
    public static string? Validate(PickupPlanDto plan)
    {
        var cost = 0;
        foreach (var buy in plan.Start)
        {
            if (!StartItems.Contains(buy.Item))
            {
                return $"{buy.Item} can't be bought for the start.";
            }

            if (buy.Count is < 1 or > MaxCount)
            {
                return $"Buy between 1 and {MaxCount} of {buy.Item}.";
            }

            cost += CostOf(buy.Item) * buy.Count;
        }

        if (cost > StartGrubs)
        {
            return $"Your start pile costs {cost.ToString("N0", CultureInfo.InvariantCulture)} grubs; you have {StartGrubs.ToString("N0", CultureInfo.InvariantCulture)}.";
        }

        if (plan.Start.GroupBy(b => b.Item).FirstOrDefault(g => IsBuilding(g.Key) && g.Sum(b => b.Count) > MaxEachBuilding) is { } crowded)
        {
            return $"At most {MaxEachBuilding} of each building to start ({crowded.Key}).";
        }

        if (plan.Steps.Count > MaxSteps)
        {
            return $"At most {MaxSteps} steps.";
        }

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            if (StepError(plan.Steps[i]) is { } error)
            {
                return $"Step {i + 1}: {error}";
            }
        }

        if (plan.Forever.Count is < 1 or > MaxForever)
        {
            return $"Forever takes 1 to {MaxForever} cards.";
        }

        if (plan.Forever.FirstOrDefault(f => !IsUnit(f) && !IsBuilding(f) && !IsUpgrade(f)) is { } odd)
        {
            return $"Forever: {odd} isn't a card.";
        }

        var a = plan.Attack;
        if (a.Trigger is not ("army" or "time") || a.Target is not ("base" or "nearest"))
        {
            return "Attack: pick when (army or time) and what (base or nearest).";
        }

        if (a.Priority is { } order && (order.Count is < 1 or > 4 || order.Distinct().Count() != order.Count || order.Any(p => !AttackTargets.Contains(p))))
        {
            return $"Attack: go for each of {string.Join(", ", AttackTargets)} at most once.";
        }

        return a.Army is < 1 or > MaxCount || a.AtSecond is < 0 or > MatchSeconds ? "Attack: that army size or time is out of range." : null;
    }

    private static string? StepError(PickupStepDto step) => step.Kind switch
    {
        "workers" => step.Count is < 1 or > MaxCount ? $"workers up to 1 to {MaxCount}." : null,
        "train" => step.Item is null || !IsUnit(step.Item) || step.Item == "Worker" ? "train a fighter." :
            step.Count is < 1 or > MaxCount ? $"train up to 1 to {MaxCount}." : null,
        "build" => step.Item is null || !IsBuilding(step.Item) ? "build a building." : null,
        "research" => step.Item is null || !IsUpgrade(step.Item) ? "take a class (an upgrade)." : null,
        _ => $"a step is one of {string.Join(", ", Kinds)}.",
    };

    /// <summary>What the start pile buys: the grubs left over, the units and buildings, and the free Housing.</summary>
    public static StartKit KitFor(PickupPlanDto plan)
    {
        var units = new List<UnitType>();
        var buildings = new List<BuildingType> { BuildingType.Barracks };
        var spent = 0;
        foreach (var buy in plan.Start)
        {
            spent += CostOf(buy.Item) * buy.Count;
            for (var i = 0; i < buy.Count; i++)
            {
                if (IsUnit(buy.Item))
                {
                    units.Add(Enum.Parse<UnitType>(buy.Item));
                }
                else
                {
                    buildings.Add(Enum.Parse<BuildingType>(buy.Item));
                }
            }
        }

        return new StartKit(Math.Max(0, StartGrubs - spent), units, buildings);
    }

    private static bool IsUnit(string item) => Enum.TryParse<UnitType>(item, out var t) && Enum.IsDefined(t) && item == t.ToString();

    private static bool IsBuilding(string item) =>
        Enum.TryParse<BuildingType>(item, out var t) && Enum.IsDefined(t) && item == t.ToString() && t != BuildingType.CommandCenter;

    private static bool IsUpgrade(string item) => Enum.TryParse<UpgradeType>(item, out var t) && Enum.IsDefined(t) && item == t.ToString();

    private static int CostOf(string item) =>
        IsUnit(item) ? GameRules.Units[Enum.Parse<UnitType>(item)].Cost : GameRules.Buildings[Enum.Parse<BuildingType>(item)].Cost;
}
