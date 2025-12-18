# Implementation Plan: RTS Game Engine for Programming Competition

**Branch**: `001-rts-game-engine` | **Date**: 2025-12-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-rts-game-engine/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Building a real-time strategy game engine for programming competitions where bots control units via REST API. The system features tick-based command execution, fog of war, resource management, building construction, unit production, upgrades, and scoring. Technical approach uses C# with .NET 10, ASP.NET Core Minimal APIs, in-memory game state with EF Core for persistence, background services for game tick processing, SignalR for real-time updates, .NET Aspire for observability, and a default web client for non-programmers.

## Technical Context

**Language/Version**: C# 12 with .NET 10 (LTS)
**Primary Dependencies**:
- ASP.NET Core 10.0 (Minimal APIs for REST endpoints)
- Entity Framework Core 10.0 (PostgreSQL provider for persistence)
- SignalR 10.0 (real-time game state updates for UI client)
- .NET Aspire 10.0 (distributed application orchestration, telemetry, logging)
- NSubstitute 5.x (mocking framework for unit tests)
- Reqnroll 2.x (BDD test framework, SpecFlow successor)
- FluentValidation 11.x (command validation)
- MediatR 13.x (CQRS pattern for commands/queries)

**Storage**: PostgreSQL 16+ (persistent storage for matches, players, leaderboard); in-memory state for active game ticks with periodic snapshots

**Testing**:
- xUnit 2.x (test runner)
- NSubstitute 5.x (mocks and stubs)
- Reqnroll 2.x (BDD-style acceptance tests)
- FluentAssertions 6.x (assertion library)
- Testcontainers 3.x (integration tests with real PostgreSQL)

**Target Platform**: Linux containers (Docker) deployed via Kubernetes or Azure Container Apps; .NET Aspire for local development orchestration

**Project Type**: Web application (backend API + frontend client)

**Performance Goals**:
- Game tick processing: 1-second intervals for all concurrent matches
- API response time: p95 < 100ms, p99 < 200ms for command queuing
- Game state queries: p95 < 150ms including fog of war calculation
- Concurrent matches: Support 50+ simultaneous matches without degradation
- Command throughput: Process 100 commands per player per tick (configurable)

**Constraints**:
- Game tick must complete within 900ms to maintain 1-second cadence (100ms buffer)
- Maximum 500 commands in queue per player (configurable)
- Map size limited to 200x200 tiles for performance
- Vision calculation must use spatial indexing for O(log n) lookups
- Match state snapshots every 10 ticks for crash recovery

**Scale/Scope**:
- Target: 100 concurrent matches (200 active players)
- Leaderboard tracking across all matches
- Match history retention: 30 days
- Average match duration: 30 minutes (1800 ticks)
- API endpoints: ~25-30 REST endpoints
- Default web client: Single-page application with lobby, game view, leaderboard

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Code Quality & Maintainability
- ✅ **SOLID Principles**: Domain-driven design with clear separation (Domain, Application, Infrastructure layers)
- ✅ **DRY**: MediatR handlers eliminate controller duplication; FluentValidation centralizes validation logic
- ✅ **Readability**: C# 12 features (primary constructors, collection expressions) enhance clarity
- ✅ **Code Reviews**: Required per constitution; enforced via PR process
- ✅ **Documentation**: XML comments for public APIs; architecture decision records (ADRs) for key choices

### II. Test-First Development (NON-NEGOTIABLE)
- ✅ **TDD Cycle**: Red-Green-Refactor enforced; tests written before implementation
- ✅ **Coverage Targets**: 80% minimum; 95% for game tick logic, command processing, scoring
- ✅ **Test Categories**:
  - **Unit Tests**: xUnit with NSubstitute for domain logic, handlers, services
  - **Integration Tests**: Testcontainers with real PostgreSQL for data persistence
  - **Contract Tests**: Reqnroll BDD tests validating API contracts against spec acceptance criteria
  - **End-to-End Tests**: Full match simulation from lobby creation to winner determination
- ✅ **Test Naming**: Reqnroll scenarios use Given-When-Then; xUnit uses descriptive method names

