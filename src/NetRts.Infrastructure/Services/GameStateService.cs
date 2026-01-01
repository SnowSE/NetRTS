using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Responses;
using NetRts.Domain.Entities;
using NetRts.Infrastructure.Caching;

namespace NetRts.Infrastructure.Services;

/// <summary>
/// Service for retrieving game state with fog of war applied.
/// </summary>
public class GameStateService : IGameStateService
{
    private readonly IGameStateCache _gameStateCache;
    private readonly IMatchRepository _matchRepository;
    private readonly IFogOfWarCalculator _fogOfWarCalculator;

    public GameStateService(
        IGameStateCache gameStateCache,
        IMatchRepository matchRepository,
        IFogOfWarCalculator fogOfWarCalculator)
    {
        _gameStateCache = gameStateCache;
        _matchRepository = matchRepository;
        _fogOfWarCalculator = fogOfWarCalculator;
    }

    public async Task<GameStateResponse?> GetGameStateAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        var match = _gameStateCache.GetMatch(matchId);

        // If not in cache, load from database
        if (match == null)
        {
            match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
            if (match == null)
            {
                return null;
            }
        }

        // Verify player is participant
        if (match.Player1Id != playerId && match.Player2Id != playerId)
        {
            return null; // Player not in this match
        }

        // Determine opponent
        var opponentId = match.Player1Id == playerId ? match.Player2Id : match.Player1Id;
        var playerScore = match.Player1Id == playerId ? match.Player1Score : match.Player2Score;
        var opponentScore = match.Player1Id == playerId ? match.Player2Score : match.Player1Score;

        // Retrieve game entities from cache or database
        var units = _gameStateCache.GetUnitsForMatch(matchId);
        var buildings = _gameStateCache.GetBuildingsForMatch(matchId);
        var resourceDeposits = _gameStateCache.GetResourceDepositsForMatch(matchId);
        var mapTiles = _gameStateCache.GetMapTilesForMatch(matchId);

        // If not in cache, load from database
        if (units.Count == 0 || buildings.Count == 0)
        {
            var matchWithEntities = await _matchRepository.GetByIdWithEntitiesAsync(matchId, cancellationToken);
            if (matchWithEntities != null)
            {
                units = matchWithEntities.Units.ToList();
                buildings = matchWithEntities.Buildings.ToList();
                resourceDeposits = matchWithEntities.ResourceDeposits.ToList();
                mapTiles = matchWithEntities.MapTiles.ToList();

                // Update cache for future requests
                _gameStateCache.SetUnitsForMatch(matchId, units);
                _gameStateCache.SetBuildingsForMatch(matchId, buildings);
                _gameStateCache.SetResourceDepositsForMatch(matchId, resourceDeposits);
                _gameStateCache.SetMapTilesForMatch(matchId, mapTiles);
            }
        }

        // Calculate visible positions using fog of war
        var visiblePositions = _fogOfWarCalculator.CalculateVisiblePositions(playerId, units, buildings);

        // Filter units - only show player's units and visible enemy units
        var visibleUnits = units
            .Where(u => u.OwnerId == playerId || visiblePositions.Contains(u.Position))
            .Select(u => new UnitDto
            {
                UnitId = u.Id,
                UnitType = u.Type.ToString(),
                Position = new PositionDto { X = u.Position.X, Y = u.Position.Y },
                Health = u.HealthPoints,
                MaxHealth = u.MaxHealthPoints,
                PlayerId = u.OwnerId,
                CurrentState = u.CurrentStatus.ToString(),
                TargetUnitId = u.TargetEntityId,
                TargetPosition = u.TargetPosition != null ? new PositionDto { X = u.TargetPosition.X, Y = u.TargetPosition.Y } : null,
                CarriedResources = u.ResourcesCarried
            })
            .ToList();

        // Filter buildings - only show player's buildings and visible enemy buildings
        var visibleBuildings = buildings
            .Where(b => b.OwnerId == playerId || visiblePositions.Contains(b.Position))
            .Select(b => new BuildingDto
            {
                BuildingId = b.Id,
                BuildingType = b.Type.ToString(),
                Position = new PositionDto { X = b.Position.X, Y = b.Position.Y },
                Health = b.HealthPoints,
                MaxHealth = b.MaxHealthPoints,
                PlayerId = b.OwnerId,
                ConstructionProgress = b.ConstructionProgress,
                IsConstructed = b.IsOperational
            })
            .ToList();

        // Filter resource deposits - only show visible ones
        var visibleDeposits = resourceDeposits
            .Where(d => visiblePositions.Contains(d.Position))
            .Select(d => new ResourceDepositDto
            {
                ResourceId = d.Id,
                Position = new PositionDto { X = d.Position.X, Y = d.Position.Y },
                RemainingCapacity = d.RemainingCapacity,
                IsDepleted = d.RemainingCapacity == 0
            })
            .ToList();

        // Filter map tiles - only show visible ones
        var visibleTiles = mapTiles
            .Where(t => visiblePositions.Contains(t.Position))
            .Select(t => new MapTileDto
            {
                Position = new PositionDto { X = t.Position.X, Y = t.Position.Y },
                TerrainType = t.TerrainType.ToString(),
                IsPassable = t.TerrainType == Domain.Enums.TerrainType.Passable,
                IsVisible = true,
                IsExplored = true // All visible tiles are explored
            })
            .ToList();

        // Get player resources (this would come from match state or player entity)
        // For now, calculate from match state or use a default
        var playerResources = match.Player1Id == playerId ? 500 : 500; // TODO: Track actual resources

        return new GameStateResponse
        {
            MatchId = match.Id,
            PlayerId = playerId,
            CurrentTick = match.CurrentTick,
            MatchStatus = match.Status.ToString(),
            PlayerResources = playerResources,
            PlayerScore = new ScoreDto
            {
                UnitsDestroyed = playerScore.UnitsDestroyed,
                BuildingsDestroyed = playerScore.BuildingsDestroyed,
                ResourcesCollected = playerScore.ResourcesGathered,
                TotalScore = playerScore.TotalScore
            },
            OpponentScore = new ScoreDto
            {
                UnitsDestroyed = opponentScore.UnitsDestroyed,
                BuildingsDestroyed = opponentScore.BuildingsDestroyed,
                ResourcesCollected = opponentScore.ResourcesGathered,
                TotalScore = opponentScore.TotalScore
            },
            Units = visibleUnits,
            Buildings = visibleBuildings,
            ResourceDeposits = visibleDeposits,
            VisibleTiles = visibleTiles,
            MapWidth = match.MapWidth,
            MapHeight = match.MapHeight,
            WinnerId = match.WinnerId
        };
    }
}
