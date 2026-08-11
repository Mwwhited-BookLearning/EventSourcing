using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
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
                    ChildEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventParents", x => new { x.ChildEventId, x.ParentEventId });
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginId = table.Column<string>(type: "text", nullable: true),
                    LogicalClock = table.Column<string>(type: "text", nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    EventKind = table.Column<int>(type: "integer", nullable: false),
                    MaterializationOfEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ChainHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SchemaStatus = table.Column<string>(type: "text", nullable: true),
                    ConflictFlag = table.Column<bool>(type: "boolean", nullable: false),
                    LateArrivalFlag = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    AttestedActorId = table.Column<string>(type: "text", nullable: true),
                    AttestedClaims = table.Column<string>(type: "text", nullable: true),
                    AuthorityStatus = table.Column<string>(type: "text", nullable: false),
                    AuthorityDecisionRef = table.Column<Guid>(type: "uuid", nullable: true),
                    TelemetryPointer = table.Column<string>(type: "text", nullable: true),
                    Signature = table.Column<string>(type: "text", nullable: true),
                    OriginalSequenceNumber = table.Column<long>(type: "bigint", nullable: true),
                    OriginalChainHash = table.Column<string>(type: "text", nullable: true),
                    ImportedFrom = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.SequenceNumber);
                });

            migrationBuilder.CreateTable(
                name: "EventTypeDefinitions",
                columns: table => new
                {
                    AppId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    JsonSchema = table.Column<string>(type: "text", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ParentValidationMode = table.Column<int>(type: "integer", nullable: false),
                    RequiredClaims = table.Column<string>(type: "text", nullable: false),
                    ChangeKind = table.Column<int>(type: "integer", nullable: false),
                    EntityIdField = table.Column<string>(type: "text", nullable: false),
                    UpcastFromPrevious = table.Column<string>(type: "text", nullable: true),
                    DowncastToPrevious = table.Column<string>(type: "text", nullable: true),
                    RejectionBehavior = table.Column<int>(type: "integer", nullable: false),
                    RequiredSignature = table.Column<string>(type: "text", nullable: true),
                    DeprecatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTypeDefinitions", x => new { x.AppId, x.Name, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "FilterableFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventTypeAppId = table.Column<string>(type: "text", nullable: false),
                    EventTypeName = table.Column<string>(type: "text", nullable: false),
                    EventTypeVersion = table.Column<int>(type: "integer", nullable: false),
                    JsonPath = table.Column<string>(type: "text", nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilterableFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilterableFields_EventTypeDefinitions_EventTypeAppId_EventT~",
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
                name: "IX_FilterableFields_EventTypeAppId_EventTypeName_EventTypeVers~",
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
