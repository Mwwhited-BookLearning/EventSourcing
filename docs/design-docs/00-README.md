# Distributed Event-Sourced Entity Platform — Design Document Set

**Status:** Draft / Idea Collection
**Version:** 0.2 (split into multiple documents)

This design has grown large enough to split into focused documents. Start here.

## Reading Order

| # | Document | Covers |
|---|---|---|
| 01 | [Overview & Goals](01-overview-and-goals.md) | Purpose, goals, non-goals, terminology |
| 02 | [Architecture Context](02-architecture-context.md) | C4 context/container diagrams, high-level shape |
| 03 | [Client Architecture (MVVM)](03-client-architecture-mvvm.md) | MVVM, command binding, entity view definitions, native/web bridge |
| 04 | [Inbound & Outbound Pipelines](04-inbound-outbound-pipelines.md) | Outbox/inbox transport, status envelopes, response vs. subscription flows |
| 05 | [Data Model](05-data-model.md) | Event store, entity store, schema registry — table shapes |
| 06 | [Partial Updates](06-partial-updates-optional.md) | `Optional<T>`, fold semantics, absent vs. null |
| 07 | [Schema Evolution & Advisory Validation](07-schema-evolution-and-validation.md) | Soft/advisory schema resolution, upcast/downcast maps, JS transforms, CEL |
| 08 | [Concurrency & Conflict Handling](08-concurrency-and-conflict.md) | Causal concurrency, LWW policy, conflict flags, change history |
| 09 | [Sharding & Replication](09-sharding-and-replication.md) | App-level sharding, multi-origin replication, peer sync outbox/inbox, gossip/Merkle catch-up |
| 10 | [Query API](10-query-api.md) | GraphQL vs. OData, hierarchical queries, nullability/extensions, schema mapping directives |
| 11 | [Compatibility & Deployment](11-compatibility-and-deployment.md) | Tolerant reader, expand/contract migrations, rollback safety, N-1/N+1 windows |
| 12 | [Non-Authoritative Capture & Attestation](12-non-authoritative-capture-attestation.md) | Self-attested data, DID/UCAN, OAuth token exchange (RFC 8693) |
| 13 | [BDD Scenarios](13-bdd-scenarios.md) | Gherkin scenario appendix |
| 14 | [Open Questions](14-open-questions.md) | Unresolved decisions |

Also included:

- [`context/conversation-context.md`](context/conversation-context.md) — a narrative summary of the design discussion that produced this document set, intended to let work continue seamlessly in another tool (e.g. Claude Code) without re-deriving the reasoning behind each decision.

## Conventions Used Throughout

- Diagrams are PlantUML (`C4`, sequence, state, class) and Salt (UI wireframes), in fenced ```plantuml``` / ```salt``` code blocks.
- Table schemas are given as simple markdown tables — physical storage (SQL Server, SQLite, etc.) is an implementation detail, not fixed by this design.
- Every numbered document is self-contained enough to read independently, but cross-references other documents by number where relevant (e.g., "see 07 §Upcasting").
