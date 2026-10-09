using EventStore.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.Postgres.Migrations
{
    // ADR-095 additive revision -- a consumed wake row is marked ("ConsumedAt")
    // rather than deleted, then swept after a retention window, so wake-ups
    // leave a short audit trail. Raw SQL for the same reason as AddWakeSignalQueue.
    [DbContext(typeof(EventStoreContext))]
    [Migration("20261009130000_AddWakeSignalQueueConsumedAt")]
    public class AddWakeSignalQueueConsumedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                ALTER TABLE "WakeSignalQueue" ADD COLUMN IF NOT EXISTS "ConsumedAt" timestamptz NULL;
                CREATE INDEX IF NOT EXISTS "IX_WakeSignalQueue_Topic_Pending" ON "WakeSignalQueue" ("Topic") WHERE "ConsumedAt" IS NULL;
                """);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WakeSignalQueue_Topic_Pending";
                DELETE FROM "WakeSignalQueue" WHERE "ConsumedAt" IS NOT NULL;
                ALTER TABLE "WakeSignalQueue" DROP COLUMN IF EXISTS "ConsumedAt";
                """);
    }
}
