using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectedResponseTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpectedResponse",
                table: "EventTypeDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RespondsToEventId",
                table: "Events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpectedResponseTrackers",
                columns: table => new
                {
                    RequestEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestEventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedResponseEventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SatisfiedByEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SatisfiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectedResponseTrackers", x => x.RequestEventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedResponseTrackers_DeadlineAt",
                table: "ExpectedResponseTrackers",
                column: "DeadlineAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpectedResponseTrackers");

            migrationBuilder.DropColumn(
                name: "ExpectedResponse",
                table: "EventTypeDefinitions");

            migrationBuilder.DropColumn(
                name: "RespondsToEventId",
                table: "Events");
        }
    }
}
