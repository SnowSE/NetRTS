using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetRts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CurrentTick = table.Column<int>(type: "integer", nullable: false),
                    MapWidth = table.Column<int>(type: "integer", nullable: false),
                    MapHeight = table.Column<int>(type: "integer", nullable: false),
                    MaxTicksPerMatch = table.Column<int>(type: "integer", nullable: false),
                    TickIntervalMs = table.Column<int>(type: "integer", nullable: false),
                    CommandQueueSizeLimit = table.Column<int>(type: "integer", nullable: false),
                    CommandsPerTick = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WinnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Player1Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Player2Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Player1_UnitsDestroyed = table.Column<int>(type: "integer", nullable: false),
                    Player1_BuildingsDestroyed = table.Column<int>(type: "integer", nullable: false),
                    Player1_ResourcesGathered = table.Column<int>(type: "integer", nullable: false),
                    Player1_UnitsRemaining = table.Column<int>(type: "integer", nullable: false),
                    Player1_BuildingsRemaining = table.Column<int>(type: "integer", nullable: false),
                    Player2_UnitsDestroyed = table.Column<int>(type: "integer", nullable: false),
                    Player2_BuildingsDestroyed = table.Column<int>(type: "integer", nullable: false),
                    Player2_ResourcesGathered = table.Column<int>(type: "integer", nullable: false),
                    Player2_UnitsRemaining = table.Column<int>(type: "integer", nullable: false),
                    Player2_BuildingsRemaining = table.Column<int>(type: "integer", nullable: false),
                    Player1Resources = table.Column<int>(type: "integer", nullable: false),
                    Player2Resources = table.Column<int>(type: "integer", nullable: false),
                    GameStateSnapshot = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchLobbies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HostPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    MaxPlayers = table.Column<int>(type: "integer", nullable: false),
                    MapWidth = table.Column<int>(type: "integer", nullable: false),
                    MapHeight = table.Column<int>(type: "integer", nullable: false),
                    MaxTicks = table.Column<int>(type: "integer", nullable: false),
                    TickIntervalMs = table.Column<int>(type: "integer", nullable: false),
                    CommandQueueSize = table.Column<int>(type: "integer", nullable: false),
                    CommandsPerTick = table.Column<int>(type: "integer", nullable: false),
                    StartingResources = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchLobbies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalMatches = table.Column<int>(type: "integer", nullable: false),
                    TotalWins = table.Column<int>(type: "integer", nullable: false),
                    CurrentElo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerScores",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalScore = table.Column<int>(type: "integer", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "integer", nullable: false),
                    MatchesWon = table.Column<int>(type: "integer", nullable: false),
                    WinRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    AverageScore = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerScores", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PositionX = table.Column<int>(type: "integer", nullable: false),
                    PositionY = table.Column<int>(type: "integer", nullable: false),
                    HealthPoints = table.Column<int>(type: "integer", nullable: false),
                    MaxHealthPoints = table.Column<int>(type: "integer", nullable: false),
                    ConstructionProgress = table.Column<int>(type: "integer", nullable: false),
                    IsOperational = table.Column<bool>(type: "boolean", nullable: false),
                    VisionRange = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => new { x.MatchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Buildings_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    TargetUnitIds = table.Column<string>(type: "text", nullable: false),
                    TargetBuildingId = table.Column<int>(type: "integer", nullable: true),
                    TargetPositionX = table.Column<int>(type: "integer", nullable: true),
                    TargetPositionY = table.Column<int>(type: "integer", nullable: true),
                    TargetEntityId = table.Column<int>(type: "integer", nullable: true),
                    TargetResourceDepositId = table.Column<int>(type: "integer", nullable: true),
                    BuildingType = table.Column<string>(type: "text", nullable: true),
                    UnitType = table.Column<string>(type: "text", nullable: true),
                    UpgradeType = table.Column<string>(type: "text", nullable: true),
                    SubmittedAtTick = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAtTick = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commands_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MapTiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionX = table.Column<int>(type: "integer", nullable: false),
                    PositionY = table.Column<int>(type: "integer", nullable: false),
                    TerrainType = table.Column<string>(type: "text", nullable: false),
                    OccupiedByUnitId = table.Column<int>(type: "integer", nullable: true),
                    OccupiedByBuildingId = table.Column<int>(type: "integer", nullable: true),
                    VisibleToPlayer1 = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleToPlayer2 = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapTiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapTiles_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceDeposits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionX = table.Column<int>(type: "integer", nullable: false),
                    PositionY = table.Column<int>(type: "integer", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RemainingCapacity = table.Column<int>(type: "integer", nullable: false),
                    InitialCapacity = table.Column<int>(type: "integer", nullable: false),
                    GatherRate = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceDeposits", x => new { x.MatchId, x.Id });
                    table.ForeignKey(
                        name: "FK_ResourceDeposits_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PositionX = table.Column<int>(type: "integer", nullable: false),
                    PositionY = table.Column<int>(type: "integer", nullable: false),
                    HealthPoints = table.Column<int>(type: "integer", nullable: false),
                    MaxHealthPoints = table.Column<int>(type: "integer", nullable: false),
                    AttackDamage = table.Column<int>(type: "integer", nullable: false),
                    AttackRange = table.Column<int>(type: "integer", nullable: false),
                    MovementSpeed = table.Column<int>(type: "integer", nullable: false),
                    VisionRange = table.Column<int>(type: "integer", nullable: false),
                    CurrentStatus = table.Column<string>(type: "text", nullable: false),
                    TargetPositionX = table.Column<int>(type: "integer", nullable: true),
                    TargetPositionY = table.Column<int>(type: "integer", nullable: true),
                    TargetEntityId = table.Column<int>(type: "integer", nullable: true),
                    ResourcesCarried = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => new { x.MatchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Units_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Upgrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ResearchProgress = table.Column<int>(type: "integer", nullable: false),
                    ResearchTicksRequired = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    ResourceCost = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Upgrades", x => new { x.MatchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Upgrades_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LobbyPlayers",
                columns: table => new
                {
                    LobbyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    MatchLobbyId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LobbyPlayers", x => new { x.LobbyId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_LobbyPlayers_MatchLobbies_MatchLobbyId",
                        column: x => x.MatchLobbyId,
                        principalTable: "MatchLobbies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_Match_Owner",
                table: "Buildings",
                columns: new[] { "MatchId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Commands_Match_Player_Status",
                table: "Commands",
                columns: new[] { "MatchId", "PlayerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LobbyPlayers_MatchLobbyId",
                table: "LobbyPlayers",
                column: "MatchLobbyId");

            migrationBuilder.CreateIndex(
                name: "IX_MapTiles_Match",
                table: "MapTiles",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_EndedAt",
                table: "Matches",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Players",
                table: "Matches",
                columns: new[] { "Player1Id", "Player2Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status",
                table: "Matches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Lobbies_Status",
                table: "MatchLobbies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Elo",
                table: "Players",
                column: "CurrentElo");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Email",
                table: "Players",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Username",
                table: "Players",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScores_Rank",
                table: "PlayerScores",
                column: "Rank");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScores_TotalScore",
                table: "PlayerScores",
                column: "TotalScore",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceDeposits_Match",
                table: "ResourceDeposits",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_Match_Owner",
                table: "Units",
                columns: new[] { "MatchId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Upgrades_Match_Owner",
                table: "Upgrades",
                columns: new[] { "MatchId", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropTable(
                name: "Commands");

            migrationBuilder.DropTable(
                name: "LobbyPlayers");

            migrationBuilder.DropTable(
                name: "MapTiles");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "PlayerScores");

            migrationBuilder.DropTable(
                name: "ResourceDeposits");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "Upgrades");

            migrationBuilder.DropTable(
                name: "MatchLobbies");

            migrationBuilder.DropTable(
                name: "Matches");
        }
    }
}
