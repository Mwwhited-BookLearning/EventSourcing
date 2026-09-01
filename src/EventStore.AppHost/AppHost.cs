using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Every static value below (port, dev password, simulator cadence) is read
// from appsettings.json's own "Ports"/"Postgres"/"Simulator" sections
// (falling back to the literal that was previously hardcoded here) rather
// than baked in -- overridable per-environment the same way client-web's
// own VITE_* env vars already are, without editing this file. A real
// deployment target never uses this AppHost at all (ADR-026: dev/POC
// orchestration only), so this is about local-dev flexibility, not
// production config management.
int Port(string key, int fallback) => builder.Configuration.GetValue($"Ports:{key}", fallback);

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
var pgPassword = builder.AddParameter("postgres-password", builder.Configuration["Postgres:DevPassword"] ?? "duplex-local-dev-only", secret: true);
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
    .WithHttpEndpoint(port: Port("DevIdpHttp", 5010))
    .WithHttpsEndpoint(port: Port("DevIdpHttps", 5011));

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
// already exist).
//
// Stale note corrected here, not left wrong: an earlier pass of this
// comment claimed "eventstore" had to wait for both seed workers because
// FollowSubscriptionTypeModule only builds its GraphQL Subscription
// schema ONCE, at host warmup, with no hot-reload for a later-registered
// event type. That claim is no longer true -- FollowSubscriptionTypeModule's
// own header comment documents a genuinely fixed and verified hot-reload
// path (docs/changes/2026-08-13.md): a type registered after Host warmup
// becomes queryable, and a real Subscription against it delivers a real
// published event, within about 150ms, with no restart. `eventstore` still
// waits for both seed workers below (WaitForCompletion), but only for the
// ordinary reason migrator itself is waited on: the schema/data these
// seeders write must exist before anything queries for it, not because the
// GraphQL schema itself is frozen at boot.
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

// "Proving-Ground Application UX" -- the long-running counterparts to the
// one-shot Seed workers above: periodically publish a brand-new event for
// a never-before-used subject/applicant, so a running instance shows
// continuing activity instead of the static seed snapshot alone. Started
// after both Seed workers (same schema-must-already-exist reasoning) but,
// unlike them, NEVER exit -- WaitForCompletion can't order two infinite
// loops against each other the way it ordered the two one-shot Seed
// workers, so each Simulator's own Program.cs retries on the same
// Postgres Serializable-transaction conflict instead (see either
// Program.cs's own comment for why running both concurrently, forever,
// genuinely hits it). Interval is config-driven ("Simulator:
// {Vitals,Meridian}IntervalSeconds" in appsettings.json), not hardcoded.
var vitalsSimulator = builder.AddProject<Projects.Samples_Vitals_Simulator>("vitals-simulator")
    .WithReference(db)
    .WaitFor(db)
    .WaitForCompletion(vitalsSeed)
    .WaitForCompletion(meridianSeed)
    .WithEnvironment("SimulatorIntervalSeconds", builder.Configuration.GetValue("Simulator:VitalsIntervalSeconds", 20).ToString());

var meridianSimulator = builder.AddProject<Projects.Samples_Meridian_Simulator>("meridian-simulator")
    .WithReference(db)
    .WaitFor(db)
    .WaitForCompletion(vitalsSeed)
    .WaitForCompletion(meridianSeed)
    .WithEnvironment("SimulatorIntervalSeconds", builder.Configuration.GetValue("Simulator:MeridianIntervalSeconds", 25).ToString());

