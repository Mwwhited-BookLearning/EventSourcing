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

Every item previously tracked here (Naive UI/Vue Router shell,
`style-guide.md`, playbook diagrams/restructure/new playbooks/READMEs,
paged entity-list data grids, configurable-presentation-type charting,
JSON Schema field/dependent-field validation, calculated fields, the
PlantUML `.puml`/Docker-render migration) is done, per the workflow
above: deleted from this file, full narrative in
[`docs/changes/2026-08-28.md`](docs/changes/2026-08-28.md) and
[`docs/changes/2026-08-29.md`](docs/changes/2026-08-29.md).

(The "DSL for user flows/validations/approvals" ask was moved to
[`docs/10-open-questions.md`](docs/10-open-questions.md) row 1, not kept
here — a genuinely undecided fork, not decided work with only the doing
left.)

- [ ] **App.vue's generic "Dispatch a command" demo panel still can't
  actually work against any real Vitals/Meridian schema in a live
  deployment** — found while building `OfflineOutboxSyncPlaybookTests.cs`
  (`docs/changes/2026-09-01.md`), worked around for that test's own
  purposes but not fixed at the product level. Two compounding, separate
  reasons:
  - `submitAmountCommand` (`App.vue`) now merges the currently cached
    entity's own known fields into the patch (this session's fix), but
    the client only ever sees GraphQL-camelCased field names
    (`EventTypeSchemaReader`'s own conversion), never a schema's
    original declared property casing — every real Vitals/Meridian
    schema uses PascalCase (`SubjectId`, `SiteId`, ...), so a merged
    patch's `required`-field names never match what JSON Schema
    validation actually checks against. No client-side mechanism exists
    today to recover a schema's original casing from a registered
    `EventTypeDefinition`.
  - No DevIdp-seeded HTTP client anywhere holds the specific
    `RequiredClaims` any real business event type demands (e.g.
    `PatientScreened`'s `patient:enroll`, `VitalsWorkflowA.cs`) — those
    events have only ever been created in-process by
    `Samples.Vitals.Seed`/`Simulator` calling `PublishService` directly,
    bypassing the HTTP auth layer's claim check entirely. No real
    browser session can dispatch one today, regardless of payload
    correctness.

  `OfflineOutboxSyncPlaybookTests.cs` sidesteps both by registering its
  own throwaway, lowercase-field, no-`RequiredClaims` schema and a new
  `demo-dispatcher-client` (`DevIdpSeeder.cs`) — proves the *outbox*
  mechanism for real, but the demo panel itself remains non-functional
  against any of this repo's actual proving-ground schemas. Fixing this
  for real needs either: a registry endpoint the client can consult for
  a schema's original field casing (closing the first gap), plus a
  decision on whether a generic demo identity should be granted a
  narrow, explicitly-labeled "demo:dispatch" claim per domain (closing
  the second) — or, alternatively, retiring the generic panel in favor
  of a per-domain demo action that already speaks the right shape/claim
  (matching how Vitals/Meridian's own Queue screens already work).

- [ ] **`FollowClient.TailAsync`/`GetChangeKindAsync` should return their
  own discriminated result, not throw for a known outcome** —
  `docs/patterns/known-outcomes-are-not-exceptions.md`'s own named
  follow-on, not done as part of the bug it was found alongside
  (`docs/bugs/framework/service/rbac-fold-404-logged-as-error-forever.md`,
  scoped to `RbacProjectionWorker`'s own `catch` clause only). The
  server side already gets this right — `EventStore.Follow.Api`'s
  `FollowResult` (`Connected`/`UnregisteredEventType`/`Forbidden`/
  `ValidationFailed`) is switched on directly, never thrown — but
  `FollowClient.TailAsync` calls `EnsureSuccessStatusCode()`, converting
  that same well-understood result back into a thrown
  `HttpRequestException` the moment it crosses the HTTP boundary, which
  is why `RbacProjectionWorker` needed its own `catch (HttpRequestException
  ex) when (ex.StatusCode == HttpStatusCode.NotFound)` filter in the
  first place. Mirroring `FollowResult` on the client side (a
  `FollowClientResult`-shaped return instead of a thrown exception)
  would give every caller — `RbacProjectionWorker`, `ProjectionHost`,
  `Samples.Orders.Projections` — the distinction for free, rather than
  each one needing to know and filter on the right `HttpStatusCode`
  itself. Worth doing the next time `FollowClient` is touched for an
  unrelated reason, not urgent enough to justify touching all three
  callers on its own.