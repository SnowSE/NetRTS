using NetRts.Protocol;

namespace NetRts.Bots;

/// <summary>Where <see cref="BotTurn.GatherIdleWorkers"/> sends idle workers.</summary>
public enum GatherStyle
{
    /// <summary>The piles in sight right now, nearest the Command Center (the house bots).</summary>
    InSightNearCommandCenter,

    /// <summary>Every pile seen this match (they never move), nearest a finished HQ or Co-op Store (Block Bot).</summary>
    RememberedNearDropOff,
}

/// <summary>
/// One tick's work for a bot: the grubs left to spend, the commands so far, the buildings already given an
/// order and the units already used. Its actions (<see cref="TryTrain"/>, <see cref="TryBuild"/>,
/// <see cref="TryResearch"/>, ...) check what's needed, add the command, spend the grubs and return an
/// <see cref="Attempt"/> saying what happened. Make a new one every tick, after <see cref="BotMemory.Update"/>.
/// </summary>
/// <param name="view">This tick's state.</param>
/// <param name="memory">The bot's memory, already updated for this tick.</param>
/// <param name="reserveUnits">
/// True (Block Bot): a unit given an order is used for this tick, so later actions pick another. False (the
/// house bots): later actions may pick it again, and its last order wins.
/// </param>
public sealed class BotTurn(BotView view, BotMemory memory, bool reserveUnits = true)
{
    /// <summary>How often <see cref="Order"/> re-aims units still heading for a target that may have moved.</summary>
    public const int RetargetEveryTicks = 15;

    private readonly HashSet<int> _claimed = [];
    private readonly Dictionary<int, int> _queuedAt = [];
    private readonly List<CommandRequest> _commands = [];

    public BotView View { get; } = view;

    public BotMemory Memory { get; } = memory;

    /// <summary>Grubs left to spend this tick.</summary>
    public int Budget { get; private set; } = view.State.You.Resources;

    public IReadOnlyList<CommandRequest> Commands => _commands;

    private int Tick => View.State.Tick;

    public void Add(CommandRequest command) => _commands.Add(command);

    /// <summary>Whether an action this tick already used the unit (always false without reserveUnits).</summary>
    public bool IsClaimed(int unitId) => _claimed.Contains(unitId);

    public void Claim(int unitId)
    {
        if (reserveUnits)
        {
            _claimed.Add(unitId);
        }
    }

    /// <summary><see cref="Attempt.TooExpensive"/> if the budget can't cover the cost, else null.</summary>
    public Attempt? CheckBudget(int cost) => Budget < cost ? new Attempt.TooExpensive(cost, Budget) : null;

    // ------------------------------------------------------------ training

    /// <summary>Trains a unit at the least busy building that makes it (at most two queued at each).</summary>
    public Attempt TryTrain(UnitType type)
    {
        var producedAt = View.Rules.Units.First(u => u.Type == type).ProducedAt;
        var maxQueue = Math.Max(1, Math.Min(2, View.Rules.Economy.MaxProductionQueue));
        var producers = View.MyBuildings.Where(b => b.Type == producedAt && b.Completed).ToList();
        if (producers.Count == 0)
        {
            return new Attempt.NeedsBuilding(producedAt);
        }

        var building = producers
            .Where(b => b.Production.Count + _queuedAt.GetValueOrDefault(b.Id) < maxQueue)
            .OrderBy(b => b.Production.Count + _queuedAt.GetValueOrDefault(b.Id)).ThenBy(b => b.Id)
            .FirstOrDefault();
        return building is null ? new Attempt.Busy(producedAt) : TryTrainAt(building, type);
    }

