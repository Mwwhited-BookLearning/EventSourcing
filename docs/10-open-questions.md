# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial. When an entry here
gets resolved, move it to a real ADR (or fold it into the ADR that
raised it) and delete the row — this file should only ever contain
things that are *actually* still open, not a permanent archive.

**Not included here, on purpose**: anything deferred purely on
scheduling with no open design question of its own — `ADR-007`
(derived/materialized event types) and `ADR-009`'s masking-enforcement
build are both fully designed, just sequenced later in
`08-build-plan.md`. Those are priority calls, not open questions — see
`CLAUDE.md`/`08-build-plan.md` for that distinction, not this file. Same
reasoning excludes a generalized-framework review's (this session)
documentation-completeness findings — missing component diagrams, the
still-partial GraphQL contract rewrite, stale `features/*.md` Gherkin
scenarios, pre-`ADR-041` DI-wiring sketches — from this table: those are
known propagation debt with no fork to weigh, already tracked in
`CLAUDE.md`'s "Genuinely still outstanding" section. The same review's
findings that *do* raise a genuine unweighed fork (tenant quotas, data
lifecycle, erasure, extensibility cataloging, SDK/codegen, testing
strategy) are the six rows below.

| Question | Raised by | Why it's still open |
|---|---|---|
| Should per-`AppId` (or per-publisher) rate limiting/quota/backpressure be added, and at what layer — the API Gateway (`ADR-049`), the Inbox itself, or a dedicated throttling component? `ADR-023`'s persist-everything, always-`202` ingestion posture plus `ADR-030`'s multi-tenancy mean nothing today stops one tenant's volume (buggy publisher, or hostile one) from starving every other tenant sharing a deployment. `ADR-037`'s GraphQL depth/cost limiter guards one query's shape, not sustained per-tenant volume. | Generalized-framework review (this session) | No proposal exists yet for either the enforcement point or the limiting dimension (requests/sec, bytes, storage volume, concurrent connections) — a real fork, not yet weighed |
| What's the retention/archival/backup/disaster-recovery story for a persist-everything, never-lose-data store that also ingests high-volume streaming channels (`ADR-031`)? `ADR-033`'s replication gives fault tolerance across live replicas, not recovery from a real data-loss/corruption incident, and there's no tiering/cost-management plan for the unbounded growth "persist everything, forever" implies. | Generalized-framework review (this session) | No proposal exists for either piece — a backup/restore mechanism, or a retention/archival tier for cold data — genuinely unweighed |
| If a real deletion/erasure requirement surfaces (GDPR Art. 17, CCPA), what does this design actually do? `ADR-009` already decided *not* to build erasure now and named this explicitly as "a deliberately unsolved, separate problem" — but that's a decision left deliberately partial, not a closed fork: this design has built unusually extensive PII/PHI/PCI classification/masking machinery (`ADR-009`/`050`/`052`) that sits in real tension with deletion being entirely out of scope. | `ADR-009`'s closing note, `README.md` | Explicitly deferred pending a real regulated-domain requirement, per `ADR-009` itself — revisit if/when one shows up, not before |
| Should there be one consolidated "Extensibility Points" reference cataloging every plugin seam this framework already has (`IMaskingStrategy`, `IUpcastExpressionEvaluator`, `IStreamRedactionStrategy`, `IProjection<T>`, `IEventUpcaster`, the per-provider `IEventLineageQueryProvider`/`IJsonPathTranslator` adapters) — and if so, where does it live (a new top-level numbered doc, a `docs/patterns/` entry, or a new section in `06-solution-structure.md`)? | Generalized-framework review (this session) | Each seam is individually documented in its own ADR; no doc answers "every way to extend this without forking core code" in one place — a scope/placement question, not yet decided |
| Should this framework name/adopt a client-SDK or codegen story for consumers — GraphQL Code Generator for the query side, NSwag/`openapi-generator` for the publish side, both, or neither (leave entirely to consumers) — and is an official typed client beyond the one Vue/Pinia reference app (`ADR-039`) worth building? | Generalized-framework review (this session) | No ADR or library doc has weighed this; GraphQL SDL and OpenAPI both make it *possible* via off-the-shelf tooling today, but nothing recommends one |
| What testing strategy validates the correctness properties this design leans on hardest under real load — hash-chain tamper detection (`ADR-019`), replication convergence under partition (`ADR-033`), conflict resolution under concurrent/replayed writes (`ADR-024`)? `06-solution-structure.md`'s `Testcontainers`-based integration suite covers cross-provider behavioral parity, not chaos/fault-injection or property-based testing of these specific invariants. | Generalized-framework review (this session) | No chaos/fault-injection or property-based testing approach is named anywhere in the design package |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
