# API Endpoints Specification

**Version**: v1
**Base URL**: `/api/v1`
**Date**: 2025-12-09

## Overview

RESTful API for RTS game engine. All endpoints return JSON. Authentication via JWT Bearer tokens.

## Authentication

**Header**: `Authorization: Bearer <jwt-token>`

**Token Claims**:
```json
{
  "sub": "player-uuid",
  "name": "PlayerName",
  "exp": 1234567890
}
```

---

## Lobby Management

### POST /lobbies
Create a new match lobby.

**Request**:
```json
{
  "name": "My Epic Match",
  "gameSettings": {
    "mapWidth": 100,
    "mapHeight": 100,
    "maxTicks": 1800,
    "tickIntervalMs": 1000,
    "commandQueueSize": 500,
    "commandsPerTick": 100,
    "startingResources": 500
  }
}
```

**Response** (201 Created):
```json
{
  "lobbyId": "uuid",
  "name": "My Epic Match",
  "hostPlayerId": "uuid",
  "status": "Open",
  "currentPlayerCount": 1,
  "maxPlayers": 2,
  "gameSettings": { ... },
  "createdAt": "2025-12-09T10:00:00Z"
}
```

---

### GET /lobbies
List all open lobbies.

**Query Parameters**:
- `status` (optional): Filter by LobbyStatus (Open, Full)
- `limit` (optional): Max results (default: 50)
- `offset` (optional): Pagination offset (default: 0)

**Response** (200 OK):
```json
{
  "lobbies": [
    {
      "lobbyId": "uuid",
      "name": "Lobby Name",
      "hostPlayerName": "Host123",
      "currentPlayerCount": 1,
      "maxPlayers": 2,
      "status": "Open",
      "createdAt": "2025-12-09T10:00:00Z"
    }
  ],
  "totalCount": 5,
  "limit": 50,
  "offset": 0
}
```

---

### GET /lobbies/{lobbyId}
Get lobby details.

**Response** (200 OK):
```json
{
  "lobbyId": "uuid",
  "name": "Lobby Name",
  "hostPlayerId": "uuid",
  "status": "Full",
  "players": [
    {
      "playerId": "uuid",
      "username": "Player1",
      "slot": 1,
      "isReady": true,
      "joinedAt": "2025-12-09T10:00:00Z"
    },
    {
      "playerId": "uuid",
      "username": "Player2",
      "slot": 2,
      "isReady": false,
      "joinedAt": "2025-12-09T10:01:00Z"
    }
  ],
  "gameSettings": { ... }
}
```

---

### POST /lobbies/{lobbyId}/join
Join an existing lobby.

**Response** (200 OK):
```json
{
  "lobbyId": "uuid",
  "playerId": "uuid",
  "slot": 2,
  "message": "Successfully joined lobby"
}
```

**Errors**:
- 404: Lobby not found
- 409: Lobby full or already joined

---

### POST /lobbies/{lobbyId}/leave
Leave a lobby before match starts.

**Response** (204 No Content)

---

### PUT /lobbies/{lobbyId}/settings
Update lobby game settings (host only).

**Request**:
```json
{
  "mapWidth": 150,
  "maxTicks": 2400
}
```

**Response** (200 OK):
```json
{
  "gameSettings": { ...updated settings }
}
```

**Errors**:
- 403: Only host can update settings
- 409: Cannot update after match started

---

### POST /lobbies/{lobbyId}/start
Start the match (host only, when lobby full).

**Response** (200 OK):
```json
{
  "matchId": "uuid",
  "message": "Match started successfully"
}
```

**Errors**:
- 403: Only host can start match
- 400: Lobby not full

---

## Match Management

### GET /matches/{matchId}
Get match metadata (not game state).

**Response** (200 OK):
```json
{
  "matchId": "uuid",
  "status": "Active",
  "currentTick": 450,
  "player1": {
    "playerId": "uuid",
    "username": "Player1"
  },
  "player2": {
    "playerId": "uuid",
    "username": "Player2"
  },
  "startedAt": "2025-12-09T10:05:00Z",
  "endedAt": null,
  "winnerId": null,
  "mapWidth": 100,
  "mapHeight": 100,
  "maxTicks": 1800
}
```

---

### GET /matches/{matchId}/state
Get current game state for requesting player (includes fog of war).