    /// <summary>Trains a unit at this building if the budget allows. Fighters walk to the rally point.</summary>
    public Attempt TryTrainAt(BuildingDto building, UnitType type)
    {
        ArgumentNullException.ThrowIfNull(building);
        var cost = View.UnitCost(type);
        if (CheckBudget(cost) is { } tooExpensive)
        {
            return tooExpensive;
        }

        var command = new CommandRequest { Type = CommandType.Produce, BuildingId = building.Id, UnitType = type };
        if (type != UnitType.Worker)
        {
            var rally = View.RallyPoint();
            command = command with { X = rally.X, Y = rally.Y };
        }

        Add(command);
        Budget -= cost;
        _queuedAt[building.Id] = _queuedAt.GetValueOrDefault(building.Id) + 1;
        return new Attempt.Done();
    }

    // ------------------------------------------------------------ building

    /// <summary>
    /// Builds near the base: checks what the building needs, the budget and the room, then sends the nearest
    /// free worker. With <paramref name="oneAtATime"/>, waits while another of the type is going up.
    /// </summary>
    public Attempt TryBuild(BuildingType type, SitePreference preference, bool oneAtATime = false)
    {
        var stats = View.Rules.Buildings.First(b => b.Type == type);
        foreach (var required in stats.Requires.Where(r => !View.MyBuildings.Any(b => b.Type == r && b.Completed)))
        {
            return new Attempt.NeedsBuilding(required);
        }

        if (stats.RequiresUpgrade is { } upgrade && !View.State.You.Upgrades.Contains(upgrade))
        {
            return new Attempt.NeedsUpgrade(upgrade);
        }

        if (oneAtATime && (Memory.PendingCount(View, type) > 0 || View.MyBuildings.Any(b => b.Type == type && !b.Completed)))
        {
            return new Attempt.AlreadyGoingUp(type);
        }

        if (CheckBudget(stats.Cost) is { } tooExpensive)
        {
            return tooExpensive;
        }

        var site = View.FindBuildSite(preference, Memory.PendingSites.Keys);
        return site is null ? new Attempt.NoSite() : PlaceBuilding(type, site.Value);
    }

    /// <summary>
    /// Sends the nearest free worker to put a building on this tile and remembers the order (the budget is
    /// assumed checked). <see cref="Attempt.Done.UnitId"/> is the worker.
    /// </summary>
    public Attempt PlaceBuilding(BuildingType type, (int X, int Y) site)
    {
        var builder = PickBuilder(site);
        if (builder is null)
        {
            return new Attempt.NoBuilder();
        }

        Add(new CommandRequest { Type = CommandType.Build, UnitIds = [builder.Id], BuildingType = type, X = site.X, Y = site.Y });
        Claim(builder.Id);
        Memory.AddPendingSite(site, type, Tick);
        Budget -= View.Rules.Buildings.First(b => b.Type == type).Cost;
        return new Attempt.Done(builder.Id);
    }

    /// <summary><see cref="BotView.PickBuilder"/>, skipping workers already used this tick.</summary>
    public UnitDto? PickBuilder((int X, int Y) site) => View.PickBuilder(site, IsClaimed);

    /// <summary>Sends a worker to every half-built building nobody is working on.</summary>
    public void FinishAbandonedSites()
    {
        foreach (var site in View.MyBuildings.Where(b => !b.Completed))
        {
            if (View.Workers.Any(w => w.TargetId == site.Id || (w.Destination is { } d && d.X == site.X && d.Y == site.Y)))
            {
                continue;
            }

            var builder = PickBuilder((site.X, site.Y));
            if (builder is not null)
            {
                Claim(builder.Id);
                Add(new CommandRequest { Type = CommandType.Build, UnitIds = [builder.Id], BuildingType = site.Type, X = site.X, Y = site.Y });
            }
        }
    }

    // ------------------------------------------------------------ research

