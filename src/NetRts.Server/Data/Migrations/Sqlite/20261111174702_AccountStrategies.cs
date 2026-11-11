using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AccountStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_strategies",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProgramJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_strategies", x => new { x.AccountId, x.StrategyId });
                    table.ForeignKey(
                        name: "FK_account_strategies_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_strategies");
        }
    }
}
