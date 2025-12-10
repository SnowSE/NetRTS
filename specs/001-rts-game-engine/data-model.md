# Data Model: RTS Game Engine

**Feature**: RTS Game Engine for Programming Competition
**Date**: 2025-12-09
**Status**: Complete

## Overview

This document defines the domain entities, value objects, and relationships for the RTS game engine. The model follows Domain-Driven Design principles with clear aggregate boundaries and enforces business rules at the domain level.

## Entity Relationship Diagram

```
┌─────────────┐         ┌─────────────┐
│   Player    │────┬────│   Match     │
└─────────────┘    │    └─────────────┘
                   │           │
                   │           │ 1:N
                   │    ┌──────┴──────┐
                   │    │             │
                   │ ┌──▼───┐    ┌────▼────┐
                   └─│ Unit │    │Building │
                     └──┬───┘    └────┬────┘
                        │             │
                     ┌──▼──────────────▼──┐
                     │   Command Queue    │
                     └────────────────────┘

┌─────────────┐         ┌──────────────┐
│ Match Lobby │────┬────│ LobbyPlayer  │
└─────────────┘    │    └──────────────┘
                   │
                ┌──▼────────┐
                │   Config  │
                └───────────┘

┌──────────────┐
│ Leaderboard  │─────┐
└──────────────┘     │
                  ┌──▼─────────┐
                  │PlayerScore │
                  └────────────┘
```

## Core Domain Entities

### 1. Match (Aggregate Root)

**Purpose**: Represents a single RTS game instance between two players

**Fields**:
- `Id` (Guid): Unique match identifier
- `Status` (MatchStatus enum): `Pending`, `Active`, `Completed`, `Abandoned`
- `CurrentTick` (int): Current game tick number (0 to 1800)
- `MapWidth` (int): Map width in tiles (default: 100)
- `MapHeight` (int): Map height in tiles (default: 100)
- `MaxTicksPerMatch` (int): Match duration limit (default: 1800)
- `TickIntervalMs` (int): Milliseconds between ticks (default: 1000)
- `CommandQueueSizeLimit` (int): Max commands per player queue (default: 500)
- `CommandsPerTick` (int): Commands processed per tick (default: 100)
- `StartedAt` (DateTime?): When match started (null if pending)
- `EndedAt` (DateTime?): When match ended (null if active)
- `WinnerId` (Guid?): ID of winning player (null if active)
- `Player1Id` (Guid): First player
- `Player2Id` (Guid): Second player
- `Player1Score` (Score): Player 1 score breakdown
- `Player2Score` (Score): Player 2 score breakdown
- `MapTiles` (List<MapTile>): All map tiles with terrain/occupancy
- `ResourceDeposits` (List<ResourceDeposit>): Resource locations
- `GameStateSnapshot` (byte[]): Serialized state for crash recovery

**Relationships**:
- 1:N to `Unit` (all units in match)
- 1:N to `Building` (all buildings in match)
- 1:N to `Command` (queued commands for both players)
- N:1 to `Player` (each match has 2 players)

**Business Rules**:
- Match cannot start unless both players joined
- Match auto-ends at `MaxTicksPerMatch` ticks
- Match ends immediately if a player's Command Center is destroyed
- Winner determined by highest score if time limit reached
- Tick processing atomic (all commands execute or none)

**State Transitions**:
```
Pending → Active (when host starts match)
Active → Completed (when victory condition met)
Active → Abandoned (if player disconnects > 5 minutes)
```

---

### 2. Player (Aggregate Root)

**Purpose**: Represents a competitor (human or bot) across multiple matches

**Fields**:
- `Id` (Guid): Unique player identifier
- `Username` (string): Display name (unique)
- `Email` (string): Contact email (optional for bots)
- `IsBot` (bool): Distinguishes automated vs human players
- `CreatedAt` (DateTime): Registration timestamp
- `LastActiveAt` (DateTime): Last API call
- `TotalMatches` (int): Lifetime match count
- `TotalWins` (int): Lifetime win count
- `CurrentElo` (int): Elo rating for matchmaking (default: 1200)

