namespace EventStore.Domain.LeaderElection;

// ADR-078 — one row per singleton background-worker role
// ("Router" | "PeerSyncOutboxPump" | "WebhookOutboxPump" |
// "ExpectedResponseWatcher"), deployment-wide, not AppId-scoped (ADR-075's silo
// model already means one deployment per tenant, so there's no per-AppId
// concept here at all). See docs/data/schema-registry.md's own "Leader
// lease" section for the full write/read reasoning.
public class LeaderLease
{
    public string WorkerRole { get; set; } = default!;  // primary key
    public string LeaseHolderId { get; set; } = default!; // this instance's own identity (host name + process id)
    public DateTimeOffset LeaseExpiresAt { get; set; }
}
