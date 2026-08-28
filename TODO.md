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

## Active

- [ ] **Three UI-playbook gaps remain, each needing real sample-data/
  infra work first, not just a new `[TestMethod]`.** Extended this
  session from one playbook (Vitals' Workflow A) to five — Meridian's
  Workflow A (both feature docs), plus Vitals' Workflows B (upstream
  half) and D, the latter two unblocked by adding `client-web-vitals-
  device`/`client-web-vitals-ionmalert` to `EventStore.AppHost`
  (`ADR-039`'s one-event-type-per-instance model meant the original
  `client-web-vitals` instance, locked to `PatientScreened`, could never
  Browse those entities — confirmed by reading `subscriptionBuilder.ts`
  directly). `docs/playbooks/README.md`'s catalog has all five. Still
  open:
  1. **Vitals' Workflow B downstream half** (Adverse Event Capture and
     Review) — `Samples.Vitals.Seed` never publishes an event that
     creates an `AdverseEvent` entity at all, so a new client instance
     alone wouldn't be enough; needs a seed event added first.
  2. **Meridian's Workflow B** (Relying-Party Verification Request) —
     no `MeridianWorkflowB.cs`/seed data exists in `Samples.Meridian`
     at all; needs a real sample workflow built, not just a client
     instance.
  3. **Meridian's Workflow C** (Periodic Screening and SAR Escalation)
     — `SarFilingRecorded` needs a step-up-auth flow `Samples.Meridian.
     Seed` deliberately doesn't perform, so no SAR entity is ever
     published to browse.
