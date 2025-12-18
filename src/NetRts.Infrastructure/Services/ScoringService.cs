using NetRts.Application.Services;
using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Services;

public class ScoringService : IScoringService
{
    public Score CalculatePlayerScore(
        Guid playerId,
        Match match,
        IEnumerable<Unit> units,
        IEnumerable<Building> buildings,
        int resourcesGathered,
        int enemyUnitsDestroyed,
        int enemyBuildingsDestroyed)
    {
        var playerUnits = units.Where(u => u.OwnerId == playerId && !u.IsDestroyed()).Count();
        var playerBuildings = buildings.Where(b => b.OwnerId == playerId && !b.IsDestroyed()).Count();

        return new Score(
            unitsDestroyed: enemyUnitsDestroyed,
            buildingsDestroyed: enemyBuildingsDestroyed,
            resourcesGathered: resourcesGathered,
            unitsRemaining: playerUnits,
            buildingsRemaining: playerBuildings);
    }

    public Guid DetermineWinner(Match match)
    {
        var player1Total = match.Player1Score.TotalScore;
        var player2Total = match.Player2Score.TotalScore;

        return player1Total >= player2Total ? match.Player1Id : match.Player2Id;
    }
}