**Relationships**:
- 1:N to `Match` (player can participate in many matches)
- 1:N to `PlayerScore` (leaderboard entries)

**Business Rules**:
- Username must be unique and 3-20 characters
- Cannot delete player with active matches
- Elo updated after each completed match

---

### 3. Unit (Entity, owned by Match)

**Purpose**: Represents a controllable game entity (worker, soldier, scout)

**Fields**:
- `Id` (int): Unique identifier within match (sequential)
- `MatchId` (Guid): Parent match
- `OwnerId` (Guid): Player who owns this unit
- `Type` (UnitType enum): `Worker`, `Soldier`, `Scout`
- `Position` (Position value object): Current (X, Y) coordinates
- `HealthPoints` (int): Current health (0 = destroyed)
- `MaxHealthPoints` (int): Maximum health (type-specific)
- `AttackDamage` (int): Damage per attack (type-specific)
- `AttackRange` (int): Tiles within which unit can attack (type-specific)
- `MovementSpeed` (int): Tiles per tick (type-specific)
- `VisionRange` (int): Tiles visible around unit (type-specific)
- `CurrentStatus` (UnitStatus enum): `Idle`, `Moving`, `Attacking`, `Gathering`, `Constructing`
- `TargetPosition` (Position?): Destination for movement
- `TargetEntityId` (int?): Target unit/building for attack
- `CurrentCommand` (Command?): Currently executing command
- `ResourcesCarried` (int): Amount of ore/crystals being carried (workers only)

**Relationships**:
- N:1 to `Match`
- N:1 to `Player` (owner)
- 0:1 to `Command` (current command)

**Business Rules**:
- Unit destroyed when HealthPoints reaches 0
- Worker-only actions: Gather resources, construct buildings
- Soldier/Scout-only actions: Attack
- Vision range determines fog of war visibility
- Cannot move outside map bounds
- Cannot occupy same tile as another unit/building

**Unit Type Stats**:
| Type    | HP  | Damage | Range | Speed | Vision | Cost |
|---------|-----|--------|-------|-------|--------|------|
| Worker  | 50  | 5      | 1     | 2     | 5      | 50   |
| Soldier | 100 | 15     | 2     | 1     | 5      | 100  |
| Scout   | 60  | 8      | 2     | 3     | 8      | 75   |

---

### 4. Building (Entity, owned by Match)

**Purpose**: Represents a structure that produces units or enables upgrades

**Fields**:
- `Id` (int): Unique identifier within match
- `MatchId` (Guid): Parent match
- `OwnerId` (Guid): Player who owns this building
- `Type` (BuildingType enum): `CommandCenter`, `Barracks`, `ResourceDepot`, `TechLab`
- `Position` (Position value object): Location on map
- `HealthPoints` (int): Current health
- `MaxHealthPoints` (int): Maximum health (type-specific)
- `ConstructionProgress` (int): Percentage complete (0-100)
- `IsOperational` (bool): True when ConstructionProgress == 100
- `VisionRange` (int): Tiles visible around building (type-specific)
- `ProductionQueue` (List<ProductionOrder>): Queued unit production
- `CurrentResearch` (Upgrade?): Upgrade being researched

**Relationships**:
- N:1 to `Match`
- N:1 to `Player` (owner)
- 1:N to `ProductionOrder` (production queue)

**Business Rules**:
- Building destroyed when HealthPoints reaches 0
- Cannot produce units until `IsOperational == true`
- Construction requires workers adjacent to tile
- Command Center destruction triggers match end (elimination victory)
- ResourceDepot increases max resource storage (unused in v1, future expansion)

