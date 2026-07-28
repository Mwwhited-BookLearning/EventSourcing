[← ADR index](../07-adrs.md)

# ADR-001: Per-deployment database provider build (vs. runtime switch)

Status: Accepted

Context: The store must run on SQLite, PostgreSQL, or SQL Server. Provider
selection could be a compile-time/per-deployment choice or a runtime
config switch.

Decision: **Per-deployment build.** Exactly one provider is chosen at
build/publish time, not read from configuration at startup. Three thin
composition-root projects — `EventStore.Host.Sqlite`,
`EventStore.Host.Postgres`, `EventStore.Host.SqlServer` — each hardcode
their one provider's `UseSqlite`/`UseNpgsql`/`UseSqlServer` call, register
that provider's `IJsonPathTranslator`/`IEventLineageQueryProvider`
implementations unconditionally (no `switch`), and reference exactly that
provider's migrations assembly directly. All three share the same
provider-agnostic setup (DI for everything else, endpoint mapping) via
`EventStore.Host.Core` — see `06-solution-structure.md`. There is no
`Database:Provider` configuration value anywhere in this design; it's
superseded by "which of the three projects did you build."

Consequences: CI/CD must build and publish **three** artifacts instead of
one — more pipeline complexity than the runtime-switch alternative. In
exchange, startup has zero provider-branching logic and zero risk of a
misconfigured `Database:Provider` value routing to the wrong migrations
assembly at runtime — the three `switch` statements the runtime-switch
design needed (DbContext options, `IJsonPathTranslator`,
`IEventLineageQueryProvider`, migrations assembly) all collapse to a
single unconditional registration per project, because each project only
ever runs against one provider. Moving a running deployment to a different
provider means redeploying a different artifact, not flipping a config
value — an explicit, accepted trade against the runtime-switch design's
main convenience. Still requires all three migration histories to be kept
in sync manually when the model changes (unchanged from the runtime-switch
alternative — this risk is about EF Core migrations not being portable
across providers, not about how the provider is selected).
