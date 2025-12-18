using System.Collections.Concurrent;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Caching;

/// <summary>
/// In-memory cache for active match state.
/// </summary>
public class GameStateCache : IGameStateCache
{
    private readonly ConcurrentDictionary<Guid, Match> _matches = new();
    private readonly ConcurrentDictionary<Guid, List<Unit>> _units = new();
    private readonly ConcurrentDictionary<Guid, List<Building>> _buildings = new();
    private readonly ConcurrentDictionary<Guid, List<ResourceDeposit>> _resourceDeposits = new();
    private readonly ConcurrentDictionary<Guid, List<MapTile>> _mapTiles = new();

    public void AddOrUpdate(Match match)
    {
        _matches[match.Id] = match;
    }

    public Match? Get(Guid matchId)
    {
        _matches.TryGetValue(matchId, out var match);
        return match;
    }

    public Match? GetMatch(Guid matchId) => Get(matchId);

    public void SetMatch(Guid matchId, Match match)
    {
        _matches[matchId] = match;
    }

    public List<Unit> GetUnitsForMatch(Guid matchId)
    {
        return _units.TryGetValue(matchId, out var units) ? units : new List<Unit>();
    }

    public void SetUnitsForMatch(Guid matchId, List<Unit> units)
    {
        _units[matchId] = units;
    }

    public List<Building> GetBuildingsForMatch(Guid matchId)
    {
        return _buildings.TryGetValue(matchId, out var buildings) ? buildings : new List<Building>();
    }

    public void SetBuildingsForMatch(Guid matchId, List<Building> buildings)
    {
        _buildings[matchId] = buildings;
    }

    public List<ResourceDeposit> GetResourceDepositsForMatch(Guid matchId)
    {
        return _resourceDeposits.TryGetValue(matchId, out var deposits) ? deposits : new List<ResourceDeposit>();
    }

    public void SetResourceDepositsForMatch(Guid matchId, List<ResourceDeposit> deposits)
    {
        _resourceDeposits[matchId] = deposits;
    }

    public List<MapTile> GetMapTilesForMatch(Guid matchId)
    {
        return _mapTiles.TryGetValue(matchId, out var tiles) ? tiles : new List<MapTile>();
    }

    public void SetMapTilesForMatch(Guid matchId, List<MapTile> tiles)
    {
        _mapTiles[matchId] = tiles;
    }

    public List<Match> GetAll()
    {
        return _matches.Values.ToList();
    }

    public void Remove(Guid matchId)
    {
        _matches.TryRemove(matchId, out _);
        _units.TryRemove(matchId, out _);
        _buildings.TryRemove(matchId, out _);
        _resourceDeposits.TryRemove(matchId, out _);
        _mapTiles.TryRemove(matchId, out _);
    }

    public bool Contains(Guid matchId)
    {
        return _matches.ContainsKey(matchId);
    }
}
