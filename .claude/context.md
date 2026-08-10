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
- **Item 16, "Binary Attachments," is Done — same day, continuing directly
  from item 15.** Smaller than the last several items. New
  `EventStore.Domain/Streaming/Attachment.cs` (`Attachment`/`ChunkRef`/
  `AttachmentRef` — same `Streaming` namespace as item 15's types, per
  `docs/data/streaming-and-attachments.md`'s own single-file grouping,
  even though `ADR-032` is its own decision) and a new `EventStore.
  Attachments` project — `AttachmentService` (`UploadAsync` with real
  SHA-256-based dedup, `RetrieveAsync`, `LinkAsync` creating `AttachmentRef`
  rows), `AttachmentEndpoints` (`POST /attachments` raw-bytes upload,
  `GET /attachments/{contentHash}` reusing the identical
  `Results.Bytes(enableRangeProcessing: true)` mechanism item 15 already
  proved for Media playback). `PublishEventRequest` gained
  `AttachmentContentHashes` — completes `ADR-032`'s two-step handoff by
  linking through the ordinary publish path, the same envelope-field
  pattern `TelemetryPointer` already established. New `attachments:ingest`/
  `attachments:read` scopes + a seeded `attachments-client` in DevIdp.
  **A real, pre-existing doc/ADR drift found and fixed while building
  against this ADR, not caused by this pass**: `ADR-032`'s own "Standalone
  attachments and direct permissions" section clearly decided `Attachment`
  needs `RequiredReadClaim`/`RequiredPublishClaim` fields, but
  `docs/data/streaming-and-attachments.md`'s `Attachment` class never
  actually carried them — added in this same pass, per this repo's own
  "the ADR that decides a field is that field's naming/shape authority"
  rule. Also added a surrogate `AttachmentRef.Id` (no natural composite
  key exists once `EntityId`/`EventId` are both independently nullable).
  Built `IAttachmentContentStore` for real (the keyed-DI seam "Event Log/
  AccessLog Archival Segment Detachment" later depends on, reused
  unchanged) with one dev/POC in-memory backend — deliberately NOT wired
  into the default upload path yet, since `ADR-032`'s own "`ContentProviderKey`:
  null means this table" framing makes `Attachment.Bytes` itself the v1
  storage; the seam exists and is registrable/testable, ready for a real
  backend or a future tiering mover to use. The tiering mover itself and
  content-defined chunking (`ChunkIndex`) are explicitly NOT built at this
  stage — flagged in the build-plan section, not silently dropped, same
  treatment as item 15's unimplemented `TransformKind`s. GraphQL-browse
  exit criterion remains deferred to "GraphQL-Only Query Layer," per this
  item's own pre-existing note.
  `EventStore.IntegrationTests` now has 41 `[TestMethod]`s (up from 37) —
  all pass across SQLite/PostgreSQL/SQL Server (`AttachmentSqliteTests`/
  `Postgres`/`SqlServer`, 7 scenarios each) plus one dedicated real-HTTP
  `AttachmentHttpSqliteTests` for the Range-request/206 behavior, run
  twice in a row for stability.
