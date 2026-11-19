using NetRts.Bots.Pickup;
using NetRts.Protocol;
using NetRts.Server.Matches;

namespace NetRts.Server.Pickup;

/// <summary>
/// The pick-up game's king of the hill (#/pickup). Phones join a line; the champion (a student, or the house
/// playing one of the decks) takes on up to three challengers at a time; winner stays on, and a champion retires
/// after <see cref="RetireAfter"/> wins. With nobody ready, the house keeps a match going while
/// someone is watching, and that match gives way the moment a student is ready. Everything lives in memory:
/// names, plans and results are gone when the server stops, and the matches are never recorded.
/// </summary>
public sealed class PickupHill(MatchManager matches, ILogger<PickupHill> logger, IReadOnlyList<PickupDeckDto>? houseDecks = null)
{
    public static readonly TimeSpan FirstPlanning = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AgainPlanning = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Unseen = TimeSpan.FromMinutes(3);   // a phone that sleeps a while keeps its place
    public static readonly TimeSpan Watched = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Forgotten = TimeSpan.FromMinutes(15);

    /// <summary>A champion whose phone hasn't checked in for this long has walked away: the house takes the hill.</summary>
    public static readonly TimeSpan ChampionAway = TimeSpan.FromSeconds(30);
    public const int RetireAfter = 5;
    public const int MaxChallengers = 3;
    /// <summary>Phones the hill keeps at once (the line, the champion, and recent results); past it, joins wait.</summary>
    public const int MaxEntries = 300;

    /// <summary>The podium's order: the winner first (last one standing, or the champion on a tie), then by score.</summary>
    public static List<PickupPlayerDto> Standings(IEnumerable<PickupPlayerDto> players, string winner) =>
        players.OrderByDescending(p => p.Name == winner).ThenByDescending(p => p.Score).ToList();

    private enum Phase
    {
        Line,
        Countdown,
        Playing,
        Over,
    }

    private sealed class Entry(string token, string name, DateTime now)
    {
        public string Token { get; } = token;
        public string Name { get; } = name;
        public PickupPlanDto Plan { get; set; } = PickupPlans.Decks[0].Plan;
        public Phase Phase { get; set; } = Phase.Line;
        public DateTime ReadyAt { get; set; } = now + FirstPlanning;
        public DateTime SeenAt { get; set; } = now;
        public PickupResultDto? Result { get; set; }
        public DateTime OverAt { get; set; }

        /// <summary>Not ready yet: keeps the place in line, but the next ready player goes ahead.</summary>
        public bool Holding { get; set; }
    }

    /// <summary>Who plays a seat in a round: a student, or the house with a deck.</summary>
    private sealed record Player(Entry? Student, PickupDeckDto? Deck)
    {
        public string Name => Student?.Name ?? $"{Deck!.HouseName} (house)";
        public PickupPlanDto Plan => Student?.Plan ?? Deck!.Plan;
    }

    private sealed class Round(List<Player> players, DateTime startsAt, bool forStudents)
    {
        public List<Player> Players { get; } = players;
        public DateTime StartsAt { get; } = startsAt;

        /// <summary>False for a round with no challenger from the line: it gives way when a student is ready.</summary>
        public bool ForStudents { get; } = forStudents;
        public MatchHost? Host { get; set; }
        public Dictionary<Guid, (Player Player, PickupBot Bot)> Seats { get; } = [];
    }

    private readonly IReadOnlyList<PickupDeckDto> _decks = houseDecks ?? PickupPlans.Decks;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly List<Entry> _line = [];
    private readonly Random _rng = new();
    private Entry? _champion;
    private int _championWins;
    private int _houseDeck;
    private int _nextDeck = 1;
    private Round? _round;
    private DateTime _watchedAt = DateTime.MinValue;

    private PickupDeckDto HouseChampion => _decks[_houseDeck % _decks.Count];

    // ------------------------------------------------------------ phones

