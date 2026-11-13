using System.Text.Json;
using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

public sealed partial class BlockBot
{
    /// <summary>
    /// Runs the rules for one tick on a <see cref="BotTurn"/> (the grubs left to spend, the units already
    /// given orders, the commands so far) and explains each in a <see cref="Phrase"/>.
    /// </summary>
    private sealed class Runner(BlockBot bot, BotView view, BotTurn turn, Dictionary<string, bool> switches)
    {
        private int Tick => view.State.Tick;

        public RuleStatus RunRule(int index, BlockRuleDto rule)
        {
            if (!rule.Enabled)
            {
                return new RuleStatus(index, "off", _ => "This rule is turned off.");
            }

            var key = rule.Once || rule.EverySeconds is not null ? JsonSerializer.Serialize(new { rule.When, rule.Do }) : null;
            if (key is not null && bot._lastRun.TryGetValue(key, out var ran))
            {
                if (rule.Once)
                {
                    return new RuleStatus(index, "idle", _ => "Done: this rule runs only once.");
                }

                var every = rule.EverySeconds!.Value;
                var wait = every - (Tick - ran);
                if (wait > 0)
                {
                    return new RuleStatus(index, "idle", _ => $"Resting: it runs at most every {every} s, so again in {wait} s.", Progress: (Tick - ran) * 100 / every);
                }
            }

            var check = Evaluate(rule.When);
            if (!check.Value)
            {
                return new RuleStatus(index, "idle", w => "Not now: " + check.Why(w), Progress: check.Progress);
            }

            if (rule.Do.Count == 0)
            {
                return new RuleStatus(index, "waiting", _ => "True, but this rule has nothing to do.");
            }

            var done = new List<Outcome>();
            var failed = new List<Outcome>();
            for (var a = 0; a < rule.Do.Count; a++)
            {
                var outcome = Run(index, a, rule.Do[a]);
                (outcome.Did ? done : failed).Add(outcome);
            }

            if (key is not null && done.Count > 0)
            {
                bot._lastRun[key] = Tick;
            }

            return done.Count > 0
                ? new RuleStatus(index, "firing", w => "Firing: " + string.Join("; ", done.Select(d => d.Text(w))), done.SelectMany(d => d.UnitIds).Distinct().Take(60).ToList())
                : new RuleStatus(index, "waiting", w => "Ready, but " + failed[0].Text(w));
        }

        // ------------------------------------------------------------ conditions

