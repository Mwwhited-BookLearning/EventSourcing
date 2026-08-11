using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddShardingAndReplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeerSyncCursors",
                columns: table => new
                {
                    PeerId = table.Column<string>(type: "text", nullable: false),
                    LastReceivedSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastAckedSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerSyncCursors", x => x.PeerId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerSyncCursors");
        }
    }
}
