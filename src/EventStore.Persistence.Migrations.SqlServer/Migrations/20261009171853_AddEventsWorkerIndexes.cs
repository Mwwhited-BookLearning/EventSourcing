using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddEventsWorkerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Events",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "Events",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AppId",
                table: "Events",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Events_AppId_EventType_SequenceNumber",
                table: "Events",
                columns: new[] { "AppId", "EventType", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_MaterializationOfEventId",
                table: "Events",
                column: "MaterializationOfEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_RespondsToEventId",
                table: "Events",
                column: "RespondsToEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status_SequenceNumber",
                table: "Events",
                columns: new[] { "Status", "SequenceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_AppId_EventType_SequenceNumber",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_MaterializationOfEventId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_RespondsToEventId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_Status_SequenceNumber",
                table: "Events");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "AppId",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
