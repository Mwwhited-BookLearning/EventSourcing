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
  "keep going," then "Commit work and then keep going," repeated). `docs/
  08-build-plan.md`'s "Implementation status" table (near its top) is the
  authoritative tracker of which of the 48 items is Done/In progress/Not
  started — don't restate its contents here.
- **Items 1–5 are Done**: "Scaffolding & Persistence," "Schema Registry,"
  "Publish API" (`EventStore.Inbox`, plus `/openapi.json` generation via
  `EventStore.SpecGeneration`), "Lineage API (read side)" — this build
  stage's own pre-GraphQL `QUERY /events/{id}/parents|children|
  ancestors|descendants` surface (`EventStore.Lineage.Api`), **not**
  `features/event-chains.md`'s current GraphQL `event(eventId){...}`
  shape, which belongs to "GraphQL-Only Query Layer" much later — and
  "Follow API + Filter Pushdown" (`EventStore.Follow.Api`: real
  `Microsoft.OData.UriParser`-based `$filter` → LINQ compilation via
  `FilterPredicateBuilder`, `EventTailReader`'s one poll loop driving both
  `mode=tail`/`mode=replay`, plus `AsyncApiDocumentBuilder`/
  `MaskingSchemaTransformer` in `EventStore.SpecGeneration`).
  `EventStore.IntegrationTests` has 15 passing tests across SQLite/
  PostgreSQL/SQL Server (Testcontainers for the latter two). Commits on
  `dev/build-framework`: `5c5fd6e` (item 1), `c30781e` (item 2), `0886194`
  (item 3), `9ecc7d2` (item 4); item 5 not yet committed as of this
  snapshot.
- **One real, provider-specific bug in item 4's recursive CTEs, caught
  only on SQL Server**: "Types don't match between the anchor and the
  recursive part" — SQL Server infers a fixed-length `NVARCHAR(n)` for
  the anchor's path-tracking column from its literal expression alone,
  which doesn't match the recursive part's longer, growing concatenation.
  Fixed by explicitly `CAST`ing the anchor's expression to
  `NVARCHAR(MAX)`. SQLite (dynamically typed) and PostgreSQL (`TEXT` has
  no length to mismatch) never hit this — a SQL-Server-only class of
  recursive-CTE bug worth remembering for any future recursive query.
- **One real, provider-specific bug in item 5's `IJsonPathTranslator`,
  again only on SQL Server**: the Number/DateTimeOffset branches modeled
  `TRY_CAST` as a plain `SqlFunctionExpression` ("FUNC(args)" call
  syntax), but `TRY_CAST` needs the special `TRY_CAST(expr AS type)` cast
  form — "Incorrect syntax near 'TRY_CAST', expected 'AS'," caught by the
  real SQL Server test run, not by reading the code back. Fixed by
  switching to a plain `SqlUnaryExpression(ExpressionType.Convert, ...)`
  (which EF renders as `CAST(expr AS type)`), matching how SQLite/
  Postgres and SQL Server's own Boolean branch already worked — no
  provider actually needed the "try" (non-throwing) semantics once
  matched to the others.
  `IEventLineageQueryProvider`'s three implementations needed no further
  correction. `IJsonPathTranslator`'s three implementations (stubbed in
  item 1) are now fully real, used by both Follow's filter pushdown and
  (already, since item 4) nothing else yet.
- **`resolved` is implemented; `restricted` deliberately is not** — the
  GraphQL Lineage response shape names both, but `restricted` depends on
  `RequiredClaims` enforcement, which needs "Event-Type Security" (not
  yet built). This item's own scope/exit-criteria never mention
  `restricted` either, so this is confirmed in-scope-as-designed, not an
  oversight to fix later without checking first. Item 5's own exit
  criteria make the same point explicitly about `parentEventIds`: a
  restricted parent's ID being omitted depends on the same not-yet-built
  `RequiredClaims`, and is called out in the build-plan's own text as
  *not* part of this item's exit bar.
- **Item 6, "Auth (OIDC/OpenIddict) + Orchestration," is Done** —
  `EventStore.DevIdp` (real OpenIddict 7.6.0 Client Credentials server, EF
  Core InMemory store, `DevIdpSeeder`'s 3 clients), `ScopeRequirement`/
  `ScopeAuthorizationHandler` (`EventStore.Host.Core`, space-delimited
  `scope` claim matching), JwtBearer + CORS wiring in
  `HostCoreExtensions`, `RequireAuthorization(...)` on every Publish/
  Registry/Follow/Lineage endpoint, `EventStore.ServiceDefaults`/
  `EventStore.AppHost` (.NET Aspire, real templates) per `ADR-026`, and a
  root `docker-compose.yml`. `EventStore.IntegrationTests` has 16 passing
  tests (up from 15) — the new `AuthSqliteTests` drives two real
  `WebApplicationFactory` TestServers (DevIdp issuing a real token,
  Host.Sqlite validating it via real JwtBearer middleware against a real
  fetched discovery doc/JWKS) through 7 scenarios (401/403/201/CORS/
  anonymous spec endpoints) — auth is pipeline/middleware behavior, only
  provably correct end-to-end, unlike every other item's direct-
  service-call test style.
- **Next up**: item 7, "Event-Type Security" — depends only on item 6.

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
   before adding to it (16 tests should pass). Requires Docker running
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
  items is welcome when asked, and continued into items 3 and 4. Read
  "keep going" as "continue the same build-plan momentum, one item at a
  time, verified end to end before moving to the next" — not as license
  to skip running the tests, and not a one-time authorization that
  expires. **Commits happen only when the user actually says so** — this
  session did not commit unprompted between items 1 and 2; only started
  doing so once explicitly asked, and has kept committing after every
  item since (items 2, 3, 4, 5 all committed; item 6's commit is pending
  as of this snapshot — "check off work as you go. then continue" is
  the standing instruction currently in effect, so item 6 is being
  committed and item 7 started without waiting for a fresh prompt).
- **Always actually run new code against every provider it's built for
  before calling an item done.** Every real bug found this session (the
  `ExecuteSqlRawAsync` brace-parsing issue, an unquoted Postgres column,
  the JsonSchema.Net incompatibility, the SQL Server recursive-CTE type
  mismatch) was caught by running tests against real engines, not by
  reading the code back. Item 3 shipped clean on the first run; item 4
  didn't (a genuinely new SQL-Server-specific class of bug, not a
  repeat) — the discipline keeps paying for itself differently each time,
  never assume a clean run on one item means the next needs less rigor.
- **A recurring, genuine doc/build-stage split, not a bug each time**:
  items 2 and 4 both build a *temporary* pre-GraphQL/pre-`ADR-023`-shaped
  surface that a current feature doc's Gherkin no longer describes (it
  was rewritten to the GraphQL end state). Before assuming a feature
  doc/build-plan mismatch needs a correction note, check whether the
  build-plan item's own text already self-declares the translation (item
  3, "Publish API," already did — no note needed there); only add a
  correction when the exit criteria actually overclaim a scenario that no
  longer exists (items 2 and, so far, not 4 — item 4's own exit criteria
  were written in this item's own prose terms, not by citing a specific
  now-superseded Gherkin scenario name).
- **`docs/06-solution-structure.md`'s code sketches are "concept accurate,
  exact wiring unverified" by its own banner** — confirmed true a fourth
  time: every new web-facing project has needed an explicit
  `FrameworkReference` to `Microsoft.AspNetCore.App` the doc's sketches
  never show, and `Microsoft.OpenApi`'s real 3.9.0 API/namespace doesn't
  match the sketch's pre-v2 shape either (item 3).
- A full repo-wide doc staleness review beyond what ADR-focused passes
  have covered is still genuinely open, unscheduled — now doubly relevant
  since code keeps surfacing more data-model drift as it's written
  against each doc for real.
- **Test-authoring gotcha, not a production bug**: calling
  `IAsyncEnumerable<T>.GetAsyncEnumerator()` more than once against the
  same instance starts a fresh enumeration each time — for a live poll
  loop like `EventTailReader.TailAsync` (a C# async-iterator method),
  that means the loop restarts from its *original* `lastSeen`, not from
  wherever a prior pull left off. Get the enumerator once per connection
  and reuse that same `IAsyncEnumerator<T>` across every subsequent pull
  in a test (or real caller) that needs to keep observing the same
  stream — this cost one debugging round-trip in item 5's Follow tests
  (`FollowScenarioAssertions.Collect`) before being caught.
