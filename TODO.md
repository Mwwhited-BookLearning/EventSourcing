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

- [ ] **Extend UI-playbook coverage beyond Vitals' Workflow A.** The
  screenshot-to-markdown mechanism itself is built and proven this
  session (`ADR-055`'s "Implementation note," `tests/EventStore.
  E2ETests/PlaybookRecorder.cs`, `docs/playbooks/README.md`'s catalog) —
  only one workflow is actually recorded so far. Add one
  `[TestMethod]` per remaining workflow (Vitals' B–D, Meridian's A–C, or
  a core-engine `docs/features/*.md` walkthrough), reusing
  `PlaybookRecorder` unchanged, and add a matching row to `docs/
  playbooks/README.md`'s catalog in the same pass each one lands.
- [ ] **Doc/reality drift found in passing, not yet fixed**: `docs/06-
  solution-structure.md`'s `tests/` layout still describes
  `EventStore.UnitTests/` as "NEVER BUILT as its own project... every
  test in this solution lives in the ONE project below [
  `EventStore.IntegrationTests`] instead" — but `tests/EventStore.
  UnitTests/` genuinely exists and has real tests (17, as of this
  session's own `CloudSearchIndexKeyStoreAdapterTests`/
  `OrderRevealingEncryptionTests`). Needs reconciling: either that
  doc's own claim is stale and should be corrected, or there's a real,
  undocumented split in what belongs in which project that should be
  stated explicitly instead of contradicted by the file tree itself.