**Building Type Stats**:
| Type          | HP  | Construction Time | Vision | Cost |
|---------------|-----|-------------------|--------|------|
| CommandCenter | 500 | 30 ticks          | 6      | N/A  |
| Barracks      | 300 | 20 ticks          | 5      | 200  |
| ResourceDepot | 200 | 15 ticks          | 5      | 150  |
| TechLab       | 250 | 25 ticks          | 5      | 300  |

---

### 5. Command (Entity, owned by Match)

**Purpose**: Represents a queued action submitted by a player

**Fields**:
- `Id` (long): Unique command identifier
- `MatchId` (Guid): Parent match
- `PlayerId` (Guid): Player who issued command
- `Type` (CommandType enum): `Move`, `Attack`, `Gather`, `Build`, `Produce`, `Research`
- `TargetUnitIds` (List<int>): Unit IDs receiving this command (e.g., "units 5-10")
- `TargetBuildingId` (int?): Building ID for production/research commands
- `TargetPosition` (Position?): Destination for move/build commands
- `TargetEntityId` (int?): Target unit/building ID for attack commands
- `TargetResourceDepositId` (int?): Resource deposit for gather commands
- `SubmittedAtTick` (int): Tick when command was queued
- `ProcessedAtTick` (int?): Tick when command was executed (null if queued)
- `Status` (CommandStatus enum): `Queued`, `Executed`, `Failed`, `Cancelled`
- `FailureReason` (string?): Error message if Status == Failed

**Relationships**:
- N:1 to `Match`
- N:1 to `Player` (issuer)

**Business Rules**:
- Commands validated before adding to queue
- Validation checks: Unit ownership, bounds, target existence
- Queue size limited to `CommandQueueSizeLimit` per player
- Commands processed FIFO up to `CommandsPerTick` limit
- Most recent command for same unit overrides previous (within same tick)

**Validation Rules**:
- `Move`: TargetPosition within map bounds
- `Attack`: Target entity visible and in range (after movement)
- `Gather`: TargetResourceDepositId exists and visible
- `Build`: TargetPosition unoccupied, TargetUnitIds are workers
- `Produce`: TargetBuildingId operational, sufficient resources
- `Research`: TargetBuildingId is TechLab, prerequisites met

---

### 6. MapTile (Value Object, owned by Match)

**Purpose**: Represents a single tile on the game grid

**Fields**:
- `Position` (Position value object): (X, Y) coordinates
- `TerrainType` (TerrainType enum): `Passable`, `Impassable` (v1 only Passable)
- `OccupiedByUnitId` (int?): Unit currently on this tile
- `OccupiedByBuildingId` (int?): Building on this tile
- `VisibleToPlayer1` (bool): Fog of war visibility for player 1
- `VisibleToPlayer2` (bool): Fog of war visibility for player 2

**Relationships**:
- N:1 to `Match`
- 0:1 to `Unit` (occupant)
- 0:1 to `Building` (occupant)

**Business Rules**:
- Tile can be occupied by at most one unit OR one building
- Visibility recalculated each tick based on unit/building vision ranges
- Impassable terrain planned for future; v1 all tiles passable

---

### 7. ResourceDeposit (Entity, owned by Match)

**Purpose**: Represents a gatherable resource location (ore, crystals)

**Fields**:
- `Id` (int): Unique identifier within match
- `MatchId` (Guid): Parent match
- `Position` (Position value object): Location on map
- `ResourceType` (string): "Ore" (v1 single type)
- `RemainingCapacity` (int): Resources left to gather
- `InitialCapacity` (int): Starting resource amount (default: 5000)
- `GatherRate` (int): Resources per tick per worker (default: 10)

**Relationships**:
- N:1 to `Match`

**Business Rules**:
- Capacity depletes as workers gather
- Deposit removed from map when RemainingCapacity reaches 0
- Multiple workers can gather from same deposit (first-come-first-served)

---

### 8. Upgrade (Entity, owned by Match)

**Purpose**: Represents a research improvement that enhances units/buildings

