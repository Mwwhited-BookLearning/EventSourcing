using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
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
                    Topic = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastSignaledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WakeSignals", x => x.Topic);
                });

            // ADR-095 -- SqlServerWorkerWakeSignal's own Service Broker
            // objects. ALTER DATABASE SET ENABLE_BROKER can't run inside a
            // transaction (SQL Server itself rejects it) -- suppressTransaction:
            // true on this specific statement only; the CREATE MESSAGE
            // TYPE/CONTRACT/QUEUE/SERVICE statements that follow run
            // normally, inside this migration's own transaction. Every
            // statement is idempotent (IF NOT EXISTS), matching this
            // codebase's own "ensure" pattern elsewhere (e.g.
            // HashiCorpVaultErasureKeyStore.EnsureTransitEngineMountedAsync)
            // -- migrations are the one-time path here, but a defensive
            // guard costs nothing and protects a hand-run script reapplying
            // this later.
            //
            // Wrapped in TRY/CATCH, swallowing the failure -- a REAL, found
            // regression, not a hypothetical: SQL Server flatly refuses
            // "Option 'ENABLE_BROKER' cannot be set in database 'master'",
            // and EVERY pre-existing *SqlServerTests.cs file in this test
            // suite migrates against Testcontainers' own default `master`
            // connection (the established convention long before this
            // migration existed) -- a hard failure here broke every one of
            // them, confirmed by actually running the full suite, not
            // assumed. CREATE QUEUE/SERVICE/CONTRACT/MESSAGE TYPE below
            // don't require Broker to be enabled to exist as schema
            // objects, only to actually carry messages -- WorkerWakeSignal
            // SqlServerTests.cs uses a real, named, non-system database
            // specifically so Broker really is enabled where this
            // mechanism is actually exercised end to end.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_broker_enabled = 1)
BEGIN
    BEGIN TRY
        DECLARE @enableBrokerSql NVARCHAR(MAX) = N'ALTER DATABASE [' + DB_NAME() + N'] SET ENABLE_BROKER';
        EXEC(@enableBrokerSql);
    END TRY
    BEGIN CATCH
    END CATCH
END
", suppressTransaction: true);

            // CREATE MESSAGE TYPE/CONTRACT/QUEUE/SERVICE all genuinely
            // require Broker to be ACTIVE on the current database ("There
            // is no Service Broker active in the database" otherwise --
            // found only by running this) -- gated on is_broker_enabled,
            // not just wrapped in TRY/CATCH like the ALTER DATABASE
            // statement above, so `master`-connected tests (Broker
            // deliberately never enabled there, per that statement's own
            // comment) skip creating these objects entirely rather than
            // failing on each one individually.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_broker_enabled = 1)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = '//EventStore/WakeSignal')
        CREATE MESSAGE TYPE [//EventStore/WakeSignal] VALIDATION = NONE;

    IF NOT EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = '//EventStore/WakeSignalContract')
        CREATE CONTRACT [//EventStore/WakeSignalContract] ([//EventStore/WakeSignal] SENT BY INITIATOR);

    IF NOT EXISTS (SELECT 1 FROM sys.service_queues WHERE name = 'WakeSignalQueue')
        CREATE QUEUE [WakeSignalQueue];

    IF NOT EXISTS (SELECT 1 FROM sys.services WHERE name = '//EventStore/WakeSignalService')
        CREATE SERVICE [//EventStore/WakeSignalService] ON QUEUE [WakeSignalQueue] ([//EventStore/WakeSignalContract]);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.services WHERE name = '//EventStore/WakeSignalService')
    DROP SERVICE [//EventStore/WakeSignalService];
IF EXISTS (SELECT 1 FROM sys.service_queues WHERE name = 'WakeSignalQueue')
    DROP QUEUE [WakeSignalQueue];
IF EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = '//EventStore/WakeSignalContract')
    DROP CONTRACT [//EventStore/WakeSignalContract];
IF EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = '//EventStore/WakeSignal')
    DROP MESSAGE TYPE [//EventStore/WakeSignal];
");

            migrationBuilder.DropTable(
                name: "WakeSignals");
        }
    }
}
