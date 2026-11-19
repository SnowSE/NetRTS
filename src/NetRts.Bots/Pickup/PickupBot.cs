using NetRts.Protocol;

namespace NetRts.Bots.Pickup;

/// <summary>
/// Plays a pick-up plan (#/pickup): the steps once each, in order (a step starts when the one before it is
/// done), then the Forever cards over and over. "Up to N" steps marked Keep replace losses before later steps
/// run. The attack fires once its trigger is met after the treaty and keeps going; the phone's Attack now
/// and Pull back buttons override it. Like Block Bot, it stands on <see cref="BotView"/>, <see cref="BotMemory"/>
/// and <see cref="BotTurn"/>, and idle workers dig and half-built buildings get finished on their own.
/// </summary>
public sealed class PickupBot(string name, PickupPlanDto plan, int treatyTicks, int tickMs = 100) : IBotStrategy
{
    private PickupPlanDto _plan = plan;
    private const int DefenseRadius = 10;
    private const int ForeverRecheckTicks = 50;
    private const int MaxWorkers = 30;

    private readonly int _ticksPerSecond = Math.Max(1, 1000 / tickMs);
    private readonly object _lock = new();
    private readonly BotMemory _memory = new();
    private readonly List<PickupStepDto> _kept = [];
    private readonly HashSet<int> _fighters = [];
    private HashSet<int>? _startFighters;
    private int _step;
    private int _stepBase = -1;
    private int _forever;
    private int _foreverBase = -1;
    private int _foreverSkipped;
    private bool _attacking;
    private bool _attackNow;
    private int _holdUntil;
    private int _tick;

    public string Name { get; } = name;

    /// <summary>The step being worked on: 0 to the number of steps (that many: on the Forever cards).</summary>
    public int CurrentStep
    {
        get
        {
            lock (_lock)
            {
                return _step;
            }
        }
    }

    /// <summary>Fighters trained during the match (not the ones it started with).</summary>
    public int Trained
    {
        get
        {
            lock (_lock)
            {
                return _fighters.Count - (_startFighters?.Count ?? 0);
            }
        }
    }

    /// <summary>Fighters it started the match with.</summary>
    public int StartFighters
    {
        get
        {
            lock (_lock)
            {
                return _startFighters?.Count ?? 0;
            }
        }
    }

    /// <summary>Attack from the next tick on (when the treaty ends, if it hasn't).</summary>
    public void AttackNow()
    {
        lock (_lock)
        {
            _attackNow = true;
        }
    }