    /// <summary>
    /// Joins the line (a new name and the first deck as the plan), or picks up where a known token left off. A
    /// phone the hill doesn't know (the server restarted) keeps its token, and the name it had if that's one of
    /// ours and nobody has it now.
    /// </summary>
    public PickupMeDto Join(string? token, DateTime now, string? name = null)
    {
        lock (_gate)
        {
            if (token is not null && _entries.TryGetValue(token, out var known))
            {
                return MeLocked(known, now);
            }

            if (_entries.Count >= MaxEntries)
            {
                throw new MatchException(503, "LINE_FULL", "The line is full right now. Try again in a minute.");
            }

            var taken = _entries.Values.Select(e => e.Name).ToHashSet();
            var keepToken = token is { Length: 32 } && token.All(char.IsAsciiHexDigitLower);
            var keepName = PickupNames.IsName(name) && !taken.Contains(name!);
            var entry = new Entry(keepToken ? token! : Guid.NewGuid().ToString("N"), keepName ? name! : PickupNames.Pick(_rng, taken), now);
            _entries[entry.Token] = entry;
            _line.Add(entry);
            logger.LogInformation("Pick-up: {Name} joined the line ({Count} in line)", entry.Name, _line.Count);
            return MeLocked(entry, now);
        }
    }

    public PickupMeDto Me(string token, DateTime now)
    {
        lock (_gate)
        {
            return MeLocked(Find(token), now);
        }
    }

    /// <summary>Stores a plan (null), or says what's wrong with it.</summary>
    public string? SetPlan(string token, PickupPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (PickupPlans.Validate(plan) is { } error)
        {
            return error;
        }

        lock (_gate)
        {
            var entry = Find(token);
            entry.Plan = plan;
            if (entry.Phase == Phase.Playing && _round?.Host is not null)
            {
                // Playing: the bot takes the new plan from its next tick.
                foreach (var (player, bot) in _round.Seats.Values.Where(s => s.Player.Student == entry))
                {
                    bot.Apply(plan);
                }
            }

            return null;
        }
    }

    /// <summary>Not ready yet (or ready again): the place in line stays; while holding, the next ready player goes ahead.</summary>
    public PickupMeDto Hold(string token, bool hold, DateTime now)
    {
        lock (_gate)
        {
            var entry = Find(token);
            entry.Holding = hold;
            if (!hold && entry.ReadyAt > now)
            {
                entry.ReadyAt = now;
            }

            return MeLocked(entry, now);
        }
    }

    /// <summary>Skips the rest of the planning time.</summary>
    public PickupMeDto StartNow(string token, DateTime now)
    {
        lock (_gate)
        {
            var entry = Find(token);
            if (entry.ReadyAt > now)
            {
                entry.ReadyAt = now;
            }

            return MeLocked(entry, now);
        }
    }

    /// <summary>After a match: back in line with a little planning time (the champion just waits for a challenger).</summary>
    public PickupMeDto Rejoin(string token, DateTime now)
    {
        lock (_gate)
        {
            var entry = Find(token);
            if (entry.Phase == Phase.Over)
            {
                entry.Phase = Phase.Line;
                entry.Result = null;
                entry.Holding = false;
                if (entry != _champion && !_line.Contains(entry))
                {
                    entry.ReadyAt = now + AgainPlanning;
                    _line.Add(entry);
                }
            }

            return MeLocked(entry, now);
        }
    }

    /// <summary>Attack now / Pull back, for a phone whose bot is playing.</summary>
    public void Order(string token, string order)
    {
        lock (_gate)
        {
            var entry = Find(token);
            var seat = _round?.Host is null ? default : _round.Seats.Values.FirstOrDefault(s => s.Player.Student == entry);
            if (entry.Phase != Phase.Playing || seat.Bot is null)
            {
                throw new MatchException(409, "NOT_PLAYING", "Your bot isn't playing right now.");
            }

            switch (order)
            {
                case "attack":
                    seat.Bot.AttackNow();
                    break;
                case "pullback":
                    seat.Bot.PullBack();
                    break;
                default:
                    throw new MatchException(400, "INVALID_ORDER", "The orders are attack and pullback.");
            }
        }
    }

    // ------------------------------------------------------------ the big screen