**Fields**:
- `Id` (int): Unique identifier within match
- `MatchId` (Guid): Parent match
- `OwnerId` (Guid): Player researching upgrade
- `Type` (UpgradeType enum): `WeaponDamage1`, `WeaponDamage2`, `Armor1`, `Armor2`, `Speed1`, `Speed2`
- `ResearchProgress` (int): Percentage complete (0-100)
- `ResearchTicksRequired` (int): Total ticks to complete (type-specific)
- `IsCompleted` (bool): True when ResearchProgress == 100
- `ResourceCost` (int): Resource cost (type-specific)

**Relationships**:
- N:1 to `Match`
- N:1 to `Player` (owner)

**Business Rules**:
- Upgrades researched at TechLab building
- Prerequisites enforced (tier 1 before tier 2)
- Effects apply immediately to all existing and future units
- Upgrades persist for duration of match (not global across matches)

**Upgrade Effects**:
| Type          | Effect                  | Cost | Time   | Prerequisite |
|---------------|-------------------------|------|--------|--------------|
| WeaponDamage1 | +5 attack damage        | 200  | 30     | None         |
| WeaponDamage2 | +10 attack damage       | 400  | 60     | WeaponDamage1|
| Armor1        | +20 max health          | 200  | 30     | None         |
| Armor2        | +40 max health          | 400  | 60     | Armor1       |
| Speed1        | +1 movement speed       | 150  | 25     | None         |
| Speed2        | +1 movement speed       | 300  | 50     | Speed1       |

---

## Lobby & Leaderboard Entities

### 9. MatchLobby (Aggregate Root)

**Purpose**: Pre-game area where players join and host configures match settings

**Fields**:
- `Id` (Guid): Unique lobby identifier
- `Name` (string): Display name for lobby listing
- `HostPlayerId` (Guid): Player who created lobby (controls settings)
- `Status` (LobbyStatus enum): `Open`, `Full`, `Starting`, `Closed`
- `MaxPlayers` (int): Always 2 for 1v1 matches
- `CurrentPlayerCount` (int): Players joined (0-2)
- `GameSettings` (GameSettings value object): Configurable match parameters
- `CreatedAt` (DateTime): Lobby creation timestamp
- `Players` (List<LobbyPlayer>): Players in lobby

**Relationships**:
- 1:N to `LobbyPlayer` (players in lobby)
- 1:1 to `Match` (created when host starts game)

**Business Rules**:
- Host can modify GameSettings until match starts
- Lobby transitions to `Full` when 2 players joined
- Host can start match when lobby is `Full`
- Lobby auto-closes 10 minutes after creation if not started

---

### 10. LobbyPlayer (Entity, owned by MatchLobby)

**Purpose**: Represents a player in a lobby

**Fields**:
- `LobbyId` (Guid): Parent lobby
- `PlayerId` (Guid): Player who joined
- `JoinedAt` (DateTime): Join timestamp
- `IsReady` (bool): Player ready status
- `Slot` (int): 1 or 2 (determines starting position in match)

**Relationships**:
- N:1 to `MatchLobby`
- N:1 to `Player`

**Business Rules**:
- Players can leave lobby before match starts
- Both players must be ready for host to start match (optional rule)

---

### 11. Leaderboard (Value Object)

**Purpose**: Ranked list of players by score/wins

**Fields**:
- `Entries` (List<PlayerScore>): Sorted player rankings
- `LastUpdated` (DateTime): When leaderboard was last recalculated
- `ScoringPeriod` (string): "All-Time", "Monthly", "Weekly"

**Relationships**:
- Contains list of `PlayerScore` entries

**Business Rules**:
- Leaderboard recalculated after each completed match
- Sorted by total score descending, then wins descending

---

### 12. PlayerScore (Entity)

**Purpose**: Leaderboard entry for a single player

