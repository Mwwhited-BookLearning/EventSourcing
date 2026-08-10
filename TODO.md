# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

## Active

- **`docs/06-solution-structure.md`'s "Project layout" sketch names one
  deployable per internal service (`EventStore.Router`/`.Fold`/`.GraphQL`/
  `.Sharding`/`.PeerSync`/`.Streaming`/`.Attachments`, etc.), but the
  actual build consolidated most of these into library namespaces inside
  each `EventStore.Host.<Provider>` process, and named the peer-sync one
  `EventStore.Replication`, not `EventStore.PeerSync` as sketched.**
  Noticed while building "SPIFFE/SPIRE Service Identity & API Gateway"
  (item 24) checking ADR-048's own flagged propagation gap ("each
  internal service project [needs] its SPIFFE ID convention annotated");
  a one-line propagation note was added at that file's top explaining the
  divergence for THIS item's own purposes, but reconciling the entire
  project-layout listing against what every prior item actually built
  (which projects are real deployables vs. consolidated namespaces, which
  names changed) is a larger sweep across many already-Done items, not
  scoped to one. Investigate: read every `src/EventStore.*` project's own
  `Program.cs` (or its absence) against this file's list, correct each
  mismatch, and add a standing note about which split is authoritative
  going forward.
- **`FollowSubscriptionTypeModule`'s dynamic Subscription schema
  (`EventStore.GraphQL`, "GraphQL-Only Query Layer") only reflects
  whatever event types are active at Host warmup — registering a new
  event type afterward never makes its `on_{appId}_{eventType}`
  Subscription field appear without a process restart.** Confirmed, by
  extensive direct debugging (Console-instrumented runs, a parallel
  independent `EventStoreContext` proving the registration genuinely
  commits and is immediately visible to any OTHER connection), that this
  is not a registration bug: `ISchemaChangeNotifier.NotifyChanged()` does
  fire and does reach a real subscriber inside HotChocolate's own
  executor machinery, but no second `CreateTypesAsync` invocation ever
  follows — not immediately, not after a 2-second periodic-timer fallback
  retried for over a minute of real wall time. Most likely explanation,
  not fully proven without reading HotChocolate's own source: whatever
  internal scope builds the schema disposes the type modules it resolves
  once building finishes, silently stopping a `Timer` field kept on this
  same long-lived singleton. Worked around for this item's own exit
  criteria by seeding the one test that needs a live Subscription
  directly into the database before the Host starts, proving the dynamic
  schema CONSTRUCTION mechanism itself is correct — the gap is narrowly
  about hot-registering against an ALREADY-RUNNING Host. Investigate:
  read HotChocolate's actual `RequestExecutorManager`/type-module-disposal
  source, or try `IRequestExecutorManager.EvictExecutor(schemaName)`
  explicitly alongside `TypesChanged` instead of relying on the event
  alone.
- **`dotnet test tests/EventStore.IntegrationTests` intermittently fails
  one or two SQL Server test classes per full run, a different class (or
  pair) failing each time** (seen: `DerivationSqlServerTests`, then
  `AllUpcastMaterializationScenarios`/`UpcastMaterializationSqlServerTests`,
  then `AllStreamingScenarios`/`StreamingSqlServerTests` +
  `InsertAndReadBackStoredEvent`/`SqlServerRoundTripTests` together in one
  run) — a Testcontainers `MsSqlContainer` readiness-check timeout, a Docker
  exec-call error, or (the clearest evidence yet, this pass) the container
  itself crashing on startup with exit code 134/1 and stderr `"Unable to
  create a new asynchronous I/O context. Please increase sysctl
  fs.aio-max-nr"` — a Linux/WSL2 kernel-wide AIO-context limit exhausted by
  however many `MsSqlContainer`s this session has cumulatively started,
  not any one test's fault. Every failing class passes cleanly when run
  alone (confirmed four separate times now, across four different
  classes), and which class(es) fail rotates run to run — conclusively a
  host resource-exhaustion issue, not a code defect in any item. First
  noticed during "Sharding & Replication" (item 17)'s own full-suite
  regression run; the `fs.aio-max-nr` evidence surfaced during "Non-
  Authoritative Capture" (item 18)'s. Investigate: raise the host/WSL2
  `fs.aio-max-nr` sysctl, run SQL Server test classes with reduced
  parallelism, or a longer/more tolerant wait strategy on `MsSqlBuilder`,
  if this keeps costing re-run time in future sessions.

