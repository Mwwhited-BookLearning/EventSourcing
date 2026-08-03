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
  `docs/10-open-questions.md` are both EMPTY. Full doc-work narrative in
  `docs/changes/2026-08-03.md`.
- **Implementation started this same session** (direct request: "start
  converting the build plan to your active TODO... let's do this," then
  "keep going," then "Commit work and then keep going"). `docs/08-build-
  plan.md`'s "Implementation status" table (near its top) is the
  authoritative tracker of which of the 48 items is Done/In progress/Not
  started — don't restate its contents here.
- **Items 1, 2, and 3 are Done**: "Scaffolding & Persistence" (solution/
  project skeleton, full `EventStoreContext` model, migrations verified
  on all three providers), "Schema Registry" (`PUT`/`GET /registry/
  {event-type}`, a temporary pre-GraphQL `QUERY /registry` listing
  endpoint, structural validation, per-provider index DDL), and "Publish
  API" (`POST /publish/{event-type}` — this build stage's own pre-
  `ADR-023` semantics: synchronous blocking validation, `201`/`400`/`404`/
  `409`, not the later always-`202` posture; `EventStore.Inbox`), plus
  `/openapi.json` generation (`EventStore.SpecGeneration`, real
  `Microsoft.OpenApi` 3.9.0 usage, cache-invalidated on registration).
  `EventStore.IntegrationTests` has 9 passing tests across SQLite/
  PostgreSQL/SQL Server (Testcontainers for the latter two). Two commits
  so far on branch `dev/build-framework`: `5c5fd6e` (item 1) and `c30781e`
  (item 2); item 3 not yet committed as of this snapshot — commit it
  before trusting `git log` alone to reflect current state.
- **Two more real bugs/corrections found this item, same "run against
  every provider" discipline as item 2** (full narrative in
  `docs/changes/2026-08-03.md`, not repeated here): `IJsonPathTranslator`/
  `IFilterableFieldIndexDdlGenerator` needed no further changes, but a
  **new** per-provider seam, `IUniqueConstraintViolationDetector`, was
  added for ADR-011's concurrent-retry race (three real implementations
  in the `.Persistence.Migrations.<Provider>` projects, since only those
  reference each provider's actual ADO.NET exception types — not
  `EventStore.Persistence` itself, which stays provider-agnostic).
  `ChainHash`/`EntityId` are stored as empty-string placeholders for now
  (owned by "Hardening & Evolution"/"Entity-Centric Core Rebuild"
  respectively, not this item) — don't mistake that for a bug later.
- **A real, self-aware build-plan design** confirmed working as intended:
  "Publish API"'s own text already explicitly translates
  `features/publish-event.md`'s post-`ADR-023` `202`-always Gherkin into
  this item's pre-`ADR-023` `201`/`400`/`404`/`409` codes — unlike Schema
  Registry's exit-criteria overclaim (item 2), no correction was needed
  here; the build-plan had already anticipated this exact doc/build-stage
  gap. Good precedent: check for this kind of explicit self-aware
  translation note before assuming a doc/build-plan mismatch needs fixing.
- **Next up**: item 4, "Lineage API (read side)" — now unblocked
  (`Follow API + Filter Pushdown`, item 5, is independently unblocked
  too — both depend only on Publish API, can be done in either order).

## How to resume cold

1. Read `CLAUDE.md` (standing conventions + doc-type index).
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
   before adding to it (9 tests should pass). Requires Docker running
   (Testcontainers for Postgres/SQL Server) and the SDK pinned in
   `global.json`.

## Working notes not yet written down elsewhere

- The user wants to be asked before large, effort-heavy content rewrites
  get started unilaterally — offer explicit options, don't just do it.
  Smaller, unambiguous fixes (broken links, typos, a stale field name,
  a wrong library choice found mid-task) are fine to fix directly without
  asking first, as several were this session.
- **Implementation pacing**: "let's do this" started item 1; a plain
  "keep going" continued into item 2 with no further scoping needed;
  "Commit work and then keep going" both confirmed committing between
  items is welcome when asked and continued into item 3. Read "keep
  going" as "continue the same build-plan momentum, one item at a time,
  verified end to end before moving to the next" — not as license to
  skip running the tests, and not a one-time authorization that expires.
  **Commits happen only when the user actually says so** — this session
  did not commit unprompted between items 1 and 2; only did so once asked.
- **Always actually run new code against every provider it's built for
  before calling an item done.** Every real bug found this session (the
  `ExecuteSqlRawAsync` brace-parsing issue, an unquoted Postgres column,
  the JsonSchema.Net incompatibility) was caught by running tests against
  real engines, not by reading the code back. Item 3 shipped clean on the
  first run — likely *because* items 1/2's lessons (quoting, avoiding
  `ExecuteSqlRaw`, hand-written JSON checks) were already applied
  up front, not because the discipline stopped being necessary.
- **`docs/06-solution-structure.md`'s code sketches are "concept accurate,
  exact wiring unverified" by its own banner** — confirmed true a third
  time: every new web-facing project (`EventStore.Host.Core`,
  `.SchemaRegistry`, `.Inbox`, `.SpecGeneration`) has needed an explicit
  `FrameworkReference` to `Microsoft.AspNetCore.App` the doc's sketches
  never show. Expect this for every future project touching ASP.NET Core
  types from a plain class library.
- **Microsoft.OpenApi 3.9.0's actual API doesn't match
  `06-solution-structure.md`'s code sketch** (different namespace —
  `Microsoft.OpenApi`, not `Microsoft.OpenApi.Models` — and
  `OpenApiSchemaJsonConverter` supersedes any hand-rolled schema
  conversion the sketch implies) — verified against the installed
  package's own XML docs before writing code, not assumed from the
  sketch. Its `OpenApiSchemaJsonConverter`/`Extensions` bag tolerates
  unknown keywords like `x-masking` fine — unlike JsonSchema.Net, no
  compatibility problem here.
- A full repo-wide doc staleness review beyond what ADR-focused passes
  have covered is still genuinely open, unscheduled — now doubly relevant
  since code keeps surfacing more data-model drift as it's written
  against each doc for real.
