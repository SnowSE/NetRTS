using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetRts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionQueueAndUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResearchTicksRequired",
                table: "Upgrades");

            migrationBuilder.RenameColumn(
                name: "ResourceCost",
                table: "Upgrades",
                newName: "StartedAtTick");

            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "Upgrades",
                newName: "PlayerId");

            migrationBuilder.RenameColumn(
                name: "IsCompleted",
                table: "Upgrades",
                newName: "IsComplete");

            migrationBuilder.RenameIndex(
                name: "IX_Upgrades_Match_Owner",
                table: "Upgrades",
                newName: "IX_Upgrades_Match_Player");

            migrationBuilder.CreateTable(
                name: "ProductionOrder",
                columns: table => new
                {
                    BuildingMatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitType = table.Column<string>(type: "text", nullable: false),
                    TicksRemaining = table.Column<int>(type: "integer", nullable: false),
                    TotalTicks = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrder", x => new { x.BuildingMatchId, x.BuildingId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_Buildings_BuildingMatchId_BuildingId",
                        columns: x => new { x.BuildingMatchId, x.BuildingId },
                        principalTable: "Buildings",
                        principalColumns: new[] { "MatchId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionOrder");

            migrationBuilder.RenameColumn(
                name: "StartedAtTick",
                table: "Upgrades",
                newName: "ResourceCost");

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "Upgrades",
                newName: "OwnerId");

            migrationBuilder.RenameColumn(
                name: "IsComplete",
                table: "Upgrades",
                newName: "IsCompleted");

            migrationBuilder.RenameIndex(
                name: "IX_Upgrades_Match_Player",
                table: "Upgrades",
                newName: "IX_Upgrades_Match_Owner");

            migrationBuilder.AddColumn<int>(
                name: "ResearchTicksRequired",
                table: "Upgrades",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
