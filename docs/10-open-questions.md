# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial.

**When a row gets resolved, delete it outright — don't strike it
through and retain it.** (Direction received this session, reversing an
earlier same-session correction that said the opposite.) Every resolved
row already has a real, permanent, scoped home: the ADR that resolved
it, or an existing ADR's additive addendum. Retaining a struck-through
copy here duplicates that home for no reason. The one-line historical
record of *what got resolved, when, by which ADR* lives in that day's
`docs/changes/{date}.md` instead — see `docs/changes/2026-07-31.md` for
this session's resolutions. If another doc cites this file by row
number, update that citation to point at the resolving ADR (or that
day's changelog) once the row is deleted — a row number is not a stable
long-term address.

**A row can also be deleted for a different reason: it turns out to be
genuinely domain-specific, not a framework-wide fork**, and gets
relocated to the owning domain's own `README.md` Special Concerns
section instead (e.g. algorithmic-bias auditing → `docs/domains/
insurance-telematics/README.md`; FDA's 15-day adverse-event clock →
`docs/domains/pharmacovigilance/README.md`). That's a "this never
belonged in the framework-level tracker" correction, not a resolution —
nothing is lost, the content lives on in the domain doc.

**Not included here, on purpose**:
- Domain-specific regulatory/compliance gaps found while reviewing one
  domain's own `README.md` — those live in that domain's own Special
  Concerns section, not here, even while genuinely unresolved.
- **Pure operations/deployment-process concerns with no architecture or
  development decision embedded** — alert thresholds, on-call rotation,
  paging policy, and similar. Confirmed explicitly this session (former
  row 7's residual, after `ADR-088` resolved the actual instrumentation
  half): these aren't merely deprioritized, they were never a fork this
  file should hold in the first place — no design decision is possible
  or needed at the framework level, only an operational runbook a
  deployment writes for itself.
- Anything deferred purely on scheduling with no open design question of
  its own (e.g. `ADR-007`, `ADR-009`'s masking-enforcement build — both
  fully designed, just sequenced later in `08-build-plan.md`). Those are
  priority calls, not open questions — see `CLAUDE.md`/`08-build-
  plan.md` for that distinction.
- Known propagation/documentation debt with no fork to weigh (a missing
  diagram, a stale Gherkin scenario) — tracked in `TODO.md`, not here.
- A question genuinely still open but explicitly **deprioritized** for
  now rather than resolved — noted in place, in the row itself, with a
  **Back-burnered** marker and the reason, rather than removed (nothing
  was decided, so there's no resolution to move elsewhere).

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of.

| # | Question | Raised by | Why it's still open |
|---|---|---|---|
| 1 | `comparisons/authority-rejection-behavior.md`'s "Refinement" section recommends "annotate-only-plus-targeted-rebuild" (a post-hoc `authorityDecision: rejected` triggers an immediate single-entity re-fold, excluding events whose current `AuthorityStatus` isn't `accepted`) as the system-wide default for `RejectionBehavior.Annotate` — but `ADR-035`'s own Decision text, `docs/data/schema-registry.md`'s `RejectionBehavior` field comment, and `docs/features/non-authoritative-capture.md`'s own Gherkin (`"the Entity Store row for sensor-42 should still show Reading 99.9"` after an Annotate-type rejection — no rebuild, no exclusion) all still describe/test the plain, un-refined shape. | Building "Non-Authoritative Capture" (this session) — implemented per the plain shape (all three of ADR-035/data-model/feature-doc agree), since that's what the item's own exit criteria actually test | Never resolved which is authoritative: was the comparison doc's refinement adopted and just never propagated to the other three surfaces (a real drift, `CLAUDE.md`'s propagation rule), or was it considered and superseded without a note? Also unresolved if adopted: `QUERY /entities/{entityId}/events` (the single-entity replay query the refinement's own text says it added "directly in response to this refinement") doesn't exist in code either — only an index comment references it. |
| 2 | `ADR-042`'s headline caller-facing requirement — every Live View response carrying an `isAuthoritative: false` flag for a not-yet-accepted event — and `ADR-045`'s own most-cited surface — "every GraphQL query against the authoritative Entity Store or Live View" gets an `AccessLogEntry` — both assume a generic entity/Live-View GraphQL query surface that was never built. "GraphQL-Only Query Layer" (built later) deliberately scoped out a generic `entity(id)`/`extensions: JSON` query ("nothing built here ever needs one," per that item's own Built-scope note) — a decision made without an explicit forward/backward pointer connecting it to ADR-042/045's earlier assumption. `isAuthoritative` appears nowhere in `src/` (only in docs); `AccessLogAppender` IS genuinely called from attachment download, streaming, lineage query/export, and `revealField` — just never from a Live-View/entity read, since none exists. | A full design-compliance audit against all 94 ADRs | Genuinely two different possible resolutions, neither decided: (a) build the generic entity/Live-View query surface ADR-042/045 assumed, now that a real caller might want it (e.g. client-web's own `EntityView`, which currently reads the Entity Store only via Follow's per-event-type Subscription, never a direct entity-by-id query); or (b) formally narrow ADR-042/045's own Consequences with an additive note acknowledging the assumed surface was superseded by "GraphQL-Only Query Layer"'s later scoping decision, the same way `ADR-030` was formally revised by `ADR-075`. Nobody has picked between these — this row exists so the choice doesn't get made by default via continued silence. |
