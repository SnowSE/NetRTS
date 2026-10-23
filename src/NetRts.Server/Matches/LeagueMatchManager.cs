using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

/// <summary>
/// A preseason, season or championship match. Verified bots join it like any match. It is played in
/// rounds: for each one the admin splits everyone into heats (separate games) and starts them all at
/// once. Between rounds new bots can join, and each round is seated so that bots who were neighbours
/// before are, where possible, not neighbours again. Bots keep using this match's id: the server sends
/// each bot's requests to its heat in the latest round it played.
/// </summary>
public sealed class LeagueMatch(Guid id, string name, MatchCategory category, int? tickIntervalMs, int? maxTicks, DateTime createdAt)
{
    internal readonly object Gate = new();
    internal readonly List<(Seat Seat, DateTime JoinedAt)> Queue = [];
    internal readonly List<LeagueRound> Rounds = [];
    private TaskCompletionSource _changed = NewSignal();

    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public MatchCategory Category { get; } = category;
    public int? TickIntervalMs { get; } = tickIntervalMs;
    public int? MaxTicks { get; } = maxTicks;
    public DateTime CreatedAt { get; } = createdAt;
    public DateTime? StartedAt { get; internal set; }

    /// <summary>The last time a bot joined or the admin split or started a round; idle matches are dropped.</summary>
    internal DateTime LastActivityAt { get; set; } = createdAt;

    /// <summary>True once the admin has started the first round.</summary>
    public bool Started => StartedAt is not null;

    /// <summary>When the admin closed the match: no more rounds, and its points go on the ladder.</summary>
    public DateTime? ClosedAt { get; internal set; }

    public bool Closed => ClosedAt is not null;

    /// <summary>The match's points are on the ladder (see <see cref="LeagueFinalizer"/>).</summary>
    public bool Finalized { get; internal set; }

    /// <summary>Every heat of every round, the split but not yet started round included.</summary>
    internal List<MatchHost> Heats => [.. Rounds.SelectMany(r => r.Heats)];

    /// <summary>The round that has been split but not started, if there is one.</summary>
    internal LeagueRound? PendingRound => Rounds.LastOrDefault() is { Started: false } round ? round : null;

    /// <summary>The latest round that has started.</summary>
    internal LeagueRound? CurrentRound => Rounds.LastOrDefault(r => r.Started);

    /// <summary>A round has started and some of its heats are still being played.</summary>
    internal bool RoundRunning => CurrentRound is { } round && round.Heats.Any(h => h.Status != MatchStatus.Completed);

    /// <summary>Completes the next time the queue, the heats or the status changes.</summary>
    public Task Changed
    {
        get
        {
            lock (Gate)
            {
                return _changed.Task;
            }
        }
    }

    internal void Signal()
    {
        TaskCompletionSource previous;
        lock (Gate)
        {
            previous = _changed;
            _changed = NewSignal();
        }

        previous.TrySetResult();
    }

    internal bool IsQueued(Guid playerId) => Queue.Any(q => q.Seat.PlayerId == playerId);

    /// <summary>The bot's heat in the latest started round it played in.</summary>
    internal MatchHost? PlayedHeatOf(Guid playerId) =>
        Rounds.Where(r => r.Started).Reverse().SelectMany(r => r.Heats).FirstOrDefault(m => m.HasPlayer(playerId));

    internal MatchHost? PendingHeatOf(Guid playerId) => PendingRound?.Heats.FirstOrDefault(m => m.HasPlayer(playerId));

    internal LeagueRound? RoundOf(Guid heatId) => Rounds.FirstOrDefault(r => r.Heats.Any(h => h.Id == heatId));

    /// <summary>Pairs of bots that have started next to each other in a round so far.</summary>
    internal HashSet<(Guid, Guid)> PastNeighbours() =>
        [.. Rounds.Where(r => r.Started).SelectMany(r => r.Heats).SelectMany(h => LeagueSeating.Neighbours(h.Seats.Select(s => s.PlayerId).ToList()))];

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>One round of a league match: everyone in it plays one heat, all at the same time.</summary>
public sealed class LeagueRound(int number, List<MatchHost> heats, int repeatedNeighbours)
{
    /// <summary>1 for the first round.</summary>
    public int Number { get; } = number;

    /// <summary>Its heats, each seated in its planned order.</summary>
    public List<MatchHost> Heats { get; } = heats;

