[← ADR index](../07-adrs.md)

# ADR-102: Cross-provider peer sync, proven real, and a configurable multi-provider Aspire topology

Status: Accepted

Context: `ADR-101`'s own closeout left one honest, explicitly-flagged
gap: `EventStore.Host.SqlServer` was wired with the flow engine's
`PendingTasksDbContext` but verified only via a DI-boot smoke test, not
a real, live run — because `EventStore.AppHost` (`ADR-026`, dev/POC
orchestration) only ever ran one provider at a time, Postgres. The user
then asked directly for a real set of containers covering all three
providers, an explicitly **configurable** database type, and a real
cross-platform data-sync capability if that's what it takes.

Direct verification, before designing anything new (this project's own
"verify before citing" standing rule): `ADR-033`'s existing peer-sync
mechanism (gossip topology, `PeerSyncClient`/`PeerSyncReceiver`) is
**already provider-agnostic by construction, not by luck**.
`PeerSyncClient.PushAsync` is a plain HTTP `POST {peerAddress}/peer-sync/
push` with a JSON body — no shared connection string, no raw SQL, no
assumption about the receiving peer's own EF Core provider.
`PeerSyncReceiver.ReceiveAsync` appends through whichever
`EventStoreContext` the receiving process already resolved (Sqlite/
Postgres/SqlServer, per `ADR-001`), via the ordinary, already-
provider-agnostic `EventAppender.AppendAsync` — exactly `ADR-033`'s own
Decision text ("sync itself performs no routing, schema validation, or
projection... lands exactly as if it arrived from its own client
Inbox"), confirmed verbatim in the code, not assumed from the ADR's
prose. **Nothing new needed to be built for cross-provider sync to
work** — it had simply never been configured or run that way before.

Decision:
- **`EventStore.AppHost` now runs a real, three-node peer-sync mesh**:
  the existing `eventstore` (Postgres, unchanged), plus two new peers —
  `eventstore-sqlite` (`EventStore.Host.Sqlite`, a local file, no
  container) and `eventstore-sqlserver` (`EventStore.Host.SqlServer`,
  a real SQL Server container via Aspire's own first-party
  `Aspire.Hosting.SqlServer`, the identical `AddPostgres`/`AddDatabase`
  shape already used for Postgres). Each gets its own `OriginId`, a full
  static seed-peer mesh (`ADR-051`) naming the other two, and the
  already-seeded `peer-sync-client` identity (`DevIdpSeeder.cs`) — no
  new DevIdp client needed.
- **`Topology:EnableSqlitePeer`/`Topology:EnableSqlServerPeer` config
  flags** (default `true`) make which providers actually participate in
  a given `aspire run` a genuine, orchestration-level configuration
  choice — the literal "configurable database type" ask. This is **not**
  a reversal of `ADR-001`: each peer still hardcodes exactly one
  provider internally; what's configurable is the topology's own node
  roster, decided one level up, in `EventStore.AppHost` specifically
  (`ADR-026`'s already-stated "dev/POC orchestration only" scope). See
  `ADR-001`'s own additive Consequences note.
- **`EventStore.Migrator` generalized to a `Database:Provider`-switched
  tool** (`Postgres`/`SqlServer`/`Sqlite`), one instance run per peer's
  own provider before that peer starts (`ADR-076`'s "no replica migrates
  itself" rule, now applied uniformly to all three peers, not only
  Postgres). Deliberately the one place in this design that DOES branch
  on a provider value at runtime — accepted because this tool runs once,
  for one named provider, and exits; there is no long-lived request path
  for a bad branch to silently misroute, the specific risk `ADR-001`'s
  own Decision was protecting against.
- **A real, genuinely cross-provider integration test**
  (`ReplicationCrossProviderHttpTests.cs`): Site A a real
  `EventStore.Host.Sqlite` TestServer, Site B a real
  `EventStore.Host.SqlServer` TestServer backed by a real
  `MsSqlContainer` (Testcontainers) — an event published and registered
  at the SQLite site, pushed over real HTTP via `PeerSyncClient`, lands
  in the SQL Server site's own `EventStoreContext` with `OriginId`
  preserved. First test of its kind in this repo; every prior
  `Replication*Tests.cs` file exercises peer sync with both peers on the
  *same* provider.
- **Live-verified against a real, fully orchestrated three-node mesh**
  (Docker: a real Postgres container + a real SQL Server container +
  a local SQLite file, all under one `aspire run`): a single event,
  published once against the Postgres node, was independently confirmed
  present — same `EventId`, `EventType`, `Payload` — in both the SQLite
  peer's own file and the SQL Server peer's own database, via each
  peer's own real `PeerSyncCursor` rows advancing on real,
  timestamped sync ticks. The `Topology:Enable*Peer=false` toggle was
  independently verified too: with both new peers disabled, only the
  Postgres node's own container starts, and the other two peers' ports
  never bind at all.

Consequences:
- A real, found-and-fixed startup defect along the way, unrelated to the
  peer-sync mechanism itself: SQL Server's own password-complexity
  policy (at least 3 of {uppercase, lowercase, digit, symbol}) rejected
  this AppHost's existing Postgres dev password pattern outright
  ("Unable to set system administrator password... not complex
  enough") — confirmed by actually starting the container and reading
  its own log, not assumed from the docs. Fixed with a SQL-Server-
  specific dev password meeting that policy.
- A second real, found-and-fixed gap, also unrelated to peer sync
  specifically: `ADR-095`'s own SQL Server Service Broker migration
  deliberately skips creating its `WakeSignalQueue`/message-type/
  contract/service objects when connected to the `master` database
  (Broker can never be enabled there) — every pre-existing
  `*SqlServerTests.cs` file already avoids this by migrating against a
  real, named, non-`master` database, but `ReplicationCrossProviderHttpTests.cs`
  is the first test in this suite to boot the *full* real
  `WebApplicationFactory<Program>` (with `RouterWorker`/
  `WebhookOutboxPump`/`PeerSyncWorker` all genuinely running as
  `BackgroundService`s) against SQL Server rather than calling
  `EventStoreContext`/`PeerSyncReceiver` directly — so it's also the
  first to actually need that Service Broker queue those workers poll.
  Fixed by creating a real named database first, mirroring
  `WorkerWakeSignalSqlServerTests.cs`'s own already-established fix for
  the identical class of problem.
- **Not solved here, deliberately**: Merkle-tree catch-up for a
  long-disconnected peer (`ADR-033`'s own named, not-yet-built
  efficiency optimization — every tick still resends everything since
  the last ack, correct but not optimally efficient) is unaffected by
  this ADR either way, cross-provider or not.
- `docs/comparisons/peer-sync-topology.md` and `08-build-plan.md`'s
  "Sharding & Replication" item both get a short additive note pointing
  at this ADR, rather than restating its content.