**Fields**:
- `PlayerId` (Guid): Player identifier
- `Username` (string): Display name
- `TotalScore` (int): Sum of scores from all matches
- `MatchesPlayed` (int): Total matches
- `MatchesWon` (int): Total wins
- `WinRate` (decimal): MatchesWon / MatchesPlayed
- `AverageScore` (int): TotalScore / MatchesPlayed
- `Rank` (int): Position on leaderboard (1 = highest)

**Relationships**:
- N:1 to `Player`

**Business Rules**:
- Score accumulated from completed matches only
- Rank recalculated when leaderboard updated

---

## Value Objects

### Position

**Purpose**: Immutable (X, Y) coordinate pair

**Fields**:
- `X` (int): Horizontal position (0 to MapWidth - 1)
- `Y` (int): Vertical position (0 to MapHeight - 1)

**Methods**:
- `DistanceTo(Position other)`: Calculate Euclidean distance
- `IsWithinRange(Position other, int range)`: Check if within vision/attack range
- `GetAdjacentPositions()`: Return 4 orthogonal neighbors

**Business Rules**:
- Immutable (set once, never modified)
- Equality based on X and Y values

---

### Score

**Purpose**: Breakdown of match scoring components

**Fields**:
- `UnitsDestroyed` (int): Points from destroying enemy units (10 points each)
- `BuildingsDestroyed` (int): Points from destroying enemy buildings (50 points each)
- `ResourcesGathered` (int): Points from gathered resources (1 point per 10 resources)
- `UnitsRemaining` (int): Points for units alive at match end (5 points each)
- `BuildingsRemaining` (int): Points for buildings standing at match end (25 points each)
- `TotalScore` (int): Sum of all components

**Methods**:
- `CalculateTotal()`: Recompute TotalScore from components

**Business Rules**:
- Immutable after match ends
- Used to determine winner if time limit reached

---

### GameSettings

**Purpose**: Configurable match parameters set by lobby host

**Fields**:
- `MapWidth` (int): Map width in tiles (50-200, default: 100)
- `MapHeight` (int): Map height in tiles (50-200, default: 100)
- `MaxTicks` (int): Match duration limit (600-3600, default: 1800)
- `TickIntervalMs` (int): Milliseconds per tick (500-2000, default: 1000)
- `CommandQueueSize` (int): Max queue size (100-1000, default: 500)
- `CommandsPerTick` (int): Commands processed per tick (50-200, default: 100)
- `StartingResources` (int): Initial resources (100-1000, default: 500)

**Business Rules**:
- All values constrained to valid ranges
- Host can only modify before match starts
- Default values ensure balanced gameplay

---

### UnitStats

**Purpose**: Type-specific unit attributes

**Fields**:
- `UnitType` (UnitType enum): Unit type
- `MaxHealthPoints` (int): Max HP
- `AttackDamage` (int): Damage per attack
- `AttackRange` (int): Attack range in tiles
- `MovementSpeed` (int): Tiles per tick
- `VisionRange` (int): Vision radius
- `ResourceCost` (int): Cost to produce

**Business Rules**:
- Defined per unit type (Worker, Soldier, Scout)
- Modified by upgrades during match

---

## Enumerations

### MatchStatus
- `Pending`: Lobby created, waiting for players
- `Active`: Match in progress
- `Completed`: Match ended with winner
- `Abandoned`: Player(s) disconnected

### UnitType
- `Worker`: Gathers resources, constructs buildings
- `Soldier`: Basic combat unit
- `Scout`: Fast movement, extended vision

### BuildingType
- `CommandCenter`: Main base, produces workers
- `Barracks`: Produces soldiers
- `ResourceDepot`: Increases storage (future)
- `TechLab`: Researches upgrades

### CommandType
- `Move`: Move units to target position
- `Attack`: Attack target entity
- `Gather`: Collect resources from deposit
- `Build`: Construct building at position
- `Produce`: Queue unit production at building
- `Research`: Start upgrade research

### UnitStatus
- `Idle`: No active command
- `Moving`: Traveling to destination
- `Attacking`: Engaging target
- `Gathering`: Collecting resources
- `Constructing`: Building structure

