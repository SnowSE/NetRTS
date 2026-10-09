using System.Diagnostics;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Server.Matches;

public sealed record MatchSettings(int MapWidth, int MapHeight, int Seed, int TickIntervalMs, int MaxTicks);

public sealed record Seat(Guid PlayerId, string Name, bool IsHouseBot);

/// <summary>
/// Owns one match: its seats while waiting, then the simulation, its tick loop and any house bots.
/// Every touch of the simulation happens under <see cref="_gate"/>, so API requests and the tick
/// loop never see half-applied state.
/// </summary>
public sealed class MatchHost
{
    private static readonly RulesDto Rules = GameRules.ToDto();

    private readonly object _gate = new();
    private readonly List<Seat> _seats = [];
    private readonly List<IBotStrategy?> _seatBots = []; // parallel to _seats; non-null for house bots
    private readonly ILogger _logger;
    private readonly MatchMetrics _metrics;
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource _tickSignal = NewSignal();
    private GameSimulation? _sim;
    private MapDto? _map;

    public MatchHost(Guid id, string? name, Guid? creatorId, int maxPlayers, MatchSettings settings, bool isExhibition, DateTime createdAt, ILogger logger, MatchMetrics metrics)
    {
        Id = id;
        Name = name;
        CreatorId = creatorId;
        MaxPlayers = maxPlayers;
        Settings = settings;
        IsExhibition = isExhibition;
        CreatedAt = createdAt;
        _logger = logger;
        _metrics = metrics;
    }

    public Guid Id { get; }
    public string? Name { get; }
    public Guid? CreatorId { get; }
    public int MaxPlayers { get; }
    public MatchSettings Settings { get; }
    public bool IsExhibition { get; }
    public DateTime CreatedAt { get; }
    public DateTime? CompletedAt { get; private set; }
    public bool Recorded { get; set; }

