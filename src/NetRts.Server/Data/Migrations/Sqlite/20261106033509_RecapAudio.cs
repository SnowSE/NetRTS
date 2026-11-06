using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Sqlite
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
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "AudioBytes",
                table: "recap_episodes",
                type: "INTEGER",
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
