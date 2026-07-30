[← ADR index](../07-adrs.md)

# ADR-042: Gated authoritative publish — the Entity Store only reflects approved data; a separate Live View shows the rest

Status: Accepted — revises `ADR-035`

Context: `ADR-035` established `AuthorityStatus` as a trust axis
independent of `SchemaStatus`, but decided an `unattested` event folds
into the Entity Store "identically to an accepted one; the only
difference is a label." Direction received this session: the **Event
Log** stays write-always (`ADR-023` unchanged — anything attested or
not, authorized or not, still gets appended, always), but the
**authoritative current-state view** should only ever reflect
data that's actually been authorized/approved. Not-yet-approved data
should still be visible somewhere — a "live," explicitly
non-authoritative view, for monitoring use cases where seeing something
immediately (even unconfirmed) is more valuable than waiting for review
— but that view must make its own untrustworthiness obvious, not just
carry a quiet flag a caller could ignore.

`ADR-035`'s trust axis is also broader than its original framing
suggests: the two concrete triggers named this session are (1) a
submitter who wasn't authorized to capture this data in the first place
(the identity/permission case `ADR-035` already covered), and (2) an
automated detector that thinks it has found a pattern but whose result
hasn't been validated yet (a content/confidence case, not an identity
one). Both are the same underlying question — "should this claim be
trusted as-is yet" — just triggered for different reasons. `AuthorityStatus`
already models exactly this question generically; this ADR doesn't add
a second field for the second trigger, it broadens what's understood to
set the first one.

Decision:
- **`AuthorityStatus` now defaults to `accepted`** for an ordinary,
  already-authenticated publish — `ADR-006`'s bearer-token auth already
  establishes both identity and permission synchronously, so there's
  nothing left to review. It only starts at `unattested`/`pending_review`
  when the publish itself declares a reason not to trust it yet:
  carrying `AttestedClaims` (self-attested credentials, `ADR-036`), **or**
  an explicit review-pending marker any caller can set on publish — the
  mechanism a detector service uses to declare its own output an
  unconfirmed pattern match, not yet validated by a second pass or a
  human. Domain-specific detail about *why* something is pending (a
  confidence score, which rule flagged it) rides inside the existing
  free-form `AttestedClaims` JSON — no new schema field, reusing the one
  that already exists for exactly this "structured, extensible claim"
  purpose.
- **The Entity Store (`ADR-021`) only folds an event once `AuthorityStatus`
  reaches `accepted`.** An event sitting at `unattested`/`pending_review`
  is fully persisted in the Event Log — queryable there, replicated
  (`ADR-033`), never blocked or delayed at capture time — but does
  **not** yet update the authoritative Entity Store's `Data`/`Version`.
  ~~An `unattested` event is folded into the Entity Store, replicated
  (`ADR-033`), and queryable — identically to an accepted one; the only
  difference is a label.~~ **Superseded by this ADR.**
- **A new Live View reflects every event immediately, gate or no gate.**
  A second, framework-level, always-on materialized view (`LiveEntityStoreRow`,
  `docs/data/entity-store.md`) folds every event the moment it's received
  — the same fold mechanism the authoritative Entity Store already uses,
  just without the `AuthorityStatus` gate. This is the "best current
  guess, including not-yet-approved data" picture for live monitoring —
  genuinely useful the moment approval can lag capture by any real
  amount of time, which is exactly `ADR-035`'s original scenario (a field
  actor working offline).
- **Explicitly, structurally non-authoritative — a wrapper, not a
  quiet flag.** Every response from the Live View surface carries a
  top-level `isAuthoritative: false` marker plus the underlying
  `AuthorityStatus` value, at the granularity of the **whole row/view**,
  not a per-field wrapper. This is a different granularity from `ADR-009`'s
  masking wrapper (which redacts individual field *values*) — worth
  disambiguating explicitly, per this project's own convention, rather
  than treating both as "the same wrapper idea." A caller reading the
  authoritative Entity Store never sees this marker at all — only the
  Live View surface carries it, so there's no ambiguous middle ground
  where a caller has to check a flag to know which view it's looking at.
