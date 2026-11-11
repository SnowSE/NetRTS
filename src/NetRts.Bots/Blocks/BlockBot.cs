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
/// <para>
/// The actions themselves are the shared ones the house bots use too (<see cref="BotTurn"/>, with
/// <see cref="BotMemory"/> and <see cref="BotView"/>); this class adds the rules, and turns each
/// <see cref="Attempt"/> into a sentence (BlockBot.Rules.cs).
/// </para>
/// </summary>
public sealed partial class BlockBot : IBotStrategy
{
    private const int DefenseRadius = 10;
    private const int TicksPerMinute = 60;

    /// <summary>A grub pile this close to an HQ or Co-op Store counts as "near my base".</summary>
    private const int NearBaseRange = 8;

    /// <summary>What an action uses when a field is left out (also served to editors in the vocabulary).</summary>
    private static readonly BlockDefaultsDto Defaults = new();

    private readonly object _lock = new();
    private readonly List<BlockVersionDto> _versions = [];
    private readonly Dictionary<string, bool> _switches = new(StringComparer.OrdinalIgnoreCase);
    private BlockProgramDto _program;
    private IReadOnlyList<RuleStatus> _ruleStatus = [];
    private BlockStatsDto _stats = new();
    private int _tick;
    private string? _error;

    // Memory between ticks: what the bot has seen and ordered (shared with the house bots), plus where each
    // trainMix is in its list, which enemy base the scout heads for, and the spot it's exploring.
    private readonly BotMemory _memory = new();
    private readonly Dictionary<(int Rule, int Action), int> _mixCursor = [];
    private int _scoutCursor;
    private (int X, int Y)? _exploreGoal;

    // What was hurt lately (the underAttack check), and when each rule last ran (Once, EverySeconds), by the
    // rule's text: an edited rule starts over, a moved one doesn't.
    private readonly AttackWatch _attacks = new();
    private readonly Dictionary<string, int> _lastRun = [];

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

    /// <summary>What the bot did on the last tick it played, in the words of the theme the page shows.</summary>
    public BlockBotStatusDto Status(Guid matchId, MatchStatus matchStatus, int slot, BlockWords? words = null)
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
                Rules = _ruleStatus.Select(r => r.Render(words ?? BlockWords.Snow)).ToList(),
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

        _memory.Update(view);
        _attacks.Update(view);
        if (_exploreGoal is { } goal && _memory.HasSeen(goal.X, goal.Y))
        {
            _exploreGoal = null;
        }

        var turn = new BotTurn(view, _memory);
        var runner = new Runner(this, view, turn, switches);
        var statuses = new List<RuleStatus>(program.Rules.Count);
        string? error = null;
        try
        {
            for (var i = 0; i < program.Rules.Count; i++)
            {
                statuses.Add(runner.RunRule(i, program.Rules[i]));
            }

            turn.FinishAbandonedSites();
            turn.GatherIdleWorkers(GatherStyle.RememberedNearDropOff);
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

    /// <summary>
    /// A sentence waiting for its words: what a rule did is worked out on the tick, but said in whichever theme
    /// the page asking shows. A phrase may only use values that don't change after it's made.
    /// </summary>
    private delegate string Phrase(BlockWords words);

    private readonly record struct Check(bool Value, Phrase Why, int? Progress = null);

    private readonly record struct Outcome(bool Did, Phrase Text, IReadOnlyList<int> UnitIds)
    {
        public static Outcome Fail(Phrase text) => new(false, text, []);
    }

    private sealed record RuleStatus(int Index, string State, Phrase Why, IReadOnlyList<int>? UnitIds = null, int? Progress = null)
    {
        public BlockRuleStatusDto Render(BlockWords words) =>
            new() { Index = Index, State = State, Why = Why(words), UnitIds = UnitIds ?? [], Progress = Progress };
    }
}