    public PickupHillDto Hill(DateTime now)
    {
        lock (_gate)
        {
            _watchedAt = now;
            var round = _round;
            var playing = round?.Host is { } host ? PlayersOf(round, host, null) : round?.Players.Select(p => new PickupPlayerDto
            {
                Name = p.Name, House = p.Student is null, Champion = IsChampion(p),
            }).ToList() ?? [];
            return new PickupHillDto
            {
                MatchId = round?.Host?.Id,
                Phase = round is null ? "idle" : round.Host is null ? "countdown" : "playing",
                Champion = new PickupPlayerDto { Name = _champion?.Name ?? $"{HouseChampion.HouseName} (house)", House = _champion is null, Champion = true },
                ChampionWins = _championWins,
                Playing = playing,
                Line = _line.Where(e => e != _champion).Take(10).Select(e => e.Name).ToList(),
                SecondsLeft = round is { Host: null } ? Seconds(round.StartsAt - now) : null,
                Warmup = round is { ForStudents: false },
            };
        }
    }

    // ------------------------------------------------------------ the loop

    /// <summary>Moves the hill along: drops phones that went away, settles finished matches, starts the next one.</summary>
    public void Update(DateTime now)
    {
        lock (_gate)
        {
            Tidy(now);
            if (_round?.Host is { } host)
            {
                if (host.Status == MatchStatus.Completed)
                {
                    Settle(_round, host, now);
                    _round = null;
                }
                else if (!_round.ForStudents && ReadyChallengers(now).Count > 0)
                {
                    GiveWay(_round, host);
                }
            }
            else if (_round is { } waiting)
            {
                if (!waiting.ForStudents && ReadyChallengers(now).Count > 0)
                {
                    GiveWay(waiting, null);
                }
                else if (now >= waiting.StartsAt)
                {
                    Begin(waiting);
                }
            }

            if (_round is null && _champion is { } champ && now - champ.SeenAt > ChampionAway)
            {
                logger.LogInformation("Pick-up: {Name} walked away; the house takes the hill", champ.Name);
                if (champ.Phase != Phase.Over)
                {
                    champ.Phase = Phase.Over;
                }

                Crown(null, deck: _nextDeck++);
            }

            if (_round is null)
            {
                Next(now);
            }
        }
    }

    private void Tidy(DateTime now)
    {
        foreach (var gone in _line.Where(e => e != _champion && e.Phase == Phase.Line && now - e.SeenAt > Unseen).ToList())
        {
            _line.Remove(gone);
            _entries.Remove(gone.Token);
        }

        foreach (var old in _entries.Values.Where(e => e != _champion && e.Phase == Phase.Over && now - e.SeenAt > Forgotten).ToList())
        {
            _entries.Remove(old.Token);
        }
    }

    /// <summary>The ready students at the front of the line: none unless the very first one is ready.</summary>
    private List<Entry> ReadyChallengers(DateTime now)
    {
        var line = _line.Where(e => e != _champion && e.Phase == Phase.Line && !e.Holding).ToList();
        return line.Count == 0 || line[0].ReadyAt > now ? [] : line.Where(e => e.ReadyAt <= now).Take(MaxChallengers).ToList();
    }

    private void Next(DateTime now)
    {
        var challengers = ReadyChallengers(now);
        var champion = new Player(_champion, _champion is null ? HouseChampion : null);
        if (challengers.Count > 0)
        {
            foreach (var c in challengers)
            {
                _line.Remove(c);
            }

            Prepare(new Round([champion, .. challengers.Select(c => new Player(c, null))], now + Countdown, forStudents: true));
            return;
        }

        // Nobody waiting: a champion who just won gets a moment on the result screen before the house comes.
        if (now - _watchedAt > Watched || (_champion is { Phase: Phase.Over } winner && now - winner.OverAt < AgainPlanning))
        {
            return;
        }

        // Nobody ready: the house keeps the screen busy, a deck other than the champion's.
        var deck = _decks[_nextDeck % _decks.Count];
        if (_decks.Count > 1 && _champion is null && deck == HouseChampion)
        {
            deck = _decks[++_nextDeck % _decks.Count];
        }

        _nextDeck++;
        Prepare(new Round([champion, new Player(null, deck)], _champion is null ? now : now + Countdown, forStudents: false));
    }

    private void Prepare(Round round)
    {
        foreach (var p in round.Players.Where(p => p.Student is not null))
        {
            p.Student!.Phase = Phase.Countdown;
            p.Student.Result = null;
        }

        _round = round;
        if (round.Players.All(p => p.Student is null))
        {
            Begin(round);
        }
    }

