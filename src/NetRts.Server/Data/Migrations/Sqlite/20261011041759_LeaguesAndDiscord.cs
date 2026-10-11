using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class LeaguesAndDiscord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_players_Rating",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Draws",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Losses",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Wins",
                table: "players");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerAccountId",
                table: "players",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "matches",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Practice");

            migrationBuilder.AddColumn<string>(
                name: "Season",
                table: "matches",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscordId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GlobalName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ServerNickname = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AvatarHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InServer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSignInAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "player_stats",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Season = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    Draws = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_stats", x => new { x.PlayerId, x.Category, x.Season });
                    table.ForeignKey(
                        name: "FK_player_stats_players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_players_OwnerAccountId",
                table: "players",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_Category_CompletedAt",
                table: "matches",
                columns: new[] { "Category", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_DiscordId",
                table: "accounts",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_stats_Category_Season_Points",
                table: "player_stats",
                columns: new[] { "Category", "Season", "Points" });

            migrationBuilder.CreateIndex(
                name: "IX_player_stats_Category_Season_Rating",
                table: "player_stats",
                columns: new[] { "Category", "Season", "Rating" });

            migrationBuilder.AddForeignKey(
                name: "FK_players_accounts_OwnerAccountId",
                table: "players",
                column: "OwnerAccountId",
                principalTable: "accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_accounts_OwnerAccountId",
                table: "players");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "player_stats");

            migrationBuilder.DropIndex(
                name: "IX_players_OwnerAccountId",
                table: "players");

            migrationBuilder.DropIndex(
                name: "IX_matches_Category_CompletedAt",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "OwnerAccountId",
                table: "players");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "matches");

            migrationBuilder.AddColumn<int>(
                name: "Draws",
                table: "players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Losses",
                table: "players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Wins",
                table: "players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_players_Rating",
                table: "players",
                column: "Rating");
        }
    }
}