- **`StoredEvent.AppId` now exists (added by "Entity-Centric Core Rebuild",
  `ADR-021` — the dedicated fix `docs/10-open-questions.md`'s former row 1
  named as one resolution path, now closed and deleted), but only
  `EventStore.Router`'s own schema/entity resolution was rewired to use it
  directly.** `SchemaRegistryService.GetActiveClaimsByNameAsync`/
  `GetActiveClaimsByNamesAsync`/`GetActiveChangeKindByNameAsync` (used by
  Follow's connect-time claim gate, Follow's per-parent visibility check,
  and Lineage's own claim checks — all bare-`EventType`-name, tie-broken by
  `AppId` ordering on a genuine collision) could now resolve unambiguously
  by reading each `StoredEvent.AppId` directly instead, for every call
  site that already has the `StoredEvent` in hand (Follow/Lineage both
  do). Not done in the same pass as adding the column — a real, scoped
  follow-up, not a fresh design question (the fix is already decided: use
  `StoredEvent.AppId`; only the doing is left across those specific call
  sites).
- **`EventStore.AppHost`'s Postgres database resource doesn't reliably
  finish auto-creating before `EventStore.Host.Postgres`'s first
  connection attempt.** `Aspire.Hosting.PostgreSql` 13.4.6's `AddDatabase
  ("Postgres")` documents that "the database being created on the
  Postgres server ... happens automatically as part of the resource
  lifecycle," but running `aspire run` against a clean checkout
  (`src/EventStore.AppHost`) repeatedly showed Postgres itself become
  ready, then a single `FATAL: database "Postgres" does not exist` with
  no further retry/creation logged, and `EventStore.Host.Postgres` never
  starts. `WaitFor(db)` on the database resource (already present in
  `AppHost.cs`) didn't close this gap. Everything else about the Auth
  item's live-orchestration verification checked out (real token issuance
  from a live `EventStore.DevIdp`, `.WithDataVolume()` + a stable
  persisted password surviving restarts, `RequireHttpsMetadata`/
  `Authority` env-var injection all correct) — this is narrowly about the
  database resource's own creation timing. Investigate: an explicit
  `.WithCreationScript(...)`, a longer/explicit health-check retry
  before the dependent resource's first connection, or filing/checking
  an upstream Aspire issue. See `docs/08-build-plan.md`'s "Auth
  (OIDC/OpenIddict) + Orchestration" section's own note for the full
  list of *other* real orchestration bugs this same pass found and
  fixed.
- **`EventUpcastFailed`/`ChannelLagDetectedEventType`/`EntityErasureRequestedEventType`
  (reserved, platform-owned event types) are NOT excluded from
  `SchemaRegistryService.ListAsync`'s own per-AppId listing, unlike
  `SchemaRegisteredEventType` (excluded while building "Control-Plane
  Actions as Reserved Events," item 30, after `ListingSupportsTopAndSkip
  Pagination`'s own count assertion broke the moment any AppId's first
  registration also triggered `SchemaRegistered`'s bootstrap).** The three
  older reserved types happen not to have surfaced this same bug only
  because no existing test both (a) registers one of them and (b) asserts
  an exact count via `ListAsync`/`QUERY /registry` against the SAME AppId
  — the underlying gap (a caller's own type listing can be silently padded
  by ANY reserved type that happened to bootstrap under their AppId) is
  real and pre-existing, just not yet observed. Investigate: either widen
  `ListAsync`'s exclusion to a real `IsReserved`/`IsPlatformOwned` column
  on `EventTypeDefinition` (checked at registration time, not a hardcoded
  name list) or accept the current per-name exclusion approach and add
  each reserved type's own lowercased name to it explicitly.
- **`RbacProjectionWorker`'s own live, cross-process Follow subscription
  (item 30, "Control-Plane Actions as Reserved Events") is not exercised
  end-to-end by any test.** `DelegatedGrantsRbacFederationHttpSqliteTests.cs`
  verifies the Host's real write path (`RbacEndpoints`, scope-gated publish)
  and DevIdp's own fold-target methods (`RoleService`/`TrustRootService`,
  unchanged) directly, but simulates the fold itself rather than running the
  live worker inside the test process — running it hit a genuine
  `WebApplicationFactory` hazard: `BackgroundService.StartAsync` invokes
  `ExecuteAsync` synchronously, inline, until its first real suspension
  point, and `RbacProjectionWorker`'s self-referential "DevIdp" `HttpClient`
  (`FollowClientOptions.ClientId` points back at the same process) recursed
  into `_devIdpFactory.Server` while that same factory was still being
  built one level up the call stack — silently, no exception, no log.
  Worked around in production code with a real one-time startup delay
  (`RbacProjectionWorker.ExecuteAsync`'s own comment), which resolves the
  hazard for this specific case but was never proven against a live,
  in-process `WebApplicationFactory` pair the way the fix was validated.
  Investigate either a real multi-process test harness (two actual `dotnet
  run` processes, no shared-process self-reference at all) or extracting
  `RbacProjectionWorker`'s own tail-and-apply logic into a `CatchUpOnceAsync`-
  style public method (mirroring `ProjectionHost<T>`'s own shape) that a test
  can drive directly, post-ClassInit, without relying on `BackgroundService`'s
  own eager startup timing.
- **`client-web`'s `useEntityViewActions.subscribe()` opens every
  Subscription (the main entity one, and "Local/Edge Active-Scope Caching
  & Erasure Invalidation"'s new `EntityErasureRequested` one) hardcoded
  `mode: TAIL`, with no persisted resume cursor and no `mode: Replay`/
  `fromSequenceNumber` reconnect path at all.** Found while verifying
  item 28's own "a client offline at the moment erasure fires still
  purges correctly once it reconnects" exit criterion — a client that
  reconnects AFTER missing an event while disconnected (not just one
  already connected when it fires) has no guaranteed catch-up today and
  may simply never see it, contradicting `ADR-039`'s own "offline is the
  default assumption" framing for exactly the reconnect case that matters
  most. Pre-existing since "MVVM Client" (item 21) built the subscription
  mechanism, not introduced by item 28 — surfaced here because this is
  the first item whose own exit criteria explicitly depend on reconnect
  behavior being correct. Investigate: persist a per-instance last-seen
  `SequenceNumber` cursor (IndexedDB, alongside the existing outbox/entity-
  cache stores) and reconnect with `mode: Replay&fromSequenceNumber=
  <cursor+1>` instead of blind `Tail` whenever a stopped subscription is
  restarted, the same tail-then-replay-cursor shape `EventTailReader`
  already uses server-side.
- **`docs/06-solution-structure.md`'s solution layout still names an
  `EventStore.Bdd/` project ("Reqnroll/SpecFlow-style step definitions
  for `*.feature` files," extracted from each feature doc's fenced
  Gherkin block "once implementation starts") that ten build-plan items
  in (1 through 10, all Done) has never actually been built.** Every
  item's real tests instead use descriptively-named MSTest
  `[TestMethod]`s / shared `*ScenarioAssertions.cs` methods calling the
  services directly (e.g. `PublishScenarioAssertions.
  PublishingAValidEventSucceeds`) — covering the same Gherkin scenarios'
  intent, just never as literal parsed `.feature` files with step
  definitions. This has been consistent across every item so far, not a
  one-off skip, so it reads as a real (if never explicitly decided)
  divergence from the solution-structure sketch rather than an oversight
  still pending — found while doing a doc-consistency sweep after item
  10, not caused by items 8–10 specifically. Needs an explicit decision:
  either retrofit `EventStore.Bdd` now (a real, sizeable undertaking
  across everything already built) or revise `06-solution-structure.md`'s
  own text to describe the testing approach actually adopted, so the doc
  stops promising a project that, ten items in, evidently isn't coming.
- **`EventStore.slnx` is missing several real, already-built `src/`
  projects** (`EventStore.FeatureFlags`, `EventStore.LeaderElection`,
  `EventStore.Erasure`, `EventStore.Rbac` at minimum) — noticed while
  adding `EventStore.Webhooks` to it (item 34) and finding the list
  already stale going in. Each missing project still builds and runs
  fine (`dotnet build <project>.csproj`/the Host projects that reference
  it directly), so this has never blocked anything — only IDE/`dotnet
  build EventStore.slnx`-at-the-solution-level discoverability is
  affected. Needs a pass reconciling the full `src/` directory listing
  against this file's `<Project Path=...>` entries.
