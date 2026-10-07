using NetRts.Protocol;

namespace NetRts.Engine;

internal enum OrderKind
{
    Idle,
    Move,
    AttackMove,
    AttackTarget,
    Gather,
    Build,
}

internal sealed class PlayerState(Guid id, int slot, string name)
{
    public Guid Id { get; } = id;
    public int Slot { get; } = slot;
    public string Name { get; } = name;
    public int Resources { get; set; } = GameRules.StartingResources;
    public bool Eliminated { get; set; }
    public HashSet<UpgradeType> Upgrades { get; } = [];
    public Queue<QueuedCommand> CommandQueue { get; } = new();

    /// <summary>Ore banked per tick for the last 60 ticks, indexed by tick % 60.</summary>
    public int[] IncomeWindow { get; } = new int[60];

    /// <summary>Enemy buildings as they looked when this player last saw them, by id.</summary>
    public SortedDictionary<int, RememberedBuildingDto> Remembered { get; } = [];
    public List<GameEventDto> Events { get; } = [];

    public int Destruction { get; set; }
    public int Gathered { get; set; }
    public int UnitsProduced { get; set; }
    public int UnitsLost { get; set; }
    public int UnitsKilled { get; set; }
    public int BuildingsLost { get; set; }
    public int BuildingsDestroyed { get; set; }

    public int Tier(UpgradeType tier1, UpgradeType tier2) =>
        (Upgrades.Contains(tier1) ? 1 : 0) + (Upgrades.Contains(tier2) ? 1 : 0);

    public int WeaponsBonus => Tier(UpgradeType.Weapons1, UpgradeType.Weapons2) * GameRules.WeaponsBonusPerTier;
    public int ArmorBonus => Tier(UpgradeType.Armor1, UpgradeType.Armor2) * GameRules.ArmorBonusPerTier;
    public int MobilityBonus => Tier(UpgradeType.Mobility1, UpgradeType.Mobility2) * GameRules.MobilityBonusPerTier;
    public int HarvestingBonus => Tier(UpgradeType.Harvesting1, UpgradeType.Harvesting2) * GameRules.HarvestingBonusPerTier;
}

internal sealed record QueuedCommand(long Id, CommandRequest Request);

internal abstract class Entity(int id, int owner, Point position)
{
    public int Id { get; } = id;

    /// <summary>Owning player slot; -1 for neutral (resource deposits).</summary>
    public int Owner { get; } = owner;

    public Point Position { get; set; } = position;
    public int Hp { get; set; }
    public abstract int MaxHp { get; }
    public bool Dead => Hp <= 0;
}

internal sealed class UnitEntity : Entity
{
    public UnitEntity(int id, int owner, UnitType type, Point position)
        : base(id, owner, position)
    {
        Type = type;
        Stats = GameRules.Units[type];
        Hp = Stats.MaxHp;
    }

    public UnitType Type { get; }
    public UnitStats Stats { get; }
    public override int MaxHp => Stats.MaxHp;

    public OrderKind Order { get; set; }
    public UnitActivity Activity { get; set; }
    public Point? Destination { get; set; }
    public int? TargetId { get; set; }

    /// <summary>Id of the command that gave the current order, for failure events.</summary>
    public long? OrderCommandId { get; set; }
    public BuildingType? BuildType { get; set; }

    /// <summary>Where the last mined deposit was, so workers can move on to a neighbouring one.</summary>
    public Point? LastDepositPosition { get; set; }

    public int Carrying { get; set; }
    public int Cooldown { get; set; }
    public int MovePoints { get; set; }

    public List<Point> Path { get; set; } = [];
    public Point? PathGoal { get; set; }
    public int PathRange { get; set; }

    public void ClearOrder()
    {
        Order = OrderKind.Idle;
        Activity = UnitActivity.Idle;
        Destination = null;
        TargetId = null;
        BuildType = null;
        OrderCommandId = null;
        ClearPath();
    }

    public void ClearPath()
    {
        Path = [];
        PathGoal = null;
        MovePoints = 0;
    }
}

internal sealed class BuildingEntity : Entity
{
    public BuildingEntity(int id, int owner, BuildingType type, Point position, bool completed)
        : base(id, owner, position)
    {
        Type = type;
        Stats = GameRules.Buildings[type];
        Completed = completed;
        Work = completed ? Stats.BuildWork : 0;
        Hp = completed ? Stats.MaxHp : HpForWork(0);
    }

    public BuildingType Type { get; }
    public BuildingStats Stats { get; }
    public override int MaxHp => Stats.MaxHp;
    public bool Completed { get; private set; }
    public int Work { get; private set; }
    public int Cooldown { get; set; }

    public List<ProductionItem> Production { get; } = [];

    /// <summary>Where newly trained units go.</summary>
    public Point? Rally { get; set; }
    public ResearchItem? Research { get; set; }

    public int ConstructionPercent => Completed ? 100 : Work * 100 / Stats.BuildWork;

    /// <summary>Adds construction work; returns true when this completes the building.</summary>
    public bool AddWork(int amount)
    {
        if (Completed)
        {
            return false;
        }

        var before = HpForWork(Work);
        Work = Math.Min(Stats.BuildWork, Work + amount);
        Hp += HpForWork(Work) - before;
        if (Work >= Stats.BuildWork)
        {
            Completed = true;
            return true;
        }

        return false;
    }

    // Sites start at 10% health and gain the rest as they are built; damage taken is kept.
    private int HpForWork(int work) => Stats.MaxHp / 10 + Stats.MaxHp * 9 / 10 * work / Stats.BuildWork;
}

internal sealed class ProductionItem(UnitType type)
{
    public UnitType Type { get; } = type;
    public int Progress { get; set; }
}

internal sealed class ResearchItem(UpgradeType type)
{
    public UpgradeType Type { get; } = type;
    public int Progress { get; set; }
}

internal sealed class DepositEntity : Entity
{
    public DepositEntity(int id, Point position, int amount)
        : base(id, -1, position)
    {
        Hp = amount;
        Initial = amount;
    }

    public int Initial { get; }
    public int Remaining => Hp;
    public override int MaxHp => Initial;
}
