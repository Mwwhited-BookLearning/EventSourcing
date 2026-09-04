[← ADR index](../07-adrs.md)

# ADR-108: Centralized NuGet package versioning via `Directory.Packages.props`

Status: Accepted

Context: Direct request: "I would like a branch to centerlize verssion
numbers for packages then update everything to latest where possible."
Before this ADR, every `.csproj`'s own `<PackageReference Include="X"
Version="Y" />` carried its own version string — 37 project files, ~60
distinct packages. Checked first, not assumed: every package that
appeared in more than one project already used the *identical* version
everywhere (confirmed by diffing every `Include`/`Version` pair across
the whole solution) — a real, load-bearing precondition for this ADR,
since NuGet Central Package Management (CPM) requires exactly one
version per package solution-wide; a real conflict would have needed
reconciling by hand first.

Decision:
- **Adopt NuGet Central Package Management** — a real, first-party
  MSBuild/NuGet feature (stable since NuGet 6.2, no third-party tooling),
  not a bespoke mechanism: a root `Directory.Packages.props` with
  `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
  and one `<PackageVersion Include="X" Version="Y" />` per distinct
  package; every `.csproj`'s own `<PackageReference Include="X" />`
  drops its `Version` attribute (other attributes — `PrivateAssets`,
  `IncludeAssets`, child elements — are untouched). `Directory.Build.
  props`'s own two solution-wide analyzer `PackageReference`s
  (`StyleCop.Analyzers`, `Microsoft.CodeAnalysis.BannedApiAnalyzers`)
  are included the same way, not left as a special case.
- **`spikes/` stays out of this entirely, on purpose** — every spike
  project keeps its own independent, self-pinned versions in its own
  `.csproj`, matching this folder's own established convention (real,
  independently-buildable code, never wired into `EventStore.slnx`).
  Folding spikes into central management would tie their version choices
  to the main solution's own upgrade cadence for no benefit, since
  they're never built together.
- **Updated every centrally-managed package to its latest stable
  version, verified against live NuGet data (`dotnet list package
  --outdated`), not asserted from memory** — this project's own
  "verify before citing" standing instruction applies exactly as much to
  a version number as to a citation. Two genuine major-version jumps
  included, per direct request to update "to latest where possible"
  across major versions, not just within the current one:
  `AWSSDK.KeyManagementService` 3.7.400 → 4.0.100.12, `Cel.NET` 1.1.0 →
  2.2.1. Both compiled clean and (`Cel.NET` specifically) remain
  covered by real, passing tests (`CelUpcastExpressionEvaluator`'s own
  suite, part of `AllUpcastMaterializationScenarios`). `AWSSDK.
  KeyManagementService`'s own real behavior against a live AWS account
  remains unverified in this environment — the same, already-honestly-
  scoped limitation `ADR-057`'s own implementation note already states
  for every cloud `IErasureKeyStore` backend (no live cloud credentials
  available here); this update does not change that scope, only the
  version under it.
- **One package left deliberately behind, not silently skipped**:
  `OpenTelemetry.Instrumentation.Process` has never had a stable
  release — bumped in lockstep with its OTel siblings
  (`1.17.0-rc.1` → `1.18.0-rc.1`), staying on the same prerelease
  channel this project already accepted when it first adopted the
  package (`ADR-088`).

Consequences:
- `docs/06-solution-structure.md` gains a short pointer to this ADR from
  wherever it already describes `Directory.Build.props`'s own role.
- Adding a new package now means one line in `Directory.Packages.props`
  plus a bare `<PackageReference Include="X" />` in the consuming
  project — never a version string in the project file itself. A
  reviewer diffing a `.csproj` for a dependency bump will no longer find
  one there; `Directory.Packages.props` is now the single, authoritative
  place every package version is declared.
- **`client-web`'s npm side updated the same pass, same "latest where
  possible" standard, verified via `npm outdated` against the live
  registry** — not folded into this ADR's own NuGet-specific mechanism
  (npm has no direct equivalent to CPM; this workspace's existing
  root-`package.json`-holds-shared-`devDependencies` shape already
  centralizes what npm workspaces can). All in-range updates applied via
  `npm update`; three packages needed an explicit major-version bump in
  `client-web/package.json`: `jsdom` 25.0.1 → 30.0.1, `vitest` 4.1.10 →
  5.0.0. A real, previously-latent test-environment bug surfaced by the
  `jsdom` bump specifically — `NativeBridgeInputSource.spec.ts` (a real
  Node `WebSocket` client, ADR-070) started throwing `TypeError: The
  "event" argument must be an instance of Event. Received an instance of
  Event` under jsdom's environment: jsdom 30 patches
  `globalThis.Event`/`EventTarget` more aggressively than jsdom 25 did,
  and Node's own native `WebSocket` (built on `undici`) dispatches
  events using its own internal `Event` class checked against whatever
  `Event` jsdom left on `globalThis` — a real cross-realm class
  mismatch, not a bug in this project's own code. Fixed with a real,
  minimal, targeted fix: a per-file `// @vitest-environment node`
  pragma on that one spec file (it touches no DOM at all — confirmed by
  grepping `NativeBridgeInputSource.ts` for `document`/`window`, zero
  matches), sidestepping the realm collision entirely rather than
  pinning `jsdom` back or patching around it globally.
- **One update explicitly not made, and why**: `typescript` stays at
  `^6.0.3` (already its own latest within what the toolchain supports),
  not bumped to the newer `7.0.2` — checked live
  (`npm view typescript-eslint@latest peerDependencies`) before
  attempting it: `typescript-eslint`'s own latest stable (`8.69.0`)
  declares `typescript: '>=4.8.4 <6.1.0'`, so it does not support
  TypeScript 7 yet at all. Bumping would have broken this project's own
  linting toolchain for a version this repo's own dependency doesn't yet
  support — a real ecosystem limit, not a judgment call to skip
  something merely inconvenient. Revisit once `typescript-eslint`
  itself supports TypeScript 7.
- Verified for real, not assumed from a clean build alone: full solution
  build (0 errors) both immediately after the CPM migration (before any
  version changed) and again after every version bump; full `.NET` unit
  suite (82/82) and integration suite (252/252, Testcontainers-backed,
  all three providers exercised) both green; `client-web`'s full
  workspace test suite (mvvm-client 145/145, reference-app 47/47) and
  `npm run build`/`npm run lint` (0 lint errors) all green after every
  update, npm side included.
