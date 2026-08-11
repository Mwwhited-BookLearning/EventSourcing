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
