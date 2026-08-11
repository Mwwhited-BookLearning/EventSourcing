using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessLogEntries",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReaderActorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReaderTrustBasis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GrantRef = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewAccessed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceRef = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChainHash = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogEntries", x => x.SequenceNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogEntries_ReaderActorId",
                table: "AccessLogEntries",
                column: "ReaderActorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogEntries");
        }
    }
}
