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
var pgServer = builder.AddPostgres("postgres-server").WithPassword(pgPassword).WithDataVolume();
var db = pgServer.AddDatabase("Postgres");
var devIdp = builder.AddProject<Projects.EventStore_DevIdp>("devidp"); // a project resource, not a container

// ADR-076 -- "No replica ever calls Database.Migrate() at startup...
// that's the thing that creates the race." EventStore.Migrator is the
// single, one-shot deploy-time apply step that ADR calls for, realized
// for this AppHost's own local dev/POC orchestration: WaitForCompletion
// below guarantees it runs to completion, exactly once, before
// "eventstore" ever starts accepting traffic.
var migrator = builder.AddProject<Projects.EventStore_Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db);

// Per ADR-001, the AppHost targets exactly one Host.<Provider> project --
// swap which Projects.EventStore_Host_* type is referenced here to run
// locally against a different provider, there is no config value to flip.
var eventstore = builder.AddProject<Projects.EventStore_Host_Postgres>("eventstore")
    .WithReference(db)
    .WaitFor(db) // without this, eventstore can start before Postgres's own
                 // startup finishes and crash on the first migration attempt --
                 // reproduced by actually running `aspire run`, not assumed
    .WaitForCompletion(migrator) // the schema must already be current before
                                  // this replica starts serving traffic
    .WithReference(devIdp)
    .WithEnvironment("Authentication__Authority", devIdp.GetEndpoint("http"))
    // devIdp.GetEndpoint("http") is plain HTTP -- appsettings.Development.json's
    // own RequireHttpsMetadata:false override only applies if this project
    // resource's ASPNETCORE_ENVIRONMENT is actually "Development" under
    // Aspire, which isn't guaranteed; setting it explicitly here removes
    // that assumption. Found by actually running `dotnet run` against this
    // AppHost and observing every token rejected -- not assumed correct.
    .WithEnvironment("Authentication__RequireHttpsMetadata", "false");

// client-web's own MVVM client (ADR-039), run as a real Vite dev server
// under Aspire rather than started by hand. App.vue reads hostBaseUrl/
// authBaseUrl from its own URL query string first (ADR-039's own "per-
// instance launch configuration," so two windows can watch different
// things) -- these two env vars are only the FALLBACK for that, letting
// the Aspire-orchestrated run resolve the actual dynamically-assigned
// endpoints with no manual query-string editing.
var clientWeb = builder.AddViteApp("client-web", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("https"))
    .WithExternalHttpEndpoints();

// Dashboard-only grouping (WithParentRelationship carries no lifecycle/
// dependency meaning of its own -- that's WithReference/WaitFor's job
// above, unaffected by this). This Aspire version (13.4.6) has no
// dedicated "resource group" primitive to reflect: real dependency
// direction points the OTHER way (eventstore depends on postgres-server/
// migrator/devidp, not the reverse), but "eventstore" is still the one
// genuinely representative resource for this whole platform's "Core
// Platform" pool, so it's used as the visual parent purely so the
// dashboard nests everything else underneath it instead of listing
// unrelated-looking top-level rows. pgServer (not db -- db already nests
// under pgServer by Aspire's own default database/server relationship,
// which this must not override) is what actually gets reparented here.
// Each future proving-ground domain (Vitals, Meridian) gets its OWN
// parent resource the identical way, once one exists to point at.
pgServer.WithParentRelationship(eventstore);
migrator.WithParentRelationship(eventstore);
devIdp.WithParentRelationship(eventstore);
clientWeb.WithParentRelationship(eventstore);

builder.Build().Run();
