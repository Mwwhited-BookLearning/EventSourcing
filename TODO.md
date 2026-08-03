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

Five items found by an implementation-readiness survey (a grep for
"flagged as remaining propagation work"-shaped markers across every ADR,
since `TODO.md` only ever tracked what was explicitly logged, not
everything any ADR ever flagged). Independent of each other; the first
two are lightweight doc-diagram updates, the middle two are about the
same underlying gap (`ADR-040` ticket exchange), the last is genuinely
out of this repo's own scope.

- [ ] **`01-c4-architecture.md`'s GraphQL resolver diagram never got
  `ADR-068`'s lineage-export/bitemporal-playback resolver nodes added** —
  flagged in `ADR-068`'s own Consequences, never done.
- [ ] **`06-solution-structure.md`'s project layout never got `ADR-072`'s
  MLLP-listener component or `ADR-068`'s offline-player build target
  added** — both flagged in their own ADRs' Consequences, neither done.
- [ ] **`ADR-093` assumed the ticket-exchange shared secret needs a
  persisted current+previous entity "since it doesn't yet have one"** —
  worth checking against `ADR-040`'s own Consequences first (the shared
  secret is either the caller's already-registered `client_secret`,
  DevIdp-side/OAuth2 state outside `EventStoreContext` entirely per
  `auth.md`'s established convention, or a caller-generated
  `one_time_secret` with no persistence at all by design) before adding a
  phantom entity that may not belong in this design's own data model.
- [ ] **`docs/features/*.md` has zero coverage for `ADR-040` (ticket
  exchange)** — only `docs/patterns/ticket-exchange-headerless-clients.md`
  exists; predates the `ADR-054`+ feature-doc backfill so was never in
  that batch's scope.
- [ ] **`docker-compose.yml` (assumed to exist by `ADR-076`, to sequence a
  migration-bundle-apply step ahead of it) doesn't exist anywhere in the
  repo** — expected for a docs-only repo with no `src/` yet; not a doc gap
  to fix, just worth tracking so it isn't mistaken for done. Closes only
  once real implementation starts.
