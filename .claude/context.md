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
- **Item 8, "Derived/Materialized Event Types (deferred)," is Done** —
  a new `EventStore.Derivation` project: `DerivationRegistrationService`
  (`POST /create/{event-type}`, `$from`/`$on`/`$select` as request-body
  fields per `ADR-012`'s own precedent, auto-composed schema, DFS cycle
  check), `DerivationWorker` (`IHostedService`, one polling loop across
  every active `DerivationDefinition`, republishing through the ordinary
  `PublishService.PublishAsync` path). New persisted shapes
  (`DerivationDefinition`, `DerivationCursor`, `PendingJoinState`,
  `StoredEvent.DerivationHopCount`) documented in `docs/data/schema-
  registry.md`/`event-log.md` in the same pass, per this repo's own
  "ADR that adds a shape is that shape's authority" rule — `ADR-007`
  itself named these as complexity without specifying a concrete shape,
  so this pass's answers are recorded there, not just in code. Two real
  bugs found only by running the tests, not by reading the code back:
  (1) `PendingJoinState.ExpiresAt <= now` doesn't translate on SQLite
  (relational operators other than equality aren't supported on
  `DateTimeOffset` columns by that provider) — fixed by filtering
  client-side after fetching non-expired candidates; (2) a `PendingJoinState`
  row `Add()`ed for one event in a multi-event batch wasn't visible to
  the *next* event's `SingleOrDefaultAsync` lookup in the same tick, since
  `SaveChangesAsync` was originally deferred to the end of the whole
  batch — fixed by saving after each event, not once per tick. Also
  dropped the `(AppId, DerivationName, JoinKeyValue)` unique index down to
  a plain (non-unique) index — a straggling source arriving after its
  join already expired starts a **fresh** row with the same key, which a
  DB-level unique constraint would reject; "at most one active row per
  key" is enforced in application code (`ExpiredReason == null` in every
  lookup), not the database. `EventStore.IntegrationTests` now has 19
  passing tests (up from 16) across SQLite/PostgreSQL/SQL Server — 3 new
  `DerivationScenarioAssertions` scenarios' worth of `[TestMethod]`s
  (`DerivationSqliteTests`/`DerivationPostgresTests`/
  `DerivationSqlServerTests`), each running all 10 scenarios (registration
  validation × 4, FireOnce join × 2, ContinuousEnrichment, TTL sweep,
  FromNow backfill, hop-count cap).
- **Item 9, "Property-Level Masking (data enforcement)," is Done** — a new
  `EventStore.Masking` project: `IPayloadMasker`/`PayloadMasker` (a
  `(schema, data, hasClaim) -> data` recursive transform), three keyed
  `IMaskingStrategy` implementations (`FixedValue`/`PartialReveal`/`Hash`,
  the last a real `Microsoft.Extensions.Compliance.Redaction.HmacRedactor`
  keyed by `x-masking.keyId`), wired into `EventTailReader.TailAsync`'s
  per-event pipeline (`FollowedEvent` gained a `MaskedPayload` field).
  `RequiredClaimEvaluator.HasClaim` promoted to `public` so masking reuses
  Event-Type Security's exact "type:value" claim primitive, per `ADR-009`'s
  own explicit instruction to share it. Confirmed empirically (not
  assumed) that `Microsoft.Extensions.Compliance.Redaction`'s default
  fallback for any unconfigured classification is `ErasingRedactor` — the
  log-redaction half (`ADR-050`) leans on this: `PayloadMasker` redacts
  any `regulatoryClassification`-tagged field through a **second**,
  deliberately distinct classification taxonomy before logging a
  diagnostic trace, verified via a capturing `ILoggerProvider` in tests.
  Only the dynamic log-redaction path was built — the static
  `[LoggerMessage]`-attribute half has no real call site yet in this
  codebase; noted as a scoped-down, honestly-flagged simplification, not
  silently dropped. `EventStore.IntegrationTests` now has 22 passing
  tests (up from 19) across SQLite/PostgreSQL/SQL Server —
  `MaskingScenarioAssertions`' 8 scenarios cover the wrapper's
  value/masked branching, all three strategies, array recursion
  (scalar-wraps-each-element vs. complex-object-wraps-just-the-property),
  absent-vs-masked distinction, and the log-redaction verification;
  registration-time `x-masking` validation and the `oneOf` wrapper's
  presence in generated docs were already covered by earlier items' own
  tests, not repeated.
