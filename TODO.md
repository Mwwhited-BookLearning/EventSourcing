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

- **`ADR-095`'s push-notification wake-up layer is proven end-to-end but
  wired into `RouterWorker` only, a deliberate staged rollout, not the
  finished job.** `DerivationWorker`, `WebhookOutboxPump`,
  `PeerSyncWorker`, `ChannelDerivationWorker`, and `ExpectedResponseWatcher`
  still poll on a fixed interval alone, with no `NotifyAsync`/
  `WaitForWakeAsync` wiring at all. Mechanical once a topic name is picked
  per worker and the matching write path (whatever feeds that worker) gets
  a `NotifyAsync` call after its own durable write succeeds — the same
  pattern `RouterWorker`/`PublishService` already establish, no new
  design needed. Pick up if/when the added latency-vs-poll-interval gap
  for any of these five specifically becomes a real, felt problem.

- **`ADR-073`/build-plan item 45's own exit criterion asks for a manual
  screen-reader pass (a real NVDA/JAWS/VoiceOver session) confirming
  `GenericFallbackView` is fully navigable — not performed.** No screen-
  reader software is installable/operable in this environment. Automated
  `axe-core` conformance (zero critical/serious violations, verified
  both under jsdom and, for `color-contrast` specifically since jsdom
  can't determine it, a real headless-Chromium cross-check) was done
  instead, plus one real, concrete fix found by reasoning directly about
  screen-reader behavior (`<th scope="row">` + a visually-hidden
  `<caption>` on the property table, closing a gap no automated tool
  flagged). Automated conformance and a literal manual pass are
  genuinely different checks — industry-standard automated tools catch
  roughly 30-50% of real accessibility issues, the rest need human
  review — so this is a real, honestly-named gap, not equivalent
  coverage under a different name. Investigate: a real NVDA (Windows,
  free) or VoiceOver (macOS, built-in) session against the built
  `client-web` app, specifically tabbing through `GenericFallbackView`
  and confirming each announced label/value pairing and the "Retry
  sync" button's own reachability/announcement.

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
  **Severity update, "Lineage Export & Bitemporal Playback" (item 41)**:
  running the SqlServer-only subset three times in immediate succession
  (`--filter FullyQualifiedName~SqlServer`, no other-provider tests
  interleaved to give Docker breathing room) failed 10-12 of 21 classes
  every single time, each visibly timing out its own container-readiness
  `sqlcmd -Q "SELECT 1"` poll around 28-29s before the container was torn
  down — worse than the "one or two classes" this entry originally
  described, but the SAME root cause (every failing class passes alone;
  zero failures ever named a class this item, or any recent item, added).
  Confirms this is a real, worsening resource-contention ceiling in this
  environment specifically when many `MsSqlContainer`s start back-to-back
  with no other work between them, not a regression to chase per-item.

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
- **The full SQLite regression suite's own known load-induced flakiness
  (see `.claude/context.md`'s repeated notes on
  `SubscribingOverRealHttpStreamsAMatchingEventAsSse` and the leader-
  election renewal-cadence fix) got noticeably worse, once, while
  building item 36** (Bulk Ingestion & Interchange Adapters): one run out
  of four produced 21 failures (vs. the usual 0-2), though every failing
  test passed cleanly on its own and three OTHER full-suite runs
  immediately after came back clean or near-clean (only the
  already-known SSE flake, plus once an unrelated Windows file-lock
  error in `AuthSqliteTests.ClassCleanup` — "process cannot access the
  file... being used by another process," a generic concurrent-SQLite-
  file-deletion race, not caused by this item). No interchange-specific
  test ever appeared in any failing run. Plausible cause, not yet
  confirmed: this item added a 4th real background poller
  (`Hl7V2MllpListener`, real TCP sockets) on top of the 3 already
  running (Router/PeerSync/WebhookOutboxPump), and the tests in this file
  spin up several MORE real `WebApplicationFactory` Hosts than most
  other items' own test files — plausibly enough added load under
  MSTest's 32-way parallelism to occasionally push several OTHER tests'
  own fixed wait margins past the edge in one unlucky run. Investigate if
  this recurs: capture a full, untruncated log (a `tail`-truncated
  capture already lost the one reproduction this session had) and check
  whether the failures cluster around a specific resource (DB file
  handles, TCP ports, thread-pool starvation) the same way the leader-
  election cadence fix's own root cause did.
  **A second, more specific manifestation, found immediately afterward
  while building item 37** (Tenant-to-Tenant Federation Mapping, which
  added 2 more real `WebApplicationFactory` Hosts of its own):
  `DataResidencyHttpSqliteTests.AnAppIdRestrictedToOneRegionReplicatesOnlyToPeersTaggedWithThatRegion...`
  failed once with `PeerSyncCursors.SingleAsync(...) ->
  InvalidOperationException: Sequence contains no elements` — passed
  cleanly in isolation immediately after. Root cause: `PeerSyncWorker.
  SyncOnceWithAsync` is deliberately wrapped in a per-peer
  `catch (Exception) { }` in production (`ADR-033`'s own "one
  unreachable peer never blocks sync with any other" requirement) — a
  genuinely transient HTTP hiccup talking to a shared `TestServer` under
  heavier overall suite load looks IDENTICAL, from this test's own
  perspective, to "that peer was truly unreachable," silently skipping
  cursor creation. **Fixed**: `SyncOnceToAsync` (the test's own helper)
  now takes an `expectedPeerId` and retries `PeerSyncWorker.RunOnceAsync`
  in a bounded loop (150ms between attempts, 10s deadline) until a
  `PeerSyncCursor` row actually appears for that peer, rather than
  assuming one call is enough — mirroring how a real deployment would
  also just retry a transiently-failed peer on its own next tick.
  Verified: `DataResidencyHttpSqliteTests` passed 3/3 in isolation across
  3 separate runs, and the full SQLite regression suite was re-run 3
  more times afterward (0-1 failures each, only the pre-existing SSE
  flake, zero `DataResidencyHttpSqliteTests` failures) — no reproduction
  of either this race or the original 21-failure anomaly above.

- **`client-web`'s own devDependencies (vitest/vite/esbuild) carry a
  known moderate/high/critical vulnerability chain** (`npm audit`, found
  while building "Release Engineering, Packaging & Supply Chain," item
  39) with no non-breaking fix available — `npm audit fix --force` would
  bump vitest across a major version (2.x → 4.x), real risk to that
  client's own test suite/config that wasn't attempted this pass. Every
  affected package is dev-only (the Vite dev server / Vitest UI server),
  never shipped in a production build; `npm audit --omit=dev` reports
  clean, and `.github/workflows/ci.yml`'s own vulnerability-scan job is
  scoped to that flag for exactly this reason, not to hide the finding.
  If this needs closing later: attempt the vitest 4.x upgrade in its own
  isolated pass, run the full client-web test suite before/after to
  isolate any breakage from the version bump itself.
- **`.github/workflows/ci.yml`/`.github/dependabot.yml` have never
  actually been run by GitHub Actions** (item 39) — this environment has
  no push access to trigger a real run, an explicit, deliberate scope
  limit agreed with the user rather than a gap discovered afterward.
  Every command the workflow calls (`dotnet build`/`test`,
  `dotnet list package --vulnerable`, `npm audit --omit=dev`) was
  verified working against this exact repository first; only the YAML
  orchestration itself (both files YAML-parse-checked, not execution-
  checked) is unexecuted. If this repo ever gets a real `origin` with
  Actions enabled: push a branch and confirm the workflow actually goes
  green.
  **Narrowed further, 2026-08-11, direct request**: SBOM generation/
  build-provenance attestation (`sbom-tool generate`, `actions/attest-
  build-provenance`) are no longer part of `ci.yml` at all — moved to a
  local-only `scripts/generate-sbom.sh`, run for real this pass
  (confirmed working standalone). If this ever needs to become a real CI
  job again: the provenance-attestation half specifically has no local
  equivalent at all (it needs a real CI provider's own signing
  identity) — that's the one piece that would need writing fresh, not
  just re-adding the already-proven `sbom-tool` step.

- **A full docs-vs-implementation audit across all 39 completed
  build-plan items (this pass) found and fixed ~65 stale-doc findings
  across 44 files** — narrated in full in `docs/changes/2026-08-11.md`.
  A handful of the findings were CODE-side gaps the docs now correctly
  describe as open, rather than doc bugs to fix; listed here since they
  represent real remaining work, not narrative to restate elsewhere:
  - **`IUpcastExpressionEvaluator`'s CEL/JSONata choice is not actually
    swappable via configuration**, contradicting `ADR-053`'s "no core-
    engine change" claim — `src/EventStore.Upcasting/
    UpcastingServiceCollectionExtensions.cs` hardcodes
    `AddSingleton<IUpcastExpressionEvaluator, CelUpcastExpressionEvaluator>()`;
    `JsonataUpcastExpressionEvaluator` exists but is registered nowhere
    outside a test. Docs now say "a code-level DI edit today, not a
    deployment-time switch" rather than overclaiming. Build a real
    config-driven registration switch if this ever needs to be genuinely
    swappable without a rebuild.
  - **`revealField`'s `ADR-066` step-up-authentication gap did not close
    when "Digital Sign-Off for Regulated Actions" (item 29) landed** —
    item 29 only wired RFC 9470 step-up into publish-time
    `RequiredSignature` enforcement; `src/EventStore.GraphQL/
    RevealFieldMutation.cs` never gained it, and `docs/data/
    schema-registry.md`'s `revealOnDemand` shape has no step-up/Acr
    field at all. Previously implied closed by cross-referencing item
    29; docs now say explicitly it's still open. Build it if a masked
    field with `RequiredSignature`-equivalent step-up protection is ever
    actually needed.
  - **GraphQL browsing of attachments (`entity(id) { attachments {...} } }`)
    was documented and Gherkin-tested as if built; it isn't** — no
    `entity(id)` field or `attachments` field exists anywhere in
    `src/EventStore.GraphQL/`. This was already indirectly explained by
    item 19's own text (no generic "get current entity" query field
    exists), but the re-verification item 16's own note promised, once
    "GraphQL-Only Query Layer" landed, never actually happened until this
    pass. Docs now state the gap as confirmed, not deferred.

- **The ticket-exchange half of "Signing Secret Rotation, Dual
  Signature" (`ADR-093`, item 40) is descoped, not built** — found while
  starting item 40: `ADR-093`'s own claim that "OpenIddict already
  supports a client holding more than one valid credential — no
  framework change needed" was never actually verified before being
  written down, and turned out to be false (verified this pass against
  OpenIddict's own docs/source/issue tracker —
  `OpenIddictApplicationDescriptor.ClientSecret` is a single string per
  application, no built-in multi-secret mechanism). Direct user decision
  once this was found: correct the ADR (done, struck through with the
  real finding) and build only the webhook half of item 40 (also done —
  `WebhookSubscriptionService.RotateSigningSecretAsync`/
  `DiscardPreviousSigningSecretAsync`, `WebhookSigner`'s dual-signature
  emission), rather than improvise a ticket-exchange mechanism on the
  spot. Real zero-downtime rotation for an OpenIddict-registered
  client's `client_secret` still needs one of: (a) a custom OpenIddict
  event handler overriding the default credential-validation pipeline
  to also accept a locally-stored previous secret (DevIdp-side state,
  outside `EventStoreContext`, matching where `client_secret` already
  lives per `ADR-040`'s Consequences), or (b) registering a second
  client application as a temporary stopgap during rotation, accepting
  either `client_id` while both are valid. Neither is built. Pick up
  when ticket-exchange credential rotation is actually needed —
  `docs/features/ticket-exchange.md` and `ADR-040`/`ADR-093` all need a
  follow-up pass once a real mechanism is designed and built.

- **`ADR-029`'s late-arrival guard is per-EVENT, not per-FIELD, and this
  can silently drop a delayed catch-up fold's own genuinely-new fields
  even when they don't conflict with anything.** Found building the
  Vitals proving-ground sample's Workflow D (`docs/domains/clinical-
  trials-device-telemetry/features/intraoperative-monitoring-and-alert-
  response.md`): `IonmAlertRaised` is deliberately non-authoritative
  (`ADR-035`/`042`, pending the neurologist's signed `authorityDecision`)
  while `IonmAlertAcknowledged` (a Partial fold onto the SAME entity) is
  an ORDINARY, immediately-accepted publish that always has a LATER
  `OccurredAt` (it can only ever be published after the alert it
  acknowledges) — so it always folds into the authoritative Entity Store
  FIRST, setting `LastAppliedLogicalTime` ahead of the alert's own. When
  the neurologist's decision finally triggers `IonmAlertRaised`'s own
  delayed catch-up fold, `RouterWorker.FoldAsync`'s late-arrival check
  (`storedEvent.OccurredAt <= row.LastAppliedLogicalTime`) rejects the
  ENTIRE fold as "late" — even though `IonmAlertRaised`'s own fields
  (`Finding`/`Severity`) don't actually conflict with the already-folded
  `AckedBy`. Confirmed by actually running the scenario
  (`VitalsWorkflowDScenarioAssertions.TheNeurologistSignsOffAcceptedAfterSteppingUpAndTheAuthoritativeEntityStoreCatchesUp`),
  not assumed: `AuthorityStatus` correctly reaches `"accepted"`, but the
  Entity Store's own `Data` never gains `Finding`/`Severity`, only
  `AckedBy`. Not a one-off race — deterministic, every time, for any
  workflow shaped like this one (a deliberately-delayed non-authoritative
  capture composing with an ordinary, immediately-accepted Partial
  follow-up on the same entity). Investigate: whether `FoldAsync`'s
  late-arrival check should compare per-property (only skip fields the
  incoming event actually redeclares older data for) rather than
  rejecting the whole merge, or whether `AuthorityDecisionResolver`'s own
  catch-up path specifically needs a different ordering rule than
  ordinary `RouterWorker.ProcessEventAsync` folds — a real design
  question, not yet decided, so left here rather than in `docs/10-open-
  questions.md` (this is "something to investigate," not yet a clearly
  posed fork with named options).

- **`ADR-013`'s RFC 9457 Problem Details decision was never actually
  implemented.** No `AddProblemDetails()`/`UseExceptionHandler`/
  `UseStatusCodePages` registration exists anywhere in `src/` (confirmed
  by grep across the whole repo). 16+ endpoint files
  (`PublishEndpoints.cs`, `SchemaRegistryEndpoints.cs`,
  `LineageEndpoints.cs`, `StreamingEndpoints.cs`, `RbacEndpoints.cs`,
  `LineageExportEndpoints.cs`, `DerivationEndpoints.cs`,
  `InterchangeEndpoints.cs`, and others) return ad hoc anonymous objects
  (`Results.BadRequest(new { error = "..." })`) instead — exactly the
  shape this ADR explicitly rejected. `DpopValidationMiddleware.cs`'s own
  `Results.Problem(...)` call is the one real, correct usage, which only
  highlights how isolated compliance is. `docs/03-api-contracts.md`
  still asserts Problem Details is used everywhere — that claim is
  false against the current code, not just aspirational. Found by a
  full design-compliance audit (ADR-001–019 range). Fix: either wire
  `AddProblemDetails()`/`UseExceptionHandler` globally and migrate every
  ad hoc error response to it, or add an honest additive note to
  `ADR-013` narrowing its own claim and correct `docs/03-api-contracts.md`
  to match reality — a real decision the user should make, not a call
  for a review pass to make silently.

- **`ADR-024`'s conflict-detection Decision explicitly narrows to
  same-property comparison** ("If another patch touching the **same
  property** was already applied since `ExpectedVersion`, set
  `ConflictFlag`... two patches based on the same version touching
  **different** properties both fold cleanly... that is not a
  conflict") **but `RouterWorker.cs` (~line 393-396) only ever compares
  whole-entity `ExpectedVersion != row.Version`, with no property-level
  check at all** — any stale version on any property patch is flagged,
  exactly the false-positive case the ADR calls out as *not* a real
  conflict. `docs/data/event-log.md`'s own comment matches the coarser
  code, not the ADR's decided nuance. No test exercises "different
  properties, same stale version → no conflict." Unlike `ADR-029`'s
  analogous per-entity/per-property tradeoff (explicitly named an
  "acceptable v1 default" with per-property as a documented upgrade
  path, and already tracked above in this file), `ADR-024` states the
  narrow per-property check as the actual default design — this gap was
  untracked anywhere until a design-compliance audit found it. Fix:
  either implement real per-property conflict comparison in
  `RouterWorker.FoldAsync`, or add an additive note to `ADR-024`
  acknowledging the coarser default actually shipped (matching how
  `ADR-029` already handles the same class of tradeoff honestly).

- **`ADR-025`'s Decision (Scalar UI at `/scalar`, a static AsyncAPI UI
  page via `@asyncapi/react-component`) was never built.** No `Scalar`
  package reference anywhere in `src/`, no `/scalar` or `/asyncapi-ui`
  route. `docs/06-solution-structure.md` still shows this only as an
  unverified code *sketch*, never confirmed built. Not its own
  build-plan item, and not previously tracked here or in
  `docs/10-open-questions.md` — an Accepted ADR with no implementation
  and no acknowledgment of the gap, found by a design-compliance audit.
  Fix: either build it (add `Scalar.AspNetCore`, map `/scalar`, add the
  static AsyncAPI viewer page) or add an additive note to `ADR-025`
  narrowing its claim to "documented, not built."

- **`ADR-050`'s `x-required-claims` JSON Schema extension (the
  entity-level spec-extension half of that ADR, distinct from
  `RequiredClaims` itself, which IS genuinely implemented and correctly
  documented) appears nowhere in code — zero hits for that exact
  string anywhere in `src/`.** Separately, the ADR's static
  `[LoggerMessage]`-attribute log-redaction half (as opposed to the
  dynamic, payload-derived `IRedactorProvider` path, which IS built and
  tested) also has no real call site anywhere in this codebase. Found
  by a design-compliance audit (ADR-039–057 range). Fix: either build
  both halves, or add an additive note to `ADR-050` narrowing its own
  claim to what actually shipped.

- **`ADR-057`'s Decision names five `IErasureKeyStore` backends
  (Local, HashiCorp Vault, Azure Key Vault, AWS KMS, Google Cloud KMS);
  only `LocalErasureKeyStore` and `HashiCorpVaultErasureKeyStore` exist
  in `src/EventStore.Erasure`.** `docs/libraries/dotnet/azure-key-vault.md`
  and its AWS/GCP siblings present those three as adopted, with no
  corresponding implementation or SDK package reference in
  `EventStore.Erasure.csproj` — the docs overclaim relative to the
  actual build. Found by a design-compliance audit (ADR-039–057 range).
  Fix: either build the three missing backends, or correct
  `docs/libraries/dotnet/{azure-key-vault,aws-kms,gcp-kms}.md` (whichever
  filenames apply) from "adopted" to "considered, not yet built,"
  per `docs/references.md`'s own adopted-vs-rejected discipline.

- **`ADR-062`'s Decision that `client-web` ships as one or more
  installable npm packages (e.g. `@eventstore/mvvm-client`), with the
  existing Vue app becoming a reference implementation consuming its
  own published package, was never built** — `client-web/package.json`
  is still the one, only app (`"name": "duplex-client-web"`), not split
  into a library + reference app. Unlike every other unfinished piece in
  this design, this gap wasn't named anywhere: not in `ADR-062`'s own
  Consequences, not in build-plan item 39's "Status: Done," not
  previously here. (The NuGet-packaging half of the same ADR IS
  genuinely built — `Directory.Build.props`, `EventStore.Abstractions`
  — this gap is npm-specific.) Found by a design-compliance audit
  (ADR-058–076 range). Fix: either split `client-web` into a published
  library + thin reference app, or add an additive note to `ADR-062`
  narrowing its own claim to the NuGet half only.

- **`ADR-063`'s Decision to adopt FsCheck (property-based tests for the
  hash chain/conflict resolution) and Polly+Simmy (fault-injection tests
  for outbox/inbox crash recovery), "alongside `ADR-055`'s
  `EventStore.UnitTests`," was never built.** There is no
  `EventStore.UnitTests` project at all (only `tests/
  EventStore.IntegrationTests`, confirmed via `EventStore.slnx`), and no
  `.csproj` anywhere references `FsCheck`, `Polly`, or `Simmy`.
  `docs/08-build-plan.md`'s "Cross-cutting, every item" testing section
  explicitly asserts "both adopted now, cheaply, alongside the existing
  MSTest suite" — a factual claim not backed by any code in the repo.
  (`ADR-055`'s own `EventStore.E2ETests`/Playwright gap is a related but
  separate, already-honestly-tracked absence — `08-build-plan.md` and
  this file both already say "no browser E2E harness yet"; this item is
  specifically about the FsCheck/Polly/Simmy half, which wasn't
  previously tracked as missing.) Found by a design-compliance audit
  (ADR-058–076 range). Fix: either build `EventStore.UnitTests` with
  real FsCheck/Polly+Simmy coverage, or correct
  `docs/08-build-plan.md`'s claim from "adopted" to "decided, not yet
  built."

- **`ADR-084`'s readiness-probe Decision — readiness should fail when
  the instance's own primary database is unreachable, while tolerating
  peer degradation — is only half-built: the database-reachability half
  was never implemented.** `EventStore.ServiceDefaults/Extensions.cs`'s
  `AddDefaultHealthChecks`/`MapDefaultEndpoints` only registers one
  always-healthy `"self"` check tagged `"live"`; no
  `Host.<Provider>`/`Program.cs` uses Aspire's health-check-integrated
  DB client APIs (each just calls plain `AddDbContext`). "Tolerates peer
  degradation" reads as true only because nothing is actually checked.
  Health endpoints are also only mapped `if
  (app.Environment.IsDevelopment())`, so even the trivial check isn't
  exposed outside dev. No corrective note anywhere flags this gap.
  Found by a design-compliance audit (ADR-077–094 range). Fix: add a
  real DB-reachability health check per provider, and decide whether
  health endpoints should be exposed (behind auth, presumably) outside
  Development.

- **`ADR-085`'s "Adopt now: BenchmarkDotNet" was never built.**
  `docs/08-build-plan.md`'s "Cross-cutting, every item" section and
  `docs/libraries/dotnet/benchmarkdotnet.md` (which names a specific
  project, `EventStore.Benchmarks`, and a runnable `dotnet run` command)
  both describe it as already wired in — no such project exists, and a
  full scan of every `.csproj` in the repo shows zero `BenchmarkDotNet`
  package references. `docs/libraries/dotnet/benchmarkdotnet.md`'s own
  runnable command would fail today. Found by a design-compliance audit
  (ADR-077–094 range). Fix: either build `EventStore.Benchmarks` with
  real BenchmarkDotNet coverage of the hash chain/conflict resolution
  paths this ADR names, or correct both docs from "adopted" to
  "decided, not yet built."

- **`client-web`'s `typescript` and `jsdom` devDependencies are
  deliberately held back one major version each, not yet at "latest."**
  Found while updating every dependency this session (commit `6716c27`):
  `typescript` is capped at `6.0.3`, not the current `7.0.2` — that's
  TypeScript's new native (non-JS) compiler rewrite, and `vue-tsc`'s
  current release reaches into a `typescript/lib/tsc` subpath that
  `7.x`'s restructured package no longer exports, breaking type-checking
  outright (`vue-tsc -b` throws `ERR_PACKAGE_PATH_NOT_EXPORTED`,
  confirmed directly). `jsdom` is capped at `25.0.1`, not the current
  `30.0.1` — `client-web/src/deviceInput/NativeBridgeInputSource.spec.ts`'s
  real (non-mocked) `WebSocket` round-trip test fails with `TypeError:
  The "event" argument must be an instance of Event. Received an
  instance of Event`, a cross-realm identity mismatch between Node's
  global `WebSocket` (undici) and jsdom's own `Event` class that `30.x`'s
  internal changes introduced. Revisit both: `npm outdated` in
  `client-web/` will show them again once either upstream issue is
  fixed (a new `vue-tsc` release supporting TS 7's native compiler; a
  jsdom release restoring cross-realm `Event`/`WebSocket` compatibility,
  or Node/undici accepting jsdom's `Event` instances again) — bump then,
  not before, and re-run the full `vitest`/`vue-tsc -b`/build sequence
  again before trusting either bump.
