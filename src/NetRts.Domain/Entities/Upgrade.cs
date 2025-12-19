using NetRts.Domain.Enums;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a research improvement that enhances units/buildings.
/// </summary>
public class Upgrade
{
    public int Id { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid OwnerId { get; private set; }
    public UpgradeType UpgradeType { get; private set; }
    public int ResearchProgress { get; private set; }
    public int ResearchTicksRequired { get; private set; }
    public bool IsCompleted { get; private set; }
    public int ResourceCost { get; private set; }

    private Upgrade() { }

    public Upgrade(int id, Guid matchId, Guid ownerId, UpgradeType type)
    {
        Id = id;
        MatchId = matchId;
        OwnerId = ownerId;
        UpgradeType = type;
        ResearchProgress = 0;
        IsCompleted = false;
        (ResourceCost, ResearchTicksRequired) = GetUpgradeDetails(type);
    }

    public void AdvanceResearch(int progress)
    {
        ResearchProgress = Math.Min(100, ResearchProgress + progress);
        if (ResearchProgress >= 100)
        {
            IsCompleted = true;
        }
    }

    private static (int Cost, int Time) GetUpgradeDetails(UpgradeType type) => type switch
    {
        UpgradeType.MeleeDamage => (200, 30),
        UpgradeType.MeleeDamage2 => (400, 60),
        UpgradeType.RangedDamage => (200, 30),
        UpgradeType.RangedDamage2 => (400, 60),
        UpgradeType.ArmorUpgrade => (200, 30),
        UpgradeType.ArmorUpgrade2 => (400, 60),
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };

    public static int GetUpgradeCost(UpgradeType type)
    {
        return GetUpgradeDetails(type).Cost;
    }

    public static UpgradeType? GetPrerequisiteUpgrade(UpgradeType type) => type switch
    {
        UpgradeType.MeleeDamage2 => UpgradeType.MeleeDamage,
        UpgradeType.RangedDamage2 => UpgradeType.RangedDamage,
        UpgradeType.ArmorUpgrade2 => UpgradeType.ArmorUpgrade,
        _ => null
    };

    public static (int Damage, int Armor) GetUpgradeEffects(UpgradeType type) => type switch
    {
        UpgradeType.MeleeDamage => (5, 0),
        UpgradeType.MeleeDamage2 => (5, 0),
        UpgradeType.RangedDamage => (5, 0),
        UpgradeType.RangedDamage2 => (5, 0),
        UpgradeType.ArmorUpgrade => (0, 2),
        UpgradeType.ArmorUpgrade2 => (0, 2),
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };
}
