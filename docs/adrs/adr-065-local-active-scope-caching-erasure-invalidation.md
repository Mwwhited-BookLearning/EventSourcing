[← ADR index](../07-adrs.md)

# ADR-065: Local/edge clients cache only the active-scoped subset, and purge it on erasure

Status: Accepted

Context: Direction received this session: a local machine only needs a
copy of *active* recording/review data, not a tenant's full history —
but a distributed clean/delete must still be possible when regulation
requires it. Two distinct concerns, addressed together because they
interact: **scope** (what a local/edge client caches at all) and
**erasure reach** (what happens to that cache when an entity is later
erased, `ADR-057`).

**A "local machine" here is a client, not a fourth replication site** —
worth stating explicitly since `ADR-033`/`ADR-061` already define a
multi-site *server* replication topology with its own region-pinning.
A clinical-site tablet, a bedside device gateway, or a KYC verification
kiosk is a leaf consumer of *one* server site via `ADR-039`'s existing
client model (GraphQL Subscription/Follow), not a peer in `ADR-033`'s
gossip mesh — this ADR doesn't add a new replication tier, it scopes and
bounds an existing client-side cache.

Real prior art checked before designing anything bespoke: **CouchDB's
filtered replication** (a filter function or Mango-selector expression
determining which documents sync to a given client — [Couchbase/
PouchDB's own docs](https://www.couchbase.com/blog/introduction-offline-data-storage-sync-pouchdb-couchbase/))
is the closest, long-established precedent for "sync only a
query-scoped subset to an offline-capable client," and this project
already has the equivalent mechanism (`ADR-003`/`04-odata-filter-
pushdown.md`'s `FilterableFields`, now expressed as GraphQL Subscription
arguments per `ADR-037`) — no new filtering mechanism needs inventing.
**One considered and explicitly not adopted**: MongoDB's Atlas Device
Sync (a productized partitioned-sync service) was checked and found
**deprecated in September 2024 and shut down September 2025** — not a
live option, correctly not cited as adopted prior art.

Decision:
- **A local/edge client subscribes with an explicit scope filter**, the
  same `FilterableFields`-backed argument shape any GraphQL Subscription
  already supports (`ADR-037`) — e.g. "entities assigned to this
  site/device AND still open," whatever "active" means for the
  consuming domain. The framework doesn't define what "active" means
  (that's domain data, consistent with `ADR-030`'s domain-agnostic
  core) — it only needs the filtering mechanism it already has.
- **The local cache holds decrypted, reviewable data, not ciphertext
  only** — stated as a deliberate, accepted trade-off, not glossed over:
  genuine offline review (`ADR-039`'s "offline is the default
  assumption") requires data usable with no network present, which means
  locally-available plaintext for whatever's in active scope. This is
  the same kind of accepted exposure `ADR-060` already names for an
  already-delivered webhook payload — bounded by keeping the *scope*
  narrow (only active data, never full history), not by avoiding local
  plaintext entirely.
- ~~**Falling out of active scope evicts the local copy** — when an
  entity no longer matches the subscription's filter (closed, completed,
  reassigned), the client purges its local cached copy proactively, not
  on some unrelated TTL — the subscription's own filter *is* the
  retention policy.~~ **Corrected, 2026-08-12, found by an independent
  design-compliance audit**: this proactive-eviction half was never
  built — confirmed directly against `client-web/src/composables/
  useEntityViewActions.ts`'s own `subscribeToEntity`, whose comment
  states the real behavior plainly: because `config.scopeFilter` is
  enforced server-side per event, an entity that stops matching "simply
  stops receiving further updates through this connection — there is no
  push-based 'you fell out of scope, evict now' signal, so an
  already-cached copy is not proactively purged the moment that
  happens. It goes stale rather than being actively wrong (no further
  writes reach it), and a fresh reconnect with the same filter never
  re-delivers it." So the subscription filter bounds what the cache
  *receives* going forward, but does not evict what it already holds —
  a stale-but-inert copy remains locally until some other event (an
  explicit erasure, a page reload with a narrower filter) removes it.
  The mandatory erasure-triggered purge below (`subscribeToErasure`) is
  genuinely built as designed; only this scope-exit bullet overclaimed.
- **An erasure event reaches subscribed local clients the same way any
  other event does, and purging on receipt is mandatory, not optional.**
  `ADR-057`'s `EntityErasureRequested` is an ordinary `StoredEvent` — a
  local client subscribed to an entity that gets erased receives it
  through its existing subscription, exactly like any other update, and
  treats it as an instruction to immediately delete its own local cached
  copy of that entity (not just wait for the next scope-eviction cycle).
  This closes the specific gap crypto-shredding alone doesn't:
  destroying the server-side DEK makes every *ciphertext* copy
  unreadable everywhere at once, but a local device that already
  decrypted and cached plaintext for offline review holds a copy
  independent of that key — the erasure event's delivery, not the key
  destruction, is what reaches it.
- **Honest, named limitation, not silently glossed over**: a local
  device that is offline at the moment erasure fires won't purge until
  it reconnects and receives the event — the same "already-delivered
  copies aren't retroactively reachable" limitation `ADR-060` already
  states for webhooks, now also true for an offline local cache. Nothing
  in this design can reach a device that never reconnects.

Consequences:
- Answers the "distributed clean/delete" requirement for the parts of
  this design already reachable: server-side replicas (`ADR-057`'s key
  destruction, effective everywhere at once) and any connected local
  client (this ADR's mandatory purge-on-erasure). A permanently
  disconnected/decommissioned device is an operational disposal
  concern (wipe the device), not something this design's own mechanisms
  can reach remotely — stated plainly rather than implied solved.
- No new sync protocol, no new replication tier — `ADR-037`'s existing
  GraphQL Subscription filtering is the entire mechanism; this ADR is a
  scoping and invalidation *policy* on top of it.
- `docs/patterns/pwa-offline-outbox.md` gains a note that its existing
  local cache (currently described for `ViewDefinition`/entity data
  generally) should be understood as scoped this way for any deployment
  handling classified (`ADR-009`) data — done this pass.

**Compliance note** (a proving-ground compliance review, this session):
this ADR is the concrete mechanism that makes `ADR-057`'s GDPR Art. 17
erasure guarantee actually reach an edge/offline client — without the
mandatory purge-on-erasure-event rule decided here, a local device's
already-decrypted cache would remain a live copy of "erased" personal
data indefinitely, silently defeating Art. 17 at exactly the layer
server-side key destruction can't touch.
