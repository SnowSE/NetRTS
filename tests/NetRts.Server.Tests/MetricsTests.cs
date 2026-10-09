using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

public class MetricsTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_match_reports_its_ticks_start_and_end()
    {
        using var listener = Listen(out var measurements);
        var (client, _) = await factory.NewPlayerAsync();

        var match = await client.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);
        factory.Matches.Get(match.MatchId)!.Advance(3);
        listener.RecordObservableInstruments();
        await client.SurrenderAsync(match.MatchId, Ct);

        lock (measurements)
        {
            Assert.Equal(3, measurements.Count(m => m.Name == "netrts.tick.duration"));
            Assert.Contains(measurements, m => m.Name == "netrts.matches.started" && Equals(m.Tags["netrts.match.kind"], "ladder"));
            Assert.Contains(measurements, m => m.Name == "netrts.matches.live" && Equals(m.Tags["netrts.match.status"], "active") && m.Value >= 1);
            Assert.Contains(measurements, m => m.Name == "netrts.matches.completed"
                                               && Equals(m.Tags["netrts.match.end_reason"], nameof(MatchEndReason.Surrender)));
        }
    }

    private sealed record Recorded(string Name, double Value, Dictionary<string, object?> Tags);

    /// <summary>Listens to this server's NetRts meter only, so parallel test servers don't interfere.</summary>
    private MeterListener Listen(out List<Recorded> measurements)
    {
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var list = measurements = [];
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "NetRts" && instrument.Meter.Scope == meterFactory)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (list)
            {
                list.Add(new Recorded(instrument.Name, value, copy));
            }
        }

        listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
        listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
        listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i, v, t));
        listener.Start();
        return listener;
    }
}
