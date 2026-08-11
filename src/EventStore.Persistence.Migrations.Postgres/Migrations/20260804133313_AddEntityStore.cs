using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntityStore",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    ShardKey = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Extensions = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    AuthorityStatus = table.Column<string>(type: "text", nullable: false),
                    LastAppliedSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastAppliedLogicalTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LateArrivalFlag = table.Column<bool>(type: "boolean", nullable: false),
                    LastAppliedOriginId = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityStore", x => x.EntityId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_EntityId",
                table: "Events",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_EntityStore_EntityType",
                table: "EntityStore",
                column: "EntityType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityStore");

            migrationBuilder.DropIndex(
                name: "IX_Events_EntityId",
                table: "Events");
        }
    }
}
