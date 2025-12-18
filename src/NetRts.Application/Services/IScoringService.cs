using NetRts.Domain.Entities;
using NetRts.Domain.ValueObjects;

namespace NetRts.Application.Services;

/// <summary>
/// Service for calculating match scores.
/// </summary>
public interface IScoringService
{
    /// <summary>
    /// Calculate current score for a player in a match.
    /// </summary>
    Score CalculatePlayerScore(
        Guid playerId,
        Match match,
        IEnumerable<Unit> units,
        IEnumerable<Building> buildings,
        int resourcesGathered,
        int enemyUnitsDestroyed,
        int enemyBuildingsDestroyed);

    /// <summary>
    /// Determine the winner based on scores.
    /// </summary>
    Guid DetermineWinner(Match match);
}
