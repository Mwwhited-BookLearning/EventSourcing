using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Sqlite.Migrations
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
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReaderActorId = table.Column<string>(type: "TEXT", nullable: false),
                    ReaderTrustBasis = table.Column<string>(type: "TEXT", nullable: false),
                    GrantRef = table.Column<Guid>(type: "TEXT", nullable: true),
                    ViewAccessed = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceRef = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    AccessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ChainHash = table.Column<string>(type: "TEXT", nullable: false)
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