### III. Security by Design
- ✅ **Threat Modeling**: Match isolation (player can only command own units), command validation prevents injection
- ✅ **Input Validation**: FluentValidation on all API inputs; coordinate bounds checking; unit ownership verification
- ✅ **Authentication & Authorization**: JWT tokens for player authentication; match-specific authorization checks
- ✅ **Data Protection**: TLS 1.3 in transit; sensitive config in Azure Key Vault / Kubernetes Secrets via Aspire
- ✅ **Dependency Security**: Dependabot enabled; NuGet package scanning in CI pipeline
- ✅ **OWASP Top 10**: Input validation prevents injection; rate limiting prevents DoS; no sensitive data in logs

### IV. Performance & Scalability
- ✅ **Performance Budgets**: API < 200ms p95, tick processing < 900ms (defined in Technical Context)
- ✅ **Resource Efficiency**: Background services for async tick processing; spatial indexing for vision calc
- ✅ **Caching**: In-memory game state cache; Redis for distributed session state if needed
- ✅ **Database Optimization**: EF Core indexes on query columns; batch updates for tick processing; connection pooling
- ✅ **Monitoring & Alerting**: .NET Aspire dashboard; OpenTelemetry metrics; Application Insights integration

### V. User Experience Consistency
- ✅ **Design System**: Default web client uses modern component library (Blazor or React with design tokens)
- ✅ **Accessibility**: WCAG 2.1 AA compliance for web client; semantic HTML; keyboard navigation
- ✅ **Error Handling**: Problem Details (RFC 7807) for API errors; user-friendly messages in client
- ✅ **Loading States**: Real-time SignalR updates eliminate polling; skeleton loaders in UI

### VI. Observability & Debugging
- ✅ **Structured Logging**: Serilog with JSON formatting; correlation IDs via Activity.TraceId
- ✅ **Logging Standards**: No PII logged; ERROR for failures; INFO for business events; DEBUG for troubleshooting
- ✅ **Distributed Tracing**: OpenTelemetry tracing across API, game tick processor, database
- ✅ **Metrics & Dashboards**: .NET Aspire dashboard for local dev; Application Insights for production
- ✅ **Error Tracking**: Centralized exception handling middleware; error aggregation in Application Insights
- ✅ **Audit Trails**: Match events logged (creation, start, end, winner); player actions auditable

### VII. Versioning & Change Management
- ✅ **Semantic Versioning**: API versioned via URL path (/api/v1/); breaking changes increment major version
- ✅ **Deprecation Policy**: Deprecated endpoints supported for 1 minor version; warnings in responses
- ✅ **Database Migrations**: EF Core migrations; tested on staging before production; rollback scripts
- ✅ **Feature Flags**: Azure App Configuration or custom feature flags for gradual rollout
- ✅ **Changelog**: CHANGELOG.md maintained with user-facing changes

