using EventStore.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    // ADR-095 additive revision -- a durable wake queue so a signal sent
    // while no reader is listening is still there on reconnect, matching
    // SQL Server Service Broker's queue semantics. Raw SQL on purpose: the
    // table is infrastructure for PostgresWorkerWakeSignal only, never an
    // EF entity, so the model snapshot is deliberately unchanged.
    [DbContext(typeof(EventStoreContext))]
    [Migration("20261009120000_AddWakeSignalQueue")]
    public class AddWakeSignalQueue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "WakeSignalQueue" (
                    "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    "Topic" text NOT NULL,
                    "EnqueuedAt" timestamptz NOT NULL DEFAULT now()
                );
                CREATE INDEX IF NOT EXISTS "IX_WakeSignalQueue_Topic" ON "WakeSignalQueue" ("Topic");
                """);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "WakeSignalQueue";""");
    }
}
