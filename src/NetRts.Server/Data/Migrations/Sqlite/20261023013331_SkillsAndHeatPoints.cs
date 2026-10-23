using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SkillsAndHeatPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows get sigma 0, which marks them for SkillBackfill (run at startup) to replay.
            migrationBuilder.AddColumn<double>(
                name: "Mu",
                table: "player_stats",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Sigma",
                table: "player_stats",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "LeagueMatchId",
                table: "match_players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Points",
                table: "match_players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Skill",
                table: "ladder_snapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_match_players_LeagueMatchId_PlayerId",
                table: "match_players",
                columns: new[] { "LeagueMatchId", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_players_LeagueMatchId_PlayerId",
                table: "match_players");

            migrationBuilder.DropColumn(
                name: "Mu",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "Sigma",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "LeagueMatchId",
                table: "match_players");

            migrationBuilder.DropColumn(
                name: "Points",
                table: "match_players");

            migrationBuilder.DropColumn(
                name: "Skill",
                table: "ladder_snapshots");
        }
    }
}
