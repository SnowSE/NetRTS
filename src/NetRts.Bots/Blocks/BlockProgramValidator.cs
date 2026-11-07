using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

/// <summary>
/// Checks a Block Bot program before the server runs it. A program is data, never code: it can only
/// use the conditions and actions listed here, and it has size limits, so a bad one can't slow a match.
/// </summary>
public static class BlockProgramValidator
{
    public const int MaxRules = 40;
    public const int MaxActionsPerRule = 8;
    public const int MaxDepth = 6;
    public const int MaxSwitches = 8;
    public const int MaxNameLength = 40;
    public const int MaxNumber = 100_000;

    public static readonly IReadOnlyList<string> Ops = ["<", "<=", ">", ">=", "="];
    public static readonly IReadOnlyList<string> ConditionKinds = ["always", "enemyNearBase", "seeEnemy", "count", "minute", "switch", "and", "or", "not"];
    public static readonly IReadOnlyList<string> ActionKinds = ["train", "trainMix", "build", "research", "attack", "defend", "retreat", "scout"];
    public static readonly IReadOnlyList<string> Targets = ["enemyBase", "nearestEnemy"];

    /// <summary>Null when the program is fine, else a message saying what's wrong (for the editor to show).</summary>
    public static string? Validate(BlockProgramDto? program)
    {
        if (program is null)
        {
            return "Send a program.";
        }

        if ((program.Name ?? "").Length > MaxNameLength)
        {
            return $"Strategy names are at most {MaxNameLength} characters.";
        }

        var switches = program.Switches ?? [];
        if (switches.Count > MaxSwitches)
        {
            return $"At most {MaxSwitches} switches.";
        }

        if (switches.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 24))
        {
            return "Switch names are 1-24 characters.";
        }

        if (switches.Distinct(StringComparer.OrdinalIgnoreCase).Count() != switches.Count)
        {
            return "Two switches have the same name.";
        }

        var rules = program.Rules ?? [];
        if (rules.Count > MaxRules)
        {
            return $"At most {MaxRules} rules.";
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule is null)
            {
                return $"Rule {i + 1} is empty.";
            }

            if (Condition(rule.When, switches, 1) is { } bad)
            {
                return $"Rule {i + 1}: {bad}";
            }

            var actions = rule.Do ?? [];
            if (actions.Count > MaxActionsPerRule)
            {
                return $"Rule {i + 1}: at most {MaxActionsPerRule} actions.";
            }

            foreach (var action in actions)
            {
                if (Action(action) is { } badAction)
                {
                    return $"Rule {i + 1}: {badAction}";
                }
            }
        }

        return null;
    }

    private static string? Condition(BlockConditionDto? c, IReadOnlyList<string> switches, int depth)
    {
        if (c is null)
        {
            return "a condition is missing.";
        }

        if (depth > MaxDepth)
        {
            return $"conditions nest at most {MaxDepth} deep.";
        }

        switch (c.Kind)
        {
            case "always" or "enemyNearBase" or "seeEnemy":
                return null;
            case "count":
                if (c.What is null || !IsCountable(c.What))
                {
                    return $"can't count '{c.What}'.";
                }

                return Comparison(c);
            case "minute":
                return Comparison(c);
            case "switch":
                return c.Name is not null && switches.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                    ? null
                    : $"there's no switch called '{c.Name}'.";
            case "and" or "or":
                return Condition(c.A, switches, depth + 1) ?? Condition(c.B, switches, depth + 1);
            case "not":
                return Condition(c.A, switches, depth + 1);
            default:
                return $"unknown condition '{c.Kind}'.";
        }
    }

    private static string? Comparison(BlockConditionDto c) =>
        c.Op is null || !Ops.Contains(c.Op) ? $"unknown comparison '{c.Op}'."
        : c.Value is not { } v || v < 0 || v > MaxNumber ? $"numbers are 0 to {MaxNumber}."
        : null;

    public static bool IsCountable(string what) =>
        what.Equals("grubs", StringComparison.OrdinalIgnoreCase)
        || what.Equals("workers", StringComparison.OrdinalIgnoreCase)
        || what.Equals("fighters", StringComparison.OrdinalIgnoreCase)
        || Enum.TryParse<UnitType>(what, true, out _)
        || Enum.TryParse<BuildingType>(what, true, out _);

    public static bool IsWho(string? who) =>
        who is null
        || who.Equals("fighters", StringComparison.OrdinalIgnoreCase)
        || (Enum.TryParse<UnitType>(who, true, out var type) && type != UnitType.Worker);

    private static string? Action(BlockActionDto? a)
    {
        if (a is null)
        {
            return "an action is empty.";
        }

        return a.Kind switch
        {
            "train" => a.Unit is null ? "train what?" : null,
            "trainMix" => a.Units is not { Count: > 0 and <= 8 } ? "a mix has 1 to 8 units." : null,
            "build" => a.Building is null ? "build what?"
                : a.Site is not null && !Enum.TryParse<SitePreference>(a.Site, true, out _) ? $"unknown place '{a.Site}'."
                : null,
            "research" => a.Upgrade is null ? "research what?" : null,
            "attack" => !IsWho(a.Who) ? $"unknown unit '{a.Who}'."
                : a.Target is not null && !Targets.Contains(a.Target) ? $"unknown target '{a.Target}'."
                : null,
            "defend" or "retreat" => !IsWho(a.Who) ? $"unknown unit '{a.Who}'." : null,
            "scout" => null,
            _ => $"unknown action '{a.Kind}'.",
        };
    }
}
