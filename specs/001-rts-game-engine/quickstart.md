# Quickstart Guide: RTS Game Engine

**Audience**: Developers implementing the system or building bot clients
**Date**: 2025-12-09

## Overview

This guide provides step-by-step instructions for:
1. Setting up the development environment
2. Running the game server locally
3. Creating a simple bot client
4. Testing end-to-end gameplay

## Prerequisites

- **.NET 10 SDK** (or later)
- **Docker Desktop** (for PostgreSQL container)
- **Visual Studio 2024** or **Rider 2024.3** (recommended) or **VS Code with C# extension**
- **Git** for version control
- **curl** or **Postman** for API testing (optional)

## 1. Development Environment Setup

### Clone Repository

```bash
git clone https://github.com/your-org/NetRts.git
cd NetRts
```

### Install .NET Aspire Workload

```bash
dotnet workload install aspire
```

### Verify Prerequisites

```bash
dotnet --version  # Should show 10.0.0 or later
docker --version  # Should be running
```

## 2. Project Structure

```
src/
├── NetRts.AppHost/          # Aspire orchestration (run this)
├── NetRts.Api/              # REST API server
├── NetRts.Application/      # Business logic (CQRS handlers)
├── NetRts.Domain/           # Domain entities and rules
├── NetRts.Infrastructure/   # EF Core, repositories, background services
├── NetRts.Client/           # Blazor web client
└── NetRts.Contracts/        # Shared DTOs

tests/
├── NetRts.UnitTests/        # xUnit unit tests
├── NetRts.IntegrationTests/ # API integration tests
├── NetRts.ContractTests/    # Reqnroll BDD tests
└── NetRts.EndToEndTests/    # Full match simulation
```

## 3. Running the Server Locally

### Option A: Using .NET Aspire (Recommended)

.NET Aspire provides orchestration, service discovery, and a dashboard for local development.

```bash
cd src/NetRts.AppHost
dotnet run
```

**What Happens**:
- Aspire starts PostgreSQL in a Docker container
- Aspire runs the API project
- Aspire runs the Blazor client
- Aspire dashboard opens at http://localhost:15888

**Dashboard Features**:
- View logs from all services in one place
- See distributed traces across API and database
- Monitor resource health (API, database, client)
- View metrics (request counts, latency, etc.)

**Service URLs** (discovered automatically):
- API: http://localhost:5000 (example)
- Client: http://localhost:5001 (example)
- Aspire Dashboard: http://localhost:15888

### Option B: Manual Setup (Without Aspire)

If not using Aspire, start services individually:

**1. Start PostgreSQL**:
```bash
docker run --name netrts-db -e POSTGRES_PASSWORD=dev -p 5432:5432 -d postgres:16
```

**2. Run EF Core Migrations**:
```bash
cd src/NetRts.Infrastructure
dotnet ef database update --startup-project ../NetRts.Api
```

**3. Start API**:
```bash
cd src/NetRts.Api
dotnet run
```

**4. Start Client** (separate terminal):
```bash
cd src/NetRts.Client
dotnet run
```

## 4. Verify Server is Running

### Check Health Endpoint

```bash
curl http://localhost:5000/health
```

Expected response:
```json
{
  "status": "Healthy",
  "checks": {
    "database": "Healthy",
    "game-tick-processor": "Healthy"
  }
}
```

### Check API Documentation

Open Swagger UI: http://localhost:5000/swagger

You should see all API endpoints documented with request/response schemas.

## 5. Create a Player Account

### Register a Bot Player

```bash
curl -X POST http://localhost:5000/api/v1/players \
  -H "Content-Type: application/json" \
  -d '{
    "username": "TestBot1",
    "email": "bot1@example.com",
    "isBot": true
  }'
```

**Response**:
```json
{
  "playerId": "uuid-here",
  "username": "TestBot1",
  "token": "jwt-token-here",
  "createdAt": "2025-12-09T12:00:00Z"
}
```

**Save the token** for authentication in subsequent requests.

### Register a Second Player (for testing)

```bash
curl -X POST http://localhost:5000/api/v1/players \
  -H "Content-Type: application/json" \
  -d '{
    "username": "TestBot2",
    "email": "bot2@example.com",
    "isBot": true
  }'
```

## 6. Create a Match Lobby

### Player 1 Creates Lobby

```bash
curl -X POST http://localhost:5000/api/v1/lobbies \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <player1-token>" \
  -d '{
    "name": "Test Match",
    "gameSettings": {
      "mapWidth": 100,
      "mapHeight": 100,
      "maxTicks": 1800,
      "tickIntervalMs": 1000,
      "commandQueueSize": 500,
      "commandsPerTick": 100,
      "startingResources": 500
    }
  }'
```

**Response**:
```json
{
  "lobbyId": "lobby-uuid",
  "name": "Test Match",
  "hostPlayerId": "player1-uuid",
  "status": "Open",
  "currentPlayerCount": 1,
  "maxPlayers": 2
}
```

### Player 2 Joins Lobby

```bash
curl -X POST http://localhost:5000/api/v1/lobbies/{lobby-uuid}/join \
  -H "Authorization: Bearer <player2-token>"
```

