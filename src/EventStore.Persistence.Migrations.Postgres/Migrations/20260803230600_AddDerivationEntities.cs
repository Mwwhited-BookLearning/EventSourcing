using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivationEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DerivationHopCount",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DerivationCursors",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "text", nullable: false),
                    DerivationName = table.Column<string>(type: "text", nullable: false),
                    SourceEventType = table.Column<string>(type: "text", nullable: false),
                    LastProcessedSequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivationCursors", x => new { x.AppId, x.DerivationName, x.SourceEventType });
                });

            migrationBuilder.CreateTable(
                name: "DerivationDefinitions",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Sources = table.Column<string>(type: "text", nullable: false),
                    JoinConditions = table.Column<string>(type: "text", nullable: false),
                    SelectFields = table.Column<string>(type: "text", nullable: false),
                    JoinTriggerMode = table.Column<int>(type: "integer", nullable: false),
                    BackfillMode = table.Column<int>(type: "integer", nullable: false),
                    BackfillThroughDerivedSources = table.Column<bool>(type: "boolean", nullable: false),
                    PendingJoinTtl = table.Column<TimeSpan>(type: "interval", nullable: false),
                    MaxHopCount = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivationDefinitions", x => new { x.AppId, x.Name });
                });

            migrationBuilder.CreateTable(
                name: "PendingJoinStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<string>(type: "text", nullable: false),
                    DerivationName = table.Column<string>(type: "text", nullable: false),
                    JoinKeyValue = table.Column<string>(type: "text", nullable: false),
                    ArrivedSourcesJson = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiredReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingJoinStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingJoinStates_AppId_DerivationName_JoinKeyValue",
                table: "PendingJoinStates",
                columns: new[] { "AppId", "DerivationName", "JoinKeyValue" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingJoinStates_ExpiresAt",
                table: "PendingJoinStates",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivationCursors");

            migrationBuilder.DropTable(
                name: "DerivationDefinitions");

            migrationBuilder.DropTable(
                name: "PendingJoinStates");

            migrationBuilder.DropColumn(
                name: "DerivationHopCount",
                table: "Events");
        }
    }
}