**GATE RESULT**: ✅ **PASSED** - All constitutional principles satisfied. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── NetRts.AppHost/                    # .NET Aspire orchestration project
│   ├── Program.cs                     # Aspire app composition
│   └── appsettings.json
├── NetRts.ServiceDefaults/            # Shared Aspire service defaults
│   ├── Extensions.cs                  # Telemetry, health checks, resilience
│   └── appsettings.json
├── NetRts.Api/                        # ASP.NET Core Web API
│   ├── Program.cs                     # Minimal API endpoint definitions
│   ├── Endpoints/                     # Endpoint groups (Lobby, Match, Command, etc.)
│   ├── Middleware/                    # Exception handling, auth, logging
│   ├── Filters/                       # Validation filters
│   └── appsettings.json
├── NetRts.Application/                # Application layer (CQRS handlers)
│   ├── Commands/                      # MediatR command handlers
│   │   ├── QueueUnitCommand/
│   │   ├── CreateMatch/
│   │   └── JoinLobby/
│   ├── Queries/                       # MediatR query handlers
│   │   ├── GetGameState/
│   │   ├── GetLeaderboard/
│   │   └── GetLobbyList/
│   ├── Services/                      # Application services
│   │   ├── IGameTickProcessor.cs
│   │   ├── IFogOfWarCalculator.cs
│   │   └── ICommandQueueManager.cs
│   └── Validators/                    # FluentValidation validators
├── NetRts.Domain/                     # Domain layer (entities, value objects)
│   ├── Entities/                      # Core domain entities
│   │   ├── Match.cs
│   │   ├── Player.cs
│   │   ├── Unit.cs
│   │   ├── Building.cs
│   │   ├── Command.cs
│   │   └── MapTile.cs
│   ├── ValueObjects/                  # Immutable value objects
│   │   ├── Position.cs
│   │   ├── UnitStats.cs
│   │   └── Score.cs
│   ├── Enums/                         # Domain enums
│   │   ├── UnitType.cs
│   │   ├── BuildingType.cs
│   │   ├── CommandType.cs
│   │   └── MatchStatus.cs
│   ├── Events/                        # Domain events
│   │   ├── UnitDestroyed.cs
│   │   ├── BuildingCompleted.cs
│   │   └── MatchEnded.cs
│   └── Interfaces/                    # Domain service interfaces
├── NetRts.Infrastructure/             # Infrastructure layer (persistence, external services)
│   ├── Data/                          # EF Core DbContext and configurations
│   │   ├── ApplicationDbContext.cs
│   │   ├── Configurations/            # Entity configurations
│   │   └── Migrations/                # EF Core migrations
│   ├── Repositories/                  # Repository implementations
│   │   ├── MatchRepository.cs
│   │   ├── PlayerRepository.cs
│   │   └── LeaderboardRepository.cs
│   ├── BackgroundServices/            # Hosted services
│   │   ├── GameTickService.cs         # Processes ticks for all active matches
│   │   └── MatchSnapshotService.cs    # Periodic state snapshots
│   ├── Caching/                       # In-memory caching
│   │   └── GameStateCache.cs
│   └── Spatial/                       # Spatial indexing for fog of war
│       └── QuadTree.cs                # Efficient vision calculation
├── NetRts.Client/                     # Default web client (Blazor WebAssembly or React)
│   ├── Pages/                         # UI pages
│   │   ├── Lobby.razor/.tsx           # Match lobby
│   │   ├── GameView.razor/.tsx        # Live game view
│   │   └── Leaderboard.razor/.tsx     # Player rankings
│   ├── Components/                    # Reusable components
│   │   ├── UnitCard.razor/.tsx
│   │   ├── MapRenderer.razor/.tsx
│   │   └── CommandPanel.razor/.tsx
│   ├── Services/                      # Client-side services
│   │   ├── GameApiClient.cs           # HTTP client for REST API
│   │   └── GameHubClient.cs           # SignalR hub client
│   └── wwwroot/                       # Static assets
└── NetRts.Contracts/                  # Shared contracts (DTOs, API models)
    ├── Requests/                      # API request models
    ├── Responses/                     # API response models
    └── Events/                        # SignalR event models

tests/
├── NetRts.UnitTests/                  # xUnit unit tests with NSubstitute
│   ├── Domain/                        # Domain logic tests
│   ├── Application/                   # Handler tests
│   └── Infrastructure/                # Repository tests
├── NetRts.IntegrationTests/           # Integration tests with Testcontainers
│   ├── Api/                           # API endpoint tests
│   └── Database/                      # EF Core integration tests
├── NetRts.ContractTests/              # Reqnroll BDD tests
│   ├── Features/                      # Gherkin feature files
│   │   ├── GameState.feature
│   │   ├── UnitCommands.feature
│   │   ├── BuildingProduction.feature
│   │   └── MatchScoring.feature
│   └── StepDefinitions/               # Step definition implementations
└── NetRts.EndToEndTests/              # Full system tests
    └── MatchSimulation/               # Complete match workflows
```

**Structure Decision**: Web application structure selected based on requirements for:
- Backend REST API for bot clients
- Default web client for non-programmers
- .NET Aspire for distributed app orchestration and observability
- Clean architecture with Domain/Application/Infrastructure separation aligns with SOLID principles and testability requirements

## Complexity Tracking

**No constitutional violations identified.** All design decisions align with project principles:
- Clean architecture (Domain/Application/Infrastructure) satisfies SOLID and maintainability requirements
- CQRS with MediatR eliminates controller duplication (DRY principle)
- TDD-ready with xUnit, NSubstitute, Reqnroll achieving 95% coverage targets
- .NET Aspire provides comprehensive observability without custom infrastructure
- JWT authentication with match-scoped authorization ensures security by design
- Background service tick processing meets performance budgets (<900ms per tick)
