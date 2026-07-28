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
    EventStore.SchemaRegistry/      -- registration service, validation
    EventStore.Publish.Api/         -- POST /publish/{event-type}
    EventStore.Follow.Api/          -- GET /follow/{event-type} (SSE), OData parsing
    EventStore.SpecGeneration/      -- OpenAPI + AsyncAPI builders
    EventStore.Host/                -- composition root: DI wiring, appsettings, Program.cs
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
builder.Services.AddScoped<ODataFilterParser>();
builder.Services.AddSingleton<OpenApiDocumentBuilder>();
builder.Services.AddSingleton<AsyncApiDocumentBuilder>();
```

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

## Integration test strategy

`EventStore.IntegrationTests` parameterizes the same test suite across all
three providers using Testcontainers (Postgres, SQL Server) and an
in-memory/temp-file SQLite database, so pushdown-filter behavior is proven
identical on all three, not just unit-tested against mocks.
