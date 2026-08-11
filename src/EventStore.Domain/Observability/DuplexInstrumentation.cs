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
}
