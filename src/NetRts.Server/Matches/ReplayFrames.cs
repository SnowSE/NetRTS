using System.IO.Compression;
using System.Text.Json;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Matches;

/// <summary>
/// A finished match's frames: what spectators saw on every tick, as Brotli-compressed chunks of
/// newline-separated <see cref="SpectatorStateDto"/> JSON, and the Brotli-compressed
/// <see cref="ReplayFramesDto"/> manifest describing them. Both are served to browsers as they are.
/// </summary>
public sealed record RecordedFrames(int ChunkTicks, IReadOnlyList<byte[]> Chunks, int LastTick, string RulesVersion, bool Rebuilt, byte[] Manifest);

/// <summary>
/// Collects one spectator frame per tick while a match runs, compressing every
/// <see cref="ChunkTicks"/> frames as they fill so only the current chunk is held uncompressed.
/// Not thread safe: the match host calls it under its lock.
/// </summary>
public sealed class ReplayFrameRecorder
{
    /// <summary>Ticks per chunk: what a viewer downloads to start playing from any point.</summary>
    public const int ChunkTicks = 100;

    // Brotli quality 9 with a 4 MB window: about a quarter the size of gzip, and a long 16-player
    // match compresses in well under a second. (Quality 11 saves ~20% more at ~80x the time.)
    private const int Quality = 9;
    private const int Window = 22;

    private readonly List<byte[]> _chunks = [];
    private readonly List<GameEventDto> _events = [];
    private readonly MemoryStream _current = new();
    private int _framesInCurrent;
    private long _lastFrameStart;
    private int _lastEventsCount;
    private int _lastTick = -1;

    /// <summary>Adds the frame for the current tick; a second frame for the same tick (a surrender) replaces the first.</summary>
    public void Add(SpectatorStateDto frame)
    {
        if (frame.Tick == _lastTick)
        {
            _current.SetLength(_lastFrameStart);
            _framesInCurrent--;
            _events.RemoveRange(_events.Count - _lastEventsCount, _lastEventsCount);
        }
        else if (_framesInCurrent == ChunkTicks)
        {
            Flush();
        }

        _lastFrameStart = _current.Length;
        JsonSerializer.Serialize(_current, frame, NetRtsClient.JsonOptions);
        _current.WriteByte((byte)'\n');
        _framesInCurrent++;
        _lastTick = frame.Tick;
        _events.AddRange(frame.Events);
        _lastEventsCount = frame.Events.Count;
    }

    public RecordedFrames Finish(Guid matchId, MapDto map, string rulesVersion, bool rebuilt = false)
    {
        Flush();
        var manifest = new ReplayFramesDto
        {
            MatchId = matchId,
            ChunkTicks = ChunkTicks,
            ChunkCount = _chunks.Count,
            LastTick = _lastTick,
            RulesVersion = rulesVersion,
            Rebuilt = rebuilt,
            Map = map,
            Events = _events,
        };
        return new RecordedFrames(ChunkTicks, _chunks.ToList(), _lastTick, rulesVersion, rebuilt,
            Compress(JsonSerializer.SerializeToUtf8Bytes(manifest, NetRtsClient.JsonOptions)));
    }

    private void Flush()
    {
        if (_framesInCurrent == 0)
        {
            return;
        }

        _chunks.Add(Compress(_current.GetBuffer().AsSpan(0, (int)_current.Length)));
        _current.SetLength(0);
        _framesInCurrent = 0;
    }

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        if (!BrotliEncoder.TryCompress(data, buffer, out var written, Quality, Window))
        {
            throw new InvalidOperationException("Brotli compression failed.");
        }

        return buffer[..written];
    }

    public static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
}