    /// <summary>
    /// A new plan from the phone while the match runs. Steps already done stay done (the step being worked on
    /// carries on, by its place in the list), new steps join the queue, the Forever cards start over and the
    /// attack follows the new settings. The start pile is ignored: it was spent at tick 0.
    /// </summary>
    public void Apply(PickupPlanDto next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_lock)
        {
            _plan = next;
            _step = Math.Min(_step, next.Steps.Count);
            _stepBase = -1;
            _kept.Clear();
            _kept.AddRange(next.Steps.Take(_step).Where(s => s.Keep && s.Kind is "train" or "workers"));
            _forever = 0;
            _foreverBase = -1;
            _foreverSkipped = 0;
        }
    }

    /// <summary>Bring the fighters home; the automatic attack waits 15 seconds before it can fire again.</summary>
    public void PullBack()
    {
        lock (_lock)
        {
            _attacking = false;
            _attackNow = false;
            _holdUntil = _tick + (15 * _ticksPerSecond);
        }
    }

    public IReadOnlyList<CommandRequest> Decide(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new BotView(context);
        if (view.State.Status != MatchStatus.Active || view.CommandCenter is null)
        {
            return [];
        }

        _memory.Update(view);
        var turn = new BotTurn(view, _memory);
        lock (_lock)
        {
            _tick = view.State.Tick;
            _startFighters ??= view.Army.Select(u => u.Id).ToHashSet();
            _fighters.UnionWith(view.Army.Select(u => u.Id));

            Fight(view, turn);
            var refilling = Refill(view, turn);
            if (!refilling)
            {
                RunSteps(view, turn);
            }

            if (!refilling && _step >= _plan.Steps.Count)
            {
                RunForever(view, turn);
            }
        }

        turn.FinishAbandonedSites();
        turn.GatherIdleWorkers(GatherStyle.RememberedNearDropOff);
        return turn.Commands;
    }

    // ------------------------------------------------------------ fighting

    private void Fight(BotView view, BotTurn turn)
    {
        var army = view.Army.ToList();
        var treaty = _tick <= treatyTicks;
        var attack = _plan.Attack;
        if (!_attacking && !treaty && _tick >= _holdUntil)
        {
            _attacking = _attackNow || (attack.Trigger == "time"
                ? _tick >= attack.AtSecond * _ticksPerSecond
                : army.Count >= attack.Army);
            _attackNow &= !_attacking;
        }

        if (army.Count == 0)
        {
            return;
        }

        if (!treaty && attack.Defend && Threat(view) is { } threat)
        {
            turn.Order(army, CommandType.Attack, (threat.X, threat.Y), retarget: false);
            return;
        }

        if (_attacking && Target(view, army) is { } spot)
        {
            turn.Order(army, CommandType.Attack, spot, retarget: true);
            return;
        }

        if (_tick < _holdUntil)
        {
            var away = view.AwayFromRally(army).ToList();
            if (away.Count > 0)
            {
                turn.Order(away, CommandType.Move, view.RallyPoint(), retarget: false);
            }
        }
    }

    /// <summary>Enemy fighters (not workers out digging) near one of our buildings, nearest the HQ first.</summary>
    private static UnitDto? Threat(BotView view) =>
        view.EnemyUnits
            .Where(e => e.Type != UnitType.Worker && view.MyBuildings.Any(b => BotView.Dist(b.X, b.Y, e.X, e.Y) <= DefenseRadius))
            .OrderBy(e => BotView.Dist(view.CommandCenter!.X, view.CommandCenter.Y, e.X, e.Y)).ThenBy(e => e.Id)
            .FirstOrDefault();

    private (int X, int Y)? Target(BotView view, IReadOnlyList<UnitDto> army)
    {
        if (_plan.Attack.Target != "nearest")
        {
            return view.AttackTarget();
        }

        var cx = (int)army.Average(u => u.X);
        var cy = (int)army.Average(u => u.Y);
        return view.EnemyUnits.Select(u => (u.X, u.Y))
            .Concat(view.EnemyBuildings.Select(b => (b.X, b.Y)))
            .OrderBy(p => BotView.Dist(p.X, p.Y, cx, cy)).ThenBy(p => p.X).ThenBy(p => p.Y)
            .Select(p => ((int X, int Y)?)p)
            .FirstOrDefault() ?? view.AttackTarget();
    }

    // ------------------------------------------------------------ the plan

    /// <summary>Kept levels first: true when one is short, so later steps wait for the refill.</summary>
    private bool Refill(BotView view, BotTurn turn)
    {
        var shortOf = false;
        foreach (var kept in _kept)
        {
            var type = UnitOf(kept);
            if (Count(view, type) < kept.Count)
            {
                shortOf = true;
                turn.TryTrain(type);
            }
        }

        return shortOf;
    }

    private void RunSteps(BotView view, BotTurn turn)
    {
        while (_step < _plan.Steps.Count)
        {
            var step = _plan.Steps[_step];
            if (!Done(view, step))
            {
                Work(view, turn, step);
                return;
            }

            if (step.Keep && step.Kind is "train" or "workers")
            {
                _kept.Add(step);
            }

            _step++;
            _stepBase = -1;
        }
    }

    private bool Done(BotView view, PickupStepDto step)
    {
        switch (step.Kind)
        {
            case "workers":
            case "train":
                return Count(view, UnitOf(step)) >= step.Count;
            case "build":
                var type = Enum.Parse<BuildingType>(step.Item!);
                if (_stepBase < 0)
                {
                    _stepBase = Buildings(view, type);
                    return false;
                }

                return Buildings(view, type) > _stepBase;
            default:
                var upgrade = Enum.Parse<UpgradeType>(step.Item!);
                return view.State.You.Upgrades.Contains(upgrade) || view.State.You.Researching.Any(r => r.Upgrade == upgrade);
        }
    }

    private void Work(BotView view, BotTurn turn, PickupStepDto step)
    {
        switch (step.Kind)
        {
            case "workers":
            case "train":
                turn.TryTrain(UnitOf(step));
                break;
            case "build":
                Build(turn, Enum.Parse<BuildingType>(step.Item!));
                break;
            default:
                turn.TryResearch(Enum.Parse<UpgradeType>(step.Item!));
                break;
        }
    }

    /// <summary>The Forever cards in order: one unit, one building or one class each, round and round.</summary>
    private void RunForever(BotView view, BotTurn turn)
    {
        var cards = _plan.Forever;
        if (cards.Count == 0)
        {
            return;
        }

        if (_tick % ForeverRecheckTicks == 0)
        {
            _foreverSkipped = 0;
        }

        for (var tries = 0; tries < cards.Count && _foreverSkipped < cards.Count; tries++)
        {
            var outcome = PlayCard(view, turn, cards[_forever % cards.Count]);
            if (outcome == Card.Wait)
            {
                return;
            }

            _foreverSkipped = outcome == Card.Skipped ? _foreverSkipped + 1 : 0;
            _forever++;
            _foreverBase = -1;
            if (outcome == Card.Played)
            {
                return;
            }
        }
    }

    private enum Card
    {
        Played,
        Wait,
        Skipped,
    }

    private Card PlayCard(BotView view, BotTurn turn, string item)
    {
        if (Enum.TryParse<UnitType>(item, out var unit))
        {
            if (unit == UnitType.Worker && Count(view, unit) >= MaxWorkers)
            {
                return Card.Skipped;
            }

            return Outcome(turn.TryTrain(unit));
        }

        if (Enum.TryParse<BuildingType>(item, out var building))
        {
            if (_foreverBase < 0)
            {
                _foreverBase = Buildings(view, building);
            }

            return Buildings(view, building) > _foreverBase ? Card.Played : Outcome(Build(turn, building), placedLater: true);
        }

        var upgrade = Enum.Parse<UpgradeType>(item);
        return view.State.You.Upgrades.Contains(upgrade) || view.State.You.Researching.Any(r => r.Upgrade == upgrade)
            ? Card.Skipped
            : Outcome(turn.TryResearch(upgrade));
    }

    /// <summary>Done: played. Not affordable or busy: wait for it. Can't ever happen as things stand: skip it.</summary>
    private static Card Outcome(Attempt attempt, bool placedLater = false) => attempt switch
    {
        Attempt.Done => placedLater ? Card.Wait : Card.Played,
        Attempt.TooExpensive or Attempt.Busy or Attempt.AlreadyGoingUp or Attempt.InProgress or Attempt.NoBuilder => Card.Wait,
        _ => Card.Skipped,
    };

    private static Attempt Build(BotTurn turn, BuildingType type) =>
        turn.TryBuild(type, type switch
        {
            BuildingType.GuardTower => SitePreference.TowardEnemy,
            BuildingType.ResourceDepot => SitePreference.NearOre,
            _ => SitePreference.TowardCenter,
        }, oneAtATime: true);

    private static UnitType UnitOf(PickupStepDto step) => step.Kind == "workers" ? UnitType.Worker : Enum.Parse<UnitType>(step.Item!);

    /// <summary>Units of a type, counting the ones being trained.</summary>
    private static int Count(BotView view, UnitType type) =>
        view.MyUnits.Count(u => u.Type == type) + view.MyBuildings.Sum(b => b.Production.Count(p => p.UnitType == type));

    /// <summary>Buildings of a type, counting ones going up and sites a worker is on the way to.</summary>
    private int Buildings(BotView view, BuildingType type) =>
        view.MyBuildings.Count(b => b.Type == type) + _memory.PendingCount(view, type);
}
