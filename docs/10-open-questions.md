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
`CLAUDE.md`/`08-build-plan.md` for that distinction, not this file.

| Question | Raised by | Why it's still open |
|---|---|---|
| Which richer masking-content strategy to build **beyond `PartialReveal`** (now decided, `ADR-009`/`ADR-052`) — is `Hash` worth building, and does tokenization belong in `x-masking` at all? Generalization/bucketing fits only as a single-value transform, never as a k-anonymity guarantee; tokenization's separate-party/separate-mechanism reversal model doesn't fit the wrapper at all and would need its own mechanism if ever needed. See `docs/comparisons/masking-strategies.md`. | `ADR-009`, `docs/comparisons/masking-strategies.md` | Narrowed further: `PartialReveal` is decided and built; `Hash`/tokenization/generalization remain undecided, with no application need driving a pick yet. |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
