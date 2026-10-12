using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Matches;

public enum FramesStatus
{
    Ok,
    NotFound,
    NotCompleted,

    /// <summary>The match was played before frames were recorded and can't be rebuilt faithfully (the rules have changed since).</summary>
    Unavailable,
}

/// <summary>
/// Serves finished matches' frames: from memory while the match is still on the server, otherwise from
/// the database. Matches played before frames were recorded get theirs rebuilt from the replay the first
/// time someone asks, provided the re-simulation ends exactly as the match did.
/// </summary>
public sealed class ReplayFrameStore(MatchManager matches, IServiceScopeFactory scopes, ILogger<ReplayFrameStore> logger)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<bool>>> _rebuilds = new();

    /// <summary>The Brotli-compressed <see cref="ReplayFramesDto"/> JSON.</summary>
    public async Task<(FramesStatus Status, byte[]? Manifest)> ManifestAsync(Guid matchId, CancellationToken ct)
    {
        if (matches.Get(matchId) is { } host)
        {
            if (host.Frames is { } frames)
            {
                return (FramesStatus.Ok, frames.Manifest);
            }

            if (host.Status != MatchStatus.Completed)
            {
                return (FramesStatus.NotCompleted, null);
            }
        }

        if (await StoredManifestAsync(matchId, ct) is { } stored)
        {
            return (FramesStatus.Ok, stored);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            if (!await db.Matches.AnyAsync(m => m.Id == matchId, ct))
            {
                return (FramesStatus.NotFound, null);
            }
        }

        // One rebuild per match however many people ask; a failed one is remembered until restart.
        var rebuilt = await _rebuilds.GetOrAdd(matchId, id => new Lazy<Task<bool>>(() => Task.Run(() => RebuildAsync(id, CancellationToken.None)))).Value;
        if (rebuilt)
        {
            _rebuilds.TryRemove(matchId, out _);
        }

        return rebuilt && await StoredManifestAsync(matchId, ct) is { } manifest
            ? (FramesStatus.Ok, manifest)
            : (FramesStatus.Unavailable, null);
    }

    /// <summary>One Brotli-compressed chunk of frames, or null if there is no such chunk.</summary>
    public async Task<byte[]?> ChunkAsync(Guid matchId, int index, CancellationToken ct)
    {
        if (matches.Get(matchId)?.Frames is { } frames)
        {
            return index >= 0 && index < frames.Chunks.Count ? frames.Chunks[index] : null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        return await db.MatchFrameChunks.AsNoTracking()
            .Where(c => c.MatchId == matchId && c.Index == index)
            .Select(c => c.Data)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Adds a match's frames to the database (saved with the caller's next SaveChanges).</summary>
    public static void Add(NetRtsDb db, Guid matchId, RecordedFrames frames, DateTime now)
    {
        db.MatchFrames.Add(new MatchFramesRecord
        {
            MatchId = matchId,
            RulesVersion = frames.RulesVersion,
            ChunkTicks = frames.ChunkTicks,
            ChunkCount = frames.Chunks.Count,
            LastTick = frames.LastTick,
            Rebuilt = frames.Rebuilt,
            CreatedAt = now,
            Manifest = frames.Manifest,
        });
        for (var i = 0; i < frames.Chunks.Count; i++)
        {
            db.MatchFrameChunks.Add(new MatchFrameChunkRecord { MatchId = matchId, Index = i, Data = frames.Chunks[i] });
        }
    }

    private async Task<byte[]?> StoredManifestAsync(Guid matchId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
        return await db.MatchFrames.AsNoTracking().Where(f => f.MatchId == matchId).Select(f => f.Manifest).FirstOrDefaultAsync(ct);
    }

    private async Task<bool> RebuildAsync(Guid matchId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NetRtsDb>();
            var record = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (record is null)
            {
                return false;
            }

            if (record.RulesVersion is { } version && version != GameRules.Version)
            {
                logger.LogInformation("Not rebuilding frames for match {MatchId}: played under rules {Then}, now {Now}", matchId, version, GameRules.Version);
                return false;
            }

            var replay = JsonSerializer.Deserialize<ReplayDto>(record.ReplayJson, NetRtsClient.JsonOptions)!;
            var summary = JsonSerializer.Deserialize<MatchSummaryDto>(record.SummaryJson, NetRtsClient.JsonOptions)!;
            var names = replay.Players
                .Select((id, slot) => (summary.Players.FirstOrDefault(p => p.PlayerId == id) ?? summary.Players.FirstOrDefault(p => p.Slot == slot))?.Name ?? $"P{slot}")
                .ToList();

            var recorder = new ReplayFrameRecorder();
            var sim = GameSimulation.FromReplay(replay, names, s => recorder.Add(s.GetSpectatorView(s.Tick - 1)));

            // Older matches have no rules version: trust the rebuild only if it ends exactly as the match did.
            if (sim.Outcome is not { } outcome || outcome.Reason.ToString() != record.Reason || outcome.WinnerId != record.WinnerId || outcome.Ticks != record.Ticks)
            {
                logger.LogWarning("Rebuilt frames for match {MatchId} don't match its result ({Rebuilt} vs {Reason} at {Ticks}); not keeping them",
                    matchId, sim.Outcome, record.Reason, record.Ticks);
                return false;
            }

            Add(db, matchId, recorder.Finish(matchId, sim.Map.ToDto(), GameRules.Version, rebuilt: true), DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Rebuilt frames for match {MatchId} ({Ticks} ticks)", matchId, record.Ticks);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Saved by another server instance in the meantime is fine; anything else isn't.
            if (await StoredManifestAsync(matchId, ct) is not null)
            {
                return true;
            }

            logger.LogError(ex, "Could not save rebuilt frames for match {MatchId}", matchId);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not rebuild frames for match {MatchId}", matchId);
            return false;
        }
    }
}
