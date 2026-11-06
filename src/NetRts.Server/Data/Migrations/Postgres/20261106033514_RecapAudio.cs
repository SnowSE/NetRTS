using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RecapAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audio",
                table: "recap_episodes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "AudioBytes",
                table: "recap_episodes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Audio",
                table: "recap_episodes");

            migrationBuilder.DropColumn(
                name: "AudioBytes",
                table: "recap_episodes");
        }
    }
}
