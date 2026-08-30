using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EventStore.Domain.Observability;

// ADR-088 -- one shared Meter/ActivitySource ("Duplex.Core") for every
// framework mechanism's own custom metric/trace, registered into ADR-026's
// existing Aspire/OTel pipeline (EventStore.ServiceDefaults) via one
// .AddMeter(Name)/.AddSource(Name) call each. Lives here, not in one of
// the 4 mechanism projects themselves (EventStore.Router/Replication/
// Webhooks/Inbox), because EventStore.Domain is the one project already a
// common (direct or transitive) dependency of all four -- confirmed by
// checking each one's own project references before adding this, not
// assumed. Static, not DI-registered: System.Diagnostics.Metrics/
// Diagnostics instruments are themselves already process-wide singletons
// by design (the same convention .NET's own runtime/ASP.NET Core
// instrumentation libraries use), and it's the only way RouterWorker/
// PeerSyncWorker/WebhookOutboxPump's own STATIC, directly-testable
// RunOnceAsync methods can reach an instrument with no DI container in
// scope at all -- the same reason those methods' existing optional
// erasureKeyService/payloadMasker parameters are plain nullable
// parameters, never DI-resolved internally.
//
// Corrected, 2026-08-11 (additive -- see ADR-088's own Consequences note):
// the ADR's original Decision text claimed the trace half needs zero
// pipeline change, piggybacking on ADR-026's existing
// AddSource(builder.Environment.ApplicationName). That doesn't hold for
// this shared, static-instance shape -- RouterWorker's own testable
// RunOnceAsync has no DI container (and therefore no IHostEnvironment) to
// resolve an ApplicationName-matched ActivitySource from when a test
// calls it directly, the exact same seam every other optional service
// parameter on that method already depends on. A second, explicit
// AddSource(Name) call (mirroring the metrics AddMeter call exactly) is
// used instead -- one added pipeline line, not zero, but still far short
// of "a new observability stack."
public static class DuplexInstrumentation
{
    public const string Name = "Duplex.Core";

    public static readonly Meter Meter = new(Name);
    public static readonly ActivitySource ActivitySource = new(Name);

    // Recorded only for events that fold immediately (AuthorityStatus
    // already "accepted" at publish) -- an event gated through
    // unattested/pending_review waits on open-ended human review, not
    // processing time, and must never be conflated into this histogram
    // (ADR-088's own explicit warning).
    public static readonly Histogram<double> RouterFoldLagMs = Meter.CreateHistogram<double>(
        "duplex.router.fold_lag", "ms",
        "Elapsed time between an event's SequenceNumber assignment (AppendedAt) and its immediate fold into the authoritative Entity Store.");

    public static readonly Histogram<double> WebhookDeliveryLagMs = Meter.CreateHistogram<double>(
        "duplex.webhook.delivery_lag", "ms",
        "Elapsed time between a webhook delivery being enqueued (WebhookOutbox.EnqueuedAt) and its confirmed delivery.");

    public static readonly Counter<long> HashChainVerificationOutcomes = Meter.CreateCounter<long>(
        "duplex.hashchain.verification_outcomes",
        description: "Count of hash-chain verification attempts, tagged by outcome (\"outcome\": \"verified\" or \"tampered\").");

    // The OTel SDK's own ObservableGauge contract requires a synchronous,
    // side-effect-free read each time a collector observes it -- no I/O.
    // PeerSyncWorker itself (the only thing that already computes current
    // depth/age per peer, once per tick, as part of its own ordinary sync
    // work) publishes into this snapshot cache; the gauge callbacks below
    // only ever read it back, never query the database themselves.
    private static readonly ConcurrentDictionary<string, (long Depth, double OldestPendingAgeMs)> PeerSyncOutboxSnapshots = new();

    public static void ReportPeerSyncOutbox(string peerId, long depth, TimeSpan oldestPendingAge) =>
        PeerSyncOutboxSnapshots[peerId] = (depth, oldestPendingAge.TotalMilliseconds);

    public static readonly ObservableGauge<long> PeerSyncOutboxDepth = Meter.CreateObservableGauge<long>(
        "duplex.peersync.outbox_depth",
        () => PeerSyncOutboxSnapshots.Select(kv => new Measurement<long>(kv.Value.Depth, new KeyValuePair<string, object?>("peer.id", kv.Key))),
        description: "Current pending-item count in this site's peer-sync outbox, per peer.");

