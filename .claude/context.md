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
  "keep going"). `docs/08-build-plan.md`'s new "Implementation status"
  table (near its top) is the authoritative tracker of which of the 48
  items is Done/In progress/Not started — don't restate its contents
  here; it drifts the moment an item's status changes.
- **Items 1 and 2 are Done**: "Scaffolding & Persistence" (the solution/
  project skeleton, full `EventStoreContext` model, migrations verified
  applying on all three providers) and "Schema Registry" (`PUT`/`GET
  /registry/{event-type}`, a temporary pre-GraphQL `QUERY /registry`
  listing endpoint, structural JSON Schema/`FilterableField`/`x-masking`
  validation, per-provider index/computed-column DDL — see
  `EventStore.SchemaRegistry`). `EventStore.IntegrationTests` now has 6
  passing tests across SQLite/PostgreSQL/SQL Server (Testcontainers for
  the latter two — Docker is available in this dev environment).
  `global.json` pins the SDK (`10.0.302`); target framework `net10.0`.
- **Item 2 surfaced several real bugs and one reverted library adoption —
  all found by actually running tests against real engines, not by
  inspection**:
  - `IJsonPathTranslator`'s placeholder shape from item 1 was simply
    wrong — the real interface (verified against
    `docs/04-odata-filter-pushdown.md`) takes an EF Core `SqlExpression`,
    not a bare string. Corrected; still unimplemented (belongs to "Follow
    API + Filter Pushdown"). A **separate**, new interface,
    `IFilterableFieldIndexDdlGenerator`, was added for the DDL-generation
    concern "Schema Registry" actually needs — don't conflate the two.
  - **JsonSchema.Net was adopted, then reverted within the same pass**:
    it rejects any undeclared vendor keyword by default ("Unknown
    keywords (x-masking) are disallowed for this dialect") unless the
    document declares `$schema`/`$vocabulary` or the caller registers a
    custom `Dialect` — incompatible with this design's pervasive,
    undeclared `x-masking` extension. Replaced with a small hand-written
    structural check (valid `type` values, `properties`/`items` shape).
    **`docs/libraries/`'s catalog does not have an entry for this** —
    correctly so, since it never became a real adoption; the story is
    only in this file and `docs/changes/2026-08-03.md`.
  - `db.Database.ExecuteSqlRawAsync(sql)` always parses `sql` as a
    composite format string internally, even with zero parameters
    supplied — PostgreSQL's own `'{Amount}'` path-array literal syntax
    broke this. Fixed by issuing DDL through a raw `DbCommand` on the
    same connection/transaction instead. **Watch for this again** — any
    future raw SQL containing literal `{`/`}` (not just this one spot)
    needs the same treatment, not `ExecuteSqlRaw`.
  - PostgreSQL's index-DDL generator had an unquoted column name
    (`Payload::jsonb` instead of `"Payload"::jsonb`) — Postgres folds
    unquoted identifiers to lowercase, so it looked for a `payload`
    column and failed. Fixed. Confirms the value of actually running
    against a real Postgres container rather than trusting the SQLite
    pass alone.
- **A real data-model doc gap found and fixed while implementing item 1,
  and another while implementing item 2** (both `docs/data/
  schema-registry.md`): `FilterableField` was missing `EventTypeAppId`
  (present in the feature doc's ER diagram, required by `ADR-030`, never
  propagated); `FilterableField.JsonPath` is now explicitly restricted to
  a safe dotted-identifier-chain grammar (`^\$(\.[A-Za-z_][A-Za-z0-9_]*)+$`)
  — `04-odata-filter-pushdown.md` cites full RFC 9535 JSONPath, but no
  real example anywhere uses more than this subset, and an unrestricted
  grammar flowing into raw DDL would be a real injection surface. Expect
  more doc-vs-code drift like this as further items get built — this is
  the first time code has needed a doc's shape to be *literally* correct,
  not just internally consistent prose.
- **A build-plan exit-criteria overclaim found and corrected**:
  "Schema Registry"'s exit criteria claimed "every scenario in
  `features/schema-registry.md`" as testable, but that doc's Gherkin was
  rewritten to the GraphQL-only end state this session with no preserved
  historical scenario for the plain `QUERY /registry` `$top`/`$skip`
  listing this item actually builds first (unlike an ADR's struck-through-
  history convention, a feature doc's Gherkin doesn't retain a superseded
  scenario). Corrected in place with a note; the listing endpoint is real
  and tested directly in `EventStore.IntegrationTests` instead.
- **Next up**: item 3, "Publish API" — now unblocked. Nothing else is in
  flight.

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
   before adding to it (6 tests should pass). Requires Docker running
   (Testcontainers for Postgres/SQL Server) and the SDK pinned in
   `global.json`.

## Working notes not yet written down elsewhere

- The user wants to be asked before large, effort-heavy content rewrites
  get started unilaterally — offer explicit options, don't just do it.
  Smaller, unambiguous fixes (broken links, typos, a stale field name,
  a wrong library choice found mid-task) are fine to fix directly without
  asking first, as several were this session.
- **Implementation pacing**: "let's do this" started item 1; a plain
  "keep going" (no further scoping) continued straight into item 2 rather
  than needing a fresh go-ahead each time. Read "keep going" as "continue
  the same build-plan momentum, one item at a time, verified end to end
  before moving to the next" — not as license to rush ahead without
  running the tests, and not as a one-time authorization that expires
  after one item.
- **Always actually run new code against every provider it's built for
  before calling an item done.** Every real bug found this session (the
  `ExecuteSqlRawAsync` brace-parsing issue, the unquoted Postgres column,
  the JsonSchema.Net incompatibility) was caught by running tests against
  real engines, not by reading the code back. A SQLite-only pass would
  have shipped two of those three bugs silently.
- **`docs/06-solution-structure.md`'s code sketches are "concept accurate,
  exact wiring unverified" by its own banner** — confirmed true again:
  `EventStore.Host.Core`/`EventStore.SchemaRegistry` both needed a
  `FrameworkReference` to `Microsoft.AspNetCore.App` the doc's sketches
  don't show at the csproj level.
- A full repo-wide doc staleness review beyond what ADR-focused passes
  have covered is still genuinely open, unscheduled — now doubly relevant
  since code keeps surfacing more data-model drift as it's written
  against each doc for real.
