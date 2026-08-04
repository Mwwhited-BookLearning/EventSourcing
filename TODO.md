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

- **`dotnet test tests/EventStore.IntegrationTests` intermittently fails
  exactly one SQL Server test class per full run, with a different class
  failing each time** (seen: `DerivationSqlServerTests`, then
  `AllUpcastMaterializationScenarios`/`UpcastMaterializationSqlServerTests`)
  — a Testcontainers `MsSqlContainer` readiness-check timeout or a Docker
  exec-call error (`Permission denied` reading `/proc`, `journalctl` not
  found) during `ClassInit`, never during the test body itself. Every
  failing class passes cleanly when run alone (confirmed for both classes
  above), and the specific class rotates run to run — consistent with
  Docker/WSL2 resource contention from spinning up several `MsSqlContainer`
  instances back-to-back in one process, not a code defect in any item.
  First noticed clearly during "Sharding & Replication" (item 17)'s own
  full-suite regression run, but the cause traces to the test
  infrastructure/environment, not that item's changes. Investigate: run
  SQL Server test classes with reduced parallelism, or a longer/more
  tolerant wait strategy on `MsSqlBuilder`, if this keeps costing
  re-run time in future sessions.

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
