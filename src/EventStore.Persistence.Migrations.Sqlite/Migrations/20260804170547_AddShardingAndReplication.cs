using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
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
                    PeerId = table.Column<string>(type: "TEXT", nullable: false),
                    LastReceivedSequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAckedSequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSyncAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSyncSuccessAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
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
