[← ADR index](../07-adrs.md)

# ADR-062: Framework distributed as installable packages (NuGet + npm), not forked/cloned per deployment

Status: Accepted

Context: `docs/10-open-questions.md` asked whether a new domain adopts
this framework by forking/cloning the repository, or by referencing
installable packages. Direction received this session: **package-
distributed** — NuGet for the .NET engine, npm for the web client —
covering both stacks this framework ships (`ADR-001`'s .NET providers,
`ADR-039`'s Vue/TypeScript client).

Decision:
- **Every `06-solution-structure.md` project that isn't provider-specific
  glue or a sample becomes a published NuGet package of the same name** —
  `EventStore.Core`, `EventStore.GraphQL`, `EventStore.Sharding`,
  `EventStore.PeerSync`, `EventStore.Streaming`, `EventStore.Attachments`,
  `EventStore.Host.Sqlite`/`.Postgres`/`.SqlServer`, and so on. A new
  package, **`EventStore.Abstractions`**, carries every interface
  catalogued in `docs/extensibility-points.md` (`IMaskingStrategy`,
  `IUpcastExpressionEvaluator`, `IErasureKeyStore`, ...) with no
  implementation — a downstream project references this package to
  implement a custom extension without pulling in the full engine.
- **A downstream domain's own project is the composition root** — this
  is `ADR-059`'s existing decision, now stated precisely: a new domain
  creates its own executable project, references the `EventStore.*`
  packages it needs (`EventStore.Core` + exactly one of `.Host.Sqlite`/
  `.Postgres`/`.SqlServer`, per `ADR-001`'s still-unchanged "one provider,
  chosen at build time" rule — just now expressed as *which package* a
  downstream project references, not which of three projects *this*
  repository builds), calls the framework's own `IServiceCollection`
  extension methods (`services.AddEventStoreCore(...)`,
  `services.AddSqliteProvider(...)`), and registers its own custom
  extension implementations the identical way `ADR-059` already
  describes. `ADR-059` is unaffected by this ADR — the registration
  discipline is the same regardless of whether the referenced code came
  from a forked copy or an installed package; this ADR only settles
  *which* of those two it is.
- **The Vue/Pinia client (`ADR-039`) ships as one or more npm packages**
  (e.g. `@eventstore/mvvm-client`) — reusable composables, the `Client
  Outbox` primitive, and base components a downstream web app installs
  and builds its own views on top of, rather than forking `EventStore.
  Client.Vue` wholesale. The existing Vue app in this repo becomes the
  reference implementation/example consuming its own published
  packages, not the only copy that will ever exist.
- **SemVer 2.0.0** ([semver.org](https://semver.org/)) governs every
  published package's version number — the standard, not a bespoke
  scheme. A breaking change to any public interface in `EventStore.
  Abstractions` or any other package's public API requires a major
  version bump; this is now a real constraint, not a stylistic
  preference, since external consumers pin versions. `ADR-038`'s
  compatibility/deployment discipline already covers *event/schema*
  evolution (Tolerant Reader, Expand/Contract, N-1/N+1 windows) — this
  ADR extends the same discipline to the engine's own public API
  surface, a distinct but analogous compatibility concern `ADR-038`
  didn't originally need to address.

Consequences:
- Resolves `docs/10-open-questions.md`'s distribution-model row.
- ~~`06-solution-structure.md`'s project list is now also a package list —
  needs a `<PackageId>`/versioning note added per project (flagged as
  remaining propagation work, not done this pass).~~ **Corrected, later
  pass**: the mechanism that actually shipped (`docs/08-build-plan.md`
  item 39, "Release Engineering, Packaging & Supply Chain") is a single
  root [`Directory.Build.props`](../../Directory.Build.props) setting
  `<PackageId>$(MSBuildProjectName)</PackageId>` and one shared
  `<Version>` once for every project in the repo via MSBuild's own
  import convention — not a per-project `<PackageId>`/versioning line to
  keep in sync across ~35 projects. `06-solution-structure.md`'s project
  list still needing entries added to reflect this remains a separate,
  still-real gap — owned by another task, not resolved by this note.
- A new, real obligation this design didn't previously have: **public
  API surface discipline**. Every `public` member of every published
  package is now something an external consumer can depend on;
  `internal`/package-private visibility needs to be used deliberately
  going forward, not left to convention.
- The three `EventStore.Host.<Provider>` projects in this repository
  stop being "the only three deployables" and become reference
  implementations/quickstart templates demonstrating how to compose the
  published packages — a new domain is free to structure its own host
  project differently as long as it references the same packages.

**Implementation note, added 2026-08-12**: the npm half of this
Decision was never built — `client-web/package.json` was still the one,
only app, not split into a library + reference app. Built: `client-web`
is now an npm workspaces root (`packages/mvvm-client`, `packages/
reference-app`), matching the NuGet half's own "single root manifest,
not N per-project copies" shape (`Directory.Build.props`, this ADR's
own earlier correction above). `@eventstore/mvvm-client` carries every
composable, API client, IndexedDB-backed store/outbox, device-input
source, i18n, and playback/bundle-verification module — everything with
no Vue-template/DOM dependency of its own; `packages/reference-app`
keeps the actual `.vue` components, `App.vue`/`main.ts`, the offline-
player build target, the native-bridge reference server, and every
build script, consuming the library via a real npm workspace symlink
(`"@eventstore/mvvm-client": "*"` — npm's own local-workspace resolution,
no publish to a real registry attempted this pass). `main`/`types` point
directly at the library's own TypeScript source (`./src/index.ts`), not
a compiled `dist/` — correct for in-monorepo consumption via Vite's own
esbuild transform, but a real future publish to an external registry
would need an actual build step first (the library's own `build` script,
`vue-tsc --emitDeclarationOnly`, already exists and was verified to emit
real `.d.ts` files, but nothing consumes that `dist/` output yet).

**A real bug found while restructuring, not assumed from the original
single-app layout**: `outbox/bundle.ts` and `playback/bundle.ts` both
declare a `parseNdjson` function with different signatures — under the
original single-app layout every consumer imported directly from
whichever file it needed, so this collision was invisible. The new
library's own barrel (`index.ts`) initially used a blanket `export *`
for both, which ES module semantics resolve by silently DROPPING an
ambiguously-named binding from the merged namespace entirely (never an
error, never a merge) — `OfflineBundleViewer.vue`'s own test failed with
"bundle.events is not iterable" because it silently received the wrong
shape. Fixed by aliasing the outbox side's export
(`parseOutboxBundleNdjson`) rather than leaving both under the same bare
name.

Verified: `npm test` (both workspaces) passes 139/139 (29 test files),
an exact match for the pre-restructure baseline; `npm run build`
(`vue-tsc -b && vite build`) and `npm run build:offline-player` both
succeed; `@eventstore/mvvm-client`'s own declaration build emits real
`.d.ts` files. One unrelated fix needed along the way:
`packages/reference-app/tsconfig.json` needed `"node"` added to its
`types` array — `global.fetch` (used by two pre-existing spec files) is
a Node.js ambient global that resolved under the original flat
`node_modules` layout but stopped resolving once `@types/node` moved two
directory levels further from the app's own `tsconfig.json` under the
new workspace layout (confirmed via a side-by-side `git worktree`
comparison against the pre-restructure commit, not assumed).