### Player 1 Starts Match

```bash
curl -X POST http://localhost:5000/api/v1/lobbies/{lobby-uuid}/start \
  -H "Authorization: Bearer <player1-token>"
```

**Response**:
```json
{
  "matchId": "match-uuid",
  "message": "Match started successfully"
}
```

## 7. Retrieve Game State

### Get Current Game State

```bash
curl http://localhost:5000/api/v1/matches/{match-uuid}/state \
  -H "Authorization: Bearer <player1-token>"
```

**Response** (truncated for brevity):
```json
{
  "matchId": "match-uuid",
  "currentTick": 0,
  "playerId": "player1-uuid",
  "resources": 500,
  "commandQueueSize": 0,
  "units": [
    {
      "unitId": 1,
      "type": "Worker",
      "position": { "x": 10, "y": 10 },
      "healthPoints": 50,
      "maxHealthPoints": 50,
      "status": "Idle"
    }
  ],
  "buildings": [
    {
      "buildingId": 1,
      "type": "CommandCenter",
      "position": { "x": 5, "y": 5 },
      "healthPoints": 500,
      "isOperational": true
    }
  ],
  "resourceDeposits": [
    {
      "depositId": 1,
      "position": { "x": 15, "y": 10 },
      "resourceType": "Ore",
      "remainingCapacity": 5000
    }
  ]
}
```

## 8. Queue Commands

### Move Units

```bash
curl -X POST http://localhost:5000/api/v1/matches/{match-uuid}/commands \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <player1-token>" \
  -d '{
    "commands": [
      {
        "type": "Move",
        "unitIds": [1, 2, 3],
        "targetPosition": { "x": 20, "y": 20 }
      }
    ]
  }'
```

### Gather Resources

```bash
curl -X POST http://localhost:5000/api/v1/matches/{match-uuid}/commands \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <player1-token>" \
  -d '{
    "commands": [
      {
        "type": "Gather",
        "unitIds": [1, 2],
        "resourceDepositId": 1
      }
    ]
  }'
```

### Build a Barracks

```bash
curl -X POST http://localhost:5000/api/v1/matches/{match-uuid}/commands \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <player1-token>" \
  -d '{
    "commands": [
      {
        "type": "Build",
        "unitIds": [1, 2, 3],
        "buildingType": "Barracks",
        "targetPosition": { "x": 25, "y": 25 }
      }
    ]
  }'
```

### Produce a Soldier

```bash
curl -X POST http://localhost:5000/api/v1/matches/{match-uuid}/commands \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <player1-token>" \
  -d '{
    "commands": [
      {
        "type": "Produce",
        "buildingId": 2,
        "unitType": "Soldier"
      }
    ]
  }'
```

## 9. Building a Simple Bot Client

### C# Example (Minimal Bot)

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

public class SimpleBot
{
    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly Guid _matchId;

    public SimpleBot(string apiBaseUrl, string token, Guid matchId)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        _token = token;
        _matchId = matchId;
    }

    public async Task<GameStateDto> GetGameStateAsync()
    {
        var response = await _httpClient.GetFromJsonAsync<GameStateDto>(
            $"/api/v1/matches/{_matchId}/state");
        return response;
    }

    public async Task QueueCommandAsync(CommandDto command)
    {
        var request = new { commands = new[] { command } };
        await _httpClient.PostAsJsonAsync(
            $"/api/v1/matches/{_matchId}/commands", request);
    }

    public async Task RunBotLogicAsync()
    {
        while (true)
        {
            var state = await GetGameStateAsync();

            // Simple strategy: Send idle workers to gather resources
            var idleWorkers = state.Units
                .Where(u => u.Type == "Worker" && u.Status == "Idle")
                .ToList();

            if (idleWorkers.Any() && state.ResourceDeposits.Any())
            {
                var deposit = state.ResourceDeposits.First();
                await QueueCommandAsync(new CommandDto
                {
                    Type = "Gather",
                    UnitIds = idleWorkers.Select(w => w.UnitId).ToList(),
                    ResourceDepositId = deposit.DepositId
                });
            }

            // Wait for next tick
            await Task.Delay(1000);
        }
    }
}
```

### Python Example (Using requests library)

```python
import requests
import time

class SimpleBot:
    def __init__(self, api_base_url, token, match_id):
        self.api_base_url = api_base_url
        self.headers = {"Authorization": f"Bearer {token}"}
        self.match_id = match_id

    def get_game_state(self):
        response = requests.get(
            f"{self.api_base_url}/api/v1/matches/{self.match_id}/state",
            headers=self.headers
        )
        return response.json()

    def queue_command(self, command):
        requests.post(
            f"{self.api_base_url}/api/v1/matches/{self.match_id}/commands",
            headers=self.headers,
            json={"commands": [command]}
        )

    def run_bot_logic(self):
        while True:
            state = self.get_game_state()

            # Simple strategy: Send idle workers to gather
            idle_workers = [u for u in state["units"]
                          if u["type"] == "Worker" and u["status"] == "Idle"]

            if idle_workers and state["resourceDeposits"]:
                deposit = state["resourceDeposits"][0]
                self.queue_command({
                    "type": "Gather",
                    "unitIds": [u["unitId"] for u in idle_workers],
                    "resourceDepositId": deposit["depositId"]
                })

            time.sleep(1)  # Wait for next tick

