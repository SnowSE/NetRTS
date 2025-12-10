# Phase 0: Research & Technical Decisions

**Feature**: RTS Game Engine for Programming Competition
**Date**: 2025-12-09
**Status**: Complete

## Overview

This document captures research findings and technical decisions for implementing the RTS game engine using C# with .NET 10, following the latest design patterns and ensuring compliance with project constitution.

## 1. Game Tick Processing Architecture

### Decision: Background Service with Timer

**Chosen Approach**: `IHostedService` with `PeriodicTimer` for tick processing

**Rationale**:
- .NET 10's `PeriodicTimer` provides precise interval timing without drift
- `BackgroundService` base class handles graceful shutdown
- Async/await pattern prevents thread blocking
- Supports multiple concurrent matches efficiently
- Integrates with .NET Aspire for health monitoring

**Implementation Pattern**:
```csharp
public class GameTickService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessAllActiveMatches(stoppingToken);
        }
    }
}
```

**Alternatives Considered**:
- **Quartz.NET**: Overkill for simple interval-based processing; adds complexity
- **Timer class**: Less modern, prone to drift, harder to test
- **Channel-based processing**: Good for queue management but unnecessary for fixed intervals

## 2. Fog of War Calculation

### Decision: Spatial Indexing with QuadTree

**Chosen Approach**: QuadTree spatial data structure for efficient vision queries

**Rationale**:
- O(log n) lookup time for entities in vision range vs O(n) for naive iteration
- Divide-and-conquer spatial partitioning reduces checks from thousands to dozens
- Map limited to 200x200 tiles (40k cells) fits efficiently in memory
- Supports dynamic updates as units move
- Vision range queries (circular area) map naturally to quadrant searches

**Implementation Strategy**:
- QuadTree rebuilt per tick (cheap with 40k cells)
- Query returns all entities within bounding box, then filter by circular distance
- Separate trees per match (isolation)

**Performance Math**:
- Worst case: 200 units × 200 enemy entities = 40k comparisons (naive)
- QuadTree: 200 units × ~10 comparisons average = 2k comparisons
- 95% reduction in vision calculation overhead

**Alternatives Considered**:
- **R-Tree**: More complex, overkill for 2D grid
- **Grid-based chunking**: Simpler but less efficient for circular vision ranges
- **Bitmap masking**: Fast but memory-intensive and inflexible

## 3. Command Queue Management

### Decision: In-Memory Queue with Persistent Snapshots

**Chosen Approach**: `ConcurrentQueue<T>` per player with periodic database snapshots

**Rationale**:
- Lock-free concurrent operations for command submission
- FIFO ordering guaranteed by `ConcurrentQueue`
- Configurable limits (500 max queue size, 100 processed per tick) enforced at enqueue time
- Snapshots every 10 ticks provide crash recovery without per-command persistence overhead
- 99% of matches complete without crashes; snapshots are insurance, not primary path

**Queue Processing Flow**:
1. Player submits commands via REST API → enqueued to `ConcurrentQueue`
2. Tick processor dequeues up to 100 commands per player
3. Commands validated (ownership, bounds) before execution
4. Executed commands update in-memory game state
5. Every 10 ticks, snapshot queue + state to database

**Crash Recovery**:
- Reload last snapshot from database
- Replay ticks from snapshot to current tick
- Lost ticks: Max 10 seconds of game time (acceptable for 30-minute matches)

**Alternatives Considered**:
- **Database-backed queue**: Too slow; persisting 100 commands/tick/match = 1000s of writes/sec
- **Redis queue**: Adds external dependency; in-memory sufficient with snapshots
- **Event sourcing**: Correct pattern but over-engineered; adds complexity without clear benefit

## 4. State Persistence Strategy

### Decision: Hybrid In-Memory + Periodic Snapshots

**Chosen Approach**: Active match state in memory; snapshots to PostgreSQL every 10 ticks; completed matches fully persisted

