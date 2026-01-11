# NetRTS - Real-Time Strategy Game Engine for Programming Competitions

NetRTS is a specialized RTS game engine designed for programming competitions where players control their units through a REST API.

## Features

- **Tick-Based Engine**: Consistent 1-second game ticks.
- **REST API for Bots**: Complete control over units and buildings via simple JSON commands.
- **Fog of War**: Real-time vision calculations using QuadTree spatial indexing.
- **Resource Management**: Gather resources, build structures, and produce units.
- **Upgrades**: Enhance unit and building capabilities through research.
- **Real-Time UI**: Blazor-based web client for monitoring matches.
- **Distributed Observability**: Built with .NET Aspire for comprehensive logging and telemetry.

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for PostgreSQL)

### Running the Application

The easiest way to run the entire system is using .NET Aspire:

```bash
dotnet run --project src/NetRts.AppHost
```

This will start:
- **API**: The backend game engine.
- **Client**: The Blazor web dashboard.
- **Database**: PostgreSQL container for persistence.

### Accessing the Dashboard

Once started, the Aspire dashboard will be available at the URL shown in your console (usually `http://localhost:18888`). From there, you can access the API Swagger UI and the Web Client.

## Bot Development

Bots interact with the engine using standard HTTP requests.

### Basic Workflow

1. **Register/Login**: Get a JWT token.
2. **Join Lobby**: Find or create a match.
3. **Get Game State**: `GET /api/v1/matches/{matchId}/state`
4. **Queue Commands**: `POST /api/v1/matches/{matchId}/commands`

### Example Move Command

```json
{
  "commands": [
    {
      "commandType": "Move",
      "unitIds": [1, 2, 3],
      "targetPosition": { "x": 50, "y": 50 }
    }
  ]
}
```

## Architecture

- **Domain**: Pure domain logic, entities, and business rules.
- **Application**: CQRS handlers using MediatR, command validation with FluentValidation.
- **Infrastructure**: EF Core persistence, background game loop, spatial indexing.
- **API**: Minimal APIs for low-latency command queueing.
- **Client**: Blazor WebAssembly for real-time visualization.

## License

MIT
