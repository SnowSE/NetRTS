using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

/// <summary>
/// A preseason, season or championship match. Verified bots join it like any match; the admin splits
/// everyone into heats (separate games) and starts them all at once. Bots keep using this match's id:
/// the server sends each bot's requests to its own heat.
/// </summary>
public sealed class LeagueMatch(Guid id, string name, MatchCategory category, int? tickIntervalMs, int? maxTicks, DateTime createdAt)
{
    internal readonly object Gate = new();
    internal readonly List<(Seat Seat, DateTime JoinedAt)> Queue = [];
    internal readonly List<MatchHost> Heats = [];
    private TaskCompletionSource _changed = NewSignal();

    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public MatchCategory Category { get; } = category;
    public int? TickIntervalMs { get; } = tickIntervalMs;
    public int? MaxTicks { get; } = maxTicks;
    public DateTime CreatedAt { get; } = createdAt;
    public DateTime? StartedAt { get; internal set; }

    /// <summary>True once the admin has started the heats.</summary>
    public bool Started => StartedAt is not null;

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

    internal MatchHost? HeatOf(Guid playerId) => Heats.FirstOrDefault(m => m.HasPlayer(playerId));

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
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

            if (league.Heats.Count > 0 || league.Started)
            {
                throw new MatchException(409, "MATCH_CLOSED", "This match has been split into heats and isn't taking players any more.");
            }

            if (league.Queue.FirstOrDefault(q => q.Seat.OwnerAccountId == seat.OwnerAccountId) is { Seat: { } other })
            {
                throw new MatchException(409, "OWNER_ALREADY_JOINED", $"Your bot '{other.Name}' is already in this match; one bot per person.");
            }