**Rationale**:
- Tick processing requires sub-second state access (100ms per tick)
- Database round-trips add 10-50ms latency per query (unacceptable)
- Memory footprint: ~5MB per active match × 100 matches = 500MB (reasonable)
- Snapshots provide durability without sacrificing performance
- Completed matches archived to database for leaderboard/history queries

**Entity Framework Core Usage**:
- **Write**: Snapshot serialization every 10 ticks (async, non-blocking)
- **Read**: Match initialization, leaderboard queries, match history
- **Not Used For**: Tick-by-tick state updates (too slow)

**Snapshot Format**:
```csharp
public class MatchSnapshot
{
    public Guid MatchId { get; set; }
    public int TickNumber { get; set; }
    public byte[] SerializedState { get; set; } // MessagePack binary
    public DateTime Timestamp { get; set; }
}
```

**Alternatives Considered**:
- **Pure in-memory**: Data loss on crash; unacceptable
- **Full database persistence**: Too slow for 1-second ticks
- **Write-ahead log**: Correct but complex; snapshots simpler for this use case

## 5. API Design Pattern

### Decision: ASP.NET Core Minimal APIs with CQRS (MediatR)

**Chosen Approach**: Route-based Minimal APIs calling MediatR handlers

**Rationale**:
- Minimal APIs reduce boilerplate vs Controllers (aligns with code quality constitution)
- MediatR implements CQRS: Commands (mutations) vs Queries (reads)
- FluentValidation pipelines integrate with MediatR
- Vertical slice architecture (feature folders) improves maintainability
- OpenAPI/Swagger generation automatic with minimal effort

**Endpoint Structure**:
```csharp
app.MapPost("/api/v1/matches/{matchId}/commands",
    async (Guid matchId, QueueCommandRequest request, IMediator mediator) =>
{
    var command = new QueueUnitCommandCommand(matchId, request);
    var result = await mediator.Send(command);
    return Results.Ok(result);
})
.WithName("QueueCommand")
.WithOpenApi();
```

**CQRS Benefits**:
- **Commands**: `QueueUnitCommand`, `CreateMatch`, `StartMatch` (mutate state)
- **Queries**: `GetGameState`, `GetLeaderboard`, `GetLobbyList` (read-only)
- Separate optimization paths (e.g., read replicas for queries)
- Clear separation of concerns

**Alternatives Considered**:
- **MVC Controllers**: More boilerplate; Minimal APIs preferred for REST APIs
- **GraphQL**: Overkill; REST sufficient for bot clients
- **gRPC**: Better performance but HTTP/2 complexity; REST more accessible for competitors

## 6. Real-Time Updates for Web Client

### Decision: SignalR with Broadcast Groups

**Chosen Approach**: SignalR Core for server-to-client push; match-based groups

**Rationale**:
- Web client needs live game state updates (polling = poor UX + server load)
- SignalR abstracts WebSocket/SSE/long-polling fallbacks
- Group-based broadcasting: One `Clients.Group(matchId).SendAsync()` notifies all spectators/players
- Integrates with .NET Aspire for connection monitoring
- Supports reconnection with automatic retry

**Broadcast Pattern**:
```csharp
// After each tick
await hubContext.Clients
    .Group(matchId.ToString())
    .SendAsync("GameStateUpdated", gameStateDto);
```

**Client Connection**:
- Bots: REST API only (no SignalR needed)
- Web client: SignalR hub connection on lobby/game page
- Automatic subscription to match group on join

**Alternatives Considered**:
- **Server-Sent Events (SSE)**: One-way only; SignalR supports bi-directional
- **Polling**: High latency, server load, poor UX
- **WebSocket directly**: SignalR provides abstraction + fallbacks

## 7. Testing Strategy

### Decision: Multi-Layer Testing with Reqnroll BDD

**Chosen Approach**:
- **Unit Tests**: xUnit + NSubstitute for domain/application logic
- **Integration Tests**: Testcontainers for database tests
- **Contract Tests**: Reqnroll BDD for acceptance criteria from spec
- **E2E Tests**: Full match simulation

