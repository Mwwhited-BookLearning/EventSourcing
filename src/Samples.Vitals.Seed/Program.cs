using System.Security.Claims;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using EventStore.ViewRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Samples.Vitals;

// One-shot demo-data seeding, mirroring EventStore.Migrator's own direct-DB,
// no-ASP.NET-container shape (ADR-076's "single deploy-time step" posture,
// applied to seeding rather than schema migration) -- gated via Aspire's
// WaitForCompletion the identical way, and run strictly after the migrator
// so the schema already exists. Registers every Vitals workflow's event
// types, one Detail ViewDefinition for the continuity "Patient" entity so
// client-web has something real to render, then publishes the same
// continuity subject (S-0091) every Vitals feature doc under docs/domains/
// clinical-trials-device-telemetry already names throughout.
//
// No PayloadEncryptor wired even though PatientScreened's LegalName/
// DateOfBirth carry x-masking.regulatoryClassification -- confirmed by
// reading EventStore.Masking.PayloadMasker.RevealAsync directly: when no
// DEK has ever been created for an entity (ErasureKeyService.ResolveAsync
// returns null), a claim-holder's reveal falls back to the raw stored
// value rather than attempting to base64-decode/decrypt it, so an
// unencrypted PHI field still round-trips correctly through both the
// FixedValue-masked (no claim) and revealed (claim held) paths. Crypto-
// shredding itself is never exercised by this seed data, only ordinary
// x-masking reveal/mask is -- which is all this pass set out to prove
// working end to end.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required -- expected to be injected by Aspire's WithReference(db).");

var dbOptions = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
    .Options;

await using var db = new EventStoreContext(dbOptions, new PostgresJsonPathTranslator());

var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), new CelUpcastExpressionEvaluator());
var views = new ViewDefinitionService(db);
var publisher = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());

await VitalsWorkflowA.RegisterAsync(registry);
await VitalsWorkflowB.RegisterAsync(registry);
await VitalsWorkflowC.RegisterAsync(registry);
await VitalsWorkflowD.RegisterAsync(registry);

var viewResult = await views.RegisterAsync(new RegisterViewDefinitionRequest(
    "Patient", "Detail", [1],
    """
    <section class="patient-detail">
      <h2>{{ t:patient_detail_title }}</h2>
      <dl>
        <dt>{{ t:subject_id }}</dt><dd>{{ subjectId }}</dd>
        <dt>{{ t:site_id }}</dt><dd>{{ siteId }}</dd>
        <dt>{{ t:protocol_id }}</dt><dd>{{ protocolId }}</dd>
        <dt>{{ t:eligibility_status }}</dt><dd>{{ eligibilityStatus }}</dd>
        <dt>{{ t:legal_name }}</dt><dd>{{ legalName }}</dd>
        <dt>{{ t:date_of_birth }}</dt><dd>{{ dateOfBirth:date }}</dd>
      </dl>
    </section>
    """));
if (viewResult is RegisterViewDefinitionResult.ValidationFailed failed)
    throw new InvalidOperationException($"Patient ViewDefinition registration failed: {string.Join("; ", failed.Errors)}");

// Carries every Publish-direction claim any Vitals workflow's own
// RequiredClaims names (PatientScreened's "patient:enroll",
// InformedConsentCaptured's "consent:capture") -- DeviceOnboarded/
// IonmAlertRaised/IonmAlertAcknowledged declare RequiredClaims: null, so
// RequiredClaimEvaluator.HasAny short-circuits true for those regardless
// of what this principal holds.
var seedUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim("patient", "enroll"), new Claim("consent", "capture")], "seed"));

const string SubjectId = "S-0091";
const string AlertId = "alert-0091";

// Fixed EventIds -- ADR-011's own idempotent-replay path (a re-publish of
// the same EventId with byte-identical content Accepted-replays rather
// than inserting a duplicate), so re-running this worker across repeated
// `aspire run` cycles during local dev never grows duplicate StoredEvents
// rows for the same demo data.
await PublishSeedEventAsync("PatientScreened", VitalsWorkflowA.AppId,
    Guid.Parse("a0000001-0000-0000-0000-000000000091"),
    $$"""{"SubjectId":"{{SubjectId}}","SiteId":"site-1","ProtocolId":"proto-1","ScreeningDate":"2026-01-15","EligibilityStatus":"Eligible","LegalName":"Jane Smith","DateOfBirth":"1980-05-12"}""");

await PublishSeedEventAsync("InformedConsentCaptured", VitalsWorkflowA.AppId,
    Guid.Parse("a0000002-0000-0000-0000-000000000091"),
    $$"""{"SubjectId":"{{SubjectId}}","ConsentVersion":"v2","ConsentObtainedAt":"2026-01-15T09:00:00Z","WitnessActorId":"coordinator-1"}""");

await PublishSeedEventAsync("DeviceOnboarded", VitalsWorkflowB.AppId,
    Guid.Parse("a0000003-0000-0000-0000-000000000091"),
    $$"""{"DeviceId":"dev-0091","DeviceModel":"NIM-Eclipse","InterfaceKind":"IONM","PairedToSubjectId":"{{SubjectId}}","SiteId":"site-1"}""");

await PublishSeedEventAsync("IonmAlertRaised", VitalsWorkflowD.AppId,
    Guid.Parse("a0000004-0000-0000-0000-000000000091"),
    $$"""{"AlertId":"{{AlertId}}","SubjectId":"{{SubjectId}}","Finding":"SSEP amplitude decrease","Severity":"High"}""");

await PublishSeedEventAsync("IonmAlertAcknowledged", VitalsWorkflowD.AppId,
    Guid.Parse("a0000005-0000-0000-0000-000000000091"),
    $$"""{"AlertId":"{{AlertId}}","AckedBy":"neurologist-1"}""");

Console.WriteLine("Samples.Vitals.Seed complete.");

async Task PublishSeedEventAsync(string eventType, string appId, Guid eventId, string payload)
{
    var result = await publisher.PublishAsync(eventType, new PublishEventRequest(appId, 1, payload, null, eventId), seedUser);
    if (result is not PublishResult.Accepted accepted)
        throw new InvalidOperationException($"Seeding {eventType} failed: {result}");
    Console.WriteLine($"Published {eventType} -> {accepted.EntityId} ({accepted.Status})");
}
