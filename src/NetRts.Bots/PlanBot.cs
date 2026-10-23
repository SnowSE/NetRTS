using NetRts.Protocol;

namespace NetRts.Bots;

public enum SitePreference
{
    TowardCenter,
    NearOre,
    TowardEnemy,
}

public sealed record BuildStep(BuildingType Type, int Count, int MinWorkers, SitePreference Site = SitePreference.TowardCenter);

/// <summary>A bot personality: what to build, in which order, and when to fight.</summary>
public sealed record BotPlan
{
    public int TargetWorkers { get; init; } = 12;
    public IReadOnlyList<BuildStep> BuildOrder { get; init; } = [];
    public IReadOnlyList<UpgradeType> Upgrades { get; init; } = [];
    public IReadOnlyList<UnitType> ArmyMix { get; init; } = [UnitType.Soldier];

    /// <summary>Attack once the army is at least this big (0 = never attack).</summary>
    public int AttackThreshold { get; init; } = 10;

    /// <summary>Attack with whatever we have from this tick on, regardless of army size.</summary>
    public int AllInTick { get; init; } = int.MaxValue;

    /// <summary>Distance from our buildings at which enemies count as a threat.</summary>
    public int DefenseRadius { get; init; } = 10;

    /// <summary>
    /// Trebuchets to keep (0 = none). Until they're built, money goes to them first and the army only
    /// grows to <see cref="AttackThreshold"/>; the attack waits for them, and the rest of the army
    /// escorts them in a ring.
    /// </summary>
    public int SiegeCount { get; init; }
}

/// <summary>
/// Plan-driven bot used by all the house bots. It is deliberately straightforward so it doubles
/// as a readable example of using the API: count what you have, spend what you can, fight when ready.
/// </summary>
public class PlanBot(string name, BotPlan plan) : IBotStrategy
{
    private const int RetargetEveryTicks = 15;
    private const int EscortEveryTicks = 3;

    // Where escorts stand around the trebuchets: the eight neighbours two tiles out, then three.
    private static readonly (int X, int Y)[] Ring =
    [
        (2, 0), (-2, 0), (0, 2), (0, -2), (2, 2), (-2, -2), (2, -2), (-2, 2),
        (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3), (3, -3), (-3, 3),
    ];

    // Build orders we issued whose building hasn't appeared yet: site -> (type, tick issued).
    private readonly Dictionary<(int X, int Y), (BuildingType Type, int Tick)> _pendingSites = [];
    private int _armyCursor;
    private bool _attacking;
    private int _lastRetargetTick = -RetargetEveryTicks;

    public string Name { get; } = name;

    public BotPlan Plan { get; } = plan;

    public IReadOnlyList<CommandRequest> Decide(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new BotView(context);
        if (view.State.Status != MatchStatus.Active || view.CommandCenter is null)
        {
            return [];
        }

        var commands = new List<CommandRequest>();
        var budget = view.State.You.Resources;

        Defend(view, commands);
        TrainWorkers(view, commands, ref budget);
        FollowBuildOrder(view, commands, ref budget);
        FinishAbandonedSites(view, commands);
        Research(view, commands, ref budget);
        TrainSiege(view, commands, ref budget);
        TrainArmy(view, commands, ref budget);
        CommandArmy(view, commands);
        PutIdleWorkersToWork(view, commands);

        return commands;
    }

