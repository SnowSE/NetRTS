using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetRts.Server.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ReplayFrames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RulesVersion",
                table: "matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "match_frames",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RulesVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkTicks = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    LastTick = table.Column<int>(type: "integer", nullable: false),
                    Rebuilt = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Manifest = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_frames", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_match_frames_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_frame_chunks",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_frame_chunks", x => new { x.MatchId, x.Index });
                    table.ForeignKey(
                        name: "FK_match_frame_chunks_match_frames_MatchId",
                        column: x => x.MatchId,
                        principalTable: "match_frames",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_frame_chunks");

            migrationBuilder.DropTable(
                name: "match_frames");

            migrationBuilder.DropColumn(
                name: "RulesVersion",
                table: "matches");
        }
    }
}
