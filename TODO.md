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

- [ ] **UI-playbook coverage is capped by `ADR-039`'s one-event-type-
  per-instance model — needs new client instances, not more
  `[TestMethod]`s.** Extended this session from one playbook (Vitals'
  Workflow A) to three (adding Meridian's Workflow A, both feature
  docs) — `docs/playbooks/README.md`'s catalog. Confirmed by reading
  `client-web/packages/mvvm-client/src/api/subscriptionBuilder.ts`
  directly: a client instance's GraphQL subscription is fixed to one
  `(AppId, EventType)` pair at launch (`EventStore.AppHost/AppHost.cs`'s
  `VITE_EVENT_TYPE`/`VITE_ENTITY_TYPE` env vars), so an entity that
  never published that exact event type never reaches that instance's
  Browse cache no matter how long a REPLAY-mode subscription waits.
  Concretely blocked by this: Vitals' Workflows B–D (`client-web-vitals`
  is locked to `PatientScreened`/`patient` — `Device`/`AdverseEvent`/
  `IonmAlert` entities are unreachable from it) and Meridian's Workflow
  C (`SarFilingRecorded` needs a step-up-auth flow `Samples.Meridian.
  Seed` deliberately doesn't perform, so no SAR entity is ever
  published to browse). Meridian's Workflow B has a deeper gap still —
  no `MeridianWorkflowB.cs`/seed data exists in `Samples.Meridian` at
  all, so there's no relying-party-access sample to walk through yet,
  UI-reachable or not. Extending coverage further needs either a new
  Aspire-hosted `client-web-*` instance per additional event type to
  browse, or a real relying-party sample workflow built first — real
  infra/sample work, not `PlaybookRecorder` reuse.
