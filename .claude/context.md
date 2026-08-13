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

*(as of 2026-08-13, branch `fix/postgres-retry-strategy-and-otel-metrics`,
HEAD `39f2bd7` — update this whole section, don't just bump the date)*

All 95 ADRs Accepted; all 52 `docs/08-build-plan.md` items Done. `TODO.md`
and `docs/10-open-questions.md` are both empty. `EventStore.
IntegrationTests` has 231 `[TestMethod]`s (`grep -rc "\[TestMethod\]"
tests/EventStore.IntegrationTests/*.cs | awk -F: '{sum+=$2} END
{print sum}'` — the reliable way to recheck, don't hand-thread a running
tally through prose).

This session's real work — full narrative in `docs/changes/2026-08-13.md`,
not repeated here:
- Three real Postgres bugs, found by actually running the AppHost under
  concurrent write load, fixed in sequence (each one blocked the next
  from being reachable): `EventAppender`/`AccessLogAppender`/
  `SchemaRegistryService` calling `BeginTransactionAsync` directly,
  incompatible with `EnableRetryOnFailure`; the obvious fix silently
  corrupting the hash chain under real retries (a shared entity reused
  across attempts); and an insufficient `maxRetryCount` under sustained
  load. Regression-tested in `RetryOnFailurePostgresTests.cs`.
- OTel metrics extended beyond `ADR-088`'s original four mechanisms
  (publish, GraphQL, derivation, archival, simulators, process) — see
  that ADR's own "Extended, 2026-08-13" note.
- A real `client-web`/`client-web-vitals`/`client-web-meridian` bug: a
  missing `--` separator in nested `npm run --workspace=` scripts
  silently swallowed Aspire's `--port` flag, so every instance was
  always listening on the wrong port. Fixed in `client-web/package.json`.
- Four new Aspire dashboard links on the `eventstore` resource (Scalar
  UI, raw OpenAPI/AsyncAPI JSON, AsyncAPI viewer).
- `EventTailReader.TailAsync` (Follow/GraphQL Subscriptions) now ends
  the stream cleanly on client disconnect instead of throwing an
  unhandled `TaskCanceledException` — regression-tested in
  `FollowScenarioAssertions.
  DisconnectingMidTailEndsTheStreamGracefullyRatherThanThrowing`.
- This file itself, purged from ~2372 lines of duplicated build-item
  narrative down to this snapshot (protocol violation caught and fixed
  the same session it was noticed).

## Actively in flight

`TODO.md` is empty — nothing queued. The branch above is 5 commits ahead
of `main` (`f271b26`, `abbc3b4`, `e078b3f`, `786bc10`, `39f2bd7`), not yet
merged or opened as a PR — a fresh session should ask the user before
assuming that's the next step.

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
