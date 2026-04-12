using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyPostEventColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_posts_events_EventId",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "IX_posts_EventId",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "ClusterAssignmentScore",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "posts");

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "UX_post_event_links_one_primary_per_post"
                ON public.post_event_links ("PostId")
                WHERE "IsPrimary" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_post_event_links_one_primary_per_post",
                table: "post_event_links");

            migrationBuilder.AddColumn<double>(
                name: "ClusterAssignmentScore",
                table: "posts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_posts_EventId",
                table: "posts",
                column: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_posts_events_EventId",
                table: "posts",
                column: "EventId",
                principalTable: "events",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
