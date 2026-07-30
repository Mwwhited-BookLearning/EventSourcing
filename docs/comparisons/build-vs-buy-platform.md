[← Comparisons index](README.md)

# Build vs. buy: does an existing platform already do this?

**Decision already made, this comparison written to show the real
alternatives checked, not to gate a not-yet-written ADR** — the same
shape as [Proving-ground domain](proving-ground-domain.md): `README.md`
already states this project's purpose is "a worked example, not just a
store," and `references.md`'s "Off-the-shelf event-store products" entry
already declined `EventStoreDB`/`Marten` on that basis. What was missing
was the concrete competitive-landscape check *behind* that declaration —
this project had named two candidates and moved on, without asking
whether some other product, or combination, already covers most of what
the base engine (internal code name **Duplex**) actually specifies.
Written after a direct request to review Duplex's design for buildability
and to find an existing product that does most or all of what it does.

## What Duplex actually specifies, for comparison purposes

Not a repeat of every ADR — the feature clusters that matter for judging
product overlap:

- **Core event-sourcing/CQRS engine**: append-only, hash-chained
  (`ADR-019`) event log with causal parent-event links (`ADR-005`, a DAG
  not a linear stream); an always-on Entity Store fold with optimistic
  concurrency (`ADR-021`/`ADR-024`) and logical-order handling for
  late-arriving events (`ADR-029`); a schema registry with JSON Schema
  validation, materialized upcasting/downcasting (`ADR-018`/`ADR-027`/
  `ADR-028`), and a pluggable upcast engine (`ADR-053`); pluggable CQRS
  read-side projections (`ADR-015`); persist-everything ingestion
  (`ADR-023`); non-authoritative capture with a visible-but-labeled Live
  View (`ADR-035`/`ADR-042`).
- **API surface**: GraphQL-only, specifically to keep PII/PHI-bearing
  filter arguments off `GET`/logs/caches (`ADR-037`), with generated
  client SDKs (`ADR-054`).
- **Multi-tenancy & distribution**: `AppId`-scoped multi-tenancy
  (`ADR-030`), gossip-topology multi-origin replication with a minimum
  2-replica guarantee (`ADR-033`), entity-type sharding (`ADR-034`),
  per-tenant data residency (`ADR-061`) — explicitly **not**
  consensus-based DLT/blockchain; peers are pre-configured and trusted,
  not a trustless network.
- **Security/compliance**: OAuth2/OIDC + DPoP (`ADR-006`/`ADR-017`),
  RBAC (`ADR-046`), UCAN-based delegated/row-scoped access (`ADR-043`),
  DID/UCAN self-attestation (`ADR-036`), pluggable property-level masking
  (`ADR-009`), GDPR erasure via crypto-shredding with keyed/multi-backend
  KMS support (`ADR-057`), read-access audit logging (`ADR-045`),
  control-plane-as-events (`ADR-067`), RFC 9470 step-up digital sign-off
  (`ADR-066`), per-tenant rate limiting (`ADR-058`), SBOM/SOUP (`ADR-074`).
- **Other data planes**: streaming channels with read-time redaction
  (`ADR-031`/`ADR-052`), content-addressable binary attachments
  (`ADR-032`), device-input ingestion (`ADR-070`), bulk ingestion +
  interchange-format adapters (`ADR-072`).
- **Litigation/forensic tooling**: bitemporal playback — valid-time vs.
  transaction-time query (`ADR-068`) — lineage export, and a
  self-contained offline viewer.
- **Client**: MVVM (`ADR-039`), installable/offline PWA with a durable
  outbox (`docs/patterns/pwa-offline-outbox.md`), WCAG 2.1 AA (`ADR-073`).

## Candidates checked

Each checked against its own current, real documentation (not recalled
from memory) — status verified as of this pass, since several of these
products have had major status changes recently.