- **Once approved, the authoritative Entity Store catches up** — a
  background reconciliation applies the now-`accepted` event to the
  authoritative Entity Store, the same "apply once, on the triggering
  condition" shape `ADR-027`'s materialization catch-up already uses,
  not a new mechanism. **Rejected events never reach the authoritative
  Entity Store at all** — they simply never satisfied the gate. They
  remain visible in the Event Log and the Live View (now labeled
  `rejected`, still never deleted), consistent with this design's
  governing "never lose or corrupt data" principle.
- **This narrows, not replaces, `ADR-035`'s annotate-only vs.
  compensating-patch fork** (`docs/comparisons/authority-rejection-behavior.md`):
  since a rejected event was, by construction, never folded into the
  authoritative store in the first place, there's nothing to compensate
  for in the common case. `RejectionBehavior` still matters for the
  narrower, real residual case — an event already `accepted` and folded,
  later *re-reviewed* and reversed to `rejected` — where `Compensate`
  genuinely still means "undo the effect," same as before.

**Prior art, composed rather than invented wholesale**:
- **Write-Audit-Publish (WAP)**, the data-engineering pattern Netflix
  popularized for Apache Iceberg (write to isolation, audit against
  quality/trust rules, publish to production only once it passes): this
  ADR's shape is exactly Write (the Event Log, always) → Audit
  (`AuthorityStatus`'s review lifecycle) → Publish (the gated fold into
  the authoritative Entity Store).
- **Quarantine pattern** ([Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/patterns/quarantine)):
  "a security measure, which consists of a series of checkpoints that
  are employed before an artifact is consumed... transitions from an
  untrusted status to a trusted status." **Deliberately not applied as
  written**: Quarantine's default is to *block* the untrusted artifact
  from consumption entirely until trusted; this design instead makes the
  untrusted data visible immediately, through a separate, clearly-labeled
  surface — a considered deviation, not an oversight, because the whole
  point here is that seeing unconfirmed data has real value for live
  monitoring, unlike a supply-chain artifact nobody should touch yet.
- **Dual-balance ledgers** (e.g. [Modern Treasury — pending vs. posted balances](https://www.moderntreasury.com/journal/how-to-think-about-ledger-balances)):
  "posted_balance" (settled, authoritative) vs. "pending_balance"
  (pending + posted — a superset, the "expect to see once everything
  settles" view) is the same shape as this design's authoritative Entity
  Store vs. Live View, in a well-known, real-world domain — cited here
  as a concrete, relatable analogy, not as a mechanism this design
  adopts directly.

Consequences:
- **`ExpectedVersion`/`ConflictFlag` (`ADR-024`) apply to the
  authoritative Entity Store's `Version` only.** A client that read the
  Live View and built an `ExpectedVersion` off of it could find that
  version never existed on the authoritative side (Live View folds
  events the authoritative side hasn't caught up to yet, or ever will,
  if later rejected) — the two views need clearly different, clearly
  documented consistency semantics; not resolved further here, flagged
  for whoever builds this.
- **Two materialized views over the same event stream, doubling
  fold/storage/rebuild cost** — an accepted trade for the transparency
  requirement, consistent with [CQRS & Materialized
  Views](../patterns/cqrs-and-materialized-views.md)'s own stated cost
  ("every materialized view is a second thing that must handle replay,
  checkpointing, and rebuild correctly").
- `docs/data/entity-store.md` needs `LiveEntityStoreRow` added alongside
  `EntityStoreRow`, and `EntityStoreRow`'s fold description revised to
  state the new gate explicitly — done this pass.
- `docs/data/event-log.md`'s `AuthorityStatus` default needs to flip
  from `"unattested"` to `"accepted"` — done this pass.
- `docs/features/non-authoritative-capture.md`'s existing scenario
  asserting an unattested event "folded into the Entity Store exactly as
  an accepted event would be" is now wrong and needs revising — done
  this pass.
- `01-c4-architecture.md` needs a Live View component alongside the
  Entity Store's — not done this pass, flagged as outstanding
  propagation work (`CLAUDE.md`).

**Compliance note** (a proving-ground compliance review, this session):
making not-yet-approved data visible-but-labeled rather than silently
withheld or discarded is precisely what FDA's "Data Integrity and
Compliance With Drug CGMP: Questions and Answers" guidance requires
under its ALCOA+ "Complete" principle — all results, including ones
later rejected, must remain retained and reviewable, never quietly
excluded from the record a regulator or auditor could inspect.
