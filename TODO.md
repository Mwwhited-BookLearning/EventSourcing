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

- **`EventStore.AppHost`'s Postgres database resource doesn't reliably
  finish auto-creating before `EventStore.Host.Postgres`'s first
  connection attempt.** `Aspire.Hosting.PostgreSql` 13.4.6's `AddDatabase
  ("Postgres")` documents that "the database being created on the
  Postgres server ... happens automatically as part of the resource
  lifecycle," but running `aspire run` against a clean checkout
  (`src/EventStore.AppHost`) repeatedly showed Postgres itself become
  ready, then a single `FATAL: database "Postgres" does not exist` with
  no further retry/creation logged, and `EventStore.Host.Postgres` never
  starts. `WaitFor(db)` on the database resource (already present in
  `AppHost.cs`) didn't close this gap. Everything else about the Auth
  item's live-orchestration verification checked out (real token issuance
  from a live `EventStore.DevIdp`, `.WithDataVolume()` + a stable
  persisted password surviving restarts, `RequireHttpsMetadata`/
  `Authority` env-var injection all correct) — this is narrowly about the
  database resource's own creation timing. Investigate: an explicit
  `.WithCreationScript(...)`, a longer/explicit health-check retry
  before the dependent resource's first connection, or filing/checking
  an upstream Aspire issue. See `docs/08-build-plan.md`'s "Auth
  (OIDC/OpenIddict) + Orchestration" section's own note for the full
  list of *other* real orchestration bugs this same pass found and
  fixed.