    public MatchStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _sim?.Status ?? MatchStatus.Waiting;
            }
        }
    }

    /// <summary>Raised (outside the lock) once, when the match finishes.</summary>
    public event Action<MatchHost>? Completed;

    public IReadOnlyList<Seat> Seats
    {
        get
        {
            lock (_gate)
            {
                return _seats.ToList();
            }
        }
    }

    public bool HasPlayer(Guid playerId)
    {
        lock (_gate)
        {
            return _seats.Any(s => s.PlayerId == playerId);
        }
    }

    /// <summary>Takes a seat. Returns an error code, or null on success.</summary>
    public string? TryJoin(Seat seat, IBotStrategy? houseBot = null)
    {
        lock (_gate)
        {
            if (_sim is not null)
            {
                return "MATCH_ALREADY_STARTED";
            }

            if (_seats.Any(s => s.PlayerId == seat.PlayerId))
            {
                return "ALREADY_JOINED";
            }

            if (_seats.Count >= MaxPlayers)
            {
                return "MATCH_FULL";
            }

            _seats.Add(seat);
            _seatBots.Add(houseBot);
            return null;
        }
    }

    public bool TryLeave(Guid playerId)
    {
        lock (_gate)
        {
            if (_sim is not null)
            {
                return false;
            }

            var index = _seats.FindIndex(s => s.PlayerId == playerId);
            if (index < 0)
            {
                return false;
            }

            _seats.RemoveAt(index);
            _seatBots.RemoveAt(index);
            return true;
        }
    }

    public bool IsFull
    {
        get
        {
            lock (_gate)
            {
                return _seats.Count >= MaxPlayers;
            }
        }
    }

    /// <summary>Creates the simulation. With <paramref name="autoTick"/> a background loop then advances it on schedule.</summary>
    public void Start(bool autoTick, TimeSpan startDelay)
    {
        lock (_gate)
        {
            if (_sim is not null)
            {
                return;
            }

            _sim = new GameSimulation(Id,
                new GameConfig
                {
                    MapWidth = Settings.MapWidth,
                    MapHeight = Settings.MapHeight,
                    Seed = Settings.Seed,
                    MaxTicks = Settings.MaxTicks,
                },
                _seats.Select(s => new PlayerSeat(s.PlayerId, s.Name)).ToList());
            _map = _sim.Map.ToDto();
        }

        var seats = Seats;
        _logger.LogInformation("Match {MatchId} started: {Players}", Id, string.Join(" vs ", seats.Select(s => s.Name)));
        _metrics.MatchStarted(IsExhibition, seats.Count);
        Signal();

        if (autoTick)
        {
            _ = Task.Run(() => RunAsync(startDelay, _stop.Token));
        }
    }

    public void Stop() => _stop.Cancel();

    /// <summary>Runs <paramref name="ticks"/> ticks immediately (tests and the tick loop both use this).</summary>
    public void Advance(int ticks = 1)
    {
        var finished = false;
        for (var i = 0; i < ticks && !finished; i++)
        {
            lock (_gate)
            {
                if (_sim is null || _sim.Status != MatchStatus.Active)
                {
                    return;
                }

                var started = Stopwatch.GetTimestamp();
                RunHouseBots();
                _sim.Step();
                _metrics.TickRan(Stopwatch.GetElapsedTime(started), Settings.TickIntervalMs);
                if (_sim.Status == MatchStatus.Completed)
                {
                    CompletedAt = DateTime.UtcNow;
                    finished = true;
                }
            }

            Signal();
        }

        if (finished)
        {
            OnCompleted();
        }
    }

    public SubmitCommandsResponse? Submit(Guid playerId, IReadOnlyList<CommandRequest> commands)
    {
        lock (_gate)
        {
            var slot = _sim?.SlotOf(playerId) ?? -1;
            return slot < 0 ? null : _sim!.Submit(slot, commands);
        }
    }

    public int? ClearQueue(Guid playerId)
    {
        lock (_gate)
        {
            var slot = _sim?.SlotOf(playerId) ?? -1;
            return slot < 0 ? null : _sim!.ClearQueue(slot);
        }
    }

    public bool Surrender(Guid playerId)
    {
        bool finished;
        lock (_gate)
        {
            var slot = _sim?.SlotOf(playerId) ?? -1;
            if (slot < 0 || _sim!.Status != MatchStatus.Active)
            {
                return false;
            }

            _sim.Surrender(slot);
            finished = _sim.Status == MatchStatus.Completed;
            if (finished)
            {
                CompletedAt = DateTime.UtcNow;
            }
        }

        Signal();
        if (finished)
        {
            OnCompleted();
        }

        return true;
    }

    public GameStateDto? GetPlayerView(Guid playerId, int? sinceTick)
    {
        lock (_gate)
        {
            var slot = _sim?.SlotOf(playerId) ?? -1;
            return slot < 0 ? null : _sim!.GetPlayerView(slot, sinceTick) with { TickIntervalMs = Settings.TickIntervalMs };
        }
    }

    public SpectatorStateDto? GetSpectatorView(int? sinceTick, bool includeVisibility)
    {
        lock (_gate)
        {
            return _sim is null ? null : _sim.GetSpectatorView(sinceTick, includeVisibility) with { TickIntervalMs = Settings.TickIntervalMs };
        }
    }

    public MapDto? Map
    {
        get
        {
            lock (_gate)
            {
                return _map;
            }
        }
    }

    public MatchResultDto? GetResult()
    {
        lock (_gate)
        {
            return _sim?.GetResult();
        }
    }

    public ReplayDto? GetReplay()
    {
        lock (_gate)
        {
            return _sim?.Status == MatchStatus.Completed ? _sim.GetReplay() : null;
        }
    }

    public MatchSummaryDto ToSummary()
    {
        lock (_gate)
        {
            return new MatchSummaryDto
            {
                MatchId = Id,
                Status = _sim?.Status ?? MatchStatus.Waiting,
                MaxPlayers = MaxPlayers,
                Tick = _sim?.Tick ?? 0,
                MaxTicks = Settings.MaxTicks,
                TickIntervalMs = Settings.TickIntervalMs,
                MapWidth = Settings.MapWidth,
                MapHeight = Settings.MapHeight,
                Seed = Settings.Seed,
                CreatedAt = new DateTimeOffset(CreatedAt, TimeSpan.Zero),
                Players = _seats.Select((s, i) => new MatchPlayerDto { PlayerId = s.PlayerId, Name = s.Name, Slot = i, IsHouseBot = s.IsHouseBot }).ToList(),
                Outcome = _sim?.Outcome,
                Name = Name,
            };
        }
    }

    /// <summary>Completes when the simulation reaches <paramref name="tick"/>, the match ends, or the timeout passes.</summary>
    public async Task WaitForTickAsync(int tick, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        while (true)
        {
            Task signal;
            lock (_gate)
            {
                signal = _tickSignal.Task;
                if (_sim is not null && (_sim.Tick >= tick || _sim.Status == MatchStatus.Completed))
                {
                    return;
                }
            }

            try
            {
                await signal.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>A task that completes on the next tick (or start/finish).</summary>
    public Task NextTick
    {
        get
        {
            lock (_gate)
            {
                return _tickSignal.Task;
            }
        }
    }

    private async Task RunAsync(TimeSpan startDelay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(startDelay, ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Settings.TickIntervalMs));
            do
            {
                Advance();
            }
            while (Status == MatchStatus.Active && await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tick loop for match {MatchId} crashed", Id);
        }
    }

    private void RunHouseBots()
    {
        for (var slot = 0; slot < _seatBots.Count; slot++)
        {
            if (_seatBots[slot] is not { } bot)
            {
                continue;
            }

            try
            {
                var commands = bot.Decide(new BotContext(_sim!.GetPlayerView(slot), _map!, Rules));
                if (commands.Count > 0)
                {
                    _sim.Submit(slot, commands.Take(_sim.Config.MaxCommandsPerSubmit).ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "House bot {Bot} failed in match {MatchId}", bot.Name, Id);
                _metrics.HouseBotFailed(bot.Name);
            }
        }
    }

    private void Signal()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _tickSignal;
            _tickSignal = NewSignal();
        }

        previous.TrySetResult();
    }

    private void OnCompleted()
    {
        _logger.LogInformation("Match {MatchId} finished at tick {Tick}: {Outcome}", Id, _sim!.Tick, _sim.Outcome);
        _metrics.MatchCompleted(IsExhibition, _sim.Outcome);
        Completed?.Invoke(this);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
