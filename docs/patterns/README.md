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

## Catalogued, not yet written up as standalone docs

Real patterns already decided (or queued) in this design, listed here so
the catalog is complete even before each gets its own full write-up —
same "state it, don't lose it" discipline this project already applies to
ADRs and `references.md`.

| Pattern | Summary | Applied in |
|---|---|---|
| Claims-based authorization + property-level masking | A second, finer-grained authorization axis on top of coarse operation scopes; redact individual fields via a value/masked wrapper rather than an all-or-nothing response | `ADR-008`, `ADR-009` |
| Proof of Possession (vs. bearer tokens) | Cryptographically bind a token to the holder's key, so a leaked token alone isn't usable | `ADR-017` |
| Safe method with a request body | A `GET`-like, cacheable, side-effect-free method that still carries a body — avoids pushing sensitive query content into a URL, access log, or proxy cache | `ADR-012`, `ADR-031` (queued — GraphQL queries specifically travel over `QUERY`, not `GET`, to keep PII/PHI-bearing filter arguments out of URLs/logs) |
| Problem Details (canonical error shape) | One consistent error response shape across every endpoint, with typed extension fields for situation-specific detail | `ADR-013` |
| Sharding (application-level partitioning) | Partition an entity store by key so no single store need hold everything | `ADR-028` (queued) |
| Multi-origin replication + anti-entropy/gossip | Independent writable replicas, each making local progress with no guarantee of agreement at any instant, converging via background reconciliation | `ADR-027` (queued) |
| Merkle tree catch-up | Exchange hash-tree summaries to find and transfer only the differing ranges after a disconnection, instead of a full resync | `ADR-027` (queued) |
| Non-authoritative capture (Reservation/Provisional + non-repudiation logging) | Accept data whose submitter's authority can't be verified yet; capture now, adjudicate later, via an explicit trust status that never gates ingestion | `ADR-029` (queued) |
| Self-attested, offline-verifiable delegation (DID/UCAN) | Prove a chain of delegated capability without needing to reach a central authority at verification time | `ADR-030` (queued) |
| MVVM (Model-View-ViewModel) | Structure/style/state/transport kept in separate layers; a ViewModel dispatches commands rather than mutating state directly | `ADR-033` (queued) |

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
