using MediatR;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Contracts.Responses;
using DomainUnit = NetRts.Domain.Entities.Unit;
using DomainBuilding = NetRts.Domain.Entities.Building;

namespace NetRts.Application.Commands.CreateMatch;

/// <summary>
/// Handler for CreateMatchCommand that initializes a new match with starting entities.
/// </summary>
public class CreateMatchCommandHandler : IRequestHandler<CreateMatchCommand, StartMatchResponse>
{
    private readonly IMatchLobbyRepository _lobbyRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IGameStateCache _gameStateCache;
    private readonly IGameUpdateBroadcaster _broadcaster;
    private readonly Random _random = new();

    public CreateMatchCommandHandler(
        IMatchLobbyRepository lobbyRepository,
        IMatchRepository matchRepository,
        IGameStateCache gameStateCache,
        IGameUpdateBroadcaster broadcaster)
    {
        _lobbyRepository = lobbyRepository;
        _matchRepository = matchRepository;
        _gameStateCache = gameStateCache;
        _broadcaster = broadcaster;
    }

    public async Task<StartMatchResponse> Handle(CreateMatchCommand request, CancellationToken cancellationToken)
    {
        // Get lobby and validate it has 2 players
        var lobby = await _lobbyRepository.GetByIdAsync(request.LobbyId, cancellationToken);
        if (lobby == null)
        {
            throw new InvalidOperationException($"Lobby {request.LobbyId} not found");
        }

        var lobbyPlayers = lobby.Players.ToList();
        if (lobbyPlayers.Count != 2)
        {
            throw new InvalidOperationException($"Lobby must have exactly 2 players, but has {lobbyPlayers.Count}");
        }

        if (lobby.HostPlayerId != request.PlayerId)
        {
            throw new UnauthorizedAccessException("Only the host can start the match");
        }

        // Create match with settings from lobby
        var player1Id = lobbyPlayers[0].PlayerId;
        var player2Id = lobbyPlayers[1].PlayerId;

        var match = new Match(player1Id, player2Id, lobby.GameSettings);
        
        // Start the match so the tick processor will process it
        match.Start();

        // ... existing code ...
        // Generate starting positions (opposite corners)
        var player1SpawnPosition = new Position(10, 10);
        var player2SpawnPosition = new Position(90, 90);

        // Generate starting units (5 workers for each player)
        var units = new List<DomainUnit>();
        int unitIdCounter = 1;

        for (int i = 0; i < 5; i++)
        {
            var workerPosition1 = new Position(
                player1SpawnPosition.X + (i % 3),
                player1SpawnPosition.Y + (i / 3));

            units.Add(new DomainUnit(
                id: unitIdCounter++,
                matchId: match.Id,
                ownerId: player1Id,
                type: UnitType.Worker,
                position: workerPosition1));
        }

        for (int i = 0; i < 5; i++)
        {
            var workerPosition2 = new Position(
                player2SpawnPosition.X + (i % 3),
                player2SpawnPosition.Y + (i / 3));

            units.Add(new DomainUnit(
                id: unitIdCounter++,
                matchId: match.Id,
                ownerId: player2Id,
                type: UnitType.Worker,
                position: workerPosition2));
        }

        // Generate starting buildings (1 Command Center for each player)
        var buildings = new List<DomainBuilding>();

        var building1 = new DomainBuilding(
            id: 1,
            matchId: match.Id,
            ownerId: player1Id,
            type: BuildingType.CommandCenter,
            position: player1SpawnPosition);
        building1.AdvanceConstruction(100); // Mark as fully constructed
        buildings.Add(building1);

        var building2 = new DomainBuilding(
            id: 2,
            matchId: match.Id,
            ownerId: player2Id,
            type: BuildingType.CommandCenter,
            position: player2SpawnPosition);
        building2.AdvanceConstruction(100); // Mark as fully constructed
        buildings.Add(building2);

        // Generate map tiles
        var mapTiles = new List<MapTile>();
        for (int x = 0; x < match.MapWidth; x++)
        {
            for (int y = 0; y < match.MapHeight; y++)
            {
                mapTiles.Add(new MapTile(
                    matchId: match.Id,
                    position: new Position(x, y),
                    terrainType: TerrainType.Passable));
            }
        }

        // Place resource deposits
        var resourceDeposits = new List<ResourceDeposit>();
        var depositPositions = new[]
        {
            new Position(20, 20),
            new Position(80, 80),
            new Position(50, 50),
            new Position(30, 70),
            new Position(70, 30),
        };

        for (int i = 0; i < depositPositions.Length; i++)
        {
            resourceDeposits.Add(new ResourceDeposit(
                id: i + 1,
                matchId: match.Id,
                position: depositPositions[i],
                initialCapacity: 5000));
        }

        // Initialize empty upgrades list
        var upgrades = new List<Upgrade>();

        // Store initialized match state in cache
        _gameStateCache.SetMatch(match.Id, match);
        _gameStateCache.SetUnitsForMatch(match.Id, units);
        _gameStateCache.SetBuildingsForMatch(match.Id, buildings);
        _gameStateCache.SetMapTilesForMatch(match.Id, mapTiles);
        _gameStateCache.SetResourceDepositsForMatch(match.Id, resourceDeposits);
        _gameStateCache.SetUpgradesForMatch(match.Id, upgrades);

        // Persist match metadata to database
        await _matchRepository.AddAsync(match, cancellationToken);

        // Close the lobby
        lobby.Close();
        await _lobbyRepository.UpdateAsync(lobby, cancellationToken);

        // Broadcast match started to lobby group
        await _broadcaster.BroadcastMatchStartedAsync(request.LobbyId, match.Id, cancellationToken);

        return new StartMatchResponse
        {
            MatchId = match.Id,
            Player1Id = player1Id,
            Player2Id = player2Id
        };
    }
}