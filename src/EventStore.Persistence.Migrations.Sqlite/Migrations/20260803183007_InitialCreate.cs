using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventParents",
                columns: table => new
                {
                    ChildEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentEventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventParents", x => new { x.ChildEventId, x.ParentEventId });
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginId = table.Column<string>(type: "TEXT", nullable: true),
                    LogicalClock = table.Column<string>(type: "TEXT", nullable: true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    EventKind = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterializationOfEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", nullable: false),
                    ChainHash = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ConflictFlag = table.Column<bool>(type: "INTEGER", nullable: false),
                    LateArrivalFlag = table.Column<bool>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    AttestedActorId = table.Column<string>(type: "TEXT", nullable: true),
                    AttestedClaims = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorityStatus = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorityDecisionRef = table.Column<Guid>(type: "TEXT", nullable: true),
                    TelemetryPointer = table.Column<string>(type: "TEXT", nullable: true),
                    Signature = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalSequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    OriginalChainHash = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedFrom = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.SequenceNumber);
                });

            migrationBuilder.CreateTable(
                name: "EventTypeDefinitions",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    JsonSchema = table.Column<string>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentValidationMode = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredClaims = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeKind = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityIdField = table.Column<string>(type: "TEXT", nullable: false),
                    UpcastFromPrevious = table.Column<string>(type: "TEXT", nullable: true),
                    DowncastToPrevious = table.Column<string>(type: "TEXT", nullable: true),
                    RejectionBehavior = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredSignature = table.Column<string>(type: "TEXT", nullable: true),
                    DeprecatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTypeDefinitions", x => new { x.AppId, x.Name, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "FilterableFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventTypeAppId = table.Column<string>(type: "TEXT", nullable: false),
                    EventTypeName = table.Column<string>(type: "TEXT", nullable: false),
                    EventTypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    JsonPath = table.Column<string>(type: "TEXT", nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsIndexed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilterableFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilterableFields_EventTypeDefinitions_EventTypeAppId_EventTypeName_EventTypeVersion",
                        columns: x => new { x.EventTypeAppId, x.EventTypeName, x.EventTypeVersion },
                        principalTable: "EventTypeDefinitions",
                        principalColumns: new[] { "AppId", "Name", "Version" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventParents_ChildEventId",
                table: "EventParents",
                column: "ChildEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventParents_ParentEventId",
                table: "EventParents",
                column: "ParentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventId",
                table: "Events",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilterableFields_EventTypeAppId_EventTypeName_EventTypeVersion",
                table: "FilterableFields",
                columns: new[] { "EventTypeAppId", "EventTypeName", "EventTypeVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventParents");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "FilterableFields");

            migrationBuilder.DropTable(
                name: "EventTypeDefinitions");
        }
    }
}
