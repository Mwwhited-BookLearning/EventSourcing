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

*(as of 2026-08-27, branch `dev/cryptoshredding` off `main` — update this
whole section, don't just bump the date)*

All 95 pre-existing ADRs Accepted; `ADR-096`/`097`/`098` added this
session. `08-build-plan.md` now has 56 items: the original 53 Done, item
54 Done, item 55 built but explicitly not marked Done (its own exit
criteria require a dedicated security review not performed this
session), item 56 Not started. `TODO.md` and `docs/10-open-questions.md`
are both still empty.

This session (full narrative split across two passes in
`docs/changes/2026-08-27.md`): first designed searchable equality/range
queries over `ADR-057`'s crypto-shredded fields (comparison doc + `ADR-
096`/`097`/`098` + every supporting doc), then — direct request, same
day — built and tested items 54 and most of 55 against that design.
**Real prerequisite bug found and fixed**: `PayloadEncryptor` (`ADR-057`'s
own encryption) was never registered in any Host's DI before this
session — classified-field encryption was inert in production,
exercised only by test code, until this pass's `AddErasure` fix. New
code: `src/EventStore.Domain/SchemaRegistry/{SearchableIndexConfig,
EncryptedFieldIndexEntry,SearchIndexKey}.cs`, `src/EventStore.
Abstractions/{ISearchIndexKeyStore,IEncryptedPredicateEvaluator}.cs`,
`src/EventStore.Erasure/{LocalSearchIndexKeyStore,SearchIndexKeyService,
PayloadIndexer,RangeBucketing,OrderRevealingEncryption,
AppTierEncryptedPredicateEvaluator}.cs`, plus
`GraphQlFilterPredicateBuilder.Build` becoming async to route encrypted-
field clauses. Migrated across all three providers. Verified: new
`OrderRevealingEncryptionTests` (unit) and `SearchableEncryptionSqliteTests`
(integration), plus the full pre-existing Sqlite suite (150/150) and
`ErasurePostgresTests`/`ErasureSqlServerTests` against real Testcontainers
Postgres/SQL Server — no regressions found.

## Actively in flight

`TODO.md` is empty. The real next step is item 56 (In-Database Native
Predicate Evaluator Seam — a SQL Server SQLCLR assembly and a PostgreSQL
native function, explicitly scoped as separate, optional sub-items) —
Not started, deliberately not rushed into the same session as 54/55
given the real infrastructure setup involved. Item 55's own required
security review is also still outstanding. `dev/cryptoshredding` is not
yet merged to `main` or opened as a PR — a fresh session should confirm
with the user before assuming either is wanted.

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
