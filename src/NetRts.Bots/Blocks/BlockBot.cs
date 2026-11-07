using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

/// <summary>
/// Runs a Block Bot program: every tick it checks the rules top to bottom and does what the first rules
/// ask first, so a rule higher up gets the grubs and the fighters before one lower down. Two things
/// happen on their own after the rules, so a first program with one rule still plays: idle workers
/// dig grubs, and half-built buildings get finished.
/// <para>
/// The program can be swapped while the match runs (<see cref="Apply"/>) and switches flipped
/// (<see cref="SetSwitch"/>); both take effect on the next tick. <see cref="Status"/> says what every rule did.
/// </para>
/// </summary>
public sealed class BlockBot : IBotStrategy
{
    private const int RetargetEveryTicks = 15;
    private const int DefenseRadius = 10;
    private const int PendingSiteTicks = 40;
    private const int TicksPerMinute = 60;

    /// <summary>A grub pile this close to an HQ or Co-op Store counts as "near my base".</summary>
    private const int NearBaseRange = 8;

    private readonly object _lock = new();
    private readonly List<BlockVersionDto> _versions = [];
    private readonly Dictionary<string, bool> _switches = new(StringComparer.OrdinalIgnoreCase);
    private BlockProgramDto _program;
    private IReadOnlyList<BlockRuleStatusDto> _ruleStatus = [];
    private BlockStatsDto _stats = new();
    private int _tick;
    private string? _error;

    // Memory between ticks.
    private readonly Dictionary<(int X, int Y), (BuildingType Type, int Tick)> _pendingSites = [];
    private readonly Dictionary<(int Rule, int Action), int> _mixCursor = [];
    private readonly Dictionary<int, (string Kind, int X, int Y, int Tick)> _lastOrder = [];
    private int _scoutCursor;

    // What the bot has seen: every grub pile (they never move, so the fog doesn't hide them from memory)
    // and every tile, for exploring.
    private readonly SortedDictionary<int, (int X, int Y, int Remaining)> _deposits = [];
    private bool[]? _seen;
    private (int X, int Y)? _exploreGoal;

    public BlockBot(string name, BlockProgramDto program)
    {
        Name = name;
        _program = program;
        _versions.Add(new BlockVersionDto(1, 0, program.Name));
        foreach (var s in program.Switches)
        {
            _switches[s] = false;
        }
    }

    public string Name { get; }

    /// <summary>The words the status uses: the site theme the player's page shows (see <see cref="BlockWords.For"/>).</summary>
    public BlockWords Words { get; set; } = BlockWords.Snow;

    public int Version
    {
        get
        {
            lock (_lock)
            {
                return _versions.Count;
            }
        }
    }

    /// <summary>Replaces the program from the next tick on. Switches with the same name keep their position.</summary>
    public int Apply(BlockProgramDto program)
    {
        lock (_lock)
        {
            _program = program;
            var kept = program.Switches.ToDictionary(s => s, s => _switches.GetValueOrDefault(s), StringComparer.OrdinalIgnoreCase);
            _switches.Clear();
            foreach (var (k, v) in kept)
            {
                _switches[k] = v;
            }

            var version = _versions.Count + 1;
            _versions.Add(new BlockVersionDto(version, _tick + 1, program.Name));
            return version;
        }
    }

    /// <summary>Flips a switch. False when the program has no switch by that name.</summary>
    public bool SetSwitch(string name, bool on)
    {
        lock (_lock)
        {
            if (!_switches.ContainsKey(name))
            {
                return false;
            }

            _switches[name] = on;
            return true;
        }
    }

    /// <summary>What the bot did on the last tick it played.</summary>
    public BlockBotStatusDto Status(Guid matchId, MatchStatus matchStatus, int slot)
    {
        lock (_lock)
        {
            return new BlockBotStatusDto
            {
                MatchId = matchId,
                MatchStatus = matchStatus,
                Tick = _tick,
                Slot = slot,
                Version = _versions.Count,
                ProgramName = _program.Name,
                Program = _program,
                Versions = [.. _versions],
                Switches = new Dictionary<string, bool>(_switches),
                Rules = _ruleStatus,
                Stats = _stats,
                Error = _error,
            };
        }
    }

