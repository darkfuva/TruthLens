using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendedSourcesPhaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfidenceModelVersion",
                table: "sources",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ConfidenceScore",
                table: "sources",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfidenceUpdatedAtUtc",
                table: "sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recommended_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Domain = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FeedUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DiscoveryMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    SamplePostCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommended_sources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recommended_sources_FeedUrl",
                table: "recommended_sources",
                column: "FeedUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recommended_sources_Status",
                table: "recommended_sources",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommended_sources");

            migrationBuilder.DropColumn(
                name: "ConfidenceModelVersion",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "ConfidenceScore",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "ConfidenceUpdatedAtUtc",
                table: "sources");
        }
    }
}
