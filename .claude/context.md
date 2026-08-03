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
- **`resolved` and `restricted` are both implemented as of item 7** —
  item 4 built `resolved` only, deliberately deferring `restricted` to
  "Event-Type Security" (this item's own scope/exit-criteria never
  mentioned it, confirmed in-scope-as-designed at the time, not an
  oversight); item 5 deferred Follow's `parentEventIds` restriction-
  filtering the same way. Both are now real, per item 7's own summary
  above.
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
- **Item 7, "Event-Type Security," is Done** — enforcement of
  `EventTypeDefinition.RequiredClaims` (already built, accepted-but-not-
  enforced, by item 2) at Publish (`RequiredClaimEvaluator` in
  `EventStore.Domain.SchemaRegistry`, shared by all three call sites),
  Follow (connect-time gate + `parentEventIds` now actually populated,
  filtering out parents the caller can't Read), and Lineage (root-
  visibility 403-vs-404, per-node stubbing for parents/children, and a
  new in-memory BFS in `LineageService` that makes ancestors/descendants
  genuinely stop recursing past a restricted node — not just redact its
  fields — without touching any of the 3 providers' recursive-CTE SQL).
  A real, pre-existing architectural gap surfaced while implementing
  read-side enforcement: `StoredEvent` carries no `AppId`, but `ADR-030`
  allows two different `AppId`s to register the same type name
  independently — nothing in any ADR/doc actually disambiguates which
  one's `RequiredClaims` governs a bare stored event's type. Recorded as
  `docs/10-open-questions.md` row 1 (genuinely unresolved, not
  deprioritized) with a pragmatic, explicitly-flagged simplification
  (`SchemaRegistryService.GetActiveClaimsByNameAsync`: resolve by
  `(Name, IsActive)` alone, deterministic-but-arbitrary tie-break on a
  genuine collision) rather than silently picking one. Also found and
  fixed, before writing any new code: item 6's own accidental deletion
  of the `## Event-Type Security` heading (an editing mistake, not a
  design change — the section's body survived, only the heading and its
  anchor were dropped) and item 7's own build-plan text describing
  building `ADR-008`'s *original* single-claim shape "as originally
  decided," when item 2 had already built `ADR-050`'s generalized
  `RequiredClaims` list — corrected the build-plan text to match what's
  actually built and being enforced, rather than build against a stale
  description. `EventStore.IntegrationTests` still has 16 `[TestMethod]`s
  (each provider's `AllXScenarios` gained more internal scenario calls,
  not new test methods) — all 16 pass across SQLite/PostgreSQL/SQL
  Server.
- **Next up**: item 8, "Derived/Materialized Event Types (deferred)" —
  depends on item 7.

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
  item since (items 2 through 6 all committed -- item 6's own work was
  captured by an external auto-checkpoint commit, `63ff5c7`, not this
  conversation directly, surfaced to the user rather than silently
  treated as this session's own action; item 7's commit is pending as of
  this snapshot — "check off work as you go. then continue" is the
  standing instruction currently in effect, so item 7 is being committed
  without waiting for a fresh prompt; item 8 not yet started as of this
  snapshot).
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
- **New testing pattern established for item 6, worth reusing for any
  future auth-adjacent item**: two `WebApplicationFactory` TestServers
  wired together via one's real HTTP client set as the other's
  `JwtBearerOptions.ConfigurationManager` (a `StaticConfigurationManager`
  built from a `ConfigurationManager<OpenIdConnectConfiguration>` that
  used `HttpDocumentRetriever(theOtherFactory.CreateClient())`) — a real,
  non-shortcut in-memory validation of one real issued token against
  another real fetched discovery doc/JWKS, no live ports needed. Setting
  `JwtBearerOptions.Configuration` directly (not `.ConfigurationManager`)
  does **not** work for this: the framework's own internal
  `PostConfigureOptions<JwtBearerOptions>` (added once, by `Program.cs`'s
  own `.AddJwtBearer(...)` call) already converts whatever `.Configuration`
  held into a real `ConfigurationManager` before a test's own later
  `PostConfigure` can supply a better one — this cost a full debugging
  cycle (repeated "issuer invalid" 401s with a *correct*-looking token)
  before being traced to that ordering, not a config-value mistake.
  Two projects sharing a top-level-statement `Program` class name (every
  Minimal API entry point) need `<ProjectReference ... Aliases="X"/>` +
  `extern alias X;` in the test file the first time a test references
  more than one such project — first hit this item, will recur for any
  future multi-service test.
- **Actually running `aspire run` (not just `dotnet build`) against
  `EventStore.AppHost` found 5 more real bugs no test could catch**,
  each fixed: `AddDatabase("Postgres")` needed (the bare server resource
  alone injects no `Database=...`); `.WaitFor(db)` needed (without it,
  `eventstore` starts before Postgres finishes its own startup and
  crashes on first migration attempt); `RequireHttpsMetadata` needed an
  explicit `.WithEnvironment(...)` override (Aspire's plain-HTTP
  `Authority` injection doesn't reliably get `appsettings.Development.
  json`'s dev override applied); a migrate-on-startup step was missing
  entirely (a brand-new container has no schema — affects `docker-
  compose.yml` too, fixed identically in all 3 Hosts); and
  `EventStore.DevIdp/Properties/launchSettings.json` (a stray scaffold
  file an earlier `rm -rf` silently failed to delete, due to a `cd`-
  relative-path mistake, not caught until Aspire's own endpoint-
  reference resolution picked up its hardcoded port instead of the real
  dynamically-assigned one). **One further, still-open issue**: the
  Postgres database resource's own documented auto-creation doesn't
  reliably finish before `eventstore`'s first connection in this
  environment (`Aspire.Hosting.PostgreSql` 13.4.6) — tracked in
  `TODO.md`, not silently claimed fixed. Lesson: `dotnet build` succeeding
  is not the same bar as actually running the orchestration end to end —
  this is the same "always actually run it" discipline already applied
  to every provider, now extended to the orchestration layer itself.
