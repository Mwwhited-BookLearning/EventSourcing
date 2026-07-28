# Solution Structure

## Project layout

```
EventStore.sln
  src/
    EventStore.Domain/              -- entities, no EF dependency
    EventStore.Persistence/         -- DbContext, repositories, IJsonPathTranslator + impls
    EventStore.Persistence.Migrations.Sqlite/
    EventStore.Persistence.Migrations.Postgres/
    EventStore.Persistence.Migrations.SqlServer/
    EventStore.SchemaRegistry/      -- registration service, validation, ParentLinkService
    EventStore.Publish.Api/         -- POST /publish/{event-type}
    EventStore.Follow.Api/          -- GET /follow/{event-type} (SSE), OData parsing
    EventStore.Lineage.Api/         -- GET /events/{id}/parents|children|ancestors|descendants
    EventStore.SpecGeneration/      -- OpenAPI + AsyncAPI builders
    EventStore.Host/                -- composition root: DI wiring, appsettings, Program.cs
    EventStore.ServiceDefaults/     -- Aspire scaffolding: OpenTelemetry, health checks, service discovery defaults
    EventStore.AppHost/             -- Aspire orchestration for local dev/POC (see below)
  tests/
    EventStore.UnitTests/
    EventStore.IntegrationTests/    -- runs against all three providers (see below)
    EventStore.Bdd/                 -- Reqnroll/SpecFlow-style step definitions for features/*.feature
```

## Why separate migrations projects

EF Core migrations embed provider-specific SQL in their generated `Up()`/
`Down()` methods — they are not portable even when the model is identical.
Each `*.Migrations.<Provider>` project references
`EventStore.Persistence` and holds only that provider's migration history.
`dotnet ef migrations add <Name> --project src/EventStore.Persistence.Migrations.Sqlite --startup-project src/EventStore.Host` and equivalent per provider.

## DI wiring (composition root)

```csharp
var provider = builder.Configuration["Database:Provider"]!;

builder.Services.AddDbContext<EventStoreContext>(options => provider switch
{
    "Sqlite"    => options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")),
    "Postgres"  => options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")),
    "SqlServer" => options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});

builder.Services.AddScoped<IJsonPathTranslator>(sp => provider switch
{
    "Sqlite"    => new SqliteJsonPathTranslator(),
    "Postgres"  => new PostgresJsonPathTranslator(),
    "SqlServer" => new SqlServerJsonPathTranslator(),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});

builder.Services.AddScoped<ISchemaRegistryReader, SchemaRegistryReader>();
builder.Services.AddScoped<SchemaValidationService>();
builder.Services.AddScoped<ParentLinkService>();
builder.Services.AddScoped<ODataFilterParser>();
builder.Services.AddScoped<IEventLineageQueryProvider>(sp => provider switch
{
    "Sqlite"    => new SqliteEventLineageQueryProvider(),
    "Postgres"  => new PostgresEventLineageQueryProvider(),
    "SqlServer" => new SqlServerEventLineageQueryProvider(),
    _ => throw new InvalidOperationException($"Unknown provider '{provider}'")
});
builder.Services.AddSingleton<OpenApiDocumentBuilder>();
builder.Services.AddSingleton<AsyncApiDocumentBuilder>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"]; // dev: Keycloak realm issuer URL
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment(); // dev Keycloak container runs over plain HTTP
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("events:publish", p => p.Requirements.Add(new ScopeRequirement("events:publish")))
    .AddPolicy("events:follow", p => p.Requirements.Add(new ScopeRequirement("events:follow")))
    .AddPolicy("events:lineage:read", p => p.Requirements.Add(new ScopeRequirement("events:lineage:read")))
    .AddPolicy("registry:admin", p => p.Requirements.Add(new ScopeRequirement("registry:admin")));
```

`ScopeRequirement`/its handler is a small custom `IAuthorizationHandler` — not
the built-in `RequireClaim` — because OAuth2 delivers `scope` as one
space-delimited string claim (`"events:publish events:follow"`), and
`RequireClaim` does an exact-value match, not a "one of the space-separated
tokens" match. See `ADR-006`.

