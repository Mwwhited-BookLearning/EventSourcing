# Project Context (session handoff)

**This is a snapshot, not a log — overwrite it in place each session,
don't append to it.** History lives in `docs/changes/{date}.md`; open
forks live in `docs/10-open-questions.md`; active doc-tracker tasks live
in `TODO.md`; active *implementation* status lives in
`docs/08-build-plan.md`'s "Implementation status" table. This file exists
so a fresh agent (or a human) can resume from the repo alone, without
replaying git-log archaeology or losing information the way an earlier,
unresumable conversation did. See `.claude/protocols/context-handoff.md`
for the update rules — in particular, narrative history of *completed*
work belongs in `docs/changes/{date}.md`, not here; this file drifted
into a multi-thousand-line duplicate of that history once already
(purged 2026-08-13, direct request — "move the important content to the
correct files... reference those files and purge the garbage"). Don't
let it happen again: if you're about to write more than a sentence or
two about *what got built*, that sentence belongs in today's
`docs/changes/{date}.md` instead, with only a pointer left here.

## What this project is

`EventSourcing` (repo name is a known typo for `EventSourcing` — see
`CLAUDE.md`, deliberately not yet renamed) is a from-scratch design
**and, since 2026-08-03, a real implementation** for an event-sourcing
store ("Duplex," `docs/naming.md`), built as a **worked teaching
example**: append-only write side (schema registry, publish/follow/
lineage APIs, GraphQL-only query layer), a CQRS read side, and two fully
worked proving-ground domains (clinical trials + device telemetry —
"Vitals"; digital identity/KYC — "Meridian"). Governing principle: never
lose or corrupt data.

## Current state

*(as of 2026-10-09, branch `dev/apphost-load-test`, pushed; PRs are the user's; see `docs/changes/2026-10-09.md`. Update this whole section, don't just bump the date)*

- **Build plan:** every item in `docs/08-build-plan.md` is Done except
  item 55 (ORE range index — **Removed**, moved to
  `spikes/order-revealing-encryption/`) and item 56 (native in-database
  predicate evaluators — **not adopted**, moved to
  `spikes/in-database-native-predicate-evaluators/`; the seam and its
  app-tier default are Done). Authoritative table is in that file.
- **ADRs:** 108 (`ADR-001`–`ADR-108`), all Accepted. Newest: `ADR-104`–`107`
  (live UCAN revocation, app-scoped RBAC, DevIdp scope, delegation-issuance
  audit event) and `ADR-108` (central NuGet package versioning,
  `Directory.Packages.props`).
- **Open forks:** `docs/10-open-questions.md` row 1 only (write-side
  orchestration engine) — back-burnered until a post-build design phase.
- **Last work:** `docs/changes/2026-10-09.md` (AppHost load run, wake-signal, Router, indexes, pool cap).

## Actively in flight

Nothing in flight in the build plan. The AppHost load-run work (branch
`dev/apphost-load-test`, narrative in `docs/changes/2026-10-09.md`) is
finished except one open bug:
`docs/bugs/framework/service/aspire-restart-leaves-stale-containers-and-dead-ports.md`
(Aspire's DCP intermittently leaves containers in `Created` or reports
projects Running with no process or listener; `scripts/aspire-clean.ps1`
helps only sometimes; needs an upstream report). Not re-confirmed: entity
routing on all three providers in a clean AppHost load run (the integration
provider e2e suite covers Postgres and SQL Server). `TODO.md` has one
doc-debt item (re-sweep of `docs/06-solution-structure.md`'s project tree).
Ask the user what is next rather than assume more work exists.

## How to resume cold

1. Read `CLAUDE.md` (standing conventions + doc-type index), then this
   file.
2. `docs/08-build-plan.md`'s "Implementation status" table, `TODO.md`,
   `docs/10-open-questions.md` — confirm they still match what this file
   claims above; if not, something changed without this file being
   updated (fix that first).
3. `git log --oneline -10` and `git status`.
4. Skim the latest `docs/changes/{date}.md` for the most recent session's
   narrative.
5. `dotnet build EventStore.slnx` and `dotnet test tests/EventStore.
   IntegrationTests` (needs Docker running for Testcontainers-backed
   PostgreSQL/SQL Server tests, and the SDK pinned in `global.json`). A
   full multi-provider run has known, pre-existing, unrelated
   load-induced flakiness under MSTest's parallelism (SQL Server/SQLite
   test classes occasionally failing container/file-cleanup races under
   host contention) — re-run the specific failing class alone before
   assuming a real regression; see this file's own "purged" note above
   for where the fuller flakiness history now lives
   (`docs/changes/{date}.md`, not here).
6. `client-web/`: `npm test` (`vitest`), `npm run build`, and `npm run
   build:offline-player`.

## Working notes not yet written down elsewhere

- **The user wants to be asked before large, effort-heavy content
  rewrites get started unilaterally — offer explicit options, don't just
  do it.** Smaller, unambiguous fixes (broken links, typos, a stale
  field name, a wrong library choice found mid-task) are fine to fix
  directly without asking first.
