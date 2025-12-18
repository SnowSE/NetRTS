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
    public UpgradeType Type { get; private set; }
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
        Type = type;
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
        UpgradeType.WeaponDamage1 => (200, 30),
        UpgradeType.WeaponDamage2 => (400, 60),
        UpgradeType.Armor1 => (200, 30),
        UpgradeType.Armor2 => (400, 60),
        UpgradeType.Speed1 => (150, 25),
        UpgradeType.Speed2 => (300, 50),
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };

    public static (int Damage, int Health, int Speed) GetUpgradeEffects(UpgradeType type) => type switch
    {
        UpgradeType.WeaponDamage1 => (5, 0, 0),
        UpgradeType.WeaponDamage2 => (10, 0, 0),
        UpgradeType.Armor1 => (0, 20, 0),
        UpgradeType.Armor2 => (0, 40, 0),
        UpgradeType.Speed1 => (0, 0, 1),
        UpgradeType.Speed2 => (0, 0, 1),
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };
}
