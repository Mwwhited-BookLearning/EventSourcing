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


The five-phase design-review program (missing-documents sweep, full ADR
review, proving-ground domain review, cross-domain-to-framework review,
architecture/design compliance guideline) plus Phase 5 (linting/static-
analysis tooling) are all **done** — per this file's own workflow,
deleted from here rather than kept as completion narratives; the full
account of each is in `docs/changes/2026-09-02.md` (Phase 0) and
`docs/changes/2026-09-03.md` (Phase 1 onward — split across the two
files since work crossed a real midnight boundary mid-session).

## Concurrent load run of the real AppHost (all three providers)

Why: both real bugs found this session (SQL Server append deadlock,
concurrent first-registration PK race) only appeared under concurrent
load, and the earlier full AppHost run only drove the sample simulators.
The Postgres wake queue (`ADR-095`, `docs/patterns/durable-wake-queue.md`)
and the SQL Server `sp_getapplock` append path haven't been pushed hard
through the real orchestration layer yet. Do these in order; the
`aspire` CLI is at `~/.aspire/bin` (`aspire start/describe/logs/stop
--apphost src/EventStore.AppHost --non-interactive --nologo`).

- [ ] 1. Start the AppHost with the Postgres, SQL Server and SQLite hosts
  all up; confirm via `aspire describe` that every resource is healthy.
- [ ] 2. Write a reusable load script (committed, e.g. under `scripts/`):
  for each host, a burst of a few thousand concurrent publishes across
  several event types, with registrations of new event types mixed in.
  Auth via DevIdp client_credentials + DPoP as the e2e suite does
  (`AuthScenarioAssertions`).
- [ ] 3. Check the results: every publish returns 202 or an expected 409;
  `/events/verify?throughSequenceNumber=9223372036854775807` reports
  `verified: true` on each host; every routed entity appears in GraphQL
  (`entity_{appId}_{entitytype}`) within a bounded time.
- [ ] 4. Read the real service logs (`aspire logs`, not just pass/fail
  counts) for deadlocks, retry exhaustion, wake-queue errors, or
  unexpected 5xx.
- [ ] 5. File anything real as `docs/bugs/framework/{tier}/...md` and fix
  it, per `.claude/protocols/bug-report-tracking.md`. If clean, record
  that in `docs/changes/{date}.md` and keep the script.
