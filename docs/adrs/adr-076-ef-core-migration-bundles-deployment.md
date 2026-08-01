[← ADR index](../07-adrs.md)

# ADR-076: Database schema deployment via EF Core migration bundles, applied as a single deploy-time step

Status: Accepted

Context: `docs/10-open-questions.md` asked how a package consumer
actually applies this framework's EF Core migrations to a database they
own, given `ADR-062` ships migrations inside a distributed package and
`ADR-075`'s silo model means many independently-deployed sites each with
their own database. The specific, concrete risk named: calling
`context.Database.Migrate()` at application startup, with more than one
replica of a `EventStore.Host.<Provider>` starting concurrently, is a
known race — two replicas can both see pending migrations and both
attempt to apply them. `ADR-038` already decides migration *discipline*
(Expand/Contract, N-1/N+1 compatibility); this ADR decides *application*
— the step that was still missing.

Decision:
- **No replica ever calls `Database.Migrate()` at startup.** This
  pattern is removed from every `EventStore.Host.<Provider>` composition
  root outright — it's the thing that creates the race, not something to
  coordinate around.
- **EF Core Migration Bundles** (`dotnet ef migrations bundle`, shipped
  since EF Core 6, [Microsoft's own documented production
  path](https://devblogs.microsoft.com/dotnet/introducing-devops-friendly-ef-core-migration-bundles/))
  run as a single, one-shot deploy-time step, before any replica starts
  serving traffic — a self-contained executable needing no .NET SDK, no
  source code, and no schema-altering permissions on the application's
  own runtime identity. This solves the multi-replica race by
  construction (exactly one execution, ever, per deployment), not by
  adding coordination logic to the application.
- **For deployments that prefer a provider-native, declarative "final-
  state" apply tool instead of running the bundle directly**: EF Core
  remains the C# authoring source (migrations, respecting `ADR-038`'s
  Expand/Contract discipline) and generates portable SQL via `dotnet ef
  migrations script --idempotent`; a provider-native tool then owns
  *applying* that SQL (or an equivalent live-database diff) as the same
  single deploy-time step:
  - **SQL Server**: DACPAC + `SqlPackage` (Microsoft's own SSDT-based
    declarative schema-deployment tooling).
  - **PostgreSQL**: [pgschema](https://www.pgschema.com/) (Terraform-
    style declarative migration — dump current schema, plan the diff,
    apply). Verified before adopting: **not** `pgpkg`, a similarly-named
    but unrelated pl/pgSQL *function*-management tool with no schema-
    migration capability at all — caught before being cited as the wrong
    tool.
  Either path is deployment-choice tooling layered on top of the same
  EF-generated SQL, not a second, independent source of schema truth —
  EF Core's own migration history stays authoritative for what "current
  schema" means, regardless of which tool actually executes the DDL.
- **This is a deployment-pipeline detail, not a per-provider runtime code
  fork** — `EventStore.Host.<Provider>`'s own application code never
  needs to know which of these paths applied its schema; it only needs
  the schema to already be current by the time it starts accepting
  traffic.

Consequences:
- The deployment pipeline (whatever CI/CD mechanism `docs/10-open-
  questions.md` row 5 eventually settles) needs an explicit migration-
  bundle-generation-and-apply step sequenced before any `Host` container
  starts — not yet built.
- `docker-compose.yml` (`ADR-026`'s production path) needs a migration/
  bundle-apply step (or an init container running one) sequenced ahead
  of the `Host` services — flagged as propagation work, not yet done.
- Resolves `docs/10-open-questions.md` row 12.

**Compliance note**: this ADR is the concrete mechanism satisfying
`ADR-038`'s N-1/N+1 rollback-safety promise in practice — a migration
bundle applying only expand-style changes means a rolled-back binary
still finds a database shape it fully understands, exactly the rollback
drill `08-build-plan.md`'s Phase 19 exit criterion already names.
