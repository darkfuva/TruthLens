using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalEvidenceAndEventStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAtUtc",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "provisional");

            migrationBuilder.CreateTable(
                name: "external_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "external_evidence_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RelevanceScore = table.Column<double>(type: "double precision", nullable: true),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_evidence_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_evidence_posts_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_evidence_posts_external_sources_ExternalSourceId",
                        column: x => x.ExternalSourceId,
                        principalTable: "external_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_Status",
                table: "events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_external_evidence_posts_DiscoveredAtUtc",
                table: "external_evidence_posts",
                column: "DiscoveredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_external_evidence_posts_EventId_Url",
                table: "external_evidence_posts",
                columns: new[] { "EventId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_evidence_posts_ExternalSourceId",
                table: "external_evidence_posts",
                column: "ExternalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_external_sources_Domain",
                table: "external_sources",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_evidence_posts");

            migrationBuilder.DropTable(
                name: "external_sources");

            migrationBuilder.DropIndex(
                name: "IX_events_Status",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "events");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "events");
        }
    }
}
