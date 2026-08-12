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
using Samples.Vitals;

// Long-running counterpart to Samples.Vitals.Seed -- same direct-DB shape
// (this repo's own EventStore.Migrator/Seed pattern), but never exits:
// periodically publishes a brand-new PatientScreened event for a fresh,
// never-before-used SubjectId, so a running AppHost instance shows
// continuing activity instead of the one static seed snapshot. Started
// AFTER Samples.Vitals.Seed (AppHost.cs's own WaitForCompletion), so every
// schema this publishes against is already registered -- this project
// never calls SchemaRegistryService.RegisterAsync itself.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required -- expected to be injected by Aspire's WithReference(db).");
var intervalSeconds = builder.Configuration.GetValue("SimulatorIntervalSeconds", 20);

var dbOptions = new DbContextOptionsBuilder<EventStoreContext>()
    .UseNpgsql(connectionString, x => x.MigrationsAssembly("EventStore.Persistence.Migrations.Postgres"))
    .Options;

await using var db = new EventStoreContext(dbOptions, new PostgresJsonPathTranslator());

var registry = new SchemaRegistryService(db, new PostgresFilterableFieldIndexDdlGenerator(), new MemoryCache(new MemoryCacheOptions()), new CelUpcastExpressionEvaluator());
var publisher = new PublishService(db, registry, new PostgresUniqueConstraintViolationDetector());
var seedUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim("patient", "enroll")], "simulator"));

// Durable-enough starting offset -- a restart continues roughly where the
// last run left off (never bursts a pile of re-published IDs, never
// strictly needs its own cursor table for a demo-data generator). Counts
// EXISTING simulator-minted subjects specifically (the "S-SIM-" prefix
// this simulator alone uses), so it never collides with the seed worker's
// own fixed continuity subject (S-0091) or a real operator's own
// Composer-published subjects.
var startingOffset = await db.Events
    .Where(e => e.AppId == VitalsWorkflowA.AppId && e.EventType == "patientscreened")
    .CountAsync();

Console.WriteLine($"Samples.Vitals.Simulator starting -- interval {intervalSeconds}s, first subject index {startingOffset}.");

var i = startingOffset;
while (true)
{
    i++;
    var subjectId = $"S-SIM-{i:D5}";
    var eventId = Guid.NewGuid();
    var payload = $$"""{"SubjectId":"{{subjectId}}","SiteId":"site-1","ProtocolId":"proto-1","ScreeningDate":"{{DateTimeOffset.UtcNow:yyyy-MM-dd}}","EligibilityStatus":"Eligible"}""";

    // Samples.Meridian.Simulator runs concurrently, forever, against the
    // same Postgres -- unlike the one-shot Seed workers (serialized via
    // AppHost.cs's own WaitForCompletion chain), two long-running
    // publishers can't be ordered that way. EventAppender.cs's Serializable
    // transaction (see that class's own comment) genuinely conflicts
    // across AppIds at the whole-table level, confirmed by hitting real
    // Postgres 40001 serialization_failure errors running both simulators
    // together -- retried here (same eventId, so a retry after a real
    // failure is safe, not a duplicate) rather than ordered away, since
    // ordering two infinite loops isn't possible the way it was for two
    // one-shot workers.
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var result = await publisher.PublishAsync("PatientScreened", new PublishEventRequest(VitalsWorkflowA.AppId, 1, payload, null, eventId), seedUser);
            Console.WriteLine(result is PublishResult.Accepted accepted
                ? $"Published PatientScreened for {subjectId} -> {accepted.EntityId}"
                : $"Publish for {subjectId} did not succeed: {result}");
            break;
        }
        catch (Exception ex) when (attempt < 5)
        {
            Console.WriteLine($"Publish for {subjectId} threw ({ex.GetType().Name}: {ex.Message}) -- retrying (attempt {attempt}).");
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
        }
    }

    // "Domain Decision Queues" -- the Vitals PI queue needs REAL,
    // continuously-arriving pending items to demo against, not just the
    // one static seed alert. ReviewPending: true (ADR-042) starts this
    // alert's AuthorityStatus at "pending_review" -- the actual signal
    // client-web's usePendingAuthorityQueue filters on -- rather than the
    // ordinary immediately-"accepted" default every other event here uses.
    // A distinct fixed EventId per tick (based on the same simulator-only
    // index i, never colliding with the seed worker's own "alert-0091")
    // keeps this idempotent-replay-safe across simulator restarts, the
    // same reasoning as the PatientScreened publish above.
    var alertId = $"alert-sim-{i:D5}";
    var alertEventId = Guid.NewGuid();
    var alertPayload = $$"""{"AlertId":"{{alertId}}","SubjectId":"{{subjectId}}","Finding":"SSEP amplitude decrease","Severity":"High"}""";
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var result = await publisher.PublishAsync("IonmAlertRaised",
                new PublishEventRequest(VitalsWorkflowD.AppId, 1, alertPayload, null, alertEventId, ReviewPending: true), seedUser);
            Console.WriteLine(result is PublishResult.Accepted accepted
                ? $"Published IonmAlertRaised for {alertId} -> {accepted.EntityId} (authorityStatus: {accepted.AuthorityStatus})"
                : $"Publish for {alertId} did not succeed: {result}");
            break;
        }
        catch (Exception ex) when (attempt < 5)
        {
            Console.WriteLine($"Publish for {alertId} threw ({ex.GetType().Name}: {ex.Message}) -- retrying (attempt {attempt}).");
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
        }
    }

    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
}
