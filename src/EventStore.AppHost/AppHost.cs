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
// restarting this AppHost twice, not assumed.
//
// GenerateParameterDefault + persist:true (this line's own prior form)
// was the first attempt at fixing that -- documented as sufficient, but
// found NOT to be, by actually running this AppHost repeatedly while
// building the Vitals/Meridian seed workers: the persisted user-secrets
// value and the value actually baked into a freshly-created container's
// own POSTGRES_PASSWORD env matched each other exactly, yet Postgres
// still rejected that identical password -- confirmed directly via
// `docker exec ... psql`, not assumed. Whatever inconsistency causes
// that (a GenerateParameterDefault re-evaluated at a different point
// than the value written to secrets, in this Aspire version/tooling
// combination) is a local-tooling flakiness this project has no reason
// to chase further -- a fixed literal dev-only password sidesteps the
// whole class of bug by construction: every reference to `pgPassword`
// resolves to the exact same value, every time, with nothing left to
// regenerate or re-resolve inconsistently. Not a secret worth real
// protection (local POC Postgres, never a real deployment target per
// ADR-062), so a literal is fine -- still marked secret:true so the
// dashboard masks it either way.
var pgPassword = builder.AddParameter("postgres-password", "duplex-local-dev-only", secret: true);
var pgServer = builder.AddPostgres("postgres-server").WithPassword(pgPassword).WithDataVolume();
var db = pgServer.AddDatabase("Postgres");
// Fixed, documented dev ports rather than Aspire's own dynamically-
// assigned ones -- the standard convention for this kind of local Aspire
// setup (a developer opening a specific URL by hand shouldn't need to
// scrape the dashboard or scan for it every run), and it's what App.vue's
// own hardcoded standalone-mode fallbacks (VITE_HOST_BASE_URL's default
// "https://localhost:5001", VITE_AUTH_BASE_URL's default "https://
// localhost:5011") already assumed -- those were never actually true
// under this AppHost until now, since nothing here had pinned them to
// match. WithHttpEndpoint/WithHttpsEndpoint override the SAME
// "http"/"https" endpoint launchSettings.json already declares for a
// project resource, they don't add a second one.
//
// devIdp's own OpenIddict issuer ("iss") is computed per-request from
// whatever scheme/host the caller actually hit -- confirmed directly by
// fetching a token from each endpoint and decoding it: via :5011 (https)
// the token's iss is "https://localhost:5011/"; via :5010 (http) it's
// "http://localhost:5010/". eventstore's own Authentication:Authority
// below is pinned to devIdp's HTTP endpoint specifically (that
// WithEnvironment call's own comment explains why -- avoiding an HTTPS
// metadata fetch at server startup). A client fetching its token from
// devIdp's HTTPS endpoint therefore gets one carrying an issuer
// eventstore doesn't trust, and every subsequent GraphQL call fails with
// "Forbidden -- caller's token does not hold the required scope" --
// a real, previously-undiscovered bug (this mismatch existed before this
// session's port-pinning, just never surfaced since nothing had ever
// actually driven a real token through both endpoints and compared).
// VITE_AUTH_BASE_URL below therefore uses devIdp's http endpoint too,
// for every client-web instance -- matching, not just coincidentally
// equal to, eventstore's own trusted issuer.
var devIdp = builder.AddProject<Projects.EventStore_DevIdp>("devidp") // a project resource, not a container
    .WithHttpEndpoint(port: 5010)
    .WithHttpsEndpoint(port: 5011);

// ADR-076 -- "No replica ever calls Database.Migrate() at startup...
// that's the thing that creates the race." EventStore.Migrator is the
// single, one-shot deploy-time apply step that ADR calls for, realized
// for this AppHost's own local dev/POC orchestration: WaitForCompletion
// below guarantees it runs to completion, exactly once, before
// "eventstore" ever starts accepting traffic.
var migrator = builder.AddProject<Projects.EventStore_Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db);

// Vitals/Meridian proving-ground demo data -- same one-shot, direct-DB
// shape as migrator above (ADR-076's posture, applied to seeding instead
// of schema migration), run after it for the same reason (schema must
// already exist). Both gate "eventstore" itself below: FollowSubscriptionTypeModule
// only builds the GraphQL Subscription schema ONCE, at host warmup
// (confirmed, no hot-reload on a newly-registered event type -- see that
// class's own comment) -- so every event type either seed worker
// registers must already exist BEFORE "eventstore" ever starts, or its
// Subscription field would silently never appear for the lifetime of
// that run.
//
// meridianSeed also WaitForCompletion(vitalsSeed) -- found by actually
// running `dotnet run` against this AppHost, not assumed: EventAppender.cs
// deliberately runs every event insert inside a Serializable transaction
// (its own comment: "prevents a phantom read... in the same transaction"),
// and Postgres's own SSI conflict detection operates across the whole
// Events table, not per-AppId -- two publishers writing concurrently, even
// to entirely disjoint AppIds (trial1/kyc), hit real 40001
// serialization_failure errors on every run. Neither seed worker retries
// a transient DB failure (there is nothing here for it to retry against --
// this is one-shot seeding, not a resilient long-running Host endpoint),
// so the fix is ordering, not a retry loop: never let them write at the
// same time.
var vitalsSeed = builder.AddProject<Projects.Samples_Vitals_Seed>("vitals-seed")
    .WithReference(db)
    .WaitFor(db)
    .WaitForCompletion(migrator);

