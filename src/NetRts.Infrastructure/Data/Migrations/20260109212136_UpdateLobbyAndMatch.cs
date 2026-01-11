using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateLobbyAndMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Armor",
                table: "Units",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetResourceDepositId",
                table: "Units",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Armor",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "TargetResourceDepositId",
                table: "Units");
        }
    }
}