    /// <summary>Pairs of bots seated next to each other again, because no seating could avoid it.</summary>
    public int RepeatedNeighbours { get; } = repeatedNeighbours;

    public DateTime? StartedAt { get; internal set; }
    public bool Started => StartedAt is not null;
}

/// <summary>League matches and their heats. Like live matches, they are held in memory.</summary>
public sealed partial class LeagueMatchManager(MatchManager matches, Presence presence, IOptions<NetRtsOptions> options, ILogger<LeagueMatchManager> logger)
{
    private readonly ConcurrentDictionary<Guid, LeagueMatch> _leagues = new();
    private readonly NetRtsOptions _options = options.Value;

    public IReadOnlyList<LeagueMatch> All => _leagues.Values.OrderByDescending(l => l.CreatedAt).ToList();

    public LeagueMatch? Find(Guid id) => _leagues.GetValueOrDefault(id);

    public LeagueMatch Get(Guid id) =>
        Find(id) ?? throw new MatchException(404, "MATCH_NOT_FOUND", "No league match with that id.");

    public LeagueMatch? Named(string name) =>
        All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public LeagueMatch Create(CreateLeagueMatchRequest request)
    {
        if (request.Category == MatchCategory.Practice)
        {
            throw new MatchException(400, "INVALID_CATEGORY", "League matches are Preseason, Season or Championship; bots create practice matches themselves.");
        }

        var name = request.Name?.Trim() ?? "";
        if (!ValidName().IsMatch(name))
        {
            throw new MatchException(400, "INVALID_NAME", "League match names are 3-28 characters: letters, digits, '_' or '-'.");
        }

        matches.ValidateLeagueSettings(request.TickIntervalMs, request.MaxTicks);
        lock (_leagues)
        {
            if (Named(name) is not null || matches.All.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && m.Status != MatchStatus.Completed))
            {
                throw new MatchException(409, "MATCH_NAME_TAKEN", $"A match named '{name}' is already waiting or running.");
            }

            var league = new LeagueMatch(Guid.NewGuid(), name, request.Category, request.TickIntervalMs, request.MaxTicks, DateTime.UtcNow);
            _leagues[league.Id] = league;
            logger.LogInformation("Created {Category} match {MatchName} ({MatchId})", league.Category, name, league.Id);
            return league;
        }
    }

    public LeagueMatch Join(Guid id, Seat seat)
    {
        var league = Get(id);
        if (!seat.Verified)
        {
            throw new MatchException(403, "NOT_VERIFIED",
                $"{league.Category} matches are for Discord-verified bots. Sign in with Discord on the website and claim this bot first.");
        }

        lock (league.Gate)
        {
            if (league.IsQueued(seat.PlayerId))
            {
                return league;
            }

            EnsureOpen(league);
            EnsureBetweenRounds(league, "MATCH_CLOSED", "isn't taking players until it ends");
            if (league.Queue.FirstOrDefault(q => q.Seat.OwnerAccountId == seat.OwnerAccountId) is { Seat: { } other })
            {
                throw new MatchException(409, "OWNER_ALREADY_JOINED", $"Your bot '{other.Name}' is already in this match; one bot per person.");
            }

            league.Queue.Add((seat, DateTime.UtcNow));
            league.LastActivityAt = DateTime.UtcNow;
        }

        league.Signal();
        return league;
    }

    /// <summary>A bot gives up its place in the rounds to come (only between rounds).</summary>
    public LeagueMatch Leave(Guid id, Guid playerId)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            if (!league.IsQueued(playerId))
            {
                throw new MatchException(409, "CANNOT_LEAVE", "You aren't in this match.");
            }

