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

var options = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
    .Options;

await using var db = new EventStoreContext(options, new PostgresJsonPathTranslator());
await db.Database.MigrateAsync();