    public static readonly ObservableGauge<long> PeerSyncOutboxOldestPendingAgeMs = Meter.CreateObservableGauge<long>(
        "duplex.peersync.outbox_oldest_pending_age",
        () => PeerSyncOutboxSnapshots.Select(kv => new Measurement<long>((long)kv.Value.OldestPendingAgeMs, new KeyValuePair<string, object?>("peer.id", kv.Key))),
        unit: "ms",
        description: "Age of the oldest pending item in this site's peer-sync outbox, per peer.");

    // Direct request -- broader than ADR-088's own original mechanism list
    // (Router/Webhooks/Replication/Inbox's hash-chain verifier), added the
    // same additive way: one more instrument on the same shared Meter, no
    // new pipeline wiring beyond what AddMeter(Name) already covers.

    public static readonly Counter<long> PublishOutcomes = Meter.CreateCounter<long>(
        "duplex.publish.outcomes",
        description: "Count of PublishService.PublishAsync calls, tagged by \"app.id\", \"event.type\", and \"outcome\" (the PublishResult case name).");

    public static readonly Histogram<double> PublishLatencyMs = Meter.CreateHistogram<double>(
        "duplex.publish.latency", "ms",
        "Wall-clock time PublishService.PublishAsync spent per call, regardless of outcome -- tagged by \"app.id\"/\"event.type\".");

    public static readonly Counter<long> GraphQlRequestOutcomes = Meter.CreateCounter<long>(
        "duplex.graphql.request_outcomes",
        description: "Count of completed GraphQL requests (query/mutation/subscription-connect), tagged by \"outcome\" (\"ok\" or \"error\" -- error means at least one ResolverError fired during the request).");

    public static readonly Histogram<double> GraphQlRequestLatencyMs = Meter.CreateHistogram<double>(
        "duplex.graphql.request_latency", "ms",
        "Wall-clock time between HotChocolate's own StartProcessing and StopProcessing diagnostic events for one GraphQL request.");

    public static readonly Histogram<double> DerivationLagMs = Meter.CreateHistogram<double>(
        "duplex.derivation.lag", "ms",
        "Elapsed time between the triggering source event's own AppendedAt and the derived event's successful publish -- the DerivationWorker analogue of RouterFoldLagMs above, tagged by \"app.id\"/\"derivation.name\".");

    public static readonly Counter<long> ArchivalSegmentsArchived = Meter.CreateCounter<long>(
        "duplex.archival.segments",
        description: "Count of ArchivalService segment-archive attempts, tagged by \"log\" (\"event\" or \"access\") and \"outcome\" (\"archived\", \"nothing_to_archive\", or \"not_verified\").");

    public static readonly Histogram<double> ArchivalOperationDurationMs = Meter.CreateHistogram<double>(
        "duplex.archival.operation_duration", "ms",
        "Wall-clock time one ArchivalService segment-archive call took (verify + serialize + checkpoint + detach), tagged by \"log\".");

    // Samples.Vitals.Simulator/Samples.Meridian.Simulator both run as plain
    // BackgroundService-free console loops with no other reason to carry
    // OTel wiring of their own -- recorded on the same shared instrument
    // here rather than a third copy of a near-identical Meter, tagged by
    // "app.id" to tell the two domains' own dashboard series apart.
    public static readonly Counter<long> SimulatorEventsPublished = Meter.CreateCounter<long>(
        "duplex.simulator.events_published",
        description: "Count of events a proving-ground Simulator process has successfully published, tagged by \"app.id\".");

    // Direct request -- background-worker tail-loop reconnects should be
    // bound into Aspire/OTel automatically (a real Activity exception
    // event, a real counter an operator can graph/alert on), not left as
    // an ILogger call alone that's easy to miss. "not_yet_registered" (a
    // reserved event type with no schema yet for this AppId, an expected,
    // recoverable state -- RbacProjectionWorker's own comment) and "error"
    // (a genuine connection loss) are tracked as the same counter's own
    // "reason" tag rather than two separate instruments, since they're
    // both answers to the same operational question, "how often is this
    // tail loop reconnecting, and why."
    public static readonly Counter<long> WorkerTailReconnects = Meter.CreateCounter<long>(
        "duplex.worker.tail_reconnects",
        description: "Count of a background worker's own tail-loop reconnects, tagged by \"worker\", \"app.id\"/\"event.type\" (or whatever the caller's own dimensions are), and \"reason\" (\"not_yet_registered\" or \"error\").");
}