**Response** (200 OK):
```json
{
  "matchId": "uuid",
  "currentTick": 450,
  "playerId": "uuid",
  "resources": 750,
  "commandQueueSize": 12,
  "units": [
    {
      "unitId": 5,
      "type": "Worker",
      "position": { "x": 10, "y": 10 },
      "healthPoints": 50,
      "maxHealthPoints": 50,
      "status": "Gathering",
      "currentCommand": "Gather resources at (15, 10)"
    }
  ],
  "buildings": [
    {
      "buildingId": 1,
      "type": "CommandCenter",
      "position": { "x": 5, "y": 5 },
      "healthPoints": 500,
      "maxHealthPoints": 500,
      "isOperational": true,
      "constructionProgress": 100,
      "productionQueue": []
    }
  ],
  "visibleEnemyUnits": [
    {
      "unitId": 25,
      "type": "Soldier",
      "position": { "x": 50, "y": 50 },
      "healthPoints": 80,
      "maxHealthPoints": 100
    }
  ],
  "visibleEnemyBuildings": [],
  "resourceDeposits": [
    {
      "depositId": 1,
      "position": { "x": 15, "y": 10 },
      "resourceType": "Ore",
      "remainingCapacity": 4200
    }
  ],
  "visibleMapArea": [
    { "x": 0, "y": 0, "terrainType": "Passable", "occupied": false },
    ...
  ],
  "score": {
    "unitsDestroyed": 2,
    "buildingsDestroyed": 0,
    "resourcesGathered": 250,
    "unitsRemaining": 15,
    "buildingsRemaining": 3,
    "totalScore": 145
  }
}
```

---

### GET /matches/{matchId}/result
Get final match results (only after match completed).

**Response** (200 OK):
```json
{
  "matchId": "uuid",
  "status": "Completed",
  "winnerId": "uuid",
  "winnerName": "Player1",
  "winCondition": "Elimination",
  "finalTick": 1234,
  "duration": "20m 34s",
  "player1": {
    "playerId": "uuid",
    "username": "Player1",
    "finalScore": 450,
    "scoreBreakdown": {
      "unitsDestroyed": 15,
      "buildingsDestroyed": 3,
      "resourcesGathered": 800,
      "unitsRemaining": 10,
      "buildingsRemaining": 2
    }
  },
  "player2": {
    "playerId": "uuid",
    "username": "Player2",
    "finalScore": 280,
    "scoreBreakdown": { ... }
  }
}
```

**Errors**:
- 409: Match still active

---

## Command Submission

### POST /matches/{matchId}/commands
Queue commands for units/buildings.

**Request**:
```json
{
  "commands": [
    {
      "type": "Move",
      "unitIds": [1, 2, 3, 4, 5],
      "targetPosition": { "x": 50, "y": 50 }
    },
    {
      "type": "Attack",
      "unitIds": [10, 11, 12],
      "targetEntityId": 25
    },
    {
      "type": "Gather",
      "unitIds": [6, 7],
      "resourceDepositId": 1
    },
    {
      "type": "Build",
      "unitIds": [1, 2, 3],
      "buildingType": "Barracks",
      "targetPosition": { "x": 20, "y": 20 }
    },
    {
      "type": "Produce",
      "buildingId": 2,
      "unitType": "Soldier"
    },
    {
      "type": "Research",
      "buildingId": 5,
      "upgradeType": "WeaponDamage1"
    }
  ]
}
```

**Response** (202 Accepted):
```json
{
  "queuedCommands": 6,
  "failedCommands": [],
  "currentQueueSize": 18,
  "message": "Commands queued successfully"
}
```

**Partial Success Response** (207 Multi-Status):
```json
{
  "queuedCommands": 4,
  "failedCommands": [
    {
      "commandIndex": 1,
      "type": "Attack",
      "error": "Target entity not visible or out of range"
    },
    {
      "commandIndex": 3,
      "type": "Build",
      "error": "Target position already occupied"
    }
  ],
  "currentQueueSize": 22
}
```

**Errors**:
- 400: Validation failed (invalid coordinates, unit ownership, etc.)
- 403: Not a player in this match
- 409: Command queue full
- 404: Match not found

---

## Leaderboard

### GET /leaderboard
Get ranked player standings.

**Query Parameters**:
- `period` (optional): "all-time" (default), "monthly", "weekly"
- `limit` (optional): Max results (default: 100)
- `offset` (optional): Pagination (default: 0)

