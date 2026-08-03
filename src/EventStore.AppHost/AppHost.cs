var builder = DistributedApplication.CreateBuilder(args);

// docs/06-solution-structure.md's own sketch passes the bare server
// resource ("db") straight to WithReference(db) -- that injects only
// server-level connection info (host/port/credentials), no Database=...,
// which wouldn't satisfy EventStore.Host.Postgres's own UseNpgsql call.
// Chaining .AddDatabase("Postgres") gives a database-level resource whose
// injected connection-string key (ConnectionStrings__Postgres) matches
// what Program.cs already reads via GetConnectionString("Postgres") --
// corrected here rather than reproduced as originally sketched.
// AddPostgres's default password is a fresh random value generated on
// EVERY `dotnet run` of this AppHost -- fine alone, but incompatible with
// .WithDataVolume() persisting the actual database files across restarts:
// the second run's newly-generated password no longer matches what's
// baked into the first run's already-initialized data directory, and
// every connection (including Aspire's own readiness check) fails
// "password authentication failed" forever after. Reproduced by actually
// restarting this AppHost twice, not assumed. persist: true stores the
// generated value in this project's user secrets so it stays stable
// across restarts, matching the persisted volume.
var pgPassword = builder.AddParameter("postgres-password", new GenerateParameterDefault { MinLength = 22 }, secret: true, persist: true);
var db = builder.AddPostgres("postgres-server").WithPassword(pgPassword).WithDataVolume().AddDatabase("Postgres");
var devIdp = builder.AddProject<Projects.EventStore_DevIdp>("devidp"); // a project resource, not a container

// Per ADR-001, the AppHost targets exactly one Host.<Provider> project --
// swap which Projects.EventStore_Host_* type is referenced here to run
// locally against a different provider, there is no config value to flip.
builder.AddProject<Projects.EventStore_Host_Postgres>("eventstore")
    .WithReference(db)
    .WaitFor(db) // without this, eventstore can start before Postgres's own
                 // startup finishes and crash on the first migration attempt --
                 // reproduced by actually running `aspire run`, not assumed
    .WithReference(devIdp)
    .WithEnvironment("Authentication__Authority", devIdp.GetEndpoint("http"))
    // devIdp.GetEndpoint("http") is plain HTTP -- appsettings.Development.json's
    // own RequireHttpsMetadata:false override only applies if this project
    // resource's ASPNETCORE_ENVIRONMENT is actually "Development" under
    // Aspire, which isn't guaranteed; setting it explicitly here removes
    // that assumption. Found by actually running `dotnet run` against this
    // AppHost and observing every token rejected -- not assumed correct.
    .WithEnvironment("Authentication__RequireHttpsMetadata", "false");

builder.Build().Run();