- **Item 17, "Sharding & Replication," is Done — same day, continuing
  directly from item 16.** New `HybridLogicalClock`
  (`EventStore.Domain/EventLog`, Kulkarni et al.'s HLC algorithm —
  physical = max(wall-clock, prior local, observed remote), logical
  increments on a tie/resets when physical genuinely advances, formatted
  `"{physicalTicks:D19}-{logicalCounter:D10}"` for correct lexicographic
  ordering); `EventAppender.AppendAsync` gained an `observedRemoteClock`
  parameter, computing `StoredEvent.LogicalClock` alongside `ChainHash` in
  the same read-prior-row pattern. `PublishService` gained an optional
  `IOptions<OriginIdOptions>?` (defaulting to `"local"`) so ~26 existing
  3-arg test call sites needed zero changes — a deliberate low-invasiveness
  choice. New `EventStore.Domain/Replication/PeerSyncCursor.cs` (exact
  shape verified against `docs/data/schema-registry.md` before building:
  `PeerId, LastReceivedSequenceNumber, LastAckedSequenceNumber,
  LastSyncAttemptAt, LastSyncSuccessAt` — no `Address` field) and a new
  `EventStore.Replication` project: `PeerAddressBook` (in-memory, gossip-
  discovered via a `knownPeers` exchange baked into every push request/
  response round trip — proves `ADR-051`'s "one seed discovers the rest of
  the mesh" requirement), `PeerSyncClient`/`PeerSyncReceiver` (the latter
  appends via `EventAppender.AppendAsync` directly, bypassing
  `PublishService` entirely — the original event already passed
  claims/parent-link checks once at its own origin site, the same
  reasoning item 14's `UpcastMaterializer` already established),
  `PeerSyncWorker` (`BackgroundService` + a public `RunOnceAsync` static
  for direct testing, per this repo's established
  RouterWorker→ChannelDerivationWorker→PeerSyncWorker testing-pattern
  precedent), `PeerSyncEndpoints` (`GET /peer-sync/whoami`,
  `POST /peer-sync/push`, both gated by a new `peer:sync` scope). All 3
  hosts wired (`AddReplication()`, `OriginIdOptions`/`PeerSyncOptions`/
  `PeerSyncClientOptions`, a `"PeerSync"` named `IHttpClientFactory`
  client with no fixed `BaseAddress`).
  **A real bug found while writing the real-HTTP test, not by reading the
  code back**: `PeerSyncClient.AttachAuth`'s `request.RequestUri!.
  GetLeftPart(UriPartial.Path)` throws `InvalidOperationException` for a
  relative `RequestUri` — which it deliberately is when the real target is
  supplied via a `FixedHttpClientFactory`-mapped `HttpClient.BaseAddress`
  rather than a real absolute URL (the same test pattern `FollowClient`'s
  own tests already use). Fixed by resolving against `client.BaseAddress`
  first when `RequestUri` isn't already absolute, mirroring
  `FollowClient.AttachAuth`'s existing pattern exactly. A second, unrelated
  bug the same test surfaced: the seeded `peer-sync-client` DevIdp client
  only held `peer:sync`, but a real site driving register+publish+sync
  from one caller (this repo's simplification — "a real deployment would
  give each site its own credential") also needs `events:publish`/
  `registry:admin` — added both, same both-roles-in-one-caller posture
  `telemetry-client`/`attachments-client` already established.
  **Built-scope note**: Merkle-tree catch-up (`ADR-033`'s named efficiency
  optimization) is NOT built — a plain `PeerSyncCursor`-based full
  resync-since-last-ack is used instead, functionally correct (converges,
  flags genuine conflicts) but not bandwidth-efficient for a
  long-disconnected peer. Honestly flagged in `docs/08-build-plan.md`'s
  own section, not silently dropped. Cross-shard fan-out remains deferred
  to "GraphQL-Only Query Layer," per this item's own pre-existing note.
  `EventStore.IntegrationTests` now has 45 `[TestMethod]`s (up from 41) —
  `ReplicationSqliteTests`/`Postgres`/`SqlServer` (5 scenarios each) plus
  one dedicated real-HTTP `ReplicationHttpSqliteTests` proving the actual
  wire/auth path (`peer:sync` scope enforcement, real DPoP-proof
  generation against a resolved absolute URI, the `/peer-sync/whoami`
  handshake) between two real `WebApplicationFactory` Hosts. All pass
  reliably alone and in the SQLite-only subset; the full multi-provider
  run has an unrelated, pre-existing flake (see `TODO.md`'s new entry) —
  a rotating single SQL Server test class occasionally fails its own
  Testcontainers `ClassInit` under resource contention from running
  several `MsSqlContainer`s in one process, never the same class twice,
  always passing standalone. Not caused by this item's changes.
- **Item 18, "Non-Authoritative Capture," is Done — same day, continuing
  directly from item 17.** New `LiveEntityStoreRow`
  (`EventStore.Domain/EntityStore`, shape straight from
  `docs/data/entity-store.md`) folded by a new `RouterWorker.FoldLiveAsync`
  — the ungated counterpart to the existing `FoldAsync`, called
  unconditionally for every event (no `AuthorityStatus` gate, no late-
  arrival ordering guard of its own — that's specifically the
  authoritative view's concern, `ADR-024`/`029`). `RouterWorker.
  ProcessEventAsync` now only calls the existing `FoldAsync` (the
  authoritative Entity Store) when `storedEvent.AuthorityStatus ==
  "accepted"` — an `unattested`/`pending_review` event still reaches the
  Live View immediately but the authoritative store gets no row at all
  until acceptance (`ADR-042`'s gate, narrower than `ADR-035`'s original
  "folds identically" framing). `PublishService`/`PublishEventRequest`
  gained `AttestedActorId`/`AttestedClaims`/`ReviewPending` and compute
  `AuthorityStatus` at publish time per `ADR-042`'s two triggers
  (self-attested claims → `unattested`; an explicit review-pending marker
  → `pending_review`; otherwise the ordinary-publish default,
  `"accepted"`). New `EventStore.Router/AuthorityDecisionResolver.cs` — a
  "special-purpose reactor" (the same shape `ADR-020`'s
  `EventUpcastFailed` handling and `ADR-027`'s materialization already
  use) invoked whenever `RouterWorker` processes an `authorityDecision`
  event (itself just an ordinary, explicitly-registered event type, not a
  reserved platform one): annotates the target's `AuthorityStatus`/
  `AuthorityDecisionRef`, catches the authoritative Entity Store up on
  acceptance (`RouterWorker.FoldAsync`, reused, the same "apply once, on
  the triggering condition" shape `ADR-027`'s catch-up already
  established), and — only for the narrow residual case `ADR-042`
  actually leaves to `RejectionBehavior.Compensate` (already accepted and
  folded, now reversed) — appends a new compensating-patch `StoredEvent`
  whose payload explicitly nulls exactly the properties the rejected
  event itself contributed, folded as `ChangeKind.Partial` regardless of
  the original type's own `ChangeKind` (`EntityDataMerger`'s existing
  "an explicit JSON null clears a key" rule, reused exactly as designed
  for this). `RegisterEventTypeRequest`/`SchemaRegistryService.
  RegisterAsync` gained `RejectionBehavior` parsing (the
  `EventTypeDefinition` field and its DB column already existed from
  earlier scaffolding, just never actually settable at registration until
  now).
  **A real, found-and-fixed gap in item 17's own code**: `ReplicatedEventPayload`
  never carried `AuthorityStatus`/`AttestedActorId`/`AttestedClaims`
  across the wire — a peer-synced `unattested` event would have silently
  reset to `StoredEvent`'s own `"accepted"` class default at the
  receiving site, never caught until an event with a non-default
  `AuthorityStatus` actually existed to replicate (nothing did, until this
  item). Fixed by adding all three fields to the payload record and
  wiring them through `PeerSyncWorker.ToPayload`/`PeerSyncReceiver.
  ReceiveAsync`.
  **A real, found doc inconsistency, not caused by this pass**:
  `comparisons/authority-rejection-behavior.md`'s own "Refinement" section
  recommends a "targeted single-entity rebuild" mechanism for the
  Annotate case that neither `ADR-035`'s Decision text, `docs/data/
  schema-registry.md`'s field comment, nor `docs/features/non-
  authoritative-capture.md`'s own Gherkin ever adopted or reflect — all
  three still describe/test the plain, un-refined "flag only" shape,
  which is what got built (matching the item's own actual exit criteria).
  Recorded as `docs/10-open-questions.md` row 1 rather than silently
  picking a side.
  **Built-scope note**: `ADR-036`'s real DID/UCAN offline chain
  verification and the actual RFC 8693 OAuth Token Exchange bridge
  endpoint are NOT built — `AttestedClaims` stores an opaque,
  credential-agnostic JSON blob, never cryptographically verified,
  exactly matching the feature doc's own explicit "credential-agnostic"
  scope. No pre-GraphQL HTTP query surface for the Entity Store/Live View
  exists either (none did before this item) — tests query
  `db.EntityStore`/`db.LiveEntityStore` directly, the same "exercise the
  mechanics directly" posture item 17's own tests already use.
  `EventStore.IntegrationTests` now has 48 `[TestMethod]`s (up from 45) —
  `NonAuthoritativeCaptureSqliteTests`/`Postgres`/`SqlServer` (one
  `AllNonAuthoritativeCaptureScenarios` method each, 9 scenarios). All 48
  pass reliably alone and as the SQLite-only subset; the full
  multi-provider run hit the same pre-existing SQL Server Testcontainers
  resource-contention flake as item 17 (see `TODO.md`) — this pass found
  the clearest evidence yet of its root cause (`fs.aio-max-nr` kernel AIO-
  context exhaustion from cumulative `MsSqlContainer` starts this
  session, not a code defect), confirmed by every affected class passing
  cleanly standalone.
- **Item 19, "GraphQL-Only Query Layer," is Done — the largest item this
  session, same day, continuing directly from item 18.** New
  `EventStore.GraphQL` project, HotChocolate.AspNetCore 16.5.1 (a version
  whose API genuinely differs from every doc sample findable by search —
  `ObjectTypeDefinition`/`ObjectFieldDefinition` were renamed
  `ObjectTypeConfiguration`/`ObjectFieldConfiguration` between the
  versions those samples covered and v16 actually installed here; every
  non-trivial API call in this item was verified against the real
  installed assemblies via a throwaway reflection scratch project before
  being written, not assumed from search results — this project's own
  "verify before citing" rule, applied under real pressure this time).
  Three static, hand-written surfaces (`[ExtendObjectType]` on empty
  `Query`/`Mutation` roots, ordinary reflection-inferred GraphQL types —
  needed none of the dynamic machinery below): `RegistryQueries`
  (`eventTypes`/`eventType`, reusing `SchemaRegistryService` unchanged),
  `LineageQueries` (`event(eventId) { ancestors descendants parents
  children }`, reusing `LineageService` unchanged), `RevealFieldMutation`
  (the actual reveal-on-demand round trip "Property-Level Masking" could
  only build half of — navigates the target event's own registered
  schema to the `x-masking.requiredClaim`, checks it, returns the real
  value or a GraphQL error). One genuinely dynamic surface: Follow's own
  per-registered-event-type Subscription fields, built via
  `FollowSubscriptionTypeModule` (`ITypeModule`, HotChocolate's real hot-
  reload mechanism) — one payload `ObjectType` and one `on_{appId}_
  {eventType}` field per active event type, reusing `EventTailReader`
  UNCHANGED underneath (`ADR-037`'s own claim, proven by literal code
  reuse, not just asserted) via a hand-rolled `ISourceStream`
  implementation (the doc-shown `SourceStreamWrapper` turned out
  `internal` when checked against the real assembly — a one-method
  interface, trivial to implement directly once found). A maskable
  property's field resolves to one of four new static `MaskedString`/
  `MaskedFloat`/`MaskedBoolean`/`MaskedDateTimeOffset` types
  (`{value, masked, erased}`), read from `IPayloadMasker`'s existing
  JSON output unchanged. `GraphQlFilterPredicateBuilder` is the new
  GraphQL-native filter translator (a static `[EventFilterInput!]` list,
  not a dynamic per-type input object — see the build-plan's own Built-
  scope note for why), reusing `FilterPredicateBuilder`'s own property-
  access/constant-expression building blocks (made `public`, not
  duplicated) so the per-provider JSON pushdown mechanism is proven
  identical to the OData era by literal code sharing. `/graphql`'s
  `QUERY`-method endpoint (`ADR-012`) is hand-mapped
  (`GraphQlEndpoints.cs`) since `MapGraphQL()` only maps GET/POST/
  WebSocket — manually invokes `IRequestExecutorProvider`/
  `IRequestExecutor.ExecuteAsync`, formats via `HotChocolate.Transport.
  Formatters.JsonResultFormatter` (found only by tracing HotChocolate's
  OWN internal `AcceptMediaType` type, needed by the "obvious"
  `IHttpResponseFormatter` path, turning out to have an `internal`-only
  constructor — `JsonResultFormatter` needs no such type at all), and
  streams a Subscription's own results as SSE frames (the same transport
  Follow's pre-GraphQL endpoint already used, now carrying a GraphQL
  document instead of an OData string). Depth limiting
  (`AddMaxExecutionDepthRule(15)`) and cost analysis
  (`HotChocolate.CostAnalysis`, `MaxFieldCost = 10_000`) both wired and
  proven — the depth limiter test uses GraphQL's own naturally-recursive
  introspection schema (`__schema { types { fields { type { fields
  {...} } } } } }`) to exceed the limit, since nothing in this item's own
  schema is deep enough otherwise.
  **Three real bugs found only by actually running this over real HTTP,
  not by reading the code back**: (1) a resolver parameter typed plain
  `ClaimsPrincipal` uses HotChocolate's OWN built-in "well-known
  parameter" binding, which failed ("Could not resolve the claims
  principal") when the request was built manually rather than through
  `MapGraphQL()`'s own pipeline — fixed by marking every such parameter
  `[Service]` to force ordinary DI resolution instead (registered via
  `services.AddScoped(sp => sp.GetRequiredService<IHttpContextAccessor>
  ().HttpContext!.User)`); (2) a dynamically-built field's name must be
  explicitly camelCased (`OrderId` -> `orderId`) — HotChocolate only
  applies this convention automatically to a REFLECTED C# property, never
  to a raw string passed to `ObjectFieldConfiguration`, caught by a
  real "field does not exist" GraphQL error; (3) a Subscription field's
  OWN resolver must read the streamed value via
  `ctx.GetEventMessage<T>()`, never `ctx.Parent<T>()` (which returns the
  `Subscription` root marker object for a root-level field, not the
  yielded message) — caught by a real "unable to cast the parent type"
  error, `[EventMessage]`'s own low-level equivalent found via reflection
  once the annotation-based pattern's underlying mechanism was needed
  directly.
  **A fourth, unresolved-in-full, honestly-flagged limitation, found
  after extensive direct debugging** (Console-instrumented runs across
  many real test iterations, a parallel independent `EventStoreContext`
  proving a registration's own commit is real and immediately visible):
  `FollowSubscriptionTypeModule`'s hot-reload never actually re-invokes
  `CreateTypesAsync` against an already-running Host, no matter how the
  reload is triggered (`ISchemaChangeNotifier.NotifyChanged()`, confirmed
  to fire and reach a real subscriber; a 2-second periodic-`Timer`
  fallback, which never ticked a second time either — most likely because
  whatever HotChocolate-internal scope builds the schema disposes the
  type modules it resolves once done, silently stopping a `Timer` field
  kept on this same long-lived singleton, which is why this class no
  longer implements `IDisposable` at all). Worked around for this item's
  own exit criteria (which never actually requires hot-registering
  against a live Host) by seeding the one Subscription-over-HTTP test's
  own event type directly into the database before the Host starts —
  this DOES prove the dynamic schema construction mechanism itself is
  correct. Tracked as a real, open follow-up in `TODO.md`, not silently
  dropped.
  **Built-scope note** (the fuller version is in `08-build-plan.md`'s own
  section): AppId-qualified shared-schema naming instead of a literally
  separate SDL per `AppId`; the static filter-input list (narrowing
  ADR-037's schema-level "undeclared field" guarantee to a runtime check,
  for filtering only — the guarantee still holds at full strength for
  Subscription field/payload names); Lineage's plain `first`/`skip`
  instead of a Relay Connection wrapper (matching the doc's own shown flat-
  list example); no generic entity/`extensions: JSON` query (nothing
  built here ever needs one); DataLoader/cross-shard fan-out genuinely not
  applicable (no per-node N+1 pattern exists, and no physical multi-shard
  deployment exists to fan out across); `ADR-036`'s real DID/UCAN
  verification and `revealField`'s step-up-auth/`AccessLogEntry` halves
  deferred to their own later items, unchanged from item 18's own framing.
  `EventStore.IntegrationTests` now has 56 `[TestMethod]`s (up from 48) —
  `GraphQlHttpSqliteTests` (5 real-HTTP scenarios: registry listing,
  lineage traversal + scope rejection, the depth-limiter rejection,
  `revealField` with/without the claim, and a real Subscription streamed
  as SSE) plus `GraphQlFilterPredicateBuilderSqliteTests` (3 scenarios —
  SQLite-only, deliberately: the per-provider native SQL generation
  underneath is REUSED UNCHANGED from "Follow API + Filter Pushdown,"
  already proven on all 3 providers there; re-proving it a fourth time
  here would test nothing new). All pass reliably alone and in the
  SQLite-only subset; the full multi-provider run (twice, for stability)
  hit the same pre-existing, unrelated SQL Server Testcontainers
  resource-contention flake already tracked in `TODO.md` (a different
  rotating class each time, confirmed unrelated to this item's own
  changes).
- **Item 20, "Compatibility & Deployment Discipline," is Done — same day,
  continuing directly from item 19.** Much smaller than 19: `ADR-038`'s
  own Consequences already said "no new mechanism is introduced here that
  this design didn't already have a piece of," and that held for three of
  its four pieces. The one genuinely new production-code change: `EventStore.
  Router/RouterWorker.cs`'s `ProcessEventAsync` now resolves
  `activeDefinition` before the schema-status check (hoisted up from
  further down the method, unchanged otherwise) and, when an event's own
  declared `SchemaVersion` is both unregistered AND newer than the active
  version, leaves it at `Status: received` and returns early instead of
  advancing to `applied` — this is the literal rollback-drill exit
  criterion, realized as a narrow forward-incompatibility gate rather than
  a new backlog-reconciliation mechanism: the very next `RunOnceAsync` tick
  already re-queries `Status == "received"`, so the same event is simply
  picked up again, this time successfully, once a later registration
  raises the active version to cover it. Deliberately narrower than
  "declaredDefinition is null" alone — an old/never-registered version
  (`SchemaVersion <= active`) is the ordinary, already-covered "unknown
  schema, advisory-only" case and is untouched by this gate, confirmed by a
  dedicated regression scenario. New `EnumFallbackSchemaValidator`
  (`EventStore.SchemaRegistry`, mirroring `MaskingSchemaValidator`'s own
  shape) validates `x-enum-fallback` at registration time (must be boolean,
  string-typed property only, requires a non-empty `"enum"` array on the
  same property, mutually exclusive with `x-masking`). `EventTypeSchemaReader`/
  `FollowSubscriptionTypeModule` (`EventStore.GraphQL`) read that
  annotation and add a sibling `{name}Known` Boolean field alongside the
  ordinary value field in Follow's dynamically-built Subscription payload
  type — the enum-fallback contract's `{status, statusKnown}` shape.  New
  `CapabilitiesQueries.cs` (`EventStore.GraphQL`) — a small, self-contained
  `capabilities(appId, name, supportedSchemaVersions)` static Query field
  (gated by `events:follow`, the same scope Follow's own connect-time check
  uses), computing a fixed numeric `[active-1, active, active+1]` window
  and throwing a `GraphQLException` when the caller's declared versions
  don't overlap it — deliberately NOT threaded through
  `FollowSubscriptionTypeModule`'s own already-intricate dynamic
  Subscription field (which the feature doc's own diagram is itself
  flagged as "this doc's own structural choice, not a shape ADR-038
  states"). Expand/Contract migration discipline needed no code at all —
  every migration in this repo has already been purely additive.
  `EventStore.IntegrationTests` now has 61 `[TestMethod]`s (up from 56) —
  `CompatibilitySqliteTests`/`Postgres`/`SqlServer` (one
  `AllCompatibilityScenarios` method each, 2 scenarios: the rollback drill
  itself, and the old-version-is-unaffected regression check) plus
  `CompatibilityGraphQlHttpSqliteTests` (2 real-HTTP scenarios: the enum-
  fallback sibling field over a real SSE-streamed Subscription, and
  `capabilities` accepting a client inside the N-1/N+1 window while
  rejecting one outside it). All pass reliably alone and in the SQLite-only
  subset; the full multi-provider run hit the same pre-existing, unrelated
  SQL Server Testcontainers resource-contention flake already tracked in
  `TODO.md` — this item's own 3 `CompatibilitySqliteTests`/`Postgres`/
  `SqlServer` runs all passed cleanly.
- **Item 21, "MVVM Client," is Done — same day, continuing directly from
  item 20.** The first item whose actual ADR decision is a real JS/TS web
  client (Vue 3 + Pinia + Vite), not server-side .NET — explicitly checked
  with the user first (a genuine tech-stack fork, unlike every prior
  item's own "which server-side mechanism" question), who chose "build the
  real client" over a C# simulation of the mechanics. New `client-web/`
  npm workspace (matching `06-solution-structure.md`'s own naming), plus
  three small, real server-side additions: `ViewDefinition`
  (`EventStore.Domain.Views`) + a new `EventStore.ViewRegistry` project
  (`ViewDefinitionService`, mirroring `SchemaRegistryService`'s content-
  addressed/versioned pattern, migrated across all 3 providers) exposed via
  a `viewDefinition` GraphQL query + `registerViewDefinition` mutation; and
  `ConflictFlag`/`LateArrivalFlag`/`AuthorityStatus`/`SchemaVersion` added
  as four FIXED envelope fields on every dynamically-built Subscription
  payload type (`FollowSubscriptionTypeModule.BuildEnvelopeFlagFields`) --
  nothing before this item ever needed these exposed over GraphQL.
  Client-side: a hand-rolled IndexedDB wrapper, Pinia outbox/entity-cache
  stores (Model layer), `useEntityViewActions` (Actions/ViewModel-commands
  layer -- dispatches through the outbox, never mutates the cache
  directly, discovers a Subscription's own field set via GraphQL
  introspection rather than hardcoding one demo entity type),
  `EntityView`/`TemplateRenderer` (the ADR's own "small injected binding
  runtime" -- a minimal `{{ field }}` interpolator + a
  `data-command-field`/`data-command-value-from` attribute convention) /
  `GenericFallbackView`/`FlagRow` (the one shared flag convention, used by
  both), and a minimal dependency-free Service Worker + Web App Manifest.
  **One real bug found only by actually running the tests**: Pinia wraps
  every store entry in a reactive `Proxy`, which IndexedDB's structured-
  clone algorithm correctly rejects (`DataCloneError`) -- fixed by having
  `db/indexedDb.ts`'s `put()` round-trip the value through
  `JSON.parse(JSON.stringify(value))` before storing it. 26 Vitest specs
  (outbox durability/restart/apply-once, entity-cache fold, the generic
  fallback never failing, the shared flag convention, the binding
  runtime's interpolation/command dispatch) plus a real `npm run build` and
  a dev-server smoke check (curl against a running `vite` process,
  confirming the app shell/manifest actually serve) all pass/succeed.
  `EventStore.IntegrationTests` now has 66 `[TestMethod]`s (up from 61) --
  `ViewDefinitionSqliteTests`/`Postgres`/`SqlServer` (5 registry scenarios
  each) plus `MvvmClientGraphQlHttpSqliteTests` (2 real-HTTP scenarios:
  registerViewDefinition/viewDefinition round trip, and the 4 envelope
  flags on a real Subscription). Honestly-flagged narrowings: no native
  shell (`WebViewBridge`/`DeviceInput`, later items' own scope) was built,
  web target only; `entityIdField`/`entityType`/`eventType` are explicit
  per-instance launch config, not resolved from a registry:admin-gated
  lookup a follower credential doesn't hold; an unknown property in the
  server's own `Extensions` bag never reaches this client at all today --
  `FollowSubscriptionTypeModule`'s dynamic payload type only ever exposes a
  schema's own declared properties, a data-availability gap, not a
  rendering one; no live browser/Playwright round trip against a real
  running Host was driven (no browser E2E harness exists in this repo yet)
  -- the Vitest suite proves the mechanics, the build/dev-server check
  proves the app is real, but an actual live GraphQL round trip through a
  browser is not exercised this pass.
- **Item 22, "Ticket Exchange for Header-Incapable Clients," is Done —
  same day, continuing directly from item 21.** This item's own "Depends
  on" text assumed "Non-Authoritative Capture" already built RFC 8693
  Token Exchange infrastructure to reuse -- checked and found NOT true
  (that item's own Built-scope note explicitly named the bridge endpoint
  as not built); this item builds it from scratch. `EventStore.DevIdp`'s
  `/connect/token` gains `options.AllowTokenExchangeFlow()` (found only by
  reflecting the real installed OpenIddict 7.6.0 assembly -- `AllowCustomFlow`
  throws for this specific grant type, "already assigned to a standard
  grant type") plus `options.Configure(o => o.RequestedTokenTypes.Add(...))`
  (OpenIddict's own built-in validation otherwise rejects an unregistered
  `requested_token_type`), and a new `TicketStore` (in-process, non-
  persistent, per `auth.md`'s existing "client/token state lives in
  DevIdp" statement). **Two more real constraints found only by actually
  running this against OpenIddict's real pipeline**: (1) `/connect/token`
  unconditionally requires a registered `client_id` for ANY grant type
  reaching it -- incompatible with `ADR-040`'s own `one_time_secret` path
  ("never requires a registered client_id"); resolved with a genuinely
  separate, non-OpenIddict-pipeline endpoint (`POST /oauth/ticket-exchange`)
  sharing the same `IssueTicketAsync` core the `client_id` path uses; (2)
  `IOpenIddictApplicationManager` never exposes a stored `client_secret`
  in plaintext (only validates a provided one, correctly) -- incompatible
  with recomputing an HMAC server-side at introspection time; resolved by
  adding `DevIdpSeeder.GetClientSecret`, reading back from the same
  dev-only plaintext source that file's own header comment already names.
  The resolution hop is a new `EventStore.TicketExchange` project
  (`HmacSigner`, `TicketAuthenticationHandler` -- a second ASP.NET Core
  authentication scheme, additive to JwtBearer, never the default) wired
  onto exactly the two named header-incapable routes (Streaming's
  byte-range playback mode, Attachment retrieval) via `AuthorizeAttribute.
  AuthenticationSchemes` listing both schemes. `DpopValidationMiddleware`
  gained one new early-return (skip when `AuthenticationType == "Ticket"`)
  since a ticket-resolved principal is never DPoP-bound by design and has
  no `Authorization` header to check at all. A new seeded DevIdp client,
  `clinician-spa-client` (named after the ADR/feature-doc's own running
  example), holds the new `Permissions.GrantTypes.TokenExchange`
  permission -- every other seeded client's permissions are unchanged.
  Verified with Attachment retrieval as the concrete header-incapable
  target (an `<img src>`/`<a href>`, named equally alongside `<video src>`
  by the ADR itself); Streaming's byte-range playback mode shares the
  identical wiring, not re-proven a second time. `EventStore.
  IntegrationTests` now has 72 `[TestMethod]`s (up from 66) --
  `TicketExchangeHttpSqliteTests` (6 real-HTTP/direct scenarios: issuance +
  signing + resolution with no Authorization header at all, single-use
  rejection on reuse, wrong-signature rejection that does NOT burn the
  ticket for a later correct retry, the `one_time_secret` path, expiry
  driven directly against `TicketStore`, and confirming an ordinary
  Bearer-authenticated request to the same route is completely
  unaffected). All pass reliably; the full multi-provider run hit the
  same pre-existing SQL Server Testcontainers resource-contention flake
  already tracked in `TODO.md` (4 unrelated classes this run -- Derivation,
  ViewDefinition, Compatibility, Replication -- none touching this item's
  own code).
- **Item 23, "Delegated Grants, RBAC, Federated Claims & Read Audit
  Logging," is Done — same day, continuing directly from item 22.**
  `EventStore.Ucan` (new project): `UcanDelegation.Create`/`UcanValidator`
  -- a self-signed JWT (DPoP's own embedded-JWK pattern, reused via a new
  `EventStore.Dpop/SelfSignedJwtVerifier` factored out of
  `DpopProofValidator`), carrying `iss`/`aud`/`appId`/`cap`/`exp`/`jti`
  and an optional `prf` (the granter's own currently-valid access token,
  embedded verbatim -- UCAN's "chain of proofs," narrowed to one hop).
  `AppTrustRoot`/`Role`/`RoleAssignment`/`UserPermission`/
  `TrustedFederationIssuer`/`FederatedIdentityMapping` all live in
  `EventStore.DevIdp`'s own `DevIdpDbContext` (EF InMemory), not
  `EventStoreContext` -- DevIdp has no live dependency on any Host's own
  database, and all six are consulted exclusively by DevIdp's own
  token-issuance/exchange logic (a deliberate narrowing from
  `docs/data/schema-registry.md`'s documentation-grouping-only
  placement). `EventStore.DevIdp/Program.cs`'s `/connect/token` gained a
  third Token Exchange use (`ExchangeUcanDelegationAsync`/
  `ExchangeFederatedTokenAsync`, sniffed via the subject_token's own JOSE
  `typ` header) alongside client_credentials and item 22's ticket
  issuance, plus opt-in RBAC permission-flattening on the ordinary
  client_credentials path (`app_id` form parameter, additive-only).
  **The single hardest problem this item hit, found only by actually
  running the exchange against the real OpenIddict 7.6.0 assembly
  (decompiled with `ilspycmd` to confirm, not guessed)**: no
  `subject_token_type` value -- not `"access_token"`, not RFC 8693's own
  generic `"jwt"`, not a wholly custom unregistered URN -- stops
  `AllowTokenExchangeFlow()`'s own built-in subject_token signature
  re-validation (against THIS server's own signing keys) from running a
  SECOND time during `Results.SignIn` itself, after this item's own
  exchange logic had already validated and approved the same token
  upstream. `ValidateSubjectToken`'s `RejectSubjectToken` flag is
  hardcoded `true` for every token-exchange request at this endpoint, no
  options switch disables it. Resolved with a `ValidateTokenContext`
  inline event handler (`options.AddEventHandler<...>(...).SetOrder(int.
  MinValue)`, running before OpenIddict's own `ValidateIdentityModelToken`),
  scoped only to this item's own custom `subject_token_type`, setting a
  placeholder principal carrying OpenIddict's own two required internal
  claims (`oi_tkn_typ` via `SetTokenType`, `oi_prst` via `SetPresenters`)
  that a separate downstream handler (`Protection.ValidatePrincipal`)
  otherwise unconditionally rejects a null/presenter-less principal for
  -- three distinct opaque errors (`ID2090`, then "missing oi_tkn_typ,"
  then `ID2184` "no presenter"), each found only by actually running it,
  peeled back one at a time. Two smaller bugs found the same way:
  `RoleService.GetFlattenedPermissionsAsync`'s `.SelectMany(r =>
  r.Permissions)` over a `HasConversion`-mapped `List<string>` property
  couldn't be translated by EF Core (fixed by materializing via
  `ToListAsync()` first, flattening client-side); and a `MapDelete`
  handler bound an inferred JSON body parameter, which ASP.NET Core
  Minimal APIs only support for POST/PUT/PATCH -- this one manifested as
  an opaque "the discovery document fetch failed" for every DevIdp-backed
  test in the whole suite (any endpoint-metadata-inference failure
  poisons a TestServer's first request), not a scoped failure, making it
  the more confusing of the two to isolate. **ADR-045 (`AccessLog`)**:
  `AccessLogEntry`/`AccessLogEntryHash` (`EventStore.Domain.AccessLog`),
  `AccessLogAppender` (`EventStore.Persistence`, mirrors `EventAppender`'s
  own read-prior-state/insert/compute-chain/update shape exactly, its own
  independent hash chain never coupled to the Event Log's), migrated into
  `EventStoreContext` across all 3 providers (`dotnet ef migrations add
  AddAccessLog`) -- physically co-located with `EventStoreContext` the
  same way Streaming/Attachments already are, despite `ADR-045`'s own
  text reading "not inside EventStoreContext"; `docs/data/streaming-and-
  attachments.md`'s own actual implementation already established this
  precedent (a logically-separate data plane, not a separate physical
  DbContext), so this follows it rather than the ADR prose literally. A
  new `GET /access-log/verify` endpoint (`AccessLogChainVerificationService`,
  mirrors `ChainVerificationService`). Wired into 4 read surfaces:
  `RevealFieldMutation` (`Action: "reveal"`), `LineageQueries.
  GetEventAsync` (`Action: "query"`), `AttachmentService.RetrieveAsync`
  (`Action: "download"`, covering item 22's ticket-authenticated path too
  since it's the same route), and `StreamingEndpoints`'s byte-range
  playback mode (`Action: "stream"`) -- the live SSE tail mode
  deliberately not also logged per-sample. `AccessLogReaderContext.
  Resolve` needed BOTH `ClaimTypes.NameIdentifier` (JwtBearer's own
  `MapInboundClaims=true` default remaps a token's literal `"sub"` claim
  before any resolver sees it) and the literal `"sub"` claim type
  (`TicketAuthenticationHandler`'s replayed claims, validated directly via
  `JsonWebTokenHandler`, never remapped) -- checking only one silently
  produced `"unauthenticated"` for whichever path wasn't checked, caught
  by running the new tests, not by reading the code back. `ReaderTrustBasis`
  is `"Attested"` only for a token minted via the UCAN-delegation exchange
  path specifically (a new `trust_basis` claim, plus `grant_ref` sourced
  from the delegation's own new `jti`); a federated-claims-augmented token
  is deliberately `"Authoritative"` -- its claims came from a real,
  already-verified external IdP the caller directly authenticated with,
  not a self-attestation or delegation chain, and `ADR-045`'s own Attested
  definition names only `ADR-036`/`ADR-043`, never `ADR-047`.
  `EventStore.IntegrationTests` now has 81 `[TestMethod]`s (up from 72) --
  `DelegatedGrantsRbacFederationHttpSqliteTests` (6 scenarios: entity-
  scoped delegation, over-broad-delegation rejection, AppTrustRoot-rooted
  custom-permission acceptance, untrusted-root rejection, RBAC
  direct-permission-survives-role-change, federated-claims augmentation)
  and a new dedicated `AccessLogHttpSqliteTests` (3 scenarios: an
  ordinary query logs the reader as Authoritative, a revealField call
  logs `Action: "reveal"`, tampering with a past entry is caught by
  `/access-log/verify`). All pass reliably; the full multi-provider run
  hit the same pre-existing SQL Server Testcontainers resource-contention
  flake already tracked in `TODO.md` (4 unrelated classes this run --
  Compatibility, Lineage, Attachment -- plus the also-pre-existing
  cross-test-class SSE orderId flake, none touching this item's own
  code). `ADR-036`'s own self-attestation issuance flow through the now-
  proven Token Exchange bridge, and its offline DID/UCAN chain
  verification, remain not built -- flagged in `08-build-plan.md`'s
  own "GraphQL-Only Query Layer" AND this item's own Built-scope notes
  rather than silently dropped, no later item's exit criteria names
  either one yet.
- **Item 24, "SPIFFE/SPIRE Service Identity & API Gateway," is Done** —
  scoped down from `ADR-048`'s own literal "internal services" (plural)
  framing once it became clear the actual build never split `Router`/
  `Fold`/`GraphQL`/`Sharding`/`PeerSync`/`Streaming`/`Attachments` into
  separate deployables at all (they're library namespaces inside one
  `EventStore.Host.<Provider>` process, per `ADR-001`) — confirmed by
  checking every `src/EventStore.*` project for its own `Program.cs`
  before writing any code, not assumed from the ADR's own prose. The two
  genuine inter-process boundaries that actually exist — peer-to-peer
  sync between independent site deployments, and a new Gateway-to-Host
  hop this item itself introduces — are where the real work landed.
  New `EventStore.Spiffe` project: `SpiffeId` (parse/validate `spiffe://
  <trust-domain>/<path>`), `SpiffeTrustBundle` (trust-domain -> trusted
  root CAs), `SpiffeCertificateValidator` (chain-to-trusted-root +
  SAN-URI SPIFFE ID match), `SpiffeSvidFactory` (self-signed CA + short-
  lived leaf SVID issuance — stands in for a real SPIRE Server/Agent, Go
  infrastructure with no NuGet package, the same role `EventStore.DevIdp`
  plays for OAuth2), `SpiffeKestrelExtensions.ListenInternalMtls` (a
  dedicated internal HTTPS Kestrel listener, `ClientCertificateMode.
  RequireCertificate`, rejecting at the TLS handshake itself). `EventStore.
  Host.Core` gained `SpiffePeerIdentity`/`SpiffePeerOptions` (self-issues
  this Host's own SVID at startup, builds a trust bundle from configured
  `TrustedPeers`, optionally starts the internal listener) and
  `AddSpiffePeerIdentity`, wired into all 3 Hosts' `Program.cs` in place
  of the old bare `AddHttpClient("PeerSync")` call — additive to, never
  replacing, `ADR-033`'s existing `peer:sync`-scoped OAuth2/DPoP bearer
  auth (`EventStore.Replication.PeerSyncClient` now also presents its
  SVID as a client certificate). New `EventStore.Gateway` deployable:
  real YARP (`AddReverseProxy().LoadFromConfig(...)`), forwarding to the
  Host with the original `Authorization` header intact (the Host still
  does its own real JWT/DPoP validation — not duplicated at the gateway),
  authenticating itself to the Host's internal mTLS listener under its
  own `/eventstore/gateway` SPIFFE path — the same listener peer-sync
  uses, not a second one, via a new `AllowedInternalCallerPaths` list.
  **Two real bugs found only by actually running code, not by reading it
  back**: `X509SubjectAlternativeNameExtension` has no `EnumerateUris()`
  (only `EnumerateDnsNames`/`EnumerateIPAddresses`) — fixed with a direct
  `System.Formats.Asn1.AsnReader` read of the SAN extension's raw DER for
  the `[6] uniformResourceIdentifier` GeneralName; and Kestrel's
  `UseHttps(Action<HttpsConnectionAdapterOptions>)` extension lives in
  `Microsoft.AspNetCore.Hosting.ListenOptionsHttpsExtensions`, not
  alongside `ListenOptions` itself — a missing `using` the compiler error
  didn't make obvious, found by reflecting the assembly directly rather
  than guessing. **A cross-project drift found and partly corrected**:
  `docs/06-solution-structure.md`'s "Project layout" sketch names
  `EventStore.PeerSync` — the real project is `EventStore.Replication`;
  a propagation note was added at that file's top explaining the
  divergence (this item's own scope), with the full project-by-project
  reconciliation tracked as its own `TODO.md` item rather than attempted
  in one pass. All exit criteria verified against a REAL Kestrel HTTPS
  listener and real TLS handshakes (`SpiffeMtlsTests`, 2 scenarios:
  federation accept/reject including untrusted-CA and wrong-identity-
  but-trusted-CA cases, plus the new shared-listener `AllowedInternal
  CallerPaths` mechanism) and a real `EventStore.Gateway` process
  (`GatewayTests`, 1 scenario: routing + header-forwarding). Deliberately
  exercised once, not ×3 — this item never touches a database provider
  at all, unlike every provider-specific item so far.
- **Next up**: item 25, "Data Lifecycle & Backup/Restore Classification"
  (`ADR-056`) — depends on Scaffolding & Persistence (Done).

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
   before adding to it (84 tests should pass). Requires Docker running
   (Testcontainers for Postgres/SQL Server) and the SDK pinned in
   `global.json`. A full multi-provider run has a known, pre-existing,
   unrelated flake — see `TODO.md`'s entry — where one or two SQL Server
   test classes occasionally fail their own container `ClassInit` under
   host resource contention (`fs.aio-max-nr` exhaustion from cumulative
   `MsSqlContainer` starts); re-running just that class alone always
   passes. Don't mistake this for a real regression.

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
  fresh prompt; items 11 through 22 now done too, per the same rhythm).
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
