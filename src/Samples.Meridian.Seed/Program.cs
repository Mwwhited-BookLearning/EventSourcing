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
using Samples.Meridian;

// Same one-shot, direct-DB shape as Samples.Vitals.Seed -- see that
// project's own Program.cs for the full reasoning (mirrors EventStore.
// Migrator's ADR-076 posture, gated after it via Aspire's
// WaitForCompletion). Registers MeridianWorkflowA/C's event types, one
// Detail ViewDefinition for the continuity "ApplicantIdentity" entity, then
// publishes the continuity applicant (applicant-1001) both workflows' own
// feature docs under docs/domains/digital-identity-kyc already name.
//
// SarFilingRecorded (WorkflowC) is deliberately never published here --
// it carries a RequiredSignature step-up requirement (RFC 9470), which
// this simple seeder has no ACR/step-up authentication to satisfy; every
// OTHER Meridian event type this pass registers needs no such thing.
//
// No PayloadEncryptor wired -- unlike Vitals' PatientScreened, none of
// this domain's own x-masking fields (ExtractedDocumentNumber,
// ClaimedLegalName, DateOfBirth) carry a regulatoryClassification, so
// EventStore.Masking.PayloadMasker never attempts crypto-shredded
// decryption for them at all regardless -- there is nothing this worker
// would even need to encrypt.
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

await MeridianWorkflowA.RegisterAsync(registry);
await MeridianWorkflowC.RegisterAsync(registry);

var viewResult = await views.RegisterAsync(new RegisterViewDefinitionRequest(
    "ApplicantIdentity", "Detail", [1],
    """
    <section class="applicant-detail">
      <h2>{{ t:applicant_detail_title }}</h2>
      <dl>
        <dt>{{ t:applicant_id }}</dt><dd>{{ applicantId }}</dd>
        <dt>{{ t:document_type }}</dt><dd>{{ documentType }}</dd>
        <dt>{{ t:claimed_legal_name }}</dt><dd>{{ claimedLegalName }}</dd>
        <dt>{{ t:date_of_birth }}</dt><dd>{{ dateOfBirth:date }}</dd>
        <dt>{{ t:did }}</dt><dd>{{ did }}</dd>
      </dl>
    </section>
    """));
if (viewResult is RegisterViewDefinitionResult.ValidationFailed failed)
    throw new InvalidOperationException($"ApplicantIdentity ViewDefinition registration failed: {string.Join("; ", failed.Errors)}");

var seedUser = new ClaimsPrincipal(new ClaimsIdentity([], "seed")); // no Meridian demo publish here needs a claim -- see file header

const string ApplicantId = "applicant-1001";

await PublishSeedEventAsync("IdentityDocumentUploaded", MeridianWorkflowA.AppId,
    Guid.Parse("b0000001-0000-0000-0000-000000001001"),
    $$"""{"ApplicantId":"{{ApplicantId}}","DocumentType":"Passport","ExtractedDocumentNumber":"P1234567"}""");

await PublishSeedEventAsync("BiometricCaptureRecorded", MeridianWorkflowA.AppId,
    Guid.Parse("b0000002-0000-0000-0000-000000001001"),
    $$"""{"ApplicantId":"{{ApplicantId}}","CaptureType":"Face","LivenessCheckResult":"Pass","LivenessConfidence":0.97}""");

await PublishSeedEventAsync("IdentityClaimSubmitted", MeridianWorkflowA.AppId,
    Guid.Parse("b0000003-0000-0000-0000-000000001001"),
    $$"""{"ApplicantId":"{{ApplicantId}}","Did":"did:key:z6MkContinuitySample","ClaimedLegalName":"John Doe","DateOfBirth":"1990-03-01","DocumentType":"Passport"}""");

await PublishSeedEventAsync("SanctionsScreeningPerformed", MeridianWorkflowA.AppId,
    Guid.Parse("b0000004-0000-0000-0000-000000001001"),
    $$"""{"ApplicantId":"{{ApplicantId}}","ScreeningDate":"2026-02-01","ListsChecked":["OFAC","EU-CFSP"],"MatchFound":false}""");

Console.WriteLine("Samples.Meridian.Seed complete.");

async Task PublishSeedEventAsync(string eventType, string appId, Guid eventId, string payload)
{
    var result = await publisher.PublishAsync(eventType, new PublishEventRequest(appId, 1, payload, null, eventId), seedUser);
    if (result is not PublishResult.Accepted accepted)
        throw new InvalidOperationException($"Seeding {eventType} failed: {result}");
    Console.WriteLine($"Published {eventType} -> {accepted.EntityId} ({accepted.Status})");
}