        private Check Evaluate(BlockConditionDto c)
        {
            switch (c.Kind)
            {
                case "always":
                    return new Check(true, _ => "every tick");
                case "enemyNearBase":
                    var near = view.Threat(DefenseRadius) is not null;
                    return new Check(near, _ => near ? "an enemy is near your base" : "no enemies near your base");
                case "seeEnemy":
                    var seen = view.EnemyUnits.Count + view.EnemyBuildings.Count > 0;
                    return new Check(seen, _ => seen ? "you can see an enemy" : "you can't see any enemies");
                case "count":
                    var (total, detail) = Count(c.What!);
                    return Compare(w => w.What(c.What!), total, c.Op!, c.Value ?? 0, detail);
                case "minute":
                    return Compare(_ => "minute", Tick / TicksPerMinute, c.Op!, c.Value ?? 0);
                case "grubsLeft":
                    return Compare(w => w.OreLeft, bot._memory.Deposits.Where(d => NearBase(d.X, d.Y)).Sum(d => d.Remaining), c.Op!, c.Value ?? 0);
                case "switch":
                    var on = switches.GetValueOrDefault(c.Name!);
                    return new Check(on, _ => $"{c.Name} is {(on ? "on" : "off")}");
                case "enemies":
                    return Compare(w => $"enemy {w.Group(c.What!)} you can see", EnemyCount(c.What!), c.Op!, c.Value ?? 0);
                case "underAttack":
                    var hit = bot._attacks.Any(c.What!, Tick);
                    return new Check(hit, w => $"your {w.Group(c.What!)} {(hit ? "are" : "aren't")} under attack");
                case "hqHealth":
                    var hq = view.CommandCenter;
                    var health = hq is null ? 0 : hq.Hp * 100 / Math.Max(1, hq.MaxHp);
                    return Compare(w => $"your {w.Building(BuildingType.CommandCenter)}'s health (%)", health, c.Op!, c.Value ?? 0);
                case "and":
                    var a = Evaluate(c.A!);
                    if (!a.Value)
                    {
                        return a;
                    }

                    var b = Evaluate(c.B!);
                    return b.Value ? new Check(true, w => $"{a.Why(w)} and {b.Why(w)}") : b;
                case "or":
                    var left = Evaluate(c.A!);
                    if (left.Value)
                    {
                        return left;
                    }

                    var right = Evaluate(c.B!);
                    return right.Value
                        ? right
                        : new Check(false, w => $"{left.Why(w)}, and {right.Why(w)}", Max(left.Progress, right.Progress));
                case "not":
                    var inner = Evaluate(c.A!);
                    return new Check(!inner.Value, inner.Why);
                default:
                    throw new InvalidOperationException($"Unknown condition '{c.Kind}'.");
            }
        }

        private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

        private static Check Compare(Phrase label, int actual, string op, int value, string detail = "")
        {
            var result = op switch
            {
                "<" => actual < value,
                "<=" => actual <= value,
                ">" => actual > value,
                ">=" => actual >= value,
                "=" => actual == value,
                _ => throw new InvalidOperationException($"Unknown comparison '{op}'."),
            };
            int? progress = op is ">=" or ">" && value > 0 ? Math.Clamp(actual * 100 / value, 0, 100) : null;
            return new Check(result, w => $"{label(w)} is {actual}{detail} (needs {BlockWords.Op(op)} {value})", progress);
        }

        /// <summary>
        /// Units count with those in production; buildings with those ordered or going up (so "fewer than 2
        /// Barracks: build one" orders one, not one a tick); grubs are those left to spend. Detail: what of the
        /// total isn't here yet (or, for grubs, what's saved), for the why.
        /// </summary>
        private (int Total, string Detail) Count(string what)
        {
            int Queued(Func<UnitType, bool> match) =>
                view.MyBuildings.Sum(b => b.Production.Count(p => match(p.UnitType)));

            static (int, string) Coming(int total, int coming, string doing) => (total, coming > 0 ? $", {coming} of them {doing}" : "");
            static (int, string) Units(int have, int queued) => Coming(have + queued, queued, "still training");

            switch (what.ToLowerInvariant())
            {
                case "grubs":
                    return (turn.Budget, Saved);
                case "workers":
                    return Units(view.Workers.Count, Queued(t => t == UnitType.Worker));
                case "fighters":
                    return Units(view.Army.Count, Queued(t => t != UnitType.Worker));
            }

            if (Enum.TryParse<UnitType>(what, true, out var unit))
            {
                return Units(view.MyUnits.Count(u => u.Type == unit), Queued(t => t == unit));
            }

            if (Enum.TryParse<BuildingType>(what, true, out var building))
            {
                var pending = bot._memory.PendingCount(view, building);
                var mine = view.MyBuildings.Where(b => b.Type == building).ToList();
                return Coming(mine.Count + pending, pending + mine.Count(b => !b.Completed), "not finished yet");
            }

            throw new InvalidOperationException($"Can't count '{what}'.");
        }

        /// <summary>", with 150 more saved for a building on the way" when the turn has grubs set aside, else "".</summary>
        private string Saved => turn.SetAside > 0 ? $", with {turn.SetAside} more saved for a building on the way" : "";

        /// <summary>"... costs 150 grubs and you have 50", saying why when some are saved for a building on the way.</summary>
        private Outcome CantAfford(Phrase what, int cost, int have) =>
            Outcome.Fail(w => $"{what(w)} costs {cost} {w.Ore} and you have {have}{Saved}.");

        /// <summary>The enemies you can see: fighters, workers, buildings, or one type of unit or building.</summary>
        private int EnemyCount(string what) => what.ToLowerInvariant() switch
        {
            "fighters" => view.EnemyUnits.Count(u => u.Type != UnitType.Worker),
            "workers" => view.EnemyUnits.Count(u => u.Type == UnitType.Worker),
            "buildings" => view.EnemyBuildings.Count,
            _ when Enum.TryParse<UnitType>(what, true, out var unit) => view.EnemyUnits.Count(u => u.Type == unit),
            _ when Enum.TryParse<BuildingType>(what, true, out var building) => view.EnemyBuildings.Count(b => b.Type == building),
            _ => throw new InvalidOperationException($"Can't count enemy '{what}'."),
        };

        // ------------------------------------------------------------ actions

        private Outcome Run(int rule, int action, BlockActionDto a) => a.Kind switch
        {
            "train" => Train(a.Unit!.Value),
            "trainMix" => TrainMix(rule, action, a.Units!),
            "build" => Build(a.Building!.Value, Enum.TryParse<SitePreference>(a.Site ?? Defaults.Site, true, out var site) ? site : SitePreference.TowardCenter),
            "research" => Research(a.Upgrade!.Value),
            "attack" => Attack(a.Who ?? Defaults.Who, a.Target ?? Defaults.Target, a.Percent),
            "defend" => Defend(a.Who ?? Defaults.Who),
            "retreat" => Retreat(a.Who ?? Defaults.Who),
            "scout" => (a.Target ?? Defaults.ScoutTarget) == "explore" ? Explore() : Scout(),
            "expand" => Expand(a.Building ?? Defaults.ExpandBuilding),
            "setSwitch" => SetSwitch(a.Name!, a.On ?? true),
            _ => throw new InvalidOperationException($"Unknown action '{a.Kind}'."),
        };

        private static InvalidOperationException Unexpected(Attempt attempt) => new($"Unexpected result {attempt}.");

        private Outcome Train(UnitType type) => turn.TryTrain(type) switch
        {
            Attempt.Done => new Outcome(true, w => $"training a {w.Unit(type)}", []),
            Attempt.NeedsBuilding(var at) => Outcome.Fail(w => $"you need a {w.Building(at)} to train a {w.Unit(type)}."),
            Attempt.Busy(var at) => Outcome.Fail(w => $"your {w.Building(at)} is busy."),
            Attempt.TooExpensive(var cost, var have) => CantAfford(w => $"a {w.Unit(type)}", cost, have),
            var other => throw Unexpected(other),
        };

        private Outcome TrainMix(int rule, int action, IReadOnlyList<UnitType> units)
        {
            var key = (rule, action);
            var cursor = bot._mixCursor.GetValueOrDefault(key);
            var outcome = Train(units[cursor % units.Count]);
            if (outcome.Did)
            {
                bot._mixCursor[key] = cursor + 1;
            }

            return outcome;
        }

        private Outcome Build(BuildingType type, SitePreference preference)
        {
            Phrase name = w => w.Building(type);
            return turn.TryBuild(type, preference, oneAtATime: true) switch
            {
                Attempt.Done(var builder) => new Outcome(true, w => $"building a {name(w)}", [builder!.Value]),
                Attempt.NeedsBuilding(var required) => Outcome.Fail(w => $"a {name(w)} needs a finished {w.Building(required)} first."),
                Attempt.NeedsUpgrade(var upgrade) => Outcome.Fail(w => $"a {name(w)} needs {w.Upgrade(upgrade)} first."),
                Attempt.AlreadyGoingUp => Outcome.Fail(w => $"a {name(w)} is already going up."),
                Attempt.TooExpensive(var cost, var have) => CantAfford(w => $"a {name(w)}", cost, have),
                Attempt.NoSite => Outcome.Fail(w => $"there's no room near your HQ for a {name(w)}."),
                Attempt.NoBuilder => Outcome.Fail(w => $"there's no free worker to build a {name(w)}."),
                var other => throw Unexpected(other),
            };
        }

        private Outcome Research(UpgradeType upgrade)
        {
            Phrase name = w => w.Upgrade(upgrade);
            return turn.TryResearch(upgrade) switch
            {
                Attempt.Done => new Outcome(true, w => $"{w.Learning} {name(w)}", []),
                Attempt.AlreadyHave => Outcome.Fail(w => $"you already learned {name(w)}."),
                Attempt.InProgress => Outcome.Fail(w => $"{name(w)} is already being learned."),
                Attempt.NeedsUpgrade(var req) => Outcome.Fail(w => $"{name(w)} needs {w.Upgrade(req)} first."),
                Attempt.NeedsBuilding(var lab) => Outcome.Fail(w => $"you need a {w.Building(lab)} to learn {name(w)}."),
                Attempt.Busy(var lab) => Outcome.Fail(w => $"your {w.Building(lab)} is busy."),
                Attempt.TooExpensive(var cost, var have) => CantAfford(name, cost, have),
                var other => throw Unexpected(other),
            };
        }

        /// <summary>The fighters an army order goes to, minus those a rule higher up already used this tick.</summary>
        private List<UnitDto> Group(string? who)
        {
            var all = who is null || who.Equals("fighters", StringComparison.OrdinalIgnoreCase)
                ? view.Army
                : Enum.TryParse<UnitType>(who, true, out var type) ? view.Army.Where(u => u.Type == type).ToList() : [];
            return all.Where(u => !turn.IsClaimed(u.Id)).ToList();
        }

        private Outcome Attack(string? who, string target, int? percent)
        {
            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail(w => $"you have no {w.Who(who)} to send.");
            }

            (int X, int Y)? spot;
            if (target == "nearestEnemy")
            {
                var cx = (int)group.Average(u => u.X);
                var cy = (int)group.Average(u => u.Y);
                spot = view.EnemyUnits.Select(u => (u.X, u.Y))
                    .Concat(view.EnemyBuildings.Select(b => (b.X, b.Y)))
                    .OrderBy(p => BotView.Dist(p.X, p.Y, cx, cy)).ThenBy(p => p.X).ThenBy(p => p.Y)
                    .Select(p => ((int X, int Y)?)p)
                    .FirstOrDefault();
                if (spot is null)
                {
                    return Outcome.Fail(_ => "you can't see any enemies yet.");
                }
            }
            else
            {
                spot = view.AttackTarget();
                if (spot is null)
                {
                    return Outcome.Fail(_ => "there's no enemy base left to attack.");
                }
            }

            if (percent is { } share and < 100)
            {
                group = Share(group, share, spot.Value);
            }

            turn.Order(group, CommandType.Attack, spot.Value, retarget: true);
            return new Outcome(true, w => $"{group.Count} {w.Who(who, group.Count)} attacking", group.Select(u => u.Id).ToList());
        }

