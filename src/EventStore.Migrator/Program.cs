using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

// ADR-076 -- "No replica ever calls Database.Migrate() at startup...
// that's the thing that creates the race." This project is the single,
// one-shot deploy-time step that ADR calls for, realized for
// EventStore.AppHost's own local dev/POC orchestration specifically:
// Aspire's own WaitForCompletion guarantees this runs to completion,
// exactly once, before the real Host resource ever starts accepting
// traffic -- the identical single-execution guarantee ADR-076's
// migration-bundle/DACPAC+SqlPackage/pgschema paths provide for a real
// deployment pipeline, just realized as an Aspire resource instead of a
// separate CI/CD step, since AppHost has no such pipeline of its own to
// hook into. Postgres-only, matching AppHost.cs's own "targets exactly
// one Host.<Provider> project" convention (ADR-001) -- swap the
// UseNpgsql/MigrationsAssembly call below if that ever changes.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required -- expected to be injected by Aspire's WithReference(db).");

// TODO.md's own Postgres-database-creation-race finding: Aspire's
// AddDatabase("Postgres") creates the named database asynchronously, off
// the Postgres server resource's own ResourceReadyEvent (confirmed
// against Aspire's own hosting docs) -- WaitFor(db) waits for the
// DATABASE RESOURCE to be registered, not for that CREATE DATABASE
// statement to have actually committed, so this migrator (the first
// real connector in the dependency chain, per AppHost.cs) can race it
// and hit "3D000: database does not exist" on its very first, otherwise-
// unretried attempt. EnableRetryOnFailure's own errorCodesToAdd
// parameter (real Npgsql.EntityFrameworkCore.PostgreSQL API, verified
// against its own docs before writing this) exists for exactly this
// class of "a known error code is actually transient in my specific
// startup scenario" case -- 3D000 (invalid_catalog_name) added
// explicitly, since it's not one of Npgsql's own built-in transient
// codes (a real "database does not exist" is normally a permanent,
// non-retryable error; it's only transient here because of Aspire's own
// async creation timing). Verified against a real Postgres container,
// not assumed from the API docs alone: an ordinary DbSet query (the
// exact call shape EF wraps in its own retry execution strategy --
// Database.ExecuteSqlRawAsync/CanConnectAsync, tried first, do NOT go
// through that strategy at all, a real EF Core nuance found only by
// testing both directly) against a not-yet-created database failed
// instantly with this exact error without this configuration, and
// succeeded automatically once a concurrently-delayed CREATE DATABASE
// landed mid-retry, with it.
var options = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(connectionString, x => x
        .MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")
        .EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: ["3D000"]))
    .Options;

await using var db = new EventStoreContext(options, new PostgresJsonPathTranslator());
await db.Database.MigrateAsync();
