using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;

namespace NetRts.Server.Matches;

public sealed class MatchException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>House bots have real player rows (so they appear on the leaderboard); this maps their names to ids.</summary>
public sealed class HouseBotDirectory
{
    private readonly ConcurrentDictionary<string, Guid> _ids = new(StringComparer.OrdinalIgnoreCase);

    public static string PlayerName(string bot) => $"house-{bot}";

    public void Register(string bot, Guid playerId) => _ids[bot] = playerId;

    public Guid IdOf(string bot) =>
        _ids.TryGetValue(bot, out var id) ? id : throw new InvalidOperationException($"House bot {bot} was not seeded.");
}

/// <summary>All live matches (waiting, running, recently finished) and the rules for creating them.</summary>
public sealed class MatchManager(
    IOptions<NetRtsOptions> options,
    HouseBotDirectory houseBots,
    MatchRecorder recorder,
    ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<Guid, MatchHost> _matches = new();
    private readonly NetRtsOptions _options = options.Value;
    private readonly ILogger _logger = loggerFactory.CreateLogger<MatchManager>();
    private readonly object _createGate = new();

    public MatchHost? Get(Guid id) => _matches.GetValueOrDefault(id);

    public IReadOnlyList<MatchHost> All => _matches.Values.OrderByDescending(m => m.CreatedAt).ToList();

    public MatchHost Create(Guid creatorId, string creatorName, CreateMatchRequest request)
    {
        var maxPlayers = request.MaxPlayers ?? 2;
        if (maxPlayers is < 2 or > 4)
        {
            throw new MatchException(400, "INVALID_SETTINGS", "maxPlayers must be between 2 and 4.");
        }

        var bots = request.HouseBots ?? [];
        if (bots.Count > maxPlayers - 1)
        {
            throw new MatchException(400, "INVALID_SETTINGS", $"At most {maxPlayers - 1} house bots fit in a {maxPlayers}-player match.");
        }

        ValidateBots(bots);
        var settings = ResolveSettings(request.Settings);

        lock (_createGate)
        {
            EnsureCapacity();
            var waitingByCreator = _matches.Values.Count(m => m.CreatorId == creatorId && m.Status == MatchStatus.Waiting);
            if (waitingByCreator >= _options.MaxWaitingMatchesPerPlayer)
            {
                throw new MatchException(429, "TOO_MANY_WAITING_MATCHES", $"You already have {waitingByCreator} matches waiting for opponents.");
            }

            var host = NewHost(creatorId, maxPlayers, settings, isExhibition: false);
            host.TryJoin(new Seat(creatorId, creatorName, IsHouseBot: false));
            foreach (var bot in bots)
            {
                host.TryJoin(new Seat(houseBots.IdOf(bot), HouseBotDirectory.PlayerName(bot), IsHouseBot: true), HouseBots.Create(bot));
            }

            _matches[host.Id] = host;
            StartIfFull(host);
            return host;
        }
    }

    public MatchHost CreateExhibition(CreateExhibitionRequest request)
    {
        if (request.Bots.Count is < 2 or > 4)
        {
            throw new MatchException(400, "INVALID_SETTINGS", "An exhibition needs 2 to 4 house bots.");
        }

        ValidateBots(request.Bots);
        var settings = ResolveSettings(request.Settings);

        lock (_createGate)
        {
            EnsureCapacity();
            var running = _matches.Values.Count(m => m.IsExhibition && m.Status != MatchStatus.Completed);
            if (running >= _options.MaxConcurrentExhibitions)
            {
                throw new MatchException(429, "TOO_MANY_EXHIBITIONS", $"{running} exhibitions are already running; try again when one finishes.");
            }

            var host = NewHost(null, request.Bots.Count, settings, isExhibition: true);
            foreach (var bot in request.Bots)
            {
                host.TryJoin(new Seat(houseBots.IdOf(bot), HouseBotDirectory.PlayerName(bot), IsHouseBot: true), HouseBots.Create(bot));
            }

            _matches[host.Id] = host;
            StartIfFull(host);
            return host;
        }
    }

    public MatchHost Join(Guid matchId, Guid playerId, string playerName)
    {
        var host = Get(matchId) ?? throw new MatchException(404, "MATCH_NOT_FOUND", "No live match with that id.");
        var error = host.TryJoin(new Seat(playerId, playerName, IsHouseBot: false));
        if (error is not null)
        {
            throw new MatchException(409, error, error switch
            {
                "MATCH_FULL" => "The match is full.",
                "ALREADY_JOINED" => "You are already in this match.",
                _ => "The match has already started.",
            });
        }

        StartIfFull(host);
        return host;
    }

    public MatchHost Leave(Guid matchId, Guid playerId)
    {
        var host = Get(matchId) ?? throw new MatchException(404, "MATCH_NOT_FOUND", "No live match with that id.");
        if (!host.TryLeave(playerId))
        {
            throw new MatchException(409, "CANNOT_LEAVE", "You can only leave a match you joined that hasn't started. Use surrender instead.");
        }

        if (host.Seats.All(s => s.IsHouseBot) && _matches.TryRemove(matchId, out _))
        {
            host.Stop();
        }

        return host;
    }

    /// <summary>Drops stale waiting matches and finished matches that have been persisted and aged out.</summary>
    public void Housekeep(DateTime now)
    {
        foreach (var host in _matches.Values)
        {
            var status = host.Status;
            var expiredWaiting = status == MatchStatus.Waiting && now - host.CreatedAt > TimeSpan.FromMinutes(_options.WaitingMatchTimeoutMinutes);
            var agedOut = status == MatchStatus.Completed && host.Recorded && host.CompletedAt is { } done
                          && now - done > TimeSpan.FromMinutes(_options.CompletedMatchRetentionMinutes);
            if ((expiredWaiting || agedOut) && _matches.TryRemove(host.Id, out _))
            {
                host.Stop();
                _logger.LogInformation("Removed {Status} match {MatchId} from memory", status, host.Id);
            }
        }
    }

    private MatchHost NewHost(Guid? creatorId, int maxPlayers, MatchSettings settings, bool isExhibition)
    {
        var host = new MatchHost(Guid.NewGuid(), creatorId, maxPlayers, settings, isExhibition, DateTime.UtcNow,
            loggerFactory.CreateLogger<MatchHost>());
        host.Completed += recorder.Enqueue;
        return host;
    }

    private void StartIfFull(MatchHost host)
    {
        if (host.IsFull)
        {
            host.Start(_options.AutoTick, TimeSpan.FromMilliseconds(_options.StartDelayMs));
        }
    }

    private void EnsureCapacity()
    {
        var live = _matches.Values.Count(m => m.Status != MatchStatus.Completed);
        if (live >= _options.MaxLiveMatches)
        {
            throw new MatchException(503, "SERVER_BUSY", $"The server is hosting its maximum of {_options.MaxLiveMatches} matches.");
        }
    }

    private static void ValidateBots(IReadOnlyList<string> bots)
    {
        foreach (var bot in bots)
        {
            if (!HouseBots.Exists(bot))
            {
                throw new MatchException(400, "UNKNOWN_HOUSE_BOT", $"Unknown house bot '{bot}'. See GET /api/v1/bots.");
            }
        }

        if (bots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bots.Count)
        {
            throw new MatchException(400, "DUPLICATE_HOUSE_BOT", "Each house bot can take only one seat in a match.");
        }
    }

    private MatchSettings ResolveSettings(MatchSettingsDto? dto)
    {
        var width = dto?.MapWidth ?? _options.DefaultMapSize;
        var height = dto?.MapHeight ?? _options.DefaultMapSize;
        var interval = dto?.TickIntervalMs ?? _options.DefaultTickIntervalMs;
        var maxTicks = dto?.MaxTicks ?? _options.DefaultMaxTicks;

        if (width is < MapGenerator.MinSize or > MapGenerator.MaxSize || height is < MapGenerator.MinSize or > MapGenerator.MaxSize)
        {
            throw new MatchException(400, "INVALID_SETTINGS", $"Map dimensions must be between {MapGenerator.MinSize} and {MapGenerator.MaxSize}.");
        }

        if (interval < _options.MinTickIntervalMs || interval > _options.MaxTickIntervalMs)
        {
            throw new MatchException(400, "INVALID_SETTINGS", $"tickIntervalMs must be between {_options.MinTickIntervalMs} and {_options.MaxTickIntervalMs}.");
        }

        if (maxTicks is < 10 or > 20_000)
        {
            throw new MatchException(400, "INVALID_SETTINGS", "maxTicks must be between 10 and 20000.");
        }

        return new MatchSettings(width, height, dto?.Seed ?? Random.Shared.Next(), interval, maxTicks);
    }
}

/// <summary>Periodically tidies <see cref="MatchManager"/>.</summary>
public sealed class MatchHousekeeper(MatchManager manager, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            manager.Housekeep(time.GetUtcNow().UtcDateTime);
        }
    }
}
