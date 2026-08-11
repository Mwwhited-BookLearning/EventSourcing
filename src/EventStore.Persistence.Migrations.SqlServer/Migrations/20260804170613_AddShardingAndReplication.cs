using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
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
                    PeerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastReceivedSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastAckedSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSyncSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
