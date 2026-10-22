using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AchievementsAndRecaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_achievements",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_achievements", x => new { x.PlayerId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_bot_achievements_players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ladder_snapshots",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ladder_snapshots", x => new { x.Date, x.Category, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_ladder_snapshots_players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_players",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsHouseBot = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Result = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Place = table.Column<int>(type: "integer", nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false),
                    Ticks = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Gathered = table.Column<int>(type: "integer", nullable: false),
                    UnitsKilled = table.Column<int>(type: "integer", nullable: false),
                    UnitsLost = table.Column<int>(type: "integer", nullable: false),
                    BuildingsDestroyed = table.Column<int>(type: "integer", nullable: false),
                    Surrendered = table.Column<bool>(type: "boolean", nullable: false),
                    RealOpponents = table.Column<int>(type: "integer", nullable: false),
                    RatingBefore = table.Column<int>(type: "integer", nullable: true),
                    RatingAfter = table.Column<int>(type: "integer", nullable: true),
                    Strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_players", x => new { x.MatchId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_match_players_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_match_players_players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recap_episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Video = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Thumbnail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recap_episodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_achievements_AchievementId_EarnedAt",
                table: "bot_achievements",
                columns: new[] { "AchievementId", "EarnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bot_achievements_EarnedAt",
                table: "bot_achievements",
                column: "EarnedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ladder_snapshots_PlayerId",
                table: "ladder_snapshots",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_match_players_CompletedAt",
                table: "match_players",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_match_players_PlayerId_CompletedAt",
                table: "match_players",
                columns: new[] { "PlayerId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recap_episodes_Date",
                table: "recap_episodes",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_achievements");

            migrationBuilder.DropTable(
                name: "ladder_snapshots");

            migrationBuilder.DropTable(
                name: "match_players");

            migrationBuilder.DropTable(
                name: "recap_episodes");
        }
    }
}
