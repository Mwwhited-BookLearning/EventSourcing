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

*(as of 2026-08-27, branch `dev/cryptoshredding` off `main` (HEAD
`4d41393` when branched) — update this whole section, don't just bump
the date)*

All 95 pre-existing ADRs Accepted; `ADR-096`/`097`/`098` added this
session (also Accepted — design work, not proposals). `08-build-plan.md`
now has 56 items: the original 53 Done, three new ones (54–56) Not
started. `TODO.md` and `docs/10-open-questions.md` are both still empty
— nothing from this session belongs in either (see this session's own
`docs/changes/2026-08-27.md`).

This session's real work — full narrative in
`docs/changes/2026-08-27.md`, not repeated here: designed searchable
equality/range queries over `ADR-057`'s crypto-shredded fields — a
comparison doc, three ADRs (`096` blind index + bucketed range, `097`
opt-in Order-Revealing Encryption, `098` an in-database native
predicate-evaluator seam, designed not built), and every supporting doc
this repo's own conventions require in the same pass (data model,
patterns, references, glossary, extensibility points, build plan).
**Docs only — no code.** Two research findings shaped the design:
`EnvelopeAesGcm`'s already-deterministic nonce gives free intra-entity
equality but nothing cross-entity; the property-preserving-encryption
leakage-abuse attack (Naveed/Kamara/Wright, CCS 2015) recovers exact
plaintext for low-cardinality fields specifically (verified directly
this session after the user asked whether a name is as recoverable as a
birthdate — it isn't, which is why `ADR-096`'s guardrail is
cardinality-aware, not a blanket classification rule).

## Actively in flight

`TODO.md` is empty. The real next step is the implementation pass
against `08-build-plan.md` items 54–56 (Searchable Blind-Index &
Bucketed-Range Indexes → Order-Revealing Encryption Range Index → 
In-Database Native Predicate Evaluator Seam) — not started, and a fresh
session should confirm with the user before starting the code rather
than assuming it's next. `dev/cryptoshredding` is not yet merged to
`main` or opened as a PR.

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