- **Item 10, "CQRS Read-Model Projections (worked example)," is Done** —
  three new projects: `EventStore.Projections.Abstractions`
  (`IProjection<TReadModel>`, no dependencies at all),
  `EventStore.Projections.Host` (`ProjectionHost<TReadModel>`,
  `SnapshotMerger`, an abstract `ProjectionsDbContext`, `FollowClient` — a
  **real** HTTP client issuing the actual `QUERY /follow/{event-type}` verb
  and parsing its SSE response, plus a real OAuth2 Client Credentials token
  fetch against `EventStore.DevIdp`; deliberately references none of
  `EventStore.Persistence`/`Host.Core`/any `Host.<Provider>`, enforced by
  the project reference graph itself), and `Samples.Orders.Projections`
  (the actual runnable Worker Service: `OrderSummaryProjection`,
  `OrderSummary`, `OrdersProjectionsDbContext`). A fourth seeded DevIdp
  client, `projections-client` (`events:follow`). A real, previously-
  unanticipated gap found while building: `ChangeKind` isn't carried on
  Follow's SSE envelope at all (it's a property of the event type's
  registration), and `ProjectionHost` has no direct DB reference to look
  it up another way — resolved with a small additive `GET /registry/
  {eventType}/change-kind` endpoint, gated by `events:follow` rather than
  the rest of the registry's `registry:admin` (a projections client has no
  reason to hold that scope). Tests are genuinely different in kind from
  every prior item's: this is the first item whose only reachable write-
  side dependency is real HTTP, so `ProjectionsSqliteTests` reuses "Auth +
  Orchestration"'s own two-`WebApplicationFactory`-TestServer pattern (real
  tokens, real JwtBearer validation) rather than calling a service
  directly — and `ProjectionHost<T>.CatchUpOnceAsync(eventType,
  maxEventsToConsume, idleTimeout, ct)` lets tests drive one bounded
  catch-up pass deterministically against Follow's inherently-infinite SSE
  stream, the same "exercise the mechanics directly, with a timeout"
  pattern this repo's Follow/Masking tests already established. Single-
  provider only (SQLite) — `docs/09-cqrs-read-models.md`'s own "no
  per-provider build split here" note, unlike every write-side item's own
  3-provider matrix. `EventStore.IntegrationTests` now has 23 passing
  tests (up from 22, +1 `[TestMethod]` covering 7 scenarios).
