# Architecture & Design Pattern Catalog

This folder is a **pattern reference**, distinct from the other two kinds
of document in this design package:

- `docs/adrs/*.md` explains *a specific decision this project made* — the
  context, the trade-off, the consequences, for this system specifically.
- `docs/references.md` is a *bibliography* — every external RFC/standard/
  library this design adopts or considered, one line each.
- **This folder** explains *the general pattern* first (what it is, who
  named it, when you'd reach for it, what it costs), then points to the
  specific ADR(s) that apply it here. Read a pattern doc to learn the
  pattern itself, portably — not just how this one project happens to use
  it. This is itself part of this project's stated purpose as a worked
  teaching example (`README.md`), not an afterthought.

## Written

| Pattern | Summary | Applied in |
|---|---|---|
| [Event Sourcing](event-sourcing.md) | State as a replayable sequence of immutable events, not a mutable row | `ADR-004`, `ADR-009`, `ADR-019`, `ADR-023` |
| [CQRS & Materialized Views](cqrs-and-materialized-views.md) | Separate write model from read model; reads come from a disposable, rebuildable projection | `ADR-015`, `ADR-016`, `ADR-021` |
| [Idempotent Receiver & Inbox/Dead Letter](idempotent-receiver-and-inbox.md) | Safe retries; persist-before-understand; failures become inspectable records, not silent drops | `ADR-011`, `ADR-020`, `ADR-023` |
| [Tolerant Reader & Schema Evolution](tolerant-reader-and-schema-evolution.md) | Ignore what you don't recognize; reconcile old-shaped data on read, never by rewriting history | `ADR-018`, `ADR-020`, `ADR-022` |
| [Optimistic Concurrency](optimistic-concurrency.md) | Check for conflict at commit time instead of locking; flag genuine conflicts rather than silently resolving them | `ADR-024` |
| [Hash Chain (Tamper-Evident Log)](hash-chain-integrity.md) | Each record's hash incorporates the previous one's, so undetected history tampering becomes detectable | `ADR-019` |

## Interactions — where two patterns compose

A pattern rarely runs alone. When two (or more) genuinely combine at one
point in this design — not just "both used somewhere in the same ADR" —
the interaction gets its own page under
[`interactions/`](interactions/), since neither pattern's own doc tells
the whole story on its own:

| Interaction | What it explains |
|---|---|
| [Fold-time ordering + conflict detection](interactions/fold-ordering-and-conflict.md) | Why [Optimistic Concurrency](optimistic-concurrency.md) and [Watermarks/event-time ordering](tolerant-reader-and-schema-evolution.md) are two different checks, run together, catching two different failure modes — and why only one of the two actually blocks a write from applying |
| [The publish pipeline](interactions/publish-pipeline.md) | How [Idempotent Receiver, Inbox](idempotent-receiver-and-inbox.md), [Tolerant Reader](tolerant-reader-and-schema-evolution.md), and Dead Letter Channel compose into one `POST /publish` request, in a specific, load-bearing order |

## Decided, not yet written up as standalone docs

Real patterns with a real, landed ADR, listed here so the catalog stays
current even before each gets its own full write-up:

| Pattern | Summary | Applied in |
|---|---|---|
| Upcast/downcast schema mapping (Avro-schema-resolution-adjacent) | Forward map (old→current) for replay, persisted once as a materialized event so it's never recomputed; backward map (current→old) for serving legacy consumers, computed fresh per request since its target isn't fixed | `ADR-018`, `ADR-027`, `ADR-028` |
| Watermarks / event-time vs. processing-time ordering | Fold by logical occurrence time, not arrival order, so a late-arriving event can't silently revert already-applied newer data; flag it instead of blocking or corrupting | `ADR-029` |
| Multi-tenancy via a namespacing key | One engine, many independent applications, each schema/entity scoped by an `appId` key so unrelated applications can't collide or see each other's registrations | `ADR-030` |

## Catalogued, still genuinely queued (no ADR yet)

Real patterns this design intends to adopt, named here so the catalog is
complete even before the deciding ADR exists — same "state it, don't lose
it" discipline this project already applies to `references.md`. Numbers
are deliberately omitted: these get assigned when each ADR is actually
written, and a hardcoded "queued as `ADR-0XX`" note here would just go
stale the next time something jumps the queue (it already has, twice).

| Pattern | Summary |
|---|---|
| Claims-based authorization + property-level masking | Already decided (`ADR-008`, `ADR-009`) — listed here too since it's a genuine pattern in its own right, not only a project-specific decision |
| Proof of Possession (vs. bearer tokens) | Already decided (`ADR-017`) — same note as above |
| Safe method with a request body | Already decided (`ADR-012`); the GraphQL-only query layer will extend it — GraphQL queries specifically over `QUERY`, not `GET`, to keep PII/PHI-bearing filter arguments out of URLs/logs |
| Problem Details (canonical error shape) | Already decided (`ADR-013`) |
| Sharding (application-level partitioning) | Partition an entity store by key so no single store need hold everything |
| Multi-origin replication + anti-entropy/gossip | Independent writable replicas, each making local progress with no guarantee of agreement at any instant, converging via background reconciliation |
| Merkle tree catch-up | Exchange hash-tree summaries to find and transfer only the differing ranges after a disconnection, instead of a full resync |
| Non-authoritative capture (Reservation/Provisional + non-repudiation logging) | Accept data whose submitter's authority can't be verified yet; capture now, adjudicate later, via an explicit trust status that never gates ingestion |
| Self-attested, offline-verifiable delegation (DID/UCAN) | Prove a chain of delegated capability without needing to reach a central authority at verification time |
| MVVM (Model-View-ViewModel) | Structure/style/state/transport kept in separate layers; a ViewModel dispatches commands rather than mutating state directly |

## A note on diagrams

Each written pattern doc includes a PlantUML diagram (sequence, C4
component, or object, whichever actually clarifies that pattern) — none
of the six written so far have a UI surface, so Salt wireframes aren't
used in this folder yet; `ADR-033`'s MVVM/entity-view pattern (queued)
will be the first one that genuinely calls for one.

## A note on superseded patterns

`ADR-018` originally justified its OData `compute()` upcast mechanism by
reusing the OData parser this design already depended on for `$filter`
pushdown — a real instance of "prefer reusing an existing primitive over
adding a new dependency" (see how [Hash Chain](hash-chain-integrity.md)
reuses `ADR-011`'s SHA-256 for the same reason). That specific reuse
argument stopped holding once OData was swapped out entirely for GraphQL
(`ADR-031`, queued) — recorded in `references.md` and `CLAUDE.md`'s
integration status, not silently dropped.
