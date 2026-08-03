# Project Context (session handoff)

**This is a snapshot, not a log — overwrite it in place each session,
don't append to it.** History lives in `docs/changes/{date}.md`; open
forks live in `docs/10-open-questions.md`; active doc-tracker tasks live
in `TODO.md`; active *implementation* status lives in
`docs/08-build-plan.md`'s "Implementation status" table. This file exists
so a fresh agent (or a human) can resume from the repo alone, without
replaying git-log archaeology or losing information the way an earlier,
unresumable conversation did. See `.claude/protocols/context-handoff.md`
for the update rules.

## What this project is

`EventSourcing` (repo name is a known typo for `EventSourcing` — see
`CLAUDE.md`, deliberately not yet renamed) is a from-scratch design **and,
as of this session, a real in-progress implementation** for an
event-sourcing store ("Duplex," `docs/naming.md`), built as a **worked
teaching example**: append-only write side (schema registry, publish/
follow/lineage APIs), a CQRS read side, and two fully worked proving-ground
domains (clinical trials + device telemetry — "Vitals"; digital identity/
KYC — "Meridian"). Governing principle: never lose or corrupt data. All 93
ADRs (`docs/adrs/adr-001` through `adr-093`) are Accepted — the *decisions*
are done; the remaining work is (a) keeping ~150 docs internally consistent
with no compiler to catch drift, and (b) building the real `src/`/`tests/`
tree in `docs/08-build-plan.md`'s dependency order, checking off that
file's status table one item at a time.

## Current state

*(update this section's content, not just its presence, every session —
stale numbers here are worse than none)*

- As of **2026-08-03**: `TODO.md`'s Active section and
  `docs/10-open-questions.md` are both EMPTY — the last remaining 9
  doc-only items (i18n/l10n, RFC3161Timestamp, RBAC reserved-event
  Gherkin, streaming `ThreadId`/`RedactedRange`, `LateArrivalFlag`, and 3
  new feature docs: `dpop-and-tamper-evidence.md`,
  `upcast-materialization-and-downcast.md`,
  `compatibility-and-versioning.md`) were closed out and `08-build-plan.md`
  consolidated to point at them. Full narrative in
  `docs/changes/2026-08-03.md`.
- **Implementation started this same session** (direct request: "start
  converting the build plan to your active TODO... let's do this").
  `docs/08-build-plan.md` gained a new "Implementation status" table near
  its top — **that table, not this file, is the authoritative tracker of
  which of the 48 items is Done/In progress/Not started.** Don't restate
  its contents here; it drifts the moment an item's status changes.
- **Item 1, "Scaffolding & Persistence," is Done** — the first real code
  in this repo. `EventStore.slnx` at repo root, `src/` (10 projects:
  `EventStore.Domain`, `EventStore.Persistence` + its `IJsonPathTranslator`
  placeholder interface/3 stub impls, 3 `.Persistence.Migrations.<Provider>`
  projects, `EventStore.Host.Core`, 3 `EventStore.Host.<Provider>`
  deployables) and `tests/EventStore.IntegrationTests` (MSTest). Full
  `EventStoreContext` model built now per that item's own scope
  (`EventTypeDefinition`/`FilterableField`/`StoredEvent`/`EventParent`,
  matching `docs/data/schema-registry.md`/`event-log.md` exactly).
  Migrations generated and **actually verified applying** on all three
  providers (SQLite file-based, Postgres/SQL Server via real Testcontainers
  — Docker is available in this dev environment) via a live insert-and-
  read-back integration test, not just "migration files exist." `global.json`
  pins the SDK (`10.0.302`); target framework is `net10.0`.
- **A real data-model doc gap found and fixed while implementing**:
  `docs/data/schema-registry.md`'s `FilterableField` class was missing
  `EventTypeAppId` — present in the feature doc's ER diagram and required
  by `ADR-030`, but never propagated to the data-model doc itself. Fixed
  in place (this is exactly the kind of drift `CLAUDE.md`'s "verify before
  citing"/data-model-authority rules exist to catch — expect more of these
  once code starts actually needing a doc's shape to be literally correct,
  not just internally consistent prose).
- **Next up**: item 2, "Schema Registry" (`docs/08-build-plan.md`) — now
  unblocked. Nothing else is in flight.

## How to resume cold

1. Read `CLAUDE.md` (standing conventions + doc-type index — now also
   describes the repo as having real `src/`/`tests/`, not "no src/ yet").
2. Read this file, then `docs/08-build-plan.md`'s "Implementation status"
   table (what's actually built vs. not), `TODO.md` (active doc work, will
   usually be empty), and `docs/10-open-questions.md` (open design forks).
3. `git log --oneline -10` and `git status` — confirm this file's
   "Current state" section still matches reality; if it doesn't, something
   changed without this file being updated (fix that first).
4. Skim the latest `docs/changes/{date}.md` for the most recent session's
   narrative.
5. `dotnet build EventStore.slnx` and `dotnet test tests/EventStore.IntegrationTests` —
   confirm the build/test baseline the last session left still holds
   before adding to it. Requires Docker running (Testcontainers for
   Postgres/SQL Server) and the SDK pinned in `global.json`.

## Working notes not yet written down elsewhere

- The user wants to be asked before large, effort-heavy content rewrites
  get started unilaterally — offer explicit options, don't just do it.
  Smaller, unambiguous fixes (broken links, typos, a stale field name
  found mid-task) are fine to fix directly without asking first — the
  `FilterableField.EventTypeAppId` fix above is exactly that kind of
  fix, done in place without a check-in.
- **Implementation pacing wasn't explicitly specified** — "let's do this"
  authorized starting, not a specific cadence (one item per session? push
  until blocked?). This session did exactly one item (Scaffolding &
  Persistence) end-to-end, verified, then stopped to report back rather
  than ploughing into item 2 unprompted. Treat that as the working default
  — finish and verify one build-plan item, report, let the next
  instruction set the pace — until told otherwise.
- **`docs/06-solution-structure.md`'s code sketches are "concept accurate,
  exact wiring unverified" by its own banner** — confirmed true in
  practice: `EventStore.Host.Core` needed a `FrameworkReference` to
  `Microsoft.AspNetCore.App` (the doc's sketch doesn't show csproj-level
  detail at all), and `IJsonPathTranslator`'s real method shape still
  doesn't exist anywhere — a deliberate placeholder now, to be designed
  for real only when "Follow API + Filter Pushdown" lands. Don't treat
  today's placeholder shape as a decision that item is bound to.
- A full repo-wide doc staleness review beyond what ADR-focused passes
  have covered is still genuinely open, unscheduled — likely a
  `parallel-batch-dispatch.md`-shaped job whenever picked up. Now doubly
  relevant since code will keep surfacing more data-model drift the way
  `FilterableField` just did.