    /// <summary>Starts learning an upgrade at a free Tech Lab.</summary>
    public Attempt TryResearch(UpgradeType upgrade)
    {
        var you = View.State.You;
        if (you.Upgrades.Contains(upgrade))
        {
            return new Attempt.AlreadyHave(upgrade);
        }

        if (you.Researching.Any(r => r.Upgrade == upgrade))
        {
            return new Attempt.InProgress(upgrade);
        }

        var stats = View.Rules.Upgrades.First(u => u.Type == upgrade);
        if (stats.Requires is { } req && !you.Upgrades.Contains(req))
        {
            return new Attempt.NeedsUpgrade(req);
        }

        var labs = View.MyBuildings.Where(b => b.Type == BuildingType.TechLab && b.Completed).ToList();
        if (labs.Count == 0)
        {
            return new Attempt.NeedsBuilding(BuildingType.TechLab);
        }

        var lab = labs.FirstOrDefault(b => b.Research is null && !_queuedAt.ContainsKey(b.Id));
        if (lab is null)
        {
            return new Attempt.Busy(BuildingType.TechLab);
        }

        if (CheckBudget(stats.Cost) is { } tooExpensive)
        {
            return tooExpensive;
        }

        Add(new CommandRequest { Type = CommandType.Research, BuildingId = lab.Id, Upgrade = upgrade });
        _queuedAt[lab.Id] = 1;
        Budget -= stats.Cost;
        return new Attempt.Done();
    }

    // ------------------------------------------------------------ units

    /// <summary>
    /// Sends a group somewhere, but only re-sends units that are idle, were told something else, or (with
    /// <paramref name="retarget"/>, for a target that moves) haven't been re-aimed for a while, so the same
    /// order doesn't go out every tick. Every unit in the group counts as used this tick.
    /// </summary>
    public void Order(IReadOnlyList<UnitDto> group, CommandType type, (int X, int Y) spot, bool retarget)
    {
        ArgumentNullException.ThrowIfNull(group);
        var ids = new List<int>();
        foreach (var u in group)
        {
            Claim(u.Id);
            var fresh = Memory.LastOrders.TryGetValue(u.Id, out var last)
                        && last.Type == type && last.X == spot.X && last.Y == spot.Y
                        && u.Activity != UnitActivity.Idle
                        && (!retarget || Tick - last.Tick < RetargetEveryTicks);
            if (!fresh)
            {
                ids.Add(u.Id);
                Memory.LastOrders[u.Id] = new LastOrder(type, spot.X, spot.Y, Tick);
            }
        }

        if (ids.Count > 0)
        {
            Add(new CommandRequest { Type = type, UnitIds = [.. ids], X = spot.X, Y = spot.Y });
        }
    }

    /// <summary>Sends every idle worker not used this tick to dig, spreading them over the piles.</summary>
    public void GatherIdleWorkers(GatherStyle style)
    {
        var assigned = View.Workers
            .Where(w => w.TargetId is not null && w.Activity != UnitActivity.Idle)
            .GroupBy(w => w.TargetId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        List<KnownDeposit> piles;
        Func<KnownDeposit, int> haul;
        if (style == GatherStyle.InSightNearCommandCenter)
        {
            var cc = View.CommandCenter!;
            piles = View.State.Resources.Select(d => new KnownDeposit(d.Id, d.X, d.Y, d.Remaining)).ToList();
            haul = d => BotView.Dist(d.X, d.Y, cc.X, cc.Y);
        }
        else
        {
            // The engine takes orders for piles in the fog, so remembered ones count too.
            var dropOffs = View.MyBuildings.Where(b => b.Type is BuildingType.CommandCenter or BuildingType.ResourceDepot && b.Completed).ToList();
            if (dropOffs.Count == 0)
            {
                return;
            }

            piles = Memory.Deposits.Where(d => d.Remaining > 0).ToList();
            haul = d => dropOffs.Min(b => BotView.Dist(b.X, b.Y, d.X, d.Y));
        }

        foreach (var worker in View.Workers.Where(w => w.Activity == UnitActivity.Idle && !_claimed.Contains(w.Id)))
        {
            if (piles.Count == 0)
            {
                return;
            }

            var deposit = piles
                .OrderBy(d => haul(d) + (3 * assigned.GetValueOrDefault(d.Id)))
                .ThenBy(d => d.Id)
                .First();
            Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [worker.Id], TargetId = deposit.Id });
            assigned[deposit.Id] = assigned.GetValueOrDefault(deposit.Id) + 1;
        }
    }
}
