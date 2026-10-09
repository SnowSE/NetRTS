using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using NetRts.Protocol;

namespace NetRts.Server.Matches;

/// <summary>
/// The server's own metrics and traces, on the "NetRts" meter and activity source that
/// <c>AddServiceDefaults</c> exports. Tags stay low-cardinality: never match or player ids.
/// </summary>
public sealed class MatchMetrics
{
    public static readonly ActivitySource ActivitySource = new(Extensions.NetRtsTelemetryName);

    private readonly Meter _meter;
    private readonly Histogram<double> _tickDuration;
    private readonly Counter<long> _tickOverruns;
    private readonly Counter<long> _matchesStarted;
    private readonly Counter<long> _matchesCompleted;
    private readonly Histogram<int> _matchTicks;
    private readonly Counter<long> _houseBotFailures;
    private readonly Histogram<double> _recordDuration;
    private readonly Counter<long> _recordFailures;

    public MatchMetrics(IMeterFactory meterFactory)
    {
        var meter = _meter = meterFactory.Create(Extensions.NetRtsTelemetryName);
        _tickDuration = meter.CreateHistogram<double>("netrts.tick.duration", "ms",
            "Time to run one tick: house bots plus the simulation step.");
        _tickOverruns = meter.CreateCounter<long>("netrts.tick.overruns", "{tick}",
            "Ticks that took longer than the match's tick interval, delaying the next one.");
        _matchesStarted = meter.CreateCounter<long>("netrts.matches.started", "{match}");
        _matchesCompleted = meter.CreateCounter<long>("netrts.matches.completed", "{match}");
        _matchTicks = meter.CreateHistogram<int>("netrts.match.ticks", "{tick}", "How many ticks finished matches lasted.");
        _houseBotFailures = meter.CreateCounter<long>("netrts.house_bot.failures", "{failure}",
            "House bot turns that threw; the bot skips that tick.");
        _recordDuration = meter.CreateHistogram<double>("netrts.recorder.duration", "ms", "Time to save one finished match.");
        _recordFailures = meter.CreateCounter<long>("netrts.recorder.failures", "{match}",
            "Finished matches that couldn't be saved. They are lost when the server restarts.");
    }

    /// <summary>Reports how many of <paramref name="matches"/> are waiting or being played; returns it for chaining.</summary>
    public ConcurrentDictionary<Guid, MatchHost> ObserveLiveMatches(ConcurrentDictionary<Guid, MatchHost> matches)
    {
        _meter.CreateObservableGauge<int>("netrts.matches.live", () =>
            (IEnumerable<Measurement<int>>)[
                new Measurement<int>(matches.Values.Count(m => m.Status == MatchStatus.Waiting), Status("waiting")),
                new Measurement<int>(matches.Values.Count(m => m.Status == MatchStatus.Active), Status("active")),
            ],
            "{match}", "Matches in memory that are waiting for players or being played.");
        return matches;
    }

    /// <summary>Reports the length of the recorder's queue; returns it for chaining.</summary>
    public Channel<MatchHost> ObserveRecorderQueue(Channel<MatchHost> queue)
    {
        _meter.CreateObservableGauge("netrts.recorder.queue", () => queue.Reader.Count, "{match}",
            "Finished matches waiting to be saved. Anything still queued is lost if the server restarts.");
        return queue;
    }

    public void TickRan(TimeSpan elapsed, int tickIntervalMs)
    {
        _tickDuration.Record(elapsed.TotalMilliseconds);
        if (elapsed.TotalMilliseconds > tickIntervalMs)
        {
            _tickOverruns.Add(1);
        }
    }

    public void MatchStarted(bool isExhibition, int players) =>
        _matchesStarted.Add(1, Kind(isExhibition), new("netrts.match.players", players));

    public void MatchCompleted(bool isExhibition, MatchOutcomeDto? outcome)
    {
        _matchesCompleted.Add(1, Kind(isExhibition), new("netrts.match.end_reason", outcome?.Reason.ToString() ?? "Unknown"));
        if (outcome is not null)
        {
            _matchTicks.Record(outcome.Ticks, Kind(isExhibition));
        }
    }

    public void HouseBotFailed(string bot) => _houseBotFailures.Add(1, new KeyValuePair<string, object?>("netrts.house_bot", bot));

    public void MatchRecorded(TimeSpan elapsed) => _recordDuration.Record(elapsed.TotalMilliseconds);

    public void MatchRecordFailed() => _recordFailures.Add(1);

    private static KeyValuePair<string, object?> Status(string status) => new("netrts.match.status", status);

    private static KeyValuePair<string, object?> Kind(bool isExhibition) => new("netrts.match.kind", isExhibition ? "exhibition" : "ladder");
}
