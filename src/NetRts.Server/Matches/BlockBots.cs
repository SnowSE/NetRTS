using System.Collections.Concurrent;
using NetRts.Bots.Blocks;

namespace NetRts.Server.Matches;

/// <summary>
/// The Block Bots playing in live matches, by match and player, so the block page can change a bot's
/// program, flip its switches and read what its rules are doing. A bot is dropped once its match is
/// no longer in memory.
/// </summary>
public sealed class BlockBots(MatchManager matches)
{
    private readonly ConcurrentDictionary<(Guid MatchId, Guid PlayerId), BlockBot> _bots = new();

    public void Add(Guid matchId, Guid playerId, BlockBot bot)
    {
        Prune();
        _bots[(matchId, playerId)] = bot;
    }

    public BlockBot? Get(Guid matchId, Guid playerId) => _bots.GetValueOrDefault((matchId, playerId));

    /// <summary>The matches still in memory where this player has a Block Bot, newest first.</summary>
    public IReadOnlyList<(MatchHost Match, BlockBot Bot)> For(Guid playerId)
    {
        Prune();
        return _bots
            .Where(kv => kv.Key.PlayerId == playerId)
            .Select(kv => (Match: matches.Get(kv.Key.MatchId), Bot: kv.Value))
            .Where(x => x.Match is not null)
            .Select(x => (x.Match!, x.Bot))
            .OrderByDescending(x => x.Item1.CreatedAt)
            .ToList();
    }

    private void Prune()
    {
        foreach (var key in _bots.Keys.Where(k => matches.Get(k.MatchId) is null).ToList())
        {
            _bots.TryRemove(key, out _);
        }
    }
}
