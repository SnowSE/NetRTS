using System.Collections.Concurrent;
using NetRts.Application.Services;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Services;

public class CommandQueueManager : ICommandQueueManager
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Command>> _queues = new();

    public Task<bool> EnqueueCommandAsync(Command command, int maxQueueSize)
    {
        var key = GetQueueKey(command.MatchId, command.PlayerId);
        var queue = _queues.GetOrAdd(key, _ => new ConcurrentQueue<Command>());

        if (queue.Count >= maxQueueSize)
        {
            return Task.FromResult(false); // Queue full
        }

        queue.Enqueue(command);
        return Task.FromResult(true);
    }

    public Task<List<Command>> DequeueCommandsAsync(Guid matchId, Guid playerId, int limit)
    {
        var key = GetQueueKey(matchId, playerId);
        var commands = new List<Command>();

        if (_queues.TryGetValue(key, out var queue))
        {
            for (int i = 0; i < limit && queue.TryDequeue(out var command); i++)
            {
                commands.Add(command);
            }
        }

        return Task.FromResult(commands);
    }

    public Task<int> GetQueueSizeAsync(Guid matchId, Guid playerId)
    {
        var key = GetQueueKey(matchId, playerId);
        if (_queues.TryGetValue(key, out var queue))
        {
            return Task.FromResult(queue.Count);
        }
        return Task.FromResult(0);
    }

    public Task ClearMatchCommandsAsync(Guid matchId)
    {
        var keysToRemove = _queues.Keys.Where(k => k.StartsWith(matchId.ToString())).ToList();
        foreach (var key in keysToRemove)
        {
            _queues.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private static string GetQueueKey(Guid matchId, Guid playerId) => $"{matchId}:{playerId}";
}