- **Item 11, "Hardening & Evolution (DPoP, event upcasting, hash-chained
  tamper evidence)," is Done.** Full narrative (code links, the real
  chain-verification gap found and fixed, the `EventUpcastFailed`
  dead-letter design, and 3 real bugs found only by running the real HTTP
  round trip) is in `docs/changes/2026-08-04.md` — not repeated here.
  Short version: `EventStore.Dpop` (new project, EC P-256 DPoP proofs,
  shared by `EventStore.DevIdp` and `EventStore.Host.Core`), `ADR-019`'s
  `ChainVerificationService` fixed to re-derive `PayloadHash` from `Payload`
  itself (it previously trusted the stored column blindly, missing exactly
  the corrupted-`Payload` scenario this item's own exit criteria names),
  `ADR-020`'s publish-time upcast compatibility check + `EventUpcastFailed`
  dead-letter built from scratch. `EventStore.IntegrationTests` still has
  23 `[TestMethod]`s (more internal scenarios each, not new methods) — all
  pass across SQLite/PostgreSQL/SQL Server.
- **Item 12, "Entity-Centric Core Rebuild," is Done — same day, same
  session, continuing directly from item 11.** Full narrative in
  `docs/changes/2026-08-04.md`'s second half. Short version: new
  `EventStore.Router` project (`RouterWorker`, the async half of the
  Inbox/Router split, `ADR-023`) does schema validation + entity
  resolution + the Entity Store fold (`ConflictFlag`/`LateArrivalFlag`,
  `ADR-024`/`029`); `PublishService` rewritten to always return `202` +
  a status envelope (`PublishResult.Accepted`), never blocking on schema
  content; **`EventUpcastFailed` (item 11's own `ADR-020` dead-letter
  mechanism, built and tested that same day) is retired**, per `ADR-023`'s
  own explicit "reframed... not a special case" text — both
  `docs/adrs/adr-020-schemaversion-on-publish.md` and item 11's own
  build-plan section got the required strikethrough-and-pointer note.
  Two real design gaps found and fixed while writing tests, not by
  reading the code back: `StoredEvent` needed a real `AppId` field (the
  "dedicated fix" `docs/10-open-questions.md`'s former row 1 predicted —
  that row is now deleted, resolved) and `EventTypeDefinition` needed a
  new `EntityType` field distinct from `Name` (`OrderPlaced`/
  `OrderShipped` are different event types that must still resolve to
  the same `EntityId` — the first implementation attempt didn't do this
  and silently created two different Entity Store rows for one order).
  `EventStore.IntegrationTests` now has 26 `[TestMethod]`s (up from 23) —
  all pass across SQLite/PostgreSQL/SQL Server.
- **Item 13, "Multi-Tenancy," is Done — same day, continuing directly from
  item 12.** Much smaller than 11/12: `ADR-030`'s own text already said
  multi-tenancy was "closer to already built than not" (`EventTypeDefinition`
  keyed by `(AppId, Name, Version)` since item 2; `EntityId`/`StoredEvent.
  AppId` already disambiguate applications since item 12). The one
  genuinely new piece: `registry:admin` gains optional `AppId`-scoped
  variants (`registry:admin:{appId}`) — `ScopeAuthorizationHandler`'s
  coarse gate plus a new `AppIdScopeEvaluator` fine-grained check in
  `SchemaRegistryEndpoints`, checked at the HTTP layer (not inside
  `SchemaRegistryService`) so its ~15 direct-construction test call sites
  needed zero changes. Found and fixed a real, previously-latent RFC 9449
  bug while writing the new isolation test (the first query-string request
  in the whole suite): `AttachAuth`'s `htu` must exclude the query string,
  in both the test helper and `FollowClient`'s own production code. Full
  narrative in `docs/changes/2026-08-04.md`. `EventStore.IntegrationTests`
  still has 26 `[TestMethod]`s (existing methods gained one scenario each).
- **Item 14, "Upcast Materialization + Downcast," is Done — same day,
  continuing directly from item 13.** New `EventStore.Persistence/
  EventAppender.cs` (the hash-chain-aware insert extracted out of
  `PublishService`, shared with the materializer below). `UpcastMaterializer`
  (`EventStore.Router`) implements both `ADR-027` triggers: Trigger 1 fires
  inline in `RouterWorker.ProcessEventAsync` right after entity resolution,
  when a just-validated publish is conformant against its own declared
  version but behind the active one; Trigger 2 (`ReconcileBacklogAsync`)
  runs every `RouterWorker` tick, scanning for any active multi-version
  type with unmaterialized backlog. Both call `EventAppender.AppendAsync`
  directly rather than `PublishService.PublishAsync` — going through the
  ordinary publish path would re-run `RequiredClaims` enforcement against
  an empty system principal, wrongly `Forbidden`-ing the materialization of
  any claim-gated type. `DowncastChain` (`EventStore.Upcasting`, `ADR-028`)
  mirrors `UpcastChain` but with no safe pass-through — a missing hop is a
  hard `Failed`. `JsonataUpcastExpressionEvaluator` (`ADR-053`'s second,
  swappable engine) rounds-trips through JSON text against
  `Jsonata.Net.Native`'s own JSON DOM, and wraps the source payload under a
  synthetic top-level `"event"` key so the *same* registered expression
  text (e.g. `"event.Amount as Amount"`) resolves identically under both
  CEL and JSONata — genuinely proven by a shared unit test, not just
  visually similar output. `FollowRequest` gained `AsOfSchemaVersion`;
  `FollowService.ConnectAsync` validates every hop down to it exists
  *before* connecting (400 otherwise); `EventTailReader` applies
  `DowncastChain` after `UpcastChain`, masking against the requested (not
  active) version's schema.
  **A real correctness bug found only by running the full suite, not by
  reading the code back**: `EventTailReader` was delivering
  `EventKind.UpcastMaterialization` rows as independent new SSE events,
  double-delivering one logical fact — caught by `ProjectionsSqliteTests`
  (a test this item never touched) failing only once repeated
  re-registration of the same schema in that test's own setup pushed one
  of its event types past `Version` 1, for real, activating Trigger 2 for
  the first time in that suite. `ADR-027`'s own text already named the
  fix: Follow's *default* is "consume only `Original` events, always
  upcasting live" — a materialization is an optional, opt-in-only
  optimization for other readers, never a second delivery of the same
  event. Fixed by filtering `EventKind == Original` in `EventTailReader`'s
  own query. `docs/features/upcast-materialization-and-downcast.md`'s two
  Trigger diagrams were also corrected in the same pass — they still
  showed the pre-`ADR-023` synchronous-Inbox and Follow-tailing/
  `PublishEndpoint`-republishing shapes, not what actually got built.
  `EventStore.IntegrationTests` now has 33 `[TestMethod]`s (up from 26) —
  all pass across SQLite/PostgreSQL/SQL Server, twice in a row for
  stability.
- **Item 15, "Streaming Channels," is Done — same day, continuing directly
  from item 14.** A genuinely large item: new `EventStore.Domain/Streaming/`
  (`TelemetryChannel`/`TelemetrySample`/`RedactedRange`/`TelemetryPointerEntry`,
  shape straight from `docs/data/streaming-and-attachments.md`) and a new
  `EventStore.Streaming` project — `ChannelRegistryService`,
  `TelemetrySampleWriter` (batch ingest, `ADR-029`'s high-water-mark reused
  per-channel for `LateArrivalFlag`, `ChannelLagDetected` published through
  the ordinary `PublishService` path on producer lag), `TelemetryTailReader`
  (`ADR-010`'s tail/replay shape reused for `TelemetrySample`, plus `ADR-081`'s
  `ThreadId`-grouped multi-channel session view), `ChannelDerivationWorker`
  (Resample-only at this stage — decimation, not a real anti-aliasing
  filter; `Filter`/`Aggregate`/`Transcode` accepted but not acted on,
  flagged in the build-plan section, not silently dropped), a sibling
  `IStreamRedactionStrategy` seam (`ZeroFillStrategy`/`ToneStrategy`/
  `BlankFrameStrategy`, plus `PartialRevealStreamRedactionStrategy`
  genuinely reusing `PartialRevealMaskingStrategy`'s reveal computation via
  a UTF-8 round trip), and `MediaFragmentUri`/`MediaFragmentResolver` (W3C
  Media Fragments URI temporal syntax, `#t=b,e`). `StreamingEndpoints`
  dual-modes one GET route by `Range` header presence: with it,
  `Results.Bytes(enableRangeProcessing: true)` serves real `206 Partial
  Content` byte-range playback; without it, the live JSON/SSE tail/replay
  stream. `PublishEventRequest`/`StoredEvent.TelemetryPointer` wired end to
  end (the column already existed, unused, since "Scaffolding &
  Persistence"). New `telemetry:ingest`/`telemetry:read` scopes + a seeded
  `telemetry-client` in DevIdp.
  **Two real SQLite-only bugs found by actually running the tests**: (1)
  SQLite's EF provider cannot translate a `DateTimeOffset` relational
  comparison (`>`/`<`), `MIN`/`MAX` aggregate, or `ORDER BY` — the exact
  same class of gap already found once before for `PendingJoinState`'s TTL
  sweep, this time hitting `TelemetryTailReader`'s cursor queries,
  `MediaFragmentResolver`'s earliest-sample lookup, and the Range-request
  handler's byte-concatenation ordering — fixed by filtering on the
  translatable column only, then ordering/comparing/aggregating
  client-side in every case; (2) a real DI wiring bug (unrelated to
  SQLite specifically, just first surfaced by the one test that uses the
  real Host composition root instead of a hand-built container):
  `EventStore.Masking.AddMasking()` registers `PartialRevealMaskingStrategy`
  **keyed** ("PartialReveal"), not plain — `PartialRevealStreamRedactionStrategy`
  originally depended on it unkeyed and failed to resolve; fixed by
  constructing it directly instead (it's stateless, no DI needed at all).
  **One real test-design bug, not a production bug**: two scenario methods
  shared one literal `"streaming-app"` AppId and each made 2 sequential
  ingest calls on a 4000-microsecond-interval channel — fine on SQLite's
  fast in-process round trips, but on a real Postgres/SQL Server container
  the real wall-clock gap between two awaited calls routinely exceeds the
  3x-interval lag threshold, so BOTH methods' incidental `ChannelLagDetected`
  events collided under one shared AppId (`SingleOrDefaultAsync` threw
  "more than one element") — fixed by giving every scenario its own unique
  AppId, the convention every other `*ScenarioAssertions.cs` file already
  follows and this one should have from the start.
  `EventStore.IntegrationTests` now has 37 `[TestMethod]`s (up from 33) —
  all pass across SQLite/PostgreSQL/SQL Server (`StreamingSqliteTests`/
  `Postgres`/`SqlServer`, 11 scenarios each) plus one dedicated real-HTTP
  `StreamingHttpSqliteTests` for the Range-request/206 behavior
  specifically (only observable through the actual ASP.NET Core pipeline),
  run twice in a row for stability.
- **Next up**: item 16, "Binary Attachments" (`ADR-032`) — depends on
  Auth + Orchestration and Entity-Centric Core Rebuild, both Done. Its own
  build-plan section already flags a real forward dependency: its
  "GraphQL browsing of an entity's linked attachments" exit criterion
  can't actually be exercised until "GraphQL-Only Query Layer" lands much
  later — build the upload/content-hash-retrieval half now, re-verify the
  GraphQL-browse half then.

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
   before adding to it (37 tests should pass). Requires Docker running
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
  treated as this session's own action; items 7 through 10 all committed —
  "check off work as you go. then continue" is the standing instruction
  currently in effect, so each item is committed without waiting for a
  fresh prompt; items 11 through 15 now done too, per the same rhythm).
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
- **`AddDbContext`'s options-configuration delegate re-runs on every
  scoped `DbContext` instantiation, not once at startup** — computing a
  fresh random value (e.g. `Guid.NewGuid()`) directly inside that
  delegate silently hands every single request its own distinct
  in-memory database, since `UseInMemoryDatabase(name)` is what actually
  gets re-evaluated per call. Compute any such value once, in a local
  variable *outside* the `AddDbContext(...)` call, and close over it.
  Cost a full extra debugging round-trip in item 11's DPoP work (a
  self-inflicted regression while fixing a genuine parallel-test-database
  race) — see `docs/changes/2026-08-04.md` for the full story.
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
