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
`CLAUDE.md`'s "Genuinely still outstanding" section. That same review's
six other findings are now all resolved: client-SDK/codegen (`ADR-054`),
tenant rate limiting (`ADR-058`), data lifecycle/backup (`ADR-056`),
GDPR/CCPA erasure (`ADR-057`), and extensibility cataloging, both the
local half (`ADR-059`, `docs/extensibility-points.md`) and the outbound/
webhook half (`ADR-060`). One narrower residual from the testing-strategy
finding remains genuinely open — the row below.

| Question | Raised by | Why it's still open |
|---|---|---|
| `ADR-055` resolves ordinary test-pyramid coverage (unit/integration/e2e/UI). What validates the harder, adversarial correctness properties this design leans on hardest — hash-chain tamper detection (`ADR-019`), replication convergence under partition (`ADR-033`), conflict resolution under concurrent/replayed writes (`ADR-024`)? `Jepsen`-style fault-injection and `FsCheck`-style property-based testing are named candidates, not yet adopted. | Generalized-framework review (this session), narrowed by `ADR-055` | No chaos/fault-injection or property-based testing approach is named anywhere in the design package — a distinct, adversarial/generative kind of testing from the coverage question `ADR-055` already answered |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
