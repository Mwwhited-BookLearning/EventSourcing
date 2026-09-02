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

- [ ] **A generic demo identity still can't publish a real Vitals/Meridian
  business event over HTTP** — the narrower, still-genuinely-open half of
  the "Dispatch a command" demo-panel gap (`docs/changes/2026-09-02.md`
  closed the OTHER half, the field-casing gap, for real). No DevIdp-seeded
  HTTP client anywhere holds the specific `RequiredClaims` any real
  business event type demands (e.g. `PatientScreened`'s `patient:enroll`,
  `VitalsWorkflowA.cs`) — those events have only ever been created
  in-process by `Samples.Vitals.Seed`/`Simulator` calling `PublishService`
  directly, bypassing the HTTP auth layer's claim check entirely. This is
  a real security-policy decision, not a technical gap: either (a) grant a
  narrow, explicitly-labeled "demo:dispatch"-style claim per domain to a
  shared demo identity (weakens this project's own "one identity per real
  capability need" convention, `DevIdpSeeder.cs`), or (b) retire the
  generic cross-domain panel in favor of a per-domain demo action that
  already speaks the right claim (matching how Vitals/Meridian's own Queue
  screens already work) — deliberately left undecided here rather than
  picked unilaterally. In the meantime the *symptom* is fixed: a rejection
  now marks the outbox entry `Failed` (terminal, visible in the UI) instead
  of retrying forever silently, with no signal anything is wrong
  (`useOutboxStore.flush`'s new `permanentFailure` handling,
  `docs/changes/2026-09-02.md`).
