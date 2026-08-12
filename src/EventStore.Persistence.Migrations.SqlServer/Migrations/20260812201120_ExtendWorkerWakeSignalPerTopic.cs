using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventStore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    // ADR-095's own Consequences named this exactly: "Extending to more
    // workers needs either per-topic queues or a message_body topic check
    // on RECEIVE, neither built here since there was only one topic to
    // receive." Extending the wake signal to the other 5 background
    // workers (TODO.md) is that trigger -- WAITFOR/RECEIVE has no WHERE
    // clause, so the only way for 6 concurrently-waiting topics to each
    // reliably see only their OWN messages is one queue/service/contract/
    // message-type set per topic, not one shared set with a body check
    // that would have to dequeue-and-discard a wrong-topic message anyway
    // (Service Broker has no "peek without removing" for RECEIVE).
    // "router" deliberately keeps its ORIGINAL, un-suffixed object names
    // from AddWorkerWakeSignal -- renaming an already-existing Service
    // Broker service/queue mid-history would need its own DROP/CREATE
    // pass for zero real benefit; SqlServerWorkerWakeSignal's own topic-to-
    // name mapping special-cases "router" for exactly this reason.
    public partial class ExtendWorkerWakeSignalPerTopic : Migration
    {
        private static readonly string[] NewTopics = ["derivation", "expectedresponse", "peersync", "webhookoutbox", "channelderivation"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var topic in NewTopics)
            {
                var messageType = $"//EventStore/WakeSignal_{topic}";
                var contract = $"//EventStore/WakeSignalContract_{topic}";
                var queue = $"WakeSignalQueue_{topic}";
                var service = $"//EventStore/WakeSignalService_{topic}";

                // Same is_broker_enabled gate as AddWorkerWakeSignal's own
                // CREATE block -- a master-connected migration (Testcontainers'
                // own default, every pre-existing *SqlServerTests.cs file)
                // skips creating these objects entirely rather than failing,
                // since Broker itself is never enabled there by design.
                migrationBuilder.Sql($@"
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_broker_enabled = 1)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = '{messageType}')
        CREATE MESSAGE TYPE [{messageType}] VALIDATION = NONE;

    IF NOT EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = '{contract}')
        CREATE CONTRACT [{contract}] ([{messageType}] SENT BY INITIATOR);

    IF NOT EXISTS (SELECT 1 FROM sys.service_queues WHERE name = '{queue}')
        CREATE QUEUE [{queue}];

    IF NOT EXISTS (SELECT 1 FROM sys.services WHERE name = '{service}')
        CREATE SERVICE [{service}] ON QUEUE [{queue}] ([{contract}]);
END
");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var topic in NewTopics)
            {
                var messageType = $"//EventStore/WakeSignal_{topic}";
                var contract = $"//EventStore/WakeSignalContract_{topic}";
                var queue = $"WakeSignalQueue_{topic}";
                var service = $"//EventStore/WakeSignalService_{topic}";

                migrationBuilder.Sql($@"
IF EXISTS (SELECT 1 FROM sys.services WHERE name = '{service}')
    DROP SERVICE [{service}];
IF EXISTS (SELECT 1 FROM sys.service_queues WHERE name = '{queue}')
    DROP QUEUE [{queue}];
IF EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = '{contract}')
    DROP CONTRACT [{contract}];
IF EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = '{messageType}')
    DROP MESSAGE TYPE [{messageType}];
");
            }
        }
    }
}