    private void Defend(BotView view, List<CommandRequest> commands)
    {
        var threat = view.EnemyUnits
            .Where(e => view.MyBuildings.Any(b => Dist(b.X, b.Y, e.X, e.Y) <= Plan.DefenseRadius))
            .OrderBy(e => Dist(view.CommandCenter!.X, view.CommandCenter.Y, e.X, e.Y))
            .FirstOrDefault();
        if (threat is null)
        {
            return;
        }

        var responders = view.Army.Where(u => u.Activity is UnitActivity.Idle or UnitActivity.Moving && !_attacking).Select(u => u.Id).ToArray();
        if (responders.Length > 0)
        {
            commands.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = responders, X = threat.X, Y = threat.Y });
        }
    }

    private void TrainWorkers(BotView view, List<CommandRequest> commands, ref int budget)
    {
        var cost = view.UnitCost(UnitType.Worker);
        foreach (var cc in view.MyBuildings.Where(b => b.Type == BuildingType.CommandCenter && b.Completed))
        {
            var queued = view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == UnitType.Worker));
            if (view.Workers.Count + queued >= Plan.TargetWorkers || cc.Production.Count >= 2 || budget < cost)
            {
                continue;
            }

            commands.Add(new CommandRequest { Type = CommandType.Produce, BuildingId = cc.Id, UnitType = UnitType.Worker });
            budget -= cost;
        }
    }

    private void FollowBuildOrder(BotView view, List<CommandRequest> commands, ref int budget)
    {
        foreach (var site in _pendingSites.Where(kv => view.State.Tick - kv.Value.Tick > 40).Select(kv => kv.Key).ToList())
        {
            _pendingSites.Remove(site);
        }

        foreach (var step in Plan.BuildOrder)
        {
            var have = view.MyBuildings.Count(b => b.Type == step.Type) + PendingCount(view, step.Type);
            if (have >= step.Count)
            {
                continue;
            }

            // Steps are a strict order: if this one can't happen yet, save up for it.
            var stats = view.Rules.Buildings.First(b => b.Type == step.Type);
            var prerequisitesMet = stats.Requires.All(r => view.MyBuildings.Any(b => b.Type == r && b.Completed))
                                   && (stats.RequiresUpgrade is not { } upgrade || view.State.You.Upgrades.Contains(upgrade));
            if (view.Workers.Count < step.MinWorkers || !prerequisitesMet || budget < stats.Cost)
            {
                return;
            }

            var site = view.FindBuildSite(step.Site, _pendingSites.Keys);
            var builder = site is null ? null : view.PickBuilder(site.Value);
            if (site is null || builder is null)
            {
                return;
            }

            commands.Add(new CommandRequest
            {
                Type = CommandType.Build,
                UnitIds = [builder.Id],
                BuildingType = step.Type,
                X = site.Value.X,
                Y = site.Value.Y,
            });
            _pendingSites[site.Value] = (step.Type, view.State.Tick);
            budget -= stats.Cost;
            return;
        }
    }

    private int PendingCount(BotView view, BuildingType type) =>
        _pendingSites.Count(kv => kv.Value.Type == type
                                  && !view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y));

    private static void FinishAbandonedSites(BotView view, List<CommandRequest> commands)
    {
        foreach (var site in view.MyBuildings.Where(b => !b.Completed))
        {
            if (view.Workers.Any(w => w.TargetId == site.Id || (w.Destination is { } d && d.X == site.X && d.Y == site.Y)))
            {
                continue;
            }

            var builder = view.PickBuilder((site.X, site.Y));
            if (builder is not null)
            {
                commands.Add(new CommandRequest
                {
                    Type = CommandType.Build,
                    UnitIds = [builder.Id],
                    BuildingType = site.Type,
                    X = site.X,
                    Y = site.Y,
                });
            }
        }
    }

    private void Research(BotView view, List<CommandRequest> commands, ref int budget)
    {
        var lab = view.MyBuildings.FirstOrDefault(b => b.Type == BuildingType.TechLab && b.Completed && b.Research is null);
        if (lab is null)
        {
            return;
        }

        var you = view.State.You;
        foreach (var upgrade in Plan.Upgrades)
        {
            if (you.Upgrades.Contains(upgrade) || you.Researching.Any(r => r.Upgrade == upgrade))
            {
                continue;
            }

            var stats = view.Rules.Upgrades.First(u => u.Type == upgrade);
            if (stats.Requires is { } req && !you.Upgrades.Contains(req))
            {
                continue;
            }

            if (budget >= stats.Cost)
            {
                commands.Add(new CommandRequest { Type = CommandType.Research, BuildingId = lab.Id, Upgrade = upgrade });
                budget -= stats.Cost;
            }

            return;
        }
    }

    /// <summary>Siege still to come: fewer trebuchets alive or in production than the plan wants.</summary>
    private bool SiegePending(BotView view) =>
        Plan.SiegeCount > 0
        && view.Siege.Count + view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == UnitType.Trebuchet)) < Plan.SiegeCount;

    private void TrainSiege(BotView view, List<CommandRequest> commands, ref int budget)
    {
        if (!SiegePending(view))
        {
            return;
        }

        var workshop = view.MyBuildings.FirstOrDefault(b => b.Type == BuildingType.SiegeWorkshop && b.Completed && b.Production.Count == 0);
        var cost = view.UnitCost(UnitType.Trebuchet);
        if (workshop is null || budget < cost)
        {
            return;
        }

        var rally = view.RallyPoint();
        commands.Add(new CommandRequest { Type = CommandType.Produce, BuildingId = workshop.Id, UnitType = UnitType.Trebuchet, X = rally.X, Y = rally.Y });
        budget -= cost;
    }

    private void TrainArmy(BotView view, List<CommandRequest> commands, ref int budget)
    {
        if (Plan.ArmyMix.Count == 0)
        {
            return;
        }

        // Saving for siege: keep just enough of an army to defend with, and to escort it.
        if (SiegePending(view) && view.Escorts.Count >= Plan.AttackThreshold)
        {
            return;
        }

        foreach (var barracks in view.MyBuildings.Where(b => b.Type == BuildingType.Barracks && b.Completed && b.Production.Count < 2))
        {
            var type = Plan.ArmyMix[_armyCursor % Plan.ArmyMix.Count];
            var cost = view.UnitCost(type);
            if (budget < cost)
            {
                return;
            }

            // New units walk straight to where the army gathers.
            var rally = view.RallyPoint();
            commands.Add(new CommandRequest { Type = CommandType.Produce, BuildingId = barracks.Id, UnitType = type, X = rally.X, Y = rally.Y });
            budget -= cost;
            _armyCursor++;
        }
    }

    private void CommandArmy(BotView view, List<CommandRequest> commands)
    {
        if (Plan.SiegeCount > 0)
        {
            CommandSiegeArmy(view, commands);
            return;
        }

        var army = view.Army;
        var tick = view.State.Tick;

        if (!_attacking && Plan.AttackThreshold > 0 && (army.Count >= Plan.AttackThreshold || (tick >= Plan.AllInTick && army.Count > 0)))
        {
            _attacking = true;
            _lastRetargetTick = -RetargetEveryTicks;
        }
        else if (_attacking && tick < Plan.AllInTick && army.Count < Math.Max(2, Plan.AttackThreshold / 3))
        {
            _attacking = false; // wave is spent; regroup
        }

        if (_attacking)
        {
            var target = view.AttackTarget();
            if (target is null)
            {
                return;
            }

            var retarget = tick - _lastRetargetTick >= RetargetEveryTicks;
            var ids = army.Where(u => retarget || u.Activity == UnitActivity.Idle).Select(u => u.Id).ToArray();
            if (ids.Length > 0)
            {
                commands.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = ids, X = target.Value.X, Y = target.Value.Y });
            }

            if (retarget)
            {
                _lastRetargetTick = tick;
            }

            return;
        }

        var rally = view.RallyPoint();
        var stragglers = army
            .Where(u => u.Activity == UnitActivity.Idle && Dist(u.X, u.Y, rally.X, rally.Y) > 3)
            .Select(u => u.Id)
            .ToArray();
        if (stragglers.Length > 0)
        {
            commands.Add(new CommandRequest { Type = CommandType.Move, UnitIds = stragglers, X = rally.X, Y = rally.Y });
        }
    }

    /// <summary>
    /// The siege push: wait at the rally point until every trebuchet is out and the escort is big
    /// enough, then roll out. The trebuchets attack-move on the target (they stop to shell anything in
    /// range); the escorts attack-move to spots in a ring around them, a step ahead, so they meet whatever
    /// comes at the siege and fall back in beside it. When the trebuchets are gone, the rest come home.
    /// </summary>
    private void CommandSiegeArmy(BotView view, List<CommandRequest> commands)
    {
        var tick = view.State.Tick;
        var siege = view.Siege;
        var escorts = view.Escorts;

        if (!_attacking && siege.Count >= Plan.SiegeCount && escorts.Count >= Plan.AttackThreshold)
        {
            _attacking = true;
            _lastRetargetTick = -RetargetEveryTicks;
        }
        else if (_attacking && siege.Count == 0)
        {
            _attacking = false; // the siege is lost; regroup and build more
        }

        var target = _attacking ? view.AttackTarget() : null;
        if (target is null)
        {
            var rally = view.RallyPoint();
            var stragglers = view.Army
                .Where(u => u.Activity == UnitActivity.Idle && Dist(u.X, u.Y, rally.X, rally.Y) > 3)
                .Select(u => u.Id)
                .ToArray();
            if (stragglers.Length > 0)
            {
                commands.Add(new CommandRequest { Type = CommandType.Move, UnitIds = stragglers, X = rally.X, Y = rally.Y });
            }

            return;
        }

        if (tick - _lastRetargetTick >= RetargetEveryTicks || siege.Any(u => u.Activity == UnitActivity.Idle))
        {
            commands.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = siege.Select(u => u.Id).ToArray(), X = target.Value.X, Y = target.Value.Y });
            _lastRetargetTick = tick;
        }

        if (tick % EscortEveryTicks != 0)
        {
            return;
        }

        // The ring is centred a step ahead of the trebuchets, toward the target.
        var cx = (int)Math.Round(siege.Average(u => u.X));
        var cy = (int)Math.Round(siege.Average(u => u.Y));
        cx += Math.Sign(target.Value.X - cx);
        cy += Math.Sign(target.Value.Y - cy);
        for (var i = 0; i < escorts.Count; i++)
        {
            var escort = escorts[i];
            var (dx, dy) = Ring[i % Ring.Length];
            var (x, y) = (Math.Clamp(cx + dx, 0, view.Map.Width - 1), Math.Clamp(cy + dy, 0, view.Map.Height - 1));

            // An escort already fighting near the siege keeps at it; one that wandered off or is idle falls in.
            var nearSiege = Dist(escort.X, escort.Y, cx, cy) <= 4;
            if (escort.Activity == UnitActivity.Attacking && nearSiege
                || escort.Destination is { } d && d.X == x && d.Y == y && escort.Activity != UnitActivity.Idle)
            {
                continue;
            }

            if (escort.Activity == UnitActivity.Idle && escort.X == x && escort.Y == y)
            {
                continue;
            }

            commands.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = [escort.Id], X = x, Y = y });
        }
    }

    private static void PutIdleWorkersToWork(BotView view, List<CommandRequest> commands)
    {
        var assigned = view.Workers
            .Where(w => w.TargetId is not null && w.Activity != UnitActivity.Idle)
            .GroupBy(w => w.TargetId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var worker in view.Workers.Where(w => w.Activity == UnitActivity.Idle))
        {
            var deposit = view.State.Resources
                .OrderBy(d => Dist(d.X, d.Y, view.CommandCenter!.X, view.CommandCenter.Y) + 3 * assigned.GetValueOrDefault(d.Id))
                .ThenBy(d => d.Id)
                .FirstOrDefault();
            if (deposit is null)
            {
                return;
            }

            commands.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [worker.Id], TargetId = deposit.Id });
            assigned[deposit.Id] = assigned.GetValueOrDefault(deposit.Id) + 1;
        }
    }

    internal static int Dist(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
