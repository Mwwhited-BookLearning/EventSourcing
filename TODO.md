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

- [ ] **Write `docs/patterns/fault-injection-chaos-engineering.md`** —
  found while fixing `docs/bugs/framework/service/follow-client-faults-
  under-default-http-resilience-timeout.md`: `docs/references.md`'s own
  "Chaos Engineering" row has pointed at this file since `ADR-063`, but
  the file itself was never actually written — a pre-existing dangling
  reference, not introduced this pass. Matches `docs/patterns/README.md`'s
  own catalog-entry convention (general pattern first, cited, then which
  ADR applies it here — `ADR-063`, via the now-hand-rolled `FaultInjector`
  rather than `Polly`+`Simmy`, see `docs/libraries/dotnet/polly-simmy.md`).

- [ ] **`scripts/extract-diagrams.mjs`'s inserted diagram image reference
  doesn't survive an E2E-test-regenerated playbook doc** — found while
  verifying the Polly/RBAC fixes by actually running
  `VitalsWorkflowBAdverseEventPlaybookTests`/
  `MeridianWorkflowBRelyingPartyAccessPlaybookTests` against a real
  `AppHost`: regenerating a playbook doc (its own header's own encouraged,
  ordinary action — "re-run `dotnet test tests/EventStore.E2ETests` to
  regenerate") overwrites the whole file from `PlaybookRecorder`'s own
  output, silently dropping the `![diagram](...)` line
  `extract-diagrams.mjs` had inserted above the fenced PlantUML block
  — the two pipelines aren't coordinated. Worked around this time by
  re-running `node scripts/extract-diagrams.mjs` after the test run (its
  own idempotency guard restores the missing line safely); worth a real
  fix — either teach `PlaybookRecorder.WriteMarkdownAsync` to emit the
  image reference itself (mirroring the extraction script's own naming
  convention) so regeneration never drops it, or note in
  `docs/changes/{date}.md`/this file that re-running
  `extract-diagrams.mjs` is a required step after any E2E test run that
  touches a playbook doc, not just after hand-editing a diagram.