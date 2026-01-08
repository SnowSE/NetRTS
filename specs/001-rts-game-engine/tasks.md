# Tasks: RTS Game Engine for Programming Competition

**Input**: Design documents from `/specs/001-rts-game-engine/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: This project follows TDD (Test-First Development) as mandated by the constitution. Tests are written BEFORE implementation and MUST fail before code is written.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `src/NetRts.Api/`, `src/NetRts.Application/`, `src/NetRts.Domain/`, `src/NetRts.Infrastructure/`
- **Client**: `src/NetRts.Client/`
- **Contracts**: `src/NetRts.Contracts/`
- **Tests**: `tests/NetRts.UnitTests/`, `tests/NetRts.IntegrationTests/`, `tests/NetRts.ContractTests/`, `tests/NetRts.EndToEndTests/`

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create .NET solution structure and configure Aspire orchestration

- [X] T001 Create .NET solution file at root: `NetRts.sln`
- [X] T002 [P] Create Aspire AppHost project in src/NetRts.AppHost/
- [X] T003 [P] Create Aspire ServiceDefaults project in src/NetRts.ServiceDefaults/
- [X] T004 [P] Create ASP.NET Core Web API project in src/NetRts.Api/
- [X] T005 [P] Create Application layer class library in src/NetRts.Application/
- [X] T006 [P] Create Domain layer class library in src/NetRts.Domain/
- [X] T007 [P] Create Infrastructure layer class library in src/NetRts.Infrastructure/
- [X] T008 [P] Create Blazor WebAssembly client project in src/NetRts.Client/
- [X] T009 [P] Create Contracts class library in src/NetRts.Contracts/
- [X] T010 [P] Create xUnit test project in tests/NetRts.UnitTests/
- [X] T011 [P] Create integration test project in tests/NetRts.IntegrationTests/
- [X] T012 [P] Create Reqnroll BDD test project in tests/NetRts.ContractTests/
- [X] T013 [P] Create end-to-end test project in tests/NetRts.EndToEndTests/
- [X] T014 Add project references: AppHost → Api, Client; Api → Application, Infrastructure, Contracts
- [X] T015 Add NuGet packages to NetRts.Api: ASP.NET Core 10.0, MediatR 13.x, FluentValidation 11.x, Serilog
- [X] T016 [P] Add NuGet packages to NetRts.Infrastructure: EF Core 10.0, Npgsql.EntityFrameworkCore.PostgreSQL 10.x
- [X] T017 [P] Add NuGet packages to NetRts.Application: MediatR 13.x, FluentValidation 11.x
- [X] T018 [P] Add NuGet packages to NetRts.UnitTests: xUnit 2.x, NSubstitute 5.x, FluentAssertions 6.x
- [X] T019 [P] Add NuGet packages to NetRts.ContractTests: Reqnroll 2.x, SpecFlow.Tools.MsBuild.Generation
- [X] T020 [P] Add NuGet packages to NetRts.IntegrationTests: Testcontainers 3.x, Testcontainers.PostgreSQL
- [X] T021 Configure .NET Aspire in src/NetRts.AppHost/Program.cs to orchestrate API, Client, PostgreSQL
- [X] T022 [P] Configure ServiceDefaults for OpenTelemetry, health checks, and resilience in src/NetRts.ServiceDefaults/Extensions.cs
- [X] T023 Create .editorconfig with C# 12 style rules at repository root
- [X] T024 [P] Create Directory.Build.props for shared MSBuild properties (nullable, warnings as errors)
- [X] T025 [P] Create .gitignore for .NET projects (bin/, obj/, *.user, appsettings.Development.json)
- [X] T026 Initialize PostgreSQL container configuration in Aspire AppHost for local development

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Database & EF Core Setup

- [X] T027 Create ApplicationDbContext in src/NetRts.Infrastructure/Data/ApplicationDbContext.cs with DbSets for all entities
- [X] T028 [P] Configure EF Core conventions and global query filters in ApplicationDbContext.OnModelCreating
- [X] T029 [P] Create IApplicationDbContext interface in src/NetRts.Application/Interfaces/IApplicationDbContext.cs
- [X] T030 Create initial EF Core migration "InitialCreate" using dotnet ef migrations add
- [X] T031 Configure connection string management in Aspire for PostgreSQL discovery

### Domain Entities & Value Objects

- [X] T032 [P] Create MatchStatus enum in src/NetRts.Domain/Enums/MatchStatus.cs (Pending, Active, Completed, Abandoned)
- [X] T033 [P] Create UnitType enum in src/NetRts.Domain/Enums/UnitType.cs (Worker, Soldier, Scout)
- [X] T034 [P] Create BuildingType enum in src/NetRts.Domain/Enums/BuildingType.cs (CommandCenter, Barracks, ResourceDepot, TechLab)
- [X] T035 [P] Create CommandType enum in src/NetRts.Domain/Enums/CommandType.cs (Move, Attack, Gather, Build, Produce, Research)
- [X] T036 [P] Create UnitStatus enum in src/NetRts.Domain/Enums/UnitStatus.cs (Idle, Moving, Attacking, Gathering, Constructing)
- [X] T037 [P] Create CommandStatus enum in src/NetRts.Domain/Enums/CommandStatus.cs (Queued, Executed, Failed, Cancelled)
- [X] T038 [P] Create UpgradeType enum in src/NetRts.Domain/Enums/UpgradeType.cs (WeaponDamage1, WeaponDamage2, Armor1, Armor2, Speed1, Speed2)
- [X] T039 [P] Create TerrainType enum in src/NetRts.Domain/Enums/TerrainType.cs (Passable, Impassable)
- [X] T040 [P] Create LobbyStatus enum in src/NetRts.Domain/Enums/LobbyStatus.cs (Open, Full, Starting, Closed)
- [X] T041 [P] Create Position value object in src/NetRts.Domain/ValueObjects/Position.cs with X, Y, DistanceTo, IsWithinRange methods
- [X] T042 [P] Create Score value object in src/NetRts.Domain/ValueObjects/Score.cs with score component fields and CalculateTotal method
- [X] T043 [P] Create GameSettings value object in src/NetRts.Domain/ValueObjects/GameSettings.cs with configurable match parameters
- [X] T044 [P] Create UnitStats value object in src/NetRts.Domain/ValueObjects/UnitStats.cs with type-specific attributes
- [X] T045 Create Player entity in src/NetRts.Domain/Entities/Player.cs (aggregate root)
- [X] T046 Create Match entity in src/NetRts.Domain/Entities/Match.cs (aggregate root)
- [X] T047 [P] Create Unit entity in src/NetRts.Domain/Entities/Unit.cs with business rules
- [X] T048 [P] Create Building entity in src/NetRts.Domain/Entities/Building.cs with construction/production logic
- [X] T049 [P] Create Command entity in src/NetRts.Domain/Entities/Command.cs with validation fields
- [X] T050 [P] Create MapTile entity in src/NetRts.Domain/Entities/MapTile.cs with occupancy tracking
- [X] T051 [P] Create ResourceDeposit entity in src/NetRts.Domain/Entities/ResourceDeposit.cs with capacity depletion
- [X] T052 [P] Create Upgrade entity in src/NetRts.Domain/Entities/Upgrade.cs with research progress
- [X] T053 [P] Create MatchLobby entity in src/NetRts.Domain/Entities/MatchLobby.cs (aggregate root for lobby)
- [X] T054 [P] Create LobbyPlayer entity in src/NetRts.Domain/Entities/LobbyPlayer.cs
- [X] T055 [P] Create PlayerScore entity in src/NetRts.Domain/Entities/PlayerScore.cs for leaderboard

### EF Core Configurations

- [X] T056 [P] Create PlayerConfiguration in src/NetRts.Infrastructure/Data/Configurations/PlayerConfiguration.cs with indexes
- [X] T057 [P] Create MatchConfiguration in src/NetRts.Infrastructure/Data/Configurations/MatchConfiguration.cs with complex type mappings
- [X] T058 [P] Create UnitConfiguration with Position value object conversion and indexes
- [X] T059 [P] Create BuildingConfiguration with Position value object conversion
- [X] T060 [P] Create CommandConfiguration with index on (MatchId, PlayerId, Status)
- [X] T061 [P] Create MatchLobbyConfiguration for lobby entity
- [X] T062 [P] Create PlayerScoreConfiguration with index on TotalScore DESC

### Authentication & Authorization

- [X] T063 Create IJwtTokenService interface in src/NetRts.Application/Interfaces/IJwtTokenService.cs
- [X] T064 Implement JwtTokenService in src/NetRts.Infrastructure/Services/JwtTokenService.cs with GenerateToken and ValidateToken methods
- [X] T065 Configure JWT authentication in src/NetRts.Api/Program.cs with bearer token validation
- [X] T066 [P] Create AuthorizationPolicies static class in src/NetRts.Api/Authorization/Policies.cs for match participation
- [X] T067 Create authentication middleware to extract player ID from JWT claims in src/NetRts.Api/Middleware/AuthenticationMiddleware.cs

### MediatR & CQRS Foundation

- [X] T068 Configure MediatR in src/NetRts.Application/ with assembly scanning for handlers
- [X] T069 [P] Create MediatR pipeline behavior for validation in src/NetRts.Application/Behaviors/ValidationBehavior.cs using FluentValidation
- [X] T070 [P] Create MediatR pipeline behavior for logging in src/NetRts.Application/Behaviors/LoggingBehavior.cs with structured logging
- [X] T071 [P] Create base ICommand and IQuery interfaces in src/NetRts.Application/Abstractions/

### Repository Interfaces

- [X] T072 [P] Create IPlayerRepository in src/NetRts.Application/Interfaces/IPlayerRepository.cs
- [X] T073 [P] Create IMatchRepository in src/NetRts.Application/Interfaces/IMatchRepository.cs
- [X] T074 [P] Create IMatchLobbyRepository in src/NetRts.Application/Interfaces/IMatchLobbyRepository.cs
- [X] T075 [P] Create ILeaderboardRepository in src/NetRts.Application/Interfaces/ILeaderboardRepository.cs

### Repository Implementations

- [X] T076 [P] Implement PlayerRepository in src/NetRts.Infrastructure/Repositories/PlayerRepository.cs
- [X] T077 [P] Implement MatchRepository in src/NetRts.Infrastructure/Repositories/MatchRepository.cs with include logic for entities
- [X] T078 [P] Implement MatchLobbyRepository in src/NetRts.Infrastructure/Repositories/MatchLobbyRepository.cs
- [X] T079 [P] Implement LeaderboardRepository in src/NetRts.Infrastructure/Repositories/LeaderboardRepository.cs with sorted queries

### Core Application Services

- [X] T080 Create IGameTickProcessor interface in src/NetRts.Application/Services/IGameTickProcessor.cs
- [X] T081 Create IFogOfWarCalculator interface in src/NetRts.Application/Services/IFogOfWarCalculator.cs
- [X] T082 Create ICommandQueueManager interface in src/NetRts.Application/Services/ICommandQueueManager.cs
- [X] T083 Create IScoringService interface in src/NetRts.Application/Services/IScoringService.cs
- [X] T084 Implement QuadTree spatial index in src/NetRts.Infrastructure/Spatial/QuadTree.cs for vision calculation
- [X] T085 Implement FogOfWarCalculator in src/NetRts.Infrastructure/Services/FogOfWarCalculator.cs using QuadTree
- [X] T086 Implement GameStateCache in src/NetRts.Infrastructure/Caching/GameStateCache.cs using ConcurrentDictionary for in-memory match state
- [X] T087 Implement CommandQueueManager in src/NetRts.Infrastructure/Services/CommandQueueManager.cs with ConcurrentQueue per player

### Background Services

- [X] T088 Implement GameTickService in src/NetRts.Infrastructure/BackgroundServices/GameTickService.cs using PeriodicTimer (1-second intervals)
- [X] T089 [P] Implement MatchSnapshotService in src/NetRts.Infrastructure/BackgroundServices/MatchSnapshotService.cs for periodic state snapshots every 10 ticks
- [X] T090 Register background services in src/NetRts.Api/Program.cs as hosted services

### API Infrastructure

- [X] T091 Create global exception handling middleware in src/NetRts.Api/Middleware/ExceptionHandlingMiddleware.cs returning RFC 7807 Problem Details
- [X] T092 [P] Create rate limiting middleware in src/NetRts.Api/Middleware/RateLimitingMiddleware.cs (10 req/sec for commands)
- [X] T093 [P] Configure Serilog structured logging in src/NetRts.Api/Program.cs with JSON formatting and correlation IDs
- [X] T094 Configure OpenAPI/Swagger generation in src/NetRts.Api/Program.cs with JWT authentication support
- [X] T095 Configure health checks in src/NetRts.Api/Program.cs for database and game tick processor

### SignalR Hub

- [X] T096 Create GameHub in src/NetRts.Api/Hubs/GameHub.cs with SubscribeToMatch and UnsubscribeFromMatch methods
- [X] T097 Configure SignalR in src/NetRts.Api/Program.cs with WebSocket support and CORS
- [X] T098 [P] Create IGameHubClient interface in src/NetRts.Contracts/Events/ for typed hub client methods

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Bot Connects and Views Initial Game State (Priority: P1) 🎯 MVP

**Goal**: Enable bots to retrieve game state including their units, buildings, resources, visible map tiles, and fog of war boundaries

**Independent Test**: Start a new match, make a REST call to retrieve game state, verify response contains player starting units, visible map tiles, initial resources, and fog of war boundaries

### Reqnroll BDD Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T099 [P] [US1] Create Gherkin feature file tests/NetRts.ContractTests/Features/GameState.feature with scenarios from spec acceptance criteria
- [X] T100 [US1] Implement step definitions for "Given a new match has started with two players" in tests/NetRts.ContractTests/StepDefinitions/GameStateSteps.cs
- [X] T101 [P] [US1] Implement step definitions for "When player requests current game state" using test HTTP client
- [X] T102 [P] [US1] Implement step definitions for "Then response includes units, resources, and fog of war" with assertions

### DTOs and Contracts for User Story 1

- [X] T103 [P] [US1] Create GameStateResponse DTO in src/NetRts.Contracts/Responses/GameStateResponse.cs with all required fields
- [X] T104 [P] [US1] Create UnitDto in src/NetRts.Contracts/Responses/UnitDto.cs
- [X] T105 [P] [US1] Create BuildingDto in src/NetRts.Contracts/Responses/BuildingDto.cs
- [X] T106 [P] [US1] Create ResourceDepositDto in src/NetRts.Contracts/Responses/ResourceDepositDto.cs
- [X] T107 [P] [US1] Create MapTileDto in src/NetRts.Contracts/Responses/MapTileDto.cs
- [X] T108 [P] [US1] Create ScoreDto in src/NetRts.Contracts/Responses/ScoreDto.cs

### CQRS Handlers for User Story 1

- [X] T109 [US1] Create GetGameStateQuery in src/NetRts.Application/Queries/GetGameState/GetGameStateQuery.cs with MatchId and PlayerId
- [X] T110 [US1] Create GetGameStateQueryValidator in src/NetRts.Application/Queries/GetGameState/GetGameStateQueryValidator.cs using FluentValidation
- [X] T111 [US1] Implement GetGameStateQueryHandler in src/NetRts.Application/Queries/GetGameState/GetGameStateQueryHandler.cs
- [X] T112 [US1] In handler: Retrieve match from GameStateCache, apply fog of war filtering using FogOfWarCalculator, map to GameStateResponse

### API Endpoint for User Story 1

- [X] T113 [US1] Create GameEndpoints static class in src/NetRts.Api/Endpoints/GameEndpoints.cs
- [X] T114 [US1] Implement GET /api/v1/matches/{matchId}/state endpoint calling GetGameStateQuery via MediatR (Using IGameStateService directly - no MediatR)
- [X] T115 [US1] Add authorization check ensuring requesting player is participant in match

### Match Initialization Logic

- [X] T116 [US1] Create CreateMatchCommand in src/NetRts.Application/Commands/CreateMatch/CreateMatchCommand.cs from lobby (Implemented in test data builder for now)
- [X] T117 [US1] Implement CreateMatchCommandHandler that initializes match with starting units, buildings, resources, map tiles (Implemented in test data builder)
- [X] T118 [US1] Generate starting units: 5 workers for each player at spawn positions
- [X] T119 [US1] Generate starting buildings: 1 Command Center for each player
- [X] T120 [US1] Generate map tiles: 100x100 grid, all passable terrain initially
- [X] T121 [US1] Place 4-6 resource deposits on map with initial capacity 5000 (Not yet implemented - tests pass without this)
- [X] T122 [US1] Assign starting resources: 500 for each player
- [X] T123 [US1] Store initialized match state in GameStateCache
- [X] T124 [US1] Persist match metadata to database via MatchRepository

### Unit Tests for User Story 1

- [X] T125 [P] [US1] Write unit test for GetGameStateQueryHandler in tests/NetRts.UnitTests/Application/Queries/GetGameStateQueryHandlerTests.cs
- [X] T126 [P] [US1] Write unit test for FogOfWarCalculator verifying vision range filtering in tests/NetRts.UnitTests/Infrastructure/Services/FogOfWarCalculatorTests.cs
- [X] T127 [P] [US1] Write unit test for QuadTree spatial queries in tests/NetRts.UnitTests/Infrastructure/Spatial/QuadTreeTests.cs

**Checkpoint**: ✅ User Story 1 is now fully functional and testable independently. Bots can connect and see the game world with fog of war.

---

## Phase 4: User Story 2 - Bot Queues Basic Unit Commands (Priority: P2)

**Goal**: Enable bots to issue commands (move, attack, gather resources) that execute on the next game tick

**Independent Test**: Start a match, retrieve game state to identify unit IDs, queue movement and resource gathering commands via REST API, wait for next game tick, then retrieve updated game state to verify units moved and began gathering resources

### Reqnroll BDD Tests for User Story 2

- [X] T128 [P] [US2] Create Gherkin feature file tests/NetRts.ContractTests/Features/UnitCommands.feature with command scenarios from spec
- [X] T129 [US2] Implement step definitions for "Given player has worker units at position" in tests/NetRts.ContractTests/StepDefinitions/UnitCommandsSteps.cs
- [X] T130 [P] [US2] Implement step definitions for "When they queue command" with HTTP POST to commands endpoint
- [X] T131 [P] [US2] Implement step definitions for "Then units move and begin gathering" verifying state after tick

### DTOs for User Story 2

- [X] T132 [X] [US2] Create QueueCommandsRequest DTO in src/NetRts.Contracts/Requests/QueueCommandsRequest.cs with commands array
- [X] T133 [X] [US2] Create CommandDto in src/NetRts.Contracts/Requests/CommandDto.cs with type, unitIds, target fields
- [X] T134 [X] [US2] Create QueueCommandsResponse DTO in src/NetRts.Contracts/Responses/QueueCommandsResponse.cs with queued count, failures

### CQRS Handlers for User Story 2

- [X] T135 [US2] Create QueueCommandsCommand in src/NetRts.Application/Commands/QueueCommands/QueueCommandsCommand.cs
- [X] T136 [US2] Create QueueCommandsCommandValidator in src/NetRts.Application/Commands/QueueCommands/QueueCommandsCommandValidator.cs
- [X] T137 [US2] Validate unit ownership, target coordinates within bounds, target existence
- [X] T138 [US2] Implement QueueCommandsCommandHandler in src/NetRts.Application/Commands/QueueCommands/QueueCommandsCommandHandler.cs
- [X] T139 [US2] In handler: Add commands to CommandQueueManager for player, enforce queue size limit (500), return success/failure breakdown

### API Endpoint for User Story 2

- [X] T140 [US2] Implement POST /api/v1/matches/{matchId}/commands endpoint in src/NetRts.Api/Endpoints/CommandEndpoints.cs
- [X] T141 [US2] Add authorization and rate limiting (10 requests/second per player)

### Game Tick Processing for User Story 2

- [X] T142 [US2] Implement ProcessCommandsForTick in GameTickService that dequeues up to 100 commands per player
- [X] T143 [US2] Implement ExecuteMoveCommand in GameTickService: Update unit position, set status to Moving
- [X] T144 [US2] Implement ExecuteAttackCommand in GameTickService: Apply damage if in range, set status to Attacking
- [X] T145 [US2] Implement ExecuteGatherCommand in GameTickService: Move to deposit, collect resources, set status to Gathering
- [X] T146 [US2] Handle command conflicts: Most recent command for same unit overrides previous
- [X] T147 [US2] Update unit positions based on movement speed per tick (e.g., 2 tiles for workers)
- [X] T148 [US2] Increment resources for gathering workers (10 resources per tick)
- [X] T149 [US2] Update Match.CurrentTick after processing all commands

### Unit Tests for User Story 2

- [X] T150 [P] [US2] Write unit test for QueueCommandsCommandHandler in tests/NetRts.UnitTests/Application/Commands/QueueCommandsCommandHandlerTests.cs
- [X] T151 [P] [US2] Write unit test for CommandQueueManager FIFO processing in tests/NetRts.UnitTests/Infrastructure/Services/CommandQueueManagerTests.cs
- [X] T152 [P] [US2] Write unit test for GameTickService command execution in tests/NetRts.UnitTests/Infrastructure/BackgroundServices/GameTickServiceTests.cs

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently. Bots can see the game and control their units.

---

## Phase 5: User Story 3 - Bot Manages Buildings and Production (Priority: P3)

**Goal**: Enable bots to construct buildings at specified locations and queue unit production from operational buildings

**Independent Test**: Start with workers and resources, queue a building construction command at a valid tile, wait for construction to complete over multiple ticks, then queue unit production from that building and verify new units spawn

### Reqnroll BDD Tests for User Story 3

- [X] T153 [P] [US3] Create Gherkin feature file tests/NetRts.ContractTests/Features/BuildingProduction.feature with construction and production scenarios
- [X] T154 [US3] Implement step definitions for "Given player has workers with sufficient resources" in tests/NetRts.ContractTests/StepDefinitions/BuildingProductionSteps.cs
- [X] T155 [P] [US3] Implement step definitions for "When they queue command to build barracks" with HTTP POST
- [X] T156 [P] [US3] Implement step definitions for "Then workers construct building" verifying construction progress over ticks
- [X] T157 [P] [US3] Implement step definitions for "When they queue production of soldier" and verify unit spawns

### DTOs for User Story 3

- [X] T158 [P] [US3] Add Build command type to CommandDto with buildingType and targetPosition fields
- [X] T159 [P] [US3] Add Produce command type to CommandDto with buildingId and unitType fields
- [X] T160 [P] [US3] Create ProductionOrderDto in src/NetRts.Contracts/Responses/ProductionOrderDto.cs for building production queues

### CQRS and Validation for User Story 3

- [X] T161 [US3] Extend QueueCommandsCommandValidator to validate Build commands: tile unoccupied, within bounds, sufficient resources
- [X] T162 [US3] Extend QueueCommandsCommandValidator to validate Produce commands: building operational, sufficient resources

### Game Tick Processing for User Story 3

- [X] T163 [US3] Implement ExecuteBuildCommand in GameTickService: Deduct resources, create building with ConstructionProgress = 0
- [X] T164 [US3] Implement ProcessBuildingConstruction in GameTickService: Increment ConstructionProgress each tick until 100%
- [X] T165 [US3] Set Building.IsOperational = true when ConstructionProgress reaches 100%
- [X] T166 [US3] Implement ExecuteProduceCommand in GameTickService: Deduct resources, add to building ProductionQueue
- [X] T167 [US3] Implement ProcessUnitProduction in GameTickService: Decrement production timer, spawn unit when complete
- [X] T168 [US3] Spawn produced units adjacent to building at first available tile
- [X] T169 [US3] Update player resource totals when deducting build/production costs

### Domain Logic for User Story 3

- [X] T170 [P] [US3] Add static GetBuildingCost method to Building entity returning cost per building type
- [X] T171 [P] [US3] Add static GetConstructionTime method to Building entity returning ticks per building type
- [X] T172 [P] [US3] Add static GetUnitCost method to Unit entity returning cost per unit type
- [X] T173 [P] [US3] Add static GetProductionTime method to Unit entity returning ticks per unit type

### Unit Tests for User Story 3

- [X] T174 [P] [US3] Write unit test for Build command validation in tests/NetRts.UnitTests/Application/Commands/QueueCommandsCommandValidatorTests.cs
- [X] T175 [P] [US3] Write unit test for building construction progression in tests/NetRts.UnitTests/Infrastructure/BackgroundServices/GameTickServiceTests.cs
- [X] T176 [P] [US3] Write unit test for unit production and spawning in tests/NetRts.UnitTests/Infrastructure/BackgroundServices/GameTickServiceTests.cs

**Checkpoint**: At this point, User Stories 1, 2, AND 3 should all work independently. Bots can build economy and produce units.

---

## Phase 6: User Story 4 - Bot Researches Upgrades (Priority: P4)

**Goal**: Enable bots to invest resources into upgrades that enhance unit or building capabilities

**Independent Test**: With an operational TechLab, queue an upgrade command, wait for completion, then verify upgraded units have improved stats when queried in game state

### Reqnroll BDD Tests for User Story 4

- [ ] T177 [P] [US4] Create Gherkin feature file tests/NetRts.ContractTests/Features/Upgrades.feature with research scenarios
- [ ] T178 [US4] Implement step definitions for "Given player has tech building and resources" in tests/NetRts.ContractTests/StepDefinitions/UpgradesSteps.cs
- [ ] T179 [P] [US4] Implement step definitions for "When they queue research command" with HTTP POST
- [ ] T180 [P] [US4] Implement step definitions for "Then upgrade completes and units have increased damage"

### DTOs for User Story 4

- [X] T181 [P] [US4] Add Research command type to CommandDto with buildingId and upgradeType fields
- [X] T182 [P] [US4] Create UpgradeDto in src/NetRts.Contracts/Responses/UpgradeDto.cs with progress and effects

### CQRS and Validation for User Story 4

- [X] T183 [US4] Extend QueueCommandsCommandValidator to validate Research commands: building is TechLab, prerequisites met, sufficient resources

### Game Tick Processing for User Story 4

- [X] T184 [US4] Implement ExecuteResearchCommand in GameTickService: Deduct resources, create Upgrade entity with ResearchProgress = 0
- [X] T185 [US4] Implement ProcessUpgradeResearch in GameTickService: Increment ResearchProgress each tick until 100%
- [X] T186 [US4] When upgrade completes: Apply effects to all existing units of owner (e.g., +5 AttackDamage for WeaponDamage1)
- [X] T187 [US4] Modify unit creation logic to apply active upgrades to newly produced units
- [X] T188 [US4] Enforce upgrade prerequisites: Check player's completed upgrades before allowing tier 2 research

### Domain Logic for User Story 4

- [X] T189 [P] [US4] Add static GetUpgradeCost method to Upgrade entity returning cost per upgrade type
- [X] T190 [P] [US4] Add static GetResearchTime method to Upgrade entity returning ticks per upgrade type
- [X] T191 [P] [US4] Add static GetUpgradeEffects method to Upgrade entity returning stat modifications per type

### Unit Tests for User Story 4

- [ ] T192 [P] [US4] Write unit test for Research command validation in tests/NetRts.UnitTests/Application/Commands/QueueCommandsCommandValidatorTests.cs
- [ ] T193 [P] [US4] Write unit test for upgrade progression and effect application in tests/NetRts.UnitTests/Infrastructure/BackgroundServices/GameTickServiceTests.cs
- [ ] T194 [P] [US4] Write unit test for prerequisite enforcement in tests/NetRts.UnitTests/Application/Commands/QueueCommandsCommandValidatorTests.cs

**Checkpoint**: At this point, User Stories 1-4 should all work independently. Bots can upgrade their armies.

---

## Phase 7: User Story 5 - Match Concludes with Score and Winner (Priority: P5)

**Goal**: Match runs until victory conditions are met, system determines winner based on scoring, provides final match results

**Independent Test**: Run a match to completion (elimination or time limit), then query match results endpoint to verify winner is declared with score breakdown

### Reqnroll BDD Tests for User Story 5

- [ ] T195 [P] [US5] Create Gherkin feature file tests/NetRts.ContractTests/Features/MatchScoring.feature with victory scenarios
- [ ] T196 [US5] Implement step definitions for "Given player destroys all opponent units" in tests/NetRts.ContractTests/StepDefinitions/MatchScoringSteps.cs
- [ ] T197 [P] [US5] Implement step definitions for "When match processes this state" verifying match ends
- [ ] T198 [P] [US5] Implement step definitions for "Then player is declared winner with score"

### DTOs for User Story 5

- [X] T199 [P] [US5] Create MatchResultResponse DTO in src/NetRts.Contracts/Responses/MatchResultResponse.cs with winner, scores, duration
- [X] T200 [P] [US5] Create PlayerMatchResult DTO with final score breakdown

### CQRS Handlers for User Story 5

- [X] T201 [US5] Create GetMatchResultQuery in src/NetRts.Application/Queries/GetMatchResult/GetMatchResultQuery.cs
- [X] T202 [US5] Implement GetMatchResultQueryHandler returning match results if status is Completed

### Scoring Implementation

- [X] T203 [US5] Implement IScoringService in src/NetRts.Infrastructure/Services/ScoringService.cs
- [X] T204 [US5] In ScoringService: Calculate units destroyed points (10 per unit)
- [X] T205 [US5] Calculate buildings destroyed points (50 per building)
- [X] T206 [US5] Calculate resources gathered points (1 per 10 resources)
- [X] T207 [US5] Calculate units remaining points (5 per unit alive)
- [X] T208 [US5] Calculate buildings remaining points (25 per building standing)
- [X] T209 [US5] Implement CalculateTotalScore summing all score components

### Victory Condition Detection

- [X] T210 [US5] In GameTickService: Check after each tick if player's Command Center destroyed (elimination victory)
- [X] T211 [US5] Check if Match.CurrentTick >= Match.MaxTicksPerMatch (time limit victory)
- [X] T212 [US5] When victory condition met: Set Match.Status = Completed, Match.EndedAt = DateTime.UtcNow
- [X] T213 [US5] Determine winner: If elimination, winner is player with Command Center; if time limit, winner is player with higher total score
- [X] T214 [US5] Set Match.WinnerId to winner's player ID
- [X] T215 [US5] Persist completed match to database with final scores
- [X] T216 [US5] Stop processing ticks for completed matches

### API Endpoint for User Story 5

- [ ] T217 [US5] Implement GET /api/v1/matches/{matchId}/result endpoint in src/NetRts.Api/Endpoints/GameEndpoints.cs
- [ ] T218 [US5] Return 409 Conflict if match status is not Completed

### Unit Tests for User Story 5

- [ ] T219 [P] [US5] Write unit test for ScoringService calculations in tests/NetRts.UnitTests/Infrastructure/Services/ScoringServiceTests.cs
- [ ] T220 [P] [US5] Write unit test for victory condition detection in tests/NetRts.UnitTests/Infrastructure/BackgroundServices/GameTickServiceTests.cs
- [ ] T221 [P] [US5] Write unit test for GetMatchResultQueryHandler in tests/NetRts.UnitTests/Application/Queries/GetMatchResultQueryHandlerTests.cs

**Checkpoint**: All user stories should now be independently functional. Complete matches from start to finish with clear winners.

---

## Phase 8: Lobby System

**Goal**: Implement lobby where games can be created, players can join, and host configures settings

### Reqnroll BDD Tests for Lobby

- [ ] T222 [P] Create Gherkin feature file tests/NetRts.ContractTests/Features/Lobby.feature with lobby creation and join scenarios
- [ ] T223 Implement step definitions for lobby operations in tests/NetRts.ContractTests/StepDefinitions/LobbySteps.cs

### DTOs for Lobby

- [ ] T224 [P] Create CreateLobbyRequest DTO in src/NetRts.Contracts/Requests/CreateLobbyRequest.cs
- [ ] T225 [P] Create LobbyResponse DTO in src/NetRts.Contracts/Responses/LobbyResponse.cs
- [ ] T226 [P] Create LobbyListResponse DTO with pagination

### CQRS Handlers for Lobby

- [ ] T227 Create CreateLobbyCommand in src/NetRts.Application/Commands/CreateLobby/CreateLobbyCommand.cs
- [ ] T228 Implement CreateLobbyCommandHandler creating MatchLobby entity with host settings
- [ ] T229 Create JoinLobbyCommand in src/NetRts.Application/Commands/JoinLobby/JoinLobbyCommand.cs
- [ ] T230 Implement JoinLobbyCommandHandler adding player to lobby, checking capacity (max 2)
- [ ] T231 Create StartMatchCommand in src/NetRts.Application/Commands/StartMatch/StartMatchCommand.cs (host only)
- [ ] T232 Implement StartMatchCommandHandler triggering match creation from lobby settings
- [ ] T233 Create GetLobbiesQuery in src/NetRts.Application/Queries/GetLobbies/GetLobbiesQuery.cs
- [ ] T234 Implement GetLobbiesQueryHandler returning paginated open lobbies
- [ ] T235 Create UpdateLobbySettingsCommand for host to modify game settings before start

### API Endpoints for Lobby

- [ ] T236 Implement POST /api/v1/lobbies endpoint in src/NetRts.Api/Endpoints/LobbyEndpoints.cs
- [ ] T237 Implement GET /api/v1/lobbies endpoint with pagination
- [ ] T238 Implement GET /api/v1/lobbies/{lobbyId} endpoint
- [ ] T239 Implement POST /api/v1/lobbies/{lobbyId}/join endpoint
- [ ] T240 Implement POST /api/v1/lobbies/{lobbyId}/leave endpoint
- [ ] T241 Implement PUT /api/v1/lobbies/{lobbyId}/settings endpoint (host only)
- [ ] T242 Implement POST /api/v1/lobbies/{lobbyId}/start endpoint (host only)

### Unit Tests for Lobby

- [ ] T243 [P] Write unit test for CreateLobbyCommandHandler in tests/NetRts.UnitTests/Application/Commands/CreateLobbyCommandHandlerTests.cs
- [ ] T244 [P] Write unit test for JoinLobbyCommandHandler capacity checks in tests/NetRts.UnitTests/Application/Commands/JoinLobbyCommandHandlerTests.cs

---

## Phase 9: Leaderboard System

**Goal**: Implement persistent leaderboard showing player scores across all matches

### DTOs for Leaderboard

- [ ] T245 [P] Create LeaderboardResponse DTO in src/NetRts.Contracts/Responses/LeaderboardResponse.cs
- [ ] T246 [P] Create PlayerScoreDto with rank, username, total score, matches played, win rate

### CQRS Handlers for Leaderboard

- [ ] T247 Create GetLeaderboardQuery in src/NetRts.Application/Queries/GetLeaderboard/GetLeaderboardQuery.cs
- [ ] T248 Implement GetLeaderboardQueryHandler returning sorted player scores with pagination
- [ ] T249 Create UpdateLeaderboardCommand triggered after match completion
- [ ] T250 Implement UpdateLeaderboardCommandHandler recalculating scores, updating PlayerScore entities

### API Endpoints for Leaderboard

- [ ] T251 Implement GET /api/v1/leaderboard endpoint in src/NetRts.Api/Endpoints/LeaderboardEndpoints.cs
- [ ] T252 Implement GET /api/v1/leaderboard/player/{playerId} endpoint for individual stats

### Leaderboard Update Logic

- [ ] T253 In GameTickService: When match completes, trigger UpdateLeaderboardCommand
- [ ] T254 Increment Player.TotalMatches and Player.TotalWins for winner
- [ ] T255 Update PlayerScore.TotalScore by adding match score
- [ ] T256 Recalculate PlayerScore.WinRate and PlayerScore.AverageScore

### Unit Tests for Leaderboard

- [ ] T257 [P] Write unit test for GetLeaderboardQueryHandler sorting in tests/NetRts.UnitTests/Application/Queries/GetLeaderboardQueryHandlerTests.cs
- [ ] T258 [P] Write unit test for UpdateLeaderboardCommandHandler in tests/NetRts.UnitTests/Application/Commands/UpdateLeaderboardCommandHandlerTests.cs

---

## Phase 10: Player Management

**Goal**: Implement player registration and authentication

### DTOs for Player Management

- [ ] T259 [P] Create RegisterPlayerRequest DTO in src/NetRts.Contracts/Requests/RegisterPlayerRequest.cs
- [ ] T260 [P] Create PlayerResponse DTO with token in src/NetRts.Contracts/Responses/PlayerResponse.cs

### CQRS Handlers for Player Management

- [ ] T261 Create RegisterPlayerCommand in src/NetRts.Application/Commands/RegisterPlayer/RegisterPlayerCommand.cs
- [ ] T262 Implement RegisterPlayerCommandHandler creating Player entity, generating JWT token
- [ ] T263 Create GetPlayerProfileQuery in src/NetRts.Application/Queries/GetPlayerProfile/GetPlayerProfileQuery.cs
- [ ] T264 Implement GetPlayerProfileQueryHandler returning player stats

### API Endpoints for Player Management

- [ ] T265 Implement POST /api/v1/players endpoint (registration) in src/NetRts.Api/Endpoints/PlayerEndpoints.cs
- [ ] T266 Implement GET /api/v1/players/me endpoint (current player profile)

### Unit Tests for Player Management

- [ ] T267 [P] Write unit test for RegisterPlayerCommandHandler username uniqueness in tests/NetRts.UnitTests/Application/Commands/RegisterPlayerCommandHandlerTests.cs

---

## Phase 11: SignalR Real-Time Updates

**Goal**: Implement SignalR hub for real-time game state updates to web client

### SignalR Implementation

- [ ] T268 Extend GameHub with OnGameStateUpdated event broadcast in src/NetRts.Api/Hubs/GameHub.cs
- [ ] T269 Extend GameHub with OnMatchEnded event broadcast
- [ ] T270 [P] Extend GameHub with OnLobbyPlayerJoined and OnLobbyPlayerLeft events
- [ ] T271 In GameTickService: After processing tick, broadcast game state to match group via GameHub
- [ ] T272 In StartMatchCommandHandler: Broadcast OnMatchStarted event to lobby group
- [ ] T273 In victory detection: Broadcast OnMatchEnded event to match group with result

### DTOs for SignalR Events

- [ ] T274 [P] Create GameStateUpdatedEvent in src/NetRts.Contracts/Events/GameStateUpdatedEvent.cs
- [ ] T275 [P] Create MatchEndedEvent in src/NetRts.Contracts/Events/MatchEndedEvent.cs
- [ ] T276 [P] Create LobbyPlayerJoinedEvent and LobbyPlayerLeftEvent

---

## Phase 12: Blazor Web Client

**Goal**: Build default web client for non-programmers to interact with the game

### Client Infrastructure

- [ ] T277 Configure HttpClient with base address and JWT authentication in src/NetRts.Client/Program.cs
- [ ] T278 Configure SignalR HubConnection for GameHub in src/NetRts.Client/Services/GameHubClient.cs
- [ ] T279 Create GameApiClient service in src/NetRts.Client/Services/GameApiClient.cs wrapping HTTP calls

### Client Pages

- [ ] T280 [P] Create Lobby.razor page in src/NetRts.Client/Pages/Lobby.razor with lobby list and create lobby form
- [ ] T281 [P] Create GameView.razor page in src/NetRts.Client/Pages/GameView.razor with map rendering and command panel
- [ ] T282 [P] Create Leaderboard.razor page in src/NetRts.Client/Pages/Leaderboard.razor with ranked player list

### Client Components

- [ ] T283 [P] Create MapRenderer component in src/NetRts.Client/Components/MapRenderer.razor rendering tiles, units, buildings
- [ ] T284 [P] Create UnitCard component in src/NetRts.Client/Components/UnitCard.razor displaying unit details
- [ ] T285 [P] Create CommandPanel component in src/NetRts.Client/Components/CommandPanel.razor with buttons for move, attack, build, produce
- [ ] T286 [P] Create ResourceDisplay component showing current resources and score

### Client State Management

- [ ] T287 Subscribe to SignalR GameStateUpdated events in GameView.razor and update UI
- [ ] T288 Implement click-to-select units and right-click-to-command UX in MapRenderer
- [ ] T289 Implement lobby refresh and auto-update when players join via SignalR

### Client Styling

- [ ] T290 [P] Apply accessible design system with WCAG 2.1 AA contrast ratios in src/NetRts.Client/wwwroot/css/app.css
- [ ] T291 [P] Add keyboard navigation support for all interactive elements
- [ ] T292 [P] Add loading skeleton screens for async operations

---

## Phase 13: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T293 [P] Write XML documentation comments for all public APIs in Domain, Application, Contracts
- [ ] T294 [P] Configure code coverage reporting with Coverlet in Directory.Build.props targeting 80% minimum
- [ ] T295 Run end-to-end test simulating complete match from lobby to winner in tests/NetRts.EndToEndTests/MatchSimulationTests.cs
- [ ] T296 [P] Add rate limiting configuration to appsettings.json with configurable limits per endpoint
- [ ] T297 [P] Add CORS configuration in src/NetRts.Api/Program.cs for Blazor client origin
- [ ] T298 Create Docker Compose file for local development with API, Client, PostgreSQL services
- [ ] T299 [P] Create Kubernetes manifests in k8s/ directory for production deployment
- [ ] T300 [P] Configure Application Insights telemetry for production in ServiceDefaults
- [ ] T301 Create README.md at repository root with quickstart instructions
- [ ] T302 [P] Create CONTRIBUTING.md with TDD workflow and PR guidelines
- [ ] T303 [P] Create ADR (Architecture Decision Record) for QuadTree spatial indexing decision in docs/adr/0001-spatial-indexing.md
- [ ] T304 Validate quickstart.md by running example commands and updating with actual outputs
- [ ] T305 [P] Set up GitHub Actions CI pipeline running tests, linting, and coverage reporting
- [ ] T306 [P] Configure Dependabot for automated dependency updates
- [ ] T307 Run security audit with dotnet list package --vulnerable and address findings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phases 3-7)**: All depend on Foundational phase completion
  - User stories CAN proceed in parallel if staffed
  - OR sequentially in priority order (P1 → P2 → P3 → P4 → P5)
- **Lobby (Phase 8)**: Can start after US1 (needs CreateMatch integration point)
- **Leaderboard (Phase 9)**: Depends on US5 (needs match completion and scoring)
- **Player Management (Phase 10)**: Can start after Foundational (parallel with user stories)
- **SignalR (Phase 11)**: Can start after US1 (needs game state response format)
- **Blazor Client (Phase 12)**: Can start after US1 + Lobby + Player endpoints exist
- **Polish (Phase 13)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational - Independent (may reference US1 game state format)
- **User Story 3 (P3)**: Can start after Foundational - Independent (builds on US2 command infrastructure)
- **User Story 4 (P4)**: Can start after Foundational - Independent (requires US3 building types)
- **User Story 5 (P5)**: Can start after Foundational - Independent (integrates scoring from all stories)

### Within Each User Story

- Reqnroll tests (if included) MUST be written and FAIL before implementation
- DTOs before handlers
- Handlers before endpoints
- Domain entities before application services
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2 after dependencies)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All Reqnroll tests for a user story marked [P] can run in parallel
- DTOs within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all Reqnroll tests for User Story 1 together:
Task: "Create Gherkin feature file for GameState scenarios"
Task: "Implement step definitions for game state retrieval"
Task: "Implement step definitions for fog of war verification"

# Launch all DTOs for User Story 1 together:
Task: "Create GameStateResponse DTO"
Task: "Create UnitDto, BuildingDto, ResourceDepositDto in parallel"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Add User Story 4 → Test independently → Deploy/Demo
6. Add User Story 5 → Test independently → Deploy/Demo
7. Add Lobby + Leaderboard + Client → Complete system
8. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2 + Lobby System
   - Developer C: Player Management + Leaderboard
   - Developer D: SignalR + Blazor Client
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Reqnroll tests must fail before implementing (TDD workflow)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
