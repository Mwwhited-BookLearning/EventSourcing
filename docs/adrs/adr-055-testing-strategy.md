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
- **Backend unit tests: MSTest + Moq**, per direct preference. MSTest v3
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
- **Frontend unit tests: Vitest + Vue Test Utils.** `Vitest` is
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
- **UI action tests: Playwright**, per direct experience/no strong
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