    public IReadOnlyList<CommandRequest> Decide(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BlockProgramDto program;
        Dictionary<string, bool> switches;
        lock (_lock)
        {
            program = _program;
            switches = new Dictionary<string, bool>(_switches, StringComparer.OrdinalIgnoreCase);
        }

        var view = new BotView(context);
        if (view.State.Status != MatchStatus.Active || view.CommandCenter is null)
        {
            return [];
        }

        Remember(view);
        var turn = new Turn(this, view, switches);
        var statuses = new List<BlockRuleStatusDto>(program.Rules.Count);
        string? error = null;
        try
        {
            ExpirePendingSites(view);
            for (var i = 0; i < program.Rules.Count; i++)
            {
                statuses.Add(turn.RunRule(i, program.Rules[i]));
            }

            turn.FinishAbandonedSites();
            turn.PutIdleWorkersToWork();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            // A program the validator let through but this tick can't run: report it, keep the match going.
            error = ex.Message;
        }

        lock (_lock)
        {
            _tick = view.State.Tick;
            _ruleStatus = statuses;
            _error = error;
            _stats = new BlockStatsDto
            {
                Grubs = view.State.You.Resources,
                Workers = view.Workers.Count,
                Fighters = view.Army.Count,
                Buildings = view.MyBuildings.Count,
                Score = view.State.Players.FirstOrDefault(p => p.Slot == view.State.You.Slot)?.Score.Total ?? 0,
            };
        }

        return turn.Commands;
    }

    /// <summary>Updates what the bot knows: the tiles it has seen and the grub piles it has seen (and which are gone).</summary>
    private void Remember(BotView view)
    {
        var state = view.State;
        var width = view.Map.Width;
        var rows = state.Visibility;
        _seen ??= new bool[width * view.Map.Height];
        bool Visible(int x, int y) => y < rows.Count && x < rows[y].Length && rows[y][x] == '1';
        for (var y = 0; y < rows.Count && y < view.Map.Height; y++)
        {
            var row = rows[y];
            for (var x = 0; x < row.Length && x < width; x++)
            {
                if (row[x] == '1')
                {
                    _seen[(y * width) + x] = true;
                }
            }
        }

        var listed = new HashSet<int>();
        foreach (var d in state.Resources)
        {
            _deposits[d.Id] = (d.X, d.Y, d.Remaining);
            listed.Add(d.Id);
        }

        // A pile we can see the tile of, but that isn't listed, has been dug out.
        foreach (var (id, d) in _deposits.Where(kv => !listed.Contains(kv.Key) && Visible(kv.Value.X, kv.Value.Y)).ToList())
        {
            _deposits.Remove(id);
        }

        if (_exploreGoal is { } goal && _seen[(goal.Y * width) + goal.X])
        {
            _exploreGoal = null;
        }
    }

    private void ExpirePendingSites(BotView view)
    {
        foreach (var site in _pendingSites
                     .Where(kv => view.State.Tick - kv.Value.Tick > PendingSiteTicks || view.MyBuildings.Any(b => b.X == kv.Key.X && b.Y == kv.Key.Y))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _pendingSites.Remove(site);
        }
    }

    private readonly record struct Check(bool Value, string Why, int? Progress = null);

    private readonly record struct Outcome(bool Did, string Text, IReadOnlyList<int> UnitIds)
    {
        public static Outcome Fail(string text) => new(false, text, []);
    }

    /// <summary>One tick's work: the grubs left to spend, the units already given orders, the commands so far.</summary>
    private sealed class Turn(BlockBot bot, BotView view, IReadOnlyDictionary<string, bool> switches)
    {
        private readonly HashSet<int> _claimed = [];
        private readonly Dictionary<int, int> _queuedAt = [];
        private int _budget = view.State.You.Resources;

        public List<CommandRequest> Commands { get; } = [];

        private int Tick => view.State.Tick;

        private BlockWords W => bot.Words;