var meridianSeed = builder.AddProject<Projects.Samples_Meridian_Seed>("meridian-seed")
    .WithReference(db)
    .WaitFor(db)
    .WaitForCompletion(migrator)
    .WaitForCompletion(vitalsSeed);

// Per ADR-001, the AppHost targets exactly one Host.<Provider> project --
// swap which Projects.EventStore_Host_* type is referenced here to run
// locally against a different provider, there is no config value to flip.
var eventstore = builder.AddProject<Projects.EventStore_Host_Postgres>("eventstore")
    .WithHttpEndpoint(port: 5000)
    .WithHttpsEndpoint(port: 5001)
    .WithReference(db)
    .WaitFor(db) // without this, eventstore can start before Postgres's own
                 // startup finishes and crash on the first migration attempt --
                 // reproduced by actually running `aspire run`, not assumed
    .WaitForCompletion(migrator) // the schema must already be current before
                                  // this replica starts serving traffic
    .WaitForCompletion(vitalsSeed)
    .WaitForCompletion(meridianSeed)
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
//
// VITE_HOST_BASE_URL pointed at eventstore's "http" endpoint for a while
// this session, as a workaround: with no pinned port, eventstore's own
// https endpoint never actually bound under Aspire orchestration
// specifically (confirmed -- the same project run standalone with an
// explicit ASPNETCORE_URLS bound both http and https immediately). Once
// eventstore's ports were pinned (WithHttpsEndpoint(port: 5001) above),
// https started binding reliably every run -- whatever was wrong was
// specific to Aspire's own DYNAMIC port allocation for this resource,
// not eventstore itself. Reverted back to https accordingly.
var clientWeb = builder.AddViteApp("client-web", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http")) // https mismatched eventstore's trusted issuer -- see the comment at this file's top
    .WithHttpEndpoint(port: 5173) // Vite's own conventional default dev port
    .WithExternalHttpEndpoints();

// One client-web instance per proving-ground domain, pre-configured via
// the VITE_APP_ID/VITE_ENTITY_TYPE/VITE_EVENT_TYPE/VITE_ENTITY_ID_FIELD
// build-time env vars App.vue's own config resolution now falls back to
// -- each watches the single event type that best showcases that domain's
// masking story (Vitals' PatientScreened carries the PHI LegalName/
// DateOfBirth fields; Meridian's IdentityClaimSubmitted carries the PII
// ClaimedLegalName/DateOfBirth fields), for the continuity subject/
// applicant vitals-seed/meridian-seed just published. Same source
// directory as the generic "client-web" instance above -- three
// independent Vite dev server processes over one unchanged codebase, not
// three copies of it.
var clientWebVitals = builder.AddViteApp("client-web-vitals", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https")) // reverted to https once pinned ports fixed the binding issue -- see clientWeb's own comment above
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http")) // https mismatched eventstore's trusted issuer -- see the comment at this file's top
    .WithEnvironment("VITE_APP_ID", "trial1")
    .WithEnvironment("VITE_ENTITY_TYPE", "patient")
    .WithEnvironment("VITE_EVENT_TYPE", "PatientScreened")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "subjectId")
    .WithHttpEndpoint(port: 5174)
    .WithExternalHttpEndpoints();

var clientWebMeridian = builder.AddViteApp("client-web-meridian", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https")) // reverted to https once pinned ports fixed the binding issue -- see clientWeb's own comment above
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http")) // https mismatched eventstore's trusted issuer -- see the comment at this file's top
    .WithEnvironment("VITE_APP_ID", "kyc")
    .WithEnvironment("VITE_ENTITY_TYPE", "applicantidentity")
    .WithEnvironment("VITE_EVENT_TYPE", "IdentityClaimSubmitted")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "applicantId")
    .WithHttpEndpoint(port: 5175)
    .WithExternalHttpEndpoints();

// Every client-web instance's browser-side JS calls devIdp's /connect/token
// and eventstore's GraphQL/registry endpoints directly, cross-origin (each
// Vite dev server has its own dynamically-assigned port) -- ADR-014's own
// deny-by-default CORS posture (both EventStore.Host.Core's existing
// policy and DevIdp's own copy of it, added this session once this gap
// was found) means neither endpoint accepts ANY cross-origin browser call
// until its Cors:AllowedOrigins config actually names the caller's origin.
// A real, found-by-actually-opening-this-in-a-browser gap, not assumed:
// curl never sends an Origin header, so every earlier curl-based check
// this session missed it completely -- three GetEndpoint("http") values
// referencing resources declared below devIdp/eventstore in this file,
// which is why this couldn't be inlined into their own definitions above.
devIdp.WithEnvironment("Cors__AllowedOrigins__0", clientWeb.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__1", clientWebVitals.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__2", clientWebMeridian.GetEndpoint("http"));
eventstore.WithEnvironment("Cors__AllowedOrigins__0", clientWeb.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__1", clientWebVitals.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__2", clientWebMeridian.GetEndpoint("http"));

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
// Each proving-ground domain (Vitals, Meridian) now gets its OWN parent
// resource the identical way -- its own seed worker, the one genuinely
// representative resource for that domain's pool -- rather than nesting
// under "eventstore" alongside the shared Core Platform pieces above.
pgServer.WithParentRelationship(eventstore);
migrator.WithParentRelationship(eventstore);
devIdp.WithParentRelationship(eventstore);
clientWeb.WithParentRelationship(eventstore);
clientWebVitals.WithParentRelationship(vitalsSeed);
clientWebMeridian.WithParentRelationship(meridianSeed);

builder.Build().Run();
