using System.Security.Claims;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.SchemaRegistry;
using EventStore.Upcasting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Samples.Meridian;

// Same shape as Samples.Vitals.Simulator -- see that project's own
// Program.cs for the full reasoning. Periodically publishes a brand-new
// IdentityClaimSubmitted event for a fresh, never-before-used ApplicantId
// -- deliberately this type, not IdentityDocumentUploaded/
// BiometricCaptureRecorded (both also RequiredClaims: null and equally
// easy to simulate): client-web-meridian's own VITE_EVENT_TYPE
// (EventStore.AppHost/AppHost.cs) watches IdentityClaimSubmitted
// specifically, and a client instance's live subscription only ever
// shows entities carrying the ONE event type it's configured for --
// publishing a different type here would leave its Browse tab (and Entity
// Browser generally) permanently empty regardless of how much simulator
// activity exists, found live by actually checking the running app, not
// assumed from the schema alone.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required -- expected to be injected by Aspire's WithReference(db).");
var intervalSeconds = builder.Configuration.GetValue("SimulatorIntervalSeconds", 25);

var dbOptions = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
    .Options;

await using var db = new EventStoreContext(dbOptions, new PostgresJsonPathTranslator());

var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), new CelUpcastExpressionEvaluator());
var publisher = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
var seedUser = new ClaimsPrincipal(new ClaimsIdentity([], "simulator"));

var startingOffset = await db.Events
    .Where(e => e.AppId == MeridianWorkflowA.AppId && e.EventType == "identityclaimsubmitted")
    .CountAsync();

Console.WriteLine($"Samples.Meridian.Simulator starting -- interval {intervalSeconds}s, first applicant index {startingOffset}.");

var i = startingOffset;
while (true)
{
    i++;
    var applicantId = $"applicant-sim-{i:D5}";
    var eventId = Guid.NewGuid();
    var payload = $$"""{"ApplicantId":"{{applicantId}}","Did":"did:key:z6MkSimulated{{i:D5}}","ClaimedLegalName":"Sim Applicant {{i}}","DateOfBirth":"1990-01-01","DocumentType":"Passport"}""";

    // Samples.Vitals.Simulator runs concurrently, forever, against the same
    // Postgres -- see that project's own Program.cs for the full reasoning
    // on why this retry exists (EventAppender.cs's Serializable transaction
    // conflicts across AppIds at the whole-table level; two infinite loops
    // can't be ordered the way the one-shot Seed workers were).
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var result = await publisher.PublishAsync("IdentityClaimSubmitted", new PublishEventRequest(MeridianWorkflowA.AppId, 1, payload, null, eventId), seedUser);
            Console.WriteLine(result is PublishResult.Accepted accepted
                ? $"Published IdentityClaimSubmitted for {applicantId} -> {accepted.EntityId}"
                : $"Publish for {applicantId} did not succeed: {result}");
            break;
        }
        catch (Exception ex) when (attempt < 5)
        {
            Console.WriteLine($"Publish for {applicantId} threw ({ex.GetType().Name}: {ex.Message}) -- retrying (attempt {attempt}).");
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
        }
    }

    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
}
