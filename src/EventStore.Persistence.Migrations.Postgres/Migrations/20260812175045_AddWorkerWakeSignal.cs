using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerWakeSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WakeSignals",
                columns: table => new
                {
                    Topic = table.Column<string>(type: "text", nullable: false),
                    LastSignaledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WakeSignals", x => x.Topic);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WakeSignals");
        }
    }
}