        public BlockRuleStatusDto RunRule(int index, BlockRuleDto rule)
        {
            if (!rule.Enabled)
            {
                return new BlockRuleStatusDto { Index = index, State = "off", Why = "This rule is turned off." };
            }

            var check = Evaluate(rule.When);
            if (!check.Value)
            {
                return new BlockRuleStatusDto { Index = index, State = "idle", Why = "Not now: " + check.Why, Progress = check.Progress };
            }

            if (rule.Do.Count == 0)
            {
                return new BlockRuleStatusDto { Index = index, State = "waiting", Why = "True, but this rule has nothing to do." };
            }

            var done = new List<Outcome>();
            var failed = new List<Outcome>();
            for (var a = 0; a < rule.Do.Count; a++)
            {
                var outcome = Run(index, a, rule.Do[a]);
                (outcome.Did ? done : failed).Add(outcome);
            }

            return done.Count > 0
                ? new BlockRuleStatusDto
                {
                    Index = index,
                    State = "firing",
                    Why = "Firing: " + string.Join("; ", done.Select(d => d.Text)),
                    UnitIds = done.SelectMany(d => d.UnitIds).Distinct().Take(60).ToList(),
                }
                : new BlockRuleStatusDto { Index = index, State = "waiting", Why = "Ready, but " + failed[0].Text };
        }

        // ------------------------------------------------------------ conditions

        private Check Evaluate(BlockConditionDto c)
        {
            switch (c.Kind)
            {
                case "always":
                    return new Check(true, "every tick");
                case "enemyNearBase":
                    var near = Threat() is not null;
                    return new Check(near, near ? "an enemy is near your base" : "no enemies near your base");
                case "seeEnemy":
                    var seen = view.EnemyUnits.Count + view.EnemyBuildings.Count > 0;
                    return new Check(seen, seen ? "you can see an enemy" : "you can't see any enemies");
                case "count":
                    return Compare(W.What(c.What!), Count(c.What!), c.Op!, c.Value ?? 0);
                case "minute":
                    return Compare("minute", Tick / TicksPerMinute, c.Op!, c.Value ?? 0);
                case "grubsLeft":
                    return Compare(W.OreLeft, KnownDeposits().Where(d => NearBase(d.X, d.Y)).Sum(d => d.Remaining), c.Op!, c.Value ?? 0);
                case "switch":
                    var on = switches.GetValueOrDefault(c.Name!);
                    return new Check(on, $"{c.Name} is {(on ? "on" : "off")}");
                case "and":
                    var a = Evaluate(c.A!);
                    if (!a.Value)
                    {
                        return a;
                    }

                    var b = Evaluate(c.B!);
                    return b.Value ? new Check(true, $"{a.Why} and {b.Why}") : b;
                case "or":
                    var left = Evaluate(c.A!);
                    if (left.Value)
                    {
                        return left;
                    }

                    var right = Evaluate(c.B!);
                    return right.Value
                        ? right
                        : new Check(false, $"{left.Why}, and {right.Why}", Max(left.Progress, right.Progress));
                case "not":
                    var inner = Evaluate(c.A!);
                    return new Check(!inner.Value, inner.Why);
                default:
                    throw new InvalidOperationException($"Unknown condition '{c.Kind}'.");
            }
        }

        private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

        private Check Compare(string label, int actual, string op, int value)
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
            return new Check(result, $"{label} is {actual} (needs {BlockWords.Op(op)} {value})", progress);
        }

        /// <summary>Units count with those in production; buildings with those going up or ordered.</summary>
        private int Count(string what)
        {
            int Queued(Func<UnitType, bool> match) =>
                view.MyBuildings.Sum(b => b.Production.Count(p => match(p.UnitType)));

            switch (what.ToLowerInvariant())
            {
                case "grubs":
                    return _budget;
                case "workers":
                    return view.Workers.Count + Queued(t => t == UnitType.Worker);
                case "fighters":
                    return view.Army.Count + Queued(t => t != UnitType.Worker);
            }

            if (Enum.TryParse<UnitType>(what, true, out var unit))
            {
                return view.MyUnits.Count(u => u.Type == unit) + Queued(t => t == unit);
            }

            if (Enum.TryParse<BuildingType>(what, true, out var building))
            {
                return view.MyBuildings.Count(b => b.Type == building) + bot._pendingSites.Count(kv => kv.Value.Type == building);
            }

            throw new InvalidOperationException($"Can't count '{what}'.");
        }

