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
  `GenericFallbackView` is fully navigable — still not literally
  performed, no such software is installable/operable in this
  environment, but narrowed further this session.** Previously: automated
  `axe-core` conformance (zero critical/serious violations, verified both
  under jsdom and, for `color-contrast` specifically, a real headless-
  Chromium cross-check) plus one real fix found by reasoning directly
  about screen-reader behavior (`<th scope="row">` + a visually-hidden
  `<caption>`). **New, 2026-08-13**: `@guidepup/virtual-screen-reader`
  (`client-web/packages/reference-app`, pure JS/TS, no OS-level screen-
  reader engine needed) added — `a11y.virtualScreenReader.spec.ts` (5
  tests) walks the actual simulated accessibility tree over the real
  rendered DOM and proves, rather than reasons about, the exact claim the
  `<th scope="row">` fix made: `GenericFallbackView`'s table announces
  `"row, carrier UPS"` with `"rowheader, carrier"` ahead of `"cell, UPS"`
  in the same row — a genuinely grouped label/value pair, not two
  anonymous cells — plus the Extensions-sourced property's own visual
  `"(Extensions)"` marker actually reaching the tree, the `Retry sync`
  button's own accessible name, and the `ViewDefinition`-template `<dl>`
  pairing its `term`/`definition` roles in navigation order.
  **Still an honest gap, not closed**: this tool's own README states it
  "should not be used as a substitute for testing with real screen
  readers and with real screen reader users" — confirmed directly while
  writing these tests, not just quoted: `FlagRow`'s own `"⚠"` glyph
  carries through as literal text in the simulated tree (`"⚠
  ConflictFlag"`), but whether a REAL screen reader actually pronounces
  that bare Unicode character (documented as inconsistent across real
  AT) is something only an actual NVDA/JAWS/VoiceOver session could
  confirm one way or the other. Investigate, if this needs fully closing:
  a real NVDA (Windows, free) or VoiceOver (macOS, built-in) session
  against the built `client-web` app, specifically confirming the `"⚠"`
  glyph's own pronunciation and any other real-AT-specific behavior this
  virtual simulation can't reach.

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
  **Retried, 2026-08-12 — still blocked, both real, neither stale.**
  `npm outdated` still shows the same two versions (`typescript` 7.0.2,
  `jsdom` 30.0.1 — no newer release of either since this item was
  written) and `vue-tsc` itself is already at its own latest (`3.3.9`),
  so no upstream fix has landed on any of the three packages this gap
  depends on. Bumping each in isolation and re-running the real build/test
  commands reproduced both failures byte-for-byte: `typescript@7.0.2` +
  `vue-tsc build` still throws the identical
  `ERR_PACKAGE_PATH_NOT_EXPORTED` on `typescript/lib/tsc`; `jsdom@30.0.1`
  + the real suite still throws the identical `TypeError: The "event"
  argument must be an instance of Event. Received an instance of Event`
  from `NativeBridgeInputSource.spec.ts`'s real `WebSocket` round trip.
  Both reverted immediately after confirming (`package.json`/
  `package-lock.json` diff-clean against `git diff` afterward, full
  workspace test suite re-passing). Re-check again next time `npm
  outdated` is run against this workspace, not before.
