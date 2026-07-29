[← Libraries index](../README.md)

# Testcontainers (dotnet)

**What it's for:** spins up real, disposable Docker containers (a real
PostgreSQL, a real SQL Server) from test code, so integration tests run
against the actual database engine instead of an in-memory fake or a
hand-maintained shared test database.

**Why bought, not built:** container lifecycle management (pull, start,
wait-until-ready, tear down, port mapping) is generic infrastructure
that has nothing to do with this project's own tests — reimplementing it
per test suite would be pure overhead with real footgun potential
(leaked containers, flaky readiness checks).

## General usage

```csharp
var postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
await postgres.StartAsync();

var options = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(postgres.GetConnectionString())
    .Options;
// run the real EF Core provider against a real, disposable Postgres
```

## Where this project uses it

`06-solution-structure.md`'s integration test strategy — per-provider
test suites (SQLite/PostgreSQL/SQL Server, `ADR-001`) run against the
real engine, not a fake, so provider-specific behavior (JSON pushdown,
`04-odata-filter-pushdown.md`) is actually exercised.

## Links

- [testcontainers.com](https://testcontainers.com/)
