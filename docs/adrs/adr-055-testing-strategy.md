[← ADR index](../07-adrs.md)

# ADR-055: Testing strategy — MSTest+Moq (backend unit), Vitest+Vue Test Utils (frontend unit), Testcontainers (service-level integration/e2e), Playwright (UI action tests)

Status: Accepted

Context: Direction received this session: good test coverage across the
whole stack — unit tests for both backend and frontend code, "even to
the database if using stored procedures (probably not)," service-level
integration/e2e tests, and UI action (browser) tests. Backend preference
stated directly: **MSTest with Moq**, open to reconsidering. UI
automation: no strong preference, prior experience with **Playwright**.

**"Probably not" on stored procedures is confirmed correct** — this
design has none. `ADR-004` stores `Payload`/`JsonSchema` as portable text
columns specifically for cross-provider portability; all three EF Core
providers (`ADR-001`) go through ordinary LINQ, with the sole exception
of `IEventLineageQueryProvider`'s **read-only** `WITH RECURSIVE`
ancestor/descendant traversal (`01-c4-architecture.md`'s Lineage
component diagram) — raw SQL for a query the relational model can't
express portably any other way, never a write-path stored procedure.
There is no stored-procedure layer to unit-test at the database — the
closest equivalent (proving that provider-specific raw SQL behaves
identically across SQLite/Postgres/SQL Server) is already the explicit
job of `EventStore.IntegrationTests`' `Testcontainers`-based suite
(`06-solution-structure.md`), not a new layer.