            EnsureOpen(league);
            EnsureBetweenRounds(league, "CANNOT_LEAVE", "can't be left until it ends; ask the admin to take you out, or surrender");
            league.Queue.RemoveAll(q => q.Seat.PlayerId == playerId);
        }

        league.Signal();
        return league;
    }

    /// <summary>An admin takes a bot out of the rounds to come, and out of its heat if already split.</summary>
    public LeagueMatch Remove(Guid id, Guid playerId)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureOpen(league);
            EnsureNoRoundRunning(league);
            if (league.Queue.RemoveAll(q => q.Seat.PlayerId == playerId) == 0)
            {
                throw new MatchException(404, "NOT_IN_MATCH", "That bot isn't in this match.");
            }

            league.PendingHeatOf(playerId)?.TryLeave(playerId);
        }

        league.Signal();
        return league;
    }

    /// <summary>
    /// Seats everyone in heats of the given sizes for the next round, replacing an earlier split of it.
    /// The heats are created with their players seated (see <see cref="LeagueSeating"/>) but don't
    /// start until <see cref="Start"/>.
    /// </summary>
    public LeagueMatch Split(Guid id, IReadOnlyList<int> sizes)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureOpen(league);
            EnsureNoRoundRunning(league);
            if (sizes.Count == 0 || sizes.Any(n => n is < 2 or > MapGenerator.MaxPlayers))
            {
                throw new MatchException(400, "INVALID_SPLIT", $"Every heat needs 2 to {MapGenerator.MaxPlayers} players.");
            }

            if (sizes.Sum() != league.Queue.Count)
            {
                throw new MatchException(400, "INVALID_SPLIT", $"The sizes add up to {sizes.Sum()}, but {league.Queue.Count} bots have joined.");
            }

            CancelPendingRound(league);
            var number = league.Rounds.Count + 1;
            var (seating, repeats) = LeagueSeating.Plan(league.Queue.Select(q => q.Seat).ToList(), sizes, league.PastNeighbours(), Random.Shared);
            var heats = new List<MatchHost>();
            try
            {
                for (var i = 0; i < seating.Count; i++)
                {
                    var name = number == 1 ? $"{league.Name}-h{i + 1}" : $"{league.Name}-r{number}-h{i + 1}";
                    heats.Add(matches.CreateHeat(league.Id, name, league.Category, seating[i], league.TickIntervalMs, league.MaxTicks));
                }
            }
            catch
            {
                CancelHeats(heats);
                throw;
            }

            league.Rounds.Add(new LeagueRound(number, heats, repeats));
            league.LastActivityAt = DateTime.UtcNow;
            logger.LogInformation("Split round {Round} of {MatchName} into heats of {Sizes} ({Repeats} repeated neighbours)",
                number, league.Name, string.Join(", ", sizes), repeats);
        }

        league.Signal();
        return league;
    }

    /// <summary>Undoes the split of the next round, and lets bots join or leave again.</summary>
    public LeagueMatch Unsplit(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureOpen(league);
            EnsureNoRoundRunning(league);
            CancelPendingRound(league);
        }

        league.Signal();
        return league;
    }

    /// <summary>Starts every heat of the split round at the same moment.</summary>
    public LeagueMatch Start(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureOpen(league);
            EnsureNoRoundRunning(league);
            if (league.PendingRound is not { } round)
            {
                throw new MatchException(409, "NOT_SPLIT", "Split the match into heats before starting the round.");
            }

            if (round.Heats.FirstOrDefault(m => m.Seats.Count < 2) is { } thin)
            {
                throw new MatchException(409, "INVALID_SPLIT", $"{thin.Name} has fewer than 2 players left; split the match again.");
            }

            foreach (var heat in round.Heats)
            {
                matches.StartHeat(heat);
            }

            round.StartedAt = DateTime.UtcNow;
            league.StartedAt ??= round.StartedAt;
            league.LastActivityAt = DateTime.UtcNow;
            logger.LogInformation("Started round {Round} of {MatchName}: {Count} heats", round.Number, league.Name, round.Heats.Count);
        }

        league.Signal();
        return league;
    }

    /// <summary>
    /// Ends a started match between rounds (dropping a round that was split but not started): no more
    /// rounds or players. <see cref="LeagueFinalizer"/> then puts its points on the ladder.
    /// </summary>
    public LeagueMatch Close(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            if (league.Closed)
            {
                return league;
            }

            if (!league.Started)
            {
                throw new MatchException(409, "MATCH_NOT_STARTED", "No round has been played yet; call the match off instead.");
            }

            EnsureNoRoundRunning(league);
            CancelPendingRound(league);
            league.ClosedAt = DateTime.UtcNow;
            league.LastActivityAt = DateTime.UtcNow;
            logger.LogInformation("Closed {MatchName} after {Rounds} rounds", league.Name, league.Rounds.Count);
        }

        league.Signal();
        return league;
    }

    /// <summary>Every heat of every started round has been saved, so the match's points can be added up.</summary>
    public bool AllHeatsRecorded(LeagueMatch league)
    {
        lock (league.Gate)
        {
            return league.Rounds.Where(r => r.Started).SelectMany(r => r.Heats).All(h => h.Recorded);
        }
    }

    public void MarkFinalized(LeagueMatch league)
    {
        lock (league.Gate)
        {
            league.Finalized = true;
        }

        league.Signal();
    }

    public LeagueMatch Cancel(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            if (league.Started)
            {
                throw new MatchException(409, "MATCH_ALREADY_STARTED", "The match has already started.");
            }

            CancelPendingRound(league);
            _leagues.TryRemove(league.Id, out _);
        }

        league.Signal();
        return league;
    }

    /// <summary>
    /// The heat a bot plays in: its heat in the latest round it played. With <paramref name="nextGame"/>
    /// (a bot waiting for a game to start) a finished heat doesn't count, so the bot waits for the next
    /// round. Waits up to <paramref name="wait"/> for one (so a state long-poll on the league match
    /// simply lasts until the admin starts the round); null if there still isn't one.
    /// </summary>
    public async Task<MatchHost?> HeatForAsync(LeagueMatch league, Guid playerId, bool nextGame, TimeSpan wait, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        while (true)
        {
            var changed = league.Changed;
            lock (league.Gate)
            {
                var heat = league.PlayedHeatOf(playerId);
                if (!league.IsQueued(playerId) && heat is null)
                {
                    throw new MatchException(403, "NOT_A_PARTICIPANT", "You are not playing in this match.");
                }

                if (heat is not null && !(nextGame && heat.Status == MatchStatus.Completed))
                {
                    return heat;
                }

                if (league.Closed)
                {
                    throw new MatchException(409, "MATCH_FINISHED", "This match has been closed; there are no more rounds.");
                }
            }

            if (wait <= TimeSpan.Zero)
            {
                return null;
            }

            try
            {
                await changed.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    /// <summary>The bot's heat in the latest round it played (for endpoints that don't wait).</summary>
    public MatchHost? StartedHeatOf(LeagueMatch league, Guid playerId)
    {
        lock (league.Gate)
        {
            return league.PlayedHeatOf(playerId);
        }
    }

    /// <summary>
    /// Drops closed league matches once their points are on the ladder and their heats have left
    /// memory, and others that have sat idle (nobody joined, split or started a round) for the league
    /// timeout. A match dropped without being closed is finalized later by <see cref="LeagueFinalizer"/>.
    /// </summary>
    public void Housekeep(DateTime now)
    {
        foreach (var league in _leagues.Values)
        {
            bool stale;
            lock (league.Gate)
            {
                var heatsGone = league.Rounds.Where(r => r.Started).SelectMany(r => r.Heats).All(m => matches.Get(m.Id) is null);
                stale = league.Closed
                    ? league.Finalized && heatsGone
                    : now - league.LastActivityAt > TimeSpan.FromMinutes(_options.LeagueMatchTimeoutMinutes) && heatsGone;
                if (stale)
                {
                    CancelPendingRound(league);
                }
            }

            if (stale && _leagues.TryRemove(league.Id, out _))
            {
                league.Signal();
                logger.LogInformation("Removed league match {MatchName}", league.Name);
            }
        }
    }

    public MatchSummaryDto ToSummary(LeagueMatch league)
    {
        lock (league.Gate)
        {
            var heats = league.Rounds.SelectMany(r => r.Heats.Select(h => (Heat: h, Round: r.Number, Summary: h.ToSummary()))).ToList();
            // Between rounds a match counts as completed, though until it is closed bots can join and the
            // admin can split and start another round.
            var status = !league.Started ? MatchStatus.Waiting
                : league.RoundRunning || league.PendingRound is not null ? MatchStatus.Active
                : MatchStatus.Completed;
            return new MatchSummaryDto
            {
                MatchId = league.Id,
                Name = league.Name,
                Category = league.Category,
                Status = status,
                MaxPlayers = 0,
                Tick = heats.Count == 0 ? 0 : heats.Max(h => h.Summary.Tick),
                MaxTicks = league.MaxTicks ?? _options.DefaultMaxTicks,
                TickIntervalMs = league.TickIntervalMs ?? _options.DefaultTickIntervalMs,
                CreatedAt = new DateTimeOffset(league.CreatedAt, TimeSpan.Zero),
                Players = league.Queue.Select((q, i) => new MatchPlayerDto
                {
                    PlayerId = q.Seat.PlayerId,
                    Name = q.Seat.Name,
                    Slot = i,
                    Verified = q.Seat.Verified,
                    OwnerName = q.Seat.OwnerName,
                    Connected = presence.IsConnected(q.Seat.PlayerId),
                }).ToList(),
                Closed = league.Closed,
                Finalized = league.Finalized,
                Heats = heats.Select(h => new HeatDto
                {
                    MatchId = h.Heat.Id,
                    Name = h.Heat.Name ?? "",
                    Round = h.Round,
                    Status = h.Summary.Status,
                    PlayerIds = h.Summary.Players.Select(p => p.PlayerId).ToList(),
                }).ToList(),
            };
        }
    }

    /// <summary>Standings of each heat (or of one ordinary match), best first.</summary>
    public IReadOnlyList<HeatStandingsDto> Standings(LeagueMatch league)
    {
        List<(MatchHost Heat, int Round)> heats;
        lock (league.Gate)
        {
            heats = [.. league.Rounds.SelectMany(r => r.Heats.Select(h => (h, r.Number)))];
        }

        return heats.Select(h => StandingsOf(h.Heat, h.Round)).ToList();
    }

    public static HeatStandingsDto StandingsOf(MatchHost host) => StandingsOf(host, 1);

    public static HeatStandingsDto StandingsOf(MatchHost host, int round)
    {
        var summary = host.ToSummary();
        var seats = summary.Players.ToDictionary(p => p.PlayerId);
        var winnerId = summary.Outcome?.WinnerId;
        var rows = host.GetSpectatorView(null, false) is { } view
            ? view.Players.Select(p => (Id: p.PlayerId, Score: p.Score.Total, p.Eliminated, Winner: p.PlayerId == winnerId)).ToList()
            : summary.Players.Select(p => (Id: p.PlayerId, Score: 0, Eliminated: false, Winner: false)).ToList();
        rows = rows.OrderByDescending(r => r.Winner).ThenByDescending(r => r.Score).ToList();
        var points = LeaguePoints.Award(rows.Select(r => r.Score).ToList());
        return new HeatStandingsDto
        {
            MatchId = host.Id,
            Name = host.Name ?? "",
            Round = round,
            Status = summary.Status,
            Tick = summary.Tick,
            MaxTicks = summary.MaxTicks,
            Outcome = summary.Outcome,
            Players = rows.Select((r, i) =>
            {
                var seat = seats.GetValueOrDefault(r.Id);
                return new StandingDto
                {
                    PlayerId = r.Id,
                    Name = seat?.Name ?? "",
                    Slot = seat?.Slot ?? 0,
                    Verified = seat?.Verified ?? false,
                    OwnerName = seat?.OwnerName,
                    Score = r.Score,
                    Eliminated = r.Eliminated,
                    Winner = r.Winner,
                    Place = 1 + rows.Count(q => (q.Winner && !r.Winner) || (q.Winner == r.Winner && q.Score > r.Score)),
                    Points = points[i],
                };
            }).ToList(),
        };
    }

    private static void EnsureOpen(LeagueMatch league)
    {
        if (league.Closed)
        {
            throw new MatchException(409, "MATCH_FINISHED", "This match has been closed.");
        }
    }

    private static void EnsureNoRoundRunning(LeagueMatch league)
    {
        if (league.RoundRunning)
        {
            throw new MatchException(409, "ROUND_RUNNING", $"Round {league.CurrentRound!.Number} is still being played; wait for every heat to finish.");
        }
    }

    /// <summary>Bots can join or leave only between rounds: not while one is split or being played.</summary>
    private static void EnsureBetweenRounds(LeagueMatch league, string code, string what)
    {
        if (league.RoundRunning)
        {
            throw new MatchException(409, code, $"Round {league.CurrentRound!.Number} of this match is being played, and it {what}.");
        }

        if (league.PendingRound is { } pending)
        {
            throw new MatchException(409, code, $"Round {pending.Number} of this match has been split into heats, and it {what}.");
        }
    }

    private void CancelPendingRound(LeagueMatch league)
    {
        if (league.PendingRound is { } round)
        {
            CancelHeats(round.Heats);
            league.Rounds.Remove(round);
        }
    }

    private void CancelHeats(IEnumerable<MatchHost> heats)
    {
        foreach (var heat in heats)
        {
            try
            {
                matches.Cancel(heat.Id);
            }
            catch (MatchException)
            {
                // Already gone (e.g. timed out).
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{3,28}$")]
    private static partial Regex ValidName();
}