// Per ADR-001, the AppHost targets exactly one Host.<Provider> project --
// swap which Projects.EventStore_Host_* type is referenced here to run
// locally against a different provider, there is no config value to flip.
var eventstore = builder.AddProject<Projects.EventStore_Host_Postgres>("eventstore")
    .WithHttpEndpoint(port: Port("EventStoreHttp", 5000))
    .WithHttpsEndpoint(port: Port("EventStoreHttps", 5001))
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
    .WithEnvironment("Authentication__RequireHttpsMetadata", "false")
    // Direct request -- EventStore.SpecGeneration.SpecGenerationEndpoints
    // maps these four routes on "eventstore" itself (verified live against
    // the running resource, not assumed): /scalar/v1 (interactive OpenAPI
    // UI, ADR-025), /openapi.json and /asyncapi.json (raw specs), and
    // /asyncapi-ui (the AsyncAPI HTML viewer, that same ADR). Anchored to
    // the "https" endpoint via WithUrlForEndpoint rather than a hardcoded
    // WithUrl string, so the dashboard link always resolves against
    // whatever host/port this resource ACTUALLY bound this run, the same
    // way GetEndpoint("https") above never hardcodes a URL either.
    //
    // The callback returns a NEW ResourceUrlAnnotation each time (not a
    // mutated existing one) -- verified against dotnet/aspire PR #8743
    // ("Custom URLs improvements") before writing this: that's the specific
    // overload (Func<EndpointReference, ResourceUrlAnnotation>) that ADDS
    // another distinct link per call: a same-shaped callback that instead
    // mutates and returns the endpoint's own existing annotation only
    // REPLACES the endpoint's single primary URL on each call, which would
    // have left only the last of these four visible.
    .WithUrlForEndpoint("https", _ => new ResourceUrlAnnotation { Url = "/scalar/v1", DisplayText = "OpenAPI (Scalar)" })
    .WithUrlForEndpoint("https", _ => new ResourceUrlAnnotation { Url = "/openapi.json", DisplayText = "OpenAPI JSON" })
    .WithUrlForEndpoint("https", _ => new ResourceUrlAnnotation { Url = "/asyncapi-ui", DisplayText = "AsyncAPI UI" })
    .WithUrlForEndpoint("https", _ => new ResourceUrlAnnotation { Url = "/asyncapi.json", DisplayText = "AsyncAPI JSON" })
    // ADR-101 -- always SQLite regardless of eventstore's own write-side
    // provider (docs/09-cqrs-read-models.md's own "one EF Core provider is
    // sufficient" posture, already established for OrdersProjectionsDbContext).
    // A relative path, not absolute: eventstore/vitals-flows/meridian-flows
    // all run from their own project directory directly under src/ (Aspire's
    // own default working directory for a project resource, matching plain
    // `dotnet run --project`), so "../pending-tasks.db" resolves to the
    // SAME literal src/pending-tasks.db file for all three -- required for
    // one cross-domain myTasks query to see both domains' tasks at all.
    .WithEnvironment("ConnectionStrings__PendingTasks", "Data Source=../pending-tasks.db");

// ADR-101 -- the flow engine's own worker host: one ProjectionHost<PendingTask>
// per registered flow (VitalsWorkflowB/D), a Follow consumer of eventstore's
// HTTP API like any other (ProjectionsScenarioAssertions.cs's own header
// comment: "ProjectionHost's only reachable dependency on the write side is
// HTTP"). WaitForCompletion(vitalsSeed) isn't strictly required --
// ProjectionHost's own reconnect-on-404 loop already tolerates a
// not-yet-registered event type -- it just avoids a burst of expected
// startup reconnect noise before VitalsWorkflowB/D's schemas exist.
var vitalsFlows = builder.AddProject<Projects.Samples_Vitals_Flows>("vitals-flows")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WaitForCompletion(vitalsSeed)
    .WithEnvironment("Follow__BaseAddress", eventstore.GetEndpoint("https"))
    .WithEnvironment("DevIdp__BaseAddress", devIdp.GetEndpoint("http"))
    .WithEnvironment("ConnectionStrings__PendingTasks", "Data Source=../pending-tasks.db");

var meridianFlows = builder.AddProject<Projects.Samples_Meridian_Flows>("meridian-flows")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WaitForCompletion(meridianSeed)
    .WithEnvironment("Follow__BaseAddress", eventstore.GetEndpoint("https"))
    .WithEnvironment("DevIdp__BaseAddress", devIdp.GetEndpoint("http"))
    .WithEnvironment("ConnectionStrings__PendingTasks", "Data Source=../pending-tasks.db");

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
    .WithHttpEndpoint(port: Port("ClientWeb", 5173)) // Vite's own conventional default dev port
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
    .WithHttpEndpoint(port: Port("ClientWebVitals", 5174))
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
    .WithHttpEndpoint(port: Port("ClientWebMeridian", 5175))
    .WithExternalHttpEndpoints();