**Response** (200 OK):
```json
{
  "period": "all-time",
  "lastUpdated": "2025-12-09T11:00:00Z",
  "entries": [
    {
      "rank": 1,
      "playerId": "uuid",
      "username": "ProBot3000",
      "totalScore": 8500,
      "matchesPlayed": 25,
      "matchesWon": 18,
      "winRate": 0.72,
      "averageScore": 340
    },
    {
      "rank": 2,
      "playerId": "uuid",
      "username": "MegaMind",
      "totalScore": 7200,
      "matchesPlayed": 20,
      "matchesWon": 15,
      "winRate": 0.75,
      "averageScore": 360
    }
  ],
  "totalPlayers": 150,
  "limit": 100,
  "offset": 0
}
```

---

### GET /leaderboard/player/{playerId}
Get specific player's leaderboard stats.

**Response** (200 OK):
```json
{
  "playerId": "uuid",
  "username": "MyBot",
  "rank": 42,
  "totalScore": 2400,
  "matchesPlayed": 10,
  "matchesWon": 6,
  "winRate": 0.60,
  "averageScore": 240,
  "recentMatches": [
    {
      "matchId": "uuid",
      "opponent": "OtherBot",
      "result": "Win",
      "score": 380,
      "playedAt": "2025-12-09T10:30:00Z"
    }
  ]
}
```

---

## Player Management

### POST /players
Register a new player/bot.

**Request**:
```json
{
  "username": "MyAwesomeBot",
  "email": "bot@example.com",
  "isBot": true
}
```

**Response** (201 Created):
```json
{
  "playerId": "uuid",
  "username": "MyAwesomeBot",
  "token": "jwt-token-here",
  "createdAt": "2025-12-09T12:00:00Z"
}
```

**Errors**:
- 409: Username already taken

---

### GET /players/me
Get current player profile.

**Response** (200 OK):
```json
{
  "playerId": "uuid",
  "username": "MyBot",
  "isBot": true,
  "createdAt": "2025-12-09T12:00:00Z",
  "totalMatches": 10,
  "totalWins": 6,
  "currentElo": 1350
}
```

---

## Error Responses

All errors follow RFC 7807 Problem Details format:

```json
{
  "type": "https://api.netrts.com/errors/validation-error",
  "title": "Validation Failed",
  "status": 400,
  "detail": "Unit IDs [5, 10] do not belong to requesting player",
  "instance": "/api/v1/matches/uuid/commands",
  "traceId": "correlation-id-here"
}
```

**Common Status Codes**:
- 200: Success
- 201: Created
- 202: Accepted (async operation)
- 204: No Content
- 207: Multi-Status (partial success)
- 400: Bad Request (validation error)
- 401: Unauthorized (missing/invalid token)
- 403: Forbidden (not authorized for resource)
- 404: Not Found
- 409: Conflict (queue full, lobby full, etc.)
- 429: Too Many Requests (rate limiting)
- 500: Internal Server Error

---

## Rate Limiting

**Limits**:
- Command submission: 10 requests/second per player
- Game state queries: 5 requests/second per player
- Lobby operations: 20 requests/minute per player

**Headers**:
```
X-RateLimit-Limit: 10
X-RateLimit-Remaining: 7
X-RateLimit-Reset: 1234567890
```

---

## Versioning

API version in URL path: `/api/v1/`

**Deprecation Policy**:
- Deprecated endpoints supported for 1 minor version
- Deprecation warnings in response headers:
  ```
  Deprecation: true
  Sunset: 2026-01-01T00:00:00Z
  Link: </api/v2/endpoint>; rel="successor-version"
  ```

---

## SignalR Hub (Real-Time Events)

**Hub URL**: `/hubs/game`

**Events Pushed to Clients**:

```csharp
// Game state updated (after each tick)
OnGameStateUpdated(Guid matchId, GameStateDto state)

// Match ended
OnMatchEnded(Guid matchId, MatchResultDto result)

// Player joined/left lobby
OnLobbyPlayerJoined(Guid lobbyId, LobbyPlayerDto player)
OnLobbyPlayerLeft(Guid lobbyId, Guid playerId)

// Match started from lobby
OnMatchStarted(Guid lobbyId, Guid matchId)
```

**Client Methods**:
```csharp
// Subscribe to match updates
await connection.InvokeAsync("SubscribeToMatch", matchId);

// Unsubscribe
await connection.InvokeAsync("UnsubscribeFromMatch", matchId);
```

---

## Summary

- **26 REST endpoints** covering lobby, match, command, leaderboard, player
- **Authentication**: JWT Bearer tokens
- **Real-time**: SignalR for live updates
- **Error Handling**: RFC 7807 Problem Details
- **Rate Limiting**: Per-player quotas
- **Versioning**: URL-based with deprecation policy

All endpoints designed for easy consumption by bot clients and web UI.