        // ------------------------------------------------------------ actions

        private Outcome Run(int rule, int action, BlockActionDto a) => a.Kind switch
        {
            "train" => Train(a.Unit!.Value),
            "trainMix" => TrainMix(rule, action, a.Units!),
            "build" => Build(a.Building!.Value, Enum.TryParse<SitePreference>(a.Site, true, out var site) ? site : SitePreference.TowardCenter),
            "research" => Research(a.Upgrade!.Value),
            "attack" => Attack(a.Who, a.Target ?? "enemyBase"),
            "defend" => Defend(a.Who),
            "retreat" => Retreat(a.Who),
            "scout" => a.Target == "explore" ? Explore() : Scout(),
            "expand" => Expand(a.Building ?? BuildingType.ResourceDepot),
            _ => throw new InvalidOperationException($"Unknown action '{a.Kind}'."),
        };

        private Outcome Train(UnitType type)
        {
            var stats = view.Rules.Units.First(u => u.Type == type);
            var maxQueue = Math.Max(1, Math.Min(2, view.Rules.Economy.MaxProductionQueue));
            var producers = view.MyBuildings.Where(b => b.Type == stats.ProducedAt && b.Completed).ToList();
            if (producers.Count == 0)
            {
                return Outcome.Fail($"you need a {W.Building(stats.ProducedAt)} to train a {W.Unit(type)}.");
            }

            var building = producers
                .Where(b => b.Production.Count + _queuedAt.GetValueOrDefault(b.Id) < maxQueue)
                .OrderBy(b => b.Production.Count + _queuedAt.GetValueOrDefault(b.Id)).ThenBy(b => b.Id)
                .FirstOrDefault();
            if (building is null)
            {
                return Outcome.Fail($"your {W.Building(stats.ProducedAt)} is busy.");
            }

            if (_budget < stats.Cost)
            {
                return Outcome.Fail($"a {W.Unit(type)} costs {stats.Cost} {W.Ore} and you have {_budget}.");
            }

            var command = new CommandRequest { Type = CommandType.Produce, BuildingId = building.Id, UnitType = type };
            if (type != UnitType.Worker)
            {
                var rally = view.RallyPoint();
                command = command with { X = rally.X, Y = rally.Y };
            }

            Commands.Add(command);
            _budget -= stats.Cost;
            _queuedAt[building.Id] = _queuedAt.GetValueOrDefault(building.Id) + 1;
            return new Outcome(true, $"training a {W.Unit(type)}", []);
        }

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
            var stats = view.Rules.Buildings.First(b => b.Type == type);
            var name = W.Building(type);
            foreach (var required in stats.Requires.Where(r => !view.MyBuildings.Any(b => b.Type == r && b.Completed)))
            {
                return Outcome.Fail($"a {name} needs a finished {W.Building(required)} first.");
            }

            if (stats.RequiresUpgrade is { } upgrade && !view.State.You.Upgrades.Contains(upgrade))
            {
                return Outcome.Fail($"a {name} needs {W.Upgrade(upgrade)} first.");
            }

            if (bot._pendingSites.Any(kv => kv.Value.Type == type) || view.MyBuildings.Any(b => b.Type == type && !b.Completed))
            {
                return Outcome.Fail($"a {name} is already going up.");
            }

            if (_budget < stats.Cost)
            {
                return Outcome.Fail($"a {name} costs {stats.Cost} {W.Ore} and you have {_budget}.");
            }

            var site = view.FindBuildSite(preference, bot._pendingSites.Keys);
            if (site is null)
            {
                return Outcome.Fail($"there's no room near your HQ for a {name}.");
            }

            var builder = Builder(site.Value);
            if (builder is null)
            {
                return Outcome.Fail($"there's no free worker to build a {name}.");
            }

