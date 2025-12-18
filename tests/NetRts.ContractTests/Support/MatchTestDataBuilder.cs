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

        // Save to database
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        await dbContext.Matches.AddAsync(match);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Save to cache
        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        cache.SetMatch(match.Id, match);
        cache.SetUnitsForMatch(match.Id, units);
        cache.SetBuildingsForMatch(match.Id, buildings);

        return (match, units, buildings);
    }

    public async Task<Match> CreateMatchWithCustomUnits(
        Guid player1Id,
        Guid player2Id,
        List<(int unitId, int x, int y, UnitType type, Guid ownerId)> unitConfigs)
    {
        var settings = GameSettings.Default;
        var match = new Match(player1Id, player2Id, settings);

        var units = unitConfigs.Select(config =>
            new Unit(config.unitId, match.Id, config.ownerId, config.type, new Position(config.x, config.y)))
            .ToList();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        await dbContext.Matches.AddAsync(match);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var cache = scope.ServiceProvider.GetRequiredService<IGameStateCache>();
        cache.SetMatch(match.Id, match);
        cache.SetUnitsForMatch(match.Id, units);
        cache.SetBuildingsForMatch(match.Id, new List<Building>());

        return match;
    }
}