            league.Queue.Add((seat, DateTime.UtcNow));
        }

        league.Signal();
        return league;
    }

    /// <summary>A bot gives up its place (only before the split).</summary>
    public LeagueMatch Leave(Guid id, Guid playerId)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            if (!league.IsQueued(playerId))
            {
                throw new MatchException(409, "CANNOT_LEAVE", "You aren't in this match.");
            }

            if (league.Heats.Count > 0 || league.Started)
            {
                throw new MatchException(409, "CANNOT_LEAVE", "The match has been split into heats; ask the admin to take you out, or surrender once it starts.");
            }

            league.Queue.RemoveAll(q => q.Seat.PlayerId == playerId);
        }

        league.Signal();
        return league;
    }

    /// <summary>An admin takes a bot out of the match, and out of its heat if already split.</summary>
    public LeagueMatch Remove(Guid id, Guid playerId)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureNotStarted(league);
            if (league.Queue.RemoveAll(q => q.Seat.PlayerId == playerId) == 0)
            {
                throw new MatchException(404, "NOT_IN_MATCH", "That bot isn't in this match.");
            }

            league.HeatOf(playerId)?.TryLeave(playerId);
        }

        league.Signal();
        return league;
    }

    /// <summary>
    /// Shuffles everyone into heats of the given sizes, replacing any earlier split. The heats are
    /// created with their players seated but don't start until <see cref="Start"/>.
    /// </summary>
    public LeagueMatch Split(Guid id, IReadOnlyList<int> sizes)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureNotStarted(league);
            if (sizes.Count == 0 || sizes.Any(n => n is < 2 or > MapGenerator.MaxPlayers))
            {
                throw new MatchException(400, "INVALID_SPLIT", $"Every heat needs 2 to {MapGenerator.MaxPlayers} players.");
            }

            if (sizes.Sum() != league.Queue.Count)
            {
                throw new MatchException(400, "INVALID_SPLIT", $"The sizes add up to {sizes.Sum()}, but {league.Queue.Count} bots have joined.");
            }

            CancelHeats(league);
            var shuffled = league.Queue.Select(q => q.Seat).OrderBy(_ => Random.Shared.Next()).ToList();
            var taken = 0;
            try
            {
                for (var i = 0; i < sizes.Count; i++)
                {
                    var seats = shuffled.Skip(taken).Take(sizes[i]).ToList();
                    taken += sizes[i];
                    league.Heats.Add(matches.CreateHeat(league.Id, $"{league.Name}-h{i + 1}", league.Category, seats, league.TickIntervalMs, league.MaxTicks));
                }
            }
            catch
            {
                CancelHeats(league);
                throw;
            }

            logger.LogInformation("Split {MatchName} into heats of {Sizes}", league.Name, string.Join(", ", sizes));
        }

        league.Signal();
        return league;
    }

    /// <summary>Undoes a split and reopens the match to new players.</summary>
    public LeagueMatch Unsplit(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureNotStarted(league);
            CancelHeats(league);
        }

        league.Signal();
        return league;
    }

    /// <summary>Starts every heat at the same moment.</summary>
    public LeagueMatch Start(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureNotStarted(league);
            if (league.Heats.Count == 0)
            {
                throw new MatchException(409, "NOT_SPLIT", "Split the match into heats before starting it.");
            }

            if (league.Heats.FirstOrDefault(m => m.Seats.Count < 2) is { } thin)
            {
                throw new MatchException(409, "INVALID_SPLIT", $"{thin.Name} has fewer than 2 players left; split the match again.");
            }

            foreach (var heat in league.Heats)
            {
                matches.StartHeat(heat);
            }

            league.StartedAt = DateTime.UtcNow;
            logger.LogInformation("Started {MatchName}: {Count} heats", league.Name, league.Heats.Count);
        }

        league.Signal();
        return league;
    }

    public LeagueMatch Cancel(Guid id)
    {
        var league = Get(id);
        lock (league.Gate)
        {
            EnsureNotStarted(league);
            CancelHeats(league);
            _leagues.TryRemove(league.Id, out _);
        }

        league.Signal();
        return league;
    }

    /// <summary>
    /// The heat a bot plays in. Before the start, waits up to <paramref name="wait"/> for it (so a state
    /// long-poll on the league match simply lasts until the admin starts it); null if it's still not started.
    /// </summary>
    public async Task<MatchHost?> HeatForAsync(LeagueMatch league, Guid playerId, TimeSpan wait, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        while (true)
        {
            var changed = league.Changed;
            lock (league.Gate)
            {
                if (!league.IsQueued(playerId) && league.HeatOf(playerId) is null)
                {
                    throw new MatchException(403, "NOT_A_PARTICIPANT", "You are not playing in this match.");
                }

                if (league.Started)
                {
                    return league.HeatOf(playerId);
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

    /// <summary>The bot's heat, only once started (for endpoints that don't wait).</summary>
    public MatchHost? StartedHeatOf(LeagueMatch league, Guid playerId)
    {
        lock (league.Gate)
        {
            return league.Started ? league.HeatOf(playerId) : null;
        }
    }

    /// <summary>
    /// Drops league matches nobody started within the league timeout, and started ones once all their
    /// heats have left memory (by then the results are on the leaderboard and in the history).
    /// </summary>
    public void Housekeep(DateTime now)
    {
        foreach (var league in _leagues.Values)
        {
            bool stale;
            lock (league.Gate)
            {
                stale = league.Started
                    ? league.Heats.All(m => matches.Get(m.Id) is null)
                    : now - league.CreatedAt > TimeSpan.FromMinutes(_options.LeagueMatchTimeoutMinutes);
                if (stale && !league.Started)
                {
                    CancelHeats(league);
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
            var heats = league.Heats.Select(h => (Heat: h, Summary: h.ToSummary())).ToList();
            var status = !league.Started ? MatchStatus.Waiting
                : heats.All(h => h.Summary.Status == MatchStatus.Completed) ? MatchStatus.Completed
                : MatchStatus.Active;
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
                Heats = heats.Select(h => new HeatDto
                {
                    MatchId = h.Heat.Id,
                    Name = h.Heat.Name ?? "",
                    Status = h.Summary.Status,
                    PlayerIds = h.Summary.Players.Select(p => p.PlayerId).ToList(),
                }).ToList(),
            };
        }
    }

    /// <summary>Standings of each heat (or of one ordinary match), best first.</summary>
    public IReadOnlyList<HeatStandingsDto> Standings(LeagueMatch league)
    {
        List<MatchHost> heats;
        lock (league.Gate)
        {
            heats = league.Heats.ToList();
        }

        return heats.Select(StandingsOf).ToList();
    }

    public static HeatStandingsDto StandingsOf(MatchHost host)
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

    private static void EnsureNotStarted(LeagueMatch league)
    {
        if (league.Started)
        {
            throw new MatchException(409, "MATCH_ALREADY_STARTED", "The heats have already started.");
        }
    }

    private void CancelHeats(LeagueMatch league)
    {
        foreach (var heat in league.Heats)
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

        league.Heats.Clear();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{3,28}$")]
    private static partial Regex ValidName();
}