// Workflow C's periodic-screening half (Samples.Meridian.Seed already
// publishes SanctionsScreeningPerformed for applicant-1001, folded onto
// the same ApplicantIdentity entity client-web-meridian above watches --
// but that instance's own subscription is fixed to IdentityClaimSubmitted,
// so ScreeningDate/MatchFound/etc. are unreachable from it, same
// one-event-type-per-instance reasoning as the Vitals instances above).
// The SAR-escalation half (SarFilingRecorded) needs a real RFC 9470
// step-up authentication flow the seeder doesn't perform -- still a
// genuinely open gap (TODO.md), not addressed by this instance.
var clientWebMeridianScreening = builder.AddViteApp("client-web-meridian-screening", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http"))
    .WithEnvironment("VITE_APP_ID", "kyc")
    .WithEnvironment("VITE_ENTITY_TYPE", "applicantidentity")
    .WithEnvironment("VITE_EVENT_TYPE", "SanctionsScreeningPerformed")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "applicantId")
    .WithHttpEndpoint(port: Port("ClientWebMeridianScreening", 5179))
    .WithExternalHttpEndpoints();

// Workflow C's SAR-escalation half -- Samples.Meridian.Seed now performs
// the real step-up-authenticated SarFilingRecorded publish (a compliance
// officer's ClaimsPrincipal carrying "acr"/"auth_time" directly, the same
// mechanism MeridianWorkflowCScenarioAssertions.cs's own passing test
// already proves, satisfied without a real DevIdp round trip since this
// seeder talks to PublishService in-process). This instance is what makes
// the resulting SarFilingRecorded event Browse-reachable at all.
var clientWebMeridianSarFiling = builder.AddViteApp("client-web-meridian-sarfiling", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http"))
    .WithEnvironment("VITE_APP_ID", "kyc")
    .WithEnvironment("VITE_ENTITY_TYPE", "applicantidentity")
    .WithEnvironment("VITE_EVENT_TYPE", "SarFilingRecorded")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "applicantId")
    .WithHttpEndpoint(port: Port("ClientWebMeridianSarFiling", 5180))
    .WithExternalHttpEndpoints();

// Two more Vitals instances, same shape as clientWebVitals above --
// ADR-039's one-event-type-per-instance model means Workflow B's Device
// entities and Workflow D's IonmAlert entities need their own dedicated
// subscriptions to ever be Browse-reachable at all (confirmed by reading
// subscriptionBuilder.ts: a GraphQL Subscription field is built per
// (AppId, EventType), never per EntityType -- TODO.md's own tracked gap
// before this pair existed). DeviceOnboarded/IonmAlertRaised both declare
// RequiredClaims: null (Samples.Vitals/VitalsWorkflowB.cs/VitalsWorkflowD.cs),
// so no additional claim beyond the DevIdp login every other instance
// already gets is needed to browse either.
var clientWebVitalsDevice = builder.AddViteApp("client-web-vitals-device", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http"))
    .WithEnvironment("VITE_APP_ID", "trial1")
    .WithEnvironment("VITE_ENTITY_TYPE", "device")
    .WithEnvironment("VITE_EVENT_TYPE", "DeviceOnboarded")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "deviceId")
    .WithHttpEndpoint(port: Port("ClientWebVitalsDevice", 5176))
    .WithExternalHttpEndpoints();

var clientWebVitalsIonmAlert = builder.AddViteApp("client-web-vitals-ionmalert", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http"))
    .WithEnvironment("VITE_APP_ID", "trial1")
    .WithEnvironment("VITE_ENTITY_TYPE", "ionmalert")
    .WithEnvironment("VITE_EVENT_TYPE", "IonmAlertRaised")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "alertId")
    .WithHttpEndpoint(port: Port("ClientWebVitalsIonmAlert", 5177))
    .WithExternalHttpEndpoints();