**Reqnroll for BDD**:
- Gherkin feature files map directly to spec acceptance scenarios
- Example from spec:
  ```gherkin
  Feature: Game State Retrieval

  Scenario: Player retrieves game state with fog of war
    Given a new match has started with two players
    And player 1 has units at position (10,10) with vision range 5
    When player 1 requests current game state
    Then the response includes units within 5 tiles
    And enemy units outside vision range are not included
  ```

**Test Coverage Targets** (per constitution):
- 80% overall code coverage minimum
- 95% for critical paths:
  - Game tick processor
  - Command validation
  - Fog of war calculation
  - Scoring logic

**Mocking with NSubstitute**:
```csharp
var mockRepository = Substitute.For<IMatchRepository>();
mockRepository.GetByIdAsync(matchId).Returns(match);
```

**Alternatives Considered**:
- **Moq**: Popular but NSubstitute has cleaner syntax for C# 12
- **SpecFlow**: Reqnroll is the open-source successor (SpecFlow went commercial)

## 8. Observability with .NET Aspire

### Decision: Full .NET Aspire Integration

**Chosen Approach**: Aspire AppHost for orchestration + ServiceDefaults for telemetry

**Aspire Components**:
- **Dashboard**: Local development UI (http://localhost:15888)
  - Logs aggregation across services
  - Distributed tracing visualization
  - Metrics and health checks
  - Resource management (containers, projects)

- **Service Discovery**: Automatic configuration of service URLs
  - API discovers PostgreSQL connection string
  - Client discovers API endpoint

- **Telemetry**: OpenTelemetry auto-instrumentation
  - HTTP request tracing
  - Database query tracing
  - Custom activity spans for tick processing

**Structured Logging with Serilog**:
```csharp
Log.Information("Processing tick {TickNumber} for match {MatchId}",
    tickNumber, matchId);
```

**Production Monitoring**:
- Application Insights for Azure deployments
- OpenTelemetry exporters for Prometheus/Grafana
- Health check endpoints for Kubernetes liveness/readiness probes

**Alternatives Considered**:
- **Manual instrumentation**: Aspire provides out-of-box setup
- **Custom dashboard**: Aspire dashboard superior for local dev

## 9. Client Technology Selection

### Decision: Blazor WebAssembly

**Chosen Approach**: Blazor WASM with SignalR client integration

**Rationale**:
- **C# code sharing**: Reuse `NetRts.Contracts` DTOs between API and client
- **Type safety**: Compile-time checking vs JavaScript runtime errors
- **Tooling**: Full Visual Studio/Rider support
- **.NET ecosystem**: Same dependency injection, testing patterns as API
- **SignalR client**: Native C# SignalR client library
- **Performance**: WASM suitable for game rendering; not AAA graphics

**Component Structure**:
```razor
@page "/game/{MatchId:guid}"
@inject GameApiClient ApiClient
@inject GameHubClient HubClient

<MapRenderer Units="@gameState.Units" />
<CommandPanel OnCommandSubmit="@SubmitCommand" />
```

**Alternatives Considered**:
- **React/TypeScript**: Popular but loses C# type sharing; adds build complexity
- **Blazor Server**: Lower client requirements but requires persistent connection
- **Angular/Vue**: Similar to React; no compelling advantage for this use case

## 10. Authentication & Authorization

### Decision: JWT with Match-Scoped Authorization

**Chosen Approach**: JWT tokens for player identity + match participation claims

**Token Structure**:
```json
{
  "sub": "player-uuid",
  "name": "BotName",
  "matches": ["match-1-uuid", "match-2-uuid"],
  "exp": 1234567890
}
```

**Authorization Flow**:
1. Player authenticates → receives JWT
2. Player joins match → match ID added to token claims (or separate match token)
3. API validates JWT + match membership on command submission
4. Prevents commanding units in matches player hasn't joined

**Security Benefits**:
- Stateless authentication (no session store needed)
- Match isolation enforced at API level
- Token expiration limits replay attacks

**Alternatives Considered**:
- **Session cookies**: Stateful; complicates horizontal scaling
- **API keys**: Simpler but lacks expiration, granular claims
- **OAuth2**: Overkill for competition context; JWT sufficient

## 11. Configuration Management

### Decision: Aspire Configuration + Azure App Configuration

**Chosen Approach**:
- **Local dev**: appsettings.json + Aspire orchestration
- **Production**: Azure App Configuration with feature flags

**Configurable Values** (per spec):
- Tick interval (default: 1 second)
- Max queue size per player (default: 500)
- Commands processed per tick (default: 100)
- Map dimensions (default: 100x100)
- Match duration limit (default: 1800 ticks)

**Feature Flags**:
- Enable/disable upgrades system
- Adjust unit balance (health, damage, cost)
- A/B testing different scoring algorithms

**Configuration Access**:
```csharp
services.AddOptions<GameSettings>()
    .BindConfiguration("GameSettings")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Alternatives Considered**:
- **Environment variables**: Works but harder to manage many settings
- **Database configuration**: Adds latency; config changes require restarts anyway

## 12. Deployment Architecture

### Decision: Container-Based with Kubernetes

**Chosen Approach**:
- Docker containers for API, Client (static files via Nginx)
- PostgreSQL managed service (Azure Database for PostgreSQL)
- Kubernetes for orchestration (Azure AKS or on-prem)

**Scaling Strategy**:
- **API pods**: Horizontal scaling (3+ replicas for HA)
- **Game Tick Service**: Single instance with leader election (prevent duplicate ticks)
- **Database**: Vertical scaling + read replicas for leaderboard queries
- **Client**: CDN for static assets

**Health Checks**:
```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddCheck<GameTickHealthCheck>("game-tick-processor");
```

**Alternatives Considered**:
- **Azure Container Apps**: Simpler but less control than AKS
- **VMs**: More management overhead vs containers
- **Serverless**: Poor fit for long-running tick processor

## Summary of Key Decisions

| Area | Decision | Rationale |
|------|----------|-----------|
| **Game Tick** | BackgroundService + PeriodicTimer | Precise timing, async, Aspire-integrated |
| **Fog of War** | QuadTree spatial indexing | O(log n) vs O(n), 95% perf improvement |
| **Command Queue** | ConcurrentQueue + snapshots | Lock-free, fast, durable |
| **State Storage** | Hybrid in-memory + PostgreSQL | Sub-second ticks, durability via snapshots |
| **API Pattern** | Minimal APIs + MediatR CQRS | Clean, maintainable, testable |
| **Real-Time** | SignalR with groups | Push updates, WebSocket abstraction |
| **Testing** | Reqnroll BDD + xUnit + NSubstitute | Spec alignment, comprehensive coverage |
| **Observability** | .NET Aspire + OpenTelemetry | Unified telemetry, local dev dashboard |
| **Client** | Blazor WebAssembly | C# code sharing, type safety, ecosystem |
| **Auth** | JWT with match claims | Stateless, secure, scalable |
| **Config** | Aspire + App Configuration | Feature flags, configurable game rules |
| **Deployment** | Kubernetes + containers | Scalable, cloud-agnostic, modern |

## Alignment with Constitution

All decisions prioritize:
- **Code Quality**: SOLID via DDD layers, DRY via MediatR/FluentValidation
- **Testing**: TDD-ready architecture, 95% coverage for critical paths
- **Security**: JWT auth, input validation, OWASP compliance
- **Performance**: Sub-second ticks, efficient algorithms, caching
- **Observability**: Aspire dashboard, structured logging, distributed tracing
- **Scalability**: Horizontal scaling, async processing, connection pooling

No constitutional violations identified. Ready to proceed to Phase 1 (Design & Contracts).