Runtime provider switch is the recommended v1 approach (single deployable
artifact; `Database:Provider` in `appsettings.json` or environment
variable) — see `ADR-001`. Startup must apply pending migrations for the
active provider's migration assembly only.

```csharp
var migrationsAssembly = provider switch
{
    "Sqlite"    => "EventStore.Persistence.Migrations.Sqlite",
    "Postgres"  => "EventStore.Persistence.Migrations.Postgres",
    "SqlServer" => "EventStore.Persistence.Migrations.SqlServer",
    _ => throw new InvalidOperationException()
};
// pass migrationsAssembly into the relevant options.UseSqlite/UseNpgsql/UseSqlServer(..., x => x.MigrationsAssembly(migrationsAssembly))
```

## Event lineage (parent/child DAG) queries

`parents`/`children` are plain LINQ joins against `EventParents` — fully
portable, no raw SQL needed. `ancestors`/`descendants` need a recursive
query; EF Core's LINQ provider has no translation for recursive CTEs, so
these are the one query path in the store that isn't a pure `IQueryable`:

```csharp
public interface IEventLineageQueryProvider
{
    Task<IReadOnlyList<LineageNode>> GetAncestorsAsync(Guid eventId);
    Task<IReadOnlyList<LineageNode>> GetDescendantsAsync(Guid eventId);
}
```

Each provider implementation issues a `WITH RECURSIVE` CTE (SQLite,
PostgreSQL) or `WITH` CTE (SQL Server — no `RECURSIVE` keyword required)
via `FromSqlInterpolated`/raw SQL, resolved via DI exactly like
`IJsonPathTranslator`.

**Cycle safety is mandatory, not conditional on `ParentValidationMode`** —
see `ADR-005` for why a cycle can exist even starting from a `Strict` event.
PostgreSQL and SQL Server recursive CTEs support a native `CYCLE` clause;
SQLite has none, so track a visited-path column/array in the CTE and stop
recursing when a node reappears. Cap traversal depth in all three as a
belt-and-suspenders limit regardless of provider.

## Auth: dev identity provider (Keycloak) and local orchestration

For this POC, `EventStore.Host` validates Bearer JWTs against a dev-mode
Keycloak realm rather than a production IdP (see `ADR-006`). Two equivalent
ways to stand the whole thing up locally:

**`EventStore.AppHost` (.NET Aspire, preferred for local `dotnet run`/`aspire run`):**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("db").WithDataVolume(); // swap for AddSqlServer(...) per Database:Provider
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithRealmImport("./keycloak-realm-event-store.json"); // pre-seeded realm + 3 clients + scopes, committed to the repo

builder.AddProject<Projects.EventStore_Host>("eventstore")
    .WithReference(db)
    .WithReference(keycloak)
    .WithEnvironment("Authentication__Authority", $"{keycloak.GetEndpoint("http")}/realms/event-store");

builder.Build().Run();
```

`EventStore.ServiceDefaults` wires the standard Aspire cross-cutting
concerns (OpenTelemetry, health checks, service discovery) into
`EventStore.Host` via `builder.AddServiceDefaults()` — no lineage/auth logic
lives there.

**`docker-compose.yml` (repo root, non-Aspire-tooling fallback):** the same
three services — `eventstore`, the chosen database, and `keycloak` in
`start-dev` mode importing the same committed realm export — so CI or
anyone without the Aspire CLI gets an identical dev environment. Aspire is
preferred day-to-day because it also wires telemetry/health checks
automatically for a .NET solution; compose stays as the lowest-common-
denominator path.

The Keycloak realm export must be committed (`keycloak-realm-event-store.json`)
so either path produces a working IdP — three clients
(`publisher-client`/`follower-client`/`operator-client`, `client_credentials`
grant) with `events:publish`/`events:follow`+`events:lineage:read`/
`registry:admin` scopes respectively — with no manual admin-console setup.

## Integration test strategy

`EventStore.IntegrationTests` parameterizes the same test suite across all
three providers using Testcontainers (Postgres, SQL Server) and an
in-memory/temp-file SQLite database, so pushdown-filter behavior is proven
identical on all three, not just unit-tested against mocks.
