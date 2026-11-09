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
/// <para>
/// The parts any bot needs are shared, so a new bot can reuse them: <see cref="BotView"/> answers questions
/// about this tick (where to rally, where to build, what threatens the base), <see cref="BotMemory"/>
/// remembers between ticks (buildings ordered, grub piles seen), and <see cref="BotTurn"/> holds this tick's
/// budget and commands and does the common actions (TryTrain, TryBuild, TryResearch, gathering, finishing
/// half-built buildings), each returning an <see cref="Attempt"/>. Block Bot runs on the same pieces.
/// </para>
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

    // Build orders are kept for their full 40 ticks, even once the building appears.
    private readonly BotMemory _memory = new(forgetSitesOnceBuilt: false);
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

        _memory.Update(view);

        // The house bots don't reserve units: a worker picked twice in one tick takes its last order.
        var turn = new BotTurn(view, _memory, reserveUnits: false);

        Defend(view, turn);
        TrainWorkers(view, turn);
        FollowBuildOrder(view, turn);
        turn.FinishAbandonedSites();
        Research(turn);
        TrainSiege(view, turn);
        TrainArmy(view, turn);
        CommandArmy(view, turn);
        turn.GatherIdleWorkers(GatherStyle.InSightNearCommandCenter);

        return turn.Commands;
    }

    private void Defend(BotView view, BotTurn turn)
    {
        var threat = view.Threat(Plan.DefenseRadius);
        if (threat is null)
        {
            return;
        }

        var responders = view.Army.Where(u => u.Activity is UnitActivity.Idle or UnitActivity.Moving && !_attacking).Select(u => u.Id).ToArray();
        if (responders.Length > 0)
        {
            turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = responders, X = threat.X, Y = threat.Y });
        }
    }

    private void TrainWorkers(BotView view, BotTurn turn)
    {
        foreach (var cc in view.MyBuildings.Where(b => b.Type == BuildingType.CommandCenter && b.Completed))
        {
            var queued = view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == UnitType.Worker));
            if (view.Workers.Count + queued >= Plan.TargetWorkers || cc.Production.Count >= 2)
            {
                continue;
            }

            turn.TryTrainAt(cc, UnitType.Worker);
        }
    }

    private void FollowBuildOrder(BotView view, BotTurn turn)
    {
        foreach (var step in Plan.BuildOrder)
        {
            var have = view.MyBuildings.Count(b => b.Type == step.Type) + _memory.PendingCount(view, step.Type);
            if (have >= step.Count)
            {
                continue;
            }

            // Steps are a strict order: if this one can't happen yet, save up for it.
            if (view.Workers.Count >= step.MinWorkers)
            {
                turn.TryBuild(step.Type, step.Site);
            }

            return;
        }
    }

    private void Research(BotTurn turn)
    {
        // In plan order, skipping what's learned, being learned or still locked; wait for the first other one.
        foreach (var upgrade in Plan.Upgrades)
        {
            if (turn.TryResearch(upgrade) is not (Attempt.AlreadyHave or Attempt.InProgress or Attempt.NeedsUpgrade))
            {
                return;
            }
        }
    }

    /// <summary>Siege still to come: fewer trebuchets alive or in production than the plan wants.</summary>
    private bool SiegePending(BotView view) =>
        Plan.SiegeCount > 0
        && view.Siege.Count + view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == UnitType.Trebuchet)) < Plan.SiegeCount;

    private void TrainSiege(BotView view, BotTurn turn)
    {
        if (!SiegePending(view))
        {
            return;
        }

        var workshop = view.MyBuildings.FirstOrDefault(b => b.Type == BuildingType.SiegeWorkshop && b.Completed && b.Production.Count == 0);
        if (workshop is not null)
        {
            turn.TryTrainAt(workshop, UnitType.Trebuchet);
        }
    }

    private void TrainArmy(BotView view, BotTurn turn)
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

        // New units walk straight to where the army gathers.
        foreach (var barracks in view.MyBuildings.Where(b => b.Type == BuildingType.Barracks && b.Completed && b.Production.Count < 2))
        {
            if (!turn.TryTrainAt(barracks, Plan.ArmyMix[_armyCursor % Plan.ArmyMix.Count]).Succeeded)
            {
                return;
            }

            _armyCursor++;
        }
    }

    private void CommandArmy(BotView view, BotTurn turn)
    {
        if (Plan.SiegeCount > 0)
        {
            CommandSiegeArmy(view, turn);
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
                turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = ids, X = target.Value.X, Y = target.Value.Y });
            }

            if (retarget)
            {
                _lastRetargetTick = tick;
            }

            return;
        }

        GatherAtRallyPoint(view, turn);
    }

    /// <summary>Idle fighters that wandered off walk back to the rally point.</summary>
    private static void GatherAtRallyPoint(BotView view, BotTurn turn)
    {
        var stragglers = view.AwayFromRally(view.Army.Where(u => u.Activity == UnitActivity.Idle)).Select(u => u.Id).ToArray();
        if (stragglers.Length > 0)
        {
            var rally = view.RallyPoint();
            turn.Add(new CommandRequest { Type = CommandType.Move, UnitIds = stragglers, X = rally.X, Y = rally.Y });
        }
    }

    /// <summary>
    /// The siege push: wait at the rally point until every trebuchet is out and the escort is big
    /// enough, then roll out. The trebuchets attack-move on the target (they stop to shell anything in
    /// range); the escorts attack-move to spots in a ring around them, a step ahead, so they meet whatever
    /// comes at the siege and fall back in beside it. When the trebuchets are gone, the rest come home.
    /// </summary>
    private void CommandSiegeArmy(BotView view, BotTurn turn)
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
            GatherAtRallyPoint(view, turn);
            return;
        }

        if (tick - _lastRetargetTick >= RetargetEveryTicks || siege.Any(u => u.Activity == UnitActivity.Idle))
        {
            turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = siege.Select(u => u.Id).ToArray(), X = target.Value.X, Y = target.Value.Y });
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
            var nearSiege = BotView.Dist(escort.X, escort.Y, cx, cy) <= 4;
            if (escort.Activity == UnitActivity.Attacking && nearSiege
                || escort.Destination is { } d && d.X == x && d.Y == y && escort.Activity != UnitActivity.Idle)
            {
                continue;
            }

            if (escort.Activity == UnitActivity.Idle && escort.X == x && escort.Y == y)
            {
                continue;
            }

            turn.Add(new CommandRequest { Type = CommandType.Attack, UnitIds = [escort.Id], X = x, Y = y });
        }
    }
}
