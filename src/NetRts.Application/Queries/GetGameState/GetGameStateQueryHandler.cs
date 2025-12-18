using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Contracts.Responses;
using NetRts.Domain.Enums;
using DomainUnit = NetRts.Domain.Entities.Unit;
using DomainBuilding = NetRts.Domain.Entities.Building;

namespace NetRts.Application.Queries.GetGameState;

/// <summary>
/// Handler for GetGameStateQuery that retrieves and filters game state for a player.
/// </summary>
public class GetGameStateQueryHandler : IRequestHandler<GetGameStateQuery, GameStateResponse>
{
    private readonly IGameStateCache _gameStateCache;
    private readonly IFogOfWarCalculator _fogOfWarCalculator;
    private readonly IApplicationDbContext _dbContext;

    public GetGameStateQueryHandler(
        IGameStateCache gameStateCache,
        IFogOfWarCalculator fogOfWarCalculator,
        IApplicationDbContext dbContext)
    {
        _gameStateCache = gameStateCache;
        _fogOfWarCalculator = fogOfWarCalculator;
        _dbContext = dbContext;
    }

    public async Task<GameStateResponse> Handle(GetGameStateQuery request, CancellationToken cancellationToken)
    {
        // Retrieve match from cache
        var match = _gameStateCache.GetMatch(request.MatchId);
        if (match == null)
        {
            throw new InvalidOperationException($"Match {request.MatchId} not found in cache");
        }

        // Verify player is participant in match
        if (match.Player1Id != request.PlayerId && match.Player2Id != request.PlayerId)
        {
            throw new UnauthorizedAccessException($"Player {request.PlayerId} is not a participant in match {request.MatchId}");
        }

        // Get all entities for the match from cache
        var allUnits = _gameStateCache.GetUnitsForMatch(request.MatchId);
        var allBuildings = _gameStateCache.GetBuildingsForMatch(request.MatchId);
        var allResourceDeposits = _gameStateCache.GetResourceDepositsForMatch(request.MatchId);
        var allMapTiles = _gameStateCache.GetMapTilesForMatch(request.MatchId);

        // Calculate fog of war
        var visiblePositions = _fogOfWarCalculator.CalculateVisiblePositions(
            request.PlayerId,
            allUnits.Where(u => u.OwnerId == request.PlayerId).ToList(),
            allBuildings.Where(b => b.OwnerId == request.PlayerId).ToList());

        // Filter units - only show player's own units and enemy units within vision
        var visibleUnits = allUnits
            .Where(u => u.OwnerId == request.PlayerId || visiblePositions.Contains(u.Position))
            .ToList();

        // Filter buildings - only show player's own buildings and enemy buildings within vision
        var visibleBuildings = allBuildings
            .Where(b => b.OwnerId == request.PlayerId || visiblePositions.Contains(b.Position))
            .ToList();

        // Filter resource deposits - only show those within vision
        var visibleResourceDeposits = allResourceDeposits
            .Where(r => visiblePositions.Contains(r.Position))
            .ToList();

        // Filter map tiles - only show explored or visible tiles
        var visibleMapTiles = allMapTiles
            .Where(t => visiblePositions.Contains(t.Position))
            .ToList();

        // Get player resources and scores
        var playerResources = match.GetPlayerResources(request.PlayerId);
        var opponentId = match.Player1Id == request.PlayerId ? match.Player2Id : match.Player1Id;

        // Map to response DTOs
        var response = new GameStateResponse
        {
            MatchId = match.Id,
            PlayerId = request.PlayerId,
            CurrentTick = match.CurrentTick,
            MatchStatus = match.Status.ToString(),
            PlayerResources = playerResources,
            PlayerScore = MapScoreToDto(match.Player1Id == request.PlayerId ? match.Player1Score : match.Player2Score),
            OpponentScore = MapScoreToDto(match.Player1Id == request.PlayerId ? match.Player2Score : match.Player1Score),
            Units = visibleUnits.Select(MapUnitToDto).ToList(),
            Buildings = visibleBuildings.Select(MapBuildingToDto).ToList(),
            ResourceDeposits = visibleResourceDeposits.Select(MapResourceDepositToDto).ToList(),
            VisibleTiles = visibleMapTiles.Select(t => MapMapTileToDto(t, visiblePositions)).ToList(),
            MapWidth = match.MapWidth,
            MapHeight = match.MapHeight,
            WinnerId = match.WinnerId
        };

        return response;
    }

    private static UnitDto MapUnitToDto(DomainUnit unit)
    {
        return new UnitDto
        {
            UnitId = unit.Id,
            PlayerId = unit.OwnerId,
            UnitType = unit.Type.ToString(),
            Position = new PositionDto { X = unit.Position.X, Y = unit.Position.Y },
            Health = unit.HealthPoints,
            MaxHealth = unit.MaxHealthPoints,
            CurrentState = unit.CurrentStatus.ToString(),
            TargetUnitId = unit.TargetEntityId,
            TargetPosition = unit.TargetPosition != null
                ? new PositionDto { X = unit.TargetPosition.X, Y = unit.TargetPosition.Y }
                : null,
            TargetResourceId = null, // TODO: Add when resource deposit targeting is implemented
            CarriedResources = unit.ResourcesCarried
        };
    }

    private static BuildingDto MapBuildingToDto(DomainBuilding building)
    {
        return new BuildingDto
        {
            BuildingId = building.Id,
            PlayerId = building.OwnerId,
            BuildingType = building.Type.ToString(),
            Position = new PositionDto { X = building.Position.X, Y = building.Position.Y },
            Health = building.HealthPoints,
            MaxHealth = building.MaxHealthPoints,
            IsConstructed = building.IsOperational,
            ConstructionProgress = building.ConstructionProgress,
            IsTraining = false, // TODO: Implement production queue
            TrainingUnitType = null,
            TrainingProgress = 0,
            TrainingTimeRemaining = 0
        };
    }

    private static ResourceDepositDto MapResourceDepositToDto(Domain.Entities.ResourceDeposit deposit)
    {
        return new ResourceDepositDto
        {
            ResourceId = deposit.Id,
            Position = new PositionDto { X = deposit.Position.X, Y = deposit.Position.Y },
            RemainingCapacity = deposit.RemainingCapacity,
            IsDepleted = deposit.RemainingCapacity <= 0
        };
    }

    private static MapTileDto MapMapTileToDto(Domain.Entities.MapTile tile, HashSet<Domain.ValueObjects.Position> visiblePositions)
    {
        var isVisible = visiblePositions.Contains(tile.Position);

        return new MapTileDto
        {
            Position = new PositionDto { X = tile.Position.X, Y = tile.Position.Y },
            TerrainType = tile.TerrainType.ToString(),
            IsPassable = tile.TerrainType == TerrainType.Passable,
            IsVisible = isVisible,
            IsExplored = isVisible // For now, tiles are explored when visible (could track separately later)
        };
    }

    private static ScoreDto MapScoreToDto(Domain.ValueObjects.Score score)
    {
        return new ScoreDto
        {
            UnitsDestroyed = score.UnitsDestroyed,
            BuildingsDestroyed = score.BuildingsDestroyed,
            ResourcesCollected = score.ResourcesGathered,
            TotalScore = score.TotalScore
        };
    }
}