// Workflow B's downstream half (Adverse Event Capture and Review) --
// same reasoning as the Device/IonmAlert pair above, now that
// Samples.Vitals.Seed actually publishes an AdverseEventReported event
// (TODO.md's own tracked gap: no AdverseEvent entity existed to browse
// at all until this pass).
var clientWebVitalsAdverseEvent = builder.AddViteApp("client-web-vitals-adverseevent", "../../client-web")
    .WithReference(eventstore)
    .WaitFor(eventstore)
    .WithReference(devIdp)
    .WithEnvironment("VITE_HOST_BASE_URL", eventstore.GetEndpoint("https"))
    .WithEnvironment("VITE_AUTH_BASE_URL", devIdp.GetEndpoint("http"))
    .WithEnvironment("VITE_APP_ID", "trial1")
    .WithEnvironment("VITE_ENTITY_TYPE", "adverseevent")
    .WithEnvironment("VITE_EVENT_TYPE", "AdverseEventReported")
    .WithEnvironment("VITE_ENTITY_ID_FIELD", "aeId")
    .WithHttpEndpoint(port: Port("ClientWebVitalsAdverseEvent", 5178))
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
    .WithEnvironment("Cors__AllowedOrigins__2", clientWebMeridian.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__3", clientWebVitalsDevice.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__4", clientWebVitalsIonmAlert.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__5", clientWebVitalsAdverseEvent.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__6", clientWebMeridianScreening.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__7", clientWebMeridianSarFiling.GetEndpoint("http"));
eventstore.WithEnvironment("Cors__AllowedOrigins__0", clientWeb.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__1", clientWebVitals.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__2", clientWebMeridian.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__3", clientWebVitalsDevice.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__4", clientWebVitalsIonmAlert.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__5", clientWebVitalsAdverseEvent.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__6", clientWebMeridianScreening.GetEndpoint("http"))
    .WithEnvironment("Cors__AllowedOrigins__7", clientWebMeridianSarFiling.GetEndpoint("http"));

// ADR-067's RbacProjectionWorker (EventStore.DevIdp) -- registered
// unconditionally (Program.cs) but a permanent no-op whenever Rbac:AppIds
// is empty, which it always was here: this AppHost never configured it at
// all until now. Found only by actually driving a real UcanDelegation
// through the live app's own trust-root registration (client-web's new
// Relying-Party Access panel, TODO.md) -- every prior exercise of this
// mechanism was an isolated WebApplicationFactory test that either
// applied TrustRootService's own fold method directly
// (DelegatedGrantsRbacFederationHttpSqliteTests.cs) or drove
// RbacProjectionWorker.CatchUpOnceAsync explicitly
// (RbacProjectionWorkerHttpSqliteTests.cs), never the real
// BackgroundService wired into a genuinely running AppHost -- so this gap
// had no way to surface before. Config keys/values match that second
// test's own already-proven configuration exactly (RbacProjectionOptions/
// FollowClientOptions, EventStore.DevIdp/Program.cs).
devIdp.WithEnvironment("Rbac__AppIds__0", "trial1")
    .WithEnvironment("Rbac__AppIds__1", "kyc")
    .WithEnvironment("Rbac__HostBaseUrl", eventstore.GetEndpoint("https"))
    .WithEnvironment("Rbac__DevIdpBaseAddress", devIdp.GetEndpoint("http"))
    .WithEnvironment("Rbac__Client__ClientId", "devidp-rbac-follower-client")
    .WithEnvironment("Rbac__Client__ClientSecret", "devidp-rbac-follower-client-secret")
    .WithEnvironment("Rbac__Client__Scope", "events:follow");

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
clientWebVitalsDevice.WithParentRelationship(vitalsSeed);
clientWebVitalsIonmAlert.WithParentRelationship(vitalsSeed);
clientWebVitalsAdverseEvent.WithParentRelationship(vitalsSeed);
clientWebMeridian.WithParentRelationship(meridianSeed);
clientWebMeridianScreening.WithParentRelationship(meridianSeed);
clientWebMeridianSarFiling.WithParentRelationship(meridianSeed);
vitalsSimulator.WithParentRelationship(vitalsSeed);
meridianSimulator.WithParentRelationship(meridianSeed);
vitalsFlows.WithParentRelationship(vitalsSeed);
meridianFlows.WithParentRelationship(meridianSeed);

builder.Build().Run();
