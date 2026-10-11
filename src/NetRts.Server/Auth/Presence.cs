using System.Collections.Concurrent;

namespace NetRts.Server.Auth;

/// <summary>When each bot last made an authenticated request, so admins can see who is connected before a round starts.</summary>
public sealed class Presence(TimeProvider time)
{
    /// <summary>Longer than a state long-poll (30 s), so a bot waiting on one still counts as connected.</summary>
    public static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(40);

    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();

    public void Touch(Guid playerId) => _lastSeen[playerId] = time.GetUtcNow().UtcDateTime;

    public DateTime? LastSeen(Guid playerId) => _lastSeen.TryGetValue(playerId, out var at) ? at : null;

    public bool IsConnected(Guid playerId) =>
        LastSeen(playerId) is { } at && time.GetUtcNow().UtcDateTime - at <= ConnectedWindow;
}