| Candidate | Status (verified) | Covers | Missing, against Duplex's spec |
|---|---|---|---|
| [EventStoreDB / Kurrent](https://www.kurrent.io/blog/exploring-the-main-features-of-eventstoredb-2/) | Active | Append-only log, quorum-based clustering, projections, optimistic concurrency | No hash-chaining, schema registry, multi-tenancy, GraphQL, RBAC, masking, or bitemporal query; [sharding isn't supported out of the box](https://blog.arkency.com/multi-tenant-applications-with-horizontal-sharding-and-rails-event-store/) |
| [Axon Server / Framework](https://www.axoniq.io/blog/multitenancy-with-axon) | Active | Event sourcing, CQRS, multi-tenant "contexts," and a **real GDPR crypto-shredding module** — [AxonIQ's own announcement](https://www.prnewswire.com/news-releases/axoniq-delivers-gdpr-module-to-enable-mandatory-erasure-of-data-in-immutable-event-driven-systems-657197853.html) is the closest single-product match to Duplex's core-engine-plus-erasure pairing | No GraphQL-only surface (Java/gRPC-centric), no gossip cross-site replication, no bitemporal playback, no DID/UCAN, no device-input ingestion, no offline litigation viewer |
| [Confluent Platform / Kafka + Schema Registry](https://docs.confluent.io/platform/current/schema-registry/fundamentals/index.html) | Active | Multi-tenant logical schema registries, [RBAC scoped to clusters/topics/subjects](https://docs.confluent.io/platform/current/schema-registry/security/rbac-schema-registry.html), stream-level masking | Its "GraphQL API" is catalog/metadata search, not a business-data query surface (no `HTTP QUERY`-based filtering); no entity-fold/`ExpectedVersion` model; no crypto-shredding erasure; no bitemporal query |
| [Amazon QLDB](https://www.infoq.com/news/2024/07/aws-kill-qldb/) | **Discontinued** — service ends July 31, 2025 | Was the closest immutable-ledger analog | Dead; [AWS's own migration path](https://techcommunity.microsoft.com/blog/azuresqlblog/moving-from-amazon-quantum-ledger-database-qldb-to-ledger-in-azure-sql/4246237) (Aurora Postgres) doesn't preserve permanent immutability |
| [Azure SQL Database Ledger](https://learn.microsoft.com/en-us/sql/relational-databases/security/ledger/ledger-overview?view=sql-server-ver17) | Active | Real SHA-256 hash-chained tamper-evidence over relational tables — closest verified analog to `ADR-019`'s own chain mechanism | A relational-table feature, not an event-sourced/entity-fold system; no schema registry, GraphQL, RBAC framework, or erasure model layered on it |
| [Datomic](https://docs.datomic.com/releases.html) | Active (v1.0.7387, June 2025) | Immutable, time-aware storage | Confirmed **unitemporal** (transaction time only) — not the valid-time-vs-transaction-time distinction `ADR-068` needs; no schema registry, multi-tenant sharding, or compliance layer |
| [XTDB v2](https://docs.xtdb.com/about/time-in-xtdb.html) | Active (v2.1.0, Dec 2025) | The one verified **genuinely bitemporal** engine (SQL:2011 valid-time + system-time, Postgres wire protocol) — direct precedent for `ADR-068` | No schema registry, RBAC, masking, erasure, GraphQL, or multi-tenant/sharding model built in |
| [Marten (.NET)](https://martendb.io/events/multitenancy) | Active | Entity-fold + optimistic concurrency + built-in per-tenant partitioning (`TenancyStyle`) — closest lightweight match to the core engine | No schema/upcast pipeline, GraphQL, crypto-shredding, replication/sharding, or compliance surface |
| [Hyperledger Fabric](https://toc.hyperledger.org/project-reports/2024/2024-Q2-Hyperledger-Fabric.html) / [R3 Corda](https://r3.com/r3-announces-launch-of-corda-protocol-on-behalf-of-r3-foundation-to-bring-institutional-grade-curated-yield-to-solana/) | Both active | Permissioned ledger, regulated-industry focus (Corda live in 20+ financial networks) | Both are consensus-based DLT — explicitly out of scope per Duplex's own design (gossip replication between trusted, pre-configured peers, not a trustless network); neither offers GraphQL, bitemporal playback, or crypto-shredding natively |
| [Microsoft Dataverse](https://learn.microsoft.com/en-us/power-platform/admin/dataverse-privacy-dsr-guide) / [Salesforce Shield](https://www.salesforce.com/platform/shield/guide/) | Both active | Broad RBAC, field/row-level security, long-retention audit trails, formal GDPR DSR erasure tooling (Dataverse) | Neither is event-sourced at the storage layer — no hash-chained append-only log, no entity-fold model, no GraphQL-only surface, no bitemporal query |

## Verdict

**No single existing product covers most or all of this feature set —
and none comes close to even half of it in verified form.** What exists
is a fragmented landscape where each functional slice has real,
well-established prior art (event-sourcing core: Axon/Marten; schema
governance: Confluent; bitemporal query: XTDB; tamper evidence:
Azure SQL Ledger; compliance/RBAC breadth: Dataverse/Shield), but nothing
bundles event-sourcing + hash-chained tamper evidence + true bitemporal
playback + GraphQL-only surface + DID/UCAN capability delegation +
crypto-shredding erasure + gossip multi-site replication + device-input
ingestion + a standalone offline litigation viewer into one generalized,
multi-tenant framework.

**Recommendation: reaffirms the existing build decision, now with
evidence rather than assertion.** `README.md`'s "worked example, not
just a store" framing and `references.md`'s prior EventStoreDB/Marten
decline both predate this check — this comparison doesn't change either,
it grounds them. The one live trade-off worth naming explicitly: **Axon
Server/Framework** is close enough on the core-engine-plus-erasure half
that a team under real time pressure could reasonably consider building
*on* Axon (Java/gRPC) rather than from scratch (.NET) and layering
Duplex's GraphQL/replication/bitemporal/compliance work on top of it —
not adopted here, since `ADR-041`'s .NET-first-party stack and this
project's own stated teaching purpose both argue against it, but a real
option a production-minded fork of this design should weigh, not
silently dismiss.