Decision:
- **Backend unit tests: [MSTest](../libraries/dotnet/mstest.md) + [Moq](../libraries/dotnet/moq.md)**, per direct preference. MSTest v3
  is Microsoft's own first-party test framework (`ADR-041`'s "prefer
  first-party" convention, the same reasoning that already picked
  `Microsoft.Extensions.Compliance.Redaction` over a third-party
  redaction library) — actively maintained, semver since v3.0, and the
  same base classes `Playwright` itself directly supports (see below).
  **One honest flag on `Moq`, not a silent pick**: its 2023 `SponsorLink`
  telemetry incident (auto-collecting hashed email addresses via a
  build-time NuGet package without clear consent) caused real, lasting
  community trust damage — many teams permanently migrated to
  `NSubstitute` even after the feature was removed. `Moq` itself is
  technically fine today and remains widely used; this is recorded so
  the choice is informed, not so it's overridden — proceeding with `Moq`
  per direct preference. Swapping to `NSubstitute` later is a mechanical,
  low-cost change if trust becomes a real concern (no assertions/API
  surface this design depends on are `Moq`-specific).
  **Corrected, 2026-08-12, found by an independent design-compliance
  audit**: `Moq` has zero actual usage anywhere in this codebase — no
  `.csproj` references it, no `Mock<`/`using Moq` appears anywhere,
  confirmed directly. This ADR's own "backend unit tests" layer was never
  built as its own thing at all: `tests/EventStore.IntegrationTests` is
  the one test project that exists, and its own style (real
  `SchemaRegistryService`/`PublishService`/`FollowService` instances
  against a real database, never a mocked dependency) never needed a
  mocking library to begin with. Same "decided, not built" shape as the
  already-tracked `EventStore.UnitTests`/FsCheck/Polly+Simmy gap
  (`TODO.md`, this ADR's own `ADR-063` escalation) — `Moq` itself just
  wasn't named there specifically until now. Resolves once `EventStore.
  UnitTests` is either built for real or that item is formally descoped.
  **Resolved, 2026-08-28**: `EventStore.UnitTests` was built per
  `ADR-063`'s own decision ("adopt now, alongside `ADR-055`'s
  `EventStore.UnitTests`") — FsCheck property tests, Polly+Simmy fault
  injection, plus ordinary pure-logic unit tests added since. `docs/06-
  solution-structure.md`'s own stale "NEVER BUILT" line for it (found
  the same day, independently, while reconciling the file tree against
  this ADR) is corrected in the same pass.
- **Frontend unit tests: [Vitest](../libraries/web/vitest.md) + [Vue Test Utils](../libraries/web/vue-test-utils.md).** `Vitest` is
  maintained by the Vue/Vite team itself, needs zero additional config
  since `ADR-039`'s client is already Vite-based, and has solidified as
  the standard for Vue 3 projects. `@vue/test-utils` is Vue's own
  official low-level component-testing library — the same
  same-vendor-on-both-ends reasoning `ADR-054` already applied to
  `Strawberry Shake`/`HotChocolate`.
- **Service-level integration/e2e tests: the existing `Testcontainers`
  suite (`06-solution-structure.md`), reaffirmed, not replaced.**
  `EventStore.IntegrationTests` already runs the same test suite against
  real SQLite/Postgres/SQL Server instances, exercising the framework's
  real HTTP/GraphQL surface rather than mocks — this **is** this
  design's service-level integration testing layer; nothing new is
  needed to satisfy that part of the request.
- **UI action tests: [Playwright](../libraries/dotnet/playwright-dotnet.md)**, per direct experience/no strong
  competing preference. Microsoft's own, cross-browser (Chromium,
  Firefox, WebKit), actively developed. **Playwright for .NET, using its
  MSTest base classes** — one language, one assertion/mocking/runner
  stack (MSTest) spans backend unit tests, integration tests, and E2E UI
  tests, rather than introducing a second test-runner convention (NUnit/
  xUnit) purely for the UI layer. Drives `ADR-039`'s Vue/MVVM client
  through a real browser against a real running deployment (or a
  `docker-compose`/Aspire-orchestrated test environment, `ADR-026`).
- **New test project**: `EventStore.E2ETests` (Playwright, MSTest base
  classes) — added to the `tests/` layout below
  `EventStore.IntegrationTests`; frontend unit tests live alongside
  `ADR-039`'s client project as its own Vitest suite, not under `tests/`
  (matching how the client is its own top-level solution area already).

Consequences:
- Resolves `docs/10-open-questions.md`'s testing-strategy row for
  ordinary test-pyramid coverage (unit/integration/e2e/UI). **Does not**
  resolve the harder, distributed-correctness testing question (chaos/
  fault-injection for replication convergence, property-based testing
  for hash-chain/conflict-resolution invariants) raised in the same
  review — that remains open as its own, narrower question, since it's a
  different kind of testing (adversarial/generative) than the coverage
  concern this ADR answers; `FsCheck` (property-based, works with
  MSTest) and `Polly`+`Simmy` (fault injection) remain named candidates
  there if it's ever picked up.
- `06-solution-structure.md`'s `tests/` layout gains `EventStore.
  E2ETests`; its existing `EventStore.UnitTests`/`.IntegrationTests`
  naming is otherwise unchanged.
- No stored-procedure test layer is added, since none exists — recorded
  here explicitly so a future reader doesn't wonder whether it was
  overlooked (the same "state explicitly rather than silently disappear"
  discipline this design already applies to `references.md`'s rejected
  items).

**Implementation note, added 2026-08-28**: `EventStore.E2ETests` finally
built, plus a real extension this ADR didn't originally scope — direct
request for automated UI playbooks: screenshots captured at each
meaningful step of a real user workflow, assembled into a markdown user
guide.
- **Not `Microsoft.Playwright.MSTest`'s `PageTest` base class** — found,
  by actually running it, that this package pins `MSTest.TestFramework`/
  `TestAdapter` **2.2.7** and the old VSTest-based `Microsoft.NET.Test.Sdk`
  **17.8.0** (confirmed via its own `.nuspec`), genuinely incompatible
  with the `Microsoft.Testing.Platform`-based MSTest 4.3.3 every other
  test project in this repo already uses — mixing them silently
  discovered zero tests ("No test is available"), not a warning. Fixed
  by referencing plain `Microsoft.Playwright` and managing the browser/
  page lifecycle by hand in `ClassInitialize`/`ClassCleanup`/
  `TestInitialize`/`TestCleanup` — `Microsoft.Playwright.Assertions.Expect`
  is part of the core package, not exclusive to the MSTest adapter, so
  nothing else about the testing experience is lost.
- **Boots the real `EventStore.AppHost` via `Aspire.Hosting.Testing`'s
  `DistributedApplicationTestingBuilder`**, not a human-started `aspire
  run` in another terminal — the whole point of the direct request this
  satisfies ("scripted so these can be updated and extended as needed")
  is that `dotnet test tests/EventStore.E2ETests` alone regenerates a
  playbook end to end: real Postgres, migrator, seed workers, DevIdp,
  `eventstore`, and the pinned `client-web-vitals`/`client-web-meridian`
  Vite instances, exactly as `AppHost.cs` already defines them, no second
  orchestration path to keep in sync. `KnownResourceStates.Running` alone
  isn't sufficient readiness signal for a Vite dev server (confirmed by
  running it) — the class-level setup also polls the resolved endpoint
  until it actually answers before Playwright ever navigates to it.
- **`PlaybookRecorder`** (`tests/EventStore.E2ETests/PlaybookRecorder.cs`)
  is the actual screenshot-to-markdown mechanism: `RecordStepAsync(page,
  caption)` captures a numbered screenshot per step; `WriteMarkdownAsync`
  assembles every captured step, in order, into one file. **Naming
  convention, confirmed with the user rather than assumed**: `{Workflow}-
  {feature doc name}.md` under `docs/playbooks/{domain}/` — reuses each
  domain README's own existing `Workflow` lettering (Vitals A–D, Meridian
  A–C) as this project's closest existing "epic" concept, rather than
  inventing a new one. Screenshots live in a sibling folder matching the
  markdown file's own basename, so the prose and its images move/delete
  together as one unit. See `docs/playbooks/README.md` for the catalog.
- Verified for real, not merely built: `VitalsWorkflowAPlaybookTests.
  RecordPatientEnrollmentAndInformedConsentPlaybook` passes against the
  actual live app and produces `docs/playbooks/vitals/workflow-a-
  patient-enrollment-and-informed-consent.md` plus its three real
  screenshots — one of which incidentally, usefully demonstrates
  `ADR-009`'s masking wrapper live (`legalName`/`dateOfBirth` render
  `"masked": "REDACTED"` for the `follower-client`'s `events:follow`-only
  scope), not a contrived example.
- Only one workflow is recorded so far (Vitals Workflow A) — the
  mechanism is proven, not yet applied to every workflow both proving-
  ground domains have; extending coverage is ordinary follow-on work
  using the same `PlaybookRecorder`, not a new mechanism each time.