### CommandStatus
- `Queued`: In command queue, not processed
- `Executed`: Successfully executed
- `Failed`: Validation failed or error
- `Cancelled`: Removed from queue

### UpgradeType
- `WeaponDamage1`: +5 attack damage
- `WeaponDamage2`: +10 attack damage (requires tier 1)
- `Armor1`: +20 max health
- `Armor2`: +40 max health (requires tier 1)
- `Speed1`: +1 movement speed
- `Speed2`: +1 movement speed (requires tier 1)

### TerrainType
- `Passable`: Units can move through (v1 only)
- `Impassable`: Blocked tiles (future)

### LobbyStatus
- `Open`: Accepting players
- `Full`: 2 players joined
- `Starting`: Host initiated match start
- `Closed`: Lobby deleted or match started

---

## Aggregate Boundaries

### Match Aggregate
- **Root**: Match
- **Entities**: Unit, Building, Command, MapTile, ResourceDeposit, Upgrade
- **Boundary**: All game state for a single match
- **Consistency**: Tick processing is transactional (all-or-nothing)

### Player Aggregate
- **Root**: Player
- **Entities**: None (player is standalone)
- **Boundary**: Player profile and statistics

### MatchLobby Aggregate
- **Root**: MatchLobby
- **Entities**: LobbyPlayer
- **Boundary**: Pre-game lobby configuration

---

## Database Indexes (Performance Optimization)

### EF Core Index Definitions

**Match**:
- `IX_Match_Status` on `Status` (query active matches)
- `IX_Match_Player1Id_Player2Id` on `(Player1Id, Player2Id)` (query player matches)
- `IX_Match_EndedAt` on `EndedAt` (leaderboard queries)

**Unit**:
- `IX_Unit_MatchId_OwnerId` on `(MatchId, OwnerId)` (fog of war queries)
- `IX_Unit_Position` on `(Position.X, Position.Y)` (spatial queries)

**Building**:
- `IX_Building_MatchId_OwnerId` on `(MatchId, OwnerId)` (fog of war queries)
- `IX_Building_Position` on `(Position.X, Position.Y)` (collision checks)

**Command**:
- `IX_Command_MatchId_PlayerId_Status` on `(MatchId, PlayerId, Status)` (queue processing)

**PlayerScore**:
- `IX_PlayerScore_TotalScore` on `TotalScore DESC` (leaderboard sorting)

---

## Validation Rules Summary

| Entity    | Field             | Validation                                |
|-----------|-------------------|-------------------------------------------|
| Match     | MapWidth/Height   | 50-200 tiles                              |
| Match     | MaxTicks          | 600-3600 ticks                            |
| Player    | Username          | 3-20 characters, alphanumeric + underscore|
| Unit      | Position          | Within map bounds (0 to Width/Height - 1) |
| Building  | Position          | Within map bounds, tile unoccupied        |
| Command   | TargetUnitIds     | All units owned by player                 |
| Command   | TargetPosition    | Within map bounds                         |
| GameSettings | All values     | Within configured min/max ranges          |

---

## Future Extensions (Not in v1)

- **Multiple Resource Types**: Ore + Crystals with different uses
- **Terrain Effects**: Impassable tiles, high ground bonuses
- **Unit Abilities**: Special powers with cooldowns
- **Alliances**: 2v2 or free-for-all matches
- **Spectator Mode**: Non-player observers
- **Replay System**: Record and playback matches
- **AI Opponents**: Built-in bot for training

---

## Summary

The data model provides:
- **Clear domain boundaries**: Aggregates enforce consistency
- **Type safety**: Enums and value objects prevent invalid states
- **Performance**: Indexes on critical query paths
- **Extensibility**: Designed for future features without breaking changes
- **Testability**: Domain logic isolated from infrastructure

All entities and relationships align with functional requirements from the specification and support efficient implementation of game tick processing, fog of war, command queuing, and scoring.