    private void Begin(Round round)
    {
        var seats = new List<(Seat, Bots.IBotStrategy, Engine.StartKit)>();
        foreach (var p in round.Players)
        {
            var plan = PickupPlans.Validate(p.Plan) is null ? p.Plan : PickupPlans.Decks[0].Plan;
            var seat = new Seat(Guid.NewGuid(), p.Name, IsHouseBot: p.Student is null, IsBlockBot: p.Student is not null);
            var bot = new PickupBot(p.Name, plan, MatchManager.PickupTreatyTicks, MatchManager.PickupTickMs);
            round.Seats[seat.PlayerId] = (p, bot);
            seats.Add((seat, bot, PickupPlans.KitFor(plan)));
            if (p.Student is not null)
            {
                p.Student.Phase = Phase.Playing;
            }
        }

        try
        {
            round.Host = matches.CreatePickup(seats);
            logger.LogInformation("Pick-up match {MatchId}: {Players}", round.Host.Id, string.Join(" vs ", round.Players.Select(p => p.Name)));
        }
        catch (Exception ex)
        {
            // Never leave students stuck "playing" a match that didn't start: back in line, try again next update.
            logger.LogError(ex, "Pick-up match not started");
            GiveWay(round, null);
        }
    }

    /// <summary>A round that gives way (or couldn't start): no result; challengers go back to the front of the line.</summary>
    private void GiveWay(Round round, MatchHost? host)
    {
        host?.EndAfterThisTick();
        var students = round.Players.Where(p => p.Student is not null && p.Student != _champion).Select(p => p.Student!).ToList();
        _line.InsertRange(0, students);
        foreach (var s in round.Players.Where(p => p.Student is not null).Select(p => p.Student!))
        {
            s.Phase = Phase.Line;
        }

        _round = null;
    }

    private void Settle(Round round, MatchHost host, DateTime now)
    {
        var winnerId = host.GetResult()?.Outcome?.WinnerId;
        var winner = winnerId is { } id && round.Seats.TryGetValue(id, out var w) ? w.Player : null;
        var champion = round.Players[0];
        winner ??= champion;   // a tie keeps the champion
        var players = Standings(PlayersOf(round, host, null), winner.Name);
        var view = host.GetSpectatorView(null, includeVisibility: false);

        foreach (var (seatId, (player, bot)) in round.Seats)
        {
            if (player.Student is not { } student)
            {
                continue;
            }

            var slot = view?.Players.FirstOrDefault(p => p.PlayerId == seatId)?.Slot ?? -1;
            var fighters = view?.Units.Count(u => u.Owner == slot && u.Type != UnitType.Worker) ?? 0;
            var mine = players.FindIndex(p => p.Name == student.Name);
            student.Result = new PickupResultDto
            {
                Place = mine + 1,
                Won = player == winner,
                Players = players.Select(p => p with { You = p.Name == student.Name }).ToList(),
                StepsDone = Math.Min(bot.CurrentStep, player.Plan.Steps.Count),
                Steps = player.Plan.Steps.Count,
                FightersTrained = bot.Trained,
                FightersLost = Math.Max(0, bot.StartFighters + bot.Trained - fighters),
                Buildings = view?.Buildings.Count(b => b.Owner == slot) ?? 0,
                Classes = view?.Players.FirstOrDefault(p => p.PlayerId == seatId)?.Upgrades.Count ?? 0,
            };
            student.Phase = Phase.Over;
            student.OverAt = now;
        }

        // Every win counts, against students or the house, the one that takes the hill included.
        if (winner == champion)
        {
            if (_champion is not null && ++_championWins >= RetireAfter)
            {
                logger.LogInformation("Pick-up: {Name} retires undefeated after {Wins} wins", _champion.Name, _championWins);
                Crown(null, deck: _nextDeck++);
            }
        }
        else if (winner.Student is { } student)
        {
            Crown(student, deck: _houseDeck);
            _championWins = 1;
        }
        else
        {
            Crown(null, deck: _decks.ToList().IndexOf(winner.Deck!));
        }
    }

