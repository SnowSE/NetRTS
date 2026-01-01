using NetRts.Domain.Enums;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a research upgrade that enhances unit or building capabilities.
/// </summary>
public class Upgrade
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Parent match ID.
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>
    /// Player who owns this upgrade.
    /// </summary>
    public Guid PlayerId { get; private set; }

    /// <summary>
    /// Type of upgrade.
    /// </summary>
    public UpgradeType Type { get; private set; }

    /// <summary>
    /// Research progress percentage (0-100).
    /// </summary>
    public int ResearchProgress { get; private set; }

    /// <summary>
    /// True when ResearchProgress == 100.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// Tick when research started.
    /// </summary>
    public int StartedAtTick { get; private set; }

    // EF Core constructor
    private Upgrade() { }

    public Upgrade(int id, Guid matchId, Guid playerId, UpgradeType type, int currentTick)
    {
        Id = id;
        MatchId = matchId;
        PlayerId = playerId;
        Type = type;
        ResearchProgress = 0;
        IsComplete = false;
        StartedAtTick = currentTick;
    }

    /// <summary>
    /// Advance research progress.
    /// </summary>
    public void AdvanceResearch(int progressAmount)
    {
        if (IsComplete)
        {
            return; // Already complete
        }

        ResearchProgress = Math.Min(100, ResearchProgress + progressAmount);

        if (ResearchProgress >= 100)
        {
            IsComplete = true;
        }
    }

    /// <summary>
    /// Get resource cost for this upgrade type.
    /// </summary>
    public static int GetUpgradeCost(UpgradeType type) => type switch
    {
        UpgradeType.WeaponDamage1 => 100,
        UpgradeType.WeaponDamage2 => 200,
        UpgradeType.Armor1 => 100,
        UpgradeType.Armor2 => 200,
        UpgradeType.Speed1 => 150,
        UpgradeType.Speed2 => 250,
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };

    /// <summary>
    /// Get research time in ticks for this upgrade type.
    /// </summary>
    public static int GetResearchTime(UpgradeType type) => type switch
    {
        UpgradeType.WeaponDamage1 => 15,
        UpgradeType.WeaponDamage2 => 20,
        UpgradeType.Armor1 => 15,
        UpgradeType.Armor2 => 20,
        UpgradeType.Speed1 => 18,
        UpgradeType.Speed2 => 23,
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };

    /// <summary>
    /// Get upgrade effects (damage bonus, health bonus, speed bonus).
    /// </summary>
    public static (int damageBonus, int healthBonus, int speedBonus) GetUpgradeEffects(UpgradeType type) => type switch
    {
        UpgradeType.WeaponDamage1 => (5, 0, 0),
        UpgradeType.WeaponDamage2 => (5, 0, 0),
        UpgradeType.Armor1 => (0, 20, 0),
        UpgradeType.Armor2 => (0, 20, 0),
        UpgradeType.Speed1 => (0, 0, 1),
        UpgradeType.Speed2 => (0, 0, 1),
        _ => throw new ArgumentException($"Unknown upgrade type: {type}")
    };

    /// <summary>
    /// Check if prerequisites are met for this upgrade type.
    /// </summary>
    public static bool CheckPrerequisites(UpgradeType type, List<Upgrade> completedUpgrades)
    {
        return type switch
        {
            UpgradeType.WeaponDamage2 => completedUpgrades.Any(u => u.Type == UpgradeType.WeaponDamage1 && u.IsComplete),
            UpgradeType.Armor2 => completedUpgrades.Any(u => u.Type == UpgradeType.Armor1 && u.IsComplete),
            UpgradeType.Speed2 => completedUpgrades.Any(u => u.Type == UpgradeType.Speed1 && u.IsComplete),
            _ => true // Tier 1 upgrades have no prerequisites
        };
    }
}
