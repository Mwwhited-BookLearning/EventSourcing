# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

Every item previously tracked here (Naive UI/Vue Router shell,
`style-guide.md`, playbook diagrams/restructure/new playbooks/READMEs,
paged entity-list data grids, configurable-presentation-type charting,
JSON Schema field/dependent-field validation, calculated fields, the
PlantUML `.puml`/Docker-render migration) is done, per the workflow
above: deleted from this file, full narrative in
[`docs/changes/2026-08-28.md`](docs/changes/2026-08-28.md) and
[`docs/changes/2026-08-29.md`](docs/changes/2026-08-29.md).

(The "DSL for user flows/validations/approvals" ask was moved to
[`docs/10-open-questions.md`](docs/10-open-questions.md) row 1, not kept
here — a genuinely undecided fork, not decided work with only the doing
left.)

- [ ] **`ADR-104`'s own live revocation check for delegated UCAN grants was
  never actually built — design only, discovered while building `ADR-107`
  (issuance audit event).** `ADR-104`'s Decision text says plainly "this
  is a design decision only — no code changes this pass," confirmed by a
  direct code search: no `UcanDelegationRevoked` type exists anywhere in
  `src/`, and `UcanValidator.ValidateAsync` (`EventStore.Ucan`) has no
  revocation check of any kind. The "grants should be validated on the
  server time to check for a revocation" decision from earlier this
  session is still not running code — a delegation today remains valid
  until its own `exp` claim passes, full stop, exactly the pre-`ADR-104`
  behavior. Needs: the `UcanDelegationRevoked` event type (mirroring
  `ADR-107`'s own `ucanDelegationIssued` shape/conventions), a real
  revoke endpoint/call path a granter uses, and the actual live query
  added at `UcanValidator.ValidateAsync`'s own choke point per `ADR-104`'s
  design.

The five-phase design-review program (missing-documents sweep, full ADR
review, proving-ground domain review, cross-domain-to-framework review,
architecture/design compliance guideline) plus Phase 5 (linting/static-
analysis tooling) are all **done** — per this file's own workflow,
deleted from here rather than kept as completion narratives; the full
account of each is in `docs/changes/2026-09-02.md` (Phase 0) and
`docs/changes/2026-09-03.md` (Phase 1 onward — split across the two
files since work crossed a real midnight boundary mid-session).

