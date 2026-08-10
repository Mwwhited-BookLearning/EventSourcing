using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookDeliveryCursors",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastDeliveredSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveryCursors", x => x.SubscriptionId);
                });

            migrationBuilder.CreateTable(
                name: "WebhookOutbox",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventPayloadSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookOutbox", x => x.SequenceNumber);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SigningSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousSigningSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FixedClaimsSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.SubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookOutbox_SubscriptionId",
                table: "WebhookOutbox",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveryCursors");

            migrationBuilder.DropTable(
                name: "WebhookOutbox");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");
        }
    }
}