    private void Crown(Entry? student, int deck)
    {
        _champion = student;
        _championWins = 0;
        _houseDeck = deck;
        if (student is not null)
        {
            _line.Remove(student);
        }
    }

    // ------------------------------------------------------------ views

    private bool IsChampion(Player p) => p.Student is null ? _champion is null && p.Deck == HouseChampion : p.Student == _champion;

    private List<PickupPlayerDto> PlayersOf(Round round, MatchHost host, Entry? you)
    {
        var view = host.GetSpectatorView(null, includeVisibility: false);
        if (view is null)
        {
            return [];
        }

        return view.Players.Select(p =>
        {
            var (player, _) = round.Seats[p.PlayerId];
            return new PickupPlayerDto
            {
                Name = player.Name,
                Slot = p.Slot,
                You = you is not null && player.Student == you,
                Champion = player == round.Players[0],
                House = player.Student is null,
                Grubs = p.Resources,
                Workers = view.Units.Count(u => u.Owner == p.Slot && u.Type == UnitType.Worker),
                Fighters = view.Units.Count(u => u.Owner == p.Slot && u.Type != UnitType.Worker),
                Buildings = view.Buildings.Count(b => b.Owner == p.Slot),
                Score = p.Score.Total,
            };
        }).ToList();
    }

    private PickupMeDto MeLocked(Entry entry, DateTime now)
    {
        entry.SeenAt = now;
        _watchedAt = now;
        var place = _line.IndexOf(entry);
        var phase = entry.Phase switch
        {
            Phase.Countdown => "countdown",
            Phase.Playing => "playing",
            Phase.Over => "over",
            _ when place == FrontPlace(entry) && !entry.Holding && (entry.ReadyAt > now || Busy(now) > TimeSpan.Zero) => "next",
            _ => "line",
        };
        var round = _round is not null && _round.Players.Any(p => p.Student == entry) ? _round : null;
        var bot = round?.Seats.Values.Where(s => s.Player.Student == entry).Select(s => s.Bot).FirstOrDefault();
        return new PickupMeDto
        {
            Token = entry.Token,
            Name = entry.Name,
            Phase = phase,
            Place = place >= 0 && entry != _champion ? place + 1 : null,
            SecondsLeft = phase switch
            {
                // When the match really starts: the planning time left, or the student match on now, then the countdown.
                "next" => Seconds(Max(entry.ReadyAt - now, Busy(now)) + Countdown),
                "countdown" when round is not null => Seconds(round.StartsAt - now),
                _ => null,
            },
            MatchId = round?.Host?.Id,
            Champion = entry == _champion,
            Wins = entry == _champion ? _championWins : 0,
            Holding = entry.Holding,
            Step = bot?.CurrentStep ?? 0,
            Waiting = phase == "playing" ? bot?.Waiting : null,
            Steps = entry.Plan.Steps.Count,
            Players = phase == "playing" && round?.Host is { } host ? PlayersOf(round, host, entry) : [],
            Result = entry.Result,
            Plan = entry.Plan,
        };
    }

    /// <summary>
    /// How long until the hill is free for the next round: the students' round on now (its countdown and what's
    /// left of its match). A house round doesn't count: it gives way to a ready player.
    /// </summary>
    private TimeSpan Busy(DateTime now)
    {
        if (_round is not { ForStudents: true } round)
        {
            return TimeSpan.Zero;
        }

        var match = TimeSpan.FromMilliseconds((double)MatchManager.PickupMaxTicks * MatchManager.PickupTickMs);
        if (round.Host is not { } host)
        {
            return round.StartsAt - now + match;
        }

        var left = Math.Max(0, host.Settings.MaxTicks - host.ToSummary().Tick);
        return host.Status == MatchStatus.Completed ? TimeSpan.Zero : TimeSpan.FromMilliseconds((double)left * host.TickIntervalMs);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>Where the front of the line is for this entry: 0, or later when everyone ahead is holding.</summary>
    private int FrontPlace(Entry entry) => _line.FindIndex(e => e == entry || !e.Holding);

    private Entry Find(string token) =>
        _entries.TryGetValue(token, out var entry) ? entry : throw new MatchException(404, "UNKNOWN_TOKEN", "That phone isn't in the pick-up game; join again.");

    private static int Seconds(TimeSpan left) => Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
}