        /// <summary>
        /// Part of a group, at least one: those already heading for the spot first (so the same ones keep going),
        /// then the nearest. The rest stay free for the rules below.
        /// </summary>
        private List<UnitDto> Share(List<UnitDto> group, int percent, (int X, int Y) spot)
        {
            var take = Math.Max(1, (int)Math.Ceiling(group.Count * percent / 100.0));
            return group
                .OrderBy(u => bot._memory.LastOrders.TryGetValue(u.Id, out var o) && o.Type == CommandType.Attack && o.X == spot.X && o.Y == spot.Y ? 0 : 1)
                .ThenBy(u => BotView.Dist(u.X, u.Y, spot.X, spot.Y)).ThenBy(u => u.Id)
                .Take(take).ToList();
        }

        /// <summary>Flips one of the program's switches, for the rules below this one too.</summary>
        private Outcome SetSwitch(string name, bool on)
        {
            if (switches.GetValueOrDefault(name) == on)
            {
                return Outcome.Fail(_ => $"{name} is already {(on ? "on" : "off")}.");
            }

            switches[name] = on;
            bot.SetSwitch(name, on);
            return new Outcome(true, _ => $"turned {name} {(on ? "on" : "off")}", []);
        }

        private Outcome Defend(string? who)
        {
            var threat = view.Threat(DefenseRadius);
            if (threat is null)
            {
                return Outcome.Fail(_ => "no enemies near your base.");
            }

            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail(w => $"you have no {w.Who(who)} to defend with.");
            }

