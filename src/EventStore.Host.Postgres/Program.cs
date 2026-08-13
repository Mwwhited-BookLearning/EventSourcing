using EventStore.Derivation;
using EventStore.Erasure;
using EventStore.ExpectedResponse;
using EventStore.FeatureFlags;
using EventStore.Follow.Api;
using EventStore.GraphQL;
using EventStore.Interchange;
using EventStore.Host.Core;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.LineageExport;
using EventStore.Timestamping;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.Persistence.Migrations.Postgres;
using EventStore.Rbac;
using EventStore.Router;
using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using EventStore.Archival;
using EventStore.Attachments;
using EventStore.Replication;
using EventStore.Streaming;
using EventStore.Upcasting;
using EventStore.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // ADR-026 -- all three OTel signals, wired identically for every service
builder.AddEventStoreCommonServices();

// ADR-077 -- opt-in: only wired when FeatureFlags:AppId is configured, so
// every existing deployment/test with no such config section is
// completely unaffected. A raw ADO.NET connection, not EventStoreContext --
// this provider runs before WebApplicationBuilder.Build() (no DI container
// yet) and only ever reads one flat table with no JSON columns.
if (builder.Configuration["FeatureFlags:AppId"] is { } featureFlagsAppId)
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
    // ConfigurationManager (WebApplicationBuilder.Configuration's own type)
    // implements IConfigurationBuilder explicitly -- Add() isn't visible
    // without the cast.
    ((IConfigurationBuilder)builder.Configuration).Add(new EventLogFeatureFlagConfigurationSource(() => new NpgsqlConnection(postgresConnectionString), featureFlagsAppId));
}

// TODO.md's own Postgres-database-creation-race finding -- same fix as
// EventStore.Migrator's own identical registration; see that file's
// comment for the full reasoning. This process starts AFTER the
// migrator already completed (AppHost.cs's own WaitForCompletion(migrator)),
// so this specific race is less likely to bite here in practice, but
// there's no reason to leave this connection any less resilient than
// the one that already needs it.
// "40001" (serialization_failure) added on top of Migrator's own "3D000"
// -- found by actually running the real AppHost under concurrent write
// load (both proving-ground Simulators plus RouterWorker), not assumed:
// EventAppender.AppendAsync/AccessLogAppender.AppendAsync's own Serializable
// isolation (their own comments: "prevents a concurrent appender's own
// insert from reading the same 'prior tail'") makes 40001 an EXPECTED,
// routine outcome under concurrency, not a rare edge case -- but it is not
// one of Npgsql's own built-in transient codes (retrying a serialization
// failure is only safe because this session's own transaction-strategy fix
// already moved every BeginTransactionAsync call into a real
// CreateExecutionStrategy().ExecuteAsync delegate that redoes the whole
// unit of work from scratch on retry, not just resends the same SQL).
// Verified against nuget/Npgsql's own docs before adding: errorCodesToAdd
// is the officially documented extension point for exactly this "a known
// error code is actually transient in my specific scenario" case, the same
// mechanism Migrator's own 3D000 already uses. Migrator itself does not
// get 40001 -- it never does concurrent Serializable inserts against the
// Events table, so there is nothing there for this code to actually guard.
//
// maxRetryCount raised 10 -> 20 (maxRetryDelay left at 2s), found
// insufficient by actually reproducing a real AppHost startup crash, not
// assumed adequate from the "40001 is now retryable" fix alone: a real
// PostgresException 40001 still propagated past 10 retries under genuine
// sustained multi-writer load (RouterWorker + both proving-ground
// Simulators concurrently appending). RetryOnFailurePostgresTests.
// SustainedConcurrentLoadFromMultipleWritersNeverExhaustsTheRetryBudget
// (16 concurrent writers, 15s) reproduces the failure at 10 retries and
// stays clean at 20, across 3 repeated runs -- this value is evidence-
// based, not a round-number guess.
builder.Services.AddDbContext<EventStoreContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres"),
    x => x
        .MigrationsAssembly("EventStore.Persistence.Migrations.Postgres")
        .EnableRetryOnFailure(maxRetryCount: 20, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: ["3D000", "40001"])));