            Commands.Add(new CommandRequest { Type = CommandType.Build, UnitIds = [builder.Id], BuildingType = type, X = site.Value.X, Y = site.Value.Y });
            _claimed.Add(builder.Id);
            bot._pendingSites[site.Value] = (type, Tick);
            _budget -= stats.Cost;
            return new Outcome(true, $"building a {name}", [builder.Id]);
        }

        private UnitDto? Builder((int X, int Y) site) =>
            view.Workers
                .Where(w => !_claimed.Contains(w.Id) && w.Activity is UnitActivity.Gathering or UnitActivity.Moving or UnitActivity.Idle && w.Carrying < 6)
                .OrderBy(w => PlanBot.Dist(w.X, w.Y, site.X, site.Y)).ThenBy(w => w.Id)
                .FirstOrDefault();

        private Outcome Research(UpgradeType upgrade)
        {
            var name = W.Upgrade(upgrade);
            var you = view.State.You;
            if (you.Upgrades.Contains(upgrade))
            {
                return Outcome.Fail($"you already learned {name}.");
            }

            if (you.Researching.Any(r => r.Upgrade == upgrade))
            {
                return Outcome.Fail($"{name} is already being learned.");
            }

            var stats = view.Rules.Upgrades.First(u => u.Type == upgrade);
            if (stats.Requires is { } req && !you.Upgrades.Contains(req))
            {
                return Outcome.Fail($"{name} needs {W.Upgrade(req)} first.");
            }

            var labs = view.MyBuildings.Where(b => b.Type == BuildingType.TechLab && b.Completed).ToList();
            if (labs.Count == 0)
            {
                return Outcome.Fail($"you need a {W.Building(BuildingType.TechLab)} to learn {name}.");
            }

            var lab = labs.FirstOrDefault(b => b.Research is null && !_queuedAt.ContainsKey(b.Id));
            if (lab is null)
            {
                return Outcome.Fail($"your {W.Building(BuildingType.TechLab)} is busy.");
            }

            if (_budget < stats.Cost)
            {
                return Outcome.Fail($"{name} costs {stats.Cost} {W.Ore} and you have {_budget}.");
            }

            Commands.Add(new CommandRequest { Type = CommandType.Research, BuildingId = lab.Id, Upgrade = upgrade });
            _queuedAt[lab.Id] = 1;
            _budget -= stats.Cost;
            return new Outcome(true, $"{W.Learning} {name}", []);
        }

        /// <summary>The fighters an army order goes to, minus those a rule higher up already used this tick.</summary>
        private List<UnitDto> Group(string? who)
        {
            var all = who is null || who.Equals("fighters", StringComparison.OrdinalIgnoreCase)
                ? view.Army
                : Enum.TryParse<UnitType>(who, true, out var type) ? view.Army.Where(u => u.Type == type).ToList() : [];
            return all.Where(u => !_claimed.Contains(u.Id)).ToList();
        }

        private Outcome Attack(string? who, string target)
        {
            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail($"you have no {W.Who(who)} to send.");
            }

            (int X, int Y)? spot;
            if (target == "nearestEnemy")
            {
                var cx = (int)group.Average(u => u.X);
                var cy = (int)group.Average(u => u.Y);
                spot = view.EnemyUnits.Select(u => (u.X, u.Y))
                    .Concat(view.EnemyBuildings.Select(b => (b.X, b.Y)))
                    .OrderBy(p => PlanBot.Dist(p.X, p.Y, cx, cy)).ThenBy(p => p.X).ThenBy(p => p.Y)
                    .Select(p => ((int X, int Y)?)p)
                    .FirstOrDefault();
                if (spot is null)
                {
                    return Outcome.Fail("you can't see any enemies yet.");
                }
            }
            else
            {
                spot = view.AttackTarget();
                if (spot is null)
                {
                    return Outcome.Fail("there's no enemy base left to attack.");
                }
            }

            Order(group, CommandType.Attack, spot.Value, retarget: true);
            return new Outcome(true, $"{group.Count} {W.Who(who, group.Count)} attacking", group.Select(u => u.Id).ToList());
        }

        private Outcome Defend(string? who)
        {
            var threat = Threat();
            if (threat is null)
            {
                return Outcome.Fail("no enemies near your base.");
            }

            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail($"you have no {W.Who(who)} to defend with.");
            }

            Order(group, CommandType.Attack, (threat.X, threat.Y), retarget: false);
            return new Outcome(true, $"{group.Count} {W.Who(who, group.Count)} defending", group.Select(u => u.Id).ToList());
        }

        private Outcome Retreat(string? who)
        {
            var group = Group(who);
            if (group.Count == 0)
            {
                return Outcome.Fail($"you have no {W.Who(who)} to bring home.");
            }

            var rally = view.RallyPoint();
            var away = group.Where(u => PlanBot.Dist(u.X, u.Y, rally.X, rally.Y) > 3).ToList();
            foreach (var u in group)
            {
                _claimed.Add(u.Id);
            }

            if (away.Count == 0)
            {
                return new Outcome(true, $"{group.Count} {W.Who(who, group.Count)} waiting at home", group.Select(u => u.Id).ToList());
            }

            Order(away, CommandType.Move, rally, retarget: false);
            return new Outcome(true, $"{away.Count} {W.Who(who, away.Count)} heading home", away.Select(u => u.Id).ToList());
        }

        // ------------------------------------------------------------ grubs further away

        private IEnumerable<(int Id, int X, int Y, int Remaining)> KnownDeposits() =>
            bot._deposits.Select(kv => (kv.Key, kv.Value.X, kv.Value.Y, kv.Value.Remaining));

        private static bool IsDropOff(BuildingType type) => type is BuildingType.CommandCenter or BuildingType.ResourceDepot;

        /// <summary>Near one of our HQs or Co-op Stores (built, going up, or just ordered).</summary>
        private bool NearBase(int x, int y) =>
            view.MyBuildings.Any(b => IsDropOff(b.Type) && PlanBot.Dist(b.X, b.Y, x, y) <= NearBaseRange)
            || bot._pendingSites.Any(kv => IsDropOff(kv.Value.Type) && PlanBot.Dist(kv.Key.X, kv.Key.Y, x, y) <= NearBaseRange);

        /// <summary>
        /// Opens a new grub field: builds a Co-op Store (or an HQ) beside the nearest pile we know of that isn't
        /// near a drop-off yet. Workers then haul to it, the nearer drop-off. Knowing of no such pile, it
        /// sends a scout to explore instead.
        /// </summary>
        private Outcome Expand(BuildingType type)
        {
            var name = W.Building(type);
            if (bot._pendingSites.Any(kv => IsDropOff(kv.Value.Type)) || view.MyBuildings.Any(b => IsDropOff(b.Type) && !b.Completed))
            {
                return Outcome.Fail($"a {W.Building(BuildingType.ResourceDepot)} or {W.Building(BuildingType.CommandCenter)} is already going up.");
            }

            var cc = view.CommandCenter!;
            var target = KnownDeposits()
                .Where(d => d.Remaining > 0 && !NearBase(d.X, d.Y))
                .OrderBy(d => PlanBot.Dist(d.X, d.Y, cc.X, cc.Y)).ThenBy(d => d.Id)
                .Select(d => ((int X, int Y)?)(d.X, d.Y))
                .FirstOrDefault();
            if (target is null)
            {
                var look = Explore();
                return look.Did
                    ? new Outcome(true, $"looking for more {W.Ore}: {look.Text}", look.UnitIds)
                    : Outcome.Fail($"you don't know where more {W.Ore} is yet, and {look.Text}");
            }

            var cost = view.Rules.Buildings.First(b => b.Type == type).Cost;
            if (_budget < cost)
            {
                return Outcome.Fail($"a {name} costs {cost} {W.Ore} and you have {_budget}.");
            }

            var site = SiteNear(target.Value);
            if (site is null)
            {
                return Outcome.Fail($"there's no room for a {name} next to those {W.Ore}.");
            }

            var builder = Builder(site.Value);
            if (builder is null)
            {
                return Outcome.Fail($"there's no free worker to build a {name}.");
            }

            Commands.Add(new CommandRequest { Type = CommandType.Build, UnitIds = [builder.Id], BuildingType = type, X = site.Value.X, Y = site.Value.Y });
            _claimed.Add(builder.Id);
            bot._pendingSites[site.Value] = (type, Tick);
            _budget -= cost;
            return new Outcome(true, $"building a {name} by new {W.Ore}", [builder.Id]);
        }

        /// <summary>
        /// An open tile two or three steps from a pile, not right beside any pile (those are where workers
        /// dig), in the middle of the most piles, then nearest home.
        /// </summary>
        private (int X, int Y)? SiteNear((int X, int Y) pile)
        {
            var cc = view.CommandCenter!;
            var piles = KnownDeposits().Select(d => (d.X, d.Y)).ToList();
            (int X, int Y)? best = null;
            var bestScore = int.MaxValue;
            for (var y = pile.Y - 3; y <= pile.Y + 3; y++)
            {
                for (var x = pile.X - 3; x <= pile.X + 3; x++)
                {
                    if (PlanBot.Dist(x, y, pile.X, pile.Y) < 2 || !view.IsOpen(x, y) || bot._pendingSites.ContainsKey((x, y))
                        || piles.Any(p => PlanBot.Dist(p.X, p.Y, x, y) <= 1))
                    {
                        continue;
                    }

                    var score = (-10 * piles.Count(p => PlanBot.Dist(p.X, p.Y, x, y) <= 3)) + PlanBot.Dist(x, y, cc.X, cc.Y);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (x, y);
                    }
                }
            }

            return best;
        }

        /// <summary>Sends a scout (or any fighter) to the nearest part of the map the bot hasn't seen yet.</summary>
        private Outcome Explore()
        {
            var scout = view.Army.Where(u => u.Type == UnitType.Scout && !_claimed.Contains(u.Id)).OrderBy(u => u.Id).FirstOrDefault()
                        ?? view.Army.Where(u => !_claimed.Contains(u.Id)).OrderBy(u => u.Id).FirstOrDefault();
            if (scout is null)
            {
                return Outcome.Fail($"you have no scouts (or any fighters) to send exploring.");
            }

            var width = view.Map.Width;

            // The scout stopped short of its spot without seeing it (walled in by rock): give up on that spot.
            if (bot._exploreGoal is { } stuck && scout.Activity == UnitActivity.Idle
                && bot._lastOrder.TryGetValue(scout.Id, out var last) && last.X == stuck.X && last.Y == stuck.Y && Tick - last.Tick > 5)
            {
                bot._seen![(stuck.Y * width) + stuck.X] = true;
                bot._exploreGoal = null;
            }

            if (bot._exploreGoal is null)
            {
                var bestDist = int.MaxValue;
                for (var y = 0; y < view.Map.Height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (bot._seen![(y * width) + x] || view.Map.Terrain[y][x] != '.')
                        {
                            continue;
                        }

                        var d = PlanBot.Dist(x, y, scout.X, scout.Y);
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
                return Outcome.Fail("you've already seen the whole map.");
            }

            _claimed.Add(scout.Id);
            Order([scout], CommandType.Move, goal, retarget: false);
            return new Outcome(true, $"a {W.Unit(scout.Type)} is exploring", [scout.Id]);
        }

        private Outcome Scout()
        {
            var scout = view.Army.Where(u => u.Type == UnitType.Scout && !_claimed.Contains(u.Id)).OrderBy(u => u.Id).FirstOrDefault()
                        ?? view.Army.Where(u => !_claimed.Contains(u.Id)).OrderBy(u => u.Id).FirstOrDefault();
            if (scout is null)
            {
                return Outcome.Fail("you have no scouts (or any fighters) to send.");
            }

            var starts = view.State.Players.Where(p => p.Slot != view.State.You.Slot && !p.Eliminated)
                .OrderBy(p => p.Slot).Select(p => p.StartPosition).ToList();
            if (starts.Count == 0)
            {
                return Outcome.Fail("there's nobody left to find.");
            }

            _claimed.Add(scout.Id);
            var goal = starts[bot._scoutCursor % starts.Count];
            if (scout.Activity == UnitActivity.Idle && PlanBot.Dist(scout.X, scout.Y, goal.X, goal.Y) <= 3)
            {
                bot._scoutCursor++;
                goal = starts[bot._scoutCursor % starts.Count];
            }

            Order([scout], CommandType.Move, (goal.X, goal.Y), retarget: false);
            return new Outcome(true, $"a {W.Unit(scout.Type)} is scouting", [scout.Id]);
        }

        /// <summary>
        /// Sends a group somewhere, but only re-sends units that are idle, were told something else, or (for
        /// a moving target) haven't been re-aimed for a while, so the rule doesn't spam the same order every tick.
        /// </summary>
        private void Order(IReadOnlyList<UnitDto> group, CommandType type, (int X, int Y) spot, bool retarget)
        {
            var kind = type.ToString();
            var ids = new List<int>();
            foreach (var u in group)
            {
                _claimed.Add(u.Id);
                var fresh = bot._lastOrder.TryGetValue(u.Id, out var last)
                            && last.Kind == kind && last.X == spot.X && last.Y == spot.Y
                            && u.Activity != UnitActivity.Idle
                            && (!retarget || Tick - last.Tick < RetargetEveryTicks);
                if (!fresh)
                {
                    ids.Add(u.Id);
                    bot._lastOrder[u.Id] = (kind, spot.X, spot.Y, Tick);
                }
            }

            if (ids.Count > 0)
            {
                Commands.Add(new CommandRequest { Type = type, UnitIds = [.. ids], X = spot.X, Y = spot.Y });
            }
        }

        private UnitDto? Threat() =>
            view.EnemyUnits
                .Where(e => view.MyBuildings.Any(b => PlanBot.Dist(b.X, b.Y, e.X, e.Y) <= DefenseRadius))
                .OrderBy(e => PlanBot.Dist(view.CommandCenter!.X, view.CommandCenter.Y, e.X, e.Y)).ThenBy(e => e.Id)
                .FirstOrDefault();

        // ------------------------------------------------------------ automatic

        public void FinishAbandonedSites()
        {
            foreach (var site in view.MyBuildings.Where(b => !b.Completed))
            {
                if (view.Workers.Any(w => w.TargetId == site.Id || (w.Destination is { } d && d.X == site.X && d.Y == site.Y)))
                {
                    continue;
                }

                var builder = Builder((site.X, site.Y));
                if (builder is not null)
                {
                    _claimed.Add(builder.Id);
                    Commands.Add(new CommandRequest { Type = CommandType.Build, UnitIds = [builder.Id], BuildingType = site.Type, X = site.X, Y = site.Y });
                }
            }
        }

        public void PutIdleWorkersToWork()
        {
            var assigned = view.Workers
                .Where(w => w.TargetId is not null && w.Activity != UnitActivity.Idle)
                .GroupBy(w => w.TargetId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            // Piles we remember count too: the engine takes orders for piles in the fog.
            var dropOffs = view.MyBuildings.Where(b => IsDropOff(b.Type) && b.Completed).ToList();
            if (dropOffs.Count == 0)
            {
                return;
            }

            int Haul((int Id, int X, int Y, int Remaining) d) => dropOffs.Min(b => PlanBot.Dist(b.X, b.Y, d.X, d.Y));
            var piles = KnownDeposits().Where(d => d.Remaining > 0).ToList();
            foreach (var worker in view.Workers.Where(w => w.Activity == UnitActivity.Idle && !_claimed.Contains(w.Id)))
            {
                if (piles.Count == 0)
                {
                    return;
                }

                var deposit = piles
                    .OrderBy(d => Haul(d) + (3 * assigned.GetValueOrDefault(d.Id)))
                    .ThenBy(d => d.Id)
                    .First();
                Commands.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [worker.Id], TargetId = deposit.Id });
                assigned[deposit.Id] = assigned.GetValueOrDefault(deposit.Id) + 1;
            }
        }
    }
}
