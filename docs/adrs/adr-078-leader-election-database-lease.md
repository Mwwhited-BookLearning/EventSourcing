[← ADR index](../07-adrs.md)

# ADR-078: Single-active-worker leader election via a database-backed lease, per worker role

Status: Accepted

Context: `docs/10-open-questions.md` asked whether more than one
`Router`/`UpcastMaterializer`/outbox-pump instance can run per site, and
if so, how they avoid double-folding the same `EntityId` — never stated
either way. `ADR-024`'s optimistic concurrency was named as one candidate
safety mechanism, single-active-worker (leader election) as the other.
`06-solution-structure.md` separately assumes a single instance for spec
caching, a related but distinct assumption not resolved by this ADR.

Decision:
- **Single-active-worker, not concurrency-safe multi-instance.** Exactly
  one instance of each singleton background-worker *role* — the
  `Router`/fold step (`ADR-021`), `UpcastMaterializer` (`ADR-027`), the
  peer-sync outbox pump (`ADR-033`), and the webhook outbox pump
  (`ADR-060`) are each their **own** role with their **own** lease, not
  one shared lease across all of them — is ever actively running per
  site at a time.
- **`ADR-024`'s optimistic concurrency is explicitly not the mechanism
  that makes this safe, and was never meant to be.** It resolves
  concurrent *write-time* races between two API callers publishing
  against the same entity version. Two fold workers concurrently
  applying the same event stream is a different problem — this ADR
  settles it as out of scope for `ADR-024` to solve, rather than leaving
  that ambiguous.
- **Mechanism: a database-backed lease row per worker role** — the same
  shape as [Azure Architecture Center's Leader Election
  pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/leader-election)
  (a worker acquires an exclusive lease and remains leader until it
  releases it or fails to renew), adapted from Azure's own Blob Storage
  lease to a plain lease **row** in the site's own database — the one
  piece of shared infrastructure every deployment already has,
  regardless of provider (`ADR-004`'s Postgres/SQL Server portability;
  no provider-specific primitive like Postgres advisory locks or SQL
  Server `sp_getapplock`):
  ```csharp
  public class LeaderLease
  {
      public string WorkerRole { get; set; } = default!;  // "Router" | "UpcastMaterializer" | "PeerSyncOutboxPump" | "WebhookOutboxPump"
      public string LeaseHolderId { get; set; } = default!; // this instance's own identity (host name + process id, or similar)
      public DateTimeOffset LeaseExpiresAt { get; set; }
  }
  ```
  A holder renews well inside its own expiry window; any instance that
  fails to renew in time loses the lease, and any other instance can
  claim it on its next attempt.
- **Not a quorum/consensus system (etcd, ZooKeeper, Consul).** Real
  quorum systems earn their complexity when there's no single trusted
  store to arbitrate from — not the case here. `ADR-075`'s silo model
  means each site already has exactly one trusted database backing its
  own Event Log, and `ADR-026` places production on Docker Compose, not
  Kubernetes or Service Fabric, so there's no cluster-orchestrator
  election primitive (a Kubernetes `Lease` object, Service Fabric's
  built-in reliable-services election) to lean on either. A new
  consensus service would be a new infrastructure dependency solving a
  problem this design's existing database already solves for free.
- **Failover needs no in-flight-transfer protocol.** Every worker role
  this ADR covers is already idempotent/resumable from durable
  checkpoint state — the `Router`/fold step resumes from
  `LastAppliedSequenceNumber`, `UpcastMaterializer` similarly, both
  outbox pumps from their own cursor (`PeerSyncCursor`/`WebhookOutbox`).
  A new leader simply resumes from the last durable checkpoint; work
  in flight when a lease is lost is safe to abandon and re-derive, not
  something that needs to be handed off cleanly.
- **`06-solution-structure.md`'s single-instance spec-caching assumption
  is a distinct question, not resolved here.** That's a race on a
  different thing entirely (an in-memory `IMemoryCache` behind a
  client-facing `GET`, not the write/fold path) — flagged as separate,
  still-open propagation work rather than silently folded into this
  decision.

Consequences:
- **`LeaderLease` is defined in `docs/data/schema-registry.md`, landed
  in this same pass** per this project's data-model-ownership
  convention — ~~only the actual `LeaderElectionService` implementation
  (a `BackgroundService`/hosted-service wrapper each singleton worker
  composes with) remains not built, consistent with this repo being a
  design package with no `src/` yet. A `DbSet<LeaderLease>` registration
  is still missing from `docs/data/dbcontext-and-conventions.md` —
  tracked in `TODO.md`'s existing data-model drift-table item, not a new
  gap this ADR introduces.~~ **Corrected, later pass**: both are built —
  `docs/08-build-plan.md`'s item 32 ("Leader Election via Database-Backed
  Lease") is marked Done. [`LeaderElectionService.cs`](../../src/EventStore.LeaderElection/LeaderElectionService.cs)
  implements `TryAcquireOrRenewAsync` as a compare-and-swap over one
  role's `LeaderLease` row, and `DbSet<LeaderLease>` is registered in
  [`EventStoreContext.cs`](../../src/EventStore.Persistence/EventStoreContext.cs).
  **One narrowing found only by building this, not anticipated by this
  ADR's own Decision text**: `UpcastMaterializer` does not get its own
  independent lease row despite being named as one of exactly 4
  independent worker roles above. Reading [`RouterWorker.cs`](../../src/EventStore.Router/RouterWorker.cs)
  showed `UpcastMaterializer.ReconcileBacklogAsync`/`TryMaterializeAsync`
  are called inline, directly from `RouterWorker`'s own tick — there is
  no separate, independently-schedulable `UpcastMaterializer` process to
  protect with its own lease; it only ever runs as part of `RouterWorker`'s
  execution, under `RouterWorker`'s own `"Router"` lease. A second lease
  row would protect nothing. Only 3 independent `LeaderLease` rows
  actually exist today: `"Router"`, `"PeerSyncOutboxPump"`, and (once
  `docs/08-build-plan.md`'s Outbound Webhooks item wired it in)
  `"WebhookOutboxPump"` — not the 4 the code sample above lists.
- Clarifies `ADR-024`'s actual scope (write-time conflict flagging only)
  — worth a cross-reference wherever `ADR-024`/`ADR-029` could otherwise
  be misread as already covering concurrent-fold safety.
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 14). `06-solution-structure.md`'s
  spec-caching assumption remains separately open.
