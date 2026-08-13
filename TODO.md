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
  about hot-registering against an ALREADY-RUNNING Host.
  **`IRequestExecutorManager.EvictExecutor(ISchemaDefinition.DefaultName)`
  tried, this session, and reverted — found unsafe, not merely
  insufficient.** Calling it alongside `TypesChanged` in
  `FollowSubscriptionTypeModule.NotifyChanged` DOES close the original
  gap (confirmed: a type registered after Host warmup became queryable
  on the very next request, no restart, when tested in isolation). But
  run alongside this suite's own `[assembly: Parallelize(Scope =
  ExecutionScope.MethodLevel)]` concurrency, it surfaced a materially
  worse bug: evicting the cached executor while a DIFFERENT test's
  Follow subscription is still connected can rebuild the schema mid-
  flight and cross-deliver an event published under one `AppId` to a
  subscription's own dynamic, AppId-qualified field for a DIFFERENT
  `AppId`. A schema rebuild is not safe to trigger while any subscription
  may be live against the current executor, and nothing in this codebase
  currently tracks that. **Do not re-attempt `EvictExecutor` without
  first solving that problem** — read HotChocolate's actual
  `RequestExecutorManager`/subscription-rebinding source to understand
  whether it's expected to survive a mid-flight eviction safely (it
  apparently isn't, as configured/used here), or find a way to defer
  eviction until no subscriptions are active.
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