builder.AddDbReachabilityHealthCheck(); // ADR-084 -- readiness fails when THIS database is unreachable
builder.Services.AddScoped<IJsonPathTranslator, PostgresJsonPathTranslator>();
builder.Services.AddScoped<IFilterableFieldIndexDdlGenerator, PostgresFilterableFieldIndexDdlGenerator>();
builder.Services.AddScoped<IUniqueConstraintViolationDetector, PostgresUniqueConstraintViolationDetector>();
builder.Services.AddScoped<IEventLineageQueryProvider, PostgresEventLineageQueryProvider>();
builder.Services.AddUpcasting(builder.Configuration);
builder.Services.AddSchemaRegistry();
builder.Services.AddFeatureFlags();
// ADR-095 -- Postgres LISTEN/NOTIFY, notify-to-wake/poll-to-confirm on top
// of RouterWorker's existing poll loop.
builder.Services.AddPostgresWorkerWakeSignal();
builder.Services.AddInbox();
builder.Services.AddRouter();
builder.Services.AddDerivation();
builder.Services.AddSpecGeneration();
builder.Services.AddLineageApi();
builder.Services.AddLineageExport();
builder.Services.AddTimestamping(builder.Configuration);
builder.Services.AddErasure(builder.Configuration);
builder.Services.AddMasking(
    builder.Configuration.GetSection("Masking:HmacKeys").GetChildren().ToDictionary(c => c.Key, c => c.Value!));
builder.Services.AddFollowApi();
builder.Services.AddStreaming();
builder.Services.AddAttachments();
builder.Services.AddArchival();
builder.Services.AddReplication();
builder.Services.AddWebhooks();
builder.Services.AddExpectedResponseTracking();
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));
builder.Services.AddEventStoreGraphQl();
builder.Services.Configure<OriginIdOptions>(builder.Configuration.GetSection("OriginId"));
builder.Services.Configure<PeerSyncOptions>(builder.Configuration.GetSection("PeerSync"));
builder.Services.Configure<PeerSyncClientOptions>(builder.Configuration.GetSection("PeerSyncClient"));
builder.Services.Configure<RegionOptions>(builder.Configuration.GetSection("Region"));
builder.Services.AddInterchange();
builder.Services.Configure<Hl7V2MllpOptions>(builder.Configuration.GetSection("Hl7V2Mllp"));
builder.Services.AddHttpClient("DevIdp", c => c.BaseAddress = new Uri(builder.Configuration["Authentication:Authority"]!));
builder.AddSpiffePeerIdentity(); // ADR-048 -- wires the "PeerSync" named HttpClient with this Host's own SVID; no fixed BaseAddress here either, same reason as before

var app = builder.Build();

// ADR-076 -- no replica ever calls Database.Migrate()/MigrateAsync() at
// startup (this line used to be here; two replicas starting concurrently
// against a fresh database is a known race). Schema is applied as a
// single deploy-time step BEFORE this Host starts, via an EF Core
// Migration Bundle -- see scripts/generate-migration-bundle.sh and
// scripts/apply-migration-bundle.sh for the local/POC equivalent of that
// deploy-time step (a real deployment pipeline would run the same bundle,
// sequenced ahead of every replica, per that ADR's own Consequences).

app.MapDefaultEndpoints();
app.MapEventStoreCommonEndpoints();
app.MapSchemaRegistryEndpoints();
app.MapPublishEndpoints();
app.MapRbacEndpoints();
app.MapFeatureFlagEndpoints();
app.MapDerivationEndpoints();
app.MapSpecGenerationEndpoints();
app.MapLineageEndpoints();
app.MapLineageExportEndpoints();
app.MapFollowEndpoints();
app.MapStreamingEndpoints();
app.MapAttachmentEndpoints();
app.MapPeerSyncEndpoints();
app.MapWebhookEndpoints();
app.MapGraphQlEndpoints();
app.MapInterchangeEndpoints();
app.Run();

public partial class Program;