            turn.Order(group, CommandType.Attack, (threat.X, threat.Y), retarget: false);
            return new Outcome(true, w => $"{group.Count} {w.Who(who, group.Count)} defending", group.Select(u => u.Id).ToList());
        }

        private Outcome Retreat(string? who)
        {
            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail(w => $"you have no {w.Who(who)} to bring home.");
            }

            var away = view.AwayFromRally(group).ToList();
            foreach (var u in group)
            {
                turn.Claim(u.Id);
            }

            if (away.Count == 0)
            {
                return new Outcome(true, w => $"{group.Count} {w.Who(who, group.Count)} waiting at home", group.Select(u => u.Id).ToList());
            }

            turn.Order(away, CommandType.Move, view.RallyPoint(), retarget: false);
            return new Outcome(true, w => $"{away.Count} {w.Who(who, away.Count)} heading home", away.Select(u => u.Id).ToList());
        }

        // ------------------------------------------------------------ grubs further away

        private static bool IsDropOff(BuildingType type) => type is BuildingType.CommandCenter or BuildingType.ResourceDepot;

        /// <summary>Near one of our HQs or Co-op Stores (built, going up, or just ordered).</summary>
        private bool NearBase(int x, int y) =>
            view.MyBuildings.Any(b => IsDropOff(b.Type) && BotView.Dist(b.X, b.Y, x, y) <= NearBaseRange)
            || bot._memory.PendingSites.Any(kv => IsDropOff(kv.Value.Type) && BotView.Dist(kv.Key.X, kv.Key.Y, x, y) <= NearBaseRange);

        /// <summary>
        /// Opens a new grub field: builds a Co-op Store (or an HQ) beside the nearest pile we know of that isn't
        /// near a drop-off yet. Workers then haul to it, the nearer drop-off. Knowing of no such pile, it
        /// sends a scout to explore instead.
        /// </summary>
        private Outcome Expand(BuildingType type)
        {
            Phrase name = w => w.Building(type);
            if (bot._memory.PendingSites.Values.Any(p => IsDropOff(p.Type)) || view.MyBuildings.Any(b => IsDropOff(b.Type) && !b.Completed))
            {
                return Outcome.Fail(w => $"a {w.Building(BuildingType.ResourceDepot)} or {w.Building(BuildingType.CommandCenter)} is already going up.");
            }

            var cc = view.CommandCenter!;
            var target = bot._memory.Deposits
                .Where(d => d.Remaining > 0 && !NearBase(d.X, d.Y))
                .OrderBy(d => BotView.Dist(d.X, d.Y, cc.X, cc.Y)).ThenBy(d => d.Id)
                .Select(d => ((int X, int Y)?)(d.X, d.Y))
                .FirstOrDefault();
            if (target is null)
            {
                var look = Explore();
                return look.Did
                    ? new Outcome(true, w => $"looking for more {w.Ore}: {look.Text(w)}", look.UnitIds)
                    : Outcome.Fail(w => $"you don't know where more {w.Ore} is yet, and {look.Text(w)}");
            }

            if (turn.CheckBudget(view.Rules.Buildings.First(b => b.Type == type).Cost) is Attempt.TooExpensive(var cost, var have))
            {
                return CantAfford(w => $"a {name(w)}", cost, have);
            }

            var site = SiteNear(target.Value);
            if (site is null)
            {
                return Outcome.Fail(w => $"there's no room for a {name(w)} next to those {w.Ore}.");
            }

            return turn.PlaceBuilding(type, site.Value) is Attempt.Done(var builder)
                ? new Outcome(true, w => $"building a {name(w)} by new {w.Ore}", [builder!.Value])
                : Outcome.Fail(w => $"there's no free worker to build a {name(w)}.");
        }

        /// <summary>
        /// An open tile two or three steps from a pile, not right beside any pile (those are where workers
        /// dig), in the middle of the most piles, then nearest home.
        /// </summary>
        private (int X, int Y)? SiteNear((int X, int Y) pile)
        {
            var cc = view.CommandCenter!;
            var piles = bot._memory.Deposits.Select(d => (d.X, d.Y)).ToList();
            (int X, int Y)? best = null;
            var bestScore = int.MaxValue;
            for (var y = pile.Y - 3; y <= pile.Y + 3; y++)
            {
                for (var x = pile.X - 3; x <= pile.X + 3; x++)
                {
                    if (BotView.Dist(x, y, pile.X, pile.Y) < 2 || !view.IsOpen(x, y) || bot._memory.PendingSites.ContainsKey((x, y))
                        || piles.Any(p => BotView.Dist(p.X, p.Y, x, y) <= 1))
                    {
                        continue;
                    }

                    var score = (-10 * piles.Count(p => BotView.Dist(p.X, p.Y, x, y) <= 3)) + BotView.Dist(x, y, cc.X, cc.Y);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (x, y);
                    }
                }
            }

            return best;
        }

        // ------------------------------------------------------------ scouting

        /// <summary>A scout, else any fighter, not used yet this tick.</summary>
        private UnitDto? FreeScout() =>
            view.Army.Where(u => u.Type == UnitType.Scout && !turn.IsClaimed(u.Id)).OrderBy(u => u.Id).FirstOrDefault()
            ?? view.Army.Where(u => !turn.IsClaimed(u.Id)).OrderBy(u => u.Id).FirstOrDefault();

        /// <summary>Sends a scout (or any fighter) to the nearest part of the map the bot hasn't seen yet.</summary>
        private Outcome Explore()
        {
            var scout = FreeScout();
            if (scout is null)
            {
                return Outcome.Fail(w => $"you have no scouts (or any fighters) to send exploring.");
            }

            // The scout stopped short of its spot without seeing it (walled in by rock): give up on that spot.
            if (bot._exploreGoal is { } stuck && scout.Activity == UnitActivity.Idle
                && bot._memory.LastOrders.TryGetValue(scout.Id, out var last) && last.X == stuck.X && last.Y == stuck.Y && Tick - last.Tick > 5)
            {
                bot._memory.MarkSeen(stuck.X, stuck.Y);
                bot._exploreGoal = null;
            }

            if (bot._exploreGoal is null)
            {
                var bestDist = int.MaxValue;
                for (var y = 0; y < view.Map.Height; y++)
                {
                    for (var x = 0; x < view.Map.Width; x++)
                    {
                        if (bot._memory.HasSeen(x, y) || view.Map.Terrain[y][x] != '.')
                        {
                            continue;
                        }

                        var d = BotView.Dist(x, y, scout.X, scout.Y);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bot._exploreGoal = (x, y);
                        }
                    }
                }
            }

            if (bot._exploreGoal is not { } goal)
            {
                return Outcome.Fail(_ => "you've already seen the whole map.");
            }

            turn.Claim(scout.Id);
            turn.Order([scout], CommandType.Move, goal, retarget: false);
            return new Outcome(true, w => $"a {w.Unit(scout.Type)} is exploring", [scout.Id]);
        }

        private Outcome Scout()
        {
            var scout = FreeScout();
            if (scout is null)
            {
                return Outcome.Fail(_ => "you have no scouts (or any fighters) to send.");
            }

            var starts = view.State.Players.Where(p => p.Slot != view.State.You.Slot && !p.Eliminated)
                .OrderBy(p => p.Slot).Select(p => p.StartPosition).ToList();
            if (starts.Count == 0)
            {
                return Outcome.Fail(_ => "there's nobody left to find.");
            }

            turn.Claim(scout.Id);
            var goal = starts[bot._scoutCursor % starts.Count];
            if (scout.Activity == UnitActivity.Idle && BotView.Dist(scout.X, scout.Y, goal.X, goal.Y) <= 3)
            {
                bot._scoutCursor++;
                goal = starts[bot._scoutCursor % starts.Count];
            }

            turn.Order([scout], CommandType.Move, (goal.X, goal.Y), retarget: false);
            return new Outcome(true, w => $"a {w.Unit(scout.Type)} is scouting", [scout.Id]);
        }
    }
}
