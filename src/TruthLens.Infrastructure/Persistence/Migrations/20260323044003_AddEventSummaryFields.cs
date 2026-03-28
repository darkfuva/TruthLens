using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TruthLens.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSummaryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SummarizedAtUtc",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryModel",
                table: "events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummarizedAtUtc",
                table: "events");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "events");

            migrationBuilder.DropColumn(
                name: "SummaryModel",
                table: "events");
        }
    }
}
