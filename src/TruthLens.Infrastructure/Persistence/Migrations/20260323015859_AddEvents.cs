using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CentroidEmbedding = table.Column<float[]>(type: "real[]", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_posts_EventId",
                table: "posts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_events_LastSeenAtUtc",
                table: "events",
                column: "LastSeenAtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_posts_events_EventId",
                table: "posts",
                column: "EventId",
                principalTable: "events",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_posts_events_EventId",
                table: "posts");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropIndex(
                name: "IX_posts_EventId",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "posts");
        }
    }
}