# Usage
bot = SimpleBot("http://localhost:5000", "your-token", "your-match-id")
bot.run_bot_logic()
```

## 10. Using the Web Client

### Access the UI

Open browser to: http://localhost:5001

### Features

1. **Lobby List**: Browse and join open lobbies
2. **Create Lobby**: Configure match settings and create a new lobby
3. **Game View**: Watch match progress in real-time
   - Map visualization with units and buildings
   - Command panel for issuing orders (if playing manually)
   - Resource and score displays
   - SignalR updates every tick (no polling needed)
4. **Leaderboard**: View player rankings and stats

### Manual Gameplay (Non-Programmer Mode)

1. Create lobby via UI
2. Wait for opponent to join (or join your own lobby with second account)
3. Start match
4. Use click-and-drag or point-and-click commands:
   - Select units
   - Right-click to move or attack
   - Click buildings to queue production
5. View live updates as game progresses

## 11. Running Tests

### Unit Tests

```bash
cd tests/NetRts.UnitTests
dotnet test
```

### Integration Tests

```bash
cd tests/NetRts.IntegrationTests
dotnet test
```
(Automatically starts Testcontainers PostgreSQL)

### BDD Contract Tests (Reqnroll)

```bash
cd tests/NetRts.ContractTests
dotnet test
```

View test results in Visual Studio Test Explorer or terminal output.

### Generate Coverage Report

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

## 12. Troubleshooting

### Problem: PostgreSQL connection error

**Solution**: Verify Docker is running and PostgreSQL container started
```bash
docker ps  # Should show postgres container
```

### Problem: Port 5000 already in use

**Solution**: Kill process on port 5000 or change API port in `appsettings.json`
```bash
lsof -i :5000  # Find process using port
kill -9 <pid>  # Kill process
```

### Problem: Aspire dashboard not opening

**Solution**: Manually navigate to http://localhost:15888

### Problem: Game tick not processing

**Solution**: Check Aspire dashboard logs for `GameTickService` errors. Verify background service is running.

### Problem: Fog of war showing all units

**Solution**: Check that vision range calculation is enabled. Verify QuadTree spatial index is built correctly.

## 13. Next Steps

### For Bot Developers

1. Study `contracts/api-endpoints.md` for full API reference
2. Implement more sophisticated strategies:
   - Scouting enemy base
   - Building production queues
   - Upgrading units
   - Coordinated attacks
3. Optimize command queuing for efficient tick usage
4. Test against other bots in tournament mode

### For System Developers

1. Review `data-model.md` for entity relationships
2. Explore `research.md` for architectural decisions
3. Implement missing features from roadmap
4. Write tests following TDD principles (see constitution)
5. Add custom metrics to Aspire dashboard

### For Testers

1. Run BDD tests from `NetRts.ContractTests/Features/`
2. Validate acceptance scenarios against spec
3. Test edge cases (queue full, simultaneous attacks, fog of war)
4. Performance test with 50+ concurrent matches

## 14. Useful Commands Cheat Sheet

```bash
# Start with Aspire
cd src/NetRts.AppHost && dotnet run

# Run all tests
dotnet test

# Generate EF Core migration
cd src/NetRts.Infrastructure
dotnet ef migrations add MigrationName --startup-project ../NetRts.Api

# Apply migration
dotnet ef database update --startup-project ../NetRts.Api

# Reset database
dotnet ef database drop --startup-project ../NetRts.Api
dotnet ef database update --startup-project ../NetRts.Api

# View Aspire dashboard
open http://localhost:15888

# View Swagger API docs
open http://localhost:5000/swagger

# Clean solution
dotnet clean

# Restore NuGet packages
dotnet restore
```

## 15. Configuration Reference

### appsettings.json (API Project)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=netrts;Username=postgres;Password=dev"
  },
  "GameSettings": {
    "DefaultMapWidth": 100,
    "DefaultMapHeight": 100,
    "DefaultMaxTicks": 1800,
    "DefaultTickIntervalMs": 1000,
    "DefaultCommandQueueSize": 500,
    "DefaultCommandsPerTick": 100,
    "DefaultStartingResources": 500,
    "SnapshotIntervalTicks": 10
  },
  "JwtSettings": {
    "SecretKey": "your-dev-secret-key-here-minimum-32-chars",
    "Issuer": "NetRts",
    "Audience": "NetRts-Clients",
    "ExpirationMinutes": 1440
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
    ]
  }
}
```

## Summary

You now have:
- ✅ Development environment configured
- ✅ Server running locally with Aspire
- ✅ PostgreSQL database initialized
- ✅ Understanding of API endpoints
- ✅ Example bot clients in C# and Python
- ✅ Web client for manual gameplay
- ✅ Test execution knowledge
- ✅ Troubleshooting guide

**Ready to build your bot and compete!** 🎮🤖
