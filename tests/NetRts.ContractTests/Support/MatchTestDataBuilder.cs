using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.ContractTests.Support;

public class MatchTestDataBuilder
{
    private readonly IServiceProvider _serviceProvider;

    public MatchTestDataBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<(Match match, List<Unit> units, List<Building> buildings)> CreateMatchWithPlayers(
        Guid player1Id,
        Guid player2Id)
    {
        var settings = new GameSettings(
            mapWidth: 100,
            mapHeight: 100,
            maxTicks: 1800,
            startingResources: 500);

        var match = new Match(player1Id, player2Id, settings);
        match.Start(); // Activate the match for testing

        // Create starting units for player 1 (5 workers)
        var units = new List<Unit>();
        for (int i = 0; i < 5; i++)
        {
            units.Add(new Unit(
                id: i + 1,
                matchId: match.Id,
                ownerId: player1Id,
                type: UnitType.Worker,
                position: new Position(10 + i, 10)));
        }

        // Create starting units for player 2 (5 workers)
        for (int i = 0; i < 5; i++)
        {
            units.Add(new Unit(
                id: i + 6,
                matchId: match.Id,
                ownerId: player2Id,
                type: UnitType.Worker,
                position: new Position(90 + i, 90)));
        }

        // Create starting buildings
        var buildings = new List<Building>();

        var building1 = new Building(1, match.Id, player1Id, BuildingType.CommandCenter, new Position(10, 10));
        building1.AdvanceConstruction(100);
        buildings.Add(building1);

        var building2 = new Building(2, match.Id, player2Id, BuildingType.CommandCenter, new Position(90, 90));
        building2.AdvanceConstruction(100);
        buildings.Add(building2);

        // Create map tiles (initialize a subset for testing - a 100x100 map would be 10,000 tiles)
        // Just create tiles that will be visible to test units
        var mapTiles = new List<MapTile>();
        for (int x = 0; x < settings.MapWidth; x++)
        {
            for (int y = 0; y < settings.MapHeight; y++)
            {
                mapTiles.Add(new MapTile(match.Id, new Position(x, y), TerrainType.Passable));
            }
        }

        // Save to database
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        await dbContext.Matches.AddAsync(match);

        // Add units to database
        foreach (var unit in units)
        {
            await dbContext.Units.AddAsync(unit);
        }

        // Add buildings to database
        foreach (var building in buildings)
        {
            await dbContext.Buildings.AddAsync(building);
        }

        // Add map tiles to database
        foreach (var tile in mapTiles)
        {
            await dbContext.MapTiles.AddAsync(tile);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Save to cache
        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        cache.SetMatch(match.Id, match);
        cache.SetUnitsForMatch(match.Id, units);
        cache.SetBuildingsForMatch(match.Id, buildings);
        cache.SetMapTilesForMatch(match.Id, mapTiles);
        cache.SetResourceDepositsForMatch(match.Id, new List<ResourceDeposit>());
        cache.SetUpgradesForMatch(match.Id, new List<Upgrade>());

        return (match, units, buildings);
    }

    public async Task<Match> CreateMatchWithCustomUnits(
        Guid player1Id,
        Guid player2Id,
        List<(int unitId, int x, int y, UnitType type, Guid ownerId)> unitConfigs,
        bool createBuildings = true)
    {
        var settings = GameSettings.Default;
        var match = new Match(player1Id, player2Id, settings);
        match.Start(); // Activate the match for testing

        var units = unitConfigs.Select(config =>
            new Unit(config.unitId, match.Id, config.ownerId, config.type, new Position(config.x, config.y)))
            .ToList();

        // Create starting buildings for both players to avoid cache fallback to database
        var buildings = new List<Building>();
        if (createBuildings)
        {
            // Create non-operational buildings that won't provide vision (for tests that don't need them)
            var building1 = new Building(1, match.Id, player1Id, BuildingType.Barracks, new Position(-50, -50));
            buildings.Add(building1);

            var building2 = new Building(2, match.Id, player2Id, BuildingType.Barracks, new Position(150, 150));
            buildings.Add(building2);
        }
        else
        {
            // Create minimal non-operational placeholder buildings to prevent cache fallback
            var building1 = new Building(1, match.Id, player1Id, BuildingType.Barracks, new Position(-50, -50));
            buildings.Add(building1);
        }

        // Create map tiles for the entire map
        var mapTiles = new List<MapTile>();
        for (int x = 0; x < settings.MapWidth; x++)
        {
            for (int y = 0; y < settings.MapHeight; y++)
            {
                mapTiles.Add(new MapTile(match.Id, new Position(x, y), TerrainType.Passable));
            }
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        await dbContext.Matches.AddAsync(match);

        // Add units to database
        foreach (var unit in units)
        {
            await dbContext.Units.AddAsync(unit);
        }

        // Add buildings to database
        foreach (var building in buildings)
        {
            await dbContext.Buildings.AddAsync(building);
        }

        // Add map tiles to database
        foreach (var tile in mapTiles)
        {
            await dbContext.MapTiles.AddAsync(tile);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        cache.SetMatch(match.Id, match);
        cache.SetUnitsForMatch(match.Id, units);
        cache.SetBuildingsForMatch(match.Id, buildings);
        cache.SetMapTilesForMatch(match.Id, mapTiles);
        cache.SetResourceDepositsForMatch(match.Id, new List<ResourceDeposit>());
        cache.SetUpgradesForMatch(match.Id, new List<Upgrade>());

        return match;
    }

    public async Task<int> AddUnit(Guid matchId, Guid ownerId, UnitType unitType, int x, int y)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Get the highest unit ID in the match (use ToList for InMemory database compatibility)
        var existingIds = dbContext.Units
            .Where(u => u.MatchId == matchId)
            .Select(u => u.Id)
            .ToList();

        var maxUnitId = existingIds.Count > 0 ? existingIds.Max() : 0;

        var newUnit = new Unit(
            id: maxUnitId + 1,
            matchId: matchId,
            ownerId: ownerId,
            type: unitType,
            position: new Position(x, y));

        await dbContext.Units.AddAsync(newUnit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Update cache
        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        var existingUnits = cache.GetUnitsForMatch(matchId);
        existingUnits.Add(newUnit);
        cache.SetUnitsForMatch(matchId, existingUnits);

        return newUnit.Id;
    }

    public async Task<int> AddResourceDeposit(Guid matchId, int x, int y, int capacity)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Get the highest deposit ID in the match (use ToList for InMemory database compatibility)
        var existingIds = dbContext.ResourceDeposits
            .Where(d => d.MatchId == matchId)
            .Select(d => d.Id)
            .ToList();

        var maxDepositId = existingIds.Count > 0 ? existingIds.Max() : 0;

        var deposit = new ResourceDeposit(
            id: maxDepositId + 1,
            matchId: matchId,
            position: new Position(x, y),
            initialCapacity: capacity);

        await dbContext.ResourceDeposits.AddAsync(deposit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Update cache
        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        var existingDeposits = cache.GetResourceDepositsForMatch(matchId);
        existingDeposits.Add(deposit);
        cache.SetResourceDepositsForMatch(matchId, existingDeposits);

        return deposit.Id;
    }
}
