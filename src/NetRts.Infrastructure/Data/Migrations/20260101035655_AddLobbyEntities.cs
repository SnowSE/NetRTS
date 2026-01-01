using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLobbyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LobbyPlayers_MatchLobbies_MatchLobbyId",
                table: "LobbyPlayers");

            migrationBuilder.DropIndex(
                name: "IX_LobbyPlayers_MatchLobbyId",
                table: "LobbyPlayers");

            migrationBuilder.DropColumn(
                name: "MatchLobbyId",
                table: "LobbyPlayers");

            migrationBuilder.AddForeignKey(
                name: "FK_LobbyPlayers_MatchLobbies_LobbyId",
                table: "LobbyPlayers",
                column: "LobbyId",
                principalTable: "MatchLobbies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LobbyPlayers_MatchLobbies_LobbyId",
                table: "LobbyPlayers");

            migrationBuilder.AddColumn<Guid>(
                name: "MatchLobbyId",
                table: "LobbyPlayers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LobbyPlayers_MatchLobbyId",
                table: "LobbyPlayers",
                column: "MatchLobbyId");

            migrationBuilder.AddForeignKey(
                name: "FK_LobbyPlayers_MatchLobbies_MatchLobbyId",
                table: "LobbyPlayers",
                column: "MatchLobbyId",
                principalTable: "MatchLobbies",
                principalColumn: "Id");
        }
    }
}
