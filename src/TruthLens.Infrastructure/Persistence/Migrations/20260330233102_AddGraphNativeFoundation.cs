using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGraphNativeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_relations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_relations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_relations_events_FromEventId",
                        column: x => x.FromEventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_relations_events_ToEventId",
                        column: x => x.ToEventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extracted_event_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    TimeHint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LocationHint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Actors = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    ExtractionConfidence = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extracted_event_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_extracted_event_candidates_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_event_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelevanceScore = table.Column<double>(type: "double precision", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_event_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_post_event_links_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_post_event_links_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_relations_FromEventId_ToEventId_RelationType",
                table: "event_relations",
                columns: new[] { "FromEventId", "ToEventId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_relations_FromEventId_UpdatedAtUtc",
                table: "event_relations",
                columns: new[] { "FromEventId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_event_relations_ToEventId_UpdatedAtUtc",
                table: "event_relations",
                columns: new[] { "ToEventId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_extracted_event_candidates_CreatedAtUtc",
                table: "extracted_event_candidates",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_extracted_event_candidates_PostId_Status_CreatedAtUtc",
                table: "extracted_event_candidates",
                columns: new[] { "PostId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_post_event_links_EventId",
                table: "post_event_links",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_post_event_links_EventId_IsPrimary",
                table: "post_event_links",
                columns: new[] { "EventId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_post_event_links_LinkedAtUtc",
                table: "post_event_links",
                column: "LinkedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_post_event_links_PostId_EventId",
                table: "post_event_links",
                columns: new[] { "PostId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_relations");

            migrationBuilder.DropTable(
                name: "extracted_event_candidates");

            migrationBuilder.DropTable(
                name: "post_event_links");
        }
    }
}
