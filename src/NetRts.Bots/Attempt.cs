using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>
/// What one of <see cref="BotTurn"/>'s actions did: <see cref="Done"/>, or the reason it couldn't. A bot can
/// ignore the reason (the house bots just wait) or explain it (Block Bot turns each into a sentence).
/// </summary>
public abstract record Attempt
{
    private Attempt()
    {
    }

    public bool Succeeded => this is Done;

    /// <summary>The order went out. <paramref name="UnitId"/> is the worker sent, for a building.</summary>
    public sealed record Done(int? UnitId = null) : Attempt;

    /// <summary>There's no finished building of this type to do it at (or, for a building, to unlock it).</summary>
    public sealed record NeedsBuilding(BuildingType Type) : Attempt;

    /// <summary>This upgrade has to be learned first.</summary>
    public sealed record NeedsUpgrade(UpgradeType Upgrade) : Attempt;

    /// <summary>Every building of this type that could do it is busy.</summary>
    public sealed record Busy(BuildingType Type) : Attempt;

    /// <summary>It costs more than is left to spend this tick.</summary>
    public sealed record TooExpensive(int Cost, int Have) : Attempt;

    /// <summary>One of these is already under construction or ordered.</summary>
    public sealed record AlreadyGoingUp(BuildingType Type) : Attempt;

    /// <summary>The upgrade is already learned.</summary>
    public sealed record AlreadyHave(UpgradeType Upgrade) : Attempt;

    /// <summary>The upgrade is being learned right now.</summary>
    public sealed record InProgress(UpgradeType Upgrade) : Attempt;

    /// <summary>No open tile for the building.</summary>
    public sealed record NoSite : Attempt;

    /// <summary>No worker free to build it.</summary>
    public sealed record NoBuilder : Attempt;
}
